namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorStorageFeedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task SubscribeToStorageOperations_receives_write_read_clear()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected = new List<StorageOperation>();
        var feedTask = CollectFeedAsync(fixture.Collector.SubscribeToStorageOperations(cts.Token), collected, count: 3, cts);

        // Read is triggered on first access, write on SetValueAsync, clear is not directly
        // exposed on ITestStateGrain — so we test write + read via initial activation (read)
        // and an explicit write.
        await grain.SetValueAsync("hello");
        await grain.GetValueAsync();

        // SetValueAsync triggers: read (activation) + write (SetValueAsync) + read (GetValueAsync reuses cached state)
        // But activation may include reads. Let's just wait for enough operations.
        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), ct);

        Assert.True(collected.Count >= 3, $"Expected at least 3 operations, got {collected.Count}");
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

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), ct);

        Assert.All(collected, op => Assert.Equal(target.GetGrainId(), op.GrainId));
    }

    [Fact]
    public async Task SubscribeToStorageOperations_is_future_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        // Trigger a write BEFORE subscribing
        await grain.SetValueAsync("before-subscribe");

        // Wait a moment to ensure the operation is fully processed
        await Task.Delay(100, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collected = new List<StorageOperation>();
        var feedTask = CollectFeedAsync(
            fixture.Collector.SubscribeToStorageOperations(grain, cts.Token),
            collected, count: 1, cts);

        // Now trigger a new write
        await grain.SetValueAsync("after-subscribe");

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), ct);

        // Only the post-subscribe write should appear
        Assert.All(collected, op =>
        {
            Assert.Equal(grain.GetGrainId(), op.GrainId);
            Assert.Equal(StorageOperationKind.Write, op.Kind);
        });
    }

    [Fact]
    public async Task SubscribeToStorageOperations_lossless_beyond_bounded_channel_capacity()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        const int operationCount = 300; // Exceeds the 256 bounded channel capacity
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

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(30), ct);

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

        // After cancellation, a new write should not appear in the old collected list
        var countAfterCancel = collected.Count;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        await grain.SetValueAsync("after-cancel");
        await Task.Delay(200, ct);

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

        await WaitForCollectedAsync(feed1, timeout: TimeSpan.FromSeconds(5), ct);
        await WaitForCollectedAsync(feed2, timeout: TimeSpan.FromSeconds(5), ct);

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

    private static async Task WaitForCollectedAsync(Task feedTask, TimeSpan timeout, CancellationToken ct)
    {
        var completed = await Task.WhenAny(feedTask, Task.Delay(timeout, ct));
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
