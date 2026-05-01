using Orleans.Concurrency;
using Orleans.TestingHost;

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

/// <summary>
/// Demonstrates advanced wait methods that inspect storage operations and
/// incoming grain calls directly.
/// </summary>
/// <remarks>
/// ⚠️ The advanced wait methods (<c>WaitForStorageOperationAsync</c>,
/// <c>WaitForGrainCallAsync</c>) couple your tests to implementation details.
/// Prefer the standard <c>WaitForAssertionAsync</c> overloads when possible.
/// </remarks>
public sealed class WarehouseGrainTests(AdvancedAssertionsFixture fixture) : IClassFixture<AdvancedAssertionsFixture>
{
    #region advanced_storage_assertion
    [Fact]
    public async Task WaitForStorageOperationAsync_waits_for_write_from_oneway_call()
    {
        var grain = fixture.GrainFactory.GetGrain<IWarehouseGrain>(Guid.NewGuid().ToString());

        // Start the wait before triggering the operation so no event is missed.
        // ReserveAsync is [OneWay] — it returns before the grain processes the reservation,
        // so we must wait for the storage write that happens inside the grain.
        var waitTask = fixture.Collector.WaitForStorageOperationAsync(
            op => op.Kind == StorageOperationKind.Write && op.GrainId == grain.GetGrainId(),
            ct: TestContext.Current.CancellationToken);

        await grain.ReserveAsync("widget", 10);

        await waitTask;

        Assert.Equal(10, await grain.GetReservedAsync("widget"));
    }
    #endregion

    #region advanced_grain_call_assertion
    [Fact]
    public async Task WaitForGrainCallAsync_waits_for_internal_grain_to_grain_call()
    {
        var grain = fixture.GrainFactory.GetGrain<IWarehouseGrain>(Guid.NewGuid().ToString());

        // The warehouse grain internally calls ILedgerGrain.AddReservationAsync,
        // which is a grain-to-grain call invisible to the original test caller.
        // WaitForGrainCallAsync lets you observe it directly.
        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            ctx => ctx.MethodName == nameof(ILedgerGrain.AddReservationAsync),
            ct: TestContext.Current.CancellationToken);

        await grain.ReserveAsync("gadget", 5);

        await waitTask;
    }
    #endregion
}

// -- Shared fixture ----------------------------------------------------------

public sealed class AdvancedAssertionsFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public IGrainFactory GrainFactory => cluster!.Client;

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
}

