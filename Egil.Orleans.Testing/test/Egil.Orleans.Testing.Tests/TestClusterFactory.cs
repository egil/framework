using Orleans.TestingHost;

namespace Egil.Orleans.Testing.Tests;

internal static class TestClusterFactory
{
    public static async Task<InProcessTestCluster> DeployAsync(GrainActivityCollector collector, bool collectStorageActivity = true)
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((options, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(ActivityFeatureTestConstants.StreamProviderName);
            siloBuilder.UseInMemoryReminderService();

            var collectorBuilder = siloBuilder.AddGrainActivityCollector(collector);
            if (collectStorageActivity)
            {
                collectorBuilder.CollectStorageActivityFrom("Default");
            }
        });

        builder.ConfigureClient(clientBuilder => clientBuilder.AddMemoryStreams(ActivityFeatureTestConstants.StreamProviderName));

        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
