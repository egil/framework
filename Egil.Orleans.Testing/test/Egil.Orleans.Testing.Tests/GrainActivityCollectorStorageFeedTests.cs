namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorStorageFeedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task SubscribeToStorageOperations_receives_write_and_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        var collectTask = fixture.Collector
            .SubscribeToStorageOperations(grain, ct)
            .Take(2)
            .ToListAsync(ct)
            .AsTask();

        await grain.SetValueAsync("hello");
        await grain.GetValueAsync();

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Contains(collected, op => op.Kind == StorageOperationKind.Write);
        Assert.Contains(collected, op => op.Kind == StorageOperationKind.Read);
    }

    [Fact]
    public async Task SubscribeToStorageOperations_grain_scoped_ignores_unrelated_grains()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = fixture.GetUniqueGrain<ITestStateGrain>();
        var other = fixture.GetUniqueGrain<ITestStateGrain>("other");

        var collectTask = fixture.Collector
            .SubscribeToStorageOperations(target, ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        // Generate noise from another grain first
        await other.SetValueAsync("noise");

        // Now trigger the target
        await target.SetValueAsync("signal");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.All(collected, op => Assert.Equal(target.GetGrainId(), op.GrainId));
    }

    [Fact]
    public async Task SubscribeToStorageOperations_is_future_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        // Trigger a write BEFORE subscribing
        await grain.SetValueAsync("before-subscribe");

        // Deterministically wait for that write to be observed by the collector
        await fixture.Collector.WaitForStorageOperationAsync(
            grain,
            op => op.Kind == StorageOperationKind.Write,
            ct: ct);

        var collectTask = fixture.Collector
            .SubscribeToStorageOperations(grain, ct)
            .Where(op => op.Kind == StorageOperationKind.Write)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        // Now trigger a new write
        await grain.SetValueAsync("after-subscribe");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        // Only the post-subscribe write should appear
        Assert.Single(collected);
        Assert.Equal(grain.GetGrainId(), collected[0].GrainId);
        Assert.Equal(StorageOperationKind.Write, collected[0].Kind);
    }

    [Fact]
    public async Task SubscribeToStorageOperations_collects_many_sequential_writes()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        const int operationCount = 50;

        var collectTask = fixture.Collector
            .SubscribeToStorageOperations(grain, ct)
            .Where(op => op.Kind == StorageOperationKind.Write)
            .Take(operationCount)
            .ToListAsync(ct)
            .AsTask();

        for (var i = 0; i < operationCount; i++)
        {
            await grain.IncrementAsync();
        }

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(30), ct);

        Assert.Equal(operationCount, collected.Count);
        Assert.All(collected, op => Assert.Equal(StorageOperationKind.Write, op.Kind));
    }

    [Fact]
    public async Task SubscribeToStorageOperations_cancellation_removes_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collectTask = fixture.Collector
            .SubscribeToStorageOperations(cts.Token)
            .ToListAsync(ct)
            .AsTask();

        await cts.CancelAsync();

        // Feed completes promptly after cancellation; the finally block removes the subscription.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct));
    }

    [Fact]
    public async Task SubscribeToStorageOperations_multiple_concurrent_subscribers()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        var feed1 = fixture.Collector
            .SubscribeToStorageOperations(grain, ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        var feed2 = fixture.Collector
            .SubscribeToStorageOperations(grain, ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await grain.SetValueAsync("shared-event");

        var collected1 = await feed1.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var collected2 = await feed2.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.NotEmpty(collected1);
        Assert.NotEmpty(collected2);
    }
}

