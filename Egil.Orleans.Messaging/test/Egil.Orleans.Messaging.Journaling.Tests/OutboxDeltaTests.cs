#pragma warning disable ORLEANSEXP005 // This suite explicitly exercises the pinned experimental journaling package.

namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class OutboxDeltaTests(JournalingPrototypeFixture fixture)
    : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task A_write_contains_only_the_net_changes_since_recovery()
    {
        await using var initial = await fixture.NewSessionAsync();
        initial.Outbox.AddRange([new OrderEvent("retained-payload"), new OrderEvent("removed-payload")]);
        await initial.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await initial.DisposeAsync();
        await using var session = await fixture.NewSessionAsync(initial.Id);
        var storage = fixture.Storage.For(session.Id);
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.Single(storage.Writes);
        var original = session.Outbox.AsImmutable();
        session.Outbox.RemoveRange(new[] { session.Outbox.Envelopes[1] });
        session.Outbox.Add(new OrderEvent("transient-payload"));
        var transient = session.Outbox.Envelopes[^1];
        session.Outbox.Add(new OrderEvent("new-payload"));
        session.Outbox.RemoveRange(new[] { transient });
        var expected = session.Outbox.AsImmutable();

        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        var delta = storage.Writes[^1];
        Assert.DoesNotContain("retained-payload", delta, StringComparison.Ordinal);
        Assert.DoesNotContain("removed-payload", delta, StringComparison.Ordinal);
        Assert.DoesNotContain("transient-payload", delta, StringComparison.Ordinal);
        Assert.Contains("new-payload", delta, StringComparison.Ordinal);
        Assert.Equal(2, original.Count);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(expected, recovered.Outbox.AsImmutable());
        Assert.Equal(expected.Envelopes, recovered.Outbox.Envelopes);
        Assert.Equal(new long[] { 1, 4 }, recovered.Outbox.Envelopes.Select(item => item.Id.SequenceNumber));
    }

    [Fact]
    public async Task An_append_cleared_before_saving_leaves_only_sequence_metadata()
    {
        await using var session = await fixture.NewSessionAsync();
        session.Outbox.Add(new OrderEvent("transient-payload")).Clear();
        var expected = session.Outbox.AsImmutable();

        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("transient-payload", Assert.Single(fixture.Storage.For(session.Id).Writes), StringComparison.Ordinal);
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(expected, recovered.Outbox.AsImmutable());
        Assert.True(recovered.Outbox.IsEmpty);
        Assert.Equal(1, recovered.Outbox.LatestSequenceNumber);
        Assert.Equal(expected.Epoch, recovered.Outbox.Epoch);
        recovered.Outbox.Add(new OrderEvent("next"));
        Assert.Equal(2, Assert.Single(recovered.Outbox.Envelopes).Id.SequenceNumber);
    }

    [Theory]
    [InlineData(false, JournalFailure.BeforeCommit, 1)]
    [InlineData(true, JournalFailure.BeforeCommit, 1)]
    [InlineData(false, JournalFailure.AfterCommit, 2)]
    [InlineData(true, JournalFailure.AfterCommit, 2)]
    public async Task Failed_persistence_fences_the_manager_and_fresh_recovery_uses_durable_storage(
        bool compact, JournalFailure failure, int expectedCount)
    {
        await using var session = await fixture.NewSessionAsync();
        session.Outbox.Add(new OrderEvent("original"));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var storage = fixture.Storage.For(session.Id);
        storage.CompactNext = compact;
        session.Outbox.Add(new OrderEvent("uncertain"));
        var candidate = session.Outbox.AsImmutable();
        storage.NextFailure = failure;

        await Assert.ThrowsAsync<IOException>(() => session.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);

        Assert.Equal(expectedCount, recovered.Outbox.Count);
        Assert.Equal(candidate.Envelopes.Take(expectedCount), recovered.Outbox.Envelopes);
        recovered.Outbox.Add(new OrderEvent("after-recovery"));
        await recovered.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        await recovered.DisposeAsync();
        await using var again = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(expectedCount + 1, again.Outbox.Count);
        Assert.Equal("after-recovery", again.Outbox[^1].Text);
    }
}
