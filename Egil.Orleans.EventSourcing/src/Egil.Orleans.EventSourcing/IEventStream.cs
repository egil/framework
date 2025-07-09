namespace Egil.Orleans.EventSourcing.EventStores;

public interface IEventStream
{
    string Name { get; }

    int EventCount { get; }

    DateTimeOffset? LatestEventTimestamp { get; }

    DateTimeOffset? OldestEventTimestamp { get; }

    bool HasUncommittedEvents { get; }

    bool HasUnreactedEvents { get; }

    IEnumerable<EventEntry> GetUncommittedEvents();

    bool Matches<TEvent>(TEvent? @event) where TEvent : notnull;

    void AppendEvent<TEvent>(TEvent @event, long sequenceNumber) where TEvent : notnull;

    ValueTask<TProjection> ApplyEventsAsync<TProjection>(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default) where TProjection : notnull;

    ValueTask ReactEventsAsync<TProjection>(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default) where TProjection : notnull;
}
