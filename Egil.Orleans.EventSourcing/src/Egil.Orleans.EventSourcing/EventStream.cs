using Egil.Orleans.EventSourcing.EventHandlerFactories;
using Egil.Orleans.EventSourcing.EventReactorFactories;
using Egil.Orleans.EventSourcing.EventReactors;
using Egil.Orleans.EventSourcing.Storage;
using Orleans.Runtime;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing;

internal class EventStream<TEventGrain, TEvent, TProjection> : IEventStream
    where TEventGrain : EventGrain<TEventGrain, TProjection>
    where TEvent : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly GrainId grainId;
    private readonly IEventStore eventStore;
    private readonly IReadOnlyList<IEventHandlerFactory<TEventGrain, TProjection>> handlerFactories;
    private readonly IReadOnlyList<IEventReactorFactory<TEventGrain, TProjection>> reactorFactories;
    private readonly EventStreamRetention<TEvent> retention;

    private IReadOnlyList<EventEntry<TEvent>>? confirmedEvents;
    private List<EventEntry<TEvent>>? unconfirmedEvents;
    private List<IEventHandler<TProjection>>? handlers;
    private List<IEventReactor<TProjection>>? reactors;

    private IReadOnlyList<IEventReactor<TProjection>> Reactors
    {
        get
        {
            reactors ??= reactorFactories.Select(factory => factory.Create()).ToList();
            return reactors;
        }
    }

    private IReadOnlyList<IEventHandler<TProjection>> Handlers
    {
        get
        {
            handlers ??= handlerFactories.Select(factory => factory.Create()).ToList();
            return handlers;
        }
    }

    public string Name { get; }

    public bool HasUnconfirmedEvents => unconfirmedEvents is not null && unconfirmedEvents.Count > 0;

    public bool HasUnreactedEvents { get; }

    public IEnumerable<IEventEntry> ChangedEvents { get; }

    public EventStream(
        GrainId grainId,
        IEventStore eventStore,
        string name,
        IReadOnlyList<IEventHandlerFactory<TEventGrain, TProjection>> handlerFactories,
        IReadOnlyList<IEventReactorFactory<TEventGrain, TProjection>> reactorFactories,
        EventStreamRetention<TEvent> retention)
    {
        this.grainId = grainId;
        this.eventStore = eventStore;
        Name = name;
        this.handlerFactories = handlerFactories;
        this.reactorFactories = reactorFactories;
        this.retention = retention;
    }

    public bool Matches<TRequestedEvent>(TRequestedEvent? @event) where TRequestedEvent : notnull
    {
        if (@event is null)
        {
            return typeof(TRequestedEvent).IsAssignableTo(typeof(TEvent));
        }

        if (@event is TEvent)
        {
            return true;
        }

        return false;
    }

    public void AppendEvent<TRequestedEvent>(TRequestedEvent @event, long sequenceNumber) where TRequestedEvent : notnull
    {
        if (@event is not TEvent castEvent)
        {
            throw new InvalidOperationException($"Event of type {typeof(TRequestedEvent).FullName} cannot be appended to stream of type {typeof(TEvent).FullName}.");
        }

        unconfirmedEvents ??= [];
        unconfirmedEvents.Add(new EventEntry<TEvent>()
        {
            Event = castEvent,
            EventId = retention.EventIdSelector?.Invoke(castEvent),
            SequenceNumber = sequenceNumber,
            EventTimestamp = retention.TimestampSelector?.Invoke(castEvent),
            ReactorStatus = Reactors
                .Where(x => x.Identifier is not null && x.Matches(@event))
                .Select(x => ReactorState.Create(x.Identifier!)).ToImmutableArray(),
        });
    }

    public async ValueTask<IReadOnlyList<IEventEntry>> GetEventsAsync(CancellationToken cancellationToken = default)
    {
        confirmedEvents ??= await eventStore.LoadEventsAsync<TEvent>(grainId, retention, cancellationToken);
        return confirmedEvents;
    }

    public ValueTask<TProjection> ApplyEventsAsync(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public ValueTask ReactEventsAsync(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default) => throw new NotImplementedException(); 
}
