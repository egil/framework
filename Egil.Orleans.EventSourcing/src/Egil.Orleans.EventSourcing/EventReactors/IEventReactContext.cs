using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.EventReactors;

public interface IEventReactContext
{
    GrainId GrainId { get; }

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : notnull;
}