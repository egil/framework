using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Internals;

/// <summary>
/// Default implementation of <see cref="IEventHandlerContext"/> used during event processing.
/// It collects events appended by handlers and provides access to the grain id,
/// grain factory and event stream.
/// </summary>
internal sealed class EventContext(GrainId grainId, IEventGrain eventGrain, IGrainFactory grainFactory) : IEventHandlerContext, IEventReactContext
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
        return eventGrain.GetEventsAsync<TEvent>();
    }

    /// <inheritdoc />
    public GrainId GrainId => grainId;

    /// <inheritdoc />
    public IGrainFactory GrainFactory => grainFactory;
}
