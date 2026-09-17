using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Egil.Orleans.Messaging.Outboxes;

/// <summary>
/// A per-grain durable buffer of messages that have been <em>announced</em>
/// (committed alongside a state change) but not yet <em>delivered</em>
/// (handed off to a postman successfully). Lives as a property on the grain's
/// state record so it participates in atomic <c>WriteStateAsync</c> calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Immutable-collection semantics:</b> Behaves like
/// <see cref="ImmutableArray{T}"/> — read-only iteration, indexer access,
/// mutators (<see cref="Add(T)"/>, <see cref="Remove(OutboxMessageId)"/>, <see cref="RemoveRange(IEnumerable{OutboxMessageId})"/>,
/// <see cref="Clear"/>) return <em>new</em> instances. The original is never
/// modified. Assign the return value back to the state property and write.
/// </para>
/// <para>
/// <b>Sequence ownership:</b> <see cref="Add(T)"/>, <see cref="AddRange(IEnumerable{T})"/>, and collection expressions assign sequence numbers.
/// Callers supply the payload; the outbox stamps <see cref="OutboxMessageId"/>
/// with a monotonically increasing <see cref="LatestSequenceNumber"/> and the
/// current <see cref="Epoch"/>. <see cref="Restore(IEnumerable{OutboxMessageEnvelope{T}}, long)"/>
/// is the single exception, and carries a different name for exactly that reason:
/// reconstructing stored history is not producing messages, so it accepts
/// pre-built identities — and validates that they increase within one epoch.
/// </para>
/// <para>
/// <b>Epoch semantics:</b>
/// <list type="bullet">
/// <item><see cref="Create()"/> → <c>Epoch = null</c>,
/// <c>LatestSequenceNumber = 0</c>. Use at construction time or for deliberate
/// ops-level sequence-space resets.</item>
/// <item>First <see cref="Add(T)"/> → stamps <c>Epoch = now</c>. Persisted with state.</item>
/// <item>Subsequent <see cref="Add(T)"/> → same epoch, incrementing sequence number.</item>
/// <item><see cref="Restore(IEnumerable{T}, DateTimeOffset)"/> → takes the epoch
/// from the restored data, never from the clock and never from an ambient activity.</item>
/// <item><see cref="Clear"/> → removes all items but <b>preserves</b>
/// <see cref="LatestSequenceNumber"/> and <see cref="Epoch"/>. This is the normal
/// "postman drained successfully" path.</item>
/// </list>
/// Grains should almost never call <see cref="Create()"/> on an active outbox.
/// </para>
/// <para>
/// <b>Equality:</b> Snapshot revisions are compared in O(1),
/// without scanning payloads. Each changed snapshot receives a fresh UUIDv7
/// <see cref="Revision"/>. Serialization preserves it, while no-op mutations
/// return the existing snapshot. This permits recovery to confirm a saved
/// snapshot without mistaking a competing append or removal for the same write.
/// Independently constructed snapshots are unequal even with identical contents.
/// </para>
/// <para>
/// <b>Unbounded growth risk:</b> If postman targets are down, the outbox grows
/// without limit unless the owning grain applies a policy. The processor
/// reports depth telemetry and passes failures to
/// <see cref="OutboxProcessorOptions{TOutbox}.ReconcileFailedAsync"/>, where
/// the grain can leave items pending, dead-letter them, or drop old entries
/// before storage-provider entity limits are reached.
/// </para>
/// <para>
/// <b>Serialization:</b> Decorated with <c>[GenerateSerializer]</c> for Orleans.
/// A <c>[JsonConverter]</c> attribute (via <c>JsonConverterFactory</c>) ensures
/// System.Text.Json round-trips without exposing private backing fields.
/// Newtonsoft.Json is not supported out of the box; users can write and
/// register their own Newtonsoft converter if needed.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The user-defined message payload type. Must be serializable by Orleans
/// (<c>[GenerateSerializer]</c>) and by System.Text.Json if the storage provider
/// uses STJ.
/// </typeparam>
[GenerateSerializer]
[Alias("egil.orleans.messaging.Outbox`1")]
[JsonConverter(typeof(OutboxJsonConverterFactory))]
[CollectionBuilder(typeof(Outbox), nameof(Outbox.Create))]
public sealed class Outbox<T> : IReadOnlyList<T>, IEquatable<Outbox<T>>
{
    [Id(1)] private readonly long latestSequenceNumber;
    [Id(2)] private readonly ImmutableArray<OutboxMessageEnvelope<T>> items;
    [Id(3)] private readonly DateTimeOffset? epoch;

