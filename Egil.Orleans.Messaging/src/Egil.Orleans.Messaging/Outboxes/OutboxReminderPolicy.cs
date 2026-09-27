namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Controls when an outbox processor establishes and removes its durable reminder.
/// </summary>
/// <remarks>
/// Both policies also attempt to establish a reminder during orderly deactivation
/// if pending work has no known reminder. This safeguard is best effort and does
/// not run after an abrupt silo crash. Neither policy looks up reminders: a
/// successful registration or matching tick establishes existence for the activation.
/// Otherwise, registration upserts with the current retry period, which can reset
/// an inherited reminder's schedule.
/// </remarks>
public enum OutboxReminderPolicy
{
    /// <summary>
    /// Register a reminder when a dispatch fails or leaves pending work, unless one
    /// is already known. Retain it across batches and remove it during deactivation
    /// only if the outbox is empty and this activation holds its registration handle.
    /// Successful initial dispatches make no reminder API calls.
    /// </summary>
    /// <remarks>
    /// Inherited reminders without a locally held handle remain registered, even
    /// when empty. Empty drains and ticks make no reminder API calls.
    /// </remarks>
    OnRetry,

    /// <summary>
    /// Establish a reminder before the first foreground or background post unless
    /// a matching tick already proves one exists, even with an empty outbox.
    /// Retain it across batches and activations.
    /// </summary>
    /// <remarks>
    /// Empty reminder ticks leave the reminder registered. Registration is awaited
    /// by the post call; merely registering the processor does not create a reminder.
    /// A crash before the first post can still leave persisted work without a wakeup.
    /// </remarks>
    KeepRegistered,
}
