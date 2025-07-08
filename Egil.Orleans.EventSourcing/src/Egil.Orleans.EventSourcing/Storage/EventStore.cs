using Azure.Data.Tables;
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
    // RowKey for projection entities. The ~ prefix ensures projections sort first in the partition
    // per Azure Table Storage lexicographical ordering rules
    internal const string ProjectionRowKey = "~projection";

    // Separator character used in RowKey construction. # is safe as it's not used in base64 encoding
    internal const char StreamSeparator = '_';

    // Azure Table Storage transaction limit as per documentation:
    // https://docs.microsoft.com/en-us/rest/api/storageservices/performing-entity-group-transactions
    private const int MaxBatchSize = 100;

    ///// <summary>
    ///// Loads events from storage with flexible querying capabilities.
    ///// Events are returned in sequence order (oldest to newest) by default due to the RowKey format.
    ///// 
    ///// The method supports both server-side filtering (via OData queries) and client-side filtering
    ///// for options not supported by Table Storage queries.
    ///// </summary>
    //public async IAsyncEnumerable<EventEntry<TEvent>> LoadEventsAsync<TEvent>(
    //    GrainId grainId,
    //    string? streamName = null,
    //    EventQueryOptions? options = null,
    //    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    //    where TEvent : notnull
    //{
    //    var partitionKey = CreatePartitionKey(grainId);

    //    // Build OData filter string for server-side filtering
    //    // This reduces data transfer and improves performance
    //    var filter = BuildEventQueryFilter(partitionKey, streamName, options);

    //    // QueryAsync returns results ordered by RowKey within the partition
    //    // Our RowKey format ensures events are ordered by sequence number
    //    var query = tableClient.QueryAsync<TableEntity>(
    //        filter: filter,
    //        cancellationToken: cancellationToken);

    //    // For distinct queries, track seen EventIds to filter duplicates client-side
    //    // This is necessary because Table Storage doesn't support DISTINCT queries
    //    var processedEventIds = options?.DistinctByEventId == true
    //        ? new HashSet<string>()
    //        : null;

    //    var eventCount = 0;
    //    var maxCount = options?.MaxCount ?? int.MaxValue;

    //    await foreach (var entity in query)
    //    {
    //        // Early exit if we've reached the requested count
    //        if (eventCount >= maxCount)
    //            break;

    //        // Deserialize the event from the table entity
    //        var eventEntry = EventEntry<TEvent>.FromTableEntity(entity, serializer);
    //        if (!eventEntry.HasValue)
    //            continue; // Skip malformed or non-matching events

    //        // Apply client-side filters for options not supported by OData
    //        if (options != null)
    //        {
    //            // Sequence number filtering - useful for catching up projections
    //            if (options.FromSequenceNumber.HasValue && eventEntry.Value.SequenceNumber < options.FromSequenceNumber.Value)
    //                continue;

    //            if (options.ToSequenceNumber.HasValue && eventEntry.Value.SequenceNumber > options.ToSequenceNumber.Value)
    //                continue;

    //            // Event ID filtering - useful for finding specific events
    //            if (options.EventId != null && eventEntry.Value.EventId != options.EventId)
    //                continue;

    //            // Distinct filtering - keeps only the first occurrence of each EventId
    //            // Since results are ordered by sequence, this keeps the oldest event per ID
    //            if (processedEventIds != null)
    //            {
    //                if (!processedEventIds.Add(eventEntry.Value.EventId))
    //                    continue; // Already seen this EventId
    //            }
    //        }

    //        eventCount++;
    //        yield return eventEntry.Value;
    //    }
    //}

    private string CreatePartitionKey(GrainId grainId)
        => tableClient.SanitizeKeyPropertyValue(
            key: $"{clusterOptions.Value.ClusterId}{StreamSeparator}{grainId.ToString()}",
            sanitizeChar: StreamSeparator);

    ///// <summary>
    ///// Convenience method for loading events starting from a specific sequence number.
    ///// This is commonly used when catching up a projection that's behind on events.
    ///// </summary>
    //public async IAsyncEnumerable<EventEntry<TEvent>> LoadEventsFromSequenceAsync<TEvent>(
    //    GrainId grainId,
    //    long fromSequenceNumber,
    //    string? streamName = null,
    //    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    //    where TEvent : notnull
    //{
    //    var options = new EventQueryOptions { FromSequenceNumber = fromSequenceNumber };
    //    await foreach (var eventEntry in LoadEventsAsync<TEvent>(grainId, streamName, options, cancellationToken))
    //    {
    //        yield return eventEntry;
    //    }
    //}

    ///// <summary>
    ///// Loads the most recent event, optionally filtered by EventId.
    ///// Due to our RowKey format, the first result is the oldest event,
    ///// so we need to process all results to find the latest (this could be optimized).
    ///// </summary>
    //public async ValueTask<EventEntry<TEvent>?> LoadLatestEventAsync<TEvent>(
    //    GrainId grainId,
    //    string? streamName = null,
    //    string? eventId = null,
    //    CancellationToken cancellationToken = default)
    //    where TEvent : notnull
    //{
    //    // TODO: This could be optimized by using reverse RowKey ordering
    //    // or by maintaining a separate index for latest events
    //    var options = new EventQueryOptions
    //    {
    //        MaxCount = 1,
    //        EventId = eventId
    //    };

    //    await foreach (var eventEntry in LoadEventsAsync<TEvent>(grainId, streamName, options, cancellationToken))
    //    {
    //        return eventEntry;
    //    }

    //    return null;
    //}

    ///// <summary>
    ///// Builds an OData filter string for querying events.
    ///// OData is the query language used by Azure Table Storage.
    ///// Reference: https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities
    ///// </summary>
    //private string BuildEventQueryFilter(string partitionKey, string? streamName, EventQueryOptions? options)
    //{
    //    var filters = new List<string>
    //    {
    //        // Always filter by partition (grain)
    //        $"PartitionKey eq '{partitionKey}'"
    //    };

    //    // Stream filter using RowKey prefix matching
    //    if (!string.IsNullOrEmpty(streamName))
    //    {
    //        var streamPrefix = $"{streamName}{StreamSeparator}";
    //        // ge (greater than or equal) and lt (less than) create a range query
    //        // The ~ character has a high ASCII value, so streamPrefix~ captures all rows starting with streamPrefix
    //        filters.Add($"RowKey ge '{streamPrefix}'");
    //        filters.Add($"RowKey lt '{streamPrefix}~'");
    //    }
    //    else
    //    {
    //        // When querying all events, exclude the projection row
    //        filters.Add($"RowKey ne '{ProjectionRowKey}'");
    //    }

    //    // Time-based filtering using the built-in Timestamp property
    //    // This leverages server-side filtering for better performance
    //    if (options?.MaxAge.HasValue == true)
    //    {
    //        var cutoff = DateTimeOffset.UtcNow - options.MaxAge.Value;
    //        // The 'O' format specifier produces ISO 8601 format required by OData
    //        filters.Add($"Timestamp ge datetime'{cutoff:O}'");
    //    }

    //    return string.Join(" and ", filters);
    //}

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

    public ValueTask LoadEventsAsync<TProjection>(GrainId grainId, IEventStream<TProjection> stream, CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>
    {

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
    public async ValueTask SaveAsync<TProjection>(GrainId grainId, ProjectionEntry<TProjection> projection, IReadOnlyCollection<IEventStream<TProjection>> streams)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        var partitionKey = CreatePartitionKey(grainId);

        // Primary batch contains critical operations that must succeed atomically
        var batch = new List<TableTransactionAction>();
        batch.Add(projection.ToTableTransactionAction(partitionKey, ProjectionRowKey, serializer));

        foreach (var stream in streams)
        {
            foreach (var @event in stream.Events)
            {
                var rowKey = $"{stream.Name}{StreamSeparator}{@event.SequenceNumber:D19}{StreamSeparator}{@event.EventId}";
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

    /// <summary>
    /// Executes retention delete operations in batches.
    /// Failures are ignored as retention is eventually consistent - 
    /// cleanup will be retried on future operations.
    /// </summary>
    private async Task ExecuteRetentionDeletes(List<TableTransactionAction> retentionDeletes)
    {
        // Split into MaxBatchSize chunks to respect transaction limits
        for (var i = 0; i < retentionDeletes.Count; i += MaxBatchSize)
        {
            var batch = retentionDeletes.Skip(i).Take(MaxBatchSize).ToList();
            if (batch.Any())
            {
                try
                {
                    await tableClient.SubmitTransactionAsync(batch);
                }
                catch (TableTransactionFailedException)
                {
                    // TODO: Add logging
                    // Retention is best-effort and will be retried on next save
                }
            }
        }
    }

    /// <summary>
    /// Collects entities to delete based on retention policies.
    /// This method only identifies entities to delete; actual deletion happens later.
    /// 
    /// Supports multiple retention strategies that can be combined:
    /// - Count-based: Keep only the latest N events
    /// - Time-based: Delete events older than a threshold
    /// - Distinct: Keep only the latest occurrence of each unique event
    /// - Processing-based: Delete events after all reactors have processed them
    /// </summary>
    private async Task CollectRetentionDeletes(
        string partitionKey,
        string streamName,
        EventStreamRetention retention,
        long currentEventCount,
        List<TableTransactionAction> retentionBatch)
    {
        var streamPrefix = $"{streamName}{StreamSeparator}";

        // Apply "keep latest N" retention
        // This requires loading all events to identify the oldest ones
        if (retention.Count.HasValue)
        {
            var toDelete = currentEventCount - retention.Count.Value;
            if (toDelete > 0)
            {
                // Query all events in the stream with minimal properties
                var query = tableClient.QueryAsync<TableEntity>(
                    filter: $"PartitionKey eq '{partitionKey}' and RowKey ge '{streamPrefix}' and RowKey lt '{streamPrefix}~'",
                    select: new[] { "RowKey", EntityConstants.SequenceNumberColumnName }); // Minimize data transfer

                var allEvents = new List<TableEntity>();
                await foreach (var entity in query)
                {
                    if (entity.TryGetValue(EntityConstants.SequenceNumberColumnName, out var seq) && seq is long sequence)
                    {
                        allEvents.Add(entity);
                    }
                }

                // Sort by sequence number and identify oldest events to delete
                var entitiesToDelete = allEvents
                    .OrderBy(e => (long)e[EntityConstants.SequenceNumberColumnName])
                    .Take((int)toDelete)
                    .ToList();

                foreach (var entity in entitiesToDelete)
                {
                    retentionBatch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
                }
            }
        }

        // Apply time-based retention
        // Leverages server-side timestamp filtering for efficiency
        if (retention.MaxAge.HasValue)
        {
            var cutoff = DateTimeOffset.UtcNow - retention.MaxAge.Value;

            var query = tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}' and RowKey ge '{streamPrefix}' and RowKey lt '{streamPrefix}~'",
                select: new[] { "RowKey", EntityConstants.EventTimestampColumnName, EntityConstants.EventIdColumnName });

            await foreach (var entity in query)
            {
                // Use Azure's built-in Timestamp property for age comparison
                if (entity.Timestamp < cutoff)
                {
                    retentionBatch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
                }
            }
        }

        // Apply distinct retention - keep only latest occurrence of each EventId
        // Useful for maintaining current state events (e.g., latest user profile update)
        if (retention.LatestDistinct)
        {
            var query = tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}' and RowKey ge '{streamPrefix}' and RowKey lt '{streamPrefix}~'",
                select: new[] { "RowKey", EntityConstants.EventIdColumnName, EntityConstants.EventTimestampColumnName });

            // Group events by EventId
            var eventGroups = new Dictionary<string, List<(TableEntity Entity, DateTimeOffset Timestamp)>>();

            await foreach (var entity in query)
            {
                if (entity.TryGetValue(EntityConstants.EventIdColumnName, out var eventId) && eventId is string id)
                {
                    if (!eventGroups.ContainsKey(id))
                    {
                        eventGroups[id] = new List<(TableEntity, DateTimeOffset)>();
                    }
                    eventGroups[id].Add((entity, entity.Timestamp!.Value));
                }
            }

            // For each EventId, keep only the latest occurrence
            foreach (var group in eventGroups.Values)
            {
                if (group.Count > 1)
                {
                    var toDelete = group
                        .OrderByDescending(e => e.Timestamp)
                        .Skip(1) // Keep the latest
                        .Select(e => e.Entity);

                    foreach (var entity in toDelete)
                    {
                        retentionBatch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
                    }
                }
            }
        }

        // Apply processing-based retention
        // Delete events only after all registered reactors have successfully processed them
        if (retention.UntilProcessed)
        {
            var query = tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}' and RowKey ge '{streamPrefix}' and RowKey lt '{streamPrefix}~'",
                select: new[] { "RowKey", EntityConstants.ReactorStatusColumnName });

            await foreach (var entity in query)
            {
                if (entity.TryGetValue(EntityConstants.ReactorStatusColumnName, out var status) && status is string reactorStatusJson)
                {
                    try
                    {
                        var reactorStates = JsonSerializer.Deserialize<ImmutableArray<ReactorState>>(reactorStatusJson);

                        // Check if all reactors have completed successfully
                        // This ensures side effects have been processed before removing the event
                        if (!reactorStates.IsDefaultOrEmpty &&
                            reactorStates.All(r => r.Status == ReactorOperationStatus.CompleteSuccessful))
                        {
                            retentionBatch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity));
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed reactor status - err on the side of retention
                    }
                }
            }
        }
    }
}