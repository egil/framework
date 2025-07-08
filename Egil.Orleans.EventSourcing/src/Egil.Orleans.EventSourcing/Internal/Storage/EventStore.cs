using Azure;
using Azure.Data.Tables;
using Orleans.Runtime;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Egil.Orleans.EventSourcing.Internal;

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
internal class EventStore(TableClient tableClient)
{
    // RowKey for projection entities. The ~ prefix ensures projections sort first in the partition
    // per Azure Table Storage lexicographical ordering rules
    internal const string ProjectionRowKey = "~projection";

    // Separator character used in RowKey construction. # is safe as it's not used in base64 encoding
    internal const char StreamSeparator = '#';

    // Azure Table Storage transaction limit as per documentation:
    // https://docs.microsoft.com/en-us/rest/api/storageservices/performing-entity-group-transactions
    private const int MaxBatchSize = 100;

    /// <summary>
    /// Loads events from storage with flexible querying capabilities.
    /// Events are returned in sequence order (oldest to newest) by default due to the RowKey format.
    /// 
    /// The method supports both server-side filtering (via OData queries) and client-side filtering
    /// for options not supported by Table Storage queries.
    /// </summary>
    public async IAsyncEnumerable<EventEntry<TEvent>> LoadEventsAsync<TEvent>(
        GrainId grainId,
        string? streamName = null,
        EventQueryOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var partitionKey = grainId.ToString();

        // Build OData filter string for server-side filtering
        // This reduces data transfer and improves performance
        var filter = BuildEventQueryFilter(partitionKey, streamName, options);

        // QueryAsync returns results ordered by RowKey within the partition
        // Our RowKey format ensures events are ordered by sequence number
        var query = tableClient.QueryAsync<TableEntity>(
            filter: filter,
            cancellationToken: cancellationToken);

        // For distinct queries, track seen EventIds to filter duplicates client-side
        // This is necessary because Table Storage doesn't support DISTINCT queries
        var processedEventIds = options?.DistinctByEventId == true
            ? new HashSet<string>()
            : null;

        var eventCount = 0;
        var maxCount = options?.MaxCount ?? int.MaxValue;

        await foreach (var entity in query)
        {
            // Early exit if we've reached the requested count
            if (eventCount >= maxCount)
                break;

            // Deserialize the event from the table entity
            var eventEntry = DeserializeEventEntry<TEvent>(entity);
            if (!eventEntry.HasValue)
                continue; // Skip malformed or non-matching events

            // Apply client-side filters for options not supported by OData
            if (options != null)
            {
                // Sequence number filtering - useful for catching up projections
                if (options.FromSequenceNumber.HasValue && eventEntry.Value.SequenceNumber < options.FromSequenceNumber.Value)
                    continue;

                if (options.ToSequenceNumber.HasValue && eventEntry.Value.SequenceNumber > options.ToSequenceNumber.Value)
                    continue;

                // Event ID filtering - useful for finding specific events
                if (options.EventId != null && eventEntry.Value.EventId != options.EventId)
                    continue;

                // Distinct filtering - keeps only the first occurrence of each EventId
                // Since results are ordered by sequence, this keeps the oldest event per ID
                if (processedEventIds != null)
                {
                    if (!processedEventIds.Add(eventEntry.Value.EventId))
                        continue; // Already seen this EventId
                }
            }

            eventCount++;
            yield return eventEntry.Value;
        }
    }

    /// <summary>
    /// Convenience method for loading events starting from a specific sequence number.
    /// This is commonly used when catching up a projection that's behind on events.
    /// </summary>
    public async IAsyncEnumerable<EventEntry<TEvent>> LoadEventsFromSequenceAsync<TEvent>(
        GrainId grainId,
        long fromSequenceNumber,
        string? streamName = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var options = new EventQueryOptions { FromSequenceNumber = fromSequenceNumber };
        await foreach (var eventEntry in LoadEventsAsync<TEvent>(grainId, streamName, options, cancellationToken))
        {
            yield return eventEntry;
        }
    }

    /// <summary>
    /// Loads the most recent event, optionally filtered by EventId.
    /// Due to our RowKey format, the first result is the oldest event,
    /// so we need to process all results to find the latest (this could be optimized).
    /// </summary>
    public async ValueTask<EventEntry<TEvent>?> LoadLatestEventAsync<TEvent>(
        GrainId grainId,
        string? streamName = null,
        string? eventId = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        // TODO: This could be optimized by using reverse RowKey ordering
        // or by maintaining a separate index for latest events
        var options = new EventQueryOptions
        {
            MaxCount = 1,
            EventId = eventId
        };

        await foreach (var eventEntry in LoadEventsAsync<TEvent>(grainId, streamName, options, cancellationToken))
        {
            return eventEntry;
        }

        return null;
    }

    /// <summary>
    /// Builds an OData filter string for querying events.
    /// OData is the query language used by Azure Table Storage.
    /// Reference: https://docs.microsoft.com/en-us/rest/api/storageservices/querying-tables-and-entities
    /// </summary>
    private string BuildEventQueryFilter(string partitionKey, string? streamName, EventQueryOptions? options)
    {
        var filters = new List<string>
        {
            // Always filter by partition (grain)
            $"PartitionKey eq '{partitionKey}'"
        };

        // Stream filter using RowKey prefix matching
        if (!string.IsNullOrEmpty(streamName))
        {
            var streamPrefix = $"{streamName}{StreamSeparator}";
            // ge (greater than or equal) and lt (less than) create a range query
            // The ~ character has a high ASCII value, so streamPrefix~ captures all rows starting with streamPrefix
            filters.Add($"RowKey ge '{streamPrefix}'");
            filters.Add($"RowKey lt '{streamPrefix}~'");
        }
        else
        {
            // When querying all events, exclude the projection row
            filters.Add($"RowKey ne '{ProjectionRowKey}'");
        }

        // Time-based filtering using the built-in Timestamp property
        // This leverages server-side filtering for better performance
        if (options?.MaxAge.HasValue == true)
        {
            var cutoff = DateTimeOffset.UtcNow - options.MaxAge.Value;
            // The 'O' format specifier produces ISO 8601 format required by OData
            filters.Add($"Timestamp ge datetime'{cutoff:O}'");
        }

        return string.Join(" and ", filters);
    }

    /// <summary>
    /// Deserializes a table entity into an EventEntry.
    /// Handles missing or malformed data gracefully to ensure system resilience.
    /// </summary>
    private EventEntry<TEvent>? DeserializeEventEntry<TEvent>(TableEntity entity) where TEvent : notnull
    {
        // Extract and deserialize the event data
        if (!entity.TryGetValue("Data", out var data) || data is not string eventJson)
            return null;

        var @event = JsonSerializer.Deserialize<TEvent>(eventJson);
        if (@event == null)
            return null;

        // Extract metadata fields with appropriate defaults
        var eventId = entity.TryGetValue("EventId", out var id) && id is string eventIdStr
            ? eventIdStr
            : string.Empty;

        var sequenceNumber = entity.TryGetValue("SequenceNumber", out var seq) && seq is long seqNum
            ? seqNum
            : 0L;

        // Prefer custom timestamp over built-in Azure timestamp
        var timestamp = entity.TryGetValue("Timestamp", out var ts) && ts is DateTimeOffset dateTime
            ? dateTime
            : entity.Timestamp ?? DateTimeOffset.UtcNow;

        // Deserialize reactor status for tracking side-effect processing
        var reactorStatus = ImmutableArray<ReactorState>.Empty;
        if (entity.TryGetValue("ReactorStatus", out var status) && status is string reactorStatusJson)
        {
            try
            {
                var states = JsonSerializer.Deserialize<ImmutableArray<ReactorState>>(reactorStatusJson);
                if (!states.IsDefault)
                    reactorStatus = states;
            }
            catch (JsonException)
            {
                // Use empty array on deserialization failure to maintain system stability
            }
        }

        return new EventEntry<TEvent>
        {
            Event = @event,
            EventId = eventId,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp,
            ETag = entity.ETag, // Preserve ETag for optimistic concurrency
            ReactorStatus = reactorStatus
        };
    }

    /// <summary>
    /// Loads the projection for a grain.
    /// Projections store the current state derived from all events, avoiding the need
    /// to replay all events on every grain activation.
    /// </summary>
    public async ValueTask<ProjectionEntry<TProjection>?> LoadProjectionAsync<TProjection>(
        GrainId grainId,
        CancellationToken cancellationToken = default)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        var partitionKey = grainId.ToString();

