#pragma warning disable ORLEANSEXP005 // These tests exercise the pinned experimental journaling package.
using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class TrackerRetentionReplayTests(JournalingPrototypeFixture fixture) : IClassFixture<JournalingPrototypeFixture>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Recovery_preserves_acceptance_cleanup_without_reapplying_current_policy(bool compact, bool receiveRpc)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        await using var session = await fixture.NewSessionAsync(time: clock);
        session.Tracker.Configure(options =>
        {
            options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
            options.RetentionPeriod = TimeSpan.FromHours(1);
        });
        var old = Cursor(1);
        Assert.True(session.Tracker.TryAcceptMessage(old));
        Assert.True(session.Tracker.TryAcceptMessage(old.OutboxToken!));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(30));
        var recent = Cursor(2);
        Assert.True(session.Tracker.TryAcceptMessage(recent));
        clock.Advance(TimeSpan.FromMinutes(30));
        session.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.StreamPosition);
        var trigger = Cursor(3) with { StreamId = StreamId.Create("orders", "other") };

        Assert.True(receiveRpc
            ? session.Tracker.TryAcceptMessage(trigger.OutboxToken! with { Sender = GrainId.Create("sender", "other") })
            : session.Tracker.TryAcceptMessage(trigger));

        Assert.Null(session.Tracker.LatestOutbox(old.OutboxToken!.Sender));
        var expected = session.Tracker.AsImmutable();
        fixture.Storage.For(session.Id).CompactNext = compact;
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();
        clock.Advance(TimeSpan.FromDays(30));
        await using var recovered = await fixture.NewSessionAsync(session.Id, clock);
        Assert.Equal(expected, recovered.Tracker.AsImmutable());
        recovered.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        Assert.False(recovered.Tracker.TryAcceptMessage(recent with { Token = new EventSequenceToken(99) }));
        Assert.True(recovered.Tracker.TryAcceptMessage(old));
    }

    [Fact]
    public async Task Position_tracking_does_not_create_receipts_during_replay()
    {
        await using var session = await fixture.NewSessionAsync();
        var delivery = Cursor(1);
        Assert.True(session.Tracker.TryAcceptMessage(delivery));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        await using var recovered = await fixture.NewSessionAsync(session.Id);
        recovered.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);

        Assert.True(recovered.Tracker.TryAcceptMessage(delivery with { Token = new EventSequenceToken(2) }));
    }

    private static StreamCursor Cursor(long sequence) => new("orders", new EventSequenceToken(sequence), "events")
    {
        StreamId = StreamId.Create("orders", "one"),
        OutboxToken = new(sequence, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
    };

    [Fact]
    public async Task Tokenless_position_delivery_persists_cleanup_without_adding_a_receipt()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        await using var session = await fixture.NewSessionAsync();
        session.Tracker.Configure(options =>
        {
            options.RetentionPeriod = TimeSpan.FromHours(1);
            options.TimeProvider = clock;
        });
        var old = Cursor(1).OutboxToken!;
        Assert.True(session.Tracker.TryAcceptMessage(old));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(1));

        Assert.True(session.Tracker.TryAcceptMessage(Cursor(2) with { Token = null }));

        Assert.Equal(new MessageTracker(), session.Tracker.AsImmutable());
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(new MessageTracker(), recovered.Tracker.AsImmutable());
    }

    [Fact]
    public async Task Receipt_only_cleanup_preserves_positions_after_recovery()
    {
        await using var session = await fixture.NewSessionAsync();
        session.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        var delivery = Cursor(1);
        Assert.True(session.Tracker.TryAcceptMessage(delivery));
        Assert.True(session.Tracker.TryAcceptMessage(delivery.OutboxToken!));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        session.Tracker.EvictStreamReceipts(DateTimeOffset.MaxValue);
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);

        Assert.Equal(delivery, recovered.Tracker.LatestStream("events", delivery.StreamId!.Value));
        Assert.Equal(delivery.OutboxToken, recovered.Tracker.LatestOutbox(delivery.OutboxToken!.Sender));
        recovered.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        Assert.True(recovered.Tracker.TryAcceptMessage(delivery));
    }
}
