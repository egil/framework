using Egil.Orleans.EventSourcing.Reactors;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Handlers;

internal class EventReactorContext<TProjection>(IEventStore<TProjection> eventStore, GrainId grainId) : IEventReactContext
    where TProjection : notnull, IEventProjection<TProjection>
{
    public GrainId GrainId { get; }
        = grainId;

    public IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions, CancellationToken cancellationToken = default) where TEvent : notnull
        => eventStore.GetEventsAsync<TEvent>(eventQueryOptions, cancellationToken);
}