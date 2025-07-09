using Orleans;

namespace Egil.Orleans.EventSourcing.EventStores;

public interface IEventStore<TEventGrain, TProjection>
    where TEventGrain : IGrainBase
    where TProjection : notnull
{
    bool HasUncommittedEvents { get; }

    bool HasUnreactedEvents { get; }

    ValueTask CommitAsync();

    void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull;

    ValueTask<TProjection> ApplyEventsAsync(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default);

    ValueTask ReactEventsAsync(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default);

    void Configure(TEventGrain eventGrain, IServiceProvider serviceProvider, IEventStoreConfigurator<TEventGrain, TProjection> builder);
}