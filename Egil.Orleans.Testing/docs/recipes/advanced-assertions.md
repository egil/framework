# Advanced Assertions

## When to prefer advanced over standard assertions

The standard `WaitForAssertionAsync` overloads assert against observable grain behavior — the *what*. The advanced methods (`WaitForStorageOperationAsync`, `WaitForGrainCallAsync`) inspect low-level storage operations and incoming call contexts — the *how*.

> ⚠️ **Coupling risk:** Tests using the advanced methods are tightly coupled to implementation details. If the grain's internal implementation changes (e.g., switching storage providers, renaming internal methods), these tests may break even if the grain's external contract is unchanged.

Use the advanced methods when:
- You need to assert that a specific write happened with specific data (e.g., ETag, state snapshot).
- You need to assert that a specific grain-to-grain call occurred without being able to observe its side effects externally.

## Waiting for a specific storage operation

<!-- snippet: advanced_storage_assertion -->
<a id='snippet-advanced_storage_assertion'></a>
```cs
[Fact]
public async Task WaitForStorageOperationAsync_waits_for_specific_write()
{
    var grain = fixture.GrainFactory.GetGrain<IInventoryGrain>(Guid.NewGuid().ToString());

    // Start the wait before triggering the operation so no event is missed.
    var waitTask = fixture.Collector.WaitForStorageOperationAsync(
        op => op.Kind == StorageOperationKind.Write && op.GrainId == grain.GetGrainId());

    await grain.ReserveAsync(10);

    await waitTask;

    Assert.Equal(10, await grain.GetReservedAsync());
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/AdvancedAssertionsSample.cs#L48-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-advanced_storage_assertion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `StorageOperation` record exposes:
- `Kind` — `Read`, `Write`, or `Clear`
- `GrainId` — identity of the grain that triggered the operation
- `StorageName` — the storage provider name (e.g., `"Default"`)
- `StateName` — the grain state name
- `ETag` — the ETag after the operation
- `State` — the state value as `object?`

## Waiting for a specific grain call

<!-- snippet: advanced_grain_call_assertion -->
<a id='snippet-advanced_grain_call_assertion'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/AdvancedAssertionsSample.cs#L66-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-advanced_grain_call_assertion' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `IIncomingGrainCallContext` exposes:
- `MethodName` — the name of the called grain interface method as a string
- `TargetId` — the `GrainId` of the receiving grain
- `InterfaceType` — the grain interface type
- `InterfaceMethod` — the `MethodInfo` of the called method
- `Request` — the `IInvocationMessage` carrying the arguments
- `Response` — the response after the call completes

Note: the predicate is evaluated **after** the call completes, so `Response` is available inside the predicate.
