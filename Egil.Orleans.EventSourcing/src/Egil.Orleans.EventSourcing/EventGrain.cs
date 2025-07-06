using Orleans;
using Egil.Orleans.EventSourcing.Internal;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventBase, TProjection> : Grain
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStorage eventStorage;
    private readonly GrainId grainId;

    protected EventGrain(IEventStorage eventStorage)
    {
        this.eventStorage = eventStorage ?? throw new ArgumentNullException(nameof(eventStorage));
        Projection = TProjection.CreateDefault();
        grainId = this.GetGrainId();
    }

    protected IEventStorage EventStorage => eventStorage;

    protected TProjection Projection { get; set; }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
    }

    protected async Task ProcessEventsAsync(params TEventBase[] events)
    {
        var context = new EventGrainContext<TEventBase>(grainId, eventStorage, GrainFactory);


    }

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : class, TEventBase
    {
        return eventStorage.LoadEventsAsync<TEvent>(grainId, cancellationToken);
    }

    protected static void Configure<TEventGrain>(Action<IEventPartitionBuilder<TEventGrain, TEventBase, TProjection>> builder)
        where TEventGrain : IGrain
    {
        throw new NotImplementedException();
    }
}
