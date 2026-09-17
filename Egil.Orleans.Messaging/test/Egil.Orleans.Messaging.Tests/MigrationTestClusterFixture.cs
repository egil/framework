using Orleans.TestingHost;

namespace Egil.Orleans.Messaging.Tests;

// Migration needs somewhere to migrate to. The shared MessagingTestClusterFixture runs a
// single silo and carries the stream, reminder and storage-activity wiring those tests
// depend on, so this keeps a second, deliberately bare cluster instead of widening it.
public sealed class MigrationTestClusterFixture : IAsyncLifetime
{
    private InProcessTestCluster? cluster;

    public InProcessTestCluster Cluster => cluster ?? throw new InvalidOperationException("Test cluster not initialized.");

    public IGrainFactory GrainFactory => Cluster.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 2);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.ConfigureServices(services => services.AddDefaultStateManager("Default"));
        });

        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    /// <summary>
    /// Returns a silo other than <paramref name="siloAddress"/>, so a migration has a
    /// destination chosen by the test rather than by placement.
    /// </summary>
    public SiloAddress OtherSiloThan(string siloAddress) => Cluster.Silos
        .Select(silo => silo.SiloAddress)
        .First(address => !string.Equals(address.ToString(), siloAddress, StringComparison.Ordinal));
}
