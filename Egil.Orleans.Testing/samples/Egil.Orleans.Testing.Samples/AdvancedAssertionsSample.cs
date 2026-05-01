using Orleans.Concurrency;
using Orleans.TestingHost;
using System.Runtime.CompilerServices;

namespace Egil.Orleans.Testing.Samples.AdvancedAssertions;

// -- Grain definitions -------------------------------------------------------

/// <summary>
/// A warehouse grain that accepts incoming reservation requests.
/// <c>ReserveAsync</c> is <see cref="OneWayAttribute"/>, so the caller returns
/// immediately while the grain processes the reservation asynchronously.
/// This makes the advanced wait methods the only reliable way to know
/// when the storage write or internal grain call completes.
/// </summary>
public interface IWarehouseGrain : IGrainWithStringKey
{
    [OneWay]
    Task ReserveAsync(string sku, int quantity);

    Task<int> GetReservedAsync(string sku);
}

/// <summary>
/// Internal ledger grain that tracks reserved quantities per SKU.
/// Called by <see cref="WarehouseGrain"/> during reservation processing.
/// </summary>
public interface ILedgerGrain : IGrainWithStringKey
{
    Task AddReservationAsync(int quantity);

    Task<int> GetTotalAsync();
}

public sealed class WarehouseState
{
    public Dictionary<string, int> Reservations { get; set; } = new();
}

public sealed class LedgerState
{
    public int Total { get; set; }
}

#region advanced_warehouse_grain
public sealed class WarehouseGrain(
    [PersistentState("warehouse", "Default")] IPersistentState<WarehouseState> state,
    IGrainFactory grainFactory)
    : Grain, IWarehouseGrain
{
    public async Task ReserveAsync(string sku, int quantity)
    {
        // Simulate async processing that happens after the caller has returned
        // (because ReserveAsync is [OneWay]).
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        state.State.Reservations[sku] = state.State.Reservations.GetValueOrDefault(sku) + quantity;
        await state.WriteStateAsync();

        // Delegate to an internal ledger grain — a grain-to-grain call that
        // is invisible to the original caller but observable via WaitForGrainCallAsync.
        var ledger = grainFactory.GetGrain<ILedgerGrain>(sku);
        await ledger.AddReservationAsync(quantity);
    }

    public Task<int> GetReservedAsync(string sku)
        => Task.FromResult(state.State.Reservations.GetValueOrDefault(sku));
}
#endregion

public sealed class LedgerGrain(
    [PersistentState("ledger", "Default")] IPersistentState<LedgerState> state)
    : Grain, ILedgerGrain
{
    public async Task AddReservationAsync(int quantity)
    {
        state.State.Total += quantity;
        await state.WriteStateAsync();
    }

    public Task<int> GetTotalAsync() => Task.FromResult(state.State.Total);
}

// -- Tests -------------------------------------------------------------------

#region advanced_storage_assertion
/// <summary>
/// Demonstrates advanced wait methods that inspect storage operations directly.
/// </summary>
/// <remarks>
/// ⚠️ <c>WaitForStorageOperationAsync</c> couples your test to implementation details.
/// Prefer <c>WaitForAssertionAsync</c> when you can assert the externally observable result.
/// </remarks>
public sealed class WarehouseStorageOperationTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task WaitForStorageOperationAsync_waits_for_write_from_oneway_call()
    {
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();

        await grain.ReserveAsync("widget", 10);

        // Assert after triggering the action. The collector remembers recent
        // storage activity, so this also works when the write already happened.
        await fixture.Collector.WaitForStorageOperationAsync(
            op => op.Kind == StorageOperationKind.Write && op.GrainId == grain.GetGrainId(),
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(10, await grain.GetReservedAsync("widget"));
    }
}
#endregion

