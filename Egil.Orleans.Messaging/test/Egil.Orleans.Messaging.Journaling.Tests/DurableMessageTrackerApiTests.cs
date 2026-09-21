#pragma warning disable ORLEANSEXP005 // This suite explicitly exercises the pinned experimental journaling package.
using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class DurableMessageTrackerApiTests(JournalingPrototypeFixture fixture)
    : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task Receive_overloads_expose_provider_aware_positions_without_replacing_the_component()
    {
        var clock = new ManualTimeProvider(ReceivedAt);
        await using var session = await fixture.NewSessionAsync();
        var tracker = session.Tracker;
        tracker.RegisterTimeProvider(clock);
        var original = tracker.AsImmutable();
        var legacyToken = new EventSequenceToken(1);
        var providerToken = new EventSequenceToken(2);
        var cursor = new StreamCursor("orders", new EventSequenceToken(4), "provider-b");
        var outboxToken = Token("sender-a");

        Assert.True(tracker.TryAcceptMessage("legacy", legacyToken, out var afterNamespace));
        Assert.True(tracker.TryAcceptMessage("provider-a", "orders", providerToken, out var afterProvider));
        Assert.True(tracker.TryAcceptMessage(cursor, out var afterCursor));
        Assert.True(tracker.TryAcceptMessage(outboxToken, out var afterOutbox));

        Assert.Same(tracker, afterNamespace);
        Assert.Same(tracker, afterProvider);
        Assert.Same(tracker, afterCursor);
        Assert.Same(tracker, afterOutbox);
        Assert.Null(original.LatestStream("legacy"));
        Assert.NotSame(original, tracker.AsImmutable());
        Assert.Equal(legacyToken, tracker.LatestStreamSequenceToken("legacy"));
        Assert.Equal(tracker.LatestStream("legacy"), tracker.LatestStream(StreamId.Create("legacy", "unused-key")));
        Assert.Equal(legacyToken, tracker.LatestStreamSequenceToken("any-provider", "legacy"));
        Assert.Equal(providerToken, tracker.LatestStream("provider-a", "orders")?.Token);
        Assert.Equal(cursor, tracker.LatestStream("provider-b", "orders"));
        Assert.Null(tracker.LatestStream("orders"));
        Assert.Equal(outboxToken, tracker.LatestOutbox(outboxToken.Sender));
        var saved = tracker.AsImmutable();
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.False(tracker.TryAcceptMessage(outboxToken, out var afterDuplicate));
        Assert.Same(tracker, afterDuplicate);
        Assert.False(tracker.TryAcceptMessage("provider-a", "orders", providerToken));
        Assert.False(tracker.TryAcceptMessage("legacy", legacyToken));
        Assert.True(tracker.TryAcceptMessage("untracked", null, out var afterTokenless));
        Assert.Same(tracker, afterTokenless);
        Assert.Same(saved, tracker.AsImmutable());
        Assert.Same(tracker, tracker.Evict(DateTimeOffset.MinValue));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Single(fixture.Storage.For(session.Id).Writes);
        await session.DisposeAsync();
        clock.Advance(TimeSpan.FromDays(1));
        await using var recovered = await fixture.NewSessionAsync(session.Id, clock);
        Assert.Equal(saved, recovered.Tracker.AsImmutable());
        Assert.Equal(cursor, recovered.Tracker.LatestStream("provider-b", "orders"));
        Assert.Null(recovered.Tracker.LatestStream("untracked"));
        recovered.Tracker.Evict(ReceivedAt);
        Assert.Null(recovered.Tracker.LatestStream("legacy"));
        Assert.Null(recovered.Tracker.LatestOutbox(outboxToken.Sender));
    }

    [Theory]
    [InlineData(EvictionScope.All, true, true, true, true)]
    [InlineData(EvictionScope.Streams, true, true, false, false)]
    [InlineData(EvictionScope.Outboxes, false, false, true, true)]
    [InlineData(EvictionScope.Namespace, true, false, false, false)]
    [InlineData(EvictionScope.StreamId, true, false, false, false)]
    [InlineData(EvictionScope.Sender, false, false, true, false)]
    public async Task Scoped_eviction_replays_only_the_requested_entries_at_the_original_cutoff(
        EvictionScope scope, bool removesOrders, bool removesPayments, bool removesSender, bool removesOtherSender)
    {
        var clock = new ManualTimeProvider(ReceivedAt);
        await using var session = await fixture.NewSessionAsync(time: clock);
        var tracker = session.Tracker;
        Assert.True(tracker.TryAcceptMessage("provider-a", "orders", new EventSequenceToken(5)));
        Assert.True(tracker.TryAcceptMessage("provider-b", "orders", new EventSequenceToken(8)));
        Assert.True(tracker.TryAcceptMessage("payments", new EventSequenceToken(2)));
        Assert.True(tracker.TryAcceptMessage(Token("sender-a")));
        Assert.True(tracker.TryAcceptMessage(Token("sender-b")));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(tracker.TryAcceptMessage("recent", new EventSequenceToken(1)));
        Assert.True(tracker.TryAcceptMessage(Token("recent")));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var before = tracker.AsImmutable();

        Assert.Same(tracker, Evict(tracker, scope, ReceivedAt));
        Assert.NotSame(before, tracker.AsImmutable());
        var expected = tracker.AsImmutable();
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("provider-a", fixture.Storage.For(session.Id).Writes[^1], StringComparison.Ordinal);
        await session.DisposeAsync();
        clock.Advance(TimeSpan.FromDays(1));
        await using var recovered = await fixture.NewSessionAsync(session.Id, clock);

        Assert.Equal(expected, recovered.Tracker.AsImmutable());
        Assert.Equal(removesOrders, recovered.Tracker.LatestStream("provider-a", "orders") is null);
        Assert.Equal(removesOrders, recovered.Tracker.LatestStream("provider-b", "orders") is null);
        Assert.Equal(removesPayments, recovered.Tracker.LatestStream("payments") is null);
        Assert.Equal(removesSender, recovered.Tracker.LatestOutbox(Token("sender-a").Sender) is null);
        Assert.Equal(removesOtherSender, recovered.Tracker.LatestOutbox(Token("sender-b").Sender) is null);
        Assert.NotNull(recovered.Tracker.LatestStream("recent"));
        Assert.Equal(Token("recent"), recovered.Tracker.LatestOutbox(Token("recent").Sender));
    }

    private static IDurableMessageTracker Evict(IDurableMessageTracker tracker, EvictionScope scope, DateTimeOffset cutoff) =>
        scope switch
        {
            EvictionScope.All => tracker.Evict(cutoff),
            EvictionScope.Streams => tracker.EvictStreams(cutoff),
            EvictionScope.Outboxes => tracker.EvictOutboxes(cutoff),
            EvictionScope.Namespace => tracker.Evict("orders", cutoff),
            EvictionScope.StreamId => tracker.Evict(StreamId.Create("orders", "unused-key"), cutoff),
            EvictionScope.Sender => tracker.Evict(Token("sender-a").Sender, cutoff),
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };

    private static DateTimeOffset ReceivedAt =>
        DateTimeOffset.Parse("2026-09-01T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static OutboxSequenceToken Token(string sender) => new(
        1, GrainId.Create("prototype-sender", sender), ReceivedAt, ReceivedAt.AddHours(-1));

    public enum EvictionScope
    {
        All,
        Streams,
        Outboxes,
        Namespace,
        StreamId,
        Sender
    }
}
