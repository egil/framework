using Orleans.TestingHost;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.Testing.Samples.GrainScopedAssertions;

// -- Grain definitions -------------------------------------------------------

public interface ICounterGrain : IGrainWithStringKey
{
    Task IncrementAsync();

    Task<int> GetCountAsync();
}

public sealed class CounterState
{
    public int Count { get; set; }
}

public sealed class CounterGrain(
    [PersistentState("counter", "Default")] IPersistentState<CounterState> state)
    : Grain, ICounterGrain
{
    public async Task IncrementAsync()
    {
        state.State.Count++;
        await state.WriteStateAsync();
    }

    public Task<int> GetCountAsync() => Task.FromResult(state.State.Count);
}

// -- Tests -------------------------------------------------------------------

#region grain_scoped_assertions_fixture
/// <summary>
/// Demonstrates grain-scoped <c>WaitForAssertionAsync</c> overloads.
/// Activity from an unrelated grain does not retrigger the assertion.
/// </summary>
public sealed class CounterGrainTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task WaitForAssertionAsync_with_grain_only_retriggers_on_activity_from_that_grain()
    {
        var targetGrain = fixture.GetUniqueGrain<ICounterGrain>();
        var unrelatedGrain = fixture.GetUniqueGrain<ICounterGrain>();

        await targetGrain.IncrementAsync();

        // Pass the grain to the scoped overload.
        // Only activity originating from 'targetGrain' will retrigger this assertion.
        await fixture.WaitForAssertionAsync(targetGrain, async () =>
        {
            Assert.Equal(1, await targetGrain.GetCountAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForAssertionAsync_grain_overload_passes_grain_to_lambda()
    {
        var grain = fixture.GetUniqueGrain<ICounterGrain>();

        await grain.IncrementAsync();
        await grain.IncrementAsync();

        // The grain reference is forwarded into the lambda so you can assert
        // without capturing it in a closure.
        var count = await fixture.WaitForAssertionAsync(grain, async (g) =>
        {
            var c = await g.GetCountAsync();
            Assert.True(c >= 2);
            return c;
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
    }
}
#endregion

// -- Shared fixture ----------------------------------------------------------

public sealed class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public IGrainFactory GrainFactory => cluster!.Client;

    public GrainId CreateUniqueGrainId<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName).GetGrainId();

    public TGrain GetUniqueGrain<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName);

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddGrainActivityCollector(Collector)
                .CollectStorageActivityFromDefault();
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

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);

    private TGrain CreateUniqueGrainReference<TGrain>(string memberName)
        where TGrain : IGrain
    {
        var grainType = typeof(TGrain);
        var grainName = grainType.Name;

        return typeof(IGrainWithStringKey).IsAssignableFrom(grainType)
            ? (TGrain)GrainFactory.GetGrain(grainType, $"{memberName}-{grainName}-{Guid.NewGuid():N}")
            : typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(grainType)
                ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid(), $"{memberName}-{grainName}")
                : typeof(IGrainWithGuidKey).IsAssignableFrom(grainType)
                    ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid())
                    : typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(grainType)
                        ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue), $"{memberName}-{grainName}")
                        : typeof(IGrainWithIntegerKey).IsAssignableFrom(grainType)
                            ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue))
                            : throw new NotSupportedException($"Unsupported grain key type for {grainType.FullName}.");
    }
}

