using System.Text.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Tracking;

public sealed class MessageTrackerJsonConverterTests
{
    public static TheoryData<string> MissingRootArraysJson => new()
    {
        """{"Streams":[]}""",
        """{"Outboxes":[]}""",
        """{}"""
    };

    public static TheoryData<StreamSequenceToken> SupportedStreamTokens => new()
    {
        new EventSequenceToken(7, 1),
        new EventSequenceTokenV2(8, 2)
    };

    [Theory]
    [MemberData(nameof(SupportedStreamTokens))]
    public void JsonSerializer_round_trips_message_tracker_stream_tokens(StreamSequenceToken token)
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", token.GetType().Name);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(now));
        tracker.ProcessMessage(new StreamCursor("orders", token), out tracker);

        var json = JsonSerializer.Serialize(tracker);
        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(tracker, roundTripped);
        Assert.Equal(new StreamCursor("orders", token), roundTripped.LatestStream(streamId));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    public void JsonSerializer_round_trips_message_tracker_outbox_tokens(long sequenceNumber)
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(now));
        tracker.ProcessMessage(new OutboxSequenceToken(sequenceNumber, sender, now, now), out tracker);

        var json = JsonSerializer.Serialize(tracker);
        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(tracker, roundTripped);
        Assert.Equal(
            new OutboxSequenceToken(sequenceNumber, sender, now, now),
            roundTripped.LatestOutbox(sender));
    }

    [Fact]
    public void JsonSerializer_throws_for_unsupported_stream_token_type()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", "unsupported");
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(now));
        tracker.ProcessMessage(new StreamCursor("orders", new UnsupportedSequenceToken(1, 0)), out tracker);

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(tracker));
    }

    [Fact]
    public void JsonSerializer_deserializes_null_stream_and_outbox_arrays_as_empty_tracker()
    {
        var json = """
            {
              "Streams": null,
              "Outboxes": null
            }
            """;

        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.NotNull(roundTripped);
        Assert.Null(roundTripped.LatestStream(StreamId.Create("orders", "one")));
        Assert.Null(roundTripped.LatestOutbox(GrainId.Create("test/sender", "one")));
    }

    [Fact]
    public void JsonSerializer_round_trips_full_tracker_across_property_naming_policies()
    {
        var tracker = CreateFullTracker();
        var camelCaseOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var camelCaseJson = JsonSerializer.Serialize(tracker, camelCaseOptions);
        var fromCamelCase = JsonSerializer.Deserialize<MessageTracker>(camelCaseJson);
        var defaultJson = JsonSerializer.Serialize(tracker);
        var fromDefault = JsonSerializer.Deserialize<MessageTracker>(defaultJson, camelCaseOptions);

        Assert.Contains("\"Streams\"", camelCaseJson);
        Assert.Contains("\"Outboxes\"", camelCaseJson);
        Assert.Contains("\"LastPosition\"", camelCaseJson);
        Assert.Contains("\"Received\"", camelCaseJson);
        Assert.Contains("\"Sender\"", camelCaseJson);
        Assert.Contains("\"Epoch\"", camelCaseJson);
        Assert.Contains("\"LastSequenceNumber\"", camelCaseJson);
        Assert.Contains("\"LastTimestamp\"", camelCaseJson);
        Assert.Contains("\"Type\"", camelCaseJson);
        Assert.Contains("\"Key\"", camelCaseJson);
        Assert.DoesNotContain("\"streams\"", camelCaseJson);
        Assert.Equal(tracker, fromCamelCase);
        Assert.Equal(tracker, fromDefault);
    }

    [Theory]
    [MemberData(nameof(MissingRootArraysJson))]
    public void JsonSerializer_throws_when_required_root_array_is_missing(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageTracker>(json));
    }

    [Fact]
    public void JsonSerializer_deserializes_tracked_stream_without_sequence_token()
    {
        var streamId = StreamId.Create("orders", "no-token");
        var json = """
            {
              "Streams": [
                {
                  "StreamNamespace": "orders",
                  "LastPosition": {
                    "StreamNamespace": "orders",
                    "Token": null
                  },
                  "Received": "2026-05-23T12:30:00+00:00"
                }
              ],
              "Outboxes": []
            }
            """;

        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(new StreamCursor("orders", null), roundTripped.LatestStream(streamId));
    }

    [Fact]
    public void JsonSerializer_throws_for_unknown_stream_token_kind()
    {
        var json = """
            {
              "Streams": [
                {
                  "StreamNamespace": "orders",
                  "LastPosition": {
                    "StreamNamespace": "orders",
                    "Token": {
                      "Kind": "unknown-kind",
                      "Payload": {
                        "SequenceNumber": 1,
                        "EventIndex": 0
                      }
                    }
                  },
                  "Received": "2026-05-23T12:30:00+00:00"
                }
              ],
              "Outboxes": []
            }
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageTracker>(json));
    }

    private static MessageTracker CreateFullTracker()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(now));
        tracker.ProcessMessage(
            new StreamCursor("orders", new EventSequenceToken(7, 1), "provider-a"),
            out tracker);
        tracker.ProcessMessage(
            new OutboxSequenceToken(
                42,
                GrainId.Create("test/sender", "one"),
                now,
                now),
            out tracker);
        return tracker;
    }

    private sealed class UnsupportedSequenceToken(long sequenceNumber, int eventIndex) : StreamSequenceToken
    {
        public override long SequenceNumber { get; protected set; } = sequenceNumber;
        public override int EventIndex { get; protected set; } = eventIndex;

        public override bool Equals(StreamSequenceToken? other) => other is UnsupportedSequenceToken token
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
