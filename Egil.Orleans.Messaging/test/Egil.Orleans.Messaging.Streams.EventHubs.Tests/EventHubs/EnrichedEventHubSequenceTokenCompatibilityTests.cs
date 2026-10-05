using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams.EventHubs;

public sealed class EnrichedEventHubSequenceTokenCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Persisted_checkpoint_lookup_retains_provider_and_complete_stream_identity(bool enriched)
    {
        EventHubStreamSequenceTokenJsonConverters.Register();
        var stream = StreamId.Create("orders", "one");
        StreamSequenceToken token = enriched
            ? new EnrichedEventHubSequenceToken("123", 42, 2, DateTimeOffset.UnixEpoch, "provider")
            : new EventHubSequenceTokenV2("123", 42, 2);
        var tracker = new Egil.Orleans.Messaging.Tracking.MessageTracker();
        Assert.True(tracker.TryAcceptMessage(new StreamCursor("orders", token, "provider") { StreamId = stream }, out tracker));

        var restored = System.Text.Json.JsonSerializer.Deserialize<Egil.Orleans.Messaging.Tracking.MessageTracker>(System.Text.Json.JsonSerializer.Serialize(tracker))!;

        var checkpoint = restored.LatestStreamSequenceToken("provider", stream);
        Assert.NotNull(checkpoint);
        Assert.Equal(token.GetType(), checkpoint.GetType());
        Assert.True(token.Equals(checkpoint));
        Assert.Null(restored.LatestStreamSequenceToken("another-provider", stream));
        Assert.Null(restored.LatestStreamSequenceToken("provider", StreamId.Create("orders", "two")));
    }
    [Theory]
    [InlineData(41, 99)]
    [InlineData(42, 1)]
    public void Numeric_order_precedes_metadata_and_offset(long sequenceNumber, int eventIndex)
    {
        var earlier = new EventHubSequenceTokenV2("later-offset", sequenceNumber, eventIndex);
        var later = new EnrichedEventHubSequenceToken("earlier-offset", 42, 2, DateTimeOffset.UnixEpoch, "provider");
        var enrichedEarlier = new EnrichedEventHubSequenceToken("other", sequenceNumber, eventIndex, DateTimeOffset.MaxValue, "another-provider");

        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
        Assert.True(enrichedEarlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(enrichedEarlier) > 0);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Standard_and_enriched_positions_compare_symmetrically(bool legacy)
    {
        StreamSequenceToken standard = legacy
            ? new EventHubSequenceToken("offset", 42, 2)
            : new EventHubSequenceTokenV2("offset", 42, 2);
        var enriched = new EnrichedEventHubSequenceToken("offset", 42, 2, DateTimeOffset.UnixEpoch, "provider");

        Assert.True(standard.Equals(enriched));
        Assert.True(enriched.Equals(standard));
        Assert.Equal(0, standard.CompareTo(enriched));
        Assert.Equal(0, enriched.CompareTo(standard));
        Assert.Equal(standard.GetHashCode(), enriched.GetHashCode());
    }
}



