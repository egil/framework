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

    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions options, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    ValueTask ApplyEventsAsync(IEventHandlerContext context, CancellationToken cancellationToken = default);

    ValueTask ReactEventsAsync(IEventReactContext context, CancellationToken cancellationToken = default);

    ValueTask InitializeAsync<TEventGrain>(TEventGrain eventGrain, IServiceProvider serviceProvider, Action<IEventStoreConfigurator<TEventGrain, TProjection>> builderAction, CancellationToken cancellationToken = default)
        where TEventGrain : IGrainBase;
}
