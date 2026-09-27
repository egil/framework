using Microsoft.Extensions.Logging;

namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    async Task IOutboxComponent.OnDeactivateAsync(CancellationToken cancellationToken)
    {
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

            if (!GetPendingItems().IsDefaultOrEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureReminderAsync().WaitAsync(cancellationToken);
            }
            else
            {
                await TryRemoveIdleReminderAsync(cancellationToken);
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
                reminderName);
        }
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "OutboxDeactivationReminderFailed",
        Level = LogLevel.Warning,
        Message = "Could not confirm an outbox reminder for grain {GrainId} ({GrainType}) during deactivation. " +
            "Pending outbox work may require manual reactivation and an explicit post. Reminder: {ReminderName}.")]
    private partial void LogDeactivationReminderFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName);

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
        Message = "Could not establish the fallback outbox reminder for grain {GrainId} ({GrainType}) during activation. " +
            "A subsequent post or deactivation will retry registration. Reminder: {ReminderName}.")]
    private partial void LogActivationReminderFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName);
}
