using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Durable identity of an item within an outbox. Sender identity is supplied
/// by the processor only when constructing a delivery token.
/// </summary>
/// <param name="SequenceNumber">The monotonic sequence within the epoch.</param>
/// <param name="Timestamp">The original append time, preserved during retries.</param>
/// <param name="Epoch">The sequence-space epoch, preserved when an outbox drains.</param>
[GenerateSerializer]
[Alias("egil.orleans.messaging.OutboxMessageId")]
public sealed record OutboxMessageId(
    [property: Id(0), JsonRequired] long SequenceNumber,
    [property: Id(1), JsonRequired] DateTimeOffset Timestamp,
    [property: Id(2), JsonRequired] DateTimeOffset Epoch)
{
    internal OutboxSequenceToken ForSender(GrainId sender) =>
        new(SequenceNumber, sender, Timestamp, Epoch);
}
