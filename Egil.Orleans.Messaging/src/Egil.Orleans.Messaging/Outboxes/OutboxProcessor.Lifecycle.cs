using Microsoft.Extensions.Logging;

namespace Egil.Orleans.Messaging.Outboxes;

public sealed partial class OutboxProcessor<TOutbox>
{
    async Task IOutboxComponent.OnDeactivateAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (reminder is not null || GetPendingItems().IsDefaultOrEmpty)
            {
                return;
            }

            // Orleans has stopped the activation's timers. Reuse any inherited
            // reminder without resetting its cadence, or establish one last
            // durable wakeup while the grain context can still make calls.
            cancellationToken.ThrowIfCancellationRequested();
            await FindOrRegisterReminderAsync().WaitAsync(cancellationToken);
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
}