    /// <summary>
    /// UUIDv7 identity of this snapshot, used as an outbox-specific ETag for recovery.
    /// Changes with each mutation and survives serialization. It is not a delivery token.
    /// </summary>
    /// <remarks>
    /// An ambiguous storage failure requires proof that the attempted snapshot was saved.
    /// Sequence numbers and timestamps cannot provide that proof: competing activations
    /// can append different payloads with identical IDs. A fresh revision distinguishes
    /// those snapshots without comparing every payload. No-op operations return this
    /// instance so they preserve its identity; deserialization restores the stored revision.
    /// </remarks>
    [Id(4)] public Guid Revision { get; }

    /// <summary>
    /// Internal constructor used by mutation methods to produce new instances.
    /// Not user-callable — use <see cref="Create()"/> to create the
    /// initial outbox, then <see cref="Add(T)"/> to append messages.
    /// </summary>
    internal Outbox(
        long latestSequenceNumber,
        ImmutableArray<OutboxMessageEnvelope<T>> items,
        DateTimeOffset? epoch,
        Guid revision)
    {
        this.latestSequenceNumber = latestSequenceNumber;
        this.items = items;
        this.epoch = epoch;
        // Restore the supplied identity instead of generating one here. Deserialization
        // must preserve it so recovery recognizes a saved snapshot after a lost response.
        // Creation and successful mutations explicitly supply a fresh UUIDv7 instead.
        Revision = revision;
    }

    /// <summary>
    /// Creates a fresh, empty outbox without an owning grain identity.
    /// <see cref="Epoch"/> is <c>null</c> and <see cref="LatestSequenceNumber"/>
    /// is <c>0</c>. The next <see cref="Add(T)"/> stamps a new epoch.
    /// </summary>
    /// <remarks>
    /// Use at grain-state construction time (default property initializer).
    /// Calling on an active outbox is a <b>nuclear reset</b> — the next
    /// <see cref="Add(T)"/> starts a fresh epoch. Receivers see the epoch change
    /// and accept unconditionally. Prefer <see cref="Clear"/> for the normal
    /// "postman drained" path.
    /// </remarks>
    public static Outbox<T> Create() =>
        new(latestSequenceNumber: 0, items: [], epoch: null, revision: Guid.CreateVersion7());

    /// <summary>
    /// Rebuilds an outbox from payloads produced earlier, each paired with the
    /// instant it was originally appended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use when reconstructing stored history — a state migration, an import, a
    /// replay — rather than producing messages. Unlike <see cref="Add(T, DateTimeOffset)"/>,
    /// <b>no trace context is recorded</b>. A rebuild runs under whatever activity
    /// happens to be current — for a migration inside grain-state deserialization,
    /// the activation — and that activity did not produce these messages. Linking a
    /// delivery span to it is worse than linking to nothing, because a wrong link is
    /// indistinguishable from a right one when reading a trace.
    /// </para>
    /// <para>
    /// The outbox still owns sequence assignment: payloads receive consecutive
    /// numbers from <c>1</c> in enumeration order, and the first payload's timestamp
    /// becomes the <see cref="Epoch"/>, matching the first-append rule. Timestamps are
    /// normalized to UTC and need not be ordered — receivers deduplicate on epoch and
    /// sequence number, not on time. An empty sequence yields the same shape as
    /// <see cref="Create()"/>.
    /// <code>
    /// var restored = Outbox&lt;OrderEvent&gt;.Restore(
    ///     legacy.Outbox.Select(item =&gt; (item, item.Timestamp)));
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="messages">The payloads to restore, paired with their original append instants.</param>
    /// <returns>An outbox holding the restored messages.</returns>
    public static Outbox<T> Restore(IEnumerable<(T Message, DateTimeOffset Timestamp)> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var builder = CreateEnvelopeBuilder(messages);
        DateTimeOffset? epoch = null;
        var sequenceNumber = 0L;
        foreach (var (message, timestamp) in messages)
        {
            var utcTimestamp = timestamp.ToUniversalTime();
            epoch ??= utcTimestamp;
            builder.Add(new(new OutboxMessageId(++sequenceNumber, utcTimestamp, epoch.Value), message));
        }

        return new Outbox<T>(sequenceNumber, builder.DrainToImmutable(), epoch, Guid.CreateVersion7());
    }

