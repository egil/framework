using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Reactors;
using Egil.Orleans.EventSourcing.Storage;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain
    where TEventGrain : EventGrain<TEventGrain, TProjection>, IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStore<TProjection> eventStore;

    protected IEventStore<TProjection> EventStore => eventStore;

    protected TProjection Projection => eventStore.Projection;

    protected EventGrain()
    {
        eventStore = ServiceProvider
            .GetRequiredService<IEventStoreFactory>()
            .CreateEventStore<TProjection>(ServiceProvider);

        eventStore.Configure(this.GetGrainId(), (TEventGrain)this, ServiceProvider, Configure);

        if (eventStore is ILifecycleParticipant<IGrainLifecycle> participant)
        {
            participant.Participate(((IGrainBase)this).GrainContext.ObservableLifecycle);
        }
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

    protected IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions eventQueryOptions = default, CancellationToken cancellationToken = default) where TEvent : notnull
        => eventStore.GetEventsAsync<TEvent>(eventQueryOptions, cancellationToken);
}
