using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Internal;

/// <summary>
/// Default implementation of <see cref="IEventGrainContext{TEvent}"/> used during event processing.
/// It collects events appended by handlers and provides access to the grain id,
/// grain factory and event stream.
/// </summary>
internal sealed class EventGrainContext<TEventBase>(GrainId grainId, IEventStorage storage, IGrainFactory grainFactory) : IEventGrainContext<TEventBase>
    where TEventBase : notnull
{
    private List<TEventBase>? appendedEvents;

    /// <inheritdoc />
    public void AppendEvent(TEventBase @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        appendedEvents ??= new List<TEventBase>();
        appendedEvents.Add(@event);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : TEventBase
    {
        return storage.LoadEventsAsync<TEvent>(grainId);
    }

    /// <inheritdoc />
    public GrainId GrainId => grainId;

    /// <inheritdoc />
    public IGrainFactory GrainFactory => grainFactory;
}