    /// <summary>
    /// Rebuilds an outbox from payloads produced earlier that share a single
    /// timestamp, for source formats that kept none per message.
    /// </summary>
    /// <remarks>
    /// Behaves as <see cref="Restore(IEnumerable{ValueTuple{T, DateTimeOffset}})"/> with
    /// <paramref name="utcNow"/> paired to every payload: consecutive sequence numbers
    /// from <c>1</c>, that instant as both every timestamp and the <see cref="Epoch"/>,
    /// and no trace context recorded. Non-UTC offsets are normalized to UTC.
    /// </remarks>
    /// <param name="messages">The payloads to restore, in their original order.</param>
    /// <param name="utcNow">The instant to stamp on every restored message.</param>
    /// <returns>An outbox holding the restored messages.</returns>
    public static Outbox<T> Restore(IEnumerable<T> messages, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var builder = CreateEnvelopeBuilder(messages);
        var timestamp = utcNow.ToUniversalTime();
        var sequenceNumber = 0L;
        foreach (var message in messages)
        {
            builder.Add(new(new OutboxMessageId(++sequenceNumber, timestamp, timestamp), message));
        }

        return new Outbox<T>(
            sequenceNumber,
            builder.DrainToImmutable(),
            // An empty restore has no history to date, so it must not claim an epoch;
            // the next Add stamps one, exactly as it would after Create().
            sequenceNumber == 0 ? null : timestamp,
            Guid.CreateVersion7());
    }

