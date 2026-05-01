# Operation Feeds

## When to use operation feeds

The `SubscribeToStorageOperations` and `SubscribeToGrainCalls` methods provide a live, future-only stream of low-level events that you can collect and inspect after triggering grain behavior. Unlike the `WaitFor*` methods, feeds do not have a timeout or a predicate — they simply yield every matching event for as long as you consume them.

Use feeds when:
- You want to **collect all** storage operations or grain calls that happened during a test scenario, not just wait for one.
- You need to assert on the **sequence** or **count** of events, not just that a single match occurred.
- You are building custom test infrastructure on top of the collector.

> ⚠️ **Coupling risk:** Feed-based tests are tightly coupled to implementation details like storage providers, write timing, persistence strategy, and internal call flow. If the grain's internal implementation changes, these tests may break even when externally observable behavior is unchanged. Prefer `WaitForAssertionAsync` when possible.

## Key semantics

| Property | Behavior |
|---|---|
| Start point | Subscription begins when enumeration starts (first `MoveNextAsync`) |
| History | Future-only — no replay of past events |
| Buffering | Unbounded — slow consumers never lose events |
| Completion | Stops when the `CancellationToken` is cancelled or the caller breaks out of the `await foreach` |

## Subscribing to storage operations

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

<!-- snippet: storage_operation_feed -->
<a id='snippet-storage_operation_feed'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/OperationFeedSample.cs#L14-L49' title='Snippet source file'>snippet source</a> | <a href='#snippet-storage_operation_feed' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Both `SubscribeToStorageOperations` overloads expose:
- **Global** — receives all storage operations from any grain.
- **Grain-scoped** — receives only storage operations for the specified grain.

## Subscribing to grain calls

<!-- snippet: grain_call_feed -->
<a id='snippet-grain_call_feed'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/OperationFeedSample.cs#L51-L91' title='Snippet source file'>snippet source</a> | <a href='#snippet-grain_call_feed' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Both `SubscribeToGrainCalls` overloads expose:
- **Global** — receives all incoming grain calls from any grain.
- **Grain-scoped** — receives only calls targeting the specified grain.

## Assertion-scope suppression

Storage operations triggered inside a `WaitForAssertionAsync` callback (via `RequestContextScope.ForAssertion()`) are automatically suppressed from the feed. This prevents self-triggered storage activity from polluting your collected events.
