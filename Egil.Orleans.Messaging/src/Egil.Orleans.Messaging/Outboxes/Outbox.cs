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
/// mutators (<see cref="Add"/>, <see cref="Remove"/>, <see cref="RemoveRange"/>,
/// <see cref="Clear"/>) return <em>new</em> instances. The original is never
/// modified. Assign the return value back to the state property and write.
/// </para>
/// <para>
/// <b>Sequence ownership:</b> Only <see cref="Add"/> assigns sequence numbers.
/// Callers supply the payload; the outbox stamps <see cref="OutboxSequenceToken"/>
/// with a monotonically increasing <see cref="LatestSequenceNumber"/> and the
/// current <see cref="Epoch"/>. This is a hard invariant — there is no public
/// constructor that accepts a pre-built sequence number.
/// </para>
/// <para>
/// <b>Epoch semantics:</b>
/// <list type="bullet">
/// <item><see cref="Create(GrainId)"/> → <c>Epoch = null</c>,
/// <c>LatestSequenceNumber = 0</c>. Use at construction time or for deliberate
/// ops-level sequence-space resets.</item>
/// <item>First <see cref="Add"/> → stamps <c>Epoch = now</c>. Persisted with state.</item>
/// <item>Subsequent <see cref="Add"/> → same epoch, incrementing sequence number.</item>
/// <item><see cref="Clear"/> → removes all items but <b>preserves</b>
/// <see cref="LatestSequenceNumber"/> and <see cref="Epoch"/>. This is the normal
/// "postman drained successfully" path.</item>
/// </list>
/// Grains should almost never call <see cref="Create(GrainId)"/> on an active outbox.
/// </para>
/// <para>
/// <b>Equality:</b> Two <see cref="Outbox{T}"/> instances are equal when
/// sender, sequence metadata, epoch, count, and the full first and last
/// pending tokens are equal. Equality is O(1) and ignores message payloads —
/// it is a fingerprint, not deep content equality. Within a single history
/// lineage every mutation changes the fingerprint (<see cref="Add"/> changes
/// the last token, removals change the count or tokens), so dirty-check
/// "skip write if unchanged" logic is safe. Outboxes from <em>divergent</em>
/// histories (duplicate activations of the same grain) can compare equal when
/// they diverged only by removals; treating them as interchangeable
/// re-delivers already posted items but never loses pending ones. See
/// <see cref="Equals(Outbox{T}?)"/> for the full contract before relying on
/// equality for anything beyond dirty-checks or write recovery.
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
/// <para>
/// <b>TimeProvider:</b> The <c>time</c> field is non-persisted
/// (<c>[NonSerialized]</c>, <c>[JsonIgnore]</c>, no <c>[Id]</c>). After
/// deserialization the grain must call <see cref="RegisterTimeProvider"/> to
/// inject a test-friendly clock. Falls back to <see cref="TimeProvider.System"/>
/// if skipped — correct for production, breaks fake-clock tests.
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
    [Id(0)] private readonly GrainId sender;
    [Id(1)] private readonly long latestSequenceNumber;
    [Id(2)] private readonly ImmutableArray<OutboxMessageEnvelope<T>> items;
    [Id(3)] private readonly DateTimeOffset? epoch;

    /// <summary>
    /// Non-persisted service reference. No <c>[Id]</c>, no serialization.
    /// Falls back to <see cref="TimeProvider.System"/> when not explicitly set.
    /// </summary>
    [NonSerialized]
    [JsonIgnore]
    private TimeProvider time = TimeProvider.System;

    /// <summary>
    /// Internal constructor used by mutation methods to produce new instances.
    /// Not user-callable — use <see cref="Create(GrainId)"/> to create the
    /// initial outbox, then <see cref="Add"/> to append messages.
    /// </summary>
    internal Outbox(
        GrainId sender,
        long latestSequenceNumber,
        ImmutableArray<OutboxMessageEnvelope<T>> items,
        DateTimeOffset? epoch)
    {
        this.sender = sender;
        this.latestSequenceNumber = latestSequenceNumber;
        this.items = items;
        this.epoch = epoch;
    }

    /// <summary>
    /// Creates a fresh, empty outbox for the given <paramref name="sender"/>.
    /// <see cref="Epoch"/> is <c>null</c> and <see cref="LatestSequenceNumber"/>
    /// is <c>0</c>. The next <see cref="Add"/> stamps a new epoch.
    /// </summary>
    /// <remarks>
    /// Use at grain-state construction time (default property initializer).
    /// Calling on an active outbox is a <b>nuclear reset</b> — the next
    /// <see cref="Add"/> starts a fresh epoch. Receivers see the epoch change
    /// and accept unconditionally. Prefer <see cref="Clear"/> for the normal
    /// "postman drained" path.
    /// </remarks>
    /// <param name="sender">
    /// The <see cref="GrainId"/> of the grain that owns this outbox. Baked
    /// into every <see cref="OutboxSequenceToken"/> produced by <see cref="Add"/>.
    /// </param>
    public static Outbox<T> Create(GrainId sender) =>
        new(sender, latestSequenceNumber: 0, items: [], epoch: null);

    /// <summary>
    /// Registers a <see cref="TimeProvider"/> for timestamp generation.
    /// Must be called after deserialization to inject a test-friendly clock.
    /// </summary>
    /// <remarks>
    /// This is a void mutator on a non-persisted field — it does not produce
    /// a new <see cref="Outbox{T}"/> instance. The provider is carried forward
    /// to successor instances created by <see cref="Add"/>, <see cref="Remove"/>,
    /// <see cref="RemoveRange"/>, and <see cref="Clear"/>.
    /// </remarks>
    public void RegisterTimeProvider(TimeProvider time) => this.time = time;

    /// <summary>
    /// The <see cref="GrainId"/> of the grain that owns this outbox. Baked
    /// into every <see cref="OutboxSequenceToken"/>.
    /// </summary>
    public GrainId Sender => sender;

    /// <summary>
    /// The highest sequence number ever assigned in this outbox, including
    /// items that have been removed. Persists independently of item contents —
    /// <see cref="Clear"/> does not reset it.
    /// </summary>
    public long LatestSequenceNumber => latestSequenceNumber;

    /// <summary>
    /// The epoch marker stamped on the first <see cref="Add"/> call. <c>null</c>
    /// only for a freshly constructed (<see cref="Create(GrainId)"/>) outbox that
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
    public Outbox<T> Add(T message)
    {
        var now = time.GetUtcNow();
        var epoch = this.epoch ?? now;
        var sequenceNumber = latestSequenceNumber + 1;
        var token = new OutboxSequenceToken(sequenceNumber, sender, now, epoch);
        var next = new Outbox<T>(
            sender,
            sequenceNumber,
            items.Add(new OutboxMessageEnvelope<T>(token, message)),
            epoch);

        next.RegisterTimeProvider(time);
        return next;
    }

    /// <summary>
    /// Removes the message identified by <paramref name="token"/> from the outbox.
    /// </summary>
    /// <remarks>
    /// Matches the full <see cref="OutboxSequenceToken"/> identity against the
    /// first pending item. If the token is not the FIFO head, returns the same
    /// instance unchanged. Does <b>not</b> affect
    /// <see cref="LatestSequenceNumber"/> or <see cref="Epoch"/>.
    /// </remarks>
    /// <param name="token">The token of the message to remove.</param>
    /// <returns>A new outbox without the specified message.</returns>
    public Outbox<T> Remove(OutboxSequenceToken token)
    {
        if (items.IsDefaultOrEmpty || items[0].Token != token)
        {
            return this;
        }

        var next = new Outbox<T>(
            sender,
            latestSequenceNumber,
            items.RemoveAt(0),
            epoch);

        next.RegisterTimeProvider(time);
        return next;
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
    public Outbox<T> RemoveRange(IEnumerable<OutboxSequenceToken> tokens)
    {
        var tokenSet = tokens.ToHashSet();
        if (tokenSet.Count == 0)
        {
            return this;
        }

        var remainingBuilder = ImmutableArray.CreateBuilder<OutboxMessageEnvelope<T>>(items.Length);
        foreach (var item in items)
        {
            if (!tokenSet.Contains(item.Token))
            {
                remainingBuilder.Add(item);
            }
        }

        if (remainingBuilder.Count == items.Length)
        {
            return this;
        }

        var remaining = remainingBuilder.ToImmutable();
        var next = new Outbox<T>(
            sender,
            latestSequenceNumber,
            remaining,
            epoch);
        next.RegisterTimeProvider(time);
        return next;
    }

    /// <summary>
    /// Removes all pending messages but <b>preserves</b>
    /// <see cref="LatestSequenceNumber"/> and <see cref="Epoch"/>.
    /// </summary>
    /// <remarks>
    /// This is the normal path after the postman has successfully drained all
    /// items. The high-water mark persists so subsequent <see cref="Add"/> calls
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

        var next = new Outbox<T>(
            sender,
            latestSequenceNumber,
            [],
            epoch);

        next.RegisterTimeProvider(time);
        return next;
    }

    /// <summary>
    /// O(1) equality over the outbox identity and sequence fingerprint:
    /// sender, latest sequence number, epoch, count, and the full first and
    /// last pending tokens (including their timestamps).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This equality is the contract the state-manager write recovery relies
    /// on: after an ambiguous storage write (the call threw but the write may
    /// have landed, e.g. a timeout or lost response), the state manager reads
    /// the persisted state back and uses <c>Equals</c> to decide between
    /// "lost response — the write landed, swallow the error" and "the write
    /// did not land — rethrow so the caller retries". A false positive here
    /// would make recovery adopt a foreign state and silently drop pending
    /// messages, so the fingerprint must never compare equal for a history
    /// that is missing items this instance added.
    /// </para>
    /// <para>
    /// The fingerprint intentionally does not compare message payloads, to
    /// avoid an O(n) scan on every recovery. It is still loss-safe because
    /// items are only ever appended at the tail: two histories that diverged
    /// by <em>adding</em> different messages — for example duplicate grain
    /// activations of the same grain in two silos racing an ambiguous write —
    /// always differ in count or in their highest pending token. The token
    /// timestamp is stamped by the producing activation's clock, which also
    /// distinguishes same-count races unless both activations produced the
    /// exact same timestamp.
    /// </para>
    /// <para>
    /// Note that direct optimistic-concurrency conflicts are not handled
    /// here: when the storage provider reports an ETag conflict
    /// (<c>InconsistentStateException</c>) the state manager rethrows it even
    /// if the values happen to match, so this equality only decides the
    /// ambiguous-outcome cases described above.
    /// </para>
    /// <para>
    /// Accepted trade-off: histories that diverged only by <em>removals</em>
    /// (or the exact-timestamp tie above) can still compare equal. Recovery
    /// then at worst re-delivers an already posted item — duplicate delivery
    /// is acceptable under the at-least-once contract, losing a pending
    /// message is not.
    /// </para>
    /// </remarks>
    public bool Equals(Outbox<T>? other)
    {
        return ReferenceEquals(this, other)
            || (other is not null
                && sender.Equals(other.sender)
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
            sender,
            latestSequenceNumber,
            epoch,
            items.Length,
            FirstToken,
            LastToken);

    // Full tokens (sequence number + epoch + timestamp) rather than bare
    // sequence numbers: the timestamp comes from the producing activation's
    // clock, so it disambiguates duplicate activations that appended different
    // items yet reached the same sequence number. See Equals for the recovery
    // contract this protects.
    private OutboxSequenceToken? FirstToken =>
        items.IsDefaultOrEmpty ? null : items[0].Token;

    private OutboxSequenceToken? LastToken =>
        items.IsDefaultOrEmpty ? null : items[^1].Token;
}
