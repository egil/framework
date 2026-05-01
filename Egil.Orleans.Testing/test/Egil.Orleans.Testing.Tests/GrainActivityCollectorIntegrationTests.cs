namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorAnyActivityTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_with_task_retries_until_grain_state_matches()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_with_task_retries_until_grain_state_matches)));
        Task Assertion() => GrainActivityCollectorTestAssertions.AssertValueAsync(grain, "ready");

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.SetValueAsync("ready");
        }, TestContext.Current.CancellationToken);

        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_valuetask_retries_until_grain_state_matches()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_with_valuetask_retries_until_grain_state_matches)));
        async ValueTask Assertion()
        {
            Assert.Equal("ready", await grain.GetValueAsync());
        }

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            assertion: Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.SetValueAsync("ready");
        }, TestContext.Current.CancellationToken);

        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_task_result_returns_asserted_value()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_with_task_result_returns_asserted_value)));
        async Task<int> Assertion()
        {
            var number = await grain.GetNumberAsync();
            Assert.True(number > 0);
            return number;
        }

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.IncrementAsync();
        }, TestContext.Current.CancellationToken);

        var result = await waitTask;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_valuetask_result_returns_asserted_value()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_with_valuetask_result_returns_asserted_value)));
        async ValueTask<string?> Assertion()
        {
            var value = await grain.GetValueAsync();
            Assert.Equal("done", value);
            return value;
        }

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            assertion: Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.SetValueAsync("done");
        }, TestContext.Current.CancellationToken);

        var result = await waitTask;

        Assert.Equal("done", result);
    }
}

public class GrainActivityCollectorGrainScopedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_grain_scope_ignores_unrelated_grain_activity()
    {
        var targetGrain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_grain_scope_ignores_unrelated_grain_activity)));
        var otherGrain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey($"{nameof(WaitForAssertionAsync_grain_scope_ignores_unrelated_grain_activity)}-other"));
        var attempts = 0;
        async Task Assertion()
        {
            attempts++;
            Assert.Equal("ready", await targetGrain.GetValueAsync());
        }

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            targetGrain,
            Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            await otherGrain.SetValueAsync("noise");
            await Task.Delay(50, TestContext.Current.CancellationToken);
            await targetGrain.SetValueAsync("ready");
        }, TestContext.Current.CancellationToken);

        await waitTask;

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_passes_grain_to_task_lambda()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_with_grain_parameter_passes_grain_to_task_lambda)));
        Task Assertion(ITestStateGrain g) => GrainActivityCollectorTestAssertions.AssertValueAsync(g, "ready");

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            grain,
            Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.SetValueAsync("ready");
        }, TestContext.Current.CancellationToken);

        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_passes_grain_to_result_lambda()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_with_grain_parameter_passes_grain_to_result_lambda)));
        async Task<int> Assertion(ITestStateGrain g)
        {
            var number = await g.GetNumberAsync();
            Assert.True(number > 0);
            return number;
        }

        var waitTask = fixture.Collector.WaitForAssertionAsync(
            grain,
            Assertion,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.IncrementAsync();
        }, TestContext.Current.CancellationToken);

        var result = await waitTask;

        Assert.Equal(1, result);
    }
}

public class GrainActivityCollectorAdvancedTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForStorageOperationAsync_matches_storage_write()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForStorageOperationAsync_matches_storage_write)));

        var waitTask = fixture.Collector.WaitForStorageOperationAsync(
            operation => operation.Kind == StorageOperationKind.Write && operation.StateName == "state",
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.SetValueAsync("written");
        }, TestContext.Current.CancellationToken);

        await waitTask;
    }

    [Fact]
    public async Task WaitForStorageOperationAsync_grain_scope_ignores_unrelated_grains()
    {
        var targetGrain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForStorageOperationAsync_grain_scope_ignores_unrelated_grains)));
        var otherGrain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey($"{nameof(WaitForStorageOperationAsync_grain_scope_ignores_unrelated_grains)}-other"));

        var waitTask = fixture.Collector.WaitForStorageOperationAsync(
            targetGrain,
            operation => operation.Kind == StorageOperationKind.Write,
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await otherGrain.SetValueAsync("noise");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        await targetGrain.SetValueAsync("hit");
        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_matches_grain_call()
    {
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForGrainCallAsync_matches_grain_call)));

        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            context => context.MethodName == nameof(ITestStateGrain.SetValueAsync),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        _ = Task.Run(async () =>
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await grain.SetValueAsync("called");
        }, TestContext.Current.CancellationToken);

        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_grain_scope_ignores_unrelated_grains()
    {
        var targetGrain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForGrainCallAsync_grain_scope_ignores_unrelated_grains)));
        var otherGrain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey($"{nameof(WaitForGrainCallAsync_grain_scope_ignores_unrelated_grains)}-other"));

        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            targetGrain,
            context => context.MethodName == nameof(ITestStateGrain.SetValueAsync),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await otherGrain.SetValueAsync("noise");
        await Task.Delay(100, TestContext.Current.CancellationToken);
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
        var grain = fixture.GrainFactory.GetGrain<ITestStateGrain>(fixture.CreateUniqueKey(nameof(WaitForAssertionAsync_throws_timeout_exception_with_last_failure)));
        Task Assertion() => GrainActivityCollectorTestAssertions.AssertValueAsync(grain, "expected");

        var exception = await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() =>
            fixture.Collector.WaitForAssertionAsync(
                Assertion,
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
