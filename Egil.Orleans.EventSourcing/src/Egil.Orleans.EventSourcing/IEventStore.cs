using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Azure Table Storage-based event storage that provides atomic transactions
/// for both event streams/partitions and projections.
/// </summary>
public interface IEventStore
{
    ValueTask<ProjectionEntry<TProjection>> LoadProjectionAsync<TProjection>(GrainId grainId, CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;

    ValueTask<IReadOnlyList<EventEntry<TEvent>>> LoadEventsAsync<TEvent>(GrainId grainId, string streamName, IEventStreamRetention retention, CancellationToken cancellationToken) where TEvent : notnull;
}