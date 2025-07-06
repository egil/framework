using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing.Internal;

/// <summary>
/// Default implementation of <see cref="IEventGrainContext"/> used during event processing.
/// It collects events appended by handlers and provides access to the grain id,
/// grain factory and event stream.
/// </summary>
internal sealed class EventGrainContext : IEventGrainContext
{
    private readonly GrainId grainId;
    private readonly IEventStorage storage;
    private readonly IGrainFactory grainFactory;
    private readonly List<object> appendedEvents = new();

    public EventGrainContext(GrainId grainId, IEventStorage storage, IGrainFactory grainFactory)
    {
        this.grainId = grainId;
        this.storage = storage;
        this.grainFactory = grainFactory;
    }

    /// <inheritdoc />
    public void AppendEvent(object @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        appendedEvents.Add(@event);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : class
    {
        return storage.LoadEventsAsync<TEvent>(grainId);
    }

    /// <inheritdoc />
    public GrainId GrainId => grainId;

    /// <inheritdoc />
    public IGrainFactory GrainFactory => grainFactory;

    /// <summary>
    /// Returns and clears all events appended via <see cref="AppendEvent"/>.
    /// </summary>
    internal IReadOnlyList<object> DrainAppendedEvents()
    {
        var events = appendedEvents.ToArray();
        appendedEvents.Clear();
        return events;
    }
}
