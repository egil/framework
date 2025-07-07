using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Azure Table Storage-based event storage that provides atomic transactions
/// for both event streams/partitions and projections.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Loads a projection from Azure Table Storage for the specified grain.
    /// </summary>
    /// <typeparam name="TProjection">The type of projection to load.</typeparam>
    /// <param name="grainId">The unique identifier of the grain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded projection, or null if none exists.</returns>
    ValueTask<TProjection?> LoadProjectionAsync<TProjection>(string streamName, GrainId grainId, CancellationToken cancellationToken = default)
        where TProjection : notnull;

    /// <summary>
    /// Loads events from the event stream for the specified grain and event type.
    /// </summary>
    /// <typeparam name="TEvent">The type of events to load.</typeparam>
    /// <param name="grainId">The unique identifier of the grain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of events from the stream.</returns>
    IAsyncEnumerable<TEvent> LoadEventsAsync<TEvent>(string streamName, GrainId grainId, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    /// Saves both events and projection to Azure Table Storage within a single transaction.
    /// This ensures that either both the events and projection are saved, or neither are saved.
    /// </summary>
    /// <typeparam name="TProjection">The type of projection to save.</typeparam>
    /// <param name="grainId">The unique identifier of the grain.</param>
    /// <param name="events">The events to append to the stream.</param>
    /// <param name="projection">The updated projection to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the atomic operation.</returns>
    ValueTask SaveAsync<TEvent, TProjection>(
        string streamName,
        GrainId grainId,
        IEnumerable<TEvent> events,
        TProjection projection,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
        where TProjection : notnull;
}