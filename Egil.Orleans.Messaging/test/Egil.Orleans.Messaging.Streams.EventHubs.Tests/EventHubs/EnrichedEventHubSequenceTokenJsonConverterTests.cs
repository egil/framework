using System.Text.Json;
using Egil.Orleans.Messaging.Tracking;

namespace Egil.Orleans.Messaging.Streams.EventHubs.Tests.EventHubs;

public sealed class EnrichedEventHubSequenceTokenJsonConverterTests
{
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
        tracker.ProcessMessage(new StreamCursor("orders", token), out tracker);

        var json = JsonSerializer.Serialize(tracker);
        var roundTripped = JsonSerializer.Deserialize<MessageTracker>(json);

        Assert.Contains(EnrichedEventHubSequenceToken.TypeAlias, json);
        Assert.Contains("\"Payload\"", json);
        Assert.NotNull(roundTripped);
        var latest = roundTripped.LatestStreamSequenceToken("event-hubs", "orders");
        AssertEnrichedToken(token, Assert.IsType<EnrichedEventHubSequenceToken>(latest));
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
}
