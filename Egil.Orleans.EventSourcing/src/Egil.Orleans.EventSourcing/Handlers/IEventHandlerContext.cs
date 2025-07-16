using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Handlers;

public interface IEventHandlerContext
{
    GrainId GrainId { get; }

    void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull;

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions, CancellationToken cancellationToken = default) where TEvent : notnull;
}
