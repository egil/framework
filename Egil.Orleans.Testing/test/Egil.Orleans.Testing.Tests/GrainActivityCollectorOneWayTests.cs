namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorOneWayTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForGrainCallAsync_matches_oneway_method()
    {
        var grain = fixture.GetUniqueGrain<IOneWayActivityGrain>();
        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            grain,
            context => context.MethodName == nameof(IOneWayActivityGrain.NotifyAsync),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.NotifyAsync("ready");
        await waitTask;
    }

    [Fact]
    public async Task WaitForAssertionAsync_observes_oneway_state_change()
    {
        var grain = fixture.GetUniqueGrain<IOneWayActivityGrain>();
        var waitTask = fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.NotifyAsync("ready");
        await waitTask;
    }
}
