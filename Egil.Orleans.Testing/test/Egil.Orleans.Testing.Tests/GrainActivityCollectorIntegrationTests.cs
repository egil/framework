namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorAnyActivityTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_with_task_retries_until_grain_state_matches()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            () => GrainActivityCollectorTestAssertions.AssertValueAsync(grain, "ready"),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.SetValueAsync("ready");
        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_task_result_returns_asserted_value()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            async () =>
            {
                var number = await grain.GetNumberAsync();
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
            g => GrainActivityCollectorTestAssertions.AssertValueAsync(g, "ready"),
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

public class GrainActivityCollectorTimeoutTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_throws_timeout_exception_with_last_failure()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var exception = await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() =>
            fixture.Collector.WaitForAssertionAsync(
                () => GrainActivityCollectorTestAssertions.AssertValueAsync(grain, "expected"),
                timeout: TimeSpan.FromMilliseconds(150),
                ct: TestContext.Current.CancellationToken));

        Assert.NotNull(exception.InnerException);
    }
}

internal static class GrainActivityCollectorTestAssertions
{
    public static async Task AssertValueAsync(ITestStateGrain grain, string expectedValue)
    {
        Assert.Equal(expectedValue, await grain.GetValueAsync());
    }
}
