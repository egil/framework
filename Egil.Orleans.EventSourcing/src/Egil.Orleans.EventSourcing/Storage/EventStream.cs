using Azure;
using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Reactors;
using Orleans;
using Orleans.Storage;
using System.Collections.Immutable;

namespace Egil.Orleans.EventSourcing.Storage;

internal class EventStream<TEventGrain, TEventBase, TProjection> : IEventStream<TProjection>
    where TEventGrain : IGrainBase
    where TEventBase : notnull
    where TProjection : notnull, IEventProjection<TProjection>
{
    private readonly Lazy<IEventHandler<TProjection>[]> handlers;
    private readonly Lazy<IEventReactor<TProjection>[]> reactors;
    private readonly EventStreamRetention<TEventBase> retention;
    private readonly TimeProvider timeProvider;

    public string Name { get; }

    public IEventStreamRetention Retention => retention;

    public EventStream(
        string name,
        IReadOnlyList<IEventHandlerFactory<TProjection>> handlerFactories,
        IReadOnlyList<IEventReactorFactory<TProjection>> reactorFactories,
        EventStreamRetention<TEventBase> retention,
        TimeProvider timeProvider)
    {
        Name = name;
        handlers = new Lazy<IEventHandler<TProjection>[]>(() => handlerFactories.Select(x => x.Create()).ToArray());
        reactors = new Lazy<IEventReactor<TProjection>[]>(() => reactorFactories.Select(x => x.Create()).ToArray());
        this.retention = retention;
        this.timeProvider = timeProvider;
    }

    public bool Matches<TEvent>(TEvent? @event) where TEvent : notnull
        => @event is not null
        ? @event is TEventBase
        : typeof(TEventBase).IsAssignableFrom(typeof(TEvent));

    public IEventEntry CreateEventEntry<TEvent>(TEvent @event, long sequenceNumber) where TEvent : notnull
    {
        if (@event is not TEventBase castEvent)
        {
            throw new InvalidOperationException($"Event type {typeof(TEvent).FullName} does not match the stream's event type {typeof(TEventBase).FullName}.");
        }

        var eventId = retention.EventIdSelector?.Invoke(castEvent);

        // If KeepDistinct retention is configured (EventIdSelector is not null),
        // EventId must not be null
        if (retention.EventIdSelector is not null && eventId is null)
        {
            throw new InvalidOperationException($"Event ID selector returned null for event type {typeof(TEvent).FullName} in stream '{Name}'. When KeepDistinct retention is configured, the event ID selector must return a non-null string.");
        }

        return new EventEntry<TEvent>
        {
            Event = @event,
            StreamName = Name,
            SequenceNumber = sequenceNumber,
            EventId = eventId,
            EventTimestamp = retention.TimestampSelector?.Invoke(castEvent),
            ReactorStatus = reactors.Value
                .Where(x => x.Matches(@event))
                .Select(x => ReactorState.Create(x.Id))
                .ToImmutableDictionary(x => x.ReactorId, x => x),
        };
    }

    public IEventEntry CreateEventEntry(IGrainStorageSerializer serializer, byte[] binaryData, long sequenceNumber, ImmutableDictionary<string, ReactorState> reactorStatus, DateTimeOffset? timestamp, ETag etag)
    {
        var @event = serializer.Deserialize<TEventBase>(BinaryData.FromBytes(binaryData));

        var eventId = retention.EventIdSelector?.Invoke(@event);

        // If KeepDistinct retention is configured (EventIdSelector is not null),
        // EventId must not be null
        if (retention.EventIdSelector is not null && eventId is null)
        {
            throw new InvalidOperationException($"Event ID selector returned null for event type {typeof(TEventBase).FullName} in stream '{Name}'. When KeepDistinct retention is configured, the event ID selector must return a non-null string.");
        }

        return new EventEntry<TEventBase>
        {
            Event = @event,
            StreamName = Name,
            SequenceNumber = sequenceNumber,
            EventId = eventId,
            EventTimestamp = retention.TimestampSelector?.Invoke(@event),
            Timestamp = timestamp,
            ETag = etag,
            ReactorStatus = reactorStatus,
        };
    }

    public byte[] SerializeEvent(IGrainStorageSerializer serializer, IEventEntry eventEntry)
        => eventEntry.Event is TEventBase castedEvent
        ? serializer.Serialize(castedEvent).ToArray()
        : throw new InvalidOperationException($"Event type {eventEntry.Event.GetType().FullName} does not match the stream's event type {typeof(TEventBase).FullName}.");

    public async ValueTask<TProjection> ApplyEventsAsync<TEvent>(TEvent @event, TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        if (@event is not TEventBase compatibleEvent)
        {
            return projection;
        }

        foreach (var handler in handlers.Value)
        {
            projection = await handler.HandleAsync(compatibleEvent, projection, context);
        }

        return projection;
    }

    public async ValueTask<ImmutableArray<IEventEntry>> ReactEventsAsync(ImmutableArray<IEventEntry> events, TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default)
    {
        foreach (var reactor in reactors.Value)
        {
            var reactorEvents = events.Where(eventEntry => MatchesReactor(eventEntry, reactor.Id));
            try
            {
                await reactor.ReactAsync(
                    reactorEvents,
                    projection,
                    context,
                    cancellationToken);
                SetReactorStatus(reactorEvents, reactor.Id, ReactorOperationStatus.CompleteSuccessful);
            }
            catch (Exception)
            {
                SetReactorStatus(reactorEvents, reactor.Id, ReactorOperationStatus.Failed);
            }
        }

        return events;

        bool MatchesReactor(IEventEntry eventEntry, string reactorId)
        {
            return eventEntry.ReactorStatus.TryGetValue(reactorId, out var state)
                && state.Status is not ReactorOperationStatus.CompleteSuccessful;
        }

        void SetReactorStatus(IEnumerable<IEventEntry> reactorEvents, string reactorId, ReactorOperationStatus status)
        {
            foreach (var reactorEvent in reactorEvents)
            {
                var state = reactorEvent.ReactorStatus[reactorId];
                events = events.Replace(reactorEvent, reactorEvent.SetReactorStatus(reactorId, state with
                {
                    Attempts = state.Attempts + 1,
                    Status = status,
                    Timestamp = timeProvider.GetUtcNow(),
                }));
            }
        }
    }
}