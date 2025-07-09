using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.EventStores;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;
using System.Collections.Immutable;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing.Storage;

/// <summary>
/// EventStore implementation using Azure Table Storage as the backing store.
/// This class manages both event streams and projections within a single table partition
/// per grain, enabling atomic transactions across both event and projection updates.
/// 
/// Design principles:
/// - Each grain has its own partition (PartitionKey = GrainId)
/// - Projections are stored at RowKey "~projection" (~ ensures it sorts first)
/// - Events use RowKey format: "{streamName}#{sequenceNumber:D19}#{eventId}"
/// - Atomic transactions ensure consistency between events and projections
/// - Optimistic concurrency via ETags prevents concurrent modifications
/// </summary>
internal class EventStore(TableClient tableClient, IGrainStorageSerializer serializer, IOptions<ClusterOptions> clusterOptions) : IEventStore
{
    // RowKey prefixes - Using "!" ensures these sort first, before any event data
    // ASCII ordering: ! (33) < numbers < letters < ~ (126)
    internal const string ProjectionRowKey = "!projection";
    internal const string StreamMetadataPrefix = "!stream";

    // Separator character used in RowKey construction. # is safe as it's not used in base64 encoding
    internal const char StreamSeparator = '_';

    // Azure Table Storage transaction limit as per documentation:
    // https://docs.microsoft.com/en-us/rest/api/storageservices/performing-entity-group-transactions
    private const int MaxBatchSize = 100;

    /// <summary>
    /// Loads the projection for a grain.
    /// Projections store the current state derived from all events, avoiding the need
    /// to replay all events on every grain activation.
    /// </summary>
    public async ValueTask<ProjectionEntry<TProjection>> LoadProjectionAsync<TProjection>(
        GrainId grainId,
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        var partitionKey = CreatePartitionKey(grainId);

        // Direct lookup using known RowKey for optimal performance
        var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(
            partitionKey,
            ProjectionRowKey,
            cancellationToken: cancellationToken);

        if (!response.HasValue || response.Value is null)
        {
            return ProjectionEntry<TProjection>.CreateDefault();
        }

        return ProjectionEntry<TProjection>.FromTableEntity(response.Value, serializer);
    }

    public ValueTask LoadEventsAsync<TEvent>(GrainId grainId, IEnumerable<IEventStream> streams, CancellationToken cancellationToken) where TEvent : notnull
    {
        var partitionKey = CreatePartitionKey(grainId);

        // Create a query to fetch all events for the specified stream
        var query = tableClient.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{partitionKey}' and RowKey startswith '{streamName}{StreamSeparator}'",
            cancellationToken: cancellationToken);

        var events = new List<EventEntry<TEvent>>();

        await foreach (var entity in query)
        {
            if (entity.RowKey.StartsWith(streamName + StreamSeparator))
            {
                var @event = EventEntry<TEvent>.FromTableEntity(entity, retention, serializer);
                events.Add(@event);
            }
        }

        return ValueTask.FromResult<IReadOnlyList<EventEntry<TEvent>>>(events.ToImmutableArray());
    }

    /// <summary>
    /// Saves events and projection updates atomically.
    /// This is the core method ensuring consistency in the event store.
    /// 
    /// The method handles:
    /// 1. New event insertions (with deduplication via Add operation)
    /// 2. Reactor state updates for existing events
    /// 3. Projection updates with optimistic concurrency
    /// 4. Retention policy enforcement
    /// 
    /// Operations are prioritized to ensure critical updates succeed even if
    /// less important operations (like retention cleanup) fail.
    /// </summary>
    public async ValueTask SaveAsync<TProjection>(
        GrainId grainId,
        ProjectionEntry<TProjection> projection,
        IEnumerable<IEventStream> streams)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        var partitionKey = CreatePartitionKey(grainId);

        // Primary batch contains critical operations that must succeed atomically
        var batch = new List<TableTransactionAction>();
        batch.Add(projection.ToTableTransactionAction(partitionKey, ProjectionRowKey, serializer));

        foreach (var stream in streams)
        {
            batch.Add(stream.);

            foreach (var @event in stream.ChangedEvents)
            {
                var rowKey = CreateEventRowKey(stream, @event);
                batch.Add(@event.ToTableTransactionAction(partitionKey, rowKey, serializer));
            }
        }

        try
        {
            await tableClient.SubmitTransactionAsync(batch);
        }
        catch (TableTransactionFailedException ex) when (ex.IsPreconditionFailed() || ex.IsConflict() || ex.IsNotFound())
        {
            throw new TableStorageUpdateConditionNotSatisfiedException(
                "Unknown",
                grainId.ToString(),
                tableClient.Name,
                "Unknown",
                "Unknown",
                ex);
        }
    }

    internal TableTransactionAction ToTableTransactionAction(string partitionKey, IEventStream stream)
    {

        var entity = new TableEntity(partitionKey, rowKey)
        {
            [EntityConstants.StreamNameColumnName] = stream.Name,
            [EntityConstants.StreamEventCountColumnName] = stream.EventCount,
            [EntityConstants.LatestEventTimestampColumnName] = stream.LatestEventTimestamp,
            [EntityConstants.OldestEventTimestampColumnName] = stream.OldestEventTimestamp,
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

    private string CreatePartitionKey(GrainId grainId)
        => $"{clusterOptions.Value.ClusterId}{StreamSeparator}{grainId}".SanitizeKeyPropertyValue(StreamSeparator);

    private string CreateStreamMetadataRowKey(string streamName)
        => $"{StreamMetadataPrefix}{StreamSeparator}{streamName}".SanitizeKeyPropertyValue('*');

    private string CreateEventRowKey(IEventStream stream, IEventEntry @event)
    {
        // Regular sequence number (not inverted) for efficient range queries
        // This allows querying from a specific sequence number onwards
        return $"{stream.Name}{StreamSeparator}{@event.SequenceNumber:D19}{StreamSeparator}{@event.EventId}".SanitizeKeyPropertyValue('*');
    }
}