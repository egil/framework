namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorExplicitStreamTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_explicit_stream_delivery()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IExplicitStreamActivityGrain>(grainKey);
        await grain.SubscribeAsync();

        var streamProvider = fixture.Cluster.Client.GetStreamProvider(ActivityFeatureTestConstants.StreamProviderName);
        var stream = streamProvider.GetStream<string>(StreamId.Create(ActivityFeatureTestConstants.ExplicitStreamNamespace, grainKey));
        var waitTask = fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            ct: TestContext.Current.CancellationToken);

        await stream.OnNextAsync("ready");
        await waitTask;
    }
}
