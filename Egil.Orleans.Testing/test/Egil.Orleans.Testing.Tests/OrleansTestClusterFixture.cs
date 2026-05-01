using System.Runtime.CompilerServices;
using Orleans.TestingHost;

[assembly: AssemblyFixture(typeof(Egil.Orleans.Testing.Tests.OrleansTestClusterFixture))]

namespace Egil.Orleans.Testing.Tests;

/// <summary>
/// Shared Orleans test cluster for the entire test assembly.
/// </summary>
/// <remarks>
/// Assembly fixtures do not disable xUnit parallelization, so tests must use unique grain keys
/// and avoid sharing mutable grain state.
/// </remarks>
public sealed class OrleansTestClusterFixture : IAsyncLifetime
{
    private InProcessTestCluster? cluster;

    /// <summary>
    /// Gets the activity collector under test.
    /// </summary>
    public GrainActivityCollector Collector { get; } = new();

    /// <summary>
    /// Gets the deployed test cluster.
    /// </summary>
    public InProcessTestCluster Cluster => cluster ?? throw new InvalidOperationException("Test cluster not initialized.");

    /// <summary>
    /// Gets the cluster client grain factory.
    /// </summary>
    public IGrainFactory GrainFactory => Cluster.Client;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((options, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");

            siloBuilder.AddGrainActivityCollector(Collector)
                .CollectStorageActivityFrom("Default");
        });

        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a grain key unique to the current test execution.
    /// </summary>
    /// <param name="suffix">An optional suffix that differentiates multiple keys created by the same test method.</param>
    /// <param name="memberName">The calling test method name.</param>
    /// <returns>A unique grain key.</returns>
    public string CreateUniqueKey(string? suffix = null, [CallerMemberName] string memberName = "")
        => suffix is null
            ? $"{memberName}-{Guid.NewGuid():N}"
            : $"{memberName}-{suffix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Gets a grain with a unique string key for the current test execution.
    /// </summary>
    /// <typeparam name="TGrain">The grain interface type.</typeparam>
    /// <param name="suffix">An optional suffix that differentiates multiple grains requested by the same test method.</param>
    /// <param name="memberName">The calling test method name.</param>
    /// <returns>A grain reference with a unique string key.</returns>
    public TGrain GetUniqueGrain<TGrain>(string? suffix = null, [CallerMemberName] string memberName = "")
        where TGrain : IGrainWithStringKey
        => GrainFactory.GetGrain<TGrain>(CreateUniqueKey(suffix, memberName));
}
