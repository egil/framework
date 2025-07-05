using Orleans;
using Egil.Orleans.EventSourcing.Internal;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventBase, TProjection> : Grain
    where TEventBase : class
    where TProjection : class, IEventProjection<TProjection>
{
    private readonly IEventStorage eventStorage;
    private readonly ProjectionLoader<TProjection> projectionLoader;
    private readonly GrainId grainId;

    protected EventGrain(IEventStorage eventStorage)
    {
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        projectionLoader = new ProjectionLoader<TProjection>(eventStorage);
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
    }

    protected IEventStorage EventStorage => eventStorage;

    protected TProjection Projection { get; set; }


    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Projection = await projectionLoader.LoadAsync(grainId, cancellationToken) ?? TProjection.CreateDefault();
    }

    protected async Task ProcessEventsAsync(params TEventBase[] events)
    {
        // Apply event handlers to update the projection
        var configuration = EventHandlerRegistry.GetConfiguration(this.GetType());
        if (configuration != null)
        {
            var updatedProjection = await configuration.ProcessEventsAsync(events.Cast<object>(), Projection);
            if (updatedProjection is TProjection typedProjection)
            {
                Projection = typedProjection;
            }
        }

        // Store the events and updated projection atomically
        await eventStorage.SaveAsync(grainId, events, Projection);
    }

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : class, TEventBase
    {
        return eventStorage.LoadEventsAsync<TEvent>(grainId, cancellationToken);
    }

    protected static void Configure<TEventGrain>(Action<IEventPartitonBuilder<TEventGrain, TEventBase, TProjection>> builder)
    {
        // Create the real configuration system
        var configuration = new GrainEventConfiguration<TEventBase, TProjection>();
        var realBuilder = new EventPartitionBuilder<TEventGrain, TEventBase, TProjection>(configuration);

        // Execute the builder to configure partitions and handlers
        builder(realBuilder);

        // Register the configuration in the registry
        EventHandlerRegistry.RegisterConfiguration<TEventGrain>(configuration);
    }
}