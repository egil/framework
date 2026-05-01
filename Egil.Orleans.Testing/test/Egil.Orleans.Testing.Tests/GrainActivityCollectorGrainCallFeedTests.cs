namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorGrainCallFeedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task SubscribeToGrainCalls_receives_grain_calls()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected = new List<IIncomingGrainCallContext>();

        // Use grain-scoped feed to avoid collecting unrelated system grain calls from the shared cluster
        var feedTask = CollectFeedAsync(fixture.Collector.SubscribeToGrainCalls(grain, cts.Token), collected, count: 1, cts);

        await grain.SetValueAsync("hello");

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), cts);

        Assert.NotEmpty(collected);
        Assert.All(collected, ctx => Assert.Equal(grain.GetGrainId(), ctx.TargetId));
        Assert.Contains(collected, ctx => ctx.MethodName == nameof(ITestStateGrain.SetValueAsync));
    }

    [Fact]
    public async Task SubscribeToGrainCalls_grain_scoped_ignores_unrelated_grains()
    {
        var ct = TestContext.Current.CancellationToken;
        var target = fixture.GetUniqueGrain<ITestStateGrain>();
        var other = fixture.GetUniqueGrain<ITestStateGrain>("other");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collected = new List<IIncomingGrainCallContext>();
        var feedTask = CollectFeedAsync(
            fixture.Collector.SubscribeToGrainCalls(target, cts.Token),
            collected, count: 1, cts);

        // Generate noise from another grain first
        await other.SetValueAsync("noise");

        // Now trigger the target
        await target.SetValueAsync("signal");

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), cts);

        Assert.All(collected, ctx => Assert.Equal(target.GetGrainId(), ctx.TargetId));
    }

    [Fact]
    public async Task SubscribeToGrainCalls_is_future_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        // Trigger a call BEFORE subscribing
        await grain.SetValueAsync("before-subscribe");

        // Deterministically wait for the pre-subscribe call to be observed
        await fixture.Collector.WaitForGrainCallAsync(
            grain,
            ctx => ctx.MethodName == nameof(ITestStateGrain.SetValueAsync),
            ct: ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collected = new List<IIncomingGrainCallContext>();
        var feedTask = CollectFeedAsync(
            fixture.Collector.SubscribeToGrainCalls(grain, cts.Token),
            collected, count: 1, cts);

        // Now trigger a new call
        await grain.GetValueAsync();

        await WaitForCollectedAsync(feedTask, timeout: TimeSpan.FromSeconds(5), cts);

        // Only the post-subscribe call should appear
        Assert.All(collected, ctx =>
        {
            Assert.Equal(grain.GetGrainId(), ctx.TargetId);
            Assert.Equal(nameof(ITestStateGrain.GetValueAsync), ctx.MethodName);
        });
    }

    [Fact]
    public async Task SubscribeToGrainCalls_cancellation_removes_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var collected = new List<IIncomingGrainCallContext>();
        var feedTask = CollectAllAsync(fixture.Collector.SubscribeToGrainCalls(cts.Token), collected);

        // Cancel to stop the feed
        await cts.CancelAsync();

        // The feed task should complete (possibly with OperationCanceledException)
        await IgnoreCancellationAsync(feedTask);

        // After cancellation, trigger a call and deterministically wait for it
        // to be processed by the collector, then verify the old list is unchanged.
        var countAfterCancel = collected.Count;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        await grain.SetValueAsync("after-cancel");
        await fixture.Collector.WaitForGrainCallAsync(
            grain,
            ctx => ctx.MethodName == nameof(ITestStateGrain.SetValueAsync),
            ct: ct);

        Assert.Equal(countAfterCancel, collected.Count);
    }

    private static async Task CollectFeedAsync<T>(
        IAsyncEnumerable<T> feed,
        List<T> target,
        int count,
        CancellationTokenSource cts)
    {
        await foreach (var item in feed)
        {
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
