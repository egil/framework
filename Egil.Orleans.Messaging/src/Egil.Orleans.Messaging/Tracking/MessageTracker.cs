using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Receiver-side, persisted dedup state. Tracks the high-water position from
/// each upstream message source so the grain can detect and reject duplicates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two source kinds:</b>
/// <list type="bullet">
/// <item><b>Orleans streams</b> — keyed by stream namespace within the grain,
/// plus stream provider name when the received token exposes one; position is
/// a <see cref="StreamCursor"/> wrapping <c>(streamNamespace, StreamSequenceToken)</c>.</item>
/// <item><b>Outbox messages</b> — keyed by sender <see cref="GrainId"/>;
/// position is an <see cref="OutboxSequenceToken"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Sealed class, not record.</b> Consistent with <see cref="Outbox{T}"/> —
/// avoids <c>time</c> field participating in record-synthesized equality, and
/// prevents <c>with { ... }</c> expressions that could bypass invariants.
/// </para>
/// <para>
/// <b>ProcessMessage semantics (streams):</b>
/// <list type="bullet">
/// <item>No prior entry → Accept, insert <c>(LastPosition = cursor, Received = now)</c>.</item>
/// <item><c>cursor &gt; stored.LastPosition</c> → Accept, update position + received.</item>
/// <item><c>cursor &lt;= stored.LastPosition</c> → Reject (duplicate), no change.</item>
/// </list>
/// </para>
/// <para>
/// <b>ProcessMessage semantics (outbox):</b>
/// <list type="bullet">
/// <item>No prior entry → Accept, insert.</item>
/// <item><c>token.Epoch &gt; stored.Epoch</c> → Accept (sender reset), replace entry.</item>
/// <item>Same epoch, <c>token.Seq &gt; stored.LastSeq</c> → Accept, update.</item>
/// <item>Same epoch, <c>token.Seq &lt;= stored.LastSeq</c> → Reject (duplicate).</item>
/// <item><c>token.Epoch &lt; stored.Epoch</c> → Reject (stale epoch).</item>
/// </list>
/// </para>
/// <para>
/// <b>Eviction:</b> Five overloads, one rule — remove entries where
/// <c>entry.Received &lt;= olderThan</c>. No separate <c>Forget</c> API.
/// <c>Evict(id, DateTimeOffset.MaxValue)</c> is the documented idiom for
/// unconditional removal of a single source entry.
/// </para>
/// <para>
/// <b>TimeProvider:</b> Non-persisted (<c>[NonSerialized]</c>,
/// <c>[JsonIgnore]</c>, no <c>[Id]</c>). After deserialization, the grain
/// must call <see cref="RegisterTimeProvider"/> to inject a test-friendly
/// clock. Falls back to <see cref="TimeProvider.System"/> if skipped.
/// </para>
/// <para>
/// <b>Serialization:</b> Decorated with <c>[GenerateSerializer]</c> for Orleans
/// and <c>[JsonConverter]</c> for STJ. The custom converter keeps private backing
/// fields encapsulated.
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias("egil.orleans.messaging.MessageTracker")]
[JsonConverter(typeof(MessageTrackerJsonConverter))]
public sealed class MessageTracker : IEquatable<MessageTracker>
{
    [Id(0)] private readonly ImmutableDictionary<StreamSource, StreamEntry> streams;
    [Id(1)] private readonly ImmutableDictionary<GrainId, OutboxEntry> outbox;

    /// <summary>
    /// Non-persisted service reference. No <c>[Id]</c>, no serialization.
    /// Falls back to <see cref="TimeProvider.System"/> when not explicitly set.
    /// </summary>
    [NonSerialized]
    [JsonIgnore]
    private TimeProvider time = TimeProvider.System;

    /// <summary>
    /// Creates an empty <see cref="MessageTracker"/> with no tracked sources.
    /// </summary>
    public MessageTracker()
    {
        streams = ImmutableDictionary<StreamSource, StreamEntry>.Empty;
        outbox = ImmutableDictionary<GrainId, OutboxEntry>.Empty;
    }

    internal MessageTracker(
        ImmutableDictionary<StreamSource, StreamEntry> streams,
        ImmutableDictionary<GrainId, OutboxEntry> outbox)
    {
        this.streams = streams;
        this.outbox = outbox;
    }

    /// <summary>
    /// Registers a <see cref="TimeProvider"/> for <c>Received</c> timestamps.
    /// Must be called after deserialization to inject a test-friendly clock.
    /// </summary>
    public void RegisterTimeProvider(TimeProvider time) => this.time = time;

