namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorTimerTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_grain_timer_state_change()
    {
        var grain = fixture.GetUniqueGrain<ITimerActivityGrain>();

        await grain.StartTimerAsync("ready");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);
    }
}
