namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorAdvancedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task GetStorageOperationsAsync_matches_storage_write()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        await grain.SetValueAsync("written");

        var operation = await fixture.Collector
            .GetStorageOperationsAsync(includeExisting: true, cancellationToken: ct)
            .Where(op => op.Kind == StorageOperationKind.Write && op.StateName == "state")
            .Take(1)
            .FirstAsync(ct);

        Assert.Equal(StorageOperationKind.Write, operation.Kind);
    }

    [Fact]
    public async Task GetStorageOperationsAsync_grain_scope_ignores_unrelated_grains()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>("other");

        var collectTask = fixture.Collector
            .GetStorageOperationsAsync(targetGrain, cancellationToken: ct)
            .Where(op => op.Kind == StorageOperationKind.Write)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await otherGrain.SetValueAsync("noise");

        Assert.False(collectTask.IsCompleted);

        await targetGrain.SetValueAsync("hit");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Single(collected);
        Assert.Equal(targetGrain.GetGrainId(), collected[0].GrainId);
    }

    [Fact]
    public async Task GetGrainCallsAsync_matches_grain_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        await grain.SetValueAsync("called");

        var call = await fixture.Collector
            .GetGrainCallsAsync(grain, includeExisting: true, cancellationToken: ct)
            .Where(ctx => ctx.MethodName == nameof(ITestStateGrain.SetValueAsync))
            .Take(1)
            .FirstAsync(ct);

        Assert.Equal(nameof(ITestStateGrain.SetValueAsync), call.MethodName);
    }

    [Fact]
    public async Task GetGrainCallsAsync_grain_scope_ignores_unrelated_grains()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>("other");

        var collectTask = fixture.Collector
            .GetGrainCallsAsync(targetGrain, cancellationToken: ct)
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await otherGrain.SetValueAsync("noise");

        Assert.False(collectTask.IsCompleted);

        await targetGrain.SetValueAsync("hit");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Single(collected);
        Assert.Equal(targetGrain.GetGrainId(), collected[0].TargetId);
    }
}