    /// <summary>
    /// Evaluates a stream message for acceptance. Returns <c>true</c> if the
    /// <paramref name="cursor"/> advances past the stored high-water mark.
    /// </summary>
    /// <param name="cursor">The stream cursor to evaluate.</param>
    /// <param name="next">
    /// When accepted, a new <see cref="MessageTracker"/> with the updated
    /// position. When rejected, equals <c>this</c>.
    /// </param>
    /// <returns><c>true</c> if accepted (new message); <c>false</c> if duplicate.</returns>
    public bool ProcessMessage(StreamCursor cursor, out MessageTracker next)
    {
        var now = time.GetUtcNow();

        var source = StreamSource.From(cursor);
        if (!streams.TryGetValue(source, out var entry))
        {
            MessagingTelemetry.RecordStreamReceiveLag(cursor, now);
            next = CreateTracker(streams.Add(source, new StreamEntry(cursor, now)), outbox);
            return true;
        }

        if (!IsNewer(cursor.Token, entry.LastPosition.Token))
        {
            next = this;
            return false;
        }

        MessagingTelemetry.RecordStreamReceiveLag(cursor, now);
        next = CreateTracker(streams.SetItem(source, new StreamEntry(cursor, now)), outbox);
        return true;
    }

    /// <summary>
    /// Evaluates an outbox message for acceptance. Returns <c>true</c> if the
    /// <paramref name="token"/> advances past the stored position or carries
    /// a newer epoch.
    /// </summary>
    /// <param name="token">The outbox sequence token to evaluate.</param>
    /// <param name="next">
    /// When accepted, a new <see cref="MessageTracker"/> with the updated
    /// position. When rejected, equals <c>this</c>.
    /// </param>
    /// <returns><c>true</c> if accepted; <c>false</c> if duplicate or stale.</returns>
    public bool ProcessMessage(OutboxSequenceToken token, out MessageTracker next)
    {
        var now = time.GetUtcNow();

        if (!outbox.TryGetValue(token.Sender, out var entry))
        {
            MessagingTelemetry.RecordOutboxReceiveLag(token, now);
            next = CreateTracker(streams, outbox.Add(token.Sender, new OutboxEntry(token.Epoch, token.SequenceNumber, now)));
            return true;
        }

        if (token.Epoch > entry.Epoch)
        {
            MessagingTelemetry.RecordOutboxReceiveLag(token, now);
            next = CreateTracker(streams, outbox.SetItem(token.Sender, new OutboxEntry(token.Epoch, token.SequenceNumber, now)));
            return true;
        }

        if (token.Epoch == entry.Epoch && token.SequenceNumber > entry.LastSequenceNumber)
        {
            MessagingTelemetry.RecordOutboxReceiveLag(token, now);
            next = CreateTracker(streams, outbox.SetItem(token.Sender, new OutboxEntry(entry.Epoch, token.SequenceNumber, now)));
            return true;
        }

        next = this;
        return false;
    }

