namespace Egil.Orleans.EventSourcing.Internal;

internal class EventQueryOptions
{
    /// <summary>
    /// Maximum number of events to return.
    /// </summary>
    public int? MaxCount { get; init; }

    /// <summary>
    /// Return events with timestamp younger than this age.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// Filter by specific event ID.
    /// </summary>
    public string? EventId { get; init; }

    /// <summary>
    /// Return only distinct events by EventId (keeps latest).
    /// </summary>
    public bool DistinctByEventId { get; init; }

    /// <summary>
    /// Return events starting from this sequence number (inclusive).
    /// </summary>
    public long? FromSequenceNumber { get; init; }

    /// <summary>
    /// Return events up to this sequence number (inclusive).
    /// </summary>
    public long? ToSequenceNumber { get; init; }
}