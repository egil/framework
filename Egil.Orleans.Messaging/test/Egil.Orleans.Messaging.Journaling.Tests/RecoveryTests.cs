namespace Egil.Orleans.Messaging.Journaling.Tests;

public sealed class RecoveryTests(JournalingPrototypeFixture fixture) : IClassFixture<JournalingPrototypeFixture>
{
    [Fact]
    public async Task Initial_replay_retry_resets_the_same_components_and_preserves_pending_baselines()
    {
        await using var saved = await fixture.NewSessionAsync();
        saved.Outbox.Add(new OrderEvent("saved"));
        var token = new OutboxSequenceToken(1, GrainId.Create("sender", "retry"), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        saved.Tracker.TryAcceptMessage(token);
        await saved.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        var expectedOutbox = saved.Outbox.AsImmutable();
        var expectedTracker = saved.Tracker.AsImmutable();
        await saved.DisposeAsync();
        var storage = fixture.Storage.For(saved.Id);
        storage.FailNextReadAfterReplay = true;
        await using var retry = fixture.CreateSession(saved.Id);

        await Assert.ThrowsAsync<IOException>(() => retry.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
        await using var gate = storage.PauseNextRead();
        var firstRetry = retry.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        gate.Observe(firstRetry);
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var secondRetry = retry.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.False(secondRetry.IsCompleted);
        gate.Release();
        await Task.WhenAll(firstRetry, secondRetry).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(retry.Manager.TryGetStateMachine("outbox", out var outbox));
        Assert.Same(retry.Outbox, outbox);
        Assert.True(retry.Manager.TryGetStateMachine("tracker", out var tracker));
        Assert.Same(retry.Tracker, tracker);
        Assert.Equal(expectedOutbox, retry.Outbox.AsImmutable());
        Assert.Equal(expectedTracker, retry.Tracker.AsImmutable());
        Assert.False(retry.Tracker.TryAcceptMessage(token));
        retry.Outbox.Add(new OrderEvent("after-retry"));
        await retry.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("saved", storage.Writes[^1], StringComparison.Ordinal);
        await retry.DisposeAsync();
        await using var again = await fixture.NewSessionAsync(saved.Id);
        Assert.Equal(new[] { "saved", "after-retry" }, again.Outbox.Select(item => item.Text));
    }

    [Theory]
    [InlineData(JournalFailure.BeforeCommit, 1)]
    [InlineData(JournalFailure.AfterCommit, 0)]
    public async Task Failed_delete_fences_messaging_state_and_fresh_recovery_observes_storage(JournalFailure failure, int count)
    {
        await using var session = await fixture.NewSessionAsync();
        session.Outbox.Add(new OrderEvent("saved"));
        await session.Manager.WriteStateAsync(TestContext.Current.CancellationToken);
        fixture.Storage.For(session.Id).NextFailure = failure;

        await Assert.ThrowsAsync<IOException>(() => session.Manager.DeleteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Manager.WriteStateAsync(TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Manager.InitializeAsync(TestContext.Current.CancellationToken).AsTask());
        await session.DisposeAsync();
        await using var recovered = await fixture.NewSessionAsync(session.Id);
        Assert.Equal(count, recovered.Outbox.Count);
    }
}
