using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Durable identity of an item within an outbox. Sender identity is supplied
/// by the processor only when constructing a delivery token.
/// </summary>
/// <param name="SequenceNumber">The monotonic sequence within the epoch.</param>
/// <param name="Timestamp">The original append time, preserved during retries.</param>
/// <param name="Epoch">The sequence-space epoch, preserved when an outbox drains.</param>
/// <param name="TraceParent">
/// The W3C <c>traceparent</c> of the activity that was current when the message
/// was appended, or <c>null</c> when no activity was current. Captured at append
/// time rather than at delivery time because delivery can happen on a timer, a
/// reminder, or a later activation, long after the producing activity ended.
/// </param>
[GenerateSerializer]
[Alias("egil.orleans.messaging.OutboxMessageId")]
public sealed record OutboxMessageId(
    [property: Id(0), JsonRequired] long SequenceNumber,
    [property: Id(1), JsonRequired] DateTimeOffset Timestamp,
    [property: Id(2), JsonRequired] DateTimeOffset Epoch,
    // Omitted from JSON when absent. The outbox is stored inside the grain's state
    // record, so every pending envelope is rewritten on every WriteStateAsync for
    // as long as the message stays pending; writing nulls would add bytes to each
    // of those writes for no information.
    [property: Id(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TraceParent = null)
{
    internal OutboxSequenceToken ForSender(GrainId sender) =>
        new(SequenceNumber, sender, Timestamp, Epoch, TraceParent);
}
