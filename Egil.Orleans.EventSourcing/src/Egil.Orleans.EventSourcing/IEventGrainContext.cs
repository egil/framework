using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

public interface IEventGrainContext<TEventBase>
    where TEventBase : notnull
{
    /// <summary>
    /// Appends an event to the grain's event stream.
    /// This allows event handlers to append events that will be persisted and processed by the grain.
    /// </summary>
    void AppendEvent(TEventBase @event);

    /// <summary>
    /// Reads the current event partition of <typeparamref name="TEvent"/> events.
    /// This allows event handlers to read the current state of the partition and process events accordingly.
    /// </summary>
    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : TEventBase;

    /// <summary>
    /// The ID of the grain that owns the event being processed.
    /// </summary>
    GrainId GrainId { get; }

    IGrainFactory GrainFactory { get; }
}
