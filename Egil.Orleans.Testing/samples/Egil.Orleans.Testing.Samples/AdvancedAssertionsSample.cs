using Orleans.Concurrency;

namespace Egil.Orleans.Testing.Samples.AdvancedAssertions;

// -- Grain definitions -------------------------------------------------------

/// <summary>
/// A warehouse grain that accepts incoming reservation requests.
/// <c>ReserveAsync</c> is <see cref="OneWayAttribute"/>, so the caller returns
/// immediately while the grain processes the reservation asynchronously.
/// This makes the <c>Get*Async</c> feeds and <c>WaitForAssertionAsync</c> the only
/// reliable way to know when the storage write or internal grain call completes.
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
        // is invisible to the original caller but observable via GetGrainCallsAsync.
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
/// Demonstrates using <c>GetStorageOperationsAsync</c> to collect and inspect storage operations directly.
/// </summary>
/// <remarks>
/// ⚠️ <c>GetStorageOperationsAsync</c> couples your test to implementation details.
/// Prefer <c>WaitForAssertionAsync</c> when you can assert the externally observable result.
/// </remarks>
public sealed class WarehouseStorageOperationTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task GetStorageOperationsAsync_collects_write_from_oneway_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();

        // Start collecting BEFORE triggering the action.
        // Use Take(1) to automatically stop after the first matching write.
        var collectTask = fixture.Collector
            .GetStorageOperationsAsync(grain, cancellationToken: ct)
            .Where(op => op.Kind == StorageOperationKind.Write)
            .Take(1)
            .ToListAsync(ct);

        await grain.ReserveAsync("widget", 10);

        var writes = await collectTask;

        Assert.Single(writes);
        Assert.Equal(grain.GetGrainId(), writes[0].GrainId);
        Assert.Equal(10, await grain.GetReservedAsync("widget"));
    }
}
#endregion

#region advanced_grain_call_assertion
/// <summary>
/// Demonstrates using <c>GetGrainCallsAsync</c> to collect and inspect incoming grain calls directly.
/// </summary>
/// <remarks>
/// ⚠️ <c>GetGrainCallsAsync</c> couples your test to implementation details.
/// Prefer <c>WaitForAssertionAsync</c> when you can assert the externally observable result.
/// </remarks>
public sealed class WarehouseGrainCallTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task GetGrainCallsAsync_collects_internal_grain_to_grain_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();

        // The warehouse grain internally calls ILedgerGrain.AddReservationAsync,
        // which is a grain-to-grain call invisible to the original test caller.
        // Use Take(1) to automatically stop after the first matching call.
        var collectTask = fixture.Collector
            .GetGrainCallsAsync(cancellationToken: ct)
            .Where(ctx => ctx.MethodName == nameof(ILedgerGrain.AddReservationAsync))
            .Take(1)
            .ToListAsync(ct);

        await grain.ReserveAsync("gadget", 5);

        var calls = await collectTask;

        Assert.Single(calls);
    }
}
#endregion

