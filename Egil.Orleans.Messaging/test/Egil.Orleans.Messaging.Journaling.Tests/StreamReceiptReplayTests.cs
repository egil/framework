#pragma warning disable ORLEANSEXP005 // These tests exercise the pinned experimental journaling package.
using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class StreamReceiptReplayTests(JournalingPrototypeFixture fixture) : IClassFixture<JournalingPrototypeFixture>
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Journal_and_compacted_snapshots_preserve_exact_receipts_and_original_acceptance_time(bool compact, bool nullPosition)
    {
        var receivedAt = DateTimeOffset.UnixEpoch;
        var clock = new ManualTimeProvider(receivedAt);
        await using var session = await fixture.NewSessionAsync(time: clock);
        session.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        var high = Cursor(12, nullPosition ? null : 100);
        var low = Cursor(11, nullPosition ? null : 99);
        Assert.True(session.Tracker.TryAcceptMessage(high));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.True(session.Tracker.TryAcceptMessage(low));
        // Old operation kinds must retain the new receipts while replaying.
        Assert.True(session.Tracker.TryAcceptMessage(high.OutboxToken!));
        Assert.True(session.Tracker.TryAcceptMessage("legacy", new EventSequenceToken(1)));
        session.Tracker.EvictOutboxes(DateTimeOffset.MaxValue);
        fixture.Storage.For(session.Id).CompactNext = compact;
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var expected = session.Tracker.AsImmutable();
        await session.DisposeAsync();
        clock.Advance(TimeSpan.FromDays(1));

        await using var recovered = await fixture.NewSessionAsync(session.Id, clock);
        recovered.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);

        Assert.Equal(expected, recovered.Tracker.AsImmutable());
        Assert.False(recovered.Tracker.TryAcceptMessage(high with { Token = new EventSequenceToken(101) }));
        Assert.False(recovered.Tracker.TryAcceptMessage(low));
        Assert.Equal(nullPosition ? null : high, recovered.Tracker.LatestStream("events", high.StreamId!.Value));
        recovered.Tracker.Evict("events", high.StreamId.Value, receivedAt);
        await recovered.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await recovered.DisposeAsync();
        await using var evicted = await fixture.NewSessionAsync(session.Id, clock);
        evicted.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        Assert.True(evicted.Tracker.TryAcceptMessage(high));
        Assert.NotNull(evicted.Tracker.LatestStream("legacy"));
    }

    [Fact]
    public async Task Full_stream_eviction_replays_without_removing_another_key_in_the_same_namespace()
    {
        await using var session = await fixture.NewSessionAsync();
        session.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);
        var first = Cursor(1, 1);
        var second = first with { StreamId = StreamId.Create("orders", "second") };
        Assert.True(session.Tracker.TryAcceptMessage(first));
        Assert.True(session.Tracker.TryAcceptMessage(second));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        session.Tracker.Evict(first.StreamId!.Value, DateTimeOffset.MaxValue);
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        await using var recovered = await fixture.NewSessionAsync(session.Id);
        recovered.Tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity);

        Assert.True(recovered.Tracker.TryAcceptMessage(first));
        Assert.False(recovered.Tracker.TryAcceptMessage(second));
    }

    private static StreamCursor Cursor(long sequence, long? position) => new("orders", position is { } number ? new EventSequenceToken(number) : null, "events")
    {
        StreamId = StreamId.Create("orders", "first"),
        OutboxToken = new(sequence, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
    };
}
