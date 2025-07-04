namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Interface for event storage operations.
/// </summary>
public interface IEventStorage
{
    /// <summary>
    /// Appends an event to the specified stream.
    /// </summary>
    Task<StoredEvent> AppendEventAsync(string grainId, string streamName, object @event, string? deduplicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all events for a grain across all streams, ordered by sequence number.
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string grainId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events for a specific stream of a grain.
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> GetEventsAsync(string grainId, string streamName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events for a grain starting from a specific sequence number.
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> GetEventsFromSequenceAsync(string grainId, long fromSequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes events from storage based on the provided event IDs.
    /// </summary>
    Task RemoveEventsAsync(string grainId, IEnumerable<(string streamName, long sequenceNumber)> eventsToRemove, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks events as handled in a batch operation.
    /// </summary>
    Task MarkEventsAsHandledAsync(string grainId, IEnumerable<(string streamName, long sequenceNumber)> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds an existing event by deduplication ID in the specified stream.
    /// </summary>
    Task<StoredEvent?> FindEventByDeduplicationIdAsync(string grainId, string streamName, string deduplicationId, CancellationToken cancellationToken = default);
}