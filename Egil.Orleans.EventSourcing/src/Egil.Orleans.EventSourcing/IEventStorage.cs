namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Represents an append-only log storage for event sourcing.
/// </summary>
public interface IEventStorage<TEvent>
{
    /// <summary>
    /// Appends a single event to the log.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the event could not be appended.</exception>
    ValueTask<int> AppendEventAsync(TEvent @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple events to the log.
    /// </summary>
    /// <remarks>
    /// It is expected that either all events are appended atomically or an exception is thrown.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if not all events could be appended.</exception>
    ValueTask<int> AppendEventsAsync(IEnumerable<TEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads <typeparamref name="TEvent"/>s from <paramref name="fromVersion"/>.
    /// </summary>
    IAsyncEnumerable<TEvent> ReadEventsAsync(int fromVersion = 0, CancellationToken cancellationToken = default);
}