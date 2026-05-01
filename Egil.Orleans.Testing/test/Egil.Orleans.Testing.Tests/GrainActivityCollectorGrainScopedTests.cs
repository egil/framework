namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorGrainScopedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_grain_scope_ignores_unrelated_grain_activity()
    {
        var targetGrain = fixture.GetUniqueGrain<ITestStateGrain>();
        var otherGrain = fixture.GetUniqueGrain<ITestStateGrain>("other");
        var attempts = 0;
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            targetGrain,
            async () =>
            {
                attempts++;
                Assert.Equal("ready", await targetGrain.GetValueAsync());
            },
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await otherGrain.SetValueAsync("noise");
        Assert.False(waitTask.IsCompleted);
        await targetGrain.SetValueAsync("ready");
        await waitTask;

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_passes_grain_to_task_lambda()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            grain,
            async g => Assert.Equal("ready", await g.GetValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.SetValueAsync("ready");
        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_passes_grain_to_result_lambda()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            grain,
            async g =>
            {
                var number = await g.GetNumberAsync();
                Assert.True(number > 0);
                return number;
            },
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.IncrementAsync();
        var result = await waitTask;

        Assert.Equal(1, result);
    }
}
