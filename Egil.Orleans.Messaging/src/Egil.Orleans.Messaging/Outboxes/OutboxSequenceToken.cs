using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Tracking;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// Identifies a specific message within an <see cref="Outbox{T}"/>. Combines a
/// monotonic sequence number with the sender's identity and timing information
/// to form a globally unique, totally ordered token per sender.
/// </summary>
/// <remarks>
/// <para>
/// <b>Identity:</b> The token is the identity of an outbox message. Receivers
/// use <see cref="SequenceNumber"/> and <see cref="Epoch"/> to detect duplicates
/// via <see cref="MessageTracker"/>.
/// </para>
/// <para>
/// <b>Epoch semantics:</b> <see cref="Epoch"/> changes only when the sender
/// calls <see cref="Outbox{T}.Create"/> (nuclear reset). When a receiver sees
/// a token with <c>Epoch &gt; stored.Epoch</c>, it accepts unconditionally,
/// knowing the sender intentionally reset its sequence space. Same-epoch
/// tokens are compared by <see cref="SequenceNumber"/> — higher wins, equal
/// or lower is a duplicate.
/// </para>
/// <para>
/// <b>Ordering:</b> Within a single sender, tokens are totally ordered by
/// <c>(Epoch, SequenceNumber)</c>. Across different senders, tokens are not
/// comparable — use <see cref="Sender"/> to partition.
/// </para>
/// <para>
/// <b>Serialization:</b> Decorated with <c>[GenerateSerializer]</c> for
/// Orleans and <c>[JsonConverter]</c> for System.Text.Json. Round-trips
/// correctly through any Orleans storage provider using either serializer.
/// </para>
/// </remarks>
/// <param name="SequenceNumber">
/// Monotonically increasing sequence number within a single <see cref="Epoch"/>.
/// Assigned by <see cref="Outbox{T}.Add"/> — callers cannot fabricate or choose
/// sequence numbers. Starts at 1 for each new epoch.
/// </param>
/// <param name="Sender">
/// The <see cref="GrainId"/> of the grain that owns the outbox. Receivers use
/// this to partition dedup tracking per sender.
/// </param>
/// <param name="Timestamp">
/// Wall-clock time when the message was added to the outbox. Informational —
/// not used for ordering or dedup. Stamped by the sender's
/// <see cref="TimeProvider"/>.
/// </param>
/// <param name="Epoch">
/// Opaque marker that changes only on <see cref="Outbox{T}.Create"/> (full
/// sequence-space reset). Receivers compare epochs to detect resets. A token
/// with a newer epoch supersedes all prior sequence numbers from the same sender.
/// </param>
[GenerateSerializer]
[Alias("egil.orleans.messaging.OutboxSequenceToken")]
[JsonConverter(typeof(OutboxSequenceTokenJsonConverter))]
public sealed record OutboxSequenceToken(
    [property: Id(0)] long SequenceNumber,
    [property: Id(1)] GrainId Sender,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] DateTimeOffset Epoch);