        try
        {
            // Direct lookup using known RowKey for optimal performance
            var response = await tableClient.GetEntityAsync<TableEntity>(
                partitionKey,
                ProjectionRowKey,
                cancellationToken: cancellationToken);

            if (!response.HasValue)
            {
                return null;
            }

            var entity = response.Value;

            // Extract and deserialize projection data
            if (!entity.TryGetValue("Data", out var data) || data is not string projectionJson)
            {
                return null;
            }

            var projection = JsonSerializer.Deserialize<TProjection>(projectionJson);
            if (projection is null)
            {
                return null;
            }

            // Extract metadata for tracking event processing state
            var nextSequenceNumber = entity.TryGetValue("NextEventSequenceNumber", out var seq) && seq is long seqNum
                ? seqNum
                : 0L;

            var streamEventCount = entity.TryGetValue("StreamEventCount", out var count) && count is long eventCount
                ? eventCount
                : 0L;

            var timestamp = entity.TryGetValue("Timestamp", out var ts) && ts is DateTimeOffset dateTime
                ? dateTime
                : entity.Timestamp ?? DateTimeOffset.UtcNow;

            return new ProjectionEntry<TProjection>
            {
                Projection = projection,
                NextEventSequenceNumber = nextSequenceNumber,
                StreamEventCount = streamEventCount,
                Timestamp = timestamp,
                ETag = entity.ETag // Critical for optimistic concurrency control
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // 404 is expected for new grains that haven't saved any events yet
            return null;
        }
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
    public async ValueTask SaveAsync<TProjection>(EventStoreSaveOperation<TProjection> operation)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        var partitionKey = operation.GrainId.ToString();

        // Primary batch contains critical operations that must succeed atomically
        var primaryBatch = new List<TableTransactionAction>();

        // Reactor updates are best-effort (at-least-once delivery guarantee)
        var reactorUpdateBatches = new List<List<TableTransactionAction>>();

        // Retention deletes are also best-effort
        var retentionBatch = new List<TableTransactionAction>();

        // Track sequence numbering for new events
        var currentSequence = operation.Projection.NextEventSequenceNumber;
        var newEventCount = 0;

        // Process each stream's events
        foreach (var stream in operation.Streams)
        {
            // Separate new events from reactor state updates
            // This distinction is important for sequence numbering and operation prioritization
            var newEvents = new List<(ITableTransactionable Event, long Sequence)>();
            var reactorUpdates = new List<ITableTransactionable>();

            foreach (var eventEntry in stream.Events)
            {
                if (eventEntry is EventEntry<object> entry && entry.ETag != default)
                {
                    // Non-default ETag indicates this is an existing event being updated
                    // Only reactor state can be updated on existing events
                    reactorUpdates.Add(eventEntry);
                }
                else
                {
                    // New event - assign next sequence number
                    newEvents.Add((eventEntry, currentSequence));
                    currentSequence++;
                    newEventCount++;
                }
            }

            // Add new events to primary batch for atomic insertion
            foreach (var (eventEntry, sequence) in newEvents)
            {
                var actions = eventEntry.ToTableTransactionAction(sequence);
                primaryBatch.AddRange(actions);
            }

            // Group reactor updates into separate batches
            // These can be split across multiple transactions if needed
            if (reactorUpdates.Any())
            {
                var currentReactorBatch = new List<TableTransactionAction>();
                reactorUpdateBatches.Add(currentReactorBatch);

                foreach (var update in reactorUpdates)
                {
                    if (update is EventEntry<object> entry)
                    {
                        var actions = update.ToTableTransactionAction(entry.SequenceNumber).ToList();

                        // Respect batch size limits by creating new batches as needed
                        if (currentReactorBatch.Count + actions.Count > MaxBatchSize)
                        {
                            currentReactorBatch = new List<TableTransactionAction>();
                            reactorUpdateBatches.Add(currentReactorBatch);
                        }

                        currentReactorBatch.AddRange(actions);
                    }
                }
            }

            // Collect retention policy deletes (executed separately)
            await CollectRetentionDeletes(
                partitionKey,
                stream.StreamName,
                stream.Retention,
                operation.Projection.StreamEventCount + newEventCount,
                retentionBatch);
        }

        // Update projection metadata to reflect new events
        var updatedProjection = operation.Projection with
        {
            NextEventSequenceNumber = currentSequence,
            StreamEventCount = operation.Projection.StreamEventCount + newEventCount,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Add projection update to primary batch
        var projectionEntity = CreateProjectionEntity(partitionKey, updatedProjection);
        if (operation.Projection.ETag == default)
        {
            // New projection - use Add to ensure it doesn't exist
            primaryBatch.Add(new TableTransactionAction(TableTransactionActionType.Add, projectionEntity));
        }
        else
        {
            // Existing projection - use Replace with ETag for optimistic concurrency
            projectionEntity.ETag = operation.Projection.ETag;
            primaryBatch.Add(new TableTransactionAction(TableTransactionActionType.UpdateReplace, projectionEntity));
        }

        // Validate we haven't exceeded transaction limits
        if (primaryBatch.Count > MaxBatchSize)
        {
            throw new InvalidOperationException(
                $"Cannot save operation: {primaryBatch.Count} operations exceed the maximum batch size of {MaxBatchSize}. " +
                $"Consider reducing the number of events being saved in a single operation.");
        }

        // Opportunistically include reactor updates in primary batch if space allows
        // This reduces the number of round trips to storage
        var includedReactorUpdates = new HashSet<List<TableTransactionAction>>();
        foreach (var reactorBatch in reactorUpdateBatches)
        {
            if (primaryBatch.Count + reactorBatch.Count <= MaxBatchSize)
            {
                primaryBatch.AddRange(reactorBatch);
                includedReactorUpdates.Add(reactorBatch);
            }
        }

        // Remove included updates from separate batch list
        reactorUpdateBatches.RemoveAll(b => includedReactorUpdates.Contains(b));

        // Execute primary transaction - this MUST succeed
        try
        {
            await tableClient.SubmitTransactionAsync(primaryBatch);
        }
        catch (TableTransactionFailedException ex)
        {
            // Map Azure Table Storage errors to domain-specific exceptions
            if (ex.Status == 412) // Precondition Failed
            {
                // ETag mismatch - another process modified the projection
                throw new OptimisticConcurrencyException(
                    "Projection was modified by another process", ex);
            }
            else if (ex.Status == 409) // Conflict
            {
                // Entity already exists - duplicate event
                throw new DuplicateEventException(
                    "One or more events already exist in the stream", ex);
            }
            throw;
        }

        // Execute remaining reactor updates - best effort
        // Failures here don't fail the operation as reactors are idempotent
        foreach (var reactorBatch in reactorUpdateBatches)
        {
            if (reactorBatch.Any())
            {
                try
                {
                    await tableClient.SubmitTransactionAsync(reactorBatch);
                }
                catch (TableTransactionFailedException)
                {
                    // TODO: Add logging
                    // Reactor updates follow at-least-once semantics
                    // Failed updates will be retried on next grain activation
                }
            }
        }

        // Execute retention cleanup - also best effort
        await ExecuteRetentionDeletes(retentionBatch);
    }

    /// <summary>
    /// Executes retention delete operations in batches.
    /// Failures are ignored as retention is eventually consistent - 
    /// cleanup will be retried on future operations.
    /// </summary>
    private async Task ExecuteRetentionDeletes(List<TableTransactionAction> retentionDeletes)
    {
        // Split into MaxBatchSize chunks to respect transaction limits
        for (int i = 0; i < retentionDeletes.Count; i += MaxBatchSize)
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
    /// Creates a table entity for the projection with all necessary metadata.
    /// </summary>
    private TableEntity CreateProjectionEntity<TProjection>(string partitionKey, ProjectionEntry<TProjection> projection)
        where TProjection : notnull, IEventProjection<TProjection>
    {
        return new TableEntity(partitionKey, ProjectionRowKey)
        {
            // Serialize projection as JSON for flexibility
            ["Data"] = JsonSerializer.Serialize(projection.Projection),

            // Metadata for event processing state
            ["NextEventSequenceNumber"] = projection.NextEventSequenceNumber,
            ["StreamEventCount"] = projection.StreamEventCount,
            ["Timestamp"] = projection.Timestamp
        };
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
                    select: new[] { "RowKey", "SequenceNumber" }); // Minimize data transfer

                var allEvents = new List<TableEntity>();
                await foreach (var entity in query)
                {
                    if (entity.TryGetValue("SequenceNumber", out var seq) && seq is long sequence)
                    {
                        allEvents.Add(entity);
                    }
                }

                // Sort by sequence number and identify oldest events to delete
                var entitiesToDelete = allEvents
                    .OrderBy(e => (long)e["SequenceNumber"])
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
                select: new[] { "RowKey", "Timestamp", "EventId" });

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
                select: new[] { "RowKey", "EventId", "Timestamp" });

            // Group events by EventId
            var eventGroups = new Dictionary<string, List<(TableEntity Entity, DateTimeOffset Timestamp)>>();

            await foreach (var entity in query)
            {
                if (entity.TryGetValue("EventId", out var eventId) && eventId is string id)
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
                select: new[] { "RowKey", "ReactorStatus" });

            await foreach (var entity in query)
            {
                if (entity.TryGetValue("ReactorStatus", out var status) && status is string reactorStatusJson)
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