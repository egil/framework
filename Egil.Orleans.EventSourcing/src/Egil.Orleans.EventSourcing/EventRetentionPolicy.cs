namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Defines retention policies for event streams.
/// </summary>
public abstract class EventRetentionPolicy
{
    /// <summary>
    /// Keep all events (no cleanup).
    /// </summary>
    public static EventRetentionPolicy KeepAll() => new KeepAllPolicy();

    /// <summary>
    /// Keep only events newer than the specified time span.
    /// </summary>
    public static EventRetentionPolicy KeepRecent(TimeSpan maxAge) => new TimeBasedPolicy(maxAge);

    /// <summary>
    /// Keep only the latest N events.
    /// </summary>
    public static EventRetentionPolicy KeepLatest(int count) => new CountBasedPolicy(count);

    /// <summary>
    /// Keep only the latest event per ID (requires deduplication to be enabled).
    /// </summary>
    public static EventRetentionPolicy KeepLatestPerDeduplicationKey() => new LatestPerIdPolicy();

    /// <summary>
    /// Determines if an event should be retained based on this policy.
    /// </summary>
    public abstract bool ShouldRetain(StoredEvent storedEvent, IEnumerable<StoredEvent> allEventsInStream);

    private sealed class KeepAllPolicy : EventRetentionPolicy
    {
        public override bool ShouldRetain(StoredEvent storedEvent, IEnumerable<StoredEvent> allEventsInStream) => true;
    }

    private sealed class TimeBasedPolicy : EventRetentionPolicy
    {
        private readonly TimeSpan maxAge;
        public TimeBasedPolicy(TimeSpan maxAge) => this.maxAge = maxAge;
        public override bool ShouldRetain(StoredEvent storedEvent, IEnumerable<StoredEvent> allEventsInStream) 
            => DateTime.UtcNow - storedEvent.Timestamp <= maxAge;
    }

    private sealed class CountBasedPolicy : EventRetentionPolicy
    {
        private readonly int maxCount;
        public CountBasedPolicy(int maxCount) => this.maxCount = maxCount;
        public override bool ShouldRetain(StoredEvent storedEvent, IEnumerable<StoredEvent> allEventsInStream)
        {
            var orderedEvents = allEventsInStream.OrderByDescending(e => e.SequenceNumber).Take(maxCount);
            return orderedEvents.Contains(storedEvent);
        }
    }

    private sealed class LatestPerIdPolicy : EventRetentionPolicy
    {
        public override bool ShouldRetain(StoredEvent storedEvent, IEnumerable<StoredEvent> allEventsInStream)
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
