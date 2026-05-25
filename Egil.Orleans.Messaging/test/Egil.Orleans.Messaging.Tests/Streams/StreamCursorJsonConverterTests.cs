using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
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
        var cursor = new StreamCursor("orders", new EventSequenceToken(7, 1));

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_without_token()
    {
        var cursor = new StreamCursor("orders", null);

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(cursor, roundTripped);
        Assert.Null(roundTripped.Token);
    }

    [Fact]
    public void JsonSerializer_round_trips_stream_cursor_with_event_sequence_token_v2()
    {
        var cursor = new StreamCursor("orders", new EventSequenceTokenV2(8, 2));

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
            "orders",
            new UnsupportedCustomSequenceToken(21, 3));

        var exception = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(cursor));
        Assert.Contains("https://github.com/egil/framework/issues", exception.Message);
        Assert.Contains("supports only built-in Orleans token types", exception.Message);
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