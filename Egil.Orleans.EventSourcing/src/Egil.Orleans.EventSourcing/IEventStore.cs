using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Reactors;
using Orleans;

namespace Egil.Orleans.EventSourcing;

public interface IEventStore<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    TProjection Projection { get; }

    bool HasUnappliedEvents { get; }

    bool HasUnreactedEvents { get; }

    void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull;

    ValueTask CommitAsync();

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions options, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    ValueTask ApplyEventsAsync(IEventHandlerContext context, CancellationToken cancellationToken = default);

    ValueTask ReactEventsAsync(IEventReactContext context, CancellationToken cancellationToken = default);

    void Configure<TEventGrain>(TEventGrain eventGrain, IServiceProvider serviceProvider, Action<IEventStoreConfigurator<TEventGrain, TProjection>> builderAction)
        where TEventGrain : IGrainBase;
}
