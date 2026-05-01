using Orleans.TestingHost;

namespace Egil.Orleans.Testing.Samples.AdvancedAssertions;

// -- Grain definitions -------------------------------------------------------

public interface IInventoryGrain : IGrainWithStringKey
{
    Task ReserveAsync(int quantity);

    Task<int> GetReservedAsync();
}

public sealed class InventoryState
{
    public int Reserved { get; set; }
}

public sealed class InventoryGrain(
    [PersistentState("inventory", "Default")] IPersistentState<InventoryState> state)
    : Grain, IInventoryGrain
{
    public async Task ReserveAsync(int quantity)
    {
        state.State.Reserved += quantity;
        await state.WriteStateAsync();
    }

    public Task<int> GetReservedAsync() => Task.FromResult(state.State.Reserved);
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
public sealed class InventoryGrainTests(AdvancedAssertionsFixture fixture) : IClassFixture<AdvancedAssertionsFixture>
{
    #region advanced_storage_assertion
    [Fact]
    public async Task WaitForStorageOperationAsync_waits_for_specific_write()
    {
        var grain = fixture.GrainFactory.GetGrain<IInventoryGrain>(Guid.NewGuid().ToString());

        // Start the wait before triggering the operation so no event is missed.
        var waitTask = fixture.Collector.WaitForStorageOperationAsync(
            op => op.Kind == StorageOperationKind.Write && op.GrainId == grain.GetGrainId(),
            ct: TestContext.Current.CancellationToken);

        await grain.ReserveAsync(10);

        await waitTask;

        Assert.Equal(10, await grain.GetReservedAsync());
    }
    #endregion

    #region advanced_grain_call_assertion
    [Fact]
    public async Task WaitForGrainCallAsync_waits_for_specific_method_call()
    {
        var grain = fixture.GrainFactory.GetGrain<IInventoryGrain>(Guid.NewGuid().ToString());

        // Start the wait before triggering the call.
        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            ctx => ctx.MethodName == nameof(IInventoryGrain.ReserveAsync)
                && ctx.TargetId == grain.GetGrainId(),
            ct: TestContext.Current.CancellationToken);

        await grain.ReserveAsync(5);

        await waitTask;
    }
    #endregion
}

// -- Shared fixture ----------------------------------------------------------

public sealed class AdvancedAssertionsFixture : IAsyncLifetime
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
}

