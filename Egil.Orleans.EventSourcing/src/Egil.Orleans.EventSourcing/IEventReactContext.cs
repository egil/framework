using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

public interface IEventReactContext
{
    GrainId GrainId { get; }

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : notnull;
}