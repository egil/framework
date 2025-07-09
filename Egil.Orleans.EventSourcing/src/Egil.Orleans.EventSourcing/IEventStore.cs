using Egil.Orleans.EventSourcing.EventStores;
using Orleans;

namespace Egil.Orleans.EventSourcing;

public interface IEventStore
{
    bool HasUncommittedEvents { get; }

    bool HasUnreactedEvents { get; }

    long EventCount { get; }

    long? LatestSequenceNumber { get; }

    DateTimeOffset? LatestEventTimestamp { get; }

    void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull;

    IEventStream<TEvent> GetStream<TEvent>() where TEvent : notnull;

    ValueTask CommitAsync();

    IAsyncEnumerable<IEventEntry> GetEventsAsync(QueryOptions? options = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<IEventEntry<TEvent>> GetEventsAsync<TEvent>(QueryOptions? options = null, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    ValueTask<TProjection> ApplyEventsAsync<TProjection>(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default)
        where TProjection : notnull;

    ValueTask ReactEventsAsync<TProjection>(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        where TProjection : notnull;

    void Configure<TEventGrain, TProjection>(TEventGrain eventGrain, IServiceProvider serviceProvider, Action<IEventStoreConfigurator<TEventGrain, TProjection>> builderAction)
        where TEventGrain : IGrainBase
        where TProjection : notnull;
}

public readonly record struct QueryOptions(
    long? SequenceNumber = null,
    DateTimeOffset? EventTimestamp = null)
{
    public static readonly QueryOptions Default = new();
}

public interface IEventStore<TProjection> : IEventStore
    where TProjection : notnull, IEventProjection<TProjection>
{
    TProjection Projection { get; }

    ValueTask ApplyEventsAsync(CancellationToken cancellationToken = default);

    ValueTask ReactEventsAsync(CancellationToken cancellationToken = default);
}
