using Egil.Orleans.EventSourcing.Storage;

namespace Egil.Orleans.EventSourcing;

public interface IEventStream
{
    string Name { get; }

    int EventCount { get; }

    DateTimeOffset? LatestEventTimestamp { get; }

    DateTimeOffset? OldestEventTimestamp { get; }

    bool HasUnappliedEvents { get; }

    bool HasUnreactedEvents { get; }

    void AddEventEntry(IEventEntry eventEntry);

    IEnumerable<IEventEntry> GetUnsavedEvents();

    bool Matches<TEvent>(TEvent? @event) where TEvent : notnull;

    void AppendEvent<TEvent>(TEvent @event, long sequenceNumber) where TEvent : notnull;

    ValueTask<TProjection> ApplyEventsAsync<TProjection>(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;

    ValueTask ReactEventsAsync<TProjection>(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>;
}
