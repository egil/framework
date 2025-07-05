using Orleans;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventBase, TProjection> : Grain
    where TProjection : class, IEventProjection<TProjection>
{
    protected static void Configure<TEventGrain>(Action<IEventPartitonBuilder<TEventGrain, TEventBase, TProjection>> builder)
    {
        throw new NotImplementedException("Event partition configuration is not implemented yet. Use the static Configure method in derived classes.");
    }

    protected TProjection Projection { get; private set; } = TProjection.CreateDefault();
    protected IEventStorage EventStorage { get; }

    protected EventGrain(IEventStorage eventStorage)
    {
        EventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
    }

    /// <summary>
    /// Reads the current event partition of <typeparamref name="TEvent"/> events.
    /// </summary>
    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : notnull, TEventBase
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Processes a batch of events asynchronously.
    /// When this method returns, all events in the batch has been saved to grains event storage
    /// and any event handlers will have completed running, including for events that may have been
    /// created by an event handler.
    ///
    /// Publishing of events happens after all event handlers have run through all events in the batch.
    /// The publising may fail and the method will still complete successfully. In that case, the events
    /// that require publishing will be retried asynchronusly in the background until they succeed or
    /// are removed from the event partition.
    /// </summary>
    protected ValueTask ProcessEventsAsync(params ReadOnlySpan<TEventBase> events)
    {
        throw new NotImplementedException();
    }
}
