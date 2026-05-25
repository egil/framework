using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamCursorJsonConverterTests
{
    [Fact]
    public void StreamCursor_is_decorated_with_stream_cursor_json_converter()
    {
        var attribute = typeof(StreamCursor).GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
            .Cast<JsonConverterAttribute>()
            .Single();

        Assert.Equal(typeof(StreamCursorJsonConverter), attribute.ConverterType);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_with_event_sequence_token()
    {
        var streamId = StreamId.Create("orders", "one");
        var cursor = new StreamCursor(streamId, new EventSequenceToken(7, 1));

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_without_token()
    {
        var streamId = StreamId.Create("orders", "no-token");
        var cursor = new StreamCursor(streamId, null);

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
        Assert.Null(roundTripped.Token);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_with_event_sequence_token_v2()
    {
        var streamId = StreamId.Create("orders", "event-v2");
        var cursor = new StreamCursor(streamId, new EventSequenceTokenV2(8, 2));

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_with_event_hub_token()
    {
        var streamId = StreamId.Create("orders", "eh");
        var cursor = new StreamCursor(streamId, new EventHubSequenceToken("41", 9, 2));

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_with_event_hub_token_v2()
    {
        var streamId = StreamId.Create("orders", "eh-v2");
        var cursor = new StreamCursor(streamId, new EventHubSequenceTokenV2("42", 10, 3));

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_with_enriched_event_hub_token()
    {
        var streamId = StreamId.Create("orders", "eh-enriched");
        var cursor = new StreamCursor(
            streamId,
            new EnrichedEventHubSequenceToken(
                "43",
                11,
                4,
                new DateTimeOffset(2026, 5, 24, 18, 30, 0, TimeSpan.Zero),
                "provider-a",
                "00-abc-xyz-01"));

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
    }

    [Fact]
    public void JsonSerializer_throws_for_stream_cursor_payload_with_unknown_token_kind()
    {
        var json = """
            {
              "StreamNamespace": "orders",
              "Token": {
                "Kind": "unknown-kind",
                "SequenceNumber": 1,
                "EventIndex": 0
              }
            }
            """;

        var exception = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<StreamCursor>(json));
        Assert.Contains("https://github.com/egil/framework/issues", exception.Message);
        Assert.Contains("supports only built-in Orleans token types", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_for_custom_token_type()
    {
        var cursor = new StreamCursor(
            StreamId.Create("orders", "custom"),
            new UnsupportedCustomSequenceToken(21, 3));

        var exception = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(cursor));
        Assert.Contains("https://github.com/egil/framework/issues", exception.Message);
        Assert.Contains("supports only built-in Orleans token types", exception.Message);
    }

    [Theory]
    [InlineData("event-hub")]
    [InlineData("event-hub-v2")]
    [InlineData("enriched-event-hub")]
    public void JsonSerializer_throws_for_event_hub_payload_without_offset(string kind)
    {
        var json = $$"""
            {
              "StreamNamespace": "orders",
              "Token": {
                "Kind": "{{kind}}",
                "SequenceNumber": 1,
                "EventIndex": 0,
                "EnqueuedTime": "2026-05-23T12:30:00Z",
                "ProviderName": "provider-a"
              }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));
        Assert.Equal("Missing EventHubOffset.", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_for_enriched_event_hub_payload_without_enqueued_time()
    {
        var json = """
            {
              "StreamNamespace": "orders",
              "Token": {
                "Kind": "enriched-event-hub",
                "SequenceNumber": 1,
                "EventIndex": 0,
                "EventHubOffset": "42",
                "ProviderName": "provider-a"
              }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));
        Assert.Equal("Missing EnqueuedTime.", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_for_enriched_event_hub_payload_without_stream_provider_name()
    {
        var json = """
            {
              "StreamNamespace": "orders",
              "Token": {
                "Kind": "enriched-event-hub",
                "SequenceNumber": 1,
                "EventIndex": 0,
                "EventHubOffset": "42",
                "EnqueuedTime": "2026-05-23T12:30:00Z"
              }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));
        Assert.Equal("Missing ProviderName.", exception.Message);
    }

    private sealed class UnsupportedCustomSequenceToken(long sequenceNumber, int eventIndex) : StreamSequenceToken
    {
        public override long SequenceNumber { get; protected set; } = sequenceNumber;
        public override int EventIndex { get; protected set; } = eventIndex;

        public override bool Equals(StreamSequenceToken? other) => other is UnsupportedCustomSequenceToken token
            && token.SequenceNumber == SequenceNumber
            && token.EventIndex == EventIndex;

        public override int CompareTo(StreamSequenceToken? other)
        {
            if (other is null)
            {
                return 1;
            }

            var sequenceComparison = SequenceNumber.CompareTo(other.SequenceNumber);
            if (sequenceComparison != 0)
            {
                return sequenceComparison;
            }

            return EventIndex.CompareTo(other.EventIndex);
        }
    }
}

