using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Handlers;

internal class ReplayEventHandlerContext<TProjection>(IEventStore<TProjection> eventStore, GrainId grainId) : IEventHandlerContext
    where TProjection : notnull, IEventProjection<TProjection>
{
    public GrainId GrainId { get; }
        = grainId;

    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
    {
        // Do not allow event appending in replay event handler context.
    }

    public IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions, CancellationToken cancellationToken = default) where TEvent : notnull
        => eventStore.GetEventsAsync<TEvent>(eventQueryOptions, cancellationToken);
}