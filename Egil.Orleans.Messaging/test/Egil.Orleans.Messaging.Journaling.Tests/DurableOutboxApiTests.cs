#pragma warning disable ORLEANSEXP005 // This suite explicitly exercises the pinned experimental journaling package.
using System.Collections;
using TimeProviderExtensions;

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class DurableOutboxApiTests(JournalingPrototypeFixture fixture)
    : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task Fluent_appends_journal_every_message_and_expose_the_staged_collection()
    {
        var now = DateTimeOffset.Parse("2026-09-01T10:00:00+02:00", System.Globalization.CultureInfo.InvariantCulture);
        await using var session = await fixture.NewSessionAsync(time: new ManualTimeProvider(now));
        var outbox = session.Outbox;
        var original = outbox.AsImmutable();

        var returned = outbox.Add(new OrderEvent("one"))
            .Add(new OrderEvent("two"), now.AddMinutes(1))
            .AddRange([new OrderEvent("three")])
            .AddRange([new OrderEvent("four"), new OrderEvent("five")], now.AddMinutes(2));

        Assert.Same(outbox, returned);
        Assert.True(original.IsEmpty);
        Assert.False(outbox.IsEmpty);
        Assert.Equal(5, outbox.Count);
        Assert.Equal(5, outbox.LatestSequenceNumber);
        Assert.Equal(now.ToUniversalTime(), outbox.Epoch);
        Assert.NotEqual(original.Revision, outbox.Revision);
        Assert.Equal("two", outbox[1].Text);
        Assert.Equal(new[] { "one", "two", "three", "four", "five" }, outbox.Select(item => item.Text));
        Assert.Equal(outbox.ToArray(), ((IEnumerable)outbox).Cast<OrderEvent>());
        Assert.Equal(new[] { now, now.AddMinutes(1), now, now.AddMinutes(2), now.AddMinutes(2) },
            outbox.Envelopes.Select(item => item.Id.Timestamp));
        Assert.All(outbox.Envelopes, item => Assert.Equal(TimeSpan.Zero, item.Id.Timestamp.Offset));
        var staged = outbox.AsImmutable();
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await session.DisposeAsync();

        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(staged.Revision, recovered.Outbox.Revision);
        Assert.Equal(staged.Envelopes, recovered.Outbox.Envelopes);
        Assert.Equal(staged.LatestSequenceNumber, recovered.Outbox.LatestSequenceNumber);
        Assert.Equal(staged.Epoch, recovered.Outbox.Epoch);
    }

    [Fact]
    public async Task A_failed_batch_enumeration_leaves_no_partial_append_to_commit()
    {
        await using var session = await fixture.NewSessionAsync();
        session.Outbox.Add(new OrderEvent("already-pending"));
        var pending = session.Outbox.AsImmutable();

        Assert.Throws<InvalidOperationException>(() => session.Outbox.AddRange(FailingBatch()));

        Assert.Same(pending, session.Outbox.AsImmutable());
        session.Outbox.AddRange([new OrderEvent("after-failure")]);
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("must-not-appear", Assert.Single(fixture.Storage.For(session.Id).Writes), StringComparison.Ordinal);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(new[] { "already-pending", "after-failure" }, recovered.Outbox.Select(item => item.Text));
        Assert.Equal(new long[] { 1, 2 }, recovered.Outbox.Envelopes.Select(item => item.Id.SequenceNumber));
    }

    [Fact]
    public async Task Head_and_batch_acknowledgement_overloads_preserve_their_different_matching_rules()
    {
        await using var session = await fixture.NewSessionAsync();
        var outbox = session.Outbox;
        outbox.AddRange([new OrderEvent("one"), new OrderEvent("two"), new OrderEvent("three"),
            new OrderEvent("four"), new OrderEvent("five"), new OrderEvent("six")]);
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var original = outbox.AsImmutable();
        var envelopes = outbox.Envelopes;

        Assert.Same(outbox, outbox.Remove(envelopes[1]).Remove(envelopes[1].Id).AddRange([]));
        Assert.Same(original, outbox.AsImmutable());
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Single(fixture.Storage.For(session.Id).Writes);

        Assert.Same(outbox, outbox.Remove(envelopes[0]).Remove(envelopes[1].Id)
            .RemoveRange(new[] { envelopes[2], envelopes[4] })
            .RemoveRange(new[] { envelopes[5].Id }));
        Assert.Equal(envelopes[3], Assert.Single(outbox.Envelopes));
        var staged = outbox.AsImmutable();
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var delta = fixture.Storage.For(session.Id).Writes[^1];
        Assert.DoesNotContain("Message", delta, StringComparison.Ordinal);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(staged, recovered.Outbox.AsImmutable());
        Assert.Equal(envelopes[3], Assert.Single(recovered.Outbox.Envelopes));
        Assert.Same(recovered.Outbox, recovered.Outbox.Clear());
        Assert.True(recovered.Outbox.IsEmpty);
        Assert.Equal(6, recovered.Outbox.LatestSequenceNumber);
        Assert.Equal(original.Epoch, recovered.Outbox.Epoch);
    }

    private static IEnumerable<OrderEvent> FailingBatch()
    {
        yield return new OrderEvent("must-not-appear");
        throw new InvalidOperationException("Cannot enumerate the rest of the batch.");
    }
}
