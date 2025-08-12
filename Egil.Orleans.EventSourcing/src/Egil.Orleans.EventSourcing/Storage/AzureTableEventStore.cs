using Azure;
using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.Configurations;
using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Reactors;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing.Storage;

internal class AzureTableEventStore<TProjection> : IEventStore<TProjection>, ILifecycleParticipant<IGrainLifecycle>, ILifecycleObserver
    where TProjection : notnull, IEventProjection<TProjection>
{
    // RowKey prefixes - Using "!" ensures these sort first, before any event data
    // ASCII ordering: ! (33) < numbers < letters < ~ (126)
    private const string ProjectionRowKey = "!projection";

    // Separator character used in RowKey construction. # is safe as it's not used in base64 encoding
    private const char StreamSeparator = '_';

    // Azure Table Storage transaction limit as per documentation:
    // https://docs.microsoft.com/en-us/rest/api/storageservices/performing-entity-group-transactions
    private const int MaxBatchSize = 100;

    private readonly SortedDictionary<long, IEventEntry> uncommittedEvents = [];
    private readonly TableClient tableClient;
    private readonly IGrainStorageSerializer serializer;
    private readonly IOptions<ClusterOptions> clusterOptions;
    private readonly TimeProvider timeProvider;
    private ProjectionEntry<TProjection> projectionEntry = ProjectionEntry<TProjection>.CreateDefault();
    private Dictionary<string, IEventStream<TProjection>> streams = [];
    private GrainId grainId;
    private int uncompletedReactStatusCount = 0;
    private long latestSequenceNumber = 0;

    public bool HasUnappliedEvents => latestSequenceNumber > projectionEntry.EventSequenceNumber;

    public bool HasUnreactedEvents => uncompletedReactStatusCount > 0;

    public TProjection Projection => projectionEntry.Projection;

    public AzureTableEventStore(
        TableClient tableClient,
        IGrainStorageSerializer serializer,
        IOptions<ClusterOptions> clusterOptions,
        TimeProvider timeProvider)
    {
        this.tableClient = tableClient;
        this.serializer = serializer;
        this.clusterOptions = clusterOptions;
        this.timeProvider = timeProvider;
    }

    public void Configure<TEventGrain>(
        GrainId grainId,
        TEventGrain eventGrain,
        IServiceProvider serviceProvider,
        Action<IEventStoreConfigurator<TEventGrain, TProjection>> builderAction)
        where TEventGrain : IGrainBase
    {
        var eventStreamBuilder = new EventStreamBuilder<TEventGrain, TProjection>(eventGrain, serviceProvider, timeProvider);
        builderAction.Invoke(eventStreamBuilder);
        streams = eventStreamBuilder.Build().ToDictionary(x => x.Name, x => x);
        this.grainId = grainId;
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Try to load the projection/head row
        var response = await tableClient.GetEntityIfExistsAsync<TableEntity>(
            CreatePartitionKey(grainId),
            ProjectionRowKey,
            select: ["Projection", "ProjectionEventSequenceNumber", "LatestSequenceNumber", "UnreactedEventsCount"],
            cancellationToken: cancellationToken);

        if (!response.HasValue)
        {
            projectionEntry = ProjectionEntry<TProjection>.CreateDefault();
            return;
        }

        latestSequenceNumber = response.Value?.GetInt64("LatestSequenceNumber") ?? 0;
        uncompletedReactStatusCount = response.Value?.GetInt32("UnreactedEventsCount") ?? 0;
        projectionEntry = DeserializeProjectionEntity(response);

        if (HasUnappliedEvents)
        {
            await ApplyEventsAsync(new ReplayEventHandlerContext<TProjection>(this, grainId), cancellationToken);
        }
    }

    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
    {
        string? matchingStreams = null;
        var matchingStreamsCount = 0;
        IEventEntry? eventEntry = null;
        IEventStream<TProjection>? eventStream = null;
        foreach (var (_, stream) in streams)
        {
            if (!stream.Matches(@event))
            {
                continue;
            }

            if (matchingStreams is not null)
            {
                matchingStreams += $", {stream.Name}";
                matchingStreamsCount++;
                continue;
            }

            matchingStreams = stream.Name;
            matchingStreamsCount = 1;
            eventEntry = stream.CreateEventEntry(@event, ++latestSequenceNumber);
            eventStream = stream;
        }

        if (matchingStreams is null)
        {
            throw new InvalidOperationException($"No event stream found for event type {typeof(TEvent).FullName}.");
        }

        if (matchingStreamsCount > 1)
        {
            throw new InvalidOperationException($"Event type {typeof(TEvent).FullName} matches multiple streams: {matchingStreams}. Please ensure only one stream matches each event type.");
        }

        if (eventEntry is not null && eventStream is not null)
        {
            uncommittedEvents[eventEntry.SequenceNumber] = eventEntry;
            uncompletedReactStatusCount = uncommittedEvents.Values.Count(events => events.ReactorStatus.Any(state => state.Value.Status is not ReactorOperationStatus.CompleteSuccessful));
        }
    }

    public async ValueTask ApplyEventsAsync(IEventHandlerContext context, CancellationToken cancellationToken = default)
    {
        // Store the original projection state before applying events
        var originalProjection = projectionEntry.Projection;
        var originalProjectionEventSequenceNumber = projectionEntry.EventSequenceNumber;

        var projection = projectionEntry.Projection;
        var projectionEventSequenceNumber = projectionEntry.EventSequenceNumber;

        try
        {
            await foreach (var eventEntry in GetEventsAsync(new EventQueryOptions { FromSequenceNumber = projectionEventSequenceNumber + 1 }, cancellationToken))
            {
                var stream = streams.FirstOrDefault(s => s.Value.Matches(eventEntry.Event)).Value;
                if (stream is null)
                {
                    continue;
                }

                projection = await stream.ApplyEventsAsync(eventEntry.Event, projection, context, cancellationToken);
                projectionEventSequenceNumber = eventEntry.SequenceNumber;
            }

            projectionEntry = projectionEntry with
            {
                Projection = projection,
                EventSequenceNumber = projectionEventSequenceNumber,
            };
        }
        catch
        {
            // Rollback: restore the projection to its original state before ApplyEventsAsync was called
            projectionEntry = projectionEntry with
            {
                Projection = originalProjection,
                EventSequenceNumber = originalProjectionEventSequenceNumber,
            };
            throw;
        }
    }

    public async ValueTask ReactEventsAsync(IEventReactContext context, CancellationToken cancellationToken = default)
    {
        var unreactedEvents = await GetEventsAsync(new EventQueryOptions { IsUnreacted = true }, cancellationToken)
            .ToImmutableArrayAsync(cancellationToken);

        var reactedEvents = unreactedEvents;
        foreach (var (_, stream) in streams)
        {
            reactedEvents = await stream.ReactEventsAsync(
                reactedEvents,
                projectionEntry.Projection,
                context,
                cancellationToken);
        }

        foreach (var @event in reactedEvents)
        {
            uncommittedEvents[@event.SequenceNumber] = @event;
        }

        uncompletedReactStatusCount = uncommittedEvents.Values.Count(events => events.ReactorStatus.Any(state => state.Value.Status is not ReactorOperationStatus.CompleteSuccessful));
    }

    public async IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default) where TEvent : notnull
    {
        var streamName = streams.Values.FirstOrDefault(stream => stream.Matches(default(TEvent)))?.Name;
        await foreach (var eventEntry in GetEventsAsync(options with { StreamName = streamName }, cancellationToken))
        {
            if (eventEntry.Event is TEvent castEvent)
            {
                yield return castEvent;
            }
        }
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        if (uncommittedEvents.Count == 0)
        {
            return;
        }

        var partitionKey = CreatePartitionKey(grainId);
        var batch = new List<TableTransactionAction>(uncommittedEvents.Count > MaxBatchSize ? MaxBatchSize : uncommittedEvents.Count + 1)
        {
            CreateHeadRowTransactionAction(partitionKey, projectionEntry)
        };

        // Group events by stream and apply distinct retention per stream
        var eventsToCommit = new List<IEventEntry>();

        foreach (var streamGroup in uncommittedEvents.Values
            .Where(x => x.ETag == ETag.All)
            .GroupBy(e => e.StreamName))
        {
            var retention = streams[streamGroup.Key].Retention;
            var filteredByRetention = streamGroup.Where(e => RetentionPredicate(e, retention));

            if (retention.LatestDistinct)
            {
                // For streams with LatestDistinct, we need to check for existing events in storage
                var filteredEvents = await ApplyDistinctRetentionWithStorageCheck(filteredByRetention, retention, partitionKey, cancellationToken);
                eventsToCommit.AddRange(filteredEvents);
            }
            else
            {
                eventsToCommit.AddRange(ApplyDistinctRetention(filteredByRetention, retention));
            }
        }

        foreach (var eventEntry in eventsToCommit)
        {
            if (batch.Count < MaxBatchSize)
            {
                batch.Add(CreateEventTransactionAction(partitionKey, eventEntry));
            }
            else
            {
                throw new InvalidOperationException($"The number of uncommitted new events exceeds Azure Table Storage limit of {MaxBatchSize} operations. Cannot create a single atomic batch operation that ensures all are saved.");
            }
        }

        var response = await tableClient.SubmitTransactionAsync(batch, cancellationToken);
        projectionEntry = projectionEntry with
        {
            ETag = response.Value[0].Headers.ETag.GetValueOrDefault(ETag.All),
            Timestamp = response.Value[0].Headers.Date,
        };
        uncommittedEvents.Clear();
    }

    private ProjectionEntry<TProjection> DeserializeProjectionEntity(NullableResponse<TableEntity> response)
    {
        if (!response.HasValue || !(response.Value is { } entity))
        {
            return ProjectionEntry<TProjection>.CreateDefault();
        }

        try
        {
            return entity.GetBinaryData("Projection") is { } projectionData
                && entity.GetInt64("ProjectionEventSequenceNumber") is { } projectionEventSequenceNumber
                && serializer.Deserialize<TProjection>(projectionData) is { } projection
                ? new ProjectionEntry<TProjection>
                {
                    EventSequenceNumber = projectionEventSequenceNumber,
                    Projection = projection,
                    Timestamp = entity.Timestamp,
                    ETag = ETag.All
                }
                : ProjectionEntry<TProjection>.CreateDefault();
        }
        catch
        {
            return ProjectionEntry<TProjection>.CreateDefault();
        }
    }

    private TableTransactionAction CreateHeadRowTransactionAction(string partitionKey, ProjectionEntry<TProjection> projectionEntry) => new TableTransactionAction(
        TableTransactionActionType.UpsertMerge,
        new TableEntity(partitionKey, ProjectionRowKey)
        {
            ["Projection"] = serializer.Serialize(projectionEntry.Projection).ToArray(),
            ["ProjectionEventSequenceNumber"] = projectionEntry.EventSequenceNumber,
            ["LatestSequenceNumber"] = latestSequenceNumber,
            ["UnreactedEventsCount"] = uncompletedReactStatusCount,
        },
        projectionEntry.ETag);

    private TableTransactionAction CreateEventTransactionAction(string partitionKey, IEventEntry eventEntry)
    {
        var rowKey = CreateEventRowKey(eventEntry);
        return new TableTransactionAction(
            TableTransactionActionType.UpsertMerge,
            new TableEntity(partitionKey, rowKey)
            {
                ["StreamName"] = eventEntry.StreamName,
                ["Event"] = streams[eventEntry.StreamName].SerializeEvent(serializer, eventEntry),
                ["SequenceNumber"] = eventEntry.SequenceNumber,
                ["EventId"] = eventEntry.EventId,
                ["EventTimestamp"] = eventEntry.EventTimestamp,
                ["ReactorStatus"] = serializer.Serialize(eventEntry.ReactorStatus).ToArray(),
                ["UnsuccessfulReactStatusCount"] = eventEntry.ReactorStatus.Count(x => x.Value.Status is not ReactorOperationStatus.CompleteSuccessful)
            },
            eventEntry.ETag);
    }

    private async IAsyncEnumerable<IEventEntry> GetEventsAsync(EventQueryOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filter = CreateTableQueryFilter(options);
        var uncommittedEvents = FilterUncommittedEvents(options).OrderBy(e => e.SequenceNumber).ToList();

        // Create a set of EventIds from uncommitted events for efficient lookup
        var uncommittedEventIds = new HashSet<string>(
            uncommittedEvents
                .Where(e => e.EventId is not null)
                .Select(e => e.EventId!));

        // Collect all committed events from storage first
        var committedEvents = new List<IEventEntry>();
        await foreach (var entity in tableClient.QueryAsync<TableEntity>(
            filter: filter,
            maxPerPage: 100,
            cancellationToken: cancellationToken))
        {
            if (!entity.TryGetValue("SequenceNumber", out var seqObj) || seqObj is not long sequenceNumber)
                continue;

            if (!entity.TryGetValue("StreamName", out var streamNameObj) || streamNameObj is not string streamName)
                continue;

            if (!entity.TryGetValue("Event", out var eventData) || eventData is not byte[] eventBytes)
                continue;

            if (!streams.TryGetValue(streamName, out var stream))
                throw new InvalidOperationException($"Event in storage is associated with unconfigured stream '{streamName}'.");

            var reactorStatus = ImmutableDictionary<string, ReactorState>.Empty;
            if (entity.TryGetValue("ReactorStatus", out var statusData) && statusData is byte[] statusBytes)
            {
                var status = serializer.Deserialize<ImmutableDictionary<string, ReactorState>>(BinaryData.FromBytes(statusBytes));
                if (status is not null)
                    reactorStatus = status;
            }

            var eventEntity = stream.CreateEventEntry(serializer, eventBytes, sequenceNumber, reactorStatus, entity.Timestamp, entity.ETag);

            // Skip storage events if we have a newer version in uncommitted events
            if (eventEntity.EventId is not null && uncommittedEventIds.Contains(eventEntity.EventId))
                continue;

            committedEvents.Add(eventEntity);
        }

        // Now merge committed and uncommitted events in sequence order
        var allEvents = committedEvents
            .Concat(uncommittedEvents)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

        // Group events by stream to apply stream-specific retention
        var eventsToYield = new List<IEventEntry>();
        foreach (var streamGroup in allEvents.GroupBy(e => e.StreamName))
        {
            var stream = streams[streamGroup.Key];
            var retention = stream.Retention;
            
            // First filter by retention predicate (UntilReactedSuccessfully)
            var filteredEvents = streamGroup.Where(e => RetentionPredicate(e, retention));
            
            // Then apply LatestDistinct retention if configured
            if (retention.LatestDistinct)
            {
                filteredEvents = ApplyDistinctRetention(filteredEvents, retention);
            }
            
            eventsToYield.AddRange(filteredEvents);
        }

        // Yield events in sequence order
        foreach (var eventEntry in eventsToYield.OrderBy(e => e.SequenceNumber))
        {
            yield return eventEntry;
        }
    }

    private string CreateTableQueryFilter(EventQueryOptions options)
    {
        var partitionKey = CreatePartitionKey(grainId);

        // Build the query filter
        var filters = new List<string>
        {
            $"PartitionKey eq '{partitionKey}'"
        };

        // Exclude projection row
        filters.Add($"RowKey gt '{ProjectionRowKey}'");

        // Apply row key range filters when stream name is specified
        if (options.StreamName is not null)
        {
            var fromRowKey = options.FromSequenceNumber.HasValue
                ? $"{options.StreamName}{StreamSeparator}{options.FromSequenceNumber.Value:D19}"
                : $"{options.StreamName}{StreamSeparator}";

            var toRowKey = options.ToSequenceNumber.HasValue
                ? $"{options.StreamName}{StreamSeparator}{options.ToSequenceNumber.Value:D19}{StreamSeparator}\uffff"
                : $"{options.StreamName}{StreamSeparator}\uffff";

            filters.Add($"RowKey ge '{fromRowKey}'");
            filters.Add($"RowKey lt '{toRowKey}'");
        }
        else
        {
            // Use column filters for sequence numbers when stream is not specified
            if (options.FromSequenceNumber.HasValue)
            {
                filters.Add($"SequenceNumber ge {options.FromSequenceNumber.Value}L");
            }

            if (options.ToSequenceNumber.HasValue)
            {
                filters.Add($"SequenceNumber le {options.ToSequenceNumber.Value}L");
            }
        }

        // Filter by reactor status
        if (options.IsUnreacted.HasValue)
        {
            filters.Add(options.IsUnreacted.Value
                ? "UnsuccessfulReactStatusCount gt 0"
                : "UnsuccessfulReactStatusCount eq 0");
        }

        var filter = string.Join(" and ", filters);
        return filter;
    }

    private IEnumerable<IEventEntry> FilterUncommittedEvents(EventQueryOptions options)
    {
        foreach (var kvp in uncommittedEvents)
        {
            var entry = kvp.Value;

            if (options.FromSequenceNumber.HasValue && entry.SequenceNumber < options.FromSequenceNumber.Value)
                continue;
            if (options.ToSequenceNumber.HasValue && entry.SequenceNumber > options.ToSequenceNumber.Value)
                continue;
            if (options.StreamName is not null && !entry.StreamName.Equals(options.StreamName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.IsUnreacted.HasValue)
            {
                var hasUnreacted = entry.ReactorStatus.Any(state => state.Value.Status is not ReactorOperationStatus.CompleteSuccessful);
                if (hasUnreacted != options.IsUnreacted.Value)
                    continue;
            }

            yield return entry;
        }
    }

    private bool RetentionPredicate(IEventEntry entry, IEventStreamRetention retention)
    {
        // Check if event should be filtered out based on UntilReactedSuccessfully policy
        if (retention.UntilReactedSuccessfully)
        {
            // If all reactors have completed successfully, filter out the event
            if (entry.ReactorStatus.IsEmpty || entry.ReactorStatus.Values.All(x => x.Status is ReactorOperationStatus.CompleteSuccessful))
            {
                return false;
            }
        }

        // Event passes retention policy
        return true;
    }

    private IEnumerable<IEventEntry> ApplyDistinctRetention(IEnumerable<IEventEntry> events, IEventStreamRetention retention)
    {
        if (!retention.LatestDistinct)
        {
            return events;
        }

        return events
            .Where(e => e.EventId is not null)
            .GroupBy(e => e.EventId)
            .Select(group => group
                .OrderByDescending(e => e.EventTimestamp ?? e.Timestamp ?? DateTimeOffset.MinValue)
                .First())
            .Concat(events.Where(e => e.EventId is null));
    }

    private async ValueTask<IEnumerable<IEventEntry>> ApplyDistinctRetentionWithStorageCheck(
        IEnumerable<IEventEntry> newEvents,
        IEventStreamRetention retention,
        string partitionKey,
        CancellationToken cancellationToken)
    {
        if (!retention.LatestDistinct)
        {
            return newEvents;
        }

        var eventsWithIds = newEvents.Where(e => e.EventId is not null).ToList();
        var eventsWithoutIds = newEvents.Where(e => e.EventId is null).ToList();

        if (eventsWithIds.Count == 0)
        {
            return eventsWithoutIds;
        }

        // Query storage for existing events with the same EventIds
        var eventIdsToCheck = eventsWithIds.Select(e => e.EventId!).Distinct().ToList();
        var existingEvents = new Dictionary<string, IEventEntry>();

        // Build a query to find existing events with matching EventIds
        // We'll query by EventId column which should be indexed
        foreach (var eventId in eventIdsToCheck)
        {
            var filter = $"PartitionKey eq '{partitionKey}' and EventId eq '{eventId}' and RowKey gt '{ProjectionRowKey}'";

            await foreach (var entity in tableClient.QueryAsync<TableEntity>(
                filter: filter,
                maxPerPage: 100,
                cancellationToken: cancellationToken))
            {
                if (!entity.TryGetValue("SequenceNumber", out var seqObj) || seqObj is not long sequenceNumber)
                    continue;

                if (!entity.TryGetValue("StreamName", out var streamNameObj) || streamNameObj is not string streamName)
                    continue;

                if (!entity.TryGetValue("Event", out var eventData) || eventData is not byte[] eventBytes)
                    continue;

                if (!streams.TryGetValue(streamName, out var stream))
                    continue;

                var reactorStatus = ImmutableDictionary<string, ReactorState>.Empty;
                if (entity.TryGetValue("ReactorStatus", out var statusData) && statusData is byte[] statusBytes)
                {
                    var status = serializer.Deserialize<ImmutableDictionary<string, ReactorState>>(BinaryData.FromBytes(statusBytes));
                    if (status is not null)
                        reactorStatus = status;
                }

                var existingEvent = stream.CreateEventEntry(serializer, eventBytes, sequenceNumber, reactorStatus, entity.Timestamp, entity.ETag);

                // Keep the latest version based on timestamp
                if (!existingEvents.TryGetValue(eventId, out var current) ||
                    (existingEvent.EventTimestamp ?? existingEvent.Timestamp ?? DateTimeOffset.MinValue) >
                    (current.EventTimestamp ?? current.Timestamp ?? DateTimeOffset.MinValue))
                {
                    existingEvents[eventId] = existingEvent;
                }
            }
        }

        // Now filter new events - only keep those that are newer than existing ones
        var result = new List<IEventEntry>();

        foreach (var newEvent in eventsWithIds)
        {
            if (existingEvents.TryGetValue(newEvent.EventId!, out var existingEvent))
            {
                // Compare timestamps to decide which one to keep
                var newTimestamp = newEvent.EventTimestamp ?? newEvent.Timestamp ?? DateTimeOffset.MinValue;
                var existingTimestamp = existingEvent.EventTimestamp ?? existingEvent.Timestamp ?? DateTimeOffset.MinValue;

                if (newTimestamp > existingTimestamp)
                {
                    // The new event is newer, keep it
                    result.Add(newEvent);
                }
                // If existing is newer or equal, don't add the new event (it will be filtered out)
            }
            else
            {
                // No existing event with this EventId, keep the new one
                result.Add(newEvent);
            }
        }

        // Apply regular distinct retention to handle duplicates within the new events
        result = ApplyDistinctRetention(result, retention).ToList();

        // Add events without EventIds
        result.AddRange(eventsWithoutIds);

        return result;
    }

    private string CreatePartitionKey(GrainId grainId)
        => $"{clusterOptions.Value.ClusterId}{StreamSeparator}{grainId}".SanitizeKeyPropertyValue(StreamSeparator);

    private string CreateEventRowKey(IEventEntry @event)
    {
        // Regular sequence number (not inverted) for efficient range queries
        // This allows querying from a specific sequence number onwards
        return $"{@event.StreamName}{StreamSeparator}{@event.SequenceNumber:D19}{StreamSeparator}{@event.EventId}".SanitizeKeyPropertyValue('*');
    }

    void ILifecycleParticipant<IGrainLifecycle>.Participate(IGrainLifecycle observer) => observer.Subscribe(GrainLifecycleStage.SetupState, this);

    Task ILifecycleObserver.OnStart(CancellationToken cancellationToken) => InitializeAsync(cancellationToken).AsTask();

    Task ILifecycleObserver.OnStop(CancellationToken cancellationToken) => Task.CompletedTask;
}

file static class AsyncEnumerableExtensions
{
    public static async ValueTask<ImmutableArray<T>> ToImmutableArrayAsync<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken = default)
    {
        var list = new List<T>();

        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            list.Add(item);
        }

        return list.ToImmutableArray();
    }
}