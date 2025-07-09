using Azure;
using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.Storage;
using Orleans.Storage;

namespace Egil.Orleans.EventSourcing;

public class ProjectionEntry<TProjection> where TProjection : notnull, IEventProjection<TProjection>
{
    public required TProjection Projection { get; set; }

    public required long NextEventSequenceNumber { get; set; }

    public required long StoreEventCount { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; } = ETag.All;

    public static ProjectionEntry<TProjection> CreateDefault() =>
        new ProjectionEntry<TProjection>
        {
            Projection = TProjection.CreateDefault(),
            NextEventSequenceNumber = 0L,
            StoreEventCount = 0L,
            Timestamp = null,
            ETag = ETag.All
        };

    internal TableTransactionAction ToTableTransactionAction(string partitionKey, string rowKey, IGrainStorageSerializer serializer)
    {
        var entity = new TableEntity(partitionKey, rowKey)
        {
            [EntityConstants.DataColumnName] = serializer.Serialize(Projection),
            [EntityConstants.NextEventSequenceNumberColumnName] = NextEventSequenceNumber,
            [EntityConstants.StoreEventCountColumnName] = StoreEventCount,
        };

        if (ETag == default)
        {
            return new TableTransactionAction(TableTransactionActionType.Add, entity);
        }
        else
        {
            // Existing projection - use Replace with ETag for optimistic concurrency
            entity.ETag = ETag;
            return new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity);
        }
    }

    internal static ProjectionEntry<TProjection> FromTableEntity(TableEntity entity, IGrainStorageSerializer serializer)
    {
        // Extract and deserialize the event data
        var projection = entity.TryGetValue(EntityConstants.DataColumnName, out var data)
            && data is byte[] eventJson
            ? serializer.Deserialize<TProjection>(eventJson) ?? TProjection.CreateDefault()
            : TProjection.CreateDefault();

        var nextEventSequenceNumber = entity.TryGetValue(EntityConstants.NextEventSequenceNumberColumnName, out var seq) && seq is long seqNum ? seqNum : 0L;

        var storeEventCount = entity.TryGetValue(EntityConstants.StoreEventCountColumnName, out var count) && count is long eventCount ? eventCount : 0L;

        return new ProjectionEntry<TProjection>
        {
            Projection = projection,
            NextEventSequenceNumber = nextEventSequenceNumber,
            StoreEventCount = storeEventCount,
            Timestamp = entity.Timestamp,
            ETag = entity.ETag,
        };
    }
}
