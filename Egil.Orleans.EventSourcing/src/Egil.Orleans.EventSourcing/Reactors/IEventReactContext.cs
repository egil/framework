using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Reactors;

public interface IEventReactContext
{
    GrainId GrainId { get; }

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions, CancellationToken cancellationToken = default) where TEvent : notnull;
}