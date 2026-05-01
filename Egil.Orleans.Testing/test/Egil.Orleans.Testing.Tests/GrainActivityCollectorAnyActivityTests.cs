namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorAnyActivityTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_with_task_retries_until_grain_state_matches_when_called_after_action()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        await grain.SetValueAsync("ready");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForAssertionAsync_with_task_result_returns_asserted_value_when_called_after_action()
    {
        var grain = fixture.GetUniqueGrain<ITestStateGrain>();

        await grain.IncrementAsync();

        var result = await fixture.WaitForAssertionAsync(
            async () =>
            {
                var number = await grain.GetNumberAsync();
                Assert.True(number > 0);
                return number;
            },
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(1, result);
    }
}
