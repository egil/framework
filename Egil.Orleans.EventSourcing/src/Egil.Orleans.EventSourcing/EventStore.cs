using Azure.Data.Tables;
using Egil.Orleans.EventSourcing.EventStores;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;
using System.Collections.Immutable;
using System.Text.Json;

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

    public bool HasUncommittedEvents { get; }
    public bool HasUnreactedEvents { get; }
    public long EventCount { get; }
    public long? LatestSequenceNumber { get; }
    public DateTimeOffset? LatestEventTimestamp { get; }

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

    public void AppendEvent<TEvent>(TEvent @event) where TEvent : notnull => throw new NotImplementedException();
    public IEventStream<TEvent> GetStream<TEvent>() where TEvent : notnull => throw new NotImplementedException();
    public ValueTask CommitAsync() => throw new NotImplementedException();
    public IAsyncEnumerable<IEventEntry> GetEventsAsync(QueryOptions? options = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public IAsyncEnumerable<IEventEntry<TEvent>> GetEventsAsync<TEvent>(QueryOptions? options = null, CancellationToken cancellationToken = default) where TEvent : notnull => throw new NotImplementedException();
    public ValueTask<TProjection> ApplyEventsAsync<TProjection>(TProjection projection, IEventHandlerContext context, CancellationToken cancellationToken = default) where TProjection : notnull => throw new NotImplementedException();
    public ValueTask ReactEventsAsync<TProjection>(TProjection projection, IEventReactContext context, CancellationToken cancellationToken = default) where TProjection : notnull => throw new NotImplementedException();
    public void Configure<TEventGrain, TProjection>(TEventGrain eventGrain, IServiceProvider serviceProvider, Action<IEventStoreConfigurator<TEventGrain, TProjection>> builderAction)
        where TEventGrain : IGrainBase
        where TProjection : notnull => throw new NotImplementedException();
}