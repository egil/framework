# Assertion Patterns

These samples reuse the canonical [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper) from the README rather than repeating its full setup on every page.

## Grain-scoped assertions

When a single grain owns the state you are asserting, prefer passing that grain into `WaitForAssertionAsync`. This keeps retries focused on the grain that actually matters.

Pass a grain reference as the first argument to restrict retriggers to activity from that grain only:

<!-- snippet: grain_scoped_assertions_fixture -->
<a id='snippet-grain_scoped_assertions_fixture'></a>
```cs
/// <summary>
/// Demonstrates grain-scoped <c>WaitForAssertionAsync</c> overloads.
/// Activity from an unrelated grain does not retrigger the assertion.
/// </summary>
public sealed class CounterGrainTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task WaitForAssertionAsync_with_grain_only_retriggers_on_activity_from_that_grain()
    {
        var targetGrain = fixture.GetUniqueGrain<ICounterGrain>();
        var unrelatedGrain = fixture.GetUniqueGrain<ICounterGrain>();

        await targetGrain.IncrementAsync();

        // Pass the grain to the scoped overload.
        // Only activity originating from 'targetGrain' will retrigger this assertion.
        await fixture.WaitForAssertionAsync(targetGrain, async () =>
        {
            Assert.Equal(1, await targetGrain.GetCountAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WaitForAssertionAsync_grain_overload_passes_grain_to_lambda()
    {
        var grain = fixture.GetUniqueGrain<ICounterGrain>();

        await grain.IncrementAsync();
        await grain.IncrementAsync();

        // The grain reference is forwarded into the lambda so you can assert
        // without capturing it in a closure.
        var count = await fixture.WaitForAssertionAsync(grain, async (g) =>
        {
            var c = await g.GetCountAsync();
            Assert.True(c >= 2);
            return c;
        }, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/GrainScopedAssertionSample.cs#L32-L75' title='Snippet source file'>snippet source</a> | <a href='#snippet-grain_scoped_assertions_fixture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

## Waiting for any grain activity

The unscoped `WaitForAssertionAsync` overload retries the assertion each time any grain activity (call or storage operation) is observed. Use it when the observed outcome can be driven by more than one grain, or when you do not have a meaningful grain reference to scope the wait to.

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

```csharp
await fixture.WaitForAssertionAsync(async () =>
{
    Assert.Equal("ready", await grain.GetStatusAsync());
});
```

## Returning values from assertions

All `WaitForAssertionAsync` overloads have value-returning variants — use `Func<Task<TResult>>` or `Func<ValueTask<TResult>>`:

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

```csharp
var count = await fixture.WaitForAssertionAsync(async () =>
{
    var c = await grain.GetCountAsync();
    Assert.True(c >= 5);
    return c;
});
// count is the first observed value that satisfied the assertion
```

## Configuring the timeout

The default timeout is **5 seconds** (or indefinite when a debugger is attached).

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

Override per-call:
```csharp
await fixture.WaitForAssertionAsync(
    async () => Assert.Equal("done", await grain.GetStatusAsync()),
    timeout: TimeSpan.FromSeconds(10));
```

Override globally in code:
```csharp
IGrainActivityWaiter.DefaultWaitTimeout = TimeSpan.FromSeconds(15);
```

Or via environment variable:
```
WAIT_FOR_ASSERTION_TIMEOUT_SECONDS=15
```

When the assertion does not pass before the timeout, `WaitForAssertionTimeoutException` is thrown. Its `InnerException` holds the last assertion failure.
