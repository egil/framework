namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorOverloadTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_with_grain_scoped_task_result_retries_until_grain_state_matches()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            grain,
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

    [Fact]
    public async Task WaitForAssertionAsync_grain_scope_timeout_includes_grain_context()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        var exception = await Assert.ThrowsAsync<WaitForAssertionTimeoutException>(() =>
            fixture.Collector.WaitForAssertionAsync(
                grain,
                async () => Assert.Equal("ready", await grain.GetValueAsync()),
                timeout: TimeSpan.FromMilliseconds(150),
                ct: TestContext.Current.CancellationToken));

        Assert.Equal(grain.GetGrainId(), exception.GrainId);
        Assert.NotNull(exception.Elapsed);
        Assert.Contains(grain.GetGrainId().ToString(), exception.Message);
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_task_throws_for_null_assertion()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() => collector.WaitForAssertionAsync(
            (Func<Task>)null!,
            ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_task_result_throws_for_null_assertion()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() => collector.WaitForAssertionAsync(
            (Func<Task<int>>)null!,
            ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_scope_task_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync<ITestStateGrain>(
                null!,
                static () => Task.CompletedTask,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_scope_task_result_throws_for_null_grain()
    {
        var collector = new GrainActivityCollector();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync<ITestStateGrain, int>(
                null!,
                static () => Task.FromResult(1),
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_task_throws_for_null_assertion()
    {
        var collector = new GrainActivityCollector();
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync(
                grain,
                (Func<ITestStateGrain, Task>)null!,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_grain_parameter_task_result_throws_for_null_assertion()
    {
        var collector = new GrainActivityCollector();
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            collector.WaitForAssertionAsync(
                grain,
                (Func<ITestStateGrain, Task<int>>)null!,
                ct: TestContext.Current.CancellationToken));
    }
}
