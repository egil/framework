using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.Handlers;
using Egil.Orleans.EventSourcing.Reactors;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.EventSourcing;

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
internal class EventStore<TProjection> : IEventStore<TProjection>
    where TProjection : notnull, IEventProjection<TProjection>
{
    // RowKey prefixes - Using "!" ensures these sort first, before any event data
    // ASCII ordering: ! (33) < numbers < letters < ~ (126)
    private const string ProjectionRowKey = "!projection";
    private const string StreamMetadataPrefix = "!stream";

    // Separator character used in RowKey construction. # is safe as it's not used in base64 encoding
    private const char StreamSeparator = '_';

    // Azure Table Storage transaction limit as per documentation:
    // https://docs.microsoft.com/en-us/rest/api/storageservices/performing-entity-group-transactions
    private const int MaxBatchSize = 100;

    private readonly Dictionary<long, IEventEntry> uncommittedEvents = [];
    private readonly TableClient tableClient;
    private readonly IGrainStorageSerializer serializer;
    private readonly IOptions<ClusterOptions> clusterOptions;
    private ProjectionEntry<TProjection> projectionEntry = ProjectionEntry<TProjection>.CreateDefault();

    private IEventStream<TProjection>[] streams = Array.Empty<IEventStream<TProjection>>();
    private GrainId grainId;

    public bool HasUnappliedEvents => LatestSequenceNumber > projectionEntry.EventSequenceNumber;

    public bool HasUnreactedEvents => uncommittedEvents.Values.Any(events => events.ReactorStatus.Any(state => state.Value.Status is not ReactorOperationStatus.CompleteSuccessful));

    public long LatestSequenceNumber { get; private set; } = 0;

    public TProjection Projection => projectionEntry.Projection;

    public EventStore(
        TableClient tableClient,
        IGrainStorageSerializer serializer,
        IOptions<ClusterOptions> clusterOptions)
    {
        this.tableClient = tableClient;
        this.serializer = serializer;
        this.clusterOptions = clusterOptions;
    }

    public void Configure<TEventGrain>(TEventGrain eventGrain, IServiceProvider serviceProvider, Action<IEventStoreConfigurator<TEventGrain, TProjection>> builderAction)
        where TEventGrain : IGrainBase
    {
        var eventStreamBuilder = new EventStreamBuilder<TEventGrain, TProjection>(eventGrain, serviceProvider);
        builderAction.Invoke(eventStreamBuilder);
        streams = eventStreamBuilder.Build();
        grainId = eventGrain.GrainContext.GrainId;
    }

    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull
    {
        string? matchingStreams = null;
        var matchingStreamsCount = 0;
        IEventEntry? eventEntry = null;
        IEventStream<TProjection>? eventStream = null;
        foreach (var stream in streams)
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
            eventEntry = stream.CreateEventEntry(@event, ++LatestSequenceNumber);
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

        if (eventEntry is not null && eventStream is not null && uncommittedEvents.ContainsKey(eventEntry.SequenceNumber))
        {
            uncommittedEvents[eventEntry.SequenceNumber] = eventEntry;
        }
    }

    public async ValueTask ApplyEventsAsync(IEventHandlerContext context, CancellationToken cancellationToken = default)
    {
        if (uncommittedEvents.Count == 0)
        {
            return;
        }

        var projection = projectionEntry.Projection;
        var projectionEventSequenceNumber = projectionEntry.EventSequenceNumber;

        await foreach (var eventEntry in GetEventsAsync(new EventQueryOptions { IncludeUncommitted = true, FromSequenceNumber = projectionEventSequenceNumber + 1 }, cancellationToken))
        {
            var stream = streams.FirstOrDefault(s => s.Matches(eventEntry.Event));
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

    public async ValueTask ReactEventsAsync(IEventReactContext context, CancellationToken cancellationToken = default)
    {
        var unreactedEvents = await GetEventsAsync(new EventQueryOptions { IncludeUnreacted = true }, cancellationToken)
            .ToImmutableArrayAsync(cancellationToken);

        var reactedEvents = unreactedEvents;
        foreach (var stream in streams)
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
    }

    public async IAsyncEnumerable<TEvent> GetEventsAsync<TEvent>(EventQueryOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default) where TEvent : notnull
    {
        await foreach (var eventEntry in GetEventsAsync(options, cancellationToken))
        {
            if (eventEntry.Event is TEvent castEvent)
            {
                yield return castEvent;
            }
        }
    }

    public async ValueTask CommitAsync()
    {
        if (uncommittedEvents.Count == 0)
        {
            return;
        }

        var partitionKey = CreatePartitionKey(grainId);
        var batch = new List<TableTransactionAction>(uncommittedEvents.Count + 1);

        // Add projection entry
        batch.Add(
            new TableTransactionAction(
                TableTransactionActionType.UpsertMerge,
                new TableEntity(partitionKey, ProjectionRowKey)
                {
                    ["Projection"] = serializer.Serialize(projectionEntry.Projection),
                    ["ProjectionEventSequenceNumber"] = projectionEntry.EventSequenceNumber,
                    ["LatestSequenceNumber"] = LatestSequenceNumber,
                    ["UnreactedEventsCount"] = uncommittedEvents.Values.Count(events => events.ReactorStatus.Any(state => state.Value.Status is not ReactorOperationStatus.CompleteSuccessful)),
                },
                projectionEntry.ETag));

        // Add each uncommitted event
        foreach (var @event in uncommittedEvents.Values)
        {
            var rowKey = CreateEventRowKey(@event.Stream, @event);
            batch.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, new TableEntity(partitionKey, rowKey)
            {
                ["Event"] = serializer.Serialize(@event.Event),
                ["SequenceNumber"] = @event.SequenceNumber,
                ["EventId"] = @event.EventId,
                ["Timestamp"] = @event.Timestamp,
            }));
        }
        // Execute the batch transaction
        return new ValueTask(tableClient.SubmitTransactionAsync(batch, cancellationToken));
    }

    private IAsyncEnumerable<IEventEntry> GetEventsAsync(EventQueryOptions options, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    private string CreatePartitionKey(GrainId grainId)
        => $"{clusterOptions.Value.ClusterId}{StreamSeparator}{grainId}".SanitizeKeyPropertyValue(StreamSeparator);

    private string CreateStreamMetadataRowKey(string streamName)
        => $"{StreamMetadataPrefix}{StreamSeparator}{streamName}".SanitizeKeyPropertyValue('*');

    private string CreateEventRowKey(IEventEntry @event)
    {
        // Regular sequence number (not inverted) for efficient range queries
        // This allows querying from a specific sequence number onwards
        return $"{@event.StreamName}{StreamSeparator}{@event.SequenceNumber:D19}{StreamSeparator}{@event.EventId}".SanitizeKeyPropertyValue('*');
    }
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