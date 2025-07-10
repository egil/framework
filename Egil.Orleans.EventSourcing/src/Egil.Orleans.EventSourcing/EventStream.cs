using Egil.Orleans.EventSourcing.EventHandlerFactories;
using Egil.Orleans.EventSourcing.EventHandlers;
using Egil.Orleans.EventSourcing.EventReactorFactories;
using Egil.Orleans.EventSourcing.EventReactors;
using Egil.Orleans.EventSourcing.EventStores;
using Orleans;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing;

internal class EventStream<TEventGrain, TEventBase, TProjection> : IEventStream<TEventBase, TProjection>, IEventStream
    where TEventGrain : IGrainBase
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly Lazy<IEventHandler<TProjection>[]> handlers;
    private readonly Lazy<IEventReactor<TProjection>[]> reactors;
    private readonly EventStreamRetention<TEventBase> retention;
    private readonly List<EventEntry<TEventBase>> uncommitted = [];

    public string Name { get; }

    public long EventCount { get; }

    public long? LatestSequenceNumber { get; }

    public DateTimeOffset? LatestEventTimestamp { get; }

    public bool HasUncommittedEvents => uncommitted.Count > 0;

    public bool HasUnreactedEvents { get; }

    public EventStream(string name, IReadOnlyList<IEventHandlerFactory<TProjection>> handlerFactories, List<IEventReactorFactory<TProjection>> reactorFactories, EventStreamRetention<TEventBase> retention)
    {
        Name = name;
        this.handlers = new Lazy<IEventHandler<TProjection>[]>(() => handlerFactories.Select(x => x.Create()).ToArray());
        this.reactors = new Lazy<IEventReactor<TProjection>[]>(() => reactorFactories.Select(x => x.Create()).ToArray());
        this.retention = retention;
    }

    public void AppendEvent(TEventBase @event, long sequenceNumber)
        => uncommitted.Add(new EventEntry<TEventBase>
        {
            Event = @event,
            SequenceNumber = sequenceNumber,
            EventId = retention.EventIdSelector?.Invoke(@event),
            EventTimestamp = retention.TimestampSelector?.Invoke(@event),
            ReactorStatus = reactors.Value.Where(x => x.Matches(@event)).Select(x => ReactorState.Create(x.Identifier)).ToImmutableArray()
        });

    public ValueTask<TProjection> ApplyEventsAsync(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default)
    {

    }

    public IAsyncEnumerable<IEventEntry<TEventBase>> GetEventsAsync(QueryOptions? options = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public IEnumerable<IEventEntry<TEventBase>> GetUncommittedEvents() => uncommitted;

    public ValueTask ReactEventsAsync(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public bool Matches<TEvent>(TEvent @event) where TEvent : notnull => @event is TEventBase;
}