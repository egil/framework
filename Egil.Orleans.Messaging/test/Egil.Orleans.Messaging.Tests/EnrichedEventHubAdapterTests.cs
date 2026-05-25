using System.Diagnostics;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;

namespace Egil.Orleans.Messaging.Tests;

public sealed class EnrichedEventHubAdapterTests
{
    [Fact]
    public void ToQueueMessage_stamps_traceparent_when_activity_is_active()
    {
        var adapter = CreateAdapter();
        using var activity = new Activity("publish").Start();

        var message = adapter.ToQueueMessage(
            StreamId.Create("orders", "one"),
            ["event-1"],
            null!,
            []);

        var value = Assert.Contains("traceparent", message.Properties);
        Assert.Equal(activity.Id, Assert.IsType<string>(value));
    }

    [Fact]
    public void ToQueueMessage_does_not_stamp_traceparent_when_no_activity_is_active()
    {
        var adapter = CreateAdapter();

        var message = adapter.ToQueueMessage(
            StreamId.Create("orders", "one"),
            ["event-1"],
            null!,
            []);

        Assert.DoesNotContain("traceparent", message.Properties);
    }

    [Fact]
    public void GetSequenceToken_returns_enriched_event_hub_sequence_token()
    {
        var adapter = CreateAdapter();
        var cachedMessage = new CachedMessage
        {
            SequenceNumber = 10,
            EventIndex = 2,
            EnqueueTimeUtc = new DateTime(2026, 5, 24, 19, 0, 0, DateTimeKind.Utc)
        };

        var token = adapter.GetSequenceToken(ref cachedMessage);

        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(token);
        Assert.Equal(10, enriched.SequenceNumber);
        Assert.Equal(2, enriched.EventIndex);
        Assert.Equal("provider-a", enriched.ProviderName);
        Assert.Null(enriched.TraceParent);
    }

    [Fact]
    public void GetSequenceToken_treats_unspecified_enqueue_time_as_utc()
    {
        var adapter = CreateAdapter();
        var cachedMessage = new CachedMessage
        {
            SequenceNumber = 11,
            EventIndex = 0,
            EnqueueTimeUtc = new DateTime(2026, 5, 24, 19, 0, 0, DateTimeKind.Unspecified)
        };

        var token = adapter.GetSequenceToken(ref cachedMessage);

        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(token);
        Assert.Equal(TimeSpan.Zero, enriched.EnqueuedTime.Offset);
        Assert.Equal(new DateTime(2026, 5, 24, 19, 0, 0, DateTimeKind.Utc), enriched.EnqueuedTime.UtcDateTime);
    }

    [Fact]
    public void GetSequenceToken_converts_local_enqueue_time_to_utc()
    {
        var adapter = CreateAdapter();
        var localTime = new DateTime(2026, 5, 24, 19, 0, 0, DateTimeKind.Local);
        var cachedMessage = new CachedMessage
        {
            SequenceNumber = 12,
            EventIndex = 0,
            EnqueueTimeUtc = localTime
        };

        var token = adapter.GetSequenceToken(ref cachedMessage);

        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(token);
        Assert.Equal(TimeSpan.Zero, enriched.EnqueuedTime.Offset);
        Assert.Equal(localTime.ToUniversalTime(), enriched.EnqueuedTime.UtcDateTime);
    }

    [Fact]
    public void GetStreamPosition_extracts_traceparent_from_event_data_properties()
    {
        var adapter = CreateAdapter();
        var traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
#pragma warning disable CS0618 // Test-only factory path for broker-owned EventData fields in the installed Event Hubs package.
        var message = EventHubsModelFactory.EventData(
            BinaryData.FromString("payload"),
            new Dictionary<string, object>
            {
                ["traceparent"] = traceParent
            },
            systemProperties: new Dictionary<string, object>(),
            partitionKey: "one",
            sequenceNumber: 42,
            offset: 123,
            enqueuedTime: new DateTimeOffset(2026, 5, 24, 19, 0, 0, TimeSpan.Zero));
#pragma warning restore CS0618
        message.SetStreamNamespaceProperty("orders");

        var position = adapter.GetStreamPosition("0", message);

        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(position.SequenceToken);
        Assert.Equal(traceParent, enriched.TraceParent);
        Assert.Equal("provider-a", enriched.ProviderName);
    }

    private static EnrichedEventHubAdapter CreateAdapter()
    {
        var services = new ServiceCollection();
        services.AddSerializer(static _ => { });
        var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<Serializer>();

        return new EnrichedEventHubAdapter("provider-a", serializer);
    }
}