#region advanced_grain_call_assertion
/// <summary>
/// Demonstrates advanced wait methods that inspect incoming grain calls directly.
/// </summary>
/// <remarks>
/// ⚠️ <c>WaitForGrainCallAsync</c> couples your test to implementation details.
/// Prefer <c>WaitForAssertionAsync</c> when you can assert the externally observable result.
/// </remarks>
public sealed class WarehouseGrainCallTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task WaitForGrainCallAsync_waits_for_internal_grain_to_grain_call()
    {
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();

        await grain.ReserveAsync("gadget", 5);

        // The warehouse grain internally calls ILedgerGrain.AddReservationAsync,
        // which is a grain-to-grain call invisible to the original test caller.
        // The collector keeps recent calls, so you can wait after triggering the action too.
        await fixture.Collector.WaitForGrainCallAsync(
            ctx => ctx.MethodName == nameof(ILedgerGrain.AddReservationAsync),
            ct: TestContext.Current.CancellationToken);
    }
}
#endregion

// -- Shared fixture ----------------------------------------------------------

/// <summary>
/// Minimal reusable Orleans test cluster fixture.
/// Copy this into your own test project when several tests need the same cluster setup.
/// </summary>
/// <remarks>
/// The fixture combines three responsibilities:
/// 1. Own the lifecycle of an <see cref="InProcessTestCluster"/>.
/// 2. Expose a ready-to-use <see cref="IGrainFactory"/> for test code.
/// 3. Forward <see cref="IGrainActivityWaiter"/> calls to a <see cref="GrainActivityCollector"/>
///    so tests can call <c>fixture.WaitForAssertionAsync(...)</c> directly.
/// </remarks>
public sealed class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    // The collector observes grain calls and storage writes inside the silo.
    // WaitForAssertionAsync uses those activity signals to know when to retry assertions.
    public GrainActivityCollector Collector { get; } = new();

    // Expose the client grain factory so tests do not need to reach into the cluster directly.
    public IGrainFactory GrainFactory => cluster!.Client;

    /// <summary>
    /// Creates a unique <see cref="GrainId"/> for the current test method.
    /// </summary>
    /// <remarks>
    /// This is useful when a test needs a stable identifier that must be shared between
    /// a grain reference and some other Orleans concept such as a stream id or reminder name.
    /// The generated key includes the calling test method name and grain interface name,
    /// which makes copied snippets easier to reason about while still avoiding collisions.
    /// </remarks>
    public GrainId CreateUniqueGrainId<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName).GetGrainId();

    /// <summary>
    /// Gets a grain reference with a test-unique key.
    /// </summary>
    /// <remarks>
    /// Prefer this helper over hard-coded keys in sample-style tests.
    /// It keeps parallel tests isolated from each other and removes boilerplate
    /// around choosing the correct Orleans key type for the grain interface.
    /// </remarks>
    public TGrain GetUniqueGrain<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName);

    public async ValueTask InitializeAsync()
    {
        // Build a one-silo in-process test cluster. Most samples only need one silo,
        // which keeps startup cost and overall test complexity low.
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Register a default storage provider so grains using [PersistentState(..., "Default")]
            // can read and write state without any external infrastructure.
            siloBuilder.AddMemoryGrainStorage("Default");

            // AddGrainActivityCollector wires up grain call observation automatically.
            // CollectStorageActivityFromDefault also enables storage observation for the
            // "Default" provider, which lets WaitForAssertionAsync react to persisted state changes.
            siloBuilder.AddGrainActivityCollector(Collector)
                .CollectStorageActivityFromDefault();
        });

        // Build first, then deploy. DeployAsync starts the silo and makes the client available.
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Always tear the cluster down after the test run so ports, timers, and other resources
        // are not kept alive across unrelated tests.
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    // Forward the waiting API through the fixture so tests can stay focused on intent:
    //   await fixture.WaitForAssertionAsync(...)
    // instead of:
    //   await fixture.Collector.WaitForAssertionAsync(...)
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

        // Match Orleans key-shape conventions based on the grain interface marker.
        // This lets the same helper work for string, Guid, integer, and compound-key grains.
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