    /// <summary>
    /// Returns the last accepted <see cref="StreamCursor"/> for the given
    /// <paramref name="streamNamespace"/>, or <c>null</c> if no messages from
    /// that namespace have been tracked by this grain.
    /// </summary>
    public StreamCursor? LatestStream(string streamNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        var conventionSource = new StreamSource(streamNamespace, null);
        if (streams.TryGetValue(conventionSource, out var entry))
        {
            return entry.LastPosition;
        }

        StreamCursor? result = null;
        foreach (var item in streams)
        {
            if (!string.Equals(item.Key.StreamNamespace, streamNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            if (result is not null)
            {
                return null;
            }

            result = item.Value.LastPosition;
        }

        return result;
    }

    /// <summary>
    /// Returns the last accepted <see cref="StreamCursor"/> for the namespace
    /// in the given <paramref name="stream"/>.
    /// </summary>
    public StreamCursor? LatestStream(StreamId stream) =>
        LatestStream(stream.GetNamespace() ?? throw new ArgumentException("StreamId must have a namespace.", nameof(stream)));

    /// <summary>
    /// Returns the last accepted <see cref="OutboxSequenceToken"/> for the
    /// given <paramref name="sender"/>, or <c>null</c> if no outbox messages
    /// from that sender have been tracked.
    /// </summary>
    public OutboxSequenceToken? LatestOutbox(GrainId sender)
    {
        return outbox.TryGetValue(sender, out var entry)
            ? new OutboxSequenceToken(entry.LastSequenceNumber, sender, entry.Received, entry.Epoch)
            : null;
    }

    /// <summary>
    /// Removes all entries (both stream and outbox) where
    /// <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// </summary>
    public MessageTracker Evict(DateTimeOffset olderThan)
    {
        var newStreams = FilterByReceived(streams, olderThan, static entry => entry.Received, out var streamsChanged);
        var newOutbox = FilterByReceived(outbox, olderThan, static entry => entry.Received, out var outboxChanged);

        return streamsChanged || outboxChanged
            ? CreateTracker(newStreams, newOutbox)
            : this;
    }

    /// <summary>
    /// Removes stream entries where
    /// <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// Outbox entries are unaffected.
    /// </summary>
    public MessageTracker EvictStreams(DateTimeOffset olderThan)
    {
        var newStreams = FilterByReceived(streams, olderThan, static entry => entry.Received, out var changed);
        return changed ? CreateTracker(newStreams, outbox) : this;
    }

    /// <summary>
    /// Removes outbox entries where
    /// <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// Stream entries are unaffected.
    /// </summary>
    public MessageTracker EvictOutboxes(DateTimeOffset olderThan)
    {
        var newOutbox = FilterByReceived(outbox, olderThan, static entry => entry.Received, out var changed);
        return changed ? CreateTracker(streams, newOutbox) : this;
    }

    /// <summary>
    /// Removes the entry for the given <paramref name="streamNamespace"/> if
    /// <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// Use <c>DateTimeOffset.MaxValue</c> to unconditionally remove.
    /// </summary>
    public MessageTracker Evict(string streamNamespace, DateTimeOffset olderThan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        var builder = streams.ToBuilder();
        var changed = false;
        foreach (var item in streams)
        {
            if (string.Equals(item.Key.StreamNamespace, streamNamespace, StringComparison.Ordinal)
                && item.Value.Received <= olderThan)
            {
                builder.Remove(item.Key);
                changed = true;
            }
        }

        if (!changed)
        {
            return this;
        }

        return CreateTracker(builder.ToImmutable(), outbox);
    }

    /// <summary>
    /// Removes the entry for the namespace in the given <paramref name="stream"/>
    /// if <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// </summary>
    public MessageTracker Evict(StreamId stream, DateTimeOffset olderThan) =>
        Evict(stream.GetNamespace() ?? throw new ArgumentException("StreamId must have a namespace.", nameof(stream)), olderThan);

    /// <summary>
    /// Removes the entry for the given outbox <paramref name="sender"/> if
    /// <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// Use <c>DateTimeOffset.MaxValue</c> to unconditionally remove.
    /// </summary>
    public MessageTracker Evict(GrainId sender, DateTimeOffset olderThan)
    {
        if (!outbox.TryGetValue(sender, out var entry) || entry.Received > olderThan)
        {
            return this;
        }

        return CreateTracker(streams, outbox.Remove(sender));
    }

    /// <inheritdoc/>
    public bool Equals(MessageTracker? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || streams.Count != other.streams.Count || outbox.Count != other.outbox.Count)
        {
            return false;
        }

        foreach (var item in streams)
        {
            if (!other.streams.TryGetValue(item.Key, out var otherEntry) || !item.Value.Equals(otherEntry))
            {
                return false;
            }
        }

        foreach (var item in outbox)
        {
            if (!other.outbox.TryGetValue(item.Key, out var otherEntry) || !item.Value.Equals(otherEntry))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is MessageTracker o && Equals(o);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            streams.Count,
            AggregateHash(streams),
            outbox.Count,
            AggregateHash(outbox));
    }

    private MessageTracker CreateTracker(
        ImmutableDictionary<StreamSource, StreamEntry> streams,
        ImmutableDictionary<GrainId, OutboxEntry> outbox)
    {
        var next = new MessageTracker(streams, outbox)
        {
            time = time
        };

        return next;
    }

    private static bool IsNewer(StreamSequenceToken? candidate, StreamSequenceToken? stored)
    {
        if (stored is null)
        {
            return candidate is not null;
        }

        if (candidate is null)
        {
            return false;
        }

        return StreamSequenceTokenUtilities.Newer(candidate, stored);
    }

    private static ImmutableDictionary<TKey, TValue> FilterByReceived<TKey, TValue>(
        ImmutableDictionary<TKey, TValue> source,
        DateTimeOffset olderThan,
        Func<TValue, DateTimeOffset> receivedSelector,
        out bool changed)
        where TKey : notnull
    {
        var builder = ImmutableDictionary.CreateBuilder<TKey, TValue>();
        changed = false;

        foreach (var item in source)
        {
            if (receivedSelector(item.Value) <= olderThan)
            {
                changed = true;
                continue;
            }

            builder.Add(item);
        }

        return changed ? builder.ToImmutable() : source;
    }

    private static int AggregateHash<TKey, TValue>(ImmutableDictionary<TKey, TValue> dictionary)
        where TKey : notnull
    {
        var hash = 0;

        foreach (var item in dictionary)
        {
            hash ^= HashCode.Combine(item.Key, item.Value);
        }

        return hash;
    }

    /// <summary>
    /// Internal entry tracking a stream source's last known position and
    /// the wall-clock time it was received.
    /// </summary>
    [GenerateSerializer]
    internal readonly record struct StreamSource(
        [property: Id(0)] string StreamNamespace,
        [property: Id(1)] string? ProviderName)
    {
        public static StreamSource From(StreamCursor cursor) =>
            new(
                cursor.StreamNamespace,
                cursor.TryGetProviderName(out var providerName) ? providerName : null);
    }

    /// <summary>
    /// Internal entry tracking a stream source's last known position and
    /// the wall-clock time it was received.
    /// </summary>
    [GenerateSerializer]
    internal readonly record struct StreamEntry(
        [property: Id(0)] StreamCursor LastPosition,
        [property: Id(1)] DateTimeOffset Received);

    /// <summary>
    /// Internal entry tracking an outbox source's last known epoch,
    /// sequence number, and the wall-clock time it was received.
    /// </summary>
    [GenerateSerializer]
    internal readonly record struct OutboxEntry(
        [property: Id(0)] DateTimeOffset Epoch,
        [property: Id(1)] long LastSequenceNumber,
        [property: Id(2)] DateTimeOffset Received);
}
