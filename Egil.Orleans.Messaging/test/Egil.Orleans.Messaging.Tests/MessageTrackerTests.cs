using Orleans.Providers.Streams.Common;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Tests;

public sealed class MessageTrackerTests
{
    [Fact]
    public void ProcessMessage_accepts_first_stream_cursor_and_tracks_latest_position()
    {
        var streamId = StreamId.Create("orders", "one");
        var cursor = new StreamCursor(streamId, new EventSequenceToken(7));
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero));
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);

        var accepted = tracker.ProcessMessage(cursor, out var next);

        Assert.True(accepted);
        Assert.Null(tracker.LatestStream(streamId));
        Assert.Equal(cursor, next.LatestStream(streamId));
        Assert.NotSame(tracker, next);
    }

    [Fact]
    public void ProcessMessage_accepts_first_outbox_token_and_tracks_latest_position()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        var token = new OutboxSequenceToken(1, sender, now, now);

        var accepted = tracker.ProcessMessage(token, out var next);

        Assert.True(accepted);
        Assert.Null(tracker.LatestOutbox(sender));
        Assert.Equal(token, next.LatestOutbox(sender));
    }

    [Fact]
    public void Evict_removes_stream_and_outbox_entries_at_or_before_cutoff()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", "one");
        var sender = GrainId.Create("test/sender", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out tracker);
        tracker.ProcessMessage(new OutboxSequenceToken(1, sender, now, now), out tracker);

        var evicted = tracker.Evict(now);

        Assert.Null(evicted.LatestStream(streamId));
        Assert.Null(evicted.LatestOutbox(sender));
    }

    [Fact]
    public void ProcessMessage_rejects_stream_cursor_when_token_is_not_newer()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var streamId = StreamId.Create("orders", "one");
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out tracker);

        var accepted = tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out var next);

        Assert.False(accepted);
        Assert.Same(tracker, next);
    }

    [Fact]
    public void ProcessMessage_accepts_stream_cursor_when_token_is_newer()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var streamId = StreamId.Create("orders", "one");
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out tracker);

        var accepted = tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(8)), out var next);

        Assert.True(accepted);
        Assert.Equal(new StreamCursor(streamId, new EventSequenceToken(8)), next.LatestStream(streamId));
    }

    [Fact]
    public void ProcessMessage_accepts_non_null_stream_token_after_null_initial_position()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var streamId = StreamId.Create("orders", "one");
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, null), out tracker);

        var accepted = tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(1)), out var next);

        Assert.True(accepted);
        Assert.Equal(new StreamCursor(streamId, new EventSequenceToken(1)), next.LatestStream(streamId));
    }

    [Fact]
    public void ProcessMessage_rejects_null_stream_token_after_position_is_tracked()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var streamId = StreamId.Create("orders", "one");
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(1)), out tracker);

        var accepted = tracker.ProcessMessage(new StreamCursor(streamId, null), out var next);

        Assert.False(accepted);
        Assert.Same(tracker, next);
    }

    [Fact]
    public void ProcessMessage_rejects_outbox_token_when_same_epoch_and_not_newer_sequence()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new OutboxSequenceToken(5, sender, now, now), out tracker);

        var accepted = tracker.ProcessMessage(new OutboxSequenceToken(5, sender, now, now), out var next);

        Assert.False(accepted);
        Assert.Same(tracker, next);
    }

    [Fact]
    public void ProcessMessage_rejects_outbox_token_when_epoch_is_stale()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var oldEpoch = now.AddMinutes(-10);
        var newEpoch = now;
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new OutboxSequenceToken(1, sender, now, newEpoch), out tracker);

        var accepted = tracker.ProcessMessage(new OutboxSequenceToken(99, sender, now, oldEpoch), out var next);

        Assert.False(accepted);
        Assert.Same(tracker, next);
    }

    [Fact]
    public void ProcessMessage_accepts_outbox_token_when_epoch_is_newer()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var oldEpoch = now.AddMinutes(-10);
        var newEpoch = now;
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new OutboxSequenceToken(10, sender, now, oldEpoch), out tracker);

        var accepted = tracker.ProcessMessage(new OutboxSequenceToken(1, sender, now, newEpoch), out var next);

        Assert.True(accepted);
        Assert.Equal(new OutboxSequenceToken(1, sender, now, newEpoch), next.LatestOutbox(sender));
    }

    [Fact]
    public void EvictStreams_only_removes_stream_entries()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", "one");
        var sender = GrainId.Create("test/sender", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out tracker);
        tracker.ProcessMessage(new OutboxSequenceToken(1, sender, now, now), out tracker);

        var evicted = tracker.EvictStreams(now);

        Assert.Null(evicted.LatestStream(streamId));
        Assert.Equal(new OutboxSequenceToken(1, sender, now, now), evicted.LatestOutbox(sender));
    }

    [Fact]
    public void EvictOutboxes_only_removes_outbox_entries()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", "one");
        var sender = GrainId.Create("test/sender", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out tracker);
        tracker.ProcessMessage(new OutboxSequenceToken(1, sender, now, now), out tracker);

        var evicted = tracker.EvictOutboxes(now);

        Assert.Equal(new StreamCursor(streamId, new EventSequenceToken(7)), evicted.LatestStream(streamId));
        Assert.Null(evicted.LatestOutbox(sender));
    }

    [Fact]
    public void Evict_stream_entry_respects_cutoff_for_targeted_remove()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out tracker);

        var notEvicted = tracker.Evict(streamId, now.AddTicks(-1));
        var evicted = tracker.Evict(streamId, now);

        Assert.Equal(new StreamCursor(streamId, new EventSequenceToken(7)), notEvicted.LatestStream(streamId));
        Assert.Null(evicted.LatestStream(streamId));
    }

    [Fact]
    public void Evict_outbox_entry_respects_cutoff_for_targeted_remove()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var time = new ManualTimeProvider(now);
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(time);
        tracker.ProcessMessage(new OutboxSequenceToken(1, sender, now, now), out tracker);

        var notEvicted = tracker.Evict(sender, now.AddTicks(-1));
        var evicted = tracker.Evict(sender, now);

        Assert.Equal(new OutboxSequenceToken(1, sender, now, now), notEvicted.LatestOutbox(sender));
        Assert.Null(evicted.LatestOutbox(sender));
    }

    [Fact]
    public void Equals_returns_false_when_tracked_entries_differ()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var streamId = StreamId.Create("orders", "one");
        var time = new ManualTimeProvider(now);
        var left = new MessageTracker();
        var right = new MessageTracker();
        left.RegisterTimeProvider(time);
        right.RegisterTimeProvider(time);
        left.ProcessMessage(new StreamCursor(streamId, new EventSequenceToken(7)), out left);
        right.ProcessMessage(new OutboxSequenceToken(1, sender, now, now), out right);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_returns_false_when_stream_keys_differ_but_counts_match()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var left = CreateTracker(
            stream: new StreamCursor(StreamId.Create("orders", "one"), new EventSequenceToken(7)),
            received: now);
        var right = CreateTracker(
            stream: new StreamCursor(StreamId.Create("orders", "two"), new EventSequenceToken(7)),
            received: now);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_returns_false_when_stream_values_differ_but_keys_match()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var streamId = StreamId.Create("orders", "one");
        var left = CreateTracker(new StreamCursor(streamId, new EventSequenceToken(7)), now);
        var right = CreateTracker(new StreamCursor(streamId, new EventSequenceToken(8)), now);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_returns_false_when_outbox_keys_differ_but_counts_match()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var left = CreateTracker(
            outbox: new OutboxSequenceToken(1, GrainId.Create("test/sender", "one"), now, now),
            received: now);
        var right = CreateTracker(
            outbox: new OutboxSequenceToken(1, GrainId.Create("test/sender", "two"), now, now),
            received: now);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_returns_false_when_outbox_values_differ_but_keys_match()
    {
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var sender = GrainId.Create("test/sender", "one");
        var left = CreateTracker(new OutboxSequenceToken(1, sender, now, now), now);
        var right = CreateTracker(new OutboxSequenceToken(2, sender, now, now), now);

        Assert.NotEqual(left, right);
    }

    private static MessageTracker CreateTracker(StreamCursor stream, DateTimeOffset received)
    {
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(received));
        tracker.ProcessMessage(stream, out tracker);

        return tracker;
    }

    private static MessageTracker CreateTracker(OutboxSequenceToken outbox, DateTimeOffset received)
    {
        var tracker = new MessageTracker();
        tracker.RegisterTimeProvider(new ManualTimeProvider(received));
        tracker.ProcessMessage(outbox, out tracker);

        return tracker;
    }

}
