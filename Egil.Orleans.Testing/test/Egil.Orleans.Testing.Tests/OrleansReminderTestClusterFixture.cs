using Orleans.TestingHost;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.Testing.Tests;

/// <summary>
/// Dedicated cluster fixture for reminder tests that need deterministic time.
/// </summary>
public sealed class OrleansReminderTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public ReminderTestClock ReminderClock { get; } = new();

    public InProcessTestCluster Cluster => cluster ?? throw new InvalidOperationException("Test cluster not initialized.");

    public IGrainFactory GrainFactory => Cluster.Client;

    public string CreateUniqueStringKey(string? suffix = null, [CallerMemberName] string memberName = "")
        => suffix is null
            ? $"{memberName}-{Guid.NewGuid():N}"
            : $"{memberName}-{suffix}-{Guid.NewGuid():N}";

    public TGrain GetUniqueGrain<TGrain>(string? suffix = null, [CallerMemberName] string memberName = "")
        where TGrain : IGrainWithStringKey
        => GrainFactory.GetGrain<TGrain>(CreateUniqueStringKey(suffix, memberName));

    public async ValueTask InitializeAsync()
    {
        cluster = await TestClusterFactory.DeployAsync(Collector, collectStorageActivity: true, reminderClock: ReminderClock);
    }

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
}
