using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Grain-scoped component that owns the timer, reminder, and postman dispatch
/// lifecycle for draining an <see cref="Outbox{T}"/>.
/// </summary>
/// <typeparam name="TOutbox">
/// The base type of items in the outbox. Postmen can handle subtypes via
/// <see cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>.
/// </typeparam>
/// <remarks>
/// Matching remains first-match-wins across registered postmen. During a post
/// run, each postman receives its matching items sequentially in the order
/// returned by <see cref="OutboxProcessorOptions{TOutbox}.PendingItems"/>. A
/// failure stops that postman's sequence so later matching items remain
/// pending until the owning grain removes or reconciles the failed item.
/// Different postmen are dispatched concurrently.
/// </remarks>
public sealed partial class OutboxProcessor<TOutbox> : IOutboxComponent
    where TOutbox : notnull
{
    private const string ReminderPrefix = "egil.orleans.messaging.outbox.";
    private static readonly AsyncLocal<OutboxProcessor<TOutbox>?> ActiveDrain = new();

    private readonly IGrainBase owner;
    private readonly IGrainFactory grainFactory;
    private readonly OutboxProcessorOptions<TOutbox> options;
    private readonly object drainGate = new();
    private readonly OutboxPostmanRegistry<TOutbox> postmen = new();
    private readonly OutboxDispatcher<TOutbox> dispatcher;
    private readonly OutboxReconciler<TOutbox> reconciler;
    private readonly string grainType;
    private readonly string reminderName;
    private IGrainTimer? dispatchTimer;
    private IGrainTimer? reconciliationTimer;
    private IGrainReminder? reminder;
    private OutboxReconciliationBatch<TOutbox>? pendingReconciliation;
    private TaskCompletionSource? activeDrain;
    private bool backgroundDrainOwnsActiveDrain;
    private bool drainRequested;

    internal OutboxProcessor(
        IGrainBase owner,
        IGrainFactory grainFactory,
        OutboxProcessorOptions<TOutbox> options,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(grainFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.ProcessingTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ProcessingTimeout must be greater than zero.");
        }

        if (options.RetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RetryDelay must be greater than zero.");
        }

        this.owner = owner;
        this.grainFactory = grainFactory;
        this.options = options;
        grainType = owner.GetType().Name;
        reminderName = ReminderPrefix + typeof(TOutbox).FullName;
        dispatcher = new OutboxDispatcher<TOutbox>(postmen, logger, grainType);
        reconciler = new OutboxReconciler<TOutbox>(
            options.AcknowledgePostedAsync,
            options.ReconcileFailedAsync);
    }

    internal string ReminderName => reminderName;

    /// <summary>
    /// Registers a postman that handles items of type <typeparamref name="TSub"/>.
    /// </summary>
    /// <remarks>
    /// Postman matching is first-match-wins, like a switch statement. Register
    /// more specific message types before base interfaces or catch-all types.
    /// Each outbox item is dispatched to at most one postman.
    /// </remarks>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add<TSub>((item, _) => postman(item));
        return this;
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add<TSub>((item, _) => new ValueTask(postman(item)));
        return this;
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add<TSub>((item, cancellationToken) => new ValueTask(postman(item, cancellationToken)));
        return this;
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        postmen.Add<TSub>((item, cancellationToken) => postman(item, grainFactory, cancellationToken));
        return this;
    }

    /// <summary>
    /// Registers a keyed <see cref="IPostman{TMessage}"/> service that handles
    /// items of type <typeparamref name="TSub"/>.
    /// </summary>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(string postmanName)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(postmanName);

        var postman = owner.GrainContext.ActivationServices
            .GetRequiredKeyedService<IPostman<TSub>>(postmanName);

        postmen.Add<TSub>(postman.PostAsync);
        return this;
    }

    /// <summary>
    /// Registers a postman that publishes each item to an Orleans stream.
    /// </summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub>(
        string streamProviderName,
        Func<TSub, StreamId> streamId)
        where TSub : TOutbox =>
        AddStreamPostman<TSub, TSub>(
            streamProviderName,
            streamId,
            static message => message);

    /// <summary>
    /// Registers a postman that projects each item and publishes the projected
    /// event to an Orleans stream.
    /// </summary>
    public OutboxProcessor<TOutbox> AddStreamPostman<TSub, TEvent>(
        string streamProviderName,
        Func<TSub, StreamId> streamId,
        Func<TSub, TEvent> project)
        where TSub : TOutbox
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentNullException.ThrowIfNull(streamId);
        ArgumentNullException.ThrowIfNull(project);

        var streamProvider = owner.GrainContext.ActivationServices
            .GetRequiredKeyedService<IStreamProvider>(streamProviderName);

        postmen.Add<TSub>((message, _) =>
        {
            var stream = streamProvider.GetStream<TEvent>(streamId(message));
            return new ValueTask(stream.OnNextAsync(project(message)));
        });
        return this;
    }

    /// <summary>
    /// Registers a postman that resolves a grain for each item and invokes it.
    /// </summary>
    public OutboxProcessor<TOutbox> AddGrainPostman<TSub, TGrain>(
        Func<TSub, IGrainFactory, TGrain> resolveGrain,
        Func<TGrain, TSub, Task> call)
        where TSub : TOutbox
        where TGrain : IGrain
    {
        ArgumentNullException.ThrowIfNull(resolveGrain);
        ArgumentNullException.ThrowIfNull(call);

        postmen.Add<TSub>((message, _) => new ValueTask(call(resolveGrain(message, grainFactory), message)));
        return this;
    }

    /// <summary>
    /// Posts one pending snapshot by dispatching each item to its matching
    /// postman. Arms timer/reminder if items remain; unregisters retry work if
    /// empty.
    /// </summary>
    public async ValueTask PostAsync(CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(ActiveDrain.Value, this))
        {
            drainRequested = true;
            return;
        }

        await WaitForTurnAsync(cancellationToken);
        try
        {
            await DrainOnceAsync(cancellationToken);
        }
        finally
        {
            CompleteDrain();
        }

        await ScheduleRequestedDrainAsync();
    }

    /// <summary>
    /// Schedules a timer-backed post run and returns after scheduling.
    /// </summary>
    public async ValueTask PostInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.PendingItems().IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
            return;
        }

        EnsureDispatchTimer(TimeSpan.Zero);
        await EnsureReminderAsync();
    }

    /// <summary>
    /// Called by the <see cref="IOutboxGrain"/> DIM when a reminder fires.
    /// No-ops for reminder names not owned by this processor.
    /// </summary>
    public ValueTask ReceiveReminderAsync(string reminderName, TickStatus status)
    {
        return string.Equals(reminderName, this.reminderName, StringComparison.Ordinal)
            ? PostInBackgroundAsync()
            : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Attaches this processor to the grain's <see cref="IGrainContext"/> as
    /// a component so the <see cref="IOutboxGrain"/> DIM can discover it.
    /// </summary>
    internal void AttachToGrain()
    {
        owner.GrainContext.SetComponent<IOutboxComponent>(this);
    }

    private async Task DispatchInBackgroundAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForTurnAsync(cancellationToken);

        var drainCompleted = false;
        try
        {
            var reconciliation = await RunAsActiveDrainAsync(
                () => ProcessPendingItemsAsync(cancellationToken));

            if (reconciliation.HasWork)
            {
                pendingReconciliation = reconciliation;
                backgroundDrainOwnsActiveDrain = true;
                EnsureReconciliationTimer(TimeSpan.Zero);
                return;
            }

            CompleteDrain();
            drainCompleted = true;
            await ScheduleRequestedDrainAsync();
        }
        catch
        {
            if (!drainCompleted)
            {
                CompleteDrain();
            }

            throw;
        }
    }

    private async Task ReconcileInBackgroundAsync(CancellationToken cancellationToken)
    {
        var reconciliation = pendingReconciliation;
        pendingReconciliation = null;

        try
        {
            await RunAsActiveDrainAsync(
                () => ReconcileAsync(reconciliation.GetValueOrDefault(), cancellationToken));
        }
        finally
        {
            if (backgroundDrainOwnsActiveDrain)
            {
                backgroundDrainOwnsActiveDrain = false;
                CompleteDrain();
            }
        }

        await ScheduleRequestedDrainAsync();
    }

    private async Task WaitForTurnAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var currentDrain = TryBeginDrainOrGetCurrent();
            if (currentDrain is null)
            {
                return;
            }

            await currentDrain.WaitAsync(cancellationToken);
        }
    }

    private async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        await RunAsActiveDrainAsync(async () =>
        {
            drainRequested = false;
            var reconciliation = await ProcessPendingItemsAsync(cancellationToken);
            await ReconcileAsync(reconciliation, cancellationToken);
        });
    }

    private async Task RunAsActiveDrainAsync(Func<Task> action)
    {
        var previous = ActiveDrain.Value;
        ActiveDrain.Value = this;
        try
        {
            await action();
        }
        finally
        {
            ActiveDrain.Value = previous;
        }
    }

    private async Task<T> RunAsActiveDrainAsync<T>(Func<Task<T>> action)
    {
        var previous = ActiveDrain.Value;
        ActiveDrain.Value = this;
        try
        {
            return await action();
        }
        finally
        {
            ActiveDrain.Value = previous;
        }
    }

    private async Task<OutboxReconciliationBatch<TOutbox>> ProcessPendingItemsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = options.PendingItems();
        MessagingTelemetry.RecordOutboxDepth(grainType, pending.IsDefault ? 0 : pending.Length);

        if (pending.IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
            return default;
        }

        var results = await dispatcher.DispatchAsync(
            pending,
            options.ProcessingTimeout,
            cancellationToken);

        return reconciler.CreateBatch(results);
    }

    private async Task ReconcileAsync(
        OutboxReconciliationBatch<TOutbox> reconciliation,
        CancellationToken cancellationToken)
    {
        await reconciler.ReconcileAsync(reconciliation, cancellationToken);
        await ReconcileRetryStateAsync();
    }

    private async Task ReconcileRetryStateAsync()
    {
        var pending = options.PendingItems();
        MessagingTelemetry.RecordOutboxDepth(grainType, pending.IsDefault ? 0 : pending.Length);

        if (pending.IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
            return;
        }

        EnsureDispatchTimer(options.RetryDelay);
        await EnsureReminderAsync();
    }

    private async Task ScheduleRequestedDrainAsync()
    {
        if (!drainRequested)
        {
            return;
        }

        drainRequested = false;
        if (options.PendingItems().IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
            return;
        }

        EnsureDispatchTimer(TimeSpan.Zero);
        await EnsureReminderAsync();
    }

    private Task? TryBeginDrainOrGetCurrent()
    {
        lock (drainGate)
        {
            if (activeDrain is not null)
            {
                return activeDrain.Task;
            }

            activeDrain = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return null;
        }
    }

    private void CompleteDrain()
    {
        TaskCompletionSource? completedDrain;
        lock (drainGate)
        {
            completedDrain = activeDrain;
            activeDrain = null;
        }

        completedDrain?.TrySetResult();
    }

    private void EnsureDispatchTimer(TimeSpan dueTime)
    {
        var timerOptions = new GrainTimerCreationOptions(dueTime, options.RetryDelay)
        {
            Interleave = options.Interleave,
            KeepAlive = options.KeepAlive
        };

        if (dispatchTimer is null)
        {
            dispatchTimer = owner.RegisterGrainTimer(
                static (processor, cancellationToken) => processor.DispatchInBackgroundAsync(cancellationToken),
                this,
                timerOptions);
            return;
        }

        dispatchTimer.Change(dueTime, options.RetryDelay);
    }

    private void EnsureReconciliationTimer(TimeSpan dueTime)
    {
        var timerOptions = new GrainTimerCreationOptions(dueTime, Timeout.InfiniteTimeSpan)
        {
            Interleave = options.InterleaveReconciliationCallbacks,
            KeepAlive = options.KeepAlive
        };

        if (reconciliationTimer is null)
        {
            reconciliationTimer = owner.RegisterGrainTimer(
                static (processor, cancellationToken) => processor.ReconcileInBackgroundAsync(cancellationToken),
                this,
                timerOptions);
            return;
        }

        reconciliationTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    private async Task EnsureReminderAsync()
    {
        var period = options.RetryDelay >= TimeSpan.FromMinutes(1)
            ? options.RetryDelay
            : TimeSpan.FromMinutes(1);

        reminder = await owner.RegisterOrUpdateReminder(reminderName, period, period);
    }

    private async Task DisableRetryAsync()
    {
        dispatchTimer?.Dispose();
        dispatchTimer = null;
        reconciliationTimer?.Dispose();
        reconciliationTimer = null;

        var activeReminder = reminder ?? await owner.GetReminder(reminderName);
        if (activeReminder is not null)
        {
            await owner.UnregisterReminder(activeReminder);
            reminder = null;
        }
    }
}

/// <summary>
/// Internal interface registered as a grain context component so the
/// <see cref="IOutboxGrain"/> DIM can forward reminder callbacks without
/// knowing the outbox generic type.
/// </summary>
internal interface IOutboxComponent
{
    /// <summary>Forwards a reminder callback to the outbox processor.</summary>
    ValueTask ReceiveReminderAsync(string reminderName, TickStatus status);
}
