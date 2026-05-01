namespace Egil.Orleans.Testing.Samples.OperationFeeds;

// Reuses the grains defined in AdvancedAssertions namespace.
using Egil.Orleans.Testing.Samples.AdvancedAssertions;

// -- Tests -------------------------------------------------------------------

#region storage_operation_feed
/// <summary>
/// Demonstrates subscribing to a live feed of storage operations
/// for collecting and inspecting persistence activity.
/// </summary>
/// <remarks>
/// ⚠️ <c>SubscribeToStorageOperations</c> exposes persistence implementation details.
/// Tests using this feed are tightly coupled to storage providers and write timing.
/// Prefer <c>WaitForAssertionAsync</c> when you can assert the externally observable result.
/// </remarks>
public sealed class StorageOperationFeedTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task Collect_storage_writes_from_oneway_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();

        // Start collecting BEFORE triggering the action.
        // The feed is future-only — it does not replay past events.
        // Use Take(1) to automatically stop after the first matching write.
        var collectTask = fixture.Collector
            .SubscribeToStorageOperations(grain, ct)
            .Where(op => op.Kind == StorageOperationKind.Write)
            .Take(1)
            .ToListAsync(ct);

        await grain.ReserveAsync("widget", 10);

        var writes = await collectTask;

        Assert.Single(writes);
        Assert.Equal(grain.GetGrainId(), writes[0].GrainId);
    }
}
#endregion

#region grain_call_feed
/// <summary>
/// Demonstrates subscribing to a live feed of incoming grain calls
/// for collecting and inspecting call flow.
/// </summary>
/// <remarks>
/// ⚠️ <c>SubscribeToGrainCalls</c> exposes low-level call flow.
/// Prefer <c>WaitForAssertionAsync</c> for behavior-first assertions.
/// </remarks>
public sealed class GrainCallFeedTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task Collect_grain_calls_from_internal_call()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();

        // Start collecting BEFORE triggering the action.
        // Use Take(1) to automatically stop after the first matching call.
        var collectTask = fixture.Collector
            .SubscribeToGrainCalls(ct)
            .Where(ctx => ctx.MethodName == nameof(ILedgerGrain.AddReservationAsync))
            .Take(1)
            .ToListAsync(ct);

        await grain.ReserveAsync("gadget", 5);

        var calls = await collectTask;

        Assert.Single(calls);
    }
}
#endregion
