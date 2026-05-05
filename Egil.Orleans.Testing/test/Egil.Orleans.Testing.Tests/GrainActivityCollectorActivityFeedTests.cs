namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorActivityFeedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task GetGrainActivityAsync_receives_grain_call_and_storage_activity()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var grainId = grain.GetGrainId();

        // Subscribe to the unified feed filtered to our grain.
        // A single SetValueAsync produces: StorageRead (activation) + StorageWrite + GrainCall.
        var collectTask = fixture.Collector
            .GetGrainActivityAsync(cancellationToken: ct)
            .Where(a => a.GrainId == grainId)
            .Take(3)
            .ToListAsync(ct)
            .AsTask();

        await grain.SetValueAsync("mixed-test");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Equal(3, collected.Count);
        Assert.Contains(collected, a => a.IsGrainCall);
        Assert.Contains(collected, a => a.IsStorageActivity);
    }

    [Fact]
    public async Task GetGrainActivityAsync_includeExisting_replays_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var grainId = grain.GetGrainId();

        // Generate activity BEFORE subscribing
        await grain.SetValueAsync("history-test");

        // Wait for the activity to land in the collector's history
        await fixture.Collector
            .GetGrainActivityAsync(includeExisting: true, cancellationToken: ct)
            .Where(a => a.GrainId == grainId && a.IsStorageActivity)
            .Take(1)
            .FirstAsync(ct);

        // Now subscribe with includeExisting — should replay the history
        var collected = await fixture.Collector
            .GetGrainActivityAsync(includeExisting: true, cancellationToken: ct)
            .Where(a => a.GrainId == grainId)
            .Take(1)
            .ToListAsync(ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.NotEmpty(collected);
        Assert.Equal(grainId, collected[0].GrainId);
    }

    [Fact]
    public async Task GetGrainActivityAsync_populates_StorageOperation_and_GrainCallContext()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var grainId = grain.GetGrainId();

        // StorageRead (activation) + StorageWrite + GrainCall = 3 events
        var collectTask = fixture.Collector
            .GetGrainActivityAsync(cancellationToken: ct)
            .Where(a => a.GrainId == grainId)
            .Take(3)
            .ToListAsync(ct)
            .AsTask();

        await grain.SetValueAsync("props-test");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        var grainCall = collected.First(a => a.IsGrainCall);
        Assert.NotNull(grainCall.GrainCallContext);
        Assert.Null(grainCall.StorageOperation);
        Assert.Equal(GrainActivityKind.GrainCall, grainCall.Kind);

        var storageWrite = collected.First(a => a.IsStorageActivity && a.Kind == GrainActivityKind.StorageWrite);
        Assert.NotNull(storageWrite.StorageOperation);
        Assert.Null(storageWrite.GrainCallContext);
        Assert.Equal(StorageOperationKind.Write, storageWrite.StorageOperation!.Value.Kind);
    }

    [Fact]
    public async Task GetGrainActivityAsync_future_only_does_not_replay_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var grainId = grain.GetGrainId();

        // Generate activity BEFORE subscribing
        await grain.SetValueAsync("pre-subscribe");

        // Wait for it to be observed
        await fixture.Collector
            .GetGrainActivityAsync(includeExisting: true, cancellationToken: ct)
            .Where(a => a.GrainId == grainId && a.IsStorageActivity)
            .Take(1)
            .FirstAsync(ct);

        // Subscribe future-only
        var collectTask = fixture.Collector
            .GetGrainActivityAsync(cancellationToken: ct)
            .Where(a => a.GrainId == grainId)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        // Trigger new activity
        await grain.SetValueAsync("post-subscribe");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Single(collected);
        Assert.Equal(grainId, collected[0].GrainId);
    }

    [Fact]
    public async Task GetGrainActivityAsync_cancellation_removes_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collectTask = fixture.Collector
            .GetGrainActivityAsync(cancellationToken: cts.Token)
            .ToListAsync(ct)
            .AsTask();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct));
    }
}
