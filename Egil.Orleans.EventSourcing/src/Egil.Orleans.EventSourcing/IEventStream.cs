using Egil.Orleans.EventSourcing;

namespace Egil.Orleans.EventSourcing;

public interface IEventStream<TEvent> where TEvent: notnull
{
    string Name { get; }

    long EventCount { get; }

    long? LatestSequenceNumber { get; }

    DateTimeOffset? LatestEventTimestamp { get; }

    bool HasUncommittedEvents { get; }

    bool HasUnreactedEvents { get; }

    IEnumerable<IEventEntry<TEvent>> GetUncommittedEvents();

    void AppendEvent(TEvent @event);

    IAsyncEnumerable<IEventEntry<TEvent>> GetEventsAsync(QueryOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask<TProjection> ApplyEventsAsync<TProjection>(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default) where TProjection : notnull;

    ValueTask ReactEventsAsync<TProjection>(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default) where TProjection : notnull;
}
