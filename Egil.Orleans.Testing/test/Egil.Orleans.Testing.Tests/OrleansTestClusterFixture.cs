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
    public IGrainFactory GrainFactory => cluster?.Client ?? throw new InvalidOperationException("Test cluster not initialized.");

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

    // Create a unique GrainId when a test needs to share the same identity across
    // multiple Orleans concepts such as a grain reference and a stream id.
    public GrainId CreateUniqueGrainId<TGrain>([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName).GetGrainId();

    // Get a grain reference with a test-unique key so parallel tests do not collide.
    // The helper chooses the correct Orleans key shape based on the grain interface type.
    public TGrain GetUniqueGrain<TGrain>([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName);

    /// <summary>
    /// Gets a second grain of the same interface type within a single test method.
    /// </summary>
    public TGrain GetUniqueGrain<TGrain>(string suffix, [CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName, suffix);

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

    private TGrain CreateUniqueGrainReference<TGrain>(string memberName, string? suffix = null)
        where TGrain : IGrain
    {
        var grainType = typeof(TGrain);
        var grainName = grainType.Name;
        var keyPrefix = suffix is null
            ? $"{memberName}-{grainName}"
            : $"{memberName}-{suffix}-{grainName}";

        return typeof(IGrainWithStringKey).IsAssignableFrom(grainType)
            ? (TGrain)GrainFactory.GetGrain(grainType, $"{keyPrefix}-{Guid.NewGuid():N}")
            : typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(grainType)
                ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid(), keyPrefix)
                : typeof(IGrainWithGuidKey).IsAssignableFrom(grainType)
                    ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid())
                    : typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(grainType)
                        ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue), keyPrefix)
                        : typeof(IGrainWithIntegerKey).IsAssignableFrom(grainType)
                            ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue))
                            : throw new NotSupportedException($"Unsupported grain key type for {grainType.FullName}.");
    }
}
