namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Interface for outbox storage operations.
/// </summary>
public interface IOutboxStorage
{
    /// <summary>
    /// Adds events to the outbox.
    /// </summary>
    Task AddOutboxEventsAsync(string grainId, IEnumerable<OutboxEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves pending outbox events for a grain.
    /// </summary>
    Task<IReadOnlyList<OutboxEvent>> GetPendingOutboxEventsAsync(string grainId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes successfully published events from the outbox.
    /// </summary>
    Task RemoveOutboxEventsAsync(string grainId, IEnumerable<string> outboxEventIds, CancellationToken cancellationToken = default);
}
