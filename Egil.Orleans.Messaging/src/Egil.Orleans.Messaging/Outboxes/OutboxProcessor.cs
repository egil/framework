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
/// returned by <see cref="OutboxProcessorOptions{TOutbox}.PendingItems"/>. A
/// failure stops that postman's sequence so later matching items remain
/// pending until the owning grain removes or reconciles the failed item.
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
    private readonly OutboxProcessorOptions<TOutbox> options;
    private readonly object drainGate = new();
    private readonly OutboxPostmanRegistry<OutboxMessageEnvelope<TOutbox>> postmen = new();
    private readonly OutboxDispatcher<OutboxMessageEnvelope<TOutbox>> dispatcher;
    private readonly OutboxReconciler<OutboxMessageEnvelope<TOutbox>> reconciler;
    private readonly ILogger logger;
    private readonly string grainType;
    private readonly string reminderName;
    private IGrainTimer? dispatchTimer;
    private IGrainTimer? reconciliationTimer;
    private IGrainReminder? reminder;
    private bool checkedForExistingReminder;
    private OutboxReconciliationBatch<OutboxMessageEnvelope<TOutbox>>? pendingReconciliation;
    private TaskCompletionSource? activeDrain;
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
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
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
        dispatcher = new OutboxDispatcher<OutboxMessageEnvelope<TOutbox>>(postmen, logger, grainType, options.TimeProvider,
            static item => item.Message is { } message ? message.GetType() : typeof(TOutbox));
        reconciler = new OutboxReconciler<OutboxMessageEnvelope<TOutbox>>(
            options.AcknowledgePostedAsync,
            options.ReconcileFailedAsync);
    }

    internal string ReminderName => reminderName;

    /// <summary>
    /// Posts one pending snapshot by dispatching each item to its matching
    /// postman. Arms timer/reminder if items remain; unregisters retry work if
    /// empty. If the run itself fails — for example with a
    /// <see cref="TimeoutException"/> when
    /// <see cref="OutboxProcessorOptions{TOutbox}.ProcessingTimeout"/> elapses,
    /// or when a reconciliation callback throws — retry work is armed before
    /// the exception is rethrown so pending items are not stranded.
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
        catch
        {
            // A failed foreground run (timeout, cancellation, or a reconciliation
            // callback error) skips ReconcileRetryStateAsync, and unlike the
            // background path no timer/reminder was armed beforehand. Arm retry
            // here — only on failure — so announced items still get delivered
            // without paying the reminder write on every successful post.
            await TrySchedulePendingRetryAsync();
            throw;
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
    /// <remarks>
    /// Only an in-memory grain timer is armed here. The durable reminder —
    /// which costs a storage write — is registered lazily, when a post run
    /// fails or leaves items pending, so successful posts incur no reminder
    /// I/O.
    /// </remarks>
    public async ValueTask PostInBackgroundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (GetPendingItems().IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
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
        if (owner.GrainContext.GetComponent<IOutboxComponent>() is not null)
        {
            // The IOutboxGrain DIM has one component slot through which all
            // durable reminders enter. Replacing it would silently orphan the
            // first processor's cross-activation retry path.
            throw new InvalidOperationException(DuplicateRegistrationMessage);
        }

        owner.GrainContext.SetComponent<IOutboxComponent>(this);
    }

    private async Task DispatchInBackgroundAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForTurnAsync(cancellationToken);

        var drainCompleted = false;
        try
        {
            if (pendingReconciliation is not null)
            {
                // A dispatch callback can win the drain gate before the queued
                // reconciliation callback. Defer instead of posting the same
                // durable snapshot again.
                EnsureReconciliationTimer(TimeSpan.Zero);
                CompleteDrain();
                drainCompleted = true;
                return;
            }

            var reconciliation = await RunAsActiveDrainAsync(
                () => ProcessPendingItemsAsync(cancellationToken));

            if (reconciliation.HasWork)
            {
                pendingReconciliation = reconciliation;
                EnsureReconciliationTimer(TimeSpan.Zero);

                // Reconciliation is a separate Orleans turn. Keeping the gate
                // across that boundary lets a non-reentrant foreground PostAsync
                // occupy the activation while waiting for the queued callback,
                // so neither operation can complete.
                CompleteDrain();
                drainCompleted = true;
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
                // The reminder is armed lazily, so a run that throws before
                // reconciliation must arm retry itself or pending items would
                // only survive in this activation's timer.
                await TrySchedulePendingRetryAsync();
                CompleteDrain();
            }

            throw;
        }
    }

    private async Task ReconcileInBackgroundAsync(CancellationToken cancellationToken)
    {
        await WaitForTurnAsync(cancellationToken);
        var reconciliation = pendingReconciliation;

        try
        {
            await RunAsActiveDrainAsync(async () =>
            {
                await reconciler.ReconcileAsync(
                    reconciliation.GetValueOrDefault(),
                    cancellationToken);

                // Keep ownership visible while user callbacks run. An
                // interleaving empty post must not dispose this timer and
                // cancel the reconciliation callback that is using it.
                pendingReconciliation = null;
                await ReconcileRetryStateAsync();
            });
        }
        catch
        {
            pendingReconciliation = null;

            // A failed acknowledgement/reconciliation callback skips
            // ReconcileRetryStateAsync; arm retry so pending items are not
            // stranded if the activation goes away.
            await TrySchedulePendingRetryAsync();
            throw;
        }
        finally
        {
            CompleteDrain();
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

            // A foreground caller can acquire the gate between background
            // dispatch and its queued reconciliation turn. Finish that batch
            // first so already-posted items are acknowledged before taking a
            // new durable snapshot for dispatch.
            var pending = pendingReconciliation;
            pendingReconciliation = null;
            if (pending is not null)
            {
                await ReconcileAsync(pending.Value, cancellationToken);
            }

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

    private ImmutableArray<OutboxMessageEnvelope<TOutbox>> GetPendingItems() =>
        (options.PendingItems() ?? throw new InvalidOperationException("PendingItems must return a non-null outbox snapshot.")).Envelopes;

    private async Task<OutboxReconciliationBatch<OutboxMessageEnvelope<TOutbox>>> ProcessPendingItemsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = GetPendingItems();
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
        OutboxReconciliationBatch<OutboxMessageEnvelope<TOutbox>> reconciliation,
        CancellationToken cancellationToken)
    {
        await reconciler.ReconcileAsync(reconciliation, cancellationToken);
        await ReconcileRetryStateAsync();
    }

    private async Task ReconcileRetryStateAsync()
    {
        var pending = GetPendingItems();
        MessagingTelemetry.RecordOutboxDepth(grainType, pending.IsDefault ? 0 : pending.Length);

        // Items the grain removed without a successful post (dead-lettered or
        // dropped in ReconcileFailedAsync) would otherwise leak their attempt
        // counters for the activation lifetime.
        reconciler.PruneAttempts(pending);

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
        if (GetPendingItems().IsDefaultOrEmpty)
        {
            await DisableRetryAsync();
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
            await EnsureReminderAsync();
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

        // The durable outbox can be cleared while an already-dispatched batch
        // still awaits callbacks. Its timer owns that reconciliation turn;
        // disposing it here would cancel the callback before it can finish.
        if (pendingReconciliation is null)
        {
            reconciliationTimer?.Dispose();
            reconciliationTimer = null;
        }

        // Look up a leftover reminder from a previous activation at most once;
        // afterwards the local field is authoritative, so an empty outbox does
        // not pay a reminder-table read on every successful drain.
        if (reminder is null && !checkedForExistingReminder)
        {
            // Retain an inherited handle before attempting removal. If removal
            // fails, the next empty drain must retry it without another lookup.
            reminder = await owner.GetReminder(reminderName);
            checkedForExistingReminder = true;
        }

        if (reminder is not null)
        {
            await owner.UnregisterReminder(reminder);
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
