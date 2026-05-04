namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorOneWayTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task GetGrainCallsAsync_matches_oneway_method()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<IOneWayActivityGrain>();

        var collectTask = fixture.Collector
            .GetGrainCallsAsync(grain, cancellationToken: ct)
            .Where(ctx => ctx.MethodName == nameof(IOneWayActivityGrain.NotifyAsync))
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await grain.NotifyAsync("ready");

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Single(collected);
    }

    [Fact]
    public async Task WaitForAssertionAsync_observes_oneway_state_change_when_called_after_action()
    {
        var grain = fixture.GetUniqueGrain<IOneWayActivityGrain>();

        await grain.NotifyAsync("ready");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);
    }
}
