using Orleans;
using Egil.Orleans.EventSourcing.Internal;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TEventBase, TProjection> : Grain
    where TEventGrain : EventGrain<TEventGrain, TEventBase, TProjection>
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStorage eventStorage;
    private readonly GrainId grainId;
    private readonly IEventPartition<TEventGrain>[] partitions;

    protected EventGrain(IEventStorage eventStorage)
    {
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
        var builder = new EventPartitionBuilder<TEventGrain, TEventBase, TProjection>();
        Configure(builder);
        partitions = builder.Build();
    }

    protected IEventStorage EventStorage => eventStorage;

    protected TProjection Projection { get; set; }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
    }

    protected async Task ProcessEventAsync<TEvent>(TEvent @event) where TEvent : TEventBase
    {
        var context = new EventGrainContext(grainId, eventStorage, GrainFactory);

        foreach (var partition in partitions)
        {
            if (partition is not IEventPartition<TEventGrain, TEvent> compatiblePartition)
            {
                continue;
            }

            foreach (var handlerFactory in compatiblePartition.Handlers)
            {
                var grain = (TEventGrain)this;
                var handler = handlerFactory.Create(grain, ServiceProvider);
                var compatibleHandler = (IEventHandler<TEvent, TProjection>)handler;
                Projection = await compatibleHandler.HandleAsync(@event, Projection, context);
            }
        }
    }

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : class, TEventBase
    {
        return eventStorage.LoadEventsAsync<TEvent>(grainId, cancellationToken);
    }

    protected abstract void Configure(IEventPartitionBuilder<TEventGrain, TEventBase, TProjection> builder);

    //protected static void Configure<TEventGrain>(Action<IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>> builderAction)
    //    where TEventGrain : IGrain
    //{
    //    var builder = new EventPartitionBuilder<TEventGrain, TEventBase, TProjection>();
    //    builderAction(builder);
    //}
}
