using Orleans;
using Egil.Orleans.EventSourcing.Internal;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStorage eventStorage;
    private readonly GrainId grainId;
    private readonly IEventPartition<TProjection>[] partitions;

    protected EventGrain(IEventStorage eventStorage)
    {
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
        var builder = new EventPartitionBuilder<TEventGrain, TProjection>();
        Configure(builder);
        partitions = builder.Build();
    }

    protected IEventStorage EventStorage => eventStorage;

    protected TProjection Projection { get; set; }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
    }

    protected async Task ProcessEventAsync<TEvent>(TEvent @event) where TEvent : notnull
    {
        var partition = FindPartition<TEvent>(@event);
        var context = new EventGrainContext(grainId, eventStorage, GrainFactory);
        
        foreach (var handlerFactory in partition.Handlers)
        {
            if (handlerFactory.TryCreate(@event, this, ServiceProvider) is { } handler)
            {
                Projection = await handler.HandleAsync(@event, Projection, context);
            }
        }
    }

    private IEventPartition<TProjection> FindPartition<TEvent>(TEvent @event) where TEvent : notnull
    {
        IEventPartition<TProjection>? result = null;

        foreach (var partition in partitions)
        {
            if (partition.TryCast(@event) is { } compatiblePartition)
            {
                if (result is null)
                {
                    result = compatiblePartition;
                }
                else
                {
                    throw new InvalidOperationException($"Multiple partitions found for event type {typeof(TEvent).Name}. Ensure only one partition handles this event type.");
                }
            }
        }

        if (result is null)
        {
            throw new InvalidOperationException($"No partition found for event type {typeof(TEvent).Name}. Ensure a partition is configured to handle this event type.");
        }

        return result;
    }

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        return eventStorage.LoadEventsAsync<TEvent>(grainId, cancellationToken);
    }

    protected abstract void Configure(IEventPartitionBuilder<TEventGrain, TProjection> builder);

    //protected static void Configure<TEventGrain>(Action<IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>> builderAction)
    //    where TEventGrain : IGrain
    //{
    //    var builder = new EventPartitionBuilder<TEventGrain, TEventBase, TProjection>();
    //    builderAction(builder);
    //}
}
