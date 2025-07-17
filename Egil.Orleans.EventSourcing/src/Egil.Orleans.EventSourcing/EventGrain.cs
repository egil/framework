using Egil.Orleans.EventSourcing.Handlers;
using Orleans;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain
    where TEventGrain : EventGrain<TEventGrain, TProjection>, IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStore<TProjection> eventStore;

    protected IEventStore<TProjection> EventStorage => eventStore;

    protected TProjection Projection => eventStore.Projection;

    protected EventGrain(IEventStore<TProjection> eventStore)
    {
        this.eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await eventStore.InitializeAsync((TEventGrain)this, ServiceProvider, Configure);
    }

    protected abstract void Configure(IEventStoreConfigurator<TEventGrain, TProjection> builder);

    protected void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
        => eventStore.AppendEvent(@event);

    protected async ValueTask ProcessEventAsync<TEvent>(TEvent @event) where TEvent : notnull
    {
        eventStore.AppendEvent(@event);
        await ProcessEventsAsync();
    }

    protected async ValueTask ProcessEventsAsync()
    {
        await ApplyEventsAsync();
        await ReactEventsAsync();
    }

    protected async ValueTask ApplyEventsAsync()
    {
        if (!eventStore.HasUnappliedEvents)
            return;

        while (eventStore.HasUnappliedEvents)
        {
            await eventStore.ApplyEventsAsync(new EventHandlerContext<TProjection>(eventStore, this.GetGrainId()), CancellationToken.None);
        }

        await eventStore.CommitAsync();
    }

    protected async ValueTask ReactEventsAsync()
    {
        if (!eventStore.HasUnreactedEvents)
            return;

        await eventStore.ReactEventsAsync(new EventReactorContext<TProjection>(eventStore, this.GetGrainId()), CancellationToken.None);
        await eventStore.CommitAsync();
    }

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions, CancellationToken cancellationToken = default) where TEvent : notnull
        => eventStore.GetEventsAsync<TEvent>(eventQueryOptions, cancellationToken);
}
