using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.EventStores;

public interface IEventReactContext
{
    GrainId GrainId { get; }

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : notnull;
}