namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorGrainCallFeedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task SubscribeToGrainCalls_receives_grain_calls()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        // Use grain-scoped feed to avoid collecting unrelated system grain calls from the shared cluster
        var collectTask = fixture.Collector
            .SubscribeToGrainCalls(grain, ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await grain.SetValueAsync("hello");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

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

        var collectTask = fixture.Collector
            .SubscribeToGrainCalls(target, ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        // Generate noise from another grain first
        await other.SetValueAsync("noise");

        // Now trigger the target
        await target.SetValueAsync("signal");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

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

        var collectTask = fixture.Collector
            .SubscribeToGrainCalls(grain, ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        // Now trigger a new call
        await grain.GetValueAsync();

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        // Only the post-subscribe call should appear
        Assert.Single(collected);
        Assert.Equal(grain.GetGrainId(), collected[0].TargetId);
        Assert.Equal(nameof(ITestStateGrain.GetValueAsync), collected[0].MethodName);
    }

    [Fact]
    public async Task SubscribeToGrainCalls_global_receives_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var grainId = grain.GetGrainId();

        // Use global overload, but filter to our grain to avoid cross-test noise
        var collectTask = fixture.Collector
            .SubscribeToGrainCalls(ct)
            .Where(ctx => ctx.TargetId == grainId)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await grain.SetValueAsync("global-test");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);

        Assert.Single(collected);
        Assert.Equal(grainId, collected[0].TargetId);
    }

    [Fact]
    public async Task SubscribeToGrainCalls_cancellation_removes_subscription()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var collectTask = fixture.Collector
            .SubscribeToGrainCalls(cts.Token)
            .ToListAsync(ct)
            .AsTask();

        await cts.CancelAsync();

        // Feed completes promptly after cancellation; the finally block removes the subscription.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct));
    }
}
