using Azure;
using Azure.Data.Tables;
using System.Collections.Immutable;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing.Internals.Storage;

internal readonly record struct EventEntry<TEvent> : ITableTransactionable
    where TEvent : notnull
{
    public required TEvent Event { get; init; }

    public required string EventId { get; init; }

    public required long SequenceNumber { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required ETag ETag { get; init; }

    public required ImmutableArray<ReactorState> ReactorStatus { get; init; }

    public IEnumerable<TableTransactionAction> ToTableTransactionAction(long startingSequenceNumber)
    {
        // Assuming this event is part of a stream, we need the stream name
        // This might need to be passed in or stored as part of the event
        var streamName = "default"; // You'll need to determine this

        var rowKey = $"{streamName}{EventStore.StreamSeparator}{SequenceNumber:D19}{EventStore.StreamSeparator}{EventId}";

        var entity = new TableEntity
        {
            RowKey = rowKey,
            ["EventType"] = Event.GetType().FullName,
            ["Data"] = JsonSerializer.Serialize(Event),
            ["EventId"] = EventId,
            ["SequenceNumber"] = SequenceNumber,
            ["Timestamp"] = Timestamp,
            ["ReactorStatus"] = JsonSerializer.Serialize(ReactorStatus)
        };

        // If ETag is default, this is a new event
        if (ETag == default)
        {
            yield return new TableTransactionAction(TableTransactionActionType.Add, entity);
        }
        else
        {
            entity.ETag = ETag;
            yield return new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity);
        }
    }
}
