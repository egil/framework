using System.Text.Json;
using Egil.Orleans.Messaging.Tracking;
using Orleans.Streaming.EventHubs;

namespace Egil.Orleans.Messaging.Streams.EventHubs.Tests.EventHubs;

public sealed class EnrichedEventHubSequenceTokenJsonConverterTests
{
    public static TheoryData<string, string, string> InvalidEventHubPayloads => new()
    {
        {
            "orleans.event-hubs.sequence-token",
            """{"SequenceNumber":42,"EventIndex":2}""",
            "EventHubOffset"
        },
        {
            "orleans.event-hubs.sequence-token",
            """{"EventHubOffset":"12345","EventIndex":2}""",
            "SequenceNumber"
        },
        {
            "orleans.event-hubs.sequence-token",
            """{"EventHubOffset":"12345","SequenceNumber":42}""",
            "EventIndex"
        },
        {
            "orleans.event-hubs.sequence-token",
            """{"EventHubOffset":1,"SequenceNumber":42,"EventIndex":2}""",
            "must be a string"
        },
        {
            EnrichedEventHubSequenceToken.TypeAlias,
            """{"EventHubOffset":"12345","SequenceNumber":42,"EventIndex":2,"ProviderName":"event-hubs","TraceParent":null}""",
            "EnqueuedTime"
        },
        {
            EnrichedEventHubSequenceToken.TypeAlias,
            """{"EventHubOffset":"12345","SequenceNumber":42,"EventIndex":2,"EnqueuedTime":"2026-05-26T10:15:00+00:00","TraceParent":null}""",
            "ProviderName"
        },
        {
            EnrichedEventHubSequenceToken.TypeAlias,
            """{"EventHubOffset":"12345","SequenceNumber":42,"EventIndex":2,"EnqueuedTime":"2026-05-26T10:15:00+00:00","ProviderName":"event-hubs"}""",
            "TraceParent"
        },
        {
            "orleans.event-hubs.sequence-token",
            """{"EventHubOffset":"12345","EventHubOffset":"other","SequenceNumber":42,"EventIndex":2}""",
            "Duplicate"
        }
    };

    [Fact]
    public void StreamCursor_round_trips_enriched_event_hub_token()
    {
        StreamSequenceTokenJsonConverters.Register(
            EnrichedEventHubSequenceToken.TypeAlias,
            new EnrichedEventHubSequenceTokenJsonConverter());
        var token = CreateToken();
        var cursor = new StreamCursor("orders", token);

        var json = JsonSerializer.Serialize(cursor);
        var roundTripped = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.Contains(EnrichedEventHubSequenceToken.TypeAlias, json);
        Assert.Contains("\"Payload\"", json);
        Assert.NotNull(roundTripped);
        AssertEnrichedToken(token, Assert.IsType<EnrichedEventHubSequenceToken>(roundTripped.Token));
    }

    [Fact]
    public void MessageTracker_round_trips_enriched_event_hub_token()
    {
        StreamSequenceTokenJsonConverters.Register(
            EnrichedEventHubSequenceToken.TypeAlias,
            new EnrichedEventHubSequenceTokenJsonConverter());
        var token = CreateToken();
        var tracker = new MessageTracker();
        tracker.TryAcceptMessage(new StreamCursor("orders", token), out tracker);

        var json = JsonSerializer.Serialize(tracker);
        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.Contains(EnrichedEventHubSequenceToken.TypeAlias, json);
        Assert.Contains("\"Payload\"", json);
        Assert.NotNull(roundTripped);
        var latest = roundTripped.LatestStreamSequenceToken("event-hubs", "orders");
        AssertEnrichedToken(token, Assert.IsType<EnrichedEventHubSequenceToken>(latest));
    }

    [Theory]
    [InlineData("orleans.event-hubs.sequence-token", typeof(EventHubSequenceToken))]
    [InlineData("orleans.event-hubs.sequence-token-v2", typeof(EventHubSequenceTokenV2))]
    [InlineData(EnrichedEventHubSequenceToken.TypeAlias, typeof(EnrichedEventHubSequenceToken))]
    public void StreamCursor_deserializes_reordered_event_hub_payload_with_unknown_properties(
        string kind,
        Type expectedType)
    {
        RegisterConverters();
        var json = $$"""
            {
              "StreamNamespace": "orders",
              "Token": {
                "Payload": {
                  "FuturePayload": {
                    "Nested": [1, { "Enabled": true }]
                  },
                  "TraceParent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                  "ProviderName": "event-hubs",
                  "EnqueuedTime": "2026-05-26T10:15:00+00:00",
                  "EventIndex": 2,
                  "SequenceNumber": 42,
                  "EventHubOffset": "12345"
                },
                "Kind": "{{kind}}"
              }
            }
            """;

        var cursor = JsonSerializer.Deserialize<StreamCursor>(json);

        Assert.NotNull(cursor);
        Assert.IsType(expectedType, cursor.Token);
        Assert.Equal(42, cursor.Token.SequenceNumber);
        Assert.Equal(2, cursor.Token.EventIndex);
        switch (cursor.Token)
        {
            case EnrichedEventHubSequenceToken enriched:
                Assert.Equal("12345", enriched.EventHubOffset);
                Assert.Equal(
                    new DateTimeOffset(2026, 5, 26, 10, 15, 0, TimeSpan.Zero),
                    enriched.EnqueuedTime);
                Assert.Equal("event-hubs", enriched.ProviderName);
                Assert.Equal(
                    "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
                    enriched.TraceParent);
                break;
            case EventHubSequenceTokenV2 tokenV2:
                Assert.Equal("12345", tokenV2.EventHubOffset);
                break;
            case EventHubSequenceToken token:
                Assert.Equal("12345", token.EventHubOffset);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidEventHubPayloads))]
    public void StreamCursor_rejects_missing_invalid_or_duplicate_event_hub_properties(
        string kind,
        string payload,
        string expectedMessage)
    {
        RegisterConverters();
        var json = $$"""
            {
              "StreamNamespace": "orders",
              "Token": {
                "Kind": "{{kind}}",
                "Payload": {{payload}}
              }
            }
            """;

        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StreamCursor>(json));

        Assert.Contains(expectedMessage, exception.Message);
    }

    private static EnrichedEventHubSequenceToken CreateToken() =>
        new(
            "12345",
            42,
            2,
            new DateTimeOffset(2026, 5, 26, 10, 15, 0, TimeSpan.Zero),
            "event-hubs",
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01");

    private static void AssertEnrichedToken(
        EnrichedEventHubSequenceToken expected,
        EnrichedEventHubSequenceToken actual)
    {
        Assert.Equal(expected.EventHubOffset, actual.EventHubOffset);
        Assert.Equal(expected.SequenceNumber, actual.SequenceNumber);
        Assert.Equal(expected.EventIndex, actual.EventIndex);
        Assert.Equal(expected.EnqueuedTime, actual.EnqueuedTime);
        Assert.Equal(expected.ProviderName, actual.ProviderName);
        Assert.Equal(expected.TraceParent, actual.TraceParent);
    }

    private static void RegisterConverters()
    {
        StreamSequenceTokenJsonConverters.Register(
            "orleans.event-hubs.sequence-token",
            new EventHubSequenceTokenJsonConverter());
        StreamSequenceTokenJsonConverters.Register(
            "orleans.event-hubs.sequence-token-v2",
            new EventHubSequenceTokenV2JsonConverter());
        StreamSequenceTokenJsonConverters.Register(
            EnrichedEventHubSequenceToken.TypeAlias,
            new EnrichedEventHubSequenceTokenJsonConverter());
    }
}
