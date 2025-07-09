using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.EventStores;

public interface IEventHandlerContext
{
    GrainId GrainId { get; }

    void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull;

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : notnull;
}
