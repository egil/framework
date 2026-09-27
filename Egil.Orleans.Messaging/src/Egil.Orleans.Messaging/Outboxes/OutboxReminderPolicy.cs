namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Controls when an outbox processor establishes and removes its durable reminder.
/// </summary>
/// <remarks>
/// Both policies establish a retry reminder during orderly deactivation if pending
/// work has no locally registered retry reminder. Registration uses a direct upsert,
/// without checking for an inherited reminder. An inherited tick allows a lookup
/// only when cleanup needs a handle; a tick alone cannot guarantee a durable wakeup.
/// </remarks>
public enum OutboxReminderPolicy
{
    /// <summary>
    /// Retry using grain timers while active. Register a durable reminder only
    /// during orderly deactivation with pending work. Remove a known inherited
    /// reminder when the outbox drains. Abrupt crashes can bypass registration.
    /// </summary>
    OnDeactivation,

    /// <summary>
    /// Establish a fallback reminder during activation and retain it across batches
    /// until empty deactivation. Use <see cref="OutboxProcessorOptions.IdleReminderPeriod"/>
    /// while idle and <see cref="OutboxProcessorOptions.ActiveReminderPeriod"/>
    /// for pending-work recovery. Grain timers provide the normal retry cadence.
    /// </summary>
    /// <remarks>
    /// Constructor attachment registers through the activation lifecycle. Attachment
    /// in OnActivateAsync or later starts registration immediately; posts await it
    /// before dispatching. Configure the idle period longer than the grain's idle
    /// collection age, with a margin for collection scans and deactivation.
    /// </remarks>
    KeepRegistered,
}
