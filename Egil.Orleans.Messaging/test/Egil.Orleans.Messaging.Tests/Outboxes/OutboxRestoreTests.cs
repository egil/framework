using System.Text.Json;
using Orleans.Serialization;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxRestoreTests
{
    private static readonly DateTimeOffset First = new(2026, 5, 23, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Second = First.AddMinutes(1);
    private static readonly DateTimeOffset Third = First.AddMinutes(2);

    [Fact]
    public void Restore_assigns_consecutive_sequence_numbers_from_payload_timestamp_pairs()
    {
        var restored = Outbox<string>.Restore([("first", First), ("second", Second), ("third", Third)]);

        // Assert.Equal<string>, not Assert.Equal: an unpinned collection expression
        // target-types to Outbox<string> through its own [CollectionBuilder] and then
        // compares by revision, which no two snapshots share.
        Assert.Equal<string>(["first", "second", "third"], restored);
        Assert.Equal([1L, 2L, 3L], restored.Envelopes.Select(envelope => envelope.Id.SequenceNumber));
        Assert.Equal([First, Second, Third], restored.Envelopes.Select(envelope => envelope.Id.Timestamp));
        Assert.Equal(3, restored.LatestSequenceNumber);
    }

    [Fact]
    public void Restore_stamps_the_epoch_from_the_first_message_timestamp()
    {
        var restored = Outbox<string>.Restore([("first", First), ("second", Second)]);

        Assert.Equal(First, restored.Epoch);
        Assert.All(restored.Envelopes, envelope => Assert.Equal(First, envelope.Id.Epoch));
    }

    [Fact]
    public void Restore_normalizes_non_utc_timestamps()
    {
        var copenhagen = new DateTimeOffset(2026, 5, 23, 14, 30, 0, TimeSpan.FromHours(2));

        var restored = Outbox<string>.Restore([("first", copenhagen)]);

        Assert.Equal(TimeSpan.Zero, restored.Envelopes[0].Id.Timestamp.Offset);
        Assert.Equal(First, restored.Envelopes[0].Id.Timestamp);
    }

    [Fact]
    public void Restore_does_not_require_timestamps_to_be_ordered()
    {
        // Receivers deduplicate on epoch and sequence number, not on time, and the
        // producing path does not enforce an order either. Inventing the rule here
        // would reject legitimate history from a source that stored domain times.
        var restored = Outbox<string>.Restore([("first", Third), ("second", First)]);

        Assert.Equal([Third, First], restored.Envelopes.Select(envelope => envelope.Id.Timestamp));
        Assert.Equal(Third, restored.Epoch);
    }

    [Fact]
    public void Restore_with_a_shared_timestamp_stamps_every_message_with_it()
    {
        var restored = Outbox<string>.Restore(["first", "second"], First);

        Assert.Equal<string>(["first", "second"], restored);
        Assert.Equal([1L, 2L], restored.Envelopes.Select(envelope => envelope.Id.SequenceNumber));
        Assert.All(restored.Envelopes, envelope => Assert.Equal(First, envelope.Id.Timestamp));
        Assert.Equal(First, restored.Epoch);
    }

    [Fact]
    public void Restore_preserves_sequence_numbers_timestamps_and_epoch_from_envelopes()
    {
        var epoch = First.AddDays(-7);

        OutboxMessageEnvelope<string>[] envelopes =
        [
            new(new OutboxMessageId(7, First, epoch), "first"),
            new(new OutboxMessageId(8, Second, epoch), "second"),
            new(new OutboxMessageId(9, Third, epoch), "third")
        ];

        var restored = Outbox<string>.Restore(envelopes, 9);

        Assert.Equal([7L, 8L, 9L], restored.Envelopes.Select(envelope => envelope.Id.SequenceNumber));
        Assert.Equal([First, Second, Third], restored.Envelopes.Select(envelope => envelope.Id.Timestamp));
        Assert.Equal(epoch, restored.Epoch);
        Assert.Equal(9, restored.LatestSequenceNumber);
    }

    [Fact]
    public void Restore_from_envelopes_preserves_each_message_traceparent()
    {
        // Stored verbatim, including a value that is not a parseable W3C traceparent:
        // the dispatcher already tolerates those by starting an unlinked span, and an
        // exception here would fail a grain activation over a diagnostic field.
        OutboxMessageEnvelope<string>[] envelopes =
        [
            new(new OutboxMessageId(1, First, First, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"), "first"),
            new(new OutboxMessageId(2, Second, First, "|legacy-hierarchical-id.1."), "second"),
            new(new OutboxMessageId(3, Third, First), "third")
        ];

        var restored = Outbox<string>.Restore(envelopes, 3);

        Assert.Equal(
            ["00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", "|legacy-hierarchical-id.1.", null],
            restored.Envelopes.Select(envelope => envelope.Id.TraceParent));
    }

    [Fact]
    public void Restore_of_an_empty_sequence_returns_an_outbox_with_no_epoch()
    {
        var fromPairs = Outbox<string>.Restore(Array.Empty<(string, DateTimeOffset)>());
        var fromPayloads = Outbox<string>.Restore(Array.Empty<string>(), First);
        var fromEnvelopes = Outbox<string>.Restore(Array.Empty<OutboxMessageEnvelope<string>>(), 0);

        Assert.All<Outbox<string>>([fromPairs, fromPayloads, fromEnvelopes], restored =>
        {
            Assert.True(restored.IsEmpty);
            Assert.Null(restored.Epoch);
            Assert.Equal(0, restored.LatestSequenceNumber);
        });
    }

    [Fact]
    public void Restore_mints_a_fresh_revision()
    {
        // Revisions prove which snapshot reached storage, so two independently
        // restored outboxes must not be mistaken for the same write.
        var first = Outbox<string>.Restore([("first", First)]);
        var second = Outbox<string>.Restore([("first", First)]);

        Assert.NotEqual(first.Revision, second.Revision);
        Assert.NotEqual(first, second);
        Assert.Equal(7, first.Revision.Version);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(5, -1)]
    public void Restore_rejects_envelopes_whose_sequence_numbers_do_not_increase(long first, long second)
    {
        // Remove only matches a FIFO head and receivers deduplicate against a per-epoch
        // high-water mark, so a repeated or out-of-order sequence number restores an
        // outbox whose messages the receiver silently drops days later.
        OutboxMessageEnvelope<string>[] envelopes =
        [
            new(new OutboxMessageId(first, First, First), "first"),
            new(new OutboxMessageId(second, Second, First), "second")
        ];

        var exception = Assert.Throws<ArgumentException>(() => Outbox<string>.Restore(envelopes, 9));

        Assert.Equal("envelopes", exception.ParamName);
    }

    [Fact]
    public void Restore_rejects_envelopes_with_differing_epochs()
    {
        // The outbox holds a single epoch field, so a mixed-epoch input has no
        // representation here and must not be silently flattened onto one of them.
        OutboxMessageEnvelope<string>[] envelopes =
        [
            new(new OutboxMessageId(1, First, First), "first"),
            new(new OutboxMessageId(2, Second, Second), "second")
        ];

        var exception = Assert.Throws<ArgumentException>(() => Outbox<string>.Restore(envelopes, 9));

        Assert.Equal("envelopes", exception.ParamName);
    }

    [Fact]
    public void Restore_rejects_a_null_sequence()
    {
        Assert.Throws<ArgumentNullException>(
            () => Outbox<string>.Restore((IEnumerable<(string, DateTimeOffset)>)null!));
        Assert.Throws<ArgumentNullException>(
            () => Outbox<string>.Restore((IEnumerable<string>)null!, First));
        Assert.Throws<ArgumentNullException>(
            () => Outbox<string>.Restore((IEnumerable<OutboxMessageEnvelope<string>>)null!, 0));
    }

    [Fact]
    public void Restore_rejects_a_null_envelope()
    {
        OutboxMessageEnvelope<string>?[] envelopes = [null];

        var exception = Assert.Throws<ArgumentNullException>(() => Outbox<string>.Restore(envelopes!, 1));

        Assert.Equal("envelopes", exception.ParamName);
    }

    [Fact]
    public void Restore_carries_a_high_water_mark_above_the_pending_envelopes()
    {
        // Envelopes hold only what is still pending. A source that already delivered and
        // removed its highest-numbered messages must not restore to a lower mark.
        OutboxMessageEnvelope<string>[] envelopes = [new(new OutboxMessageId(3, First, First), "third")];

        var restored = Outbox<string>.Restore(envelopes, 10);

        Assert.Equal(10, restored.LatestSequenceNumber);
        Assert.Equal(11, restored.Add("next", Second).Envelopes[^1].Id.SequenceNumber);
    }

    [Fact]
    public void Restore_rejects_a_high_water_mark_for_an_empty_sequence()
    {
        // Receivers compare epochs first and sequence numbers only within the same epoch,
        // so a mark with no epoch behind it cannot be honoured. Keeping it would hide that
        // the source epoch was dropped; a drained source starts fresh with Create().
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Outbox<string>.Restore(Array.Empty<OutboxMessageEnvelope<string>>(), 10));

        Assert.Equal("latestSequenceNumber", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Restore_rejects_envelopes_with_a_non_positive_sequence_number(long sequenceNumber)
    {
        // Add assigns from 1, so an id at or below 0 is state the producing path can never
        // reach. Allowing it let a restore build a negative high-water mark, after which
        // the next append handed out sequence 0.
        OutboxMessageEnvelope<string>[] envelopes =
            [new(new OutboxMessageId(sequenceNumber, First, First), "first")];

        var exception = Assert.Throws<ArgumentException>(
            () => Outbox<string>.Restore(envelopes, sequenceNumber));

        Assert.Equal("envelopes", exception.ParamName);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Restore_rejects_a_high_water_mark_below_the_last_envelope(long latestSequenceNumber)
    {
        OutboxMessageEnvelope<string>[] envelopes = [new(new OutboxMessageId(3, First, First), "third")];

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Outbox<string>.Restore(envelopes, latestSequenceNumber));

        Assert.Equal("latestSequenceNumber", exception.ParamName);
    }

    [Fact]
    public void Restore_keeps_a_receiver_accepting_after_the_tail_was_acknowledged()
    {
        // End to end: the source delivered 2 and 3 and removed them out of order, leaving
        // 1 pending. Restoring without the mark would reset it to 1, the next append would
        // reuse sequence 2, and the receiver would reject it as an already-seen duplicate.
        var sender = GrainId.Create("test/sender", "one");
        var outbox = Outbox<string>.Create().AddRange(["a", "b", "c"], First);
        var tracker = new MessageTracker();
        tracker.TryAcceptMessage(outbox.Envelopes[0].Id.ForSender(sender), out tracker);
        tracker.TryAcceptMessage(outbox.Envelopes[1].Id.ForSender(sender), out tracker);
        tracker.TryAcceptMessage(outbox.Envelopes[2].Id.ForSender(sender), out tracker);
        var drained = outbox.RemoveRange([outbox.Envelopes[1].Id, outbox.Envelopes[2].Id]);

        var restored = Outbox<string>.Restore(drained.Envelopes, drained.LatestSequenceNumber);
        var next = restored.Add("d", Second);

        Assert.Equal(4, next.Envelopes[^1].Id.SequenceNumber);
        Assert.True(tracker.TryAcceptMessage(next.Envelopes[^1].Id.ForSender(sender), out _));
    }

    [Fact]
    public void Restored_outbox_accepts_further_appends_without_a_sequence_gap()
    {
        // What makes a migrated outbox usable: the grain keeps appending into the
        // restored sequence space instead of starting a new epoch the receiver would
        // have to accept unconditionally.
        var epoch = First.AddDays(-7);
        OutboxMessageEnvelope<string>[] envelopes =
        [
            new(new OutboxMessageId(7, First, epoch), "first"),
            new(new OutboxMessageId(8, Second, epoch), "second")
        ];

        var appended = Outbox<string>.Restore(envelopes, 8).Add("third", Third);

        Assert.Equal(9, appended.Envelopes[2].Id.SequenceNumber);
        Assert.Equal(9, appended.LatestSequenceNumber);
        Assert.Equal(epoch, appended.Epoch);
        Assert.Equal(epoch, appended.Envelopes[2].Id.Epoch);
    }

    [Fact]
    public void Restore_round_trips_through_json()
    {
        var epoch = First.AddDays(-7);
        OutboxMessageEnvelope<string>[] envelopes =
        [
            new(new OutboxMessageId(7, First, epoch, "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"), "first"),
            new(new OutboxMessageId(8, Second, epoch), "second")
        ];
        var restored = Outbox<string>.Restore(envelopes, 8);

        var loaded = JsonSerializer.Deserialize<Outbox<string>>(JsonSerializer.Serialize(restored));

        Assert.NotNull(loaded);
        Assert.Equal(restored, loaded);
        Assert.Equal(restored.Envelopes, loaded.Envelopes);
        Assert.Equal(
            restored.Envelopes.Select(envelope => envelope.Id.TraceParent),
            loaded.Envelopes.Select(envelope => envelope.Id.TraceParent));
    }

    [Fact]
    public void Restore_round_trips_through_orleans_serialization()
    {
        var restored = Outbox<string>.Restore([("first", First), ("second", Second)]);
        var services = new ServiceCollection();
        services.AddSerializer(builder => builder.AddAssembly(typeof(Outbox<>).Assembly));
        using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<Serializer>();

        var loaded = serializer.Deserialize<Outbox<string>>(serializer.SerializeToArray(restored));

        Assert.NotNull(loaded);
        Assert.Equal(restored, loaded);
        Assert.Equal(restored.Envelopes, loaded.Envelopes);
        Assert.Equal(restored.Epoch, loaded.Epoch);
        Assert.Equal(restored.LatestSequenceNumber, loaded.LatestSequenceNumber);
    }

    [Fact]
    public void Restore_overloads_stay_unambiguous_for_payloads_shaped_like_their_parameters()
    {
        // The pair and envelope overloads are both arity-1. A payload type that is
        // itself a (T, DateTimeOffset) tuple or an OutboxMessageEnvelope<T> is the
        // adversarial closing; this must keep compiling without CS0121.
        var tuples = Outbox<(string Value, DateTimeOffset At)>.Restore([(("first", First), First)]);
        var envelopes = Outbox<OutboxMessageEnvelope<string>>.Restore(
            [(new OutboxMessageEnvelope<string>(new OutboxMessageId(1, First, First), "first"), First)]);

        // For Outbox<OutboxMessageEnvelope<T>> the two arity-2 overloads share a first
        // parameter type and are told apart only by DateTimeOffset versus long.
        OutboxMessageEnvelope<OutboxMessageEnvelope<string>>[] nested =
            [new(new OutboxMessageId(1, First, First), new(new OutboxMessageId(1, First, First), "first"))];
        var asPayloads = Outbox<OutboxMessageEnvelope<string>>.Restore(
            nested.Select(envelope => envelope.Message), First);
        var asEnvelopes = Outbox<OutboxMessageEnvelope<string>>.Restore(nested, 1L);

        Assert.Equal(1, tuples.LatestSequenceNumber);
        Assert.Equal(1, envelopes.LatestSequenceNumber);
        Assert.Equal(1, asPayloads.LatestSequenceNumber);
        Assert.Equal(1, asEnvelopes.LatestSequenceNumber);
    }
}
