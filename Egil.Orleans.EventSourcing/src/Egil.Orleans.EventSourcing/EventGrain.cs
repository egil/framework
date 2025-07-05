using Orleans;
using Egil.Orleans.EventSourcing.Internal;

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
    }    protected TProjection Projection { get; private set; } = TProjection.CreateDefault();

    protected IEventStorage EventStorage { get; }
    private readonly IProjectionLoader<TProjection> projectionLoader;

    protected EventGrain(IEventStorage eventStorage)
    {
        EventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        projectionLoader = new ProjectionLoader<TProjection>(eventStorage);
    }

    /// <summary>
    /// Called when the grain is activated. Loads the projection from storage.
    /// </summary>
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);

        // Get grain ID safely, fallback to type name for unit tests
        string grainId;
        try
        {
            grainId = this.GetGrainId().ToString();
        }
        catch
        {
            // Fallback for unit tests where Orleans runtime context is not available
            grainId = this.GetType().Name;
        }

        var loadedProjection = await projectionLoader.LoadAsync(grainId, cancellationToken);

        // If no projection exists in storage, keep the default one
        Projection = loadedProjection ?? TProjection.CreateDefault();
    }

    /// <summary>
    /// Reads the current event partition of <typeparamref name="TEvent"/> events.
    /// </summary>
    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>() where TEvent : class, TEventBase
    {
        // Get grain ID safely, fallback to type name for unit tests
        string grainId;
        try
        {
            grainId = this.GetGrainId().ToString();
        }
        catch
        {
            // Fallback for unit tests where Orleans runtime context is not available
            grainId = this.GetType().Name;
        }

        return EventStorage.LoadEventsAsync<TEvent>(grainId);
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
    protected async ValueTask ProcessEventsAsync(params TEventBase[] events)
    {
        if (events.Length == 0)
            return;

        // Get grain ID safely, fallback to type name for unit tests
        string grainId;
        try
        {
            grainId = this.GetGrainId().ToString();
        }
        catch
        {
            // Fallback for unit tests where Orleans runtime context is not available
            grainId = this.GetType().Name;
        }

        // Convert events to list for storage
        var eventsList = new List<object>();
        foreach (var @event in events)
        {
            if (@event is not null)
            {
                eventsList.Add(@event);
            }
        }

        // Save events and projection atomically
        await EventStorage.SaveAsync(grainId, eventsList, Projection);
    }
}
