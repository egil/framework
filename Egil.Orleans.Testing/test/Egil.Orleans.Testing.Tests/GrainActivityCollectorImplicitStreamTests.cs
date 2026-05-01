namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorImplicitStreamTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_implicit_stream_delivery()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IImplicitStreamActivityGrain>(grainKey);

        var streamProvider = fixture.Cluster.Client.GetStreamProvider(ActivityFeatureTestConstants.StreamProviderName);
        var stream = streamProvider.GetStream<string>(StreamId.Create(ActivityFeatureTestConstants.ImplicitStreamNamespace, grainKey));
        var waitTask = fixture.Collector.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);

        await stream.OnNextAsync("ready");
        await waitTask;
    }
}
