namespace Egil.Orleans.EventSourcing;

public readonly record struct EventQueryOptions
{
    /// <summary>
    /// Return events starting from this sequence number (inclusive).
    /// </summary>
    public readonly long? FromSequenceNumber { get; init; }

    /// <summary>
    /// Return events up to this sequence number (inclusive).
    /// </summary>
    public readonly long? ToSequenceNumber { get; init; }

    /// <summary>
    /// Include uncommitted events in the query results.
    /// </summary>
    public readonly bool? IsUnreacted { get; init; }

    public readonly string? StreamName { get; init; }
}