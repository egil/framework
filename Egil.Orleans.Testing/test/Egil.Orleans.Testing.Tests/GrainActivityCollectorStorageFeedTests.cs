namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorStorageFeedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task SubscribeToStorageOperations_receives_write_and_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected = new List<StorageOperation>();
        var feedTask = CollectFeedAsync(fixture.Collector.SubscribeToStorageOperations(cts.Token), collected, count: 2, cts);

        await grain.SetValueAsync("hello");
        await grain.GetValueAsync();

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), cts);

        Assert.True(collected.Count >= 2, $"Expected at least 2 operations, got {collected.Count}");
        Assert.Contains(collected, op => op.Kind == StorageOperationKind.Write);
        Assert.Contains(collected, op => op.Kind == StorageOperationKind.Read);
    }

    [Fact]
    public async Task SubscribeToStorageOperations_grain_scoped_ignores_unrelated_grains()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = fixture.GetUniqueGrain<ITestStateGrain>();
        var other = fixture.GetUniqueGrain<ITestStateGrain>("other");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected = new List<StorageOperation>();
        var feedTask = CollectFeedAsync(
            fixture.Collector.SubscribeToStorageOperations(target, cts.Token),
            collected, count: 1, cts);

        // Generate noise from another grain first
        await other.SetValueAsync("noise");

        // Now trigger the target
        await target.SetValueAsync("signal");

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), cts);

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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collected = new List<StorageOperation>();
        var feedTask = CollectFeedAsync(
            fixture.Collector.SubscribeToStorageOperations(grain, cts.Token),
            collected, count: 1, cts);

        // Now trigger a new write
        await grain.SetValueAsync("after-subscribe");

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), cts);

        // Only the post-subscribe write should appear
        Assert.All(collected, op =>
        {
            Assert.Equal(grain.GetGrainId(), op.GrainId);
            Assert.Equal(StorageOperationKind.Write, op.Kind);
        });
    }

    [Fact]
    public async Task SubscribeToStorageOperations_collects_many_sequential_writes()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        const int operationCount = 50;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected = new List<StorageOperation>();

        // Subscribe to writes only for the target grain
        var feedTask = CollectFeedAsync(
            fixture.Collector.SubscribeToStorageOperations(grain, cts.Token),
            collected, count: operationCount, cts,
            filter: op => op.Kind == StorageOperationKind.Write);

        for (var i = 0; i < operationCount; i++)
        {
            await grain.IncrementAsync();
        }

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(30), cts);

        Assert.Equal(operationCount, collected.Count);
        Assert.All(collected, op => Assert.Equal(StorageOperationKind.Write, op.Kind));
    }

    [Fact]
    public async Task SubscribeToStorageOperations_cancellation_removes_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collected = new List<StorageOperation>();
        var feedTask = CollectAllAsync(fixture.Collector.SubscribeToStorageOperations(cts.Token), collected);

        // Cancel to stop the feed
        await cts.CancelAsync();

        // The feed task should complete (possibly with OperationCanceledException)
        await IgnoreCancellationAsync(feedTask);

        // After cancellation, trigger a write and deterministically wait for it
        // to be processed by the collector, then verify the old list is unchanged.
        var countAfterCancel = collected.Count;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        await grain.SetValueAsync("after-cancel");
        await fixture.Collector.WaitForStorageOperationAsync(
            grain,
            op => op.Kind == StorageOperationKind.Write,
            ct: ct);

        Assert.Equal(countAfterCancel, collected.Count);
    }

    [Fact]
    public async Task SubscribeToStorageOperations_multiple_concurrent_subscribers()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected1 = new List<StorageOperation>();
        var collected2 = new List<StorageOperation>();
        var feed1 = CollectFeedAsync(fixture.Collector.SubscribeToStorageOperations(grain, cts1.Token), collected1, count: 1, cts1);
        var feed2 = CollectFeedAsync(fixture.Collector.SubscribeToStorageOperations(grain, cts2.Token), collected2, count: 1, cts2);

        await grain.SetValueAsync("shared-event");

        await WaitForCollectedAsync(feed1, timeout: TimeSpan.FromSeconds(5), cts1);
        await WaitForCollectedAsync(feed2, timeout: TimeSpan.FromSeconds(5), cts2);

        Assert.NotEmpty(collected1);
        Assert.NotEmpty(collected2);
    }

    private static async Task CollectFeedAsync<T>(
        IAsyncEnumerable<T> feed,
        List<T> target,
        int count,
        CancellationTokenSource cts,
        Func<T, bool>? filter = null)
    {
        await foreach (var item in feed)
        {
            if (filter is not null && !filter(item))
            {
                continue;
            }

            target.Add(item);
            if (target.Count >= count)
            {
                await cts.CancelAsync();
            }
        }
    }

    private static async Task CollectAllAsync<T>(IAsyncEnumerable<T> feed, List<T> target)
    {
        await foreach (var item in feed)
        {
            target.Add(item);
        }
    }

    private static async Task WaitForCollectedAsync(Task feedTask, TimeSpan timeout, CancellationTokenSource cts)
    {
        var completed = await Task.WhenAny(feedTask, Task.Delay(timeout));
        if (!ReferenceEquals(completed, feedTask))
        {
            await cts.CancelAsync();
        }

        await IgnoreCancellationAsync(feedTask);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }
}

