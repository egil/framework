using Egil.Orleans.EventSourcing.EventHandlers;
using Egil.Orleans.EventSourcing.EventStores;
using Orleans;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing;

/// <summary>
/// Base class for event-sourced grains.
/// </summary>
public abstract class EventGrain<TEventGrain, TProjection> : Grain
    where TEventGrain : EventGrain<TEventGrain, TProjection>, IGrainBase
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly IEventStore<TProjection> eventStore;

    protected IEventStore EventStorage => eventStore;

    protected TProjection Projection => eventStore.Projection;

    protected EventGrain(IEventStore<TProjection> eventStore)
    {
        this.eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        eventStore.Configure<TEventGrain, TProjection>((TEventGrain)this, ServiceProvider, Configure);
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
        if (!eventStore.HasUncommittedEvents)
            return;

        while (eventStore.HasUncommittedEvents)
        {
            await eventStore.ApplyEventsAsync(Projection, new EventHandlerContext(eventStore, this.GetGrainId()));
        }

        await eventStore.CommitAsync();
    }

    protected async ValueTask ReactEventsAsync()
    {
        if (!eventStore.HasUnreactedEvents)
            return;

        await eventStore.ReactEventsAsync();
    }

    protected async IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>([EnumeratorCancellation] CancellationToken cancellationToken = default) where TEvent : notnull
    {
        await foreach (var entry in eventStore.GetEventsAsync<TEvent>(QueryOptions.Default, cancellationToken))
        {
            yield return entry.Event;
        }
    }
}
