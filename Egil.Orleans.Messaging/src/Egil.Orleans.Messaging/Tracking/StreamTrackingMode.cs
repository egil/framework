namespace Egil.Orleans.Messaging.Tracking;

/// <summary>Chooses how a receiver recognizes duplicate stream deliveries.</summary>
public enum StreamTrackingMode
{
    /// <summary>
    /// Keeps one provider position per complete stream source. Republishing an outbox
    /// item at a new position remains eligible, so handlers must be idempotent.
    /// </summary>
    StreamPosition,

    /// <summary>
    /// Keeps exact receipts for outbox-tagged deliveries, including unseen lower sequences.
    /// Untagged deliveries still use provider positions. Receipts need a retention policy.
    /// </summary>
    OutboxIdentity
}