    /// <summary>
    /// Rebuilds an outbox from envelopes that already carry their stored identity,
    /// preserving sequence numbers, timestamps, epoch, and trace context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The full-fidelity restore: for moving messages between outboxes, or replaying
    /// an exported one, where the original <see cref="OutboxMessageId"/> values must
    /// survive. Nothing is captured from the ambient activity — see
    /// <see cref="Restore(IEnumerable{ValueTuple{T, DateTimeOffset}})"/> for why — and each
    /// <see cref="OutboxMessageId.TraceParent"/> is carried through verbatim, including
    /// values that are not parseable W3C traceparents. Delivery tolerates those by
    /// starting an unlinked span, and rejecting them here would fail a grain activation
    /// over a diagnostic field.
    /// </para>
    /// <para>
    /// This is the one entry point that accepts caller-supplied sequence numbers, so it
    /// validates them. <see cref="OutboxMessageId.SequenceNumber"/> must strictly increase
    /// in enumeration order and every <see cref="OutboxMessageId.Epoch"/> must match:
    /// <see cref="Remove(OutboxMessageId)"/> only matches a FIFO head and receivers
    /// deduplicate against a per-epoch high-water mark, so a mis-ordered restore produces
    /// an outbox whose messages the receiver silently drops.
    /// </para>
    /// <para>
    /// Restoring a live outbox therefore carries its high-water mark across as well:
    /// <code>
    /// var moved = Outbox&lt;OrderEvent&gt;.Restore(previous.Envelopes, previous.LatestSequenceNumber);
    /// </code>
    /// An empty sequence with <paramref name="latestSequenceNumber"/> <c>0</c> yields the
    /// same shape as <see cref="Create()"/>; an empty sequence with a higher mark keeps it,
    /// and the next <see cref="Add(T)"/> stamps a fresh epoch above it.
    /// </para>
    /// </remarks>
    /// <param name="envelopes">The stored envelopes to restore, in FIFO order.</param>
    /// <param name="latestSequenceNumber">
    /// The source outbox's <see cref="LatestSequenceNumber"/>. Required rather than
    /// inferred: <see cref="Envelopes"/> holds only what is still pending, so a source
    /// whose highest-numbered messages were already delivered and removed would restore
    /// a lower high-water mark, and the next <see cref="Add(T)"/> would reuse a sequence
    /// number the receiver has already seen and reject as a duplicate.
    /// </param>
    /// <returns>An outbox holding the restored envelopes.</returns>
    /// <exception cref="ArgumentException">
    /// Sequence numbers do not strictly increase in enumeration order, or the envelopes
    /// do not all share one epoch.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="latestSequenceNumber"/> is below the last envelope's sequence
    /// number, or is negative.
    /// </exception>
    public static Outbox<T> Restore(IEnumerable<OutboxMessageEnvelope<T>> envelopes, long latestSequenceNumber)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        var builder = CreateEnvelopeBuilder(envelopes);
        DateTimeOffset? epoch = null;
        long? sequenceNumber = null;
        foreach (var envelope in envelopes)
        {
            ArgumentNullException.ThrowIfNull(envelope, nameof(envelopes));

            var id = envelope.Id;
            // The outbox holds one epoch field, so a mixed-epoch restore has no
            // representation here — it is rejected rather than silently flattened.
            if (epoch is { } established && id.Epoch != established)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"All envelopes must share one epoch; found {established:O} and {id.Epoch:O}."),
                    nameof(envelopes));
            }

            if (sequenceNumber >= id.SequenceNumber)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Envelope sequence numbers must strictly increase in enumeration order; {sequenceNumber} was followed by {id.SequenceNumber}."),
                    nameof(envelopes));
            }

            epoch = id.Epoch;
            sequenceNumber = id.SequenceNumber;
            builder.Add(envelope);
        }

        // Guarded after enumeration because the last sequence number is the floor: a mark
        // below it would let Add hand out a number already carried by a pending message.
        ArgumentOutOfRangeException.ThrowIfLessThan(latestSequenceNumber, sequenceNumber ?? 0);

        return new Outbox<T>(
            latestSequenceNumber,
            builder.DrainToImmutable(),
            epoch,
            Guid.CreateVersion7());
    }

    private static ImmutableArray<OutboxMessageEnvelope<T>>.Builder CreateEnvelopeBuilder<TSource>(
        IEnumerable<TSource> source) =>
        ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>(
            source.TryGetNonEnumeratedCount(out var count) ? count : 0);

    /// <summary>
    /// The highest sequence number ever assigned in this outbox, including
    /// items that have been removed. Persists independently of item contents —
    /// <see cref="Clear"/> does not reset it.
    /// </summary>
    public long LatestSequenceNumber => latestSequenceNumber;

    /// <summary>
    /// The epoch marker stamped on the first <see cref="Add(T)"/> call. <c>null</c>
    /// only for a freshly constructed (<see cref="Create()"/>) outbox that
    /// has never had an item added.
    /// </summary>
    public DateTimeOffset? Epoch => epoch;

    /// <summary>Gets the number of pending messages.</summary>
    public int Count => items.Length;

    /// <summary>
    /// <c>true</c> when the outbox contains no pending messages. Note that
    /// <see cref="LatestSequenceNumber"/> may be non-zero even when empty
    /// (items were drained by the postman).
    /// </summary>
    public bool IsEmpty => items.IsDefaultOrEmpty;

    /// <summary>Gets the payload at the specified index.</summary>
    public T this[int index] => items[index].Message;

    /// <summary>Gets the immutable snapshot of payloads and their assigned message IDs.</summary>
    /// <remarks>
    /// Acknowledgement callbacks return entries from this view. Pass them to
    /// <see cref="RemoveRange(IEnumerable{OutboxMessageEnvelope{T}})"/> to remove
    /// specific queued occurrences, even when payloads are equal. The array is
    /// shared without copying; payload objects must not be mutated after enqueueing.
    /// </remarks>
    public ImmutableArray<OutboxMessageEnvelope<T>> Envelopes => items;

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        foreach (var item in items)
        {
            yield return item.Message;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Appends a message to the outbox, assigning the next sequence number
    /// and stamping the epoch on first call.
    /// </summary>
    /// <remarks>
    /// Returns a <b>new</b> <see cref="Outbox{T}"/> instance — the original
    /// is not modified. Assign the result back to the state property:
    /// <code>
    /// state = state with { Outbox = state.Outbox.Add(myEvent) };
    /// await stateManager.WriteAsync(state);
    /// </code>
    /// </remarks>
    /// <param name="message">The user-defined payload to enqueue.</param>
    /// <returns>A new outbox containing the appended message.</returns>
    public Outbox<T> Add(T message) =>
        Add(message, TimeProvider.System.GetUtcNow());

    /// <summary>
    /// Appends a message using an explicitly supplied current UTC instant,
    /// assigning the next sequence number and stamping the epoch on first call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this overload when timestamp generation must use an injected clock.
    /// Pass the clock's current instant for each append:
    /// <code>
    /// state = state with
    /// {
    ///     Outbox = state.Outbox.Add(myEvent, timeProvider.GetUtcNow())
    /// };
    /// </code>
    /// </para>
    /// <para>
    /// <paramref name="utcNow"/> is the append instant, not the message's
    /// domain timestamp. It becomes the token timestamp and, for the first
    /// append, the outbox epoch. Non-UTC offsets are normalized to UTC.
    /// </para>
    /// </remarks>
    /// <param name="message">The user-defined payload to enqueue.</param>
    /// <param name="utcNow">The current instant used to timestamp this append.</param>
    /// <returns>A new outbox containing the appended message.</returns>
    public Outbox<T> Add(T message, DateTimeOffset utcNow)
    {
        utcNow = utcNow.ToUniversalTime();
        var epoch = this.epoch ?? utcNow;
        var sequenceNumber = latestSequenceNumber + 1;
        // Captured here, not at delivery time. The processor drains on a grain
        // timer, a reminder, or whichever request happens to trigger the drain,
        // and dispatches groups concurrently, so Activity.Current during delivery
        // is unrelated to the request that appended this message.
        var token = new OutboxMessageId(sequenceNumber, utcNow, epoch, Activity.Current?.Id);
        return new Outbox<T>(
            sequenceNumber,
            items.Add(new OutboxMessageEnvelope<T>(token, message)),
            epoch,
            Guid.CreateVersion7());
    }

    /// <summary>Appends payloads in enumeration order using one system UTC timestamp for the batch.</summary>
    /// <remarks>
    /// Preserves existing IDs and sequence history. An empty batch returns this
    /// snapshot unchanged. Inputs are enumerated once and the backing array is built once.
    /// </remarks>
    public Outbox<T> AddRange(IEnumerable<T> messages) =>
        AddRange(messages, TimeProvider.System.GetUtcNow());

    /// <summary>Appends payloads with consecutive IDs using the supplied timestamp for the batch.</summary>
    /// <remarks>Normalizes the timestamp to UTC. The first nonempty batch establishes the epoch.</remarks>
    public Outbox<T> AddRange(IEnumerable<T> messages, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(messages);
        using var enumerator = messages.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return this;
        }

        utcNow = utcNow.ToUniversalTime();
        var batchEpoch = epoch ?? utcNow;
        var sequenceNumber = latestSequenceNumber;
        // Captured here, not at delivery time. The processor drains on a grain
        // timer, a reminder, or whichever request happens to trigger the drain,
        // and dispatches groups concurrently, so Activity.Current during delivery
        // is unrelated to the request that appended this message.
        // One capture for the whole batch, matching the single batch timestamp:
        // a batch is appended within one ambient scope by construction.
        var traceParent = Activity.Current?.Id;
        var count = messages is IReadOnlyCollection<T> collection
            ? collection.Count
            : messages.TryGetNonEnumeratedCount(out var knownCount) ? knownCount : 0;
        var capacity = checked(items.Length + count);
        var builder = ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>(capacity);
        builder.AddRange(items);
        do
        {
            var id = new OutboxMessageId(++sequenceNumber, utcNow, batchEpoch, traceParent);
            builder.Add(new(id, enumerator.Current));
        }
        while (enumerator.MoveNext());

        return new(sequenceNumber, builder.DrainToImmutable(), batchEpoch, Guid.CreateVersion7());
    }

    /// <summary>Removes the supplied queued occurrence if it is the FIFO head.</summary>
    /// <remarks>Matches the message ID, not payload equality; a non-head item leaves the snapshot unchanged.</remarks>
    public Outbox<T> Remove(OutboxMessageEnvelope<T> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Remove(item.Id);
    }

    /// <summary>Removes the supplied queued occurrences by ID, preserving the order of remaining items.</summary>
    /// <remarks>Supports noncontiguous acknowledgement batches. Missing IDs are ignored.</remarks>
    public Outbox<T> RemoveRange(IEnumerable<OutboxMessageEnvelope<T>> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return RemoveRange(items.Select(item => item.Id));
    }

    /// <summary>
    /// Removes the message identified by <paramref name="id"/> from the outbox.
    /// </summary>
    /// <remarks>
    /// Matches the full <see cref="OutboxMessageId"/> identity against the
    /// first pending item. If the id is not the FIFO head, returns the same
    /// instance unchanged. Does <b>not</b> affect
    /// <see cref="LatestSequenceNumber"/> or <see cref="Epoch"/>.
    /// </remarks>
    /// <param name="id">The stored ID of the message to remove.</param>
    /// <returns>A new outbox without the specified message.</returns>
    public Outbox<T> Remove(OutboxMessageId id)
    {
        if (items.IsDefaultOrEmpty || items[0].Id != id)
        {
            return this;
        }

        return new Outbox<T>(
            latestSequenceNumber,
            items.RemoveAt(0),
            epoch,
            Guid.CreateVersion7());
    }

    /// <summary>
    /// Batch-removes messages identified by <paramref name="ids"/>.
    /// </summary>
    /// <remarks>
    /// Removes all pending messages whose ids appear in
    /// <paramref name="ids"/> and preserves the original order of messages
    /// that remain pending. IDs not found in the outbox are silently ignored.
    /// </remarks>
    /// <param name="ids">The stored IDs of the messages to remove.</param>
    /// <returns>A new outbox without the specified messages.</returns>
    public Outbox<T> RemoveRange(IEnumerable<OutboxMessageId> ids)
    {
        var idSet = ids.ToHashSet();
        if (idSet.Count == 0)
        {
            return this;
        }

        var remainingBuilder = ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>(items.Length);
        foreach (var item in items)
        {
            if (!idSet.Contains(item.Id))
            {
                remainingBuilder.Add(item);
            }
        }

        if (remainingBuilder.Count == items.Length)
        {
            return this;
        }

        var remaining = remainingBuilder.ToImmutable();
        return new Outbox<T>(
            latestSequenceNumber,
            remaining,
            epoch,
            Guid.CreateVersion7());
    }

    /// <summary>
    /// Removes all pending messages but <b>preserves</b>
    /// <see cref="LatestSequenceNumber"/> and <see cref="Epoch"/>.
    /// </summary>
    /// <remarks>
    /// This is the normal path after the postman has successfully drained all
    /// items. The high-water mark persists so subsequent <see cref="Add(T)"/> calls
    /// continue the sequence without gaps. Receivers see monotonically increasing
    /// sequence numbers within the same epoch.
    /// </remarks>
    /// <returns>A new empty outbox preserving sequence metadata.</returns>
    public Outbox<T> Clear()
    {
        if (items.IsDefaultOrEmpty)
        {
            return this;
        }

        return new Outbox<T>(
            latestSequenceNumber,
            [],
            epoch,
            Guid.CreateVersion7());
    }

    /// <summary>
    /// O(1) snapshot equality using the persisted revision.
    /// </summary>
    /// <remarks>
    /// Every mutation assigns a fresh UUIDv7 revision. Recovery can therefore
    /// distinguish competing snapshots even when append timestamps, sequence
    /// numbers, and endpoint IDs match. Serialization preserves the revision
    /// so a successful write with a lost response can still be confirmed.
    /// Independently constructed snapshots are unequal even with identical payloads.
    /// Revisions are compared for equality, not order: clock order cannot prove persistence.
    /// First/last IDs, count, and sequence metadata are unnecessary once revisions identify
    /// snapshots. Those checks previously allowed different payloads with matching metadata
    /// to appear equal, causing recovery to swallow an error for a write that did not land.
    /// This contract assumes payloads are not changed in place after enqueueing: callers
    /// must preserve the snapshot represented by the revision.
    /// </remarks>
    public bool Equals(Outbox<T>? other)
    {
        // Keep equality reflexive, including for an instance loaded from older binary data.
        // Distinct instances with a missing revision cannot establish persistence identity;
        // rejecting them is safer than reporting a competing or unknown save as successful.
        return ReferenceEquals(this, other)
            || (other is not null
                && Revision != Guid.Empty
                && Revision == other.Revision);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Outbox<T> o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode() => Revision.GetHashCode();
}
