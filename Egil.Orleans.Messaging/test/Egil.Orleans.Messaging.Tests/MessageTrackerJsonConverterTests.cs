using System.Text.Json;
using Orleans.Providers.Streams.Common;
using Orleans.Streaming.EventHubs;
using Orleans.Streams;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests;

public sealed class MessageTrackerJsonConverterTests
{
    public static TheoryData<StreamSequenceToken> SupportedStreamTokens => new()
    {
        new EventSequenceToken(7, 1),
        new EventSequenceTokenV2(8, 2),
        new EventHubSequenceToken("42", 10, 3),
        new EventHubSequenceTokenV2("43", 11, 4),
        new EnrichedEventHubSequenceToken(
            offset: "44",
            sequenceNumber: 12,
            eventIndex: 5,
            enqueuedTime: new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero),
            streamProviderName: "provider-a",
            traceParent: "00-abc-xyz-01")
    };

    [Theory]
    [MemberData(nameof(SupportedStreamTokens))]
    public void JsonSerializer_round_trips_message_tracker_stream_tokens(StreamSequenceToken token)
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", token.GetType().Name);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(now));
        tracker.ProcessMessage(new StreamCursor(streamId, token), out tracker);

        var json = JsonSerializer.Serialize(tracker);
        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(tracker, roundTripped);
        Assert.Equal(new StreamCursor(streamId, token), roundTripped.LatestStream(streamId));
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
        tracker.ProcessMessage(new StreamCursor(streamId, new UnsupportedSequenceToken(1, 0)), out tracker);

        Assert.Throws<NotSupportedException>(() => JsonSerializer.Serialize(tracker));
    }

    [Fact]
    public void JsonSerializer_throws_for_unknown_stream_token_kind()
    {
        var json = """
            {
              "Streams": [
                {
                  "StreamId": "orders/one",
                  "LastPosition": {
                    "StreamId": "orders/one",
                    "Token": {
                      "Kind": "unknown-kind",
                      "SequenceNumber": 1,
                      "EventIndex": 0
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
