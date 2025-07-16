using Azure;
using Azure.Data.Tables;
using Orleans.Storage;
using System.Collections.Immutable;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing;

internal record class EventEntry<TEvent> : IEventEntry<TEvent>, IEventEntry
    where TEvent : notnull
{
    public required TEvent Event { get; init; }

    public required string StreamName { get; init; }

    public required long SequenceNumber { get; init; }

    public string? EventId { get; init; }

    public DateTimeOffset? EventTimestamp { get; init; }

    public DateTimeOffset? Timestamp { get; init; }

    public ETag ETag { get; init; } = ETag.All;

    public ImmutableDictionary<string, ReactorState> ReactorStatus { get; init; } = ImmutableDictionary<string, ReactorState>.Empty;

    object IEventEntry.Event => Event;

    public TRequestedEvent? TryCastEvent<TRequestedEvent>() where TRequestedEvent : notnull
        => Event is TRequestedEvent requestedEvent
        ? requestedEvent
        : default;

    public IEventEntry SetReactorStatus(string reactorId, ReactorState state)
    {
        return this with { ReactorStatus = ReactorStatus.SetItem(reactorId, state) };
    }

    public TableTransactionAction ToTableTransactionAction(string partitionKey, string rowKey, IGrainStorageSerializer serializer)
    {
        var entity = new TableEntity(partitionKey, rowKey)
        {
            [EntityConstants.DataColumnName] = serializer.Serialize(Event),
            [EntityConstants.ReactorStatusColumnName] = serializer.Serialize(ReactorStatus),
            [EntityConstants.EventIdColumnName] = EventId,
            [EntityConstants.SequenceNumberColumnName] = SequenceNumber,
            [EntityConstants.EventTimestampColumnName] = EventTimestamp,
            [EntityConstants.StreamNameColumnName] = StreamName,
        };

        // If ETag is default, this is a new event
        if (ETag == default)
        {
            return new TableTransactionAction(TableTransactionActionType.Add, entity);
        }
        else
        {
            entity.ETag = ETag;
            return new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity);
        }
    }

    public static EventEntry<TEvent>? FromTableEntity(TableEntity entity, IGrainStorageSerializer serializer)
    {
        // Extract and deserialize the event data
        if (!entity.TryGetValue(EntityConstants.DataColumnName, out var data) || data is not byte[] eventJson)
            throw new Exception($"{EntityConstants.DataColumnName} not found in table");

        if (serializer.Deserialize<TEvent>(eventJson) is not { } @event)
            throw new Exception($"Event could not be deserialized.");

        if (!entity.TryGetValue(EntityConstants.SequenceNumberColumnName, out var seq) || seq is not long sequenceNumber)
            throw new Exception($"{EntityConstants.SequenceNumberColumnName} not found in table");

        if (!entity.TryGetValue(EntityConstants.StreamNameColumnName, out var val) || val is not string streamName)
            throw new Exception($"{EntityConstants.StreamNameColumnName} not found in table");

        // Deserialize reactor status for tracking side-effect processing
        ImmutableDictionary<string, ReactorState> reactorStatus = ImmutableDictionary<string, ReactorState>.Empty;
        if (entity.TryGetValue(EntityConstants.ReactorStatusColumnName, out var status) && status is string reactorStatusJson)
        {
            var states = JsonSerializer.Deserialize<ImmutableDictionary<string, ReactorState>>(reactorStatusJson);
            if (states is not null)
            {
                reactorStatus = states;
            }
        }

        return new EventEntry<TEvent>
        {
            Event = @event,
            StreamName = streamName,
            EventId = entity.TryGetValue(EntityConstants.EventIdColumnName, out var objId) && objId is string eventId ? eventId : null,
            SequenceNumber = sequenceNumber,
            EventTimestamp = entity.TryGetValue(EntityConstants.EventTimestampColumnName, out var objTs) && objTs is DateTimeOffset eventTimestamp ? eventTimestamp : null,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            ReactorStatus = reactorStatus
        };
    }
}
