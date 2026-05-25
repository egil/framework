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
    private IGrainReminder? reminder;
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
                await DrainOnceAsync(cancellationToken);
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
            ? PostAsync()
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

    private async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = options.PendingItems();
        MessagingTelemetry.RecordOutboxDepth(grainType, pending.IsDefault ? 0 : pending.Length);

        if (pending.IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
            return;
        }

        using var timeout = new CancellationTokenSource(options.ProcessingTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = Stopwatch.GetTimestamp();
        var posted = ImmutableArray.CreateBuilder<TOutbox>();
        var failed = ImmutableArray.CreateBuilder<(TOutbox Item, Exception Error, int Attempt)>();

        try
        {
            foreach (var item in pending)
            {
                linked.Token.ThrowIfCancellationRequested();
                await DispatchItemAsync(item, posted, failed, linked.Token);
            }

            if (posted.Count > 0)
            {
                await options.AcknowledgePostedAsync(posted.ToImmutable(), linked.Token);
                foreach (var item in posted)
                {
                    attempts.Remove(item);
                }
            }

            if (failed.Count > 0 && options.ReconcileFailedAsync is { } reconcileFailed)
            {
                await reconcileFailed(failed.ToImmutable(), linked.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Outbox post run exceeded the configured timeout of {options.ProcessingTimeout}.");
        }
        finally
        {
            MessagingTelemetry.RecordOutboxPostDuration(
                grainType,
                options.PostmanExecution,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        await ReconcileRetryStateAsync();
    }

    private async Task DispatchItemAsync(
        TOutbox item,
        ImmutableArray<TOutbox>.Builder posted,
        ImmutableArray<(TOutbox Item, Exception Error, int Attempt)>.Builder failed,
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
            failed.Add((item, error, IncrementAttempt(item)));
            MessagingTelemetry.RecordOutboxPostError(grainType, itemType.Name, error);
            return;
        }

        try
        {
            await InvokePostmanAsync(postman, item, cancellationToken);
            posted.Add(item);
            MessagingTelemetry.RecordOutboxPostItem(
                grainType,
                postman.ItemType.Name,
                options.PostmanExecution,
                success: true,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var itemType = item.GetType();
            logger.LogError(
                ex,
                "Outbox postman failed for item type {OutboxItemType} on grain {GrainType}.",
                itemType.FullName,
                grainType);
            failed.Add((item, ex, IncrementAttempt(item)));
            MessagingTelemetry.RecordOutboxPostError(grainType, itemType.Name, ex);
            MessagingTelemetry.RecordOutboxPostItem(
                grainType,
                postman.ItemType.Name,
                options.PostmanExecution,
                success: false,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private ValueTask InvokePostmanAsync(
        PostmanRegistration postman,
        TOutbox item,
        CancellationToken cancellationToken)
    {
        if (options.PostmanExecution is OutboxPostmanExecutionMode.GrainScheduler)
        {
            return postman.Invoke(item, cancellationToken);
        }

        return new ValueTask(Task.Run(
            async () => await postman.Invoke(item, cancellationToken),
            cancellationToken));
    }

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
                static (processor, cancellationToken) => processor.PostAsync(cancellationToken).AsTask(),
                this,
                options);
            return;
        }

        timer.Change(dueTime, this.options.RetryDelay);
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

        var activeReminder = reminder ?? await owner.GetReminder(reminderName);
        if (activeReminder is not null)
        {
            await owner.UnregisterReminder(activeReminder);
            reminder = null;
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private sealed record PostmanRegistration(
        Func<TOutbox, bool> ItemFilter,
        Func<TOutbox, CancellationToken, ValueTask> Postman,
        Type ItemType)
    {
        public ValueTask Invoke(TOutbox item, CancellationToken cancellationToken) =>
            Postman(item, cancellationToken);
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