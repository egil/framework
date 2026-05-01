# Advanced Assertions

## When to prefer advanced over standard assertions

The standard `WaitForAssertionAsync` overloads assert against observable grain behavior — the *what*. The advanced methods (`WaitForStorageOperationAsync`, `WaitForGrainCallAsync`) inspect low-level storage operations and incoming call contexts — the *how*.

> ⚠️ **Coupling risk:** Tests using the advanced methods are tightly coupled to implementation details. If the grain's internal implementation changes (e.g., switching storage providers, renaming internal methods), these tests may break even if the grain's external contract is unchanged.

Use the advanced methods when:
- You need to assert that a specific write happened with specific data (e.g., ETag, state snapshot).
- You need to assert that a specific grain-to-grain call occurred without being able to observe its side effects externally.

## Waiting for a specific storage operation

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleans-test-cluster-fixture)

<!-- snippet: advanced_storage_assertion -->
<a id='snippet-advanced_storage_assertion'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/AdvancedAssertionsSample.cs#L84-L110' title='Snippet source file'>snippet source</a> | <a href='#snippet-advanced_storage_assertion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `StorageOperation` record exposes:
- `Kind` — `Read`, `Write`, or `Clear`
- `GrainId` — identity of the grain that triggered the operation
- `StorageName` — the storage provider name (e.g., `"Default"`)
- `StateName` — the grain state name
- `ETag` — the ETag after the operation
- `State` — the state value as `object?`

## Waiting for a specific grain call

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleans-test-cluster-fixture)

<!-- snippet: advanced_grain_call_assertion -->
<a id='snippet-advanced_grain_call_assertion'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/AdvancedAssertionsSample.cs#L112-L137' title='Snippet source file'>snippet source</a> | <a href='#snippet-advanced_grain_call_assertion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `IIncomingGrainCallContext` exposes:
- `MethodName` — the name of the called grain interface method as a string
- `TargetId` — the `GrainId` of the receiving grain
- `InterfaceType` — the grain interface type
- `InterfaceMethod` — the `MethodInfo` of the called method
- `Request` — the `IInvocationMessage` carrying the arguments
- `Response` — the response after the call completes

Note: the predicate is evaluated **after** the call completes, so `Response` is available inside the predicate.
