# Egil.Orleans.Testing

[![NuGet](https://img.shields.io/nuget/v/Egil.Orleans.Testing.svg)](https://www.nuget.org/packages/Egil.Orleans.Testing)

Deterministic async assertion helpers for [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/) test suites.

## Overview

`Egil.Orleans.Testing` provides a `GrainActivityCollector` that monitors grain calls and storage operations during integration tests. Instead of arbitrary `Task.Delay` waits, your assertions are retried automatically each time the collector detects grain activity — making tests both fast and reliable.

The library is **test-framework-agnostic**: it works with xUnit, NUnit, MSTest, or any other framework.

## Getting started

### 1. Install the package

```shell
dotnet add package Egil.Orleans.Testing
```

### 2. Inline setup in a test class

The following is a complete, copy/paste-ready example that shows how to set up an `InProcessTestCluster` directly in a test class with **both** storage observation and grain call observation enabled:

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

// Test class — cluster lives inline, no shared fixture required
public sealed class OrderGrainTests : IAsyncLifetime
{
    private InProcessTestCluster? cluster;
    private readonly GrainActivityCollector collector = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");

            // AddGrainActivityCollector wires up grain call observation automatically.
            // CollectStorageActivityFromDefault also enables storage observation.
            siloBuilder.AddGrainActivityCollector(collector)
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

    [Fact]
    public async Task SubmitAsync_sets_status_to_submitted()
    {
        var grain = cluster!.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("laptop");

        // WaitForAssertionAsync retries the assertion each time grain activity
        // is detected (storage write or grain call) until it passes or times out.
        await collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("submitted", await grain.GetStatusAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
```

### 3. Shared fixture (optional)

When many test classes share the same cluster, use an assembly fixture:

```csharp
[assembly: AssemblyFixture(typeof(OrleansTestClusterFixture))]

public sealed class OrleansTestClusterFixture : IAsyncLifetime
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
            await cluster.DisposeAsync();
    }
}

// Test class — injects the shared fixture
public class MyGrainTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task My_grain_does_the_right_thing()
    {
        var grain = fixture.GrainFactory.GetGrain<IMyGrain>(Guid.NewGuid().ToString());
        await grain.DoSomethingAsync();

        await fixture.Collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("expected", await grain.GetStateAsync());
        });
    }
}
```

## Features

- **Standard assertions** — `WaitForAssertionAsync` retries on any detected grain activity (calls or storage operations). Ideal for asserting observable grain behavior.
- **Grain-scoped assertions** — pass a grain reference to restrict retriggers to that grain only.
- **Advanced assertions** — `WaitForStorageOperationAsync` and `WaitForGrainCallAsync` wait for events matching a predicate. Use sparingly — these couple tests to implementation details.
- **Configurable timeout** — defaults to 5 seconds, overridable per call or via the `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` environment variable. Timeout is automatically bypassed when a debugger is attached.
- **Test-framework-agnostic** — no runtime dependency on any test framework.

## Recipes

See [docs/recipes](docs/recipes/README.md) for scenario-driven guides covering:

- [Getting started](docs/recipes/getting-started.md)
- [Assertion patterns](docs/recipes/assertion-patterns.md)
- [Advanced assertions](docs/recipes/advanced-assertions.md)
- [Timers and reminders](docs/recipes/timers-and-reminders.md)

## License

[MIT](../LICENSE)
