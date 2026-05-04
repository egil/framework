# Advanced Assertions

## When to prefer advanced over standard assertions

The standard `WaitForAssertionAsync` overloads assert against observable grain behavior — the *what*. The advanced methods (`GetStorageOperationsAsync`, `GetGrainCallsAsync`) collect low-level storage operations and incoming call contexts as `IAsyncEnumerable<T>` feeds — the *how*.

> ⚠️ **Coupling risk:** Tests using the advanced methods are tightly coupled to implementation details. If the grain's internal implementation changes (e.g., switching storage providers, renaming internal methods), these tests may break even if the grain's external contract is unchanged.

Use the advanced methods when:
- You need to assert that a specific write happened with specific data (e.g., ETag, state snapshot).
- You need to assert that a specific grain-to-grain call occurred without being able to observe its side effects externally.
- You need to collect and inspect a specific **number** of events (e.g., `.Take(N)`).

## Collecting storage operations

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

<!-- snippet: advanced_storage_assertion -->
<a id='snippet-advanced_storage_assertion'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/AdvancedAssertionsSample.cs#L84-L118' title='Snippet source file'>snippet source</a> | <a href='#snippet-advanced_storage_assertion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `StorageOperation` record exposes:
- `Kind` — `Read`, `Write`, or `Clear`
- `GrainId` — identity of the grain that triggered the operation
- `StorageName` — the storage provider name (e.g., `"Default"`)
- `StateName` — the grain state name
- `ETag` — the ETag after the operation
- `State` — the state value as `object?`

## Collecting grain calls

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

<!-- snippet: advanced_grain_call_assertion -->
<a id='snippet-advanced_grain_call_assertion'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/AdvancedAssertionsSample.cs#L120-L153' title='Snippet source file'>snippet source</a> | <a href='#snippet-advanced_grain_call_assertion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `IIncomingGrainCallContext` exposes:
- `MethodName` — the name of the called grain interface method as a string
- `TargetId` — the `GrainId` of the receiving grain
- `InterfaceType` — the grain interface type
- `InterfaceMethod` — the `MethodInfo` of the called method
- `Request` — the `IInvocationMessage` carrying the arguments
- `Response` — the response after the call completes

Note: the predicate is evaluated **after** the call completes, so `Response` is available inside the predicate.
