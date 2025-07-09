using Azure;
using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.Storage;

namespace Egil.Orleans.EventSourcing;

public class StreamEntry
{
    public required string StreamName { get; set; }

    public required long EventCount { get; set; }

    public DateTimeOffset? LatestEventTimestamp { get; set; }

    public DateTimeOffset? OldestEventTimestamp { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; } = ETag.All;

    internal TableTransactionAction ToTableTransactionAction(string partitionKey, string rowKey)
    {
        var entity = new TableEntity(partitionKey, rowKey)
        {
            [EntityConstants.StreamNameColumnName] = StreamName,
            [EntityConstants.StreamEventCountColumnName] = EventCount,
            [EntityConstants.LatestEventTimestampColumnName] = LatestEventTimestamp,
            [EntityConstants.OldestEventTimestampColumnName] = OldestEventTimestamp,
        };

        if (ETag == default)
        {
            return new TableTransactionAction(TableTransactionActionType.Add, entity);
        }
        else
        {
            // Existing stream - use Replace with ETag for optimistic concurrency
            entity.ETag = ETag;
            return new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity);
        }
    }
}
