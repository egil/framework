# Egil.Orleans.Testing

[![NuGet](https://img.shields.io/nuget/v/Egil.Orleans.Testing.svg)](https://www.nuget.org/packages/Egil.Orleans.Testing)

Deterministic async assertion helpers for [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/) test suites.

## Overview

`Egil.Orleans.Testing` provides a `GrainActivityCollector` that monitors grain calls and storage operations during integration tests. Instead of arbitrary `Task.Delay` waits, your assertions are retried automatically each time the collector detects grain activity, making tests both fast and reliable.

The library is **test-framework-agnostic**: it works with xUnit, NUnit, MSTest, or any other framework. The examples below use xUnit syntax like `[Fact]` only for concreteness; the collector and waiter patterns are not tied to xUnit.

## Getting started

### 1. Install the package

```shell
dotnet add package Egil.Orleans.Testing
```

### 2. Inline setup in a test

The following is a complete example that builds an `InProcessTestCluster` directly in the test arrange step:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Orleans.TestingHost;
using Orleans.Timers;
using Egil.Orleans.Testing;

// Grain interface and implementation (in your production code)
public interface IOrderGrain : IGrainWithStringKey
{
    Task SubmitAsync(string item);
    Task<string?> GetLastSubmittedItemAsync();
}

public sealed class OrderState
{
    public string? PendingItem { get; set; }
    public string? LastSubmittedItem { get; set; }
}

public sealed class OrderGrain(
    [PersistentState("order", "Default")] IPersistentState<OrderState> state,
    ITimerRegistry timerRegistry,
    IGrainContext grainContext)
    : Grain, IOrderGrain
{
    private IGrainTimer? timer;

    public async Task SubmitAsync(string item)
    {
        // This extra timer hop is intentionally a little indirect.
        // It exists here to demonstrate the kind of async follow-up work
        // that is awkward to test reliably with a plain immediate assertion.
        state.State.PendingItem = item;
        await state.WriteStateAsync();

        timer?.Dispose();
        timer = timerRegistry.RegisterGrainTimer(
            grainContext,
            static (grain, ct) => grain.OnSubmissionCompletedAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(1),
                Period = Timeout.InfiniteTimeSpan,
            });
    }

    public Task<string?> GetLastSubmittedItemAsync()
        => Task.FromResult(state.State.LastSubmittedItem);

    private async Task OnSubmissionCompletedAsync(CancellationToken cancellationToken)
    {
        state.State.LastSubmittedItem = state.State.PendingItem;
        await state.WriteStateAsync();
        timer?.Dispose();
        timer = null;
    }
}

public sealed class OrderGrainTests
{
    [Fact]
    public async Task SubmitAsync_sets_status_to_submitted()
    {
        var collector = new GrainActivityCollector();
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddGrainActivityCollector(collector)
                .CollectStorageActivityFromDefault();
        });

        await using var cluster = builder.Build();
        await cluster.DeployAsync();

        var grain = cluster.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("laptop");

        // SubmitAsync only schedules the follow-up work on a grain timer.
        // The method returns before the timer callback writes the final state,
        // so a direct assertion here would race and tempt you to add Task.Delay.
        // WaitForAssertionAsync retries whenever the collector observes the
        // timer callback's storage write, which makes the test deterministic.
        await collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("laptop", await grain.GetLastSubmittedItemAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
```

<a id="orleans-test-cluster-fixture"></a>
### 3. Reusable Fixture Or Helper Object

When many tests share the same cluster, it is convenient to wrap the cluster and collector in a reusable object that implements `IGrainActivityWaiter`. In xUnit this could be a class fixture or collection fixture. In other frameworks it could be any shared helper with setup and teardown.

```csharp
public sealed class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    // The collector observes grain calls and storage writes inside the silo.
    // WaitForAssertionAsync uses those activity signals to know when to retry assertions.
    public GrainActivityCollector Collector { get; } = new();

    // Expose the client grain factory so tests do not need to reach into the cluster directly.
    public IGrainFactory GrainFactory => cluster?.Client ?? throw new InvalidOperationException("Test cluster not initialized.");

    // Create a unique GrainId when a test needs to share the same identity across
    // multiple Orleans concepts such as a grain reference and a stream id.
    public GrainId CreateUniqueGrainId<TGrain>([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName).GetGrainId();

    // Get a grain reference with a test-unique key so parallel tests do not collide.
    // The helper chooses the correct Orleans key shape based on the grain interface type.
    public TGrain GetUniqueGrain<TGrain>([System.Runtime.CompilerServices.CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName);

    public async ValueTask InitializeAsync()
    {
        // Build a single-silo in-process cluster. This is enough for most integration-style tests
        // and keeps startup time low.
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Register an in-memory default storage provider so grains using
            // [PersistentState(..., "Default")] can persist state without external dependencies.
            siloBuilder.AddMemoryGrainStorage("Default");

            // AddGrainActivityCollector wires up grain call observation automatically.
            // Most tests should start here even if they do not need low-level assertions.
            siloBuilder.AddGrainActivityCollector(Collector)

                // CollectStorageActivityFromDefault also enables storage observation,
                // which lets WaitForAssertionAsync react to persistence-driven changes.
                .CollectStorageActivityFromDefault();
        });

        // Build first, then deploy. DeployAsync starts the silo and connects the client.
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Always tear the cluster down so timers, ports, and background work are not shared
        // across unrelated tests.
        if (cluster is not null)
            await cluster.DisposeAsync();
    }

    // Forward the waiting API through the fixture so tests can say
    // `await fixture.WaitForAssertionAsync(...)` instead of going through `fixture.Collector`.
    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, ct);

    private TGrain CreateUniqueGrainReference<TGrain>(string memberName)
        where TGrain : IGrain
    {
        var grainType = typeof(TGrain);
        var grainName = grainType.Name;

        // Match Orleans key-shape conventions automatically so the calling test
        // does not need to care whether the grain uses string, Guid, integer,
        // or compound keys.
        return typeof(IGrainWithStringKey).IsAssignableFrom(grainType)
            ? (TGrain)GrainFactory.GetGrain(grainType, $"{memberName}-{grainName}-{Guid.NewGuid():N}")
            : typeof(IGrainWithGuidCompoundKey).IsAssignableFrom(grainType)
                ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid(), $"{memberName}-{grainName}")
                : typeof(IGrainWithGuidKey).IsAssignableFrom(grainType)
                    ? (TGrain)GrainFactory.GetGrain(grainType, Guid.NewGuid())
                    : typeof(IGrainWithIntegerCompoundKey).IsAssignableFrom(grainType)
                        ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue), $"{memberName}-{grainName}")
                        : typeof(IGrainWithIntegerKey).IsAssignableFrom(grainType)
                            ? (TGrain)GrainFactory.GetGrain(grainType, Random.Shared.NextInt64(1, long.MaxValue))
                            : throw new NotSupportedException($"Unsupported grain key type for {grainType.FullName}.");
    }
}

public class MyGrainTests(OrleansTestClusterFixture fixture)
    : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task My_grain_does_the_right_thing()
    {
        var grain = fixture.GetUniqueGrain<IMyGrain>();
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

- [Assertion patterns](docs/recipes/assertion-patterns.md)
- [Advanced assertions](docs/recipes/advanced-assertions.md)
- [Timers and reminders](docs/recipes/timers-and-reminders.md)
- [Streams](docs/recipes/streams.md)

## License

[MIT](../LICENSE)
