using System.Collections.Immutable;
using System.Diagnostics;
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
public sealed partial class OutboxProcessor<TOutbox> : IOutboxComponent
    where TOutbox : notnull
{
    private const string ReminderPrefix = "egil.orleans.messaging.outbox.";

    private readonly IGrainBase owner;
    private readonly IGrainFactory grainFactory;
    private readonly OutboxProcessorOptions<TOutbox> options;
    private readonly ILogger logger;
    private readonly List<PostmanRegistration> postmen = [];
    private readonly Dictionary<TOutbox, int> attempts = [];
    private readonly string grainType;
    private readonly string reminderName;
    private IGrainTimer? timer;
    private IGrainTimer? reconciliationTimer;
    private IGrainReminder? reminder;
    private ReconciliationBatch? pendingReconciliation;
    private bool drainActive;
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
        this.logger = logger;
        grainType = owner.GetType().Name;
        reminderName = ReminderPrefix + typeof(TOutbox).FullName;
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
        return AddPostmanCore<TSub>((item, _) => postman(item));
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostmanCore<TSub>((item, _) => new ValueTask(postman(item)));
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, CancellationToken, Task> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostmanCore<TSub>((item, cancellationToken) => new ValueTask(postman(item, cancellationToken)));
    }

    /// <inheritdoc cref="AddPostman{TSub}(Func{TSub, ValueTask})"/>
    public OutboxProcessor<TOutbox> AddPostman<TSub>(
        Func<TSub, IGrainFactory, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        ArgumentNullException.ThrowIfNull(postman);
        return AddPostmanCore<TSub>((item, cancellationToken) => postman(item, grainFactory, cancellationToken));
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

        return AddPostmanCore<TSub>(postman.PostAsync);
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

        return AddPostmanCore<TSub>((message, _) =>
        {
            var stream = streamProvider.GetStream<TEvent>(streamId(message));
            return new ValueTask(stream.OnNextAsync(project(message)));
        });
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

        return AddPostmanCore<TSub>((message, _) =>
            new ValueTask(call(resolveGrain(message, grainFactory), message)));
    }

    /// <summary>
    /// Posts all pending items by dispatching each to its matching postman.
    /// Arms timer/reminder if items remain; unregisters retry work if empty.
    /// </summary>
    public async ValueTask PostAsync(CancellationToken cancellationToken = default)
    {
        if (drainActive)
        {
            drainRequested = true;
            return;
        }

        drainActive = true;
        try
        {
            do
            {
                drainRequested = false;
                var reconciliation = await DispatchOnceAsync(cancellationToken);
                await ReconcileAsync(reconciliation, cancellationToken);
            }
            while (drainRequested);
        }
        finally
        {
            drainActive = false;
        }
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

        EnsureTimer(TimeSpan.Zero);
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

    private OutboxProcessor<TOutbox> AddPostmanCore<TSub>(
        Func<TSub, CancellationToken, ValueTask> postman) where TSub : TOutbox
    {
        postmen.Add(new PostmanRegistration(
            item => item is TSub,
            (item, cancellationToken) => postman((TSub)item, cancellationToken),
            typeof(TSub)));

        return this;
    }

    private async Task DispatchInBackgroundAsync(CancellationToken cancellationToken)
    {
        if (drainActive)
        {
            drainRequested = true;
            return;
        }

        drainActive = true;
        try
        {
            drainRequested = false;
            var reconciliation = await DispatchOnceAsync(cancellationToken);
            if (reconciliation.HasWork)
            {
                pendingReconciliation = reconciliation;
                EnsureReconciliationTimer(TimeSpan.Zero);
                return;
            }

            drainActive = false;
        }
        catch
        {
            drainActive = false;
            throw;
        }
    }

    private async Task ReconcileInBackgroundAsync(CancellationToken cancellationToken)
    {
        var reconciliation = pendingReconciliation;
        pendingReconciliation = null;

        try
        {
            await ReconcileAsync(reconciliation.GetValueOrDefault(), cancellationToken);
        }
        finally
        {
            drainActive = false;
        }

        if (drainRequested)
        {
            EnsureTimer(TimeSpan.Zero);
        }
    }

    private async Task<ReconciliationBatch> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = options.PendingItems();
        MessagingTelemetry.RecordOutboxDepth(grainType, pending.IsDefault ? 0 : pending.Length);

        if (pending.IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
            return default;
        }

        using var timeout = new CancellationTokenSource(options.ProcessingTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = Stopwatch.GetTimestamp();
        var posted = ImmutableArray.CreateBuilder<TOutbox>();
        var failed = ImmutableArray.CreateBuilder<(TOutbox Item, Exception Error, int Attempt)>();

        try
        {
            var dispatchTasks = new Task<DispatchResult>[pending.Length];
            for (var i = 0; i < pending.Length; i++)
            {
                linked.Token.ThrowIfCancellationRequested();
                dispatchTasks[i] = DispatchItemAsync(pending[i], linked.Token);
            }

            var dispatchResults = await Task.WhenAll(dispatchTasks);
            foreach (var result in dispatchResults)
            {
                if (result.Error is null)
                {
                    posted.Add(result.Item);
                }
                else
                {
                    failed.Add((result.Item, result.Error, IncrementAttempt(result.Item)));
                }
            }

            return new ReconciliationBatch(posted.ToImmutable(), failed.ToImmutable());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Outbox post run exceeded the configured timeout of {options.ProcessingTimeout}.");
        }
        finally
        {
            MessagingTelemetry.RecordOutboxPostDuration(
                grainType,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task ReconcileAsync(
        ReconciliationBatch reconciliation,
        CancellationToken cancellationToken)
    {
        if (!reconciliation.HasWork)
        {
            return;
        }

        if (!reconciliation.Posted.IsDefaultOrEmpty)
        {
            await options.AcknowledgePostedAsync(reconciliation.Posted, cancellationToken);
            foreach (var item in reconciliation.Posted)
            {
                attempts.Remove(item);
            }
        }

        if (!reconciliation.Failed.IsDefaultOrEmpty && options.ReconcileFailedAsync is { } reconcileFailed)
        {
            await reconcileFailed(reconciliation.Failed, cancellationToken);
        }

        await ReconcileRetryStateAsync();
    }

    private async Task<DispatchResult> DispatchItemAsync(
        TOutbox item,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var postman = FindPostman(item);
        if (postman is null)
        {
            var itemType = item.GetType();
            var error = new NoPostmanRegisteredException(itemType);
            logger.LogWarning(
                error,
                "No outbox postman registered for item type {OutboxItemType} on grain {GrainType}.",
                itemType.FullName,
                grainType);
            MessagingTelemetry.RecordOutboxPostError(grainType, itemType.Name, error);
            return new DispatchResult(item, error);
        }

        try
        {
            await InvokePostmanAsync(postman, item, cancellationToken);
            MessagingTelemetry.RecordOutboxPostItem(
                grainType,
                postman.ItemType.Name,
                success: true,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new DispatchResult(item);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var itemType = item.GetType();
            logger.LogError(
                ex,
                "Outbox postman failed for item type {OutboxItemType} on grain {GrainType}.",
                itemType.FullName,
                grainType);
            MessagingTelemetry.RecordOutboxPostError(grainType, itemType.Name, ex);
            MessagingTelemetry.RecordOutboxPostItem(
                grainType,
                postman.ItemType.Name,
                success: false,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new DispatchResult(item, ex);
        }
    }

    private ValueTask InvokePostmanAsync(
        PostmanRegistration postman,
        TOutbox item,
        CancellationToken cancellationToken) =>
        postman.Invoke(item, cancellationToken);

    private PostmanRegistration? FindPostman(TOutbox item)
    {
        foreach (var postman in postmen)
        {
            if (postman.ItemFilter(item))
            {
                return postman;
            }
        }

        return null;
    }

    private int IncrementAttempt(TOutbox item)
    {
        attempts.TryGetValue(item, out var current);
        var next = current + 1;
        attempts[item] = next;
        return next;
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

        EnsureTimer(options.RetryDelay);
        await EnsureReminderAsync();
    }

    private void EnsureTimer(TimeSpan dueTime)
    {
        var options = new GrainTimerCreationOptions(dueTime, this.options.RetryDelay)
        {
            Interleave = this.options.Interleave,
            KeepAlive = this.options.KeepAlive
        };

        if (timer is null)
        {
            timer = owner.RegisterGrainTimer(
                static (processor, cancellationToken) => processor.DispatchInBackgroundAsync(cancellationToken),
                this,
                options);
            return;
        }

        timer.Change(dueTime, this.options.RetryDelay);
    }

    private void EnsureReconciliationTimer(TimeSpan dueTime)
    {
        var options = new GrainTimerCreationOptions(dueTime, Timeout.InfiniteTimeSpan)
        {
            Interleave = this.options.InterleaveReconciliationCallbacks,
            KeepAlive = this.options.KeepAlive
        };

        if (reconciliationTimer is null)
        {
            reconciliationTimer = owner.RegisterGrainTimer(
                static (processor, cancellationToken) => processor.ReconcileInBackgroundAsync(cancellationToken),
                this,
                options);
            return;
        }

        reconciliationTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    private async Task EnsureReminderAsync()
    {
        var period = Max(options.RetryDelay, TimeSpan.FromMinutes(1));
        reminder = await owner.RegisterOrUpdateReminder(reminderName, period, period);
    }

    private async Task DisableRetryAsync()
    {
        timer?.Dispose();
        timer = null;
        reconciliationTimer?.Dispose();
        reconciliationTimer = null;

        var activeReminder = reminder ?? await owner.GetReminder(reminderName);
        if (activeReminder is not null)
        {
            await owner.UnregisterReminder(activeReminder);
            reminder = null;
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private readonly record struct ReconciliationBatch(
        ImmutableArray<TOutbox> Posted,
        ImmutableArray<(TOutbox Item, Exception Error, int Attempt)> Failed)
    {
        public bool HasWork => !Posted.IsDefaultOrEmpty || !Failed.IsDefaultOrEmpty;
    }

    private sealed record PostmanRegistration(
        Func<TOutbox, bool> ItemFilter,
        Func<TOutbox, CancellationToken, ValueTask> Postman,
        Type ItemType)
    {
        public ValueTask Invoke(TOutbox item, CancellationToken cancellationToken) =>
            Postman(item, cancellationToken);
    }

    private readonly record struct DispatchResult(TOutbox Item, Exception? Error = null);
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