using Azure.Messaging.EventHubs;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams.EventHubs;

public sealed class EventHubsRecoveryTests
{
    [Fact]
    public void Inherited_event_factory_retains_enrichment()
    {
        var token = new EnrichedEventHubSequenceToken("123", 42, 0, DateTimeOffset.UnixEpoch, "provider", "trace");

        var perEvent = Assert.IsType<EnrichedEventHubSequenceToken>(token.CreateSequenceTokenForEvent(2));

        Assert.Equal(2, perEvent.EventIndex);
        Assert.Equal(0, token.EventIndex);
        Assert.Equal(token.EventHubOffset, perEvent.EventHubOffset);
        Assert.Equal(token.SequenceNumber, perEvent.SequenceNumber);
        Assert.Equal(token.EnqueuedTime, perEvent.EnqueuedTime);
        Assert.Equal(token.ProviderName, perEvent.ProviderName);
        Assert.Equal(token.TraceParent, perEvent.TraceParent);
    }

    [Theory]
    [InlineData(CheckpointKind.Standard)]
    [InlineData(CheckpointKind.Enriched)]
    [InlineData(CheckpointKind.HistoricalV1)]
    public void Cache_recovers_standard_enriched_and_historical_checkpoints(CheckpointKind kind)
    {
        var (cache, stream) = CreateCache();
        StreamSequenceToken checkpoint = kind switch
        {
            CheckpointKind.Standard => new EventHubSequenceTokenV2("123", 42, 0),
            CheckpointKind.Enriched => new EnrichedEventHubSequenceToken("123", 42, 0, DateTimeOffset.UnixEpoch, "provider"),
            _ => new EventSequenceToken(42, 0)
        };

        var cursor = cache.TryGetCursor(stream, checkpoint);

        Assert.Equal(QueueCacheCursorResultKind.Success, cursor.Kind);
        cache.TryGetNextMessageWithResult(cursor.Cursor!, out var batch);
        Assert.NotNull(batch);
        Assert.Equal("event", Assert.Single(batch.GetEvents<string>()).Item1);
        Assert.IsType<EnrichedEventHubSequenceToken>(batch.SequenceToken);
    }

    [Fact]
    public void Evicted_checkpoint_reports_cache_miss_after_new_message_is_retained()
    {
        var (cache, stream) = CreateCache();

        cache.RemoveOldestMessage();

        var result = cache.TryGetCursor(stream, new EnrichedEventHubSequenceToken("122", 41, 0, DateTimeOffset.UnixEpoch, "provider"));

        Assert.Equal(QueueCacheCursorResultKind.CacheMiss, result.Kind);
        Assert.NotNull(result.CacheMiss);
    }

    [Fact]
    public void Generic_tokens_remain_distinct_in_public_equality()
    {
        var token = new EnrichedEventHubSequenceToken("123", 42, 0, DateTimeOffset.UnixEpoch, "provider");
        var generic = new EventSequenceToken(42, 0);
        var genericV2 = new EventSequenceTokenV2(42, 0);

        var unrelated = new UnrelatedProviderToken(42, 0);
        Assert.False(token.Equals(unrelated));
        Assert.False(unrelated.Equals(token));
        Assert.False(token.Equals(generic));
        Assert.False(generic.Equals(token));
        Assert.False(token.Equals(genericV2));
        Assert.False(genericV2.Equals(token));
        Assert.Throws<ArgumentOutOfRangeException>(() => token.CompareTo(genericV2));
    }

    public enum CheckpointKind
    {
        Standard,
        Enriched,
        HistoricalV1
    }

    private sealed class UnrelatedProviderToken(long sequenceNumber, int eventIndex) : EventSequenceToken(sequenceNumber, eventIndex);

    private static (PooledQueueCache Cache, StreamId Stream) CreateCache()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(EnrichedEventHubAdapter).Assembly));
        var serializer = services.BuildServiceProvider().GetRequiredService<Serializer>();
        var adapter = new EnrichedEventHubAdapter("provider", serializer);
        var stream = StreamId.Create("orders", "one");
        var cache = new PooledQueueCache(adapter, NullLogger.Instance, null, null);
        cache.Add([CreateCachedMessage(adapter, stream, 41, 122), CreateCachedMessage(adapter, stream, 42, 123)], DateTime.UnixEpoch);
        return (cache, stream);
    }

    private static CachedMessage CreateCachedMessage(EnrichedEventHubAdapter adapter, StreamId stream, long sequenceNumber, long offset)
    {
        var outgoing = adapter.ToQueueMessage(stream, ["event"], null, []);
#pragma warning disable CS0618 // Broker-owned fields require the Event Hubs model factory.
        var received = EventHubsModelFactory.EventData(outgoing.EventBody, new Dictionary<string, object>(), new Dictionary<string, object>(), "one", sequenceNumber, offset, DateTimeOffset.UnixEpoch);
#pragma warning restore CS0618
        received.SetStreamNamespaceProperty("orders");
        var position = adapter.GetStreamPosition("0", received);
        return adapter.FromQueueMessage(position, received, DateTime.UnixEpoch, static size => new ArraySegment<byte>(new byte[size]));
    }
}
