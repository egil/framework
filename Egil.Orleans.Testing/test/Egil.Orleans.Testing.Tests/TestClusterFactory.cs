using Orleans.TestingHost;

namespace Egil.Orleans.Testing.Tests;

internal static class TestClusterFactory
{
    public static async Task<InProcessTestCluster> DeployAsync(
        GrainActivityCollector collector,
        bool collectStorageActivity = true,
        Action<InProcessTestClusterBuilder>? configureClusterBuilder = null,
        Action<ISiloBuilder>? configureSiloBuilder = null,
        Action<IClientBuilder>? configureClientBuilder = null)
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        configureClusterBuilder?.Invoke(builder);

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

            configureSiloBuilder?.Invoke(siloBuilder);
        });

        builder.ConfigureClient(clientBuilder =>
        {
            clientBuilder.AddMemoryStreams(ActivityFeatureTestConstants.StreamProviderName);
            configureClientBuilder?.Invoke(clientBuilder);
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();
        return cluster;
    }
}
