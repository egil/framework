using Egil.Orleans.Messaging.State;
using Microsoft.Extensions.Logging;

namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    async Task IOutboxComponent.OnDeactivateAsync(CancellationToken cancellationToken)
    {
        int? outboxItemCount = null;
        try
        {
            if (reminderOperation is { } registration)
            {
                try
                {
                    // A post can still be waiting for storage when timers stop.
                    // Reconcile its result before creating or removing a reminder.
                    await registration.WaitAsync(cancellationToken);
                }
                catch (Exception) when (registration.IsFaulted || registration.IsCanceled)
                {
                    // The original caller observes this failure. Pending work can
                    // still need the final best-effort registration attempt below.
                    if (ReferenceEquals(reminderOperation, registration))
                    {
                        reminderOperation = null;
                    }
                }
            }

            var activationFencing = owner.GrainContext.GetComponent<StateManagerFencing>();
            var fenced = activationFencing is not null;
            var onlyConflicts = activationFencing?.OnlyConflicts ?? true;
            try
            {
                var pendingItems = GetPendingItems();
                outboxItemCount = pendingItems.IsDefaultOrEmpty ? 0 : pendingItems.Length;
            }
            catch (StateManagerFencedException exception)
            {
                fenced = true;
                onlyConflicts &= exception.FailureKind is StorageFailureKind.Conflict;
            }
            catch (Exception) when (fenced)
            {
                // Local inspection can fail independently of storage. The recorded
                // fence still determines recovery; only the diagnostic count is lost.
            }

            if (fenced && onlyConflicts)
            {
                // Leave recovery to the competing owner or a later activation.
                // A conflict does not prove that another activation is still alive.
                return;
            }

            // An empty local snapshot can precede a failed write which actually
            // persisted new messages. Fencing keeps that recovery need separate
            // from the readable count used for diagnostics.
            if (!fenced && outboxItemCount == 0)
            {
                await TryRemoveIdleReminderAsync(cancellationToken);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureReminderAsync().WaitAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // A failed or timed-out registration cannot prevent deactivation.
            // Include the identity so operators can find and explicitly post
            // this grain if the best-effort durable handoff did not complete.
            LogDeactivationReminderFailed(
                exception,
                owner.GrainContext.GrainId,
                grainType,
                reminderName,
                outboxItemCount);
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "OutboxDeactivationReminderFailed",
        Level = LogLevel.Warning,
        Message = "Could not confirm an outbox reminder for grain {GrainId} ({GrainType}) during deactivation. " +
            "Pending outbox work may require manual reactivation and an explicit post. " +
            "Reminder: {ReminderName}. Outbox item count: {OutboxItemCount}.")]
    private partial void LogDeactivationReminderFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName,
        int? outboxItemCount);

    [LoggerMessage(
        EventId = 2,
        EventName = "OutboxReminderRemovalFailed",
        Level = LogLevel.Warning,
        Message = "Could not remove the idle outbox reminder for grain {GrainId} ({GrainType}). " +
            "The reminder may continue firing. Reminder: {ReminderName}.")]
    private partial void LogReminderRemovalFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName);

    [LoggerMessage(
        EventId = 3,
        EventName = "OutboxActivationReminderFailed",
        Level = LogLevel.Warning,
        Message = "Could not establish the fallback outbox reminder for grain {GrainId} ({GrainType}) after late processor attachment. " +
            "A subsequent post or deactivation will retry registration. Reminder: {ReminderName}.")]
    private partial void LogActivationReminderFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName);

    [LoggerMessage(
        EventId = 4,
        EventName = "OutboxReminderAdjustmentFailed",
        Level = LogLevel.Warning,
        Message = "Could not adjust the established outbox reminder for grain {GrainId} ({GrainType}) to {Period}. " +
            "The fallback remains registered. Reminder: {ReminderName}.")]
    private partial void LogReminderAdjustmentFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName,
        TimeSpan period);
}
