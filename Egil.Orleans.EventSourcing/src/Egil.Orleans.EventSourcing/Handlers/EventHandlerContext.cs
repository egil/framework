using Orleans.Runtime;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing.Handlers;

internal class EventHandlerContext<TProjection>(IEventStore<TProjection> eventStore, GrainId grainId) : IEventHandlerContext
    where TProjection : notnull, IEventProjection<TProjection>
{
    public GrainId GrainId { get; }
        = grainId;

    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
        => eventStore.AppendEvent(@event);

    public IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions, CancellationToken cancellationToken = default) where TEvent : notnull
        => eventStore.GetEventsAsync<TEvent>(eventQueryOptions, cancellationToken);
}