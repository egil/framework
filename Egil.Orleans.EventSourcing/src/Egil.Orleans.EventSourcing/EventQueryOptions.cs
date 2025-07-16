namespace Egil.Orleans.EventSourcing;

public readonly record struct EventQueryOptions
{
    /// <summary>
    /// Maximum number of events to return.
    /// </summary>
    public readonly int? MaxCount { get; init; }

    /// <summary>
    /// Return events with timestamp younger than this age.
    /// </summary>
    public readonly TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// Filter by specific event ID.
    /// </summary>
    public readonly string? EventId { get; init; }

    /// <summary>
    /// Return only distinct events by EventId (keeps latest).
    /// </summary>
    public readonly bool DistinctByEventId { get; init; }

    /// <summary>
    /// Return events starting from this sequence number (inclusive).
    /// </summary>
    public readonly long? FromSequenceNumber { get; init; }

    /// <summary>
    /// Return events up to this sequence number (inclusive).
    /// </summary>
    public readonly long? ToSequenceNumber { get; init; }

    /// <summary>
    /// Return events starting from this sequence number (inclusive).
    /// </summary>
    public readonly long? FromTimestamp { get; init; }

    /// <summary>
    /// Return events up to this sequence number (inclusive).
    /// </summary>
    public readonly long? ToTimestamp { get; init; }

    /// <summary>
    /// Include uncommitted events in the query results.
    /// </summary>
    public readonly bool IncludeUncommitted { get; init; } = true;

    /// <summary>
    /// Include uncommitted events in the query results.
    /// </summary>
    public readonly bool? IncludeUnreacted { get; init; }
}