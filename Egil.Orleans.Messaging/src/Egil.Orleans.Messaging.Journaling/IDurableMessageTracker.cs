using Egil.Orleans.Messaging.Outboxes;
using Egil.Orleans.Messaging.Streams;
using Egil.Orleans.Messaging.Tracking;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Journaling;

/// <summary>
/// The tracker's receive, lookup, and eviction API over a registered journal component.
/// Mutations stage changes in this component; eviction results and receive out parameters
/// refer to this same component. Use the immutable snapshots for value equality.
/// </summary>
public interface IDurableMessageTracker
{
    /// <summary>Returns the internally held immutable value without copying, including unsaved changes.</summary>
    MessageTracker AsImmutable();
    /// <summary>Sets the clock used for future receive timestamps; existing timestamps are unchanged.</summary>
    void RegisterTimeProvider(TimeProvider time);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions.</summary>
    bool TryAcceptMessage(OutboxSequenceToken token);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions.</summary>
    bool TryAcceptMessage(StreamCursor cursor);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions.</summary>
    bool TryAcceptMessage(string streamNamespace, StreamSequenceToken? token);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions.</summary>
    bool TryAcceptMessage(string streamProviderName, string streamNamespace, StreamSequenceToken? token);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions. The out parameter returns this component.</summary>
    bool TryAcceptMessage(OutboxSequenceToken token, out IDurableMessageTracker next);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions. The out parameter returns this component.</summary>
    bool TryAcceptMessage(StreamCursor cursor, out IDurableMessageTracker next);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions. The out parameter returns this component.</summary>
    bool TryAcceptMessage(string streamNamespace, StreamSequenceToken? token, out IDurableMessageTracker next);
    /// <summary>Accepts a new position and stages it for journaling; rejects duplicates or stale positions. The out parameter returns this component.</summary>
    bool TryAcceptMessage(string streamProviderName, string streamNamespace, StreamSequenceToken? token, out IDurableMessageTracker next);
    /// <summary>Returns the latest matching stream cursor using the immutable tracker lookup rules.</summary>
    StreamCursor? LatestStream(string streamNamespace);
    /// <summary>Returns the latest tracked stream token, or null when no matching position is available.</summary>
    StreamSequenceToken? LatestStreamSequenceToken(string streamNamespace);
    /// <summary>Returns the latest matching stream cursor using the immutable tracker lookup rules.</summary>
    StreamCursor? LatestStream(string streamProviderName, string streamNamespace);
    /// <summary>Returns the latest tracked stream token, or null when no matching position is available.</summary>
    StreamSequenceToken? LatestStreamSequenceToken(string streamProviderName, string streamNamespace);
    /// <summary>Returns the latest matching stream cursor using the immutable tracker lookup rules.</summary>
    StreamCursor? LatestStream(StreamId stream);
    /// <summary>Returns the latest accepted position for the specified outbox sender.</summary>
    OutboxSequenceToken? LatestOutbox(GrainId sender);
    /// <summary>Evicts stream and outbox entries received at or before the cutoff and returns this component.</summary>
    IDurableMessageTracker Evict(DateTimeOffset olderThan);
    /// <summary>Evicts stream entries received at or before the cutoff and returns this component.</summary>
    IDurableMessageTracker EvictStreams(DateTimeOffset olderThan);
    /// <summary>Evicts outbox sender entries received at or before the cutoff and returns this component.</summary>
    IDurableMessageTracker EvictOutboxes(DateTimeOffset olderThan);
    /// <summary>Evicts entries in the specified namespace across providers received at or before the cutoff and returns this component.</summary>
    IDurableMessageTracker Evict(string streamNamespace, DateTimeOffset olderThan);
    /// <summary>Evicts entries in the specified stream namespace received at or before the cutoff and returns this component.</summary>
    IDurableMessageTracker Evict(StreamId stream, DateTimeOffset olderThan);
    /// <summary>Evicts entries for the specified outbox sender received at or before the cutoff and returns this component.</summary>
    IDurableMessageTracker Evict(GrainId sender, DateTimeOffset olderThan);
}
