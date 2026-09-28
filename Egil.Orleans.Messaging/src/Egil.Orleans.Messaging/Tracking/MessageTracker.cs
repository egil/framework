using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tracking;

/// <summary>
/// Immutable receiver-side deduplication state. Persist the returned snapshot with business changes.
/// </summary>
/// <remarks>
/// <para>
/// Streams default to one provider high-water mark per complete source. Opt into
/// <see cref="StreamTrackingMode.OutboxIdentity"/> for exact receipts scoped by provider,
/// complete StreamId, sender, epoch, and sequence. In that mode, unseen lower sequences and
/// earlier epochs remain eligible without moving retained checkpoints backwards.
/// Untagged streams and explicit RPC tokens retain their provider/sender high-water rules.
/// </para>
/// <para>
/// Receipt mode also tracks deliveries with null native positions. Configured retention removes
/// expired entries on acceptance; otherwise entries remain until explicit eviction. Removing an
/// entry ends its duplicate protection. Rejection always returns the original tracker, so callers
/// need not make cleanup-only or checkpoint-only writes.
/// </para>
/// <para>
/// Acceptance times use <see cref="RegisterTimeProvider"/>, then instance configuration, then
/// silo defaults, then <see cref="TimeProvider.System"/>. Immutable updates retain instance
/// settings; serialization does not persist them. Eviction uses each entry's acceptance time.
/// </para>
/// <para>
/// Old namespace-only checkpoints remain legacy entries. Complete-source lookup never guesses
/// their missing keys; callers with known mappings explicitly rebind before tracked resume.
/// </para>
/// </remarks>
[GenerateSerializer]
[Alias("egil.orleans.messaging.MessageTracker")]
[JsonConverter(typeof(MessageTrackerJsonConverter))]
public sealed class MessageTracker : IEquatable<MessageTracker>
{
    [Id(0)] private readonly ImmutableDictionary<StreamSource, StreamEntry> streams;
    [Id(1)] private readonly ImmutableDictionary<GrainId, OutboxEntry> outbox;
    [Id(2)] private readonly ImmutableDictionary<StreamMessageIdentity, DateTimeOffset>? receipts;

    [JsonIgnore]
    internal ImmutableDictionary<StreamMessageIdentity, DateTimeOffset> StreamReceipts =>
        receipts ?? ImmutableDictionary<StreamMessageIdentity, DateTimeOffset>.Empty;

    // Journal replay restores entries without invoking receive-time clocks or telemetry.
    // Immutable views keep that reconstruction seam internal to the toolbox. Only the
    // backing fields have Orleans IDs; these computed properties add no serialized state.
    [JsonIgnore]
    internal ImmutableDictionary<StreamSource, StreamEntry> StreamEntries => streams;
    [JsonIgnore]
    internal ImmutableDictionary<GrainId, OutboxEntry> OutboxEntries => outbox;

    /// <summary>
    /// Non-persisted service reference. No <c>[Id]</c>, no serialization.
    /// <see langword="null"/> falls back to the silo-wide clock.
    /// </summary>
    [NonSerialized]
    [JsonIgnore]
    private TimeProvider? time;

    [field: NonSerialized]
    [JsonIgnore]
    internal MessageTrackerSettings? Configuration { get; set; }

    private MessageTrackerSettings Settings => Configuration ?? MessageTrackerDefaults.Current;

    // A lower bound lets receives skip full-state cleanup until an entry could expire.
    // Updating a checkpoint can make the bound conservative; a due sweep refreshes it.
    // Rebuild after loading or manual eviction instead of persisting derived state.
    // Publish an immutable reference because callers can share a snapshot across readers.
    [NonSerialized]
    [JsonIgnore]
    private volatile RetentionBound? retentionBound;

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
        ImmutableDictionary<GrainId, OutboxEntry> outbox,
        ImmutableDictionary<StreamMessageIdentity, DateTimeOffset>? receipts = null)
    {
        this.receipts = receipts;
        this.streams = streams;
        this.outbox = outbox;
    }

    /// <summary>
    /// Registers a <see cref="TimeProvider"/> for <c>Received</c> timestamps on
    /// this instance and the snapshots it returns. It takes precedence over the
    /// silo-wide clock installed with <c>ConfigureMessageTracker</c>.
    /// </summary>
    public void RegisterTimeProvider(TimeProvider time) => this.time = time;

    /// <summary>Overrides the effective global settings for this tracker and its future snapshots.</summary>
    /// <remarks>
    /// Call from the grain's state configuration hook so overrides are restored after every load.
    /// Settings are runtime configuration, are not serialized, and do not change existing entries.
    /// </remarks>
    public void Configure(Action<MessageTrackerOptions> configure) => Configuration = Settings.Configure(configure);

    /// <summary>
    /// Accepts a delivery according to the configured stream tracking mode and retention period.
    /// </summary>
    /// <remarks>
    /// Receipt mode requires complete source metadata for tagged deliveries, including tokenless ones.
    /// Position mode ignores outbox identity. Acceptance also removes expired entries;
    /// tokenless position deliveries only perform cleanup. Rejection returns this instance.
    /// </remarks>
    public bool TryAcceptMessage(StreamCursor cursor, out MessageTracker next) =>
        TryAcceptMessage(cursor, out next, out _);

    internal bool TryAcceptMessage(StreamCursor cursor, out MessageTracker next, out MessageTrackerAcceptance acceptance, MessageTrackerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        cursor.ValidateSource();
        settings ??= Settings;
        if (settings.StreamTrackingMode == StreamTrackingMode.StreamPosition && cursor.OutboxToken is not null)
            cursor = cursor with { OutboxToken = null };
        var now = (time ?? settings.TimeProvider ?? TimeProvider.System).GetUtcNow();
        var cutoff = settings.RetentionCutoff(now);
        var current = cutoff is { } expiry ? EvictExpiredEntries(expiry) : this;
        acceptance = new(now, ReferenceEquals(current, this) ? null : cutoff, cursor);
        if (current.TryAcceptStream(cursor, now, out next))
            return true;

        // Existing handlers return immediately on rejection. Cleanup must only become
        // visible with an accepted message whose business state they will persist.
        next = this;
        return false;
    }

    private bool TryAcceptStream(StreamCursor cursor, DateTimeOffset now, out MessageTracker next)
    {
        if (cursor.OutboxToken is not null)
        {
            if (StreamReceipts.ContainsKey(StreamMessageIdentity.From(cursor)))
            {
                // Keep callers' early-return/atomic-write pattern. A newer native position may replay
                // again after activation, but must not repeat the effect of a retained identity.
                next = this;
                return false;
            }
            MessagingTelemetry.RecordStreamReceiveLag(cursor, now);
            next = RecordStreamAcceptance(cursor, now);
            return true;
        }
        if (cursor.Token is null)
        {
            MessagingTelemetry.RecordStreamReceiveLag(cursor, now);
            next = this;
            return true;
        }

        var source = StreamSource.From(cursor);
        if (!streams.TryGetValue(source, out var entry))
        {
            MessagingTelemetry.RecordStreamReceiveLag(cursor, now);
            next = CreateTracker(streams.Add(source, new StreamEntry(cursor, now)), outbox, acceptedAt: now);
            return true;
        }

        if (!IsNewer(cursor.Token, entry.LastPosition.Token))
        {
            next = this;
            return false;
        }

        MessagingTelemetry.RecordStreamReceiveLag(cursor, now);
        next = CreateTracker(streams.SetItem(source, new StreamEntry(cursor, now)), outbox, acceptedAt: now);
        return true;
    }

    /// <summary>
    /// Evaluates a stream message for acceptance. Returns <c>true</c> if the
    /// <paramref name="token"/> is null or advances past the stored high-water mark for
    /// <paramref name="streamNamespace"/>. Tokenless messages do not store a position,
    /// but can remove expired entries when retention is configured.
    /// </summary>
    /// <param name="streamNamespace">The Orleans stream namespace within the grain.</param>
    /// <param name="token">The stream sequence token to evaluate.</param>
    /// <param name="next">
    /// When accepted, a new <see cref="MessageTracker"/> with the updated
    /// position and any retention cleanup. Tokenless messages return <c>this</c>
    /// unless cleanup removes entries. Rejected messages always return <c>this</c>.
    /// </param>
    /// <returns><c>true</c> if accepted, including tokenless messages; <c>false</c> if duplicate or stale.</returns>
    public bool TryAcceptMessage(
        string streamNamespace,
        StreamSequenceToken? token,
        out MessageTracker next)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);

        return TryAcceptMessage(new StreamCursor(streamNamespace, token), out next);
    }

    /// <summary>
    /// Evaluates a provider-qualified stream message for acceptance. Returns
    /// <c>true</c> if the <paramref name="token"/> is null or advances past the stored
    /// high-water mark for the provider and namespace. Tokenless messages do not store
    /// a position, but can remove expired entries when retention is configured.
    /// </summary>
    /// <param name="streamProviderName">The Orleans stream provider name.</param>
    /// <param name="streamNamespace">The Orleans stream namespace within the grain.</param>
    /// <param name="token">The stream sequence token to evaluate.</param>
    /// <param name="next">
    /// When accepted, a new <see cref="MessageTracker"/> with the updated
    /// position and any retention cleanup. Tokenless messages return <c>this</c>
    /// unless cleanup removes entries. Rejected messages always return <c>this</c>.
    /// </param>
    /// <returns><c>true</c> if accepted, including tokenless messages; <c>false</c> if duplicate or stale.</returns>
    public bool TryAcceptMessage(
        string streamProviderName,
        string streamNamespace,
        StreamSequenceToken? token,
        out MessageTracker next)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);

        return TryAcceptMessage(new StreamCursor(streamNamespace, token, streamProviderName), out next);
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
    public bool TryAcceptMessage(OutboxSequenceToken token, out MessageTracker next) =>
        TryAcceptMessage(token, out next, out _);

    internal bool TryAcceptMessage(OutboxSequenceToken token, out MessageTracker next, out MessageTrackerAcceptance acceptance, MessageTrackerSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        settings ??= Settings;
        var now = (time ?? settings.TimeProvider ?? TimeProvider.System).GetUtcNow();
        var cutoff = settings.RetentionCutoff(now);
        var current = cutoff is { } expiry ? EvictExpiredEntries(expiry) : this;
        acceptance = new(now, ReferenceEquals(current, this) ? null : cutoff);
        if (current.TryAcceptOutbox(token, now, out next))
            return true;

        next = this;
        return false;
    }

    private bool TryAcceptOutbox(OutboxSequenceToken token, DateTimeOffset now, out MessageTracker next)
    {
        if (!outbox.TryGetValue(token.Sender, out var entry))
        {
            MessagingTelemetry.RecordOutboxReceiveLag(token, now);
            next = CreateTracker(streams, outbox.Add(token.Sender, new OutboxEntry(token.Epoch, token.SequenceNumber, now, token.Timestamp)), acceptedAt: now);
            return true;
        }

        if (token.Epoch > entry.Epoch)
        {
            MessagingTelemetry.RecordOutboxReceiveLag(token, now);
            next = CreateTracker(streams, outbox.SetItem(token.Sender, new OutboxEntry(token.Epoch, token.SequenceNumber, now, token.Timestamp)), acceptedAt: now);
            return true;
        }

        if (token.Epoch == entry.Epoch && token.SequenceNumber > entry.LastSequenceNumber)
        {
            MessagingTelemetry.RecordOutboxReceiveLag(token, now);
            next = CreateTracker(streams, outbox.SetItem(token.Sender, new OutboxEntry(entry.Epoch, token.SequenceNumber, now, token.Timestamp)), acceptedAt: now);
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
        return UniqueStream(source => source.StreamNamespace == streamNamespace);
    }

    /// <summary>
    /// Returns the last accepted <see cref="StreamSequenceToken"/> for the
    /// given <paramref name="streamNamespace"/>, or <c>null</c> if no token is
    /// tracked for that namespace.
    /// </summary>
    /// <remarks>
    /// A <c>null</c> result can mean either no stream has been tracked or the
    /// latest tracked cursor has a <c>null</c> token. Use
    /// <see cref="LatestStream(string)"/> when callers need to distinguish
    /// those cases.
    /// </remarks>
    public StreamSequenceToken? LatestStreamSequenceToken(string streamNamespace) =>
        LatestStream(streamNamespace)?.Token;

    /// <summary>
    /// Returns the last accepted <see cref="StreamCursor"/> for the given
    /// <paramref name="streamProviderName"/> and
    /// <paramref name="streamNamespace"/>, or <c>null</c> if no matching
    /// stream position has been tracked.
    /// </summary>
    /// <remarks>
    /// Cursors without a provider name are treated as convention-compatible
    /// with the requested provider. This preserves positions produced before
    /// provider-aware stream tracking was available, while avoiding accidental
    /// fallback to a different provider's cursor.
    /// </remarks>
    public StreamCursor? LatestStream(string streamProviderName, string streamNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        if (streams.Keys.Any(source => source.ProviderName == streamProviderName && source.StreamNamespace == streamNamespace))
            return UniqueStream(source => source.ProviderName == streamProviderName && source.StreamNamespace == streamNamespace);
        return UniqueStream(source => source.ProviderName is null && source.StreamNamespace == streamNamespace);
    }

    /// <summary>
    /// Returns the last accepted <see cref="StreamSequenceToken"/> for the
    /// given provider and stream namespace, or <c>null</c> if no token is
    /// tracked for that provider/namespace pair.
    /// </summary>
    /// <remarks>
    /// A <c>null</c> result can mean either no stream has been tracked or the
    /// latest tracked cursor has a <c>null</c> token. Use
    /// <see cref="LatestStream(string, string)"/> when callers need to
    /// distinguish those cases.
    /// </remarks>
    public StreamSequenceToken? LatestStreamSequenceToken(string streamProviderName, string streamNamespace) =>
        LatestStream(streamProviderName, streamNamespace)?.Token;

    /// <summary>
    /// Returns the cursor for this complete <paramref name="stream"/> across providers,
    /// or null when no checkpoint exists or more than one provider matches.
    /// </summary>
    public StreamCursor? LatestStream(StreamId stream) => UniqueStream(source => source.StreamId == stream);

    /// <summary>Returns the checkpoint for exactly this provider and complete stream identity.</summary>
    public StreamCursor? LatestStream(string streamProviderName, StreamId stream)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        return streams.TryGetValue(new StreamSource(stream.GetNamespace()!, streamProviderName, stream), out var entry)
            ? entry.LastPosition : null;
    }

    /// <summary>Returns the native checkpoint for exactly this provider and complete stream identity.</summary>
    public StreamSequenceToken? LatestStreamSequenceToken(string streamProviderName, StreamId stream) => LatestStream(streamProviderName, stream)?.Token;

    private StreamCursor? UniqueStream(Func<StreamSource, bool> matches)
    {
        StreamCursor? result = null;
        foreach (var item in streams)
        {
            if (!matches(item.Key))
                continue;
            if (result is not null)
                return null;
            result = item.Value.LastPosition;
        }
        return result;
    }

    /// <summary>
    /// Returns the last accepted <see cref="OutboxSequenceToken"/> for the
    /// given <paramref name="sender"/>, or <c>null</c> if no outbox messages
    /// from that sender have been tracked.
    /// </summary>
    public OutboxSequenceToken? LatestOutbox(GrainId sender)
    {
        // Reconstruct with the sender-stamped LastTimestamp, not Received:
        // OutboxSequenceToken equality includes Timestamp, so using receiver
        // time would make the returned token differ from the token that was
        // actually accepted whenever sender and receiver clocks drift.
        return outbox.TryGetValue(sender, out var entry)
            ? new OutboxSequenceToken(entry.LastSequenceNumber, sender, entry.LastTimestamp, entry.Epoch)
            : null;
    }

    /// <summary>
    /// Removes all entries (both stream and outbox) where
    /// <c>entry.Received &lt;= <paramref name="olderThan"/></c>.
    /// </summary>
    public MessageTracker Evict(DateTimeOffset olderThan) => EvictSources(static _ => true, olderThan, includeOutboxes: true);

    private MessageTracker EvictExpiredEntries(DateTimeOffset cutoff)
    {
        var bound = retentionBound ??= new RetentionBound(FindOldestReceived());
        if (bound.Received > cutoff)
            return this;

        var next = Evict(cutoff);
        next.retentionBound = new RetentionBound(next.FindOldestReceived());
        return next;
    }

    private sealed record RetentionBound(DateTimeOffset Received);

    private DateTimeOffset FindOldestReceived()
    {
        var oldest = DateTimeOffset.MaxValue;
        foreach (var entry in streams.Values)
            oldest = entry.Received < oldest ? entry.Received : oldest;
        foreach (var received in StreamReceipts.Values)
            oldest = received < oldest ? received : oldest;
        foreach (var entry in outbox.Values)
            oldest = entry.Received < oldest ? entry.Received : oldest;
        return oldest;
    }

    /// <summary>Evicts stream checkpoints and receipts received at or before the cutoff.</summary>
    public MessageTracker EvictStreams(DateTimeOffset olderThan) => EvictSources(static _ => true, olderThan);

    /// <summary>
    /// Evicts only stream receipts accepted at or before the cutoff, preserving stream checkpoints
    /// and RPC positions. Use <see cref="DateTimeOffset.MaxValue"/> when migrating to position tracking.
    /// </summary>
    public MessageTracker EvictStreamReceipts(DateTimeOffset olderThan)
    {
        var remaining = StreamReceipts.RemoveRange(StreamReceipts.Where(item => item.Value <= olderThan).Select(item => item.Key));
        return remaining.Count == StreamReceipts.Count ? this : CreateTracker(streams, outbox, remaining);
    }

    /// <summary>Evicts RPC sender high-water entries; stream receipts are retained.</summary>
    public MessageTracker EvictOutboxes(DateTimeOffset olderThan)
    {
        var remaining = FilterByReceived(outbox, olderThan, static entry => entry.Received, out var changed);
        return changed ? CreateTracker(streams, remaining) : this;
    }

    /// <summary>Evicts all checkpoints and receipts in this namespace across providers.</summary>
    public MessageTracker Evict(string streamNamespace, DateTimeOffset olderThan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamNamespace);
        return EvictSources(source => source.StreamNamespace == streamNamespace, olderThan);
    }

    /// <summary>Evicts this complete stream source across providers, ending deduplication for removed receipts.</summary>
    public MessageTracker Evict(StreamId stream, DateTimeOffset olderThan) => EvictSources(source => source.StreamId == stream, olderThan);

    /// <summary>Evicts this complete stream source within one provider.</summary>
    public MessageTracker Evict(string streamProviderName, StreamId stream, DateTimeOffset olderThan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamProviderName);
        return EvictSources(source => source.StreamId == stream && source.ProviderName == streamProviderName, olderThan);
    }

    private MessageTracker EvictSources(Func<StreamSource, bool> matches, DateTimeOffset olderThan, bool includeOutboxes = false)
    {
        var remainingStreams = streams.RemoveRange(streams.Where(item => matches(item.Key) && item.Value.Received <= olderThan).Select(item => item.Key));
        var remainingReceipts = StreamReceipts.RemoveRange(StreamReceipts.Where(item => matches(item.Key.Source) && item.Value <= olderThan).Select(item => item.Key));
        var remainingOutboxes = includeOutboxes ? FilterByReceived(outbox, olderThan, static entry => entry.Received, out _) : outbox;
        return remainingStreams.Count == streams.Count && remainingReceipts.Count == StreamReceipts.Count && remainingOutboxes.Count == outbox.Count
            ? this : CreateTracker(remainingStreams, remainingOutboxes, remainingReceipts);
    }

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

        if (other is null || streams.Count != other.streams.Count || outbox.Count != other.outbox.Count || StreamReceipts.Count != other.StreamReceipts.Count)
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

        foreach (var item in StreamReceipts)
        {
            if (!other.StreamReceipts.TryGetValue(item.Key, out var received) || received != item.Value)
                return false;
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
            AggregateHash(outbox), StreamReceipts.Count, AggregateHash(StreamReceipts));
    }

    private MessageTracker CreateTracker(
        ImmutableDictionary<StreamSource, StreamEntry> streams,
        ImmutableDictionary<GrainId, OutboxEntry> outbox,
        ImmutableDictionary<StreamMessageIdentity, DateTimeOffset>? receipts = null,
        DateTimeOffset? acceptedAt = null)
    {
        var bound = retentionBound;
        var next = new MessageTracker(streams, outbox, receipts ?? StreamReceipts)
        {
            time = time,
            Configuration = Configuration,
            retentionBound = bound is not null && acceptedAt is { } received
                ? (received < bound.Received ? new RetentionBound(received) : bound) : null
        };

        return next;
    }

    // Replay uses the original acceptance time without receive telemetry. A receipt is independent
    // of provider progress: accepting an unseen older event must never move a checkpoint backwards.
    internal MessageTracker RecordStreamAcceptance(StreamCursor cursor, DateTimeOffset received)
    {
        cursor.ValidateSource();
        var source = StreamSource.From(cursor);
        var updatedStreams = streams;
        if (cursor.Token is not null && (!streams.TryGetValue(source, out var previous) || IsNewer(cursor.Token, previous.LastPosition.Token)))
            updatedStreams = streams.SetItem(source, new StreamEntry(cursor, received));
        var updatedReceipts = cursor.OutboxToken is null ? StreamReceipts : StreamReceipts.SetItem(StreamMessageIdentity.From(cursor), received);
        return CreateTracker(updatedStreams, outbox, updatedReceipts, received);
    }

    // Dispatcher groups can deliver higher sequences before unseen lower ones. Timestamp and trace
    // metadata do not identify a message; neither sender high-water marks nor full token equality is safe.
    [GenerateSerializer]
    internal readonly record struct StreamMessageIdentity(
        [property: Id(0)] StreamSource Source,
        [property: Id(1)] GrainId Sender,
        [property: Id(2)] DateTimeOffset Epoch,
        [property: Id(3)] long SequenceNumber)
    {
        public static StreamMessageIdentity From(StreamCursor cursor) =>
            new(StreamSource.From(cursor), cursor.OutboxToken!.Sender, cursor.OutboxToken.Epoch, cursor.OutboxToken.SequenceNumber);
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
        [property: Id(1)] string? ProviderName,
        [property: Id(2)] StreamId? StreamId = null)
    {
        public static StreamSource From(StreamCursor cursor) =>
            new(
                cursor.StreamNamespace,
                cursor.TryGetProviderName(out var providerName) ? providerName : null, cursor.StreamId);
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
    /// sequence number, the sender-stamped timestamp of the last accepted
    /// token, and the wall-clock time it was received.
    /// </summary>
    /// <remarks>
    /// <c>LastTimestamp</c> preserves the sender's original token timestamp
    /// so <see cref="LatestOutbox"/> can reconstruct the token faithfully.
    /// <c>Received</c> uses the receiver's clock and exists for eviction
    /// policies only — the two differ whenever sender and receiver clocks
    /// drift.
    /// </remarks>
    [GenerateSerializer]
    internal readonly record struct OutboxEntry(
        [property: Id(0)] DateTimeOffset Epoch,
        [property: Id(1)] long LastSequenceNumber,
        [property: Id(2)] DateTimeOffset Received,
        [property: Id(3)] DateTimeOffset LastTimestamp);
}
