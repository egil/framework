using System.Collections;
using System.Collections.Immutable;
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
/// mutators (<see cref="Add(T)"/>, <see cref="Remove"/>, <see cref="RemoveRange"/>,
/// <see cref="Clear"/>) return <em>new</em> instances. The original is never
/// modified. Assign the return value back to the state property and write.
/// </para>
/// <para>
/// <b>Sequence ownership:</b> Only <see cref="Add(T)"/> assigns sequence numbers.
/// Callers supply the payload; the outbox stamps <see cref="OutboxMessageId"/>
/// with a monotonically increasing <see cref="LatestSequenceNumber"/> and the
/// current <see cref="Epoch"/>. This is a hard invariant — there is no public
/// constructor that accepts a pre-built sequence number.
/// </para>
/// <para>
/// <b>Epoch semantics:</b>
/// <list type="bullet">
/// <item><see cref="Create()"/> → <c>Epoch = null</c>,
/// <c>LatestSequenceNumber = 0</c>. Use at construction time or for deliberate
/// ops-level sequence-space resets.</item>
/// <item>First <see cref="Add(T)"/> → stamps <c>Epoch = now</c>. Persisted with state.</item>
/// <item>Subsequent <see cref="Add(T)"/> → same epoch, incrementing sequence number.</item>
/// <item><see cref="Clear"/> → removes all items but <b>preserves</b>
/// <see cref="LatestSequenceNumber"/> and <see cref="Epoch"/>. This is the normal
/// "postman drained successfully" path.</item>
/// </list>
/// Grains should almost never call <see cref="Create()"/> on an active outbox.
/// </para>
/// <para>
/// <b>Equality:</b> Snapshot identity and sequence metadata are compared in O(1),
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
public sealed class Outbox<T> : IReadOnlyList<OutboxMessageEnvelope<T>>, IEquatable<Outbox<T>>
{
    [Id(1)] private readonly long latestSequenceNumber;
    [Id(2)] private readonly ImmutableArray<OutboxMessageEnvelope<T>> items;
    [Id(3)] private readonly DateTimeOffset? epoch;

    /// <summary>
    /// UUIDv7 identity of this snapshot, used as an outbox-specific ETag for recovery.
    /// Changes with each mutation and survives serialization. It is not a delivery token.
    /// </summary>
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

    /// <summary>Gets the envelope at the specified index.</summary>
    public OutboxMessageEnvelope<T> this[int index] => items[index];

    /// <inheritdoc/>
    public IEnumerator<OutboxMessageEnvelope<T>> GetEnumerator()
        => ((IEnumerable<OutboxMessageEnvelope<T>>)items).GetEnumerator();

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
        var token = new OutboxMessageId(sequenceNumber, utcNow, epoch);
        return new Outbox<T>(
            sequenceNumber,
            items.Add(new OutboxMessageEnvelope<T>(token, message)),
            epoch,
            Guid.CreateVersion7());
    }

    /// <summary>
    /// Removes the message identified by <paramref name="token"/> from the outbox.
    /// </summary>
    /// <remarks>
    /// Matches the full <see cref="OutboxMessageId"/> identity against the
    /// first pending item. If the token is not the FIFO head, returns the same
    /// instance unchanged. Does <b>not</b> affect
    /// <see cref="LatestSequenceNumber"/> or <see cref="Epoch"/>.
    /// </remarks>
    /// <param name="token">The token of the message to remove.</param>
    /// <returns>A new outbox without the specified message.</returns>
    public Outbox<T> Remove(OutboxMessageId token)
    {
        if (items.IsDefaultOrEmpty || items[0].Id != token)
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
    /// Batch-removes messages identified by <paramref name="tokens"/>.
    /// </summary>
    /// <remarks>
    /// Removes all pending messages whose tokens appear in
    /// <paramref name="tokens"/> and preserves the original order of messages
    /// that remain pending. Tokens not found in the outbox are silently ignored.
    /// </remarks>
    /// <param name="tokens">The tokens of the messages to remove.</param>
    /// <returns>A new outbox without the specified messages.</returns>
    public Outbox<T> RemoveRange(IEnumerable<OutboxMessageId> tokens)
    {
        var tokenSet = tokens.ToHashSet();
        if (tokenSet.Count == 0)
        {
            return this;
        }

        var remainingBuilder = ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>(items.Length);
        foreach (var item in items)
        {
            if (!tokenSet.Contains(item.Id))
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
    /// O(1) snapshot equality using the revision and sequence fingerprint.
    /// </summary>
    /// <remarks>
    /// Every mutation assigns a fresh UUIDv7 revision. Recovery can therefore
    /// distinguish competing snapshots even when append timestamps, sequence
    /// numbers, and endpoint IDs match. Serialization preserves the revision
    /// so a successful write with a lost response can still be confirmed.
    /// Independently constructed snapshots are unequal even with identical payloads.
    /// Revisions are compared for equality, not order: clock order cannot prove persistence.
    /// </remarks>
    public bool Equals(Outbox<T>? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && Revision != Guid.Empty
                && Revision == other.Revision
                && latestSequenceNumber == other.latestSequenceNumber
                && epoch == other.epoch
                && items.Length == other.items.Length
                && Equals(FirstToken, other.FirstToken)
                && Equals(LastToken, other.LastToken));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Outbox<T> o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            Revision,
            latestSequenceNumber,
            epoch,
            items.Length,
            FirstToken,
            LastToken);

    // Retain the sequence fingerprint as a consistency check alongside the
    // revision. Timestamps alone cannot distinguish competing append histories.
    private OutboxMessageId? FirstToken =>
        items.IsDefaultOrEmpty ? null : items[0].Id;

    private OutboxMessageId? LastToken =>
        items.IsDefaultOrEmpty ? null : items[^1].Id;
}
