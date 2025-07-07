using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Internal;

/// <summary>
/// Default implementation of <see cref="IEventHandlerContext"/> used during event processing.
/// It collects events appended by handlers and provides access to the grain id,
/// grain factory and event stream.
/// </summary>
internal sealed class EventContext(GrainId grainId, IEventStore storage, IGrainFactory grainFactory) : IEventHandlerContext, IEventReactContext
{
    private List<object>? appendedEvents;

    /// <inheritdoc />
    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        appendedEvents ??= new List<object>();
        appendedEvents.Add(@event);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : notnull
    {
        return storage.LoadEventsAsync<TEvent>(grainId);
    }

    /// <inheritdoc />
    public GrainId GrainId => grainId;

    /// <inheritdoc />
    public IGrainFactory GrainFactory => grainFactory;
}
