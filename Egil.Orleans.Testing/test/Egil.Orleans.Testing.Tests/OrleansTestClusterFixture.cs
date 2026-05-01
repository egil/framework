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
public sealed class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    /// <summary>
    /// Gets the activity collector under test.
    /// </summary>
    public GrainActivityCollector Collector { get; } = new();

    /// <summary>
    /// Gets the deterministic reminder clock.
    /// </summary>
    public ReminderTestClock ReminderClock { get; } = new();

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
        cluster = await TestClusterFactory.DeployAsync(Collector, collectStorageActivity: true, reminderClock: ReminderClock);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        ReminderClock.Dispose();

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
}
