using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

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
/// returned by the outbox accessor passed to <c>RegisterOutboxProcessor</c>. A
/// failure stops that postman's sequence so later matching items remain pending
/// until the failed item posts on a later run or the owning grain removes it.
/// Different postmen are dispatched concurrently.
/// </remarks>
public sealed partial class OutboxProcessor<TOutbox> : IOutboxComponent
    where TOutbox : notnull
{
    private const string ReminderPrefix = "egil.orleans.messaging.outbox.";
    internal const string DuplicateRegistrationMessage =
        "Only one outbox processor can be registered per grain activation.";

    private static readonly AsyncLocal<OutboxProcessor<TOutbox>?> ActiveDrain = new();

    private readonly IGrainBase owner;
    private readonly IGrainFactory grainFactory;
    private readonly Func<Outbox<TOutbox>> outboxAccessor;
    private readonly OutboxProcessorOptions<TOutbox> options;
    private readonly object drainGate = new();
    private readonly OutboxPostmanRegistry<OutboxMessageEnvelope<TOutbox>> postmen = new();
    private readonly OutboxDispatcher<OutboxMessageEnvelope<TOutbox>> dispatcher;
    private readonly OutboxAcknowledger<OutboxMessageEnvelope<TOutbox>> acknowledger;
    private readonly ILogger logger;
    private readonly string grainType;
    private readonly string reminderName;
    private readonly OutboxPendingCounter.Contribution pendingOutbox;
    private IGrainTimer? dispatchTimer;
    private IGrainTimer? acknowledgementTimer;
    private IGrainReminder? reminder;
    private Task? reminderOperation;
    private TimeSpan? reminderPeriod;
    private bool reminderTicked;
    private OutboxAcknowledgementBatch<OutboxMessageEnvelope<TOutbox>>? pendingAcknowledgement;
    private TaskCompletionSource? activeDrain;
    private bool drainRequested;

    internal OutboxProcessor(
        IGrainBase owner,
        IGrainFactory grainFactory,
        Func<Outbox<TOutbox>> outboxAccessor,
        OutboxProcessorOptions<TOutbox> options,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(grainFactory);
        ArgumentNullException.ThrowIfNull(outboxAccessor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.owner = owner;
        this.grainFactory = grainFactory;
        this.outboxAccessor = outboxAccessor;
        this.options = options;
        this.logger = logger;
        grainType = owner.GetType().Name;
        reminderName = ReminderPrefix + typeof(TOutbox).FullName;
        dispatcher = new OutboxDispatcher<OutboxMessageEnvelope<TOutbox>>(postmen, logger, grainType, options.TimeProvider ?? TimeProvider.System,
            options.Trace,
            static item => item.Message is { } message ? message.GetType() : typeof(TOutbox),
            static item => item.Id.TraceParent,
            static item => item.Id.Timestamp);
        acknowledger = new OutboxAcknowledger<OutboxMessageEnvelope<TOutbox>>(
            options.AcknowledgePosted,
            options.AcknowledgePostedAsync,
            options.AcknowledgeFailuresAsync);
        pendingOutbox = MessagingTelemetry.TrackPendingOutbox(grainType, owner.GrainContext.Deactivated);
    }

    internal string ReminderName => reminderName;

    /// <summary>
    /// Posts one pending snapshot by dispatching each item to its matching
    /// postman. Arms retry work if items remain and stops local timers if empty.
    /// Reminder creation and retention follow
    /// <see cref="OutboxProcessorOptions.ReminderPolicy"/>.
    /// If the run itself fails — for example with a
    /// <see cref="TimeoutException"/> when
    /// <see cref="OutboxProcessorOptions.ProcessingTimeout"/> elapses,
    /// or when an acknowledgement callback throws — retry work is armed before
    /// the exception is rethrown so pending items are not stranded.
    /// </summary>
    public async ValueTask PostAsync(CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(ActiveDrain.Value, this))
        {
            drainRequested = true;
            return;
        }

        if (options.ReminderPolicy == OutboxReminderPolicy.KeepRegistered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Registration failure must reach the caller without arming an
            // in-memory retry that could dispatch before a reminder exists.
            await EnsureInitialReminderAsync();
        }

        await WaitForTurnAsync(cancellationToken);
        try
        {
            await DrainOnceAsync(cancellationToken);
        }
        catch
        {
            // A failed foreground run (timeout, cancellation, or an acknowledgement
            // callback error) skips ReconcileRetryStateAsync. Arm the retry timer
            // here so the caller need not schedule recovery after catching it.
            await TrySchedulePendingRetryAsync();
            throw;
        }
        finally
        {
            CompleteDrain();
        }

        ScheduleRequestedDrain();
    }

    /// <summary>
    /// Schedules a timer-backed post run and returns after scheduling.
    /// </summary>
    /// <remarks>
    /// With the default <see cref="OutboxReminderPolicy.OnDeactivation"/> policy, only
    /// an in-memory timer is armed here. Failed or incomplete dispatches also use
    /// that timer; a reminder is registered only during pending deactivation. With
    /// <see cref="OutboxReminderPolicy.KeepRegistered"/>, this call first establishes
    /// a durable reminder unless this activation has already registered one,
    /// even if the outbox is empty.
    /// </remarks>
    public async ValueTask PostInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.ReminderPolicy == OutboxReminderPolicy.KeepRegistered)
        {
            await EnsureInitialReminderAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (GetPendingItems().IsDefaultOrEmpty)
        {
            StopRetryTimers();
            await ReconcileIdleReminderAsync();
            return;
        }

        EnsureDispatchTimer(TimeSpan.Zero);
    }

    /// <summary>
    /// Called by the <see cref="IOutboxGrain"/> DIM when a reminder fires.
    /// No-ops for reminder names not owned by this processor.
    /// </summary>
    public ValueTask ReceiveReminderAsync(string reminderName, TickStatus status)
    {
        if (!string.Equals(reminderName, this.reminderName, StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }

        // A tick is evidence for cleanup, not a durable registration guarantee:
        // an already-queued tick can arrive after the reminder was removed.
        reminderTicked = true;
        return PostInBackgroundAsync();
    }

    /// <summary>
    /// Attaches this processor to the grain's <see cref="IGrainContext"/> as
    /// a component so the <see cref="IOutboxGrain"/> DIM can discover it.
    /// </summary>
    internal void AttachToGrain()
    {
        if (owner.GrainContext.GetComponent<IOutboxComponent>() is not null)
        {
            // The IOutboxGrain DIM has one component slot through which all
            // durable reminders enter. Replacing it would silently orphan the
            // first processor's cross-activation retry path.
            throw new InvalidOperationException(DuplicateRegistrationMessage);
        }

        if (owner.GrainContext.GetComponent<OutboxDeactivationObserver>() is null)
        {
            // Lifecycle subscriptions are only accepted before activation starts.
            // Constructor registration can install directly; later registration
            // needs the silo configurator to have installed the forwarding hook.
            if (owner.GrainContext.GrainInstance is not null)
            {
                throw new InvalidOperationException(
                    "Call ConfigureOutboxProcessor() on the silo builder before registering an outbox processor in OnActivateAsync or a grain method. Alternatively, register the processor in the grain constructor.");
            }

            OutboxDeactivationObserver.Install(owner.GrainContext);
        }

        owner.GrainContext.SetComponent<IOutboxComponent>(this);
        if (owner.GrainContext.GetComponent<OutboxDeactivationObserver>()!.HasStarted)
        {
            // Orleans runs lifecycle OnStart before the grain's OnActivateAsync.
            // A processor attached there must start its own registration; posts
            // and shutdown join the same operation before touching the reminder.
            _ = InitializeReminderAfterLifecycleStartAsync();
        }
    }

    private async Task DispatchInBackgroundAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForTurnAsync(cancellationToken);

        var drainCompleted = false;
        try
        {
            if (pendingAcknowledgement is not null)
            {
                // A dispatch callback can win the drain gate before the queued
                // acknowledgement callback. Defer instead of posting the same
                // durable snapshot again.
                EnsureAcknowledgementTimer(TimeSpan.Zero);
                CompleteDrain();
                drainCompleted = true;
                return;
            }

            var acknowledgement = await RunAsActiveDrainAsync(
                () => ProcessPendingItemsAsync(cancellationToken));

            if (acknowledgement.HasWork)
            {
                pendingAcknowledgement = acknowledgement;
                EnsureAcknowledgementTimer(TimeSpan.Zero);

                // Acknowledgement is a separate Orleans turn. Keeping the gate
                // across that boundary lets a non-reentrant foreground PostAsync
                // occupy the activation while waiting for the queued callback,
                // so neither operation can complete.
                CompleteDrain();
                drainCompleted = true;
                return;
            }

            CompleteDrain();
            drainCompleted = true;
            ScheduleRequestedDrain();
        }
        catch
        {
            if (!drainCompleted)
            {
                // A run that throws before acknowledgement skips reconciliation.
                // Arm its timer here so recovery does not require another post.
                await TrySchedulePendingRetryAsync();
                CompleteDrain();
            }

            throw;
        }
    }

    private async Task AcknowledgeInBackgroundAsync(CancellationToken cancellationToken)
    {
        await WaitForTurnAsync(cancellationToken);
        var acknowledgement = pendingAcknowledgement;

        try
        {
            await RunAsActiveDrainAsync(async () =>
            {
                await acknowledger.AcknowledgeAsync(
                    acknowledgement.GetValueOrDefault(),
                    cancellationToken);

                // Keep ownership visible while user callbacks run. An
                // interleaving empty post must not dispose this timer and
                // cancel the acknowledgement callback that is using it.
                pendingAcknowledgement = null;
                await ReconcileRetryStateAsync();
            });
        }
        catch
        {
            pendingAcknowledgement = null;

            // A failed acknowledgement callback skips ReconcileRetryStateAsync;
            // arm the retry timer while this activation remains available.
            await TrySchedulePendingRetryAsync();
            throw;
        }
        finally
        {
            CompleteDrain();
        }

        ScheduleRequestedDrain();
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

            // A foreground caller can acquire the gate between background
            // dispatch and its queued acknowledgement turn. Finish that batch
            // first so already-posted items are acknowledged before taking a
            // new durable snapshot for dispatch.
            var pending = pendingAcknowledgement;
            pendingAcknowledgement = null;
            if (pending is not null)
            {
                await AcknowledgeAsync(pending.Value, cancellationToken);
            }

            var acknowledgement = await ProcessPendingItemsAsync(cancellationToken);
            await AcknowledgeAsync(acknowledgement, cancellationToken);
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

    private ImmutableArray<OutboxMessageEnvelope<TOutbox>> GetPendingItems()
    {
        var pending = (outboxAccessor()
            ?? throw new InvalidOperationException("The outbox accessor must return a non-null outbox snapshot.")).Envelopes;
        pendingOutbox.SetPending(!pending.IsDefaultOrEmpty);
        return pending;
    }

    private async Task<OutboxAcknowledgementBatch<OutboxMessageEnvelope<TOutbox>>> ProcessPendingItemsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = GetPendingItems();

        if (pending.IsDefaultOrEmpty)
        {
            StopRetryTimers();
            await ReconcileIdleReminderAsync();
            return default;
        }

        var results = await dispatcher.DispatchAsync(
            pending,
            options.ProcessingTimeout,
            cancellationToken);

        return acknowledger.CreateBatch(results);
    }

    private async Task AcknowledgeAsync(
        OutboxAcknowledgementBatch<OutboxMessageEnvelope<TOutbox>> acknowledgement,
        CancellationToken cancellationToken)
    {
        await acknowledger.AcknowledgeAsync(acknowledgement, cancellationToken);
        await ReconcileRetryStateAsync();
    }

    private async Task ReconcileRetryStateAsync()
    {
        var pending = GetPendingItems();

        // Items the grain removed without a successful post (dead-lettered or
        // dropped in AcknowledgeFailuresAsync) would otherwise leak their attempt
        // counters for the activation lifetime.
        acknowledger.PruneAttempts(pending);

        if (pending.IsDefaultOrEmpty)
        {
            StopRetryTimers();
            await ReconcileIdleReminderAsync();
            return;
        }

        EnsureDispatchTimer(options.RetryDelay);
        if (options.ReminderPolicy == OutboxReminderPolicy.KeepRegistered)
        {
            await EnsureReminderAsync();
        }
    }

    private void ScheduleRequestedDrain()
    {
        if (!drainRequested)
        {
            return;
        }

        drainRequested = false;
        if (GetPendingItems().IsDefaultOrEmpty)
        {
            StopRetryTimers();
            return;
        }

        EnsureDispatchTimer(TimeSpan.Zero);
    }

    private async Task TrySchedulePendingRetryAsync()
    {
        try
        {
            if (GetPendingItems().IsDefaultOrEmpty)
            {
                return;
            }

            EnsureDispatchTimer(options.RetryDelay);
            if (options.ReminderPolicy == OutboxReminderPolicy.KeepRegistered)
            {
                await EnsureReminderAsync();
            }
        }
        catch (Exception ex)
        {
            // Swallow so the original post-run failure stays the surfaced error.
            logger.LogWarning(
                ex,
                "Failed to arm outbox retry after a failed post run on grain {GrainType}. Pending items remain until the next post or activation.",
                grainType);
        }
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

    private void EnsureAcknowledgementTimer(TimeSpan dueTime)
    {
        var timerOptions = new GrainTimerCreationOptions(dueTime, Timeout.InfiniteTimeSpan)
        {
            Interleave = options.InterleaveAcknowledgementCallbacks,
            KeepAlive = options.KeepAlive
        };

        if (acknowledgementTimer is null)
        {
            acknowledgementTimer = owner.RegisterGrainTimer(
                static (processor, cancellationToken) => processor.AcknowledgeInBackgroundAsync(cancellationToken),
                this,
                timerOptions);
            return;
        }

        acknowledgementTimer.Change(dueTime, Timeout.InfiniteTimeSpan);
    }

    private void StopRetryTimers()
    {
        dispatchTimer?.Dispose();
        dispatchTimer = null;

        // The durable outbox can be cleared while an already-dispatched batch
        // still awaits callbacks. Its timer owns that acknowledgement turn;
        // disposing it here would cancel the callback before it can finish.
        if (pendingAcknowledgement is null)
        {
            acknowledgementTimer?.Dispose();
            acknowledgementTimer = null;
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
    /// <summary>Establishes the fallback reminder during activation.</summary>
    Task OnActivateAsync(CancellationToken cancellationToken);

    /// <summary>Forwards a reminder callback to the outbox processor.</summary>
    ValueTask ReceiveReminderAsync(string reminderName, TickStatus status);

    /// <summary>Attempts a durable wakeup for pending work before deactivation completes.</summary>
    Task OnDeactivateAsync(CancellationToken cancellationToken);
}
