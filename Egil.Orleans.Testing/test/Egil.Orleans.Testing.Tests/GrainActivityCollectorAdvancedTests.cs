namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorAdvancedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForStorageOperationAsync_matches_storage_write()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        var waitTask = fixture.Collector.WaitForStorageOperationAsync(
            operation => operation.Kind == StorageOperationKind.Write && operation.StateName == "state",
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.SetValueAsync("written");
        await waitTask;
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_grain_scope_ignores_unrelated_grains()
    {
        var targetGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>("other");

        var waitTask = fixture.Collector.WaitForStorageOperationAsync(
            targetGrain,
            operation => operation.Kind == StorageOperationKind.Write,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await otherGrain.SetValueAsync("noise");
        Assert.False(waitTask.IsCompleted);

        await targetGrain.SetValueAsync("hit");
        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_matches_grain_call()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            context => context.MethodName == nameof(ITestStateGrain.SetValueAsync),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.SetValueAsync("called");
        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_grain_scope_ignores_unrelated_grains()
    {
        var targetGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>("other");

        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            targetGrain,
            context => context.MethodName == nameof(ITestStateGrain.SetValueAsync),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await otherGrain.SetValueAsync("noise");
        Assert.False(waitTask.IsCompleted);

        await targetGrain.SetValueAsync("hit");
        await waitTask;
    }
}
