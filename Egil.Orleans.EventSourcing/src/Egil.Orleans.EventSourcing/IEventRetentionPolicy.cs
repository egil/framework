namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Interface for retention policies that determine which events should be kept.
/// </summary>
public interface IEventRetentionPolicy
{
    /// <summary>
    /// Determines if an event should be retained based on this policy.
    /// </summary>
    bool ShouldRetain(StoredEvent storedEvent, IReadOnlyList<StoredEvent> allEventsInStream);
}

/// <summary>
/// Factory for creating common retention policies.
/// </summary>
public static class EventRetentionPolicies
{
    /// <summary>
    /// Keep all events (no cleanup).
    /// </summary>
    public static IEventRetentionPolicy KeepAll() => new KeepAllPolicy();

    /// <summary>
    /// Keep only events newer than the specified time span.
    /// </summary>
    public static IEventRetentionPolicy KeepRecent(TimeSpan maxAge) => new TimeBasedPolicy(maxAge);

    /// <summary>
    /// Keep only the latest N events.
    /// </summary>
    public static IEventRetentionPolicy KeepLatest(int count) => new CountBasedPolicy(count);

    /// <summary>
    /// Keep only the latest event per deduplication ID.
    /// </summary>
    public static IEventRetentionPolicy KeepLatestPerDeduplicationKey() => new LatestPerIdPolicy();

    private sealed record KeepAllPolicy() : IEventRetentionPolicy
    {
        public bool ShouldRetain(StoredEvent storedEvent, IReadOnlyList<StoredEvent> allEventsInStream) => true;
    }

    private sealed record TimeBasedPolicy(TimeSpan MaxAge) : IEventRetentionPolicy
    {
        public bool ShouldRetain(StoredEvent storedEvent, IReadOnlyList<StoredEvent> allEventsInStream) 
            => DateTime.UtcNow - storedEvent.Timestamp <= MaxAge;
    }

    private sealed record CountBasedPolicy(int MaxCount) : IEventRetentionPolicy
    {
        public bool ShouldRetain(StoredEvent storedEvent, IReadOnlyList<StoredEvent> allEventsInStream)
        {
            var orderedEvents = allEventsInStream.OrderByDescending(e => e.SequenceNumber).Take(MaxCount);
            return orderedEvents.Contains(storedEvent);
        }
    }

    private sealed record LatestPerIdPolicy() : IEventRetentionPolicy
    {
        public bool ShouldRetain(StoredEvent storedEvent, IReadOnlyList<StoredEvent> allEventsInStream)
        {
            if (string.IsNullOrEmpty(storedEvent.DeduplicationId))
                return true;

            var latestForId = allEventsInStream
                .Where(e => e.DeduplicationId == storedEvent.DeduplicationId)
                .OrderByDescending(e => e.SequenceNumber)
                .FirstOrDefault();

            return latestForId?.SequenceNumber == storedEvent.SequenceNumber;
        }
    }
}
