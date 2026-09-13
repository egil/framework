using System.Text.Json;
using System.Text.Json.Serialization;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamCursorJsonConverterTests
{
    [Theory]
    [InlineData("event-sequence", "1.5")]
    [InlineData("event-sequence", "2147483648")]
    [InlineData("event-sequence", "\"1\"")]
    [InlineData("event-sequence-v2", "1.5")]
    [InlineData("event-sequence-v2", "2147483648")]
    [InlineData("event-sequence-v2", "\"1\"")]
    public void Invalid_event_index_cannot_change_stream_position(string kind, string index)
    {
        var json = $$"""{"StreamNamespace":"orders","Token":{"Kind":"{{kind}}","Payload":{"SequenceNumber":7,"EventIndex":{{index}} } } }""";

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));

        Assert.Contains("EventIndex", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_null_provider_preserves_stream_position()
    {
        var json = """{"StreamNamespace":"orders","ProviderName":null,"Token":{"Kind":"event-sequence","Payload":{"SequenceNumber":7,"EventIndex":2}}}""";

        var cursor = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.Equal(new StreamCursor("orders", new EventSequenceToken(7, 2)), cursor);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("{}")]
    public void Invalid_provider_metadata_is_rejected(string provider)
    {
        var json = $$"""{"StreamNamespace":"orders","ProviderName":{{provider}},"Token":null}""";

        var error = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));

        Assert.Contains("ProviderName", error.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> InvalidCursorJson => new()
    {
        { """{"Token":null}""", "StreamNamespace" },
        { """{"StreamNamespace":"orders"}""", "Token" },
        {
            """{"StreamNamespace":"orders","Token":{"Payload":{"SequenceNumber":1,"EventIndex":0}}}""",
            "Kind"
        },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":"event-sequence"}}""",
            "Payload"
        },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":"event-sequence","Payload":{"EventIndex":0}}}""",
            "SequenceNumber"
        },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":"event-sequence","Payload":{"SequenceNumber":1}}}""",
            "EventIndex"
        },
        { """{"StreamNamespace":1,"Token":null}""", "must be a string" },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":1,"Payload":{"SequenceNumber":1,"EventIndex":0}}}""",
            "must be a string"
        },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":"event-sequence","Payload":{"SequenceNumber":"1","EventIndex":0}}}""",
            "must be a 64-bit integer"
        },
        {
            """{"StreamNamespace":"orders","StreamNamespace":"other","Token":null}""",
            "Duplicate"
        },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":"event-sequence","Kind":"event-sequence","Payload":{"SequenceNumber":1,"EventIndex":0}}}""",
            "Duplicate"
        },
        {
            """{"StreamNamespace":"orders","Token":{"Kind":"event-sequence","Payload":{"SequenceNumber":1,"SequenceNumber":2,"EventIndex":0}}}""",
            "Duplicate"
        }
    };

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
    public void JsonSerializer_deserializes_reordered_cursor_and_token_with_unknown_properties()
    {
        var json = """
            {
              "FutureCursor": {
                "Nested": [1, { "Enabled": true }]
              },
              "ProviderName": "provider-a",
              "Token": {
                "Payload": {
                  "FuturePayload": {
                    "Nested": ["ignored"]
                  },
                  "EventIndex": 1,
                  "SequenceNumber": 7
                },
                "FutureEnvelope": [1, 2, 3],
                "Kind": "event-sequence"
              },
              "StreamNamespace": "orders"
            }
            """;

        var cursor = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(cursor);
        Assert.Equal("orders", cursor.StreamNamespace);
        Assert.Equal("provider-a", cursor.ProviderName);
        Assert.Equal(new EventSequenceToken(7, 1), cursor.Token);
    }

    [Theory]
    [MemberData(nameof(InvalidCursorJson))]
    public void JsonSerializer_rejects_missing_invalid_or_duplicate_known_properties(
        string json,
        string expectedMessage)
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_for_stream_cursor_payload_with_unknown_token_kind()
    {
        var json = """
            {
              "StreamNamespace": "orders",
              "Token": {
                "Kind": "unknown-kind",
                "Payload": {
                  "SequenceNumber": 1,
                  "EventIndex": 0
                }
              }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));
        Assert.Contains("Register a JsonConverter", exception.Message);
    }

    [Fact]
    public void JsonSerializer_throws_for_custom_token_type()
    {
        var cursor = new StreamCursor(
            "orders",
            new UnsupportedCustomSequenceToken(21, 3));

        var exception = Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(cursor));
        Assert.Contains("Register a JsonConverter", exception.Message);
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
