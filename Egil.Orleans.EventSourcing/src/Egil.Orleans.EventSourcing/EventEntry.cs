using Azure;
using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.EventStores;
using Orleans.Storage;
using System.Collections.Immutable;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing;

internal class EventEntry<TEvent> : IEventEntry<TEvent>, IEventEntry
    where TEvent : notnull
{
    public required TEvent Event { get; set; }

    public required long SequenceNumber { get; set; }

    public string? EventId { get; set; }

    public DateTimeOffset? EventTimestamp { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; } = ETag.All;

    public ImmutableArray<ReactorState> ReactorStatus { get; set; } = ImmutableArray<ReactorState>.Empty;

    object IEventEntry.Event => Event;

    public TRequestedEvent? TryCastEvent<TRequestedEvent>() where TRequestedEvent : notnull
        => Event is TRequestedEvent requestedEvent
        ? requestedEvent
        : default;

    public TableTransactionAction ToTableTransactionAction(string partitionKey, string rowKey, IGrainStorageSerializer serializer)
    {
        var entity = new TableEntity(partitionKey, rowKey)
        {
            [EntityConstants.DataColumnName] = serializer.Serialize(Event),
            [EntityConstants.ReactorStatusColumnName] = serializer.Serialize(ReactorStatus),
            [EntityConstants.EventIdColumnName] = EventId,
            [EntityConstants.SequenceNumberColumnName] = SequenceNumber,
            [EntityConstants.EventTimestampColumnName] = EventTimestamp,
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

        // Deserialize reactor status for tracking side-effect processing
        var reactorStatus = ImmutableArray<ReactorState>.Empty;
        if (entity.TryGetValue(EntityConstants.ReactorStatusColumnName, out var status) && status is string reactorStatusJson)
        {
            var states = JsonSerializer.Deserialize<ImmutableArray<ReactorState>>(reactorStatusJson);
            if (!states.IsDefault)
            {
                reactorStatus = states;
            }
        }

        return new EventEntry<TEvent>
        {
            Event = @event,
            EventId = entity.TryGetValue(EntityConstants.EventIdColumnName, out var objId) && objId is string eventId ? eventId : null,
            SequenceNumber = sequenceNumber,
            EventTimestamp = entity.TryGetValue(EntityConstants.EventTimestampColumnName, out var objTs) && objTs is DateTimeOffset eventTimestamp ? eventTimestamp : null,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
            ReactorStatus = reactorStatus
        };
    }
}
