namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Controls when an outbox processor establishes and removes its durable reminder.
/// </summary>
/// <remarks>
/// Both policies also attempt to establish a reminder during orderly deactivation
/// if pending work has no known reminder. This safeguard is best effort and does
/// not run after an abrupt silo crash.
/// </remarks>
public enum OutboxReminderPolicy
{
    /// <summary>
    /// Register a reminder when a dispatch fails or leaves pending work, and remove
    /// it when the outbox drains. Successful initial dispatches need no reminder write.
    /// </summary>
    OnRetry,

    /// <summary>
    /// Establish a reminder before the first foreground or background post, even
    /// with an empty outbox, and retain it across batches and activations.
    /// </summary>
    /// <remarks>
    /// Empty reminder ticks leave the reminder registered. Registration is awaited
    /// by the post call; merely registering the processor does not create a reminder.
    /// A crash before the first post can still leave persisted work without a wakeup.
    /// </remarks>
    KeepRegistered,
}
