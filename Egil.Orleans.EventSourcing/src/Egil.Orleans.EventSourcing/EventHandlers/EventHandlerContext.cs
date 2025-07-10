using Orleans.Runtime;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing.EventHandlers;

internal class EventHandlerContext(IEventStore eventStore, GrainId grainId) : IEventHandlerContext
{
    public GrainId GrainId { get; } = grainId;

    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull => eventStore.AppendEvent(@event);

    public async IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>([EnumeratorCancellation] CancellationToken cancellationToken = default) where TEvent : notnull
    {
        await foreach (var entry in eventStore.GetEventsAsync<TEvent>(QueryOptions.Default, cancellationToken))
        {
            yield return entry.Event;
        }
    }
}