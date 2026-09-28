using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests.Tracking;

public sealed class MessageTrackerPolicyTests
{
    [Fact]
    public void Default_tracking_accepts_republication_at_a_new_provider_position()
    {
        var delivery = Cursor(1, 100);
        Assert.True(new MessageTracker().TryAcceptMessage(delivery, out var tracker));

        Assert.True(tracker.TryAcceptMessage(delivery with { Token = new EventSequenceToken(101) }, out var next));

        Assert.Equal(new EventSequenceToken(101), next.LatestStreamSequenceToken("events", delivery.StreamId!.Value));
        Assert.False(next.TryAcceptMessage(delivery, out _));
    }

    [Fact]
    public void Default_tracking_rejects_older_provider_positions_even_for_unseen_outbox_identities()
    {
        Assert.True(new MessageTracker().TryAcceptMessage(Cursor(1, 100), out var tracker));

        Assert.False(tracker.TryAcceptMessage(Cursor(2, 99), out var next));

        Assert.Same(tracker, next);
    }

    [Fact]
    public void Default_tracking_does_not_store_receipts_for_tokenless_deliveries()
    {
        var tracker = new MessageTracker();

        Assert.True(tracker.TryAcceptMessage(Cursor(1, null), out var next));

        Assert.Same(tracker, next);
    }

    [Theory]
    [InlineData(StreamTrackingMode.StreamPosition)]
    [InlineData(StreamTrackingMode.OutboxIdentity)]
    public void Retention_is_disabled_by_default_in_both_tracking_modes(StreamTrackingMode mode)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(clock);
        tracker.Configure(options => options.StreamTrackingMode = mode);
        var old = Cursor(1, 1);
        Assert.True(tracker.TryAcceptMessage(old, out tracker));
        Assert.True(tracker.TryAcceptMessage(old.OutboxToken!, out tracker));
        clock.Advance(TimeSpan.FromDays(3650));

        Assert.True(tracker.TryAcceptMessage(Cursor(2, 2) with { StreamId = StreamId.Create("orders", "other") }, out tracker));

        Assert.NotNull(tracker.LatestStream("events", old.StreamId!.Value));
        Assert.False(tracker.TryAcceptMessage(old, out _));
        Assert.False(tracker.TryAcceptMessage(old.OutboxToken!, out _));
    }

    [Fact]
    public void Null_retention_disables_previously_configured_eviction()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = RetainedTracker(clock);
        var old = Cursor(1, 1);
        Assert.True(tracker.TryAcceptMessage(old, out tracker));
        Assert.True(tracker.TryAcceptMessage(old.OutboxToken!, out tracker));
        tracker.Configure(options => options.RetentionPeriod = null);
        clock.Advance(TimeSpan.FromDays(3650));

        Assert.True(tracker.TryAcceptMessage(Cursor(2, 2) with { StreamId = StreamId.Create("orders", "other") }, out tracker));

        Assert.Equal(old, tracker.LatestStream("events", old.StreamId!.Value));
        Assert.False(tracker.TryAcceptMessage(old with { Token = new EventSequenceToken(99) }, out _));
        Assert.False(tracker.TryAcceptMessage(old.OutboxToken!, out _));
    }

    [Fact]
    public void Updated_checkpoints_expire_from_their_latest_acceptance()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = RetainedTracker(clock);
        tracker.Configure(options => options.StreamTrackingMode = StreamTrackingMode.StreamPosition);
        Assert.True(tracker.TryAcceptMessage(Cursor(1, 1), out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));
        var latest = Cursor(2, 2);
        Assert.True(tracker.TryAcceptMessage(latest, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(tracker.TryAcceptMessage(Cursor(3, 3) with { StreamId = StreamId.Create("orders", "other") }, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1));

        Assert.False(tracker.TryAcceptMessage(latest, out _));
        clock.Advance(TimeSpan.FromTicks(1));

        Assert.True(tracker.TryAcceptMessage(latest, out _));
    }

    [Fact]
    public void Acceptance_uses_the_settings_captured_before_reading_the_clock()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var callbackClock = new CallbackTimeProvider(clock);
        var tracker = RetainedTracker(callbackClock);
        var delivery = Cursor(1, 1);
        Assert.True(tracker.TryAcceptMessage(delivery, out tracker));
        clock.Advance(TimeSpan.FromHours(1));
        callbackClock.BeforeRead = () => tracker.Configure(options => options.RetentionPeriod = null);

        Assert.True(tracker.TryAcceptMessage(delivery, out tracker));

        callbackClock.BeforeRead = null;
        clock.Advance(TimeSpan.FromDays(1));
        Assert.False(tracker.TryAcceptMessage(delivery, out _));
    }

    [Fact]
    public void Earlier_acceptance_after_a_clock_change_is_not_skipped_by_retention()
    {
        var tracker = RetainedTracker(new ManualTimeProvider(DateTimeOffset.UnixEpoch));
        var first = Cursor(1, 1);
        Assert.True(tracker.TryAcceptMessage(first, out tracker));
        var earlierClock = new ManualTimeProvider(DateTimeOffset.UnixEpoch.AddMinutes(-30));
        tracker.RegisterTimeProvider(earlierClock);
        var earlier = Cursor(2, 2) with { StreamId = StreamId.Create("orders", "earlier") };
        Assert.True(tracker.TryAcceptMessage(earlier, out tracker));
        earlierClock.Advance(TimeSpan.FromHours(1));

        Assert.True(tracker.TryAcceptMessage(new StreamCursor("orders", null), out tracker));

        Assert.Null(tracker.LatestStream("events", earlier.StreamId!.Value));
        Assert.True(tracker.TryAcceptMessage(earlier, out _));
        Assert.False(tracker.TryAcceptMessage(first, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accepted_messages_remove_expired_sources_and_receipts_but_keep_recent_entries(bool receiveRpc)
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = RetainedTracker(clock);
        var old = Cursor(1, 1);
        var staleStream = old with { StreamId = StreamId.Create("orders", "completed") };
        Assert.True(tracker.TryAcceptMessage(old, out tracker));
        Assert.True(tracker.TryAcceptMessage(staleStream, out tracker));
        Assert.True(tracker.TryAcceptMessage(old.OutboxToken!, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));
        var recent = Cursor(2, 2);
        Assert.True(tracker.TryAcceptMessage(recent, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.NotNull(tracker.LatestOutbox(old.OutboxToken!.Sender));
        Assert.NotNull(tracker.LatestStream("events", staleStream.StreamId!.Value));

        var trigger = Cursor(3, 3);
        Assert.True(receiveRpc
            ? tracker.TryAcceptMessage(trigger.OutboxToken! with { Sender = GrainId.Create("sender", "other") }, out tracker)
            : tracker.TryAcceptMessage(trigger with { StreamId = StreamId.Create("orders", "other") }, out tracker));

        Assert.Null(tracker.LatestOutbox(old.OutboxToken!.Sender))
        Assert.Null(tracker.LatestStream("events", staleStream.StreamId.Value));
        Assert.Equal(recent, tracker.LatestStream("events", recent.StreamId!.Value));
        Assert.True(tracker.TryAcceptMessage(old, out _));
        Assert.False(tracker.TryAcceptMessage(recent, out _));
    }

    [Fact]
    public void Expiration_uses_acceptance_time_and_allows_a_retry_at_the_exact_cutoff()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = RetainedTracker(clock);
        var delivery = Cursor(1, 1) with { OutboxToken = Cursor(1, 1).OutboxToken! with { Timestamp = DateTimeOffset.MaxValue } };
        Assert.True(tracker.TryAcceptMessage(delivery, out tracker));
        clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromTicks(1));
        Assert.False(tracker.TryAcceptMessage(delivery, out var duplicate));
        Assert.Same(tracker, duplicate);
        clock.Advance(TimeSpan.FromTicks(1));

        Assert.True(tracker.TryAcceptMessage(delivery, out var accepted));

        Assert.False(accepted.TryAcceptMessage(delivery, out _));
    }

    [Fact]
    public void Rejected_duplicates_do_not_publish_cleanup_changes()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = RetainedTracker(clock);
        var oldSender = Cursor(1, 1).OutboxToken!;
        Assert.True(tracker.TryAcceptMessage(oldSender, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));
        var recent = Cursor(2, 2);
        Assert.True(tracker.TryAcceptMessage(recent, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(tracker.TryAcceptMessage(recent, out var rejected));

        Assert.Same(tracker, rejected);
        Assert.NotNull(rejected.LatestOutbox(oldSender.Sender));
        Assert.True(tracker.TryAcceptMessage(Cursor(3, null), out var accepted));
        Assert.Null(accepted.LatestOutbox(oldSender.Sender));
    }

    [Fact]
    public void Instance_configuration_is_snapshotted_and_inherited_by_new_tracker_values()
    {
        MessageTrackerOptions? captured = null;
        var tracker = new MessageTracker();
        tracker.Configure(options =>
        {
            options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
            captured = options;
        });
        captured!.StreamTrackingMode = StreamTrackingMode.StreamPosition;
        Assert.True(tracker.TryAcceptMessage(Cursor(1, 1), out tracker));

        Assert.False(tracker.TryAcceptMessage(Cursor(1, 2), out _));
        Assert.True(new MessageTracker().TryAcceptMessage(Cursor(1, 1), out var unrelated));
        Assert.True(unrelated.TryAcceptMessage(Cursor(1, 2), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_retention_is_rejected_without_changing_the_tracker_policy(int seconds)
    {
        var tracker = new MessageTracker();

        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.Configure(options =>
        {
            options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
            options.RetentionPeriod = TimeSpan.FromSeconds(seconds);
        }));

        Assert.True(tracker.TryAcceptMessage(Cursor(1, 1), out tracker));
        Assert.True(tracker.TryAcceptMessage(Cursor(1, 2), out _));
    }

    private static MessageTracker RetainedTracker(TimeProvider clock)
    {
        var tracker = new MessageTracker();
        tracker.Configure(options =>
        {
            options.StreamTrackingMode = StreamTrackingMode.OutboxIdentity;
            options.RetentionPeriod = TimeSpan.FromHours(1);
            options.TimeProvider = clock;
        });
        return tracker;
    }

    [Fact]
    public void Default_tracking_stores_the_same_state_with_or_without_sender_metadata()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tagged = new MessageTracker();
        var ordinary = new MessageTracker();
        tagged.RegisterTimeProvider(clock);
        ordinary.RegisterTimeProvider(clock);
        var delivery = Cursor(1, 1);

        Assert.True(tagged.TryAcceptMessage(delivery, out tagged));
        Assert.True(ordinary.TryAcceptMessage(delivery with { OutboxToken = null }, out ordinary));

        Assert.Equal(ordinary, tagged);
    }

    [Fact]
    public void Receipt_only_eviction_preserves_resume_checkpoints_and_rpc_positions()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var tracker = RetainedTracker(clock);
        var first = Cursor(1, 1);
        Assert.True(tracker.TryAcceptMessage(first, out tracker));
        Assert.True(tracker.TryAcceptMessage(first.OutboxToken!, out tracker));
        clock.Advance(TimeSpan.FromMinutes(1));
        var recent = Cursor(2, 2);
        Assert.True(tracker.TryAcceptMessage(recent, out tracker));

        var evicted = tracker.EvictStreamReceipts(DateTimeOffset.UnixEpoch);

        Assert.Equal(recent, evicted.LatestStream("events", recent.StreamId!.Value));
        Assert.Equal(first.OutboxToken, evicted.LatestOutbox(first.OutboxToken!.Sender));
        Assert.True(evicted.TryAcceptMessage(first, out _));
        Assert.False(evicted.TryAcceptMessage(recent, out _));
        Assert.False(tracker.TryAcceptMessage(first, out _));
    }

    [Fact]
    public void A_retention_window_before_the_clock_range_does_not_expire_the_oldest_representable_entry()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.MinValue);
        var tracker = RetainedTracker(clock);
        var delivery = Cursor(1, null);
        Assert.True(tracker.TryAcceptMessage(delivery, out tracker));
        clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(tracker.TryAcceptMessage(delivery, out _));
    }

    private static StreamCursor Cursor(long sequence, long? position) => new("orders", position is { } number ? new EventSequenceToken(number) : null, "events")
    {
        StreamId = StreamId.Create("orders", "first"),
        OutboxToken = new(sequence, GrainId.Create("sender", "one"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
    };

    private sealed class CallbackTimeProvider(TimeProvider clock) : TimeProvider
    {
        public Action? BeforeRead { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            BeforeRead?.Invoke();
            return clock.GetUtcNow();
        }
    }
}
