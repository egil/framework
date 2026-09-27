using Microsoft.Extensions.Logging;

namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    async Task IOutboxComponent.OnDeactivateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (reminderRegistration is { } registration)
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
                    if (ReferenceEquals(reminderRegistration, registration))
                    {
                        reminderRegistration = null;
                    }
                }
            }

            if (!GetPendingItems().IsDefaultOrEmpty)
            {
                // A known handle or tick already guarantees a durable wakeup.
                // Otherwise upsert directly: discovering an inherited reminder
                // would add a read before the write when no reminder exists.
                if (reminder is null && !reminderTicked)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await EnsureReminderAsync().WaitAsync(cancellationToken);
                }
            }
            else if (options.ReminderPolicy == OutboxReminderPolicy.OnRetry && reminder is { } idleReminder)
            {
                await TryRemoveReminderOnDeactivateAsync(idleReminder, cancellationToken);
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

    private async Task TryRemoveReminderOnDeactivateAsync(IGrainReminder idleReminder, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await owner.UnregisterReminder(idleReminder).WaitAsync(cancellationToken);
            reminder = null;
        }
        catch (Exception exception)
        {
            // Cleanup failure leaves harmless ticks, not stranded pending work.
            // Do not report it as a failure to establish a durable wakeup.
            LogDeactivationReminderRemovalFailed(
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
        EventName = "OutboxDeactivationReminderRemovalFailed",
        Level = LogLevel.Warning,
        Message = "Could not remove the idle outbox reminder for grain {GrainId} ({GrainType}) during deactivation. " +
            "The reminder may continue firing. Reminder: {ReminderName}.")]
    private partial void LogDeactivationReminderRemovalFailed(
        Exception exception,
        GrainId grainId,
        string grainType,
        string reminderName);
}
