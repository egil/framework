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
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var writes = new List<StorageOperation>();

        // Start collecting storage operations for the grain BEFORE triggering
        // the action. The feed is future-only — it does not replay past events.
        var feedTask = Task.Run(async () =>
        {
            await foreach (var op in fixture.Collector.SubscribeToStorageOperations(grain, cts.Token))
            {
                if (op.Kind == StorageOperationKind.Write)
                {
                    writes.Add(op);
                    await cts.CancelAsync();
                }
            }
        }, cts.Token);

        await grain.ReserveAsync("widget", 10);

        // Wait for the feed to collect the write
        try { await feedTask; }
        catch (OperationCanceledException) { }

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
        var grain = fixture.GetUniqueGrain<IWarehouseGrain>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var calls = new List<IIncomingGrainCallContext>();

        // Start collecting grain calls globally BEFORE triggering the action.
        // We'll look for the internal ILedgerGrain.AddReservationAsync call.
        var feedTask = Task.Run(async () =>
        {
            await foreach (var ctx in fixture.Collector.SubscribeToGrainCalls(cts.Token))
            {
                if (ctx.MethodName == nameof(ILedgerGrain.AddReservationAsync))
                {
                    calls.Add(ctx);
                    await cts.CancelAsync();
                }
            }
        }, cts.Token);

        await grain.ReserveAsync("gadget", 5);

        try { await feedTask; }
        catch (OperationCanceledException) { }

        Assert.Single(calls);
    }
}
#endregion
