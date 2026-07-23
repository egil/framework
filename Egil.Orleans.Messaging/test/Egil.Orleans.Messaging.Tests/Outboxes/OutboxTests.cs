using Orleans.Serialization;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxTests
{
    [Fact]
    public void Add_after_Orleans_deep_copy_uses_explicit_timestamp()
    {
        var sender = GrainId.Create("test/sender", "one");
        var epoch = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var later = epoch.AddMinutes(5);
        var outbox = Outbox<string>.Create(sender).Add("first", epoch);
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(Outbox<>).Assembly));
        using var serviceProvider = services.BuildServiceProvider();
        var copied = serviceProvider.GetRequiredService<DeepCopier>().Copy(outbox);

        var next = copied.Add("second", later);

        Assert.Equal(2, next.Count);
        Assert.Equal(2, next.LatestSequenceNumber);
        Assert.Equal("second", next[1].Message);
        Assert.Equal(new OutboxSequenceToken(2, sender, later, epoch), next[1].Token);
    }

    [Fact]
    public void Add_appends_message_with_next_sequence_sender_and_time()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create(sender);

        var next = outbox.Add("created", now);

        Assert.Empty(outbox);
        Assert.Single(next);
        Assert.Equal(1, next.LatestSequenceNumber);
        Assert.Equal(now, next.Epoch);
        Assert.Equal("created", next[0].Message);
        Assert.Equal(new OutboxSequenceToken(1, sender, now, now), next[0].Token);
    }

    [Fact]
    public void Add_normalizes_supplied_timestamp_to_UTC()
    {
        var sender = GrainId.Create("test/sender", "one");
        var localNow = new DateTimeOffset(2026, 5, 23, 14, 30, 0, TimeSpan.FromHours(2));
        var utcNow = localNow.ToUniversalTime();

        var next = Outbox<string>.Create(sender).Add("created", localNow);

        Assert.Equal(utcNow, next.Epoch);
        Assert.Equal(utcNow, next[0].Token.Timestamp);
        Assert.Equal(TimeSpan.Zero, next[0].Token.Timestamp.Offset);
    }

    [Fact]
    public void Add_preserves_epoch_and_increments_sequence_for_later_messages()
    {
        var sender = GrainId.Create("test/sender", "one");
        var epoch = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var later = epoch.AddMinutes(5);
        var outbox = Outbox<string>.Create(sender);

        var next = outbox.Add("first", epoch);
        next = next.Add("second", later);

        Assert.Equal(2, next.Count);
        Assert.Equal(2, next.LatestSequenceNumber);
        Assert.Equal(epoch, next.Epoch);
        Assert.Equal(new OutboxSequenceToken(1, sender, epoch, epoch), next[0].Token);
        Assert.Equal(new OutboxSequenceToken(2, sender, later, epoch), next[1].Token);
    }

    [Fact]
    public void Remove_deletes_matching_sequence_and_preserves_sequence_metadata()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create(sender);
        outbox = outbox.Add("first", now).Add("second", now);

        var next = outbox.Remove(outbox[0].Token);

        Assert.Single(next);
        Assert.Equal("second", next[0].Message);
        Assert.Equal(2, next.LatestSequenceNumber);
        Assert.Equal(now, next.Epoch);
    }

    [Fact]
    public void Remove_ignores_token_from_different_sender_with_same_sequence()
    {
        var sender = GrainId.Create("test/sender", "one");
        var otherSender = GrainId.Create("test/sender", "two");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create(sender);
        outbox = outbox.Add("first", now);
        var otherToken = new OutboxSequenceToken(1, otherSender, now, now);

        var next = outbox.Remove(otherToken);

        Assert.Same(outbox, next);
        Assert.Single(next);
    }

    [Fact]
    public void Remove_ignores_matching_token_that_is_not_fifo_head()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create(sender);
        outbox = outbox.Add("first", now).Add("second", now);

        var next = outbox.Remove(outbox[1].Token);

        Assert.Same(outbox, next);
        Assert.Equal(2, next.Count);
    }

    [Fact]
    public void RemoveRange_deletes_matching_fifo_prefix_and_ignores_missing_tokens()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create(sender);
        outbox = outbox.Add("first", now).Add("second", now).Add("third", now);
        var missing = new OutboxSequenceToken(99, sender, now, now);

        var next = outbox.RemoveRange([outbox[0].Token, outbox[1].Token, missing]);

        Assert.Single(next);
        Assert.Equal("third", next[0].Message);
        Assert.Equal(3, next.LatestSequenceNumber);
        Assert.Equal(now, next.Epoch);
    }

    [Fact]
    public void RemoveRange_removes_matching_tokens_after_gap_and_preserves_remaining_order()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var outbox = Outbox<string>.Create(sender);
        outbox = outbox.Add("first", now).Add("second", now).Add("third", now);

        var next = outbox.RemoveRange([outbox[0].Token, outbox[2].Token]);

        Assert.Single(next);
        Assert.Equal("second", next[0].Message);
    }

    [Fact]
    public void Clear_removes_items_and_keeps_sequence_metadata_for_next_add()
    {
        var sender = GrainId.Create("test/sender", "one");
        var epoch = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var later = epoch.AddMinutes(5);
        var outbox = Outbox<string>.Create(sender);
        outbox = outbox.Add("first", epoch).Add("second", epoch);

        var cleared = outbox.Clear();
        var next = cleared.Add("third", later);

        Assert.Empty(cleared);
        Assert.Equal(2, cleared.LatestSequenceNumber);
        Assert.Equal(epoch, cleared.Epoch);
        Assert.Equal(new OutboxSequenceToken(3, sender, later, epoch), next[0].Token);
    }

    [Fact]
    public void Equals_treats_matching_sequence_window_as_same_pending_items()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var left = Outbox<string>.Create(sender);
        var right = Outbox<string>.Create(sender);

        left = left.Add("left", now).Add("middle-left", now).Add("last", now);
        right = right.Add("right", now).Add("middle-right", now).Add("last", now);

        Assert.Equal(left, right);
    }

    [Fact]
    public void Equals_returns_true_when_sender_sequence_epoch_and_envelopes_match()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var left = Outbox<string>.Create(sender);
        var right = Outbox<string>.Create(sender);

        left = left.Add("first", now).Add("second", now);
        right = right.Add("first", now).Add("second", now);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_returns_false_when_sequence_metadata_differs()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var left = Outbox<string>.Create(sender);
        var right = Outbox<string>.Create(sender);

        left = left.Add("first", now).Add("second", now);
        right = right.Add("first", now).Add("second", now).Add("third", now);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Equals_returns_false_when_diverged_tail_items_have_different_timestamps()
    {
        var sender = GrainId.Create("test/sender", "one");
        var now = new DateTimeOffset(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
        var left = Outbox<string>.Create(sender);
        var right = Outbox<string>.Create(sender);

        // Shared history establishes the same epoch and head token, then two
        // duplicate activations each append their own message at the same
        // sequence number but at different wall-clock instants.
        left = left.Add("base", now);
        right = right.Add("base", now);
        left = left.Add("from-activation-a", now.AddMilliseconds(1));
        right = right.Add("from-activation-b", now.AddMilliseconds(2));

        Assert.NotEqual(left, right);
    }

}
