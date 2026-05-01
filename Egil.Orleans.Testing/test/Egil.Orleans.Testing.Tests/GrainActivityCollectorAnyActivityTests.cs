namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorAnyActivityTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_with_task_retries_until_grain_state_matches()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.SetValueAsync("ready");
        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_task_result_returns_asserted_value()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();
        var waitTask = fixture.WaitForAssertionAsync(
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
