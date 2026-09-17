using System.Diagnostics;
using System.Text;
using Azure.Messaging.EventHubs;
using Orleans.Providers.Streams.Common;
using Orleans.Serialization;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams.EventHubs;

public sealed class EnrichedEventHubAdapterTests
{
    private const string TraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private static readonly DateTimeOffset EnqueuedTime = new(2026, 5, 24, 19, 0, 0, TimeSpan.Zero);

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
        var message = CreateReceivedMessage(BinaryData.FromString("payload"), []);

        var position = adapter.GetStreamPosition("0", message);

        var enriched = Assert.IsType<EnrichedEventHubSequenceToken>(position.SequenceToken);
        Assert.Equal(TraceParent, enriched.TraceParent);
        Assert.Equal("provider-a", enriched.ProviderName);
    }

    [Fact]
    public void GetBatchContainer_delivers_enriched_tokens_from_cached_message()
    {
        var adapter = CreateAdapter(out var serializer);
        var outgoing = adapter.ToQueueMessage(
            StreamId.Create("orders", "one"),
            ["event-1", "event-2"],
            null!,
            []);
        var cachedMessage = CreateCachedMessage(adapter, outgoing.EventBody, []);

        var container = RoundTrip(serializer, adapter.GetBatchContainer(ref cachedMessage));
        var delivered = container.GetEvents<string>().ToArray();

        Assert.Equal(["event-1", "event-2"], delivered.Select(item => item.Item1));
        AssertEnrichedToken(container.SequenceToken, 0, EnqueuedTime, TraceParent);
        AssertEnrichedToken(delivered[0].Item2, 0, EnqueuedTime, TraceParent);
        AssertEnrichedToken(delivered[1].Item2, 1, EnqueuedTime, TraceParent);
    }

    [Fact]
    public void GetBatchContainer_enriches_tokens_from_custom_inner_container()
    {
        var adapter = CreateCustomContainerAdapter(out var serializer);
        var cachedMessage = CreateCachedMessage(
            adapter,
            BinaryData.FromString("event-1\nevent-2"),
            []);

        var container = RoundTrip(serializer, adapter.GetBatchContainer(ref cachedMessage));
        var delivered = container.GetEvents<string>().ToArray();

        Assert.Equal(["event-1", "event-2"], delivered.Select(item => item.Item1));
        AssertEnrichedToken(container.SequenceToken, 0, EnqueuedTime, TraceParent);
        AssertEnrichedToken(delivered[0].Item2, 0, EnqueuedTime, TraceParent);
        AssertEnrichedToken(delivered[1].Item2, 1, EnqueuedTime, TraceParent);
    }

    [Fact]
    public void GetBatchContainer_delegates_stream_id_and_request_context_to_custom_inner_container()
    {
        var adapter = CreateCustomContainerAdapter(out var serializer);
        var cachedMessage = CreateCachedMessage(
            adapter,
            BinaryData.FromString("event-1"),
            new Dictionary<string, object>
            {
                [DecodedBatchContainer.RequestContextPropertyKey] = "present"
            });

        var container = RoundTrip(serializer, adapter.GetBatchContainer(ref cachedMessage));

        Assert.Equal(StreamId.Create("orders", "one"), container.StreamId);
        Assert.True(container.ImportRequestContext());
    }

    private static EnrichedEventHubAdapter CreateAdapter() => CreateAdapter(out _);

    private static EnrichedEventHubAdapter CreateAdapter(out Serializer serializer)
    {
        serializer = CreateSerializer();

        return new EnrichedEventHubAdapter("provider-a", serializer);
    }

    private static CustomContainerAdapter CreateCustomContainerAdapter(out Serializer serializer)
    {
        serializer = CreateSerializer();

        return new CustomContainerAdapter("provider-a", serializer);
    }

    private static Serializer CreateSerializer()
    {
        var services = new ServiceCollection();
        services.AddSerializer(builder =>
        {
            builder.AddAssembly(typeof(EnrichedEventHubAdapter).Assembly);

            // DecodedBatchContainer travels to consumers as the wrapped inner container,
            // so the test assembly has to be part of the serializer configuration too.
            builder.AddAssembly(typeof(EnrichedEventHubAdapterTests).Assembly);
        });

        return services.BuildServiceProvider().GetRequiredService<Serializer>();
    }

    private static EventData CreateReceivedMessage(
        BinaryData body,
        Dictionary<string, object> extraProperties)
    {
        var properties = new Dictionary<string, object>(extraProperties)
        {
            ["traceparent"] = TraceParent
        };
#pragma warning disable CS0618 // Test-only factory path for broker-owned EventData fields in the installed Event Hubs package.
        var received = EventHubsModelFactory.EventData(
            body,
            properties,
            systemProperties: new Dictionary<string, object>(),
            partitionKey: "one",
            sequenceNumber: 42,
            offset: 123,
            enqueuedTime: EnqueuedTime);
#pragma warning restore CS0618
        received.SetStreamNamespaceProperty("orders");

        return received;
    }

    private static CachedMessage CreateCachedMessage(
        EnrichedEventHubAdapter adapter,
        BinaryData body,
        Dictionary<string, object> extraProperties)
    {
        var received = CreateReceivedMessage(body, extraProperties);
        var position = adapter.GetStreamPosition("0", received);

        return adapter.FromQueueMessage(
            position,
            received,
            new DateTime(2026, 5, 24, 19, 0, 1, DateTimeKind.Utc),
            static size => new ArraySegment<byte>(new byte[size]));
    }

    private static IBatchContainer RoundTrip(Serializer serializer, IBatchContainer container)
    {
        var serialized = serializer.SerializeToArray<IBatchContainer>(container);
        var roundTripped = serializer.Deserialize<IBatchContainer>(serialized);

        Assert.NotNull(roundTripped);

        return roundTripped;
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

    /// <summary>
    /// The shape an application adapter takes when the Event Hub carries a payload
    /// format the application owns: it supplies the container and leaves token
    /// enrichment to <see cref="EnrichedEventHubAdapter"/>.
    /// </summary>
    private sealed class CustomContainerAdapter(string providerName, Serializer serializer)
        : EnrichedEventHubAdapter(providerName, serializer)
    {
        protected override IBatchContainer CreateInnerBatchContainer(EventHubMessage eventHubMessage)
            => new DecodedBatchContainer(eventHubMessage);
    }

    [GenerateSerializer]
    [Alias("egil.orleans.messaging.tests.DecodedBatchContainer")]
    internal sealed class DecodedBatchContainer : IBatchContainer
    {
        public const string RequestContextPropertyKey = "decoded-request-context";

        [Id(0)]
        private StreamId streamId;

        [Id(1)]
        private string[] payloads = [];

        [Id(2)]
        private bool hasRequestContext;

        /// <summary>
        /// Creates an instance for Orleans serialization.
        /// </summary>
        public DecodedBatchContainer()
        {
        }

        /// <summary>
        /// Decodes the producer's own wire format rather than an Orleans-serialized
        /// batch. The format here is newline-separated text: <c>"event-1\nevent-2"</c>.
        /// </summary>
        public DecodedBatchContainer(EventHubMessage eventHubMessage)
        {
            streamId = eventHubMessage.StreamId;
            payloads = Encoding.UTF8.GetString(eventHubMessage.Payload).Split('\n');
            hasRequestContext = eventHubMessage.Properties.ContainsKey(RequestContextPropertyKey);
        }

        public StreamId StreamId => streamId;

        // No token of its own: the adapter replaces batch and per-event tokens.
        public StreamSequenceToken SequenceToken => null!;

        public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
            => payloads.Select(payload => Tuple.Create((T)(object)payload, (StreamSequenceToken)null!));

        public bool ImportRequestContext() => hasRequestContext;
    }
}
