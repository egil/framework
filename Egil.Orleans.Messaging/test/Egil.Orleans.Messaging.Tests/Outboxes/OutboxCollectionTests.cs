using System.Collections;
using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxCollectionTests
{
    [Fact]
    public void Appending_an_outbox_to_itself_reenqueues_payloads_without_changing_original_ids()
    {
        Outbox<string> original = ["same", "other"];
        var originalEnvelopes = original.Envelopes;

        var appended = original.AddRange(original, DateTimeOffset.UnixEpoch);

        Assert.Equal(["same", "other", "same", "other"], appended.ToArray());
        Assert.Equal(originalEnvelopes, appended.Envelopes.Take(2));
        Assert.Equal([1L, 2L, 3L, 4L], appended.Envelopes.Select(item => item.Id.SequenceNumber));
        Assert.Equal(2, original.Count);
    }

    [Fact]
    public void Batch_input_failure_leaves_the_original_snapshot_and_history_usable()
    {
        Outbox<string> original = ["original"];
        var revision = original.Revision;

        Assert.Throws<InvalidOperationException>(() => original.AddRange(FailingBatch()));
        var next = original.Add("next");

        Assert.Equal(revision, original.Revision);
        Assert.Equal(["original"], original.ToArray());
        Assert.Equal(["original", "next"], next.ToArray());
        Assert.Equal(2, next.LatestSequenceNumber);
    }

    private static IEnumerable<string> FailingBatch()
    {
        yield return "partial";
        throw new InvalidOperationException("Input could not be fully read.");
    }

    [Fact]
    public void Collection_expressions_enqueue_payloads_with_fresh_history()
    {
        Outbox<string> empty = [];
        Outbox<string> single = ["first"];
        Outbox<string> source = ["first", "second"];
        var drained = source.Clear();

        Outbox<string> copied = [.. source, "third"];
        Outbox<string> restarted = [.. drained, "new"];

        Assert.Empty(empty);
        Assert.Null(empty.Epoch);
        Assert.Equal(0, empty.LatestSequenceNumber);
        Assert.Equal(7, empty.Revision.Version);
        Assert.Equal("first", Assert.Single(single));
        Assert.Equal(["first", "second", "third"], copied.ToArray());
        Assert.Equal([1L, 2L, 3L], copied.Envelopes.Select(item => item.Id.SequenceNumber));
        Assert.NotEqual(source.Revision, copied.Revision);
        Assert.Equal(1, restarted.LatestSequenceNumber);
        Assert.Equal(2, drained.LatestSequenceNumber);
    }

    [Fact]
    public void Payload_views_and_envelope_snapshot_stay_consistent_after_append()
    {
        Outbox<string> original = ["first", "second"];
        var envelopes = original.Envelopes;
        IReadOnlyList<string> list = original;
        IEnumerable untyped = original;

        var appended = original.AddRange(["third", "fourth"]);

        Assert.Equal("second", list[1]);
        Assert.Equal(["first", "second"], untyped.Cast<string>());
        Assert.Equal(["first", "second"], original.Select(item => item));
        Assert.Equal(["first", "second"], envelopes.Select(item => item.Message));
        Assert.Equal(["first", "second", "third", "fourth"], appended.ToArray());
        Assert.Equal(envelopes[0], appended.Envelopes[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Batch_append_preserves_history_and_assigns_consecutive_ids(bool lazy)
    {
        var epoch = DateTimeOffset.UnixEpoch;
        var later = epoch.AddHours(1).ToOffset(TimeSpan.FromHours(2));
        var original = Outbox<string>.Create().Add("old", epoch).Clear();
        IEnumerable<string> messages = lazy ? YieldMessages() : new[] { "first", "second" };

        var appended = original.AddRange(messages, later);

        Assert.Empty(original);
        Assert.Equal(1, original.LatestSequenceNumber);
        Assert.Equal(epoch, appended.Epoch);
        Assert.Equal(3, appended.LatestSequenceNumber);
        Assert.Equal(["first", "second"], appended.ToArray());
        Assert.Equal(new OutboxMessageId(2, later.ToUniversalTime(), epoch), appended.Envelopes[0].Id);
        Assert.Equal(new OutboxMessageId(3, later.ToUniversalTime(), epoch), appended.Envelopes[1].Id);
        Assert.NotEqual(original.Revision, appended.Revision);
        Assert.Equal(7, appended.Revision.Version);
    }

    [Fact]
    public void Empty_batch_preserves_snapshot_and_first_batch_establishes_epoch()
    {
        Outbox<string> empty = [];
        var now = DateTimeOffset.UnixEpoch;

        var appended = empty.AddRange(["first", "second"], now);

        Assert.Same(empty, empty.AddRange([]));
        Assert.Same(appended, appended.AddRange(Enumerable.Empty<string>().Where(_ => true)));
        Assert.Equal(now, appended.Epoch);
        Assert.Equal(new OutboxMessageId(1, now, now), appended.Envelopes[0].Id);
        Assert.Equal(new OutboxMessageId(2, now, now), appended.Envelopes[1].Id);
    }

    [Fact]
    public void Envelope_acknowledgement_distinguishes_equal_payloads_and_preserves_gaps()
    {
        Outbox<string> original = ["same", "same", "third"];
        var envelopes = original.Envelopes;

        var headRemoved = original.Remove(envelopes[0]);
        var batchRemoved = original.RemoveRange(new[] { envelopes[1], envelopes[2] });

        Assert.Same(original, original.Remove(envelopes[1]));
        Assert.Equal(envelopes[1], headRemoved.Envelopes[0]);
        Assert.Equal(envelopes[0], Assert.Single(batchRemoved.Envelopes));
        Assert.Equal(original.LatestSequenceNumber, batchRemoved.LatestSequenceNumber);
        Assert.Equal(original.Epoch, batchRemoved.Epoch);
        Assert.NotEqual(original.Revision, batchRemoved.Revision);
        Assert.Same(original, original.RemoveRange(ImmutableArray<OutboxMessageEnvelope<string>>.Empty));
        Assert.Same(batchRemoved, batchRemoved.RemoveRange(new[] { envelopes[1] }));
    }

    private static IEnumerable<string> YieldMessages()
    {
        yield return "first";
        yield return "second";
    }
}
