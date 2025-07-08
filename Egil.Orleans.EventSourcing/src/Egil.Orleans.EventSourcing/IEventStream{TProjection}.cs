using Egil.Orleans.EventSourcing.Storage;

namespace Egil.Orleans.EventSourcing;

public interface IEventStream<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    string Name { get; }

    bool HasUnconfirmedEvents { get; }

    bool HasUnreactedEvents { get; }

    bool Matches<TEvent>(TEvent? @event) where TEvent : notnull;

    void AppendEvent<TEvent>(TEvent @event, long sequenceNumber) where TEvent : notnull;

    ValueTask<IReadOnlyList<IEventEntry>> GetEventsAsync(CancellationToken cancellationToken = default);

    ValueTask<TProjection> ApplyEventsAsync(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default);

    ValueTask ReactEventsAsync(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default);
}
