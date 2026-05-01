# Egil.Orleans.Testing

[![NuGet](https://img.shields.io/nuget/v/Egil.Orleans.Testing.svg)](https://www.nuget.org/packages/Egil.Orleans.Testing)

Deterministic async assertion helpers for [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/) test suites.

## Overview

`Egil.Orleans.Testing` provides a `GrainActivityCollector` that monitors grain calls and storage operations during integration tests. Instead of arbitrary `Task.Delay` waits, your assertions are retried automatically each time the collector detects grain activity, making tests both fast and reliable.

The library is **test-framework-agnostic**: it works with xUnit, NUnit, MSTest, or any other framework. The examples below use xUnit syntax like `[Fact]` and `IAsyncLifetime` only for concreteness; the collector and waiter patterns are not tied to xUnit.

## Getting started

### 1. Install the package

```shell
dotnet add package Egil.Orleans.Testing
```

### 2. Inline setup in a test class

The following is a complete example that sets up an `InProcessTestCluster` directly in the test class and forwards `IGrainActivityWaiter` so the tests can call `this.WaitForAssertionAsync(...)` directly:

```csharp
using Orleans.TestingHost;
using Egil.Orleans.Testing;

// Grain interface and implementation (in your production code)
public interface IOrderGrain : IGrainWithStringKey
{
    Task SubmitAsync(string item);
    Task<string?> GetStatusAsync();
}

public sealed class OrderGrain(
    [PersistentState("order", "Default")] IPersistentState<OrderState> state)
    : Grain, IOrderGrain
{
    public async Task SubmitAsync(string item)
    {
        state.State.Item = item;
        state.State.Status = "submitted";
        await state.WriteStateAsync();
    }

    public Task<string?> GetStatusAsync() => Task.FromResult(state.State.Status);
}

public sealed class OrderGrainTests : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;
    private readonly GrainActivityCollector collector = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorageAsDefault();
            siloBuilder.AddMemoryGrainStorage("Orders");

            siloBuilder.AddGrainActivityCollector(collector)
                .CollectStorageActivityFromDefault()
                .CollectStorageActivityFrom("Orders");
        });

        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
            await cluster.DisposeAsync();
    }

    [Fact]
    public async Task SubmitAsync_sets_status_to_submitted()
    {
        var grain = cluster!.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("laptop");

        await this.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("submitted", await grain.GetStatusAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct)
        => ((IGrainActivityWaiter)collector).WaitForAssertionAsync(assertion, filter, timeout, ct);
    }
}
```

### 3. Reusable Fixture Or Helper Object

When many tests share the same cluster, it is convenient to wrap the cluster and collector in a reusable object that implements `IGrainActivityWaiter`. In xUnit this could be a class fixture, collection fixture, or assembly fixture. In other frameworks it could be any shared helper with setup and teardown.

```csharp
public sealed class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
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

            // AddGrainActivityCollector wires up grain call observation automatically.
            // Most tests should start here even if they do not need low-level assertions.
            siloBuilder.AddGrainActivityCollector(Collector)

                // CollectStorageActivityFromDefault also enables storage observation,
                // which lets WaitForAssertionAsync react to persistence-driven changes.
                .CollectStorageActivityFromDefault();
        });
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (cluster is not null)
            await cluster.DisposeAsync();
    }

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, ct);
}

public class MyGrainTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task My_grain_does_the_right_thing()
    {
        var grain = fixture.GrainFactory.GetGrain<IMyGrain>(Guid.NewGuid().ToString());
        await grain.DoSomethingAsync();

        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("expected", await grain.GetStateAsync());
        });
    }
}
```

In xUnit, see the official docs for fixture registration options:
[Sharing Context between Tests](https://xunit.net/docs/shared-context)

## Features

- **Grain-scoped assertions** — pass a grain reference to restrict retriggers to that grain only. This is the best default when one grain owns the state you are asserting.
- **Standard assertions** — `WaitForAssertionAsync` without a grain scope retries on any detected grain activity (calls or storage operations). Useful when multiple grains contribute to the observed outcome.
- **Advanced assertions** — `WaitForStorageOperationAsync` and `WaitForGrainCallAsync` wait for events matching a predicate. Use sparingly — these couple tests to implementation details.
- **Configurable timeout** — defaults to 5 seconds, overridable per call or via the `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` environment variable. Timeout is automatically bypassed when a debugger is attached.
- **Test-framework-agnostic** — no runtime dependency on any test framework.

## Recipes

See [docs/recipes](docs/recipes/README.md) for scenario-driven guides covering:

- [Getting started](docs/recipes/getting-started.md)
- [Assertion patterns](docs/recipes/assertion-patterns.md)
- [Advanced assertions](docs/recipes/advanced-assertions.md)
- [Timers and reminders](docs/recipes/timers-and-reminders.md)
- [Streams](docs/recipes/streams.md)

## License

[MIT](../LICENSE)
