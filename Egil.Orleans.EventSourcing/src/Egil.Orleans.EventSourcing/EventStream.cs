namespace Egil.Orleans.EventSourcing;

internal class EventStream<TEvent>() : IEventStream<TEvent>
    where TEvent : notnull
{
    private readonly List<EventEntry<TEvent>> uncommitted = [];

    public required string Name { get; set; }

    public long EventCount { get; }

    public long? LatestSequenceNumber { get; }

    public DateTimeOffset? LatestEventTimestamp { get; }

    public bool HasUncommittedEvents => uncommitted.Count > 0;

    public bool HasUnreactedEvents { get; }

    public void AppendEvent(TEvent @event, long sequenceNumber) => uncommitted.Add(new EventEntry<TEvent>
    {
        Event = @event,
        SequenceNumber = sequenceNumber,
        // ...
    });

    public ValueTask<TProjection> ApplyEventsAsync<TProjection>(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default) where TProjection : notnull
        => throw new NotImplementedException();

    public IAsyncEnumerable<IEventEntry<TEvent>> GetEventsAsync(QueryOptions? options = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public IEnumerable<IEventEntry<TEvent>> GetUncommittedEvents() => uncommitted.Select(e => new EventEntry<TEvent>(e));

    public ValueTask ReactEventsAsync<TProjection>(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default) where TProjection : notnull => throw new NotImplementedException();
}