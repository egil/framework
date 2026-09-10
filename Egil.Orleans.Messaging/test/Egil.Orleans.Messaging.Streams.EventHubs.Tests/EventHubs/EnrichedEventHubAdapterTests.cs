using System.Diagnostics;
using Azure.Messaging.EventHubs;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams.EventHubs;

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

    [Fact]
    public void GetBatchContainer_delivers_enriched_tokens_from_cached_message()
    {
        var adapter = CreateAdapter(out var serializer);
        var streamId = StreamId.Create("orders", "one");
        var traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var enqueuedTime = new DateTimeOffset(2026, 5, 24, 19, 0, 0, TimeSpan.Zero);
        var outgoing = adapter.ToQueueMessage(
            streamId,
            ["event-1", "event-2"],
            null!,
            []);
        var properties = new Dictionary<string, object>(outgoing.Properties)
        {
            ["traceparent"] = traceParent
        };
#pragma warning disable CS0618 // Test-only factory path for broker-owned EventData fields in the installed Event Hubs package.
        var received = EventHubsModelFactory.EventData(
            outgoing.EventBody,
            properties,
            systemProperties: new Dictionary<string, object>(),
            partitionKey: "one",
            sequenceNumber: 42,
            offset: 123,
            enqueuedTime: enqueuedTime);
#pragma warning restore CS0618
        var position = adapter.GetStreamPosition("0", received);
        var cachedMessage = adapter.FromQueueMessage(
            position,
            received,
            new DateTime(2026, 5, 24, 19, 0, 1, DateTimeKind.Utc),
            static size => new ArraySegment<byte>(new byte[size]));

        var serialized = serializer.SerializeToArray<IBatchContainer>(
            adapter.GetBatchContainer(ref cachedMessage));
        var container = serializer.Deserialize<IBatchContainer>(serialized);
        Assert.NotNull(container);
        var delivered = container.GetEvents<string>().ToArray();

        Assert.Equal(["event-1", "event-2"], delivered.Select(item => item.Item1));
        AssertEnrichedToken(container.SequenceToken, 0, enqueuedTime, traceParent);
        AssertEnrichedToken(delivered[0].Item2, 0, enqueuedTime, traceParent);
        AssertEnrichedToken(delivered[1].Item2, 1, enqueuedTime, traceParent);
    }

    private static EnrichedEventHubAdapter CreateAdapter() => CreateAdapter(out _);

    private static EnrichedEventHubAdapter CreateAdapter(out Serializer serializer)
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(EnrichedEventHubAdapter).Assembly));
        var provider = services.BuildServiceProvider();
        serializer = provider.GetRequiredService<Serializer>();

        return new EnrichedEventHubAdapter("provider-a", serializer);
    }

    private static void AssertEnrichedToken(
        StreamSequenceToken token,
        int expectedEventIndex,
        DateTimeOffset expectedEnqueuedTime,
        string expectedTraceParent)
    {
        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(token);
        Assert.Equal("123", enriched.EventHubOffset);
        Assert.Equal(42, enriched.SequenceNumber);
        Assert.Equal(expectedEventIndex, enriched.EventIndex);
        Assert.Equal(expectedEnqueuedTime, enriched.EnqueuedTime);
        Assert.Equal("provider-a", enriched.ProviderName);
        Assert.Equal(expectedTraceParent, enriched.TraceParent);
    }
}
