namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorTimerWithoutStorageObserverTests
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_grain_timer_without_storage_observer()
    {
        var collector = new GrainActivityCollector();
        await using var cluster = await TestClusterFactory.DeployAsync(collector, collectStorageActivity: false);
        var grain = cluster.Client.GetGrain<ITimerActivityGrain>(Guid.NewGuid().ToString("N"));

        var waitTask = collector.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await grain.StartTimerAsync("ready");
        await waitTask;
    }
}
