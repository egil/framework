using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

public interface IEventHandlerContext
{
    GrainId GrainId { get; }

    void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull;

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default) where TEvent : notnull;
}
