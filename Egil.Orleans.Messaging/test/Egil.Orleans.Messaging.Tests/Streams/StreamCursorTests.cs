using Orleans.Providers.Streams.Common;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamCursorTests
{
    [Fact]
    public void TryGetEnqueuedTime_returns_true_and_value_for_enriched_event_hub_token()
    {
        var enqueuedTime = new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero);
        var cursor = new StreamCursor(
            StreamId.Create("orders", "one"),
            new EnrichedEventHubSequenceToken("42", 10, 0, enqueuedTime, "provider-a"));

        var found = cursor.TryGetEnqueuedTime(out var actual);

        Assert.True(found);
        Assert.Equal(enqueuedTime, actual);
    }

    [Fact]
    public void TryGetEnqueuedTime_returns_false_for_non_enriched_token()
    {
        var cursor = new StreamCursor(
            StreamId.Create("orders", "one"),
            new EventSequenceToken(10));

        var found = cursor.TryGetEnqueuedTime(out var actual);

        Assert.False(found);
        Assert.Equal(default, actual);
    }

    [Fact]
    public void TryGetProviderName_returns_true_and_value_for_enriched_event_hub_token()
    {
        var cursor = new StreamCursor(
            StreamId.Create("orders", "one"),
            new EnrichedEventHubSequenceToken(
                "42",
                10,
                0,
                new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero),
                "provider-a"));

        var found = cursor.TryGetProviderName(out var providerName);

        Assert.True(found);
        Assert.Equal("provider-a", providerName);
    }

    [Fact]
    public void TryGetProviderName_returns_false_for_non_enriched_token()
    {
        var cursor = new StreamCursor(
            StreamId.Create("orders", "one"),
            new EventSequenceToken(10));

        var found = cursor.TryGetProviderName(out var providerName);

        Assert.False(found);
        Assert.Null(providerName);
    }

    [Fact]
    public void TryGetTraceParent_returns_true_and_value_when_present_on_enriched_event_hub_token()
    {
        var cursor = new StreamCursor(
            StreamId.Create("orders", "one"),
            new EnrichedEventHubSequenceToken(
                "42",
                10,
                0,
                new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero),
                "provider-a",
                "00-abc-xyz-01"));

        var found = cursor.TryGetTraceParent(out var traceParent);

        Assert.True(found);
        Assert.Equal("00-abc-xyz-01", traceParent);
    }

    [Fact]
    public void TryGetTraceParent_returns_false_when_missing_on_enriched_event_hub_token()
    {
        var cursor = new StreamCursor(
            StreamId.Create("orders", "one"),
            new EnrichedEventHubSequenceToken(
                "42",
                10,
                0,
                new DateTimeOffset(2026, 5, 24, 12, 30, 0, TimeSpan.Zero),
                "provider-a"));

        var found = cursor.TryGetTraceParent(out var traceParent);

        Assert.False(found);
        Assert.Null(traceParent);
    }
}
