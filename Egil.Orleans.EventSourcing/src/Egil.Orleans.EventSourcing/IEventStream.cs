using Egil.Orleans.EventSourcing.EventHandlers;
using Egil.Orleans.EventSourcing.EventReactors;

namespace Egil.Orleans.EventSourcing;

internal interface IEventStream
{
    bool Matches<TEvent>(TEvent @event) where TEvent : notnull;
}

internal interface IEventStream<TEvent, TProjection>
    where TEvent : notnull
    where TProjection : notnull
{
    string Name { get; }

    long EventCount { get; }

    long? LatestSequenceNumber { get; }

    DateTimeOffset? LatestEventTimestamp { get; }

    bool HasUncommittedEvents { get; }

    bool HasUnreactedEvents { get; }

    IEnumerable<IEventEntry<TEvent>> GetUncommittedEvents();

    void AppendEvent(TEvent @event, long sequenceNumber);

    IAsyncEnumerable<IEventEntry<TEvent>> GetEventsAsync(QueryOptions? options = null, CancellationToken cancellationToken = default);

    ValueTask<TProjection> ApplyEventsAsync(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default);

    ValueTask ReactEventsAsync(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default);
}
