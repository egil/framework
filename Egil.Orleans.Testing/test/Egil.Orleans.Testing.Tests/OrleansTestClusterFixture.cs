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
public class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
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

    /// <summary>
    /// Controls whether the shared fixture observes writes to the default grain storage provider.
    /// </summary>
    protected virtual bool CollectStorageActivityFromDefault => true;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        cluster = await TestClusterFactory.DeployAsync(
            Collector,
            collectStorageActivity: CollectStorageActivityFromDefault,
            configureClusterBuilder: ConfigureClusterBuilder,
            configureSiloBuilder: ConfigureSiloBuilder,
            configureClientBuilder: ConfigureClientBuilder);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();

        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, ct);

    /// <summary>
    /// Creates a unique string key for test resources that must share an identifier with a grain.
    /// </summary>
    /// <param name="suffix">An optional suffix that differentiates multiple keys requested by the same test method.</param>
    /// <param name="memberName">The calling test method name.</param>
    /// <returns>A unique string key.</returns>
    public string CreateUniqueStringKey(string? suffix = null, [CallerMemberName] string memberName = "")
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
        => GrainFactory.GetGrain<TGrain>(CreateUniqueStringKey(suffix, memberName));

    /// <summary>
    /// Allows derived fixtures to customize the cluster builder before common registrations run.
    /// </summary>
    protected virtual void ConfigureClusterBuilder(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// Allows derived fixtures to append feature-specific silo registrations.
    /// </summary>
    protected virtual void ConfigureSiloBuilder(ISiloBuilder siloBuilder)
    {
    }

    /// <summary>
    /// Allows derived fixtures to append feature-specific client registrations.
    /// </summary>
    protected virtual void ConfigureClientBuilder(IClientBuilder clientBuilder)
    {
    }

    /// <summary>
    /// Allows derived fixtures to dispose reminder clocks or similar resources before the cluster shuts down.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
