# Getting Started

## Registering the collector

Create a `GrainActivityCollector` and register it with your silo builder before deploying the cluster.

```csharp
var collector = new GrainActivityCollector();

siloBuilder.AddGrainActivityCollector(collector)
    .CollectStorageActivityFromDefault()
    .CollectStorageActivityFrom("Orders");
```

- **`AddGrainActivityCollector`** wires up grain call observation automatically as an `IIncomingGrainCallFilter`.
- **`CollectStorageActivityFromDefault()`** wraps the `"Default"` storage provider and enables storage observation.
- **`CollectStorageActivityFrom("Orders")`** shows the named-provider variant for non-default storage.
- Omit the storage call entirely if you only need grain call observation.

## Inline setup in a test class

The simplest way to use the library is to initialize a cluster directly in a test class with `IAsyncLifetime`:

<!-- snippet: getting_started_test_class -->
<a id='snippet-getting_started_test_class'></a>
```cs
/// <summary>
/// Example: inline <see cref="InProcessTestCluster"/> in a test class,
/// showing both storage observation and grain call observation.
/// </summary>
public sealed class OrderGrainTests : IAsyncLifetime
{
    private InProcessTestCluster? cluster;

    // A single collector shared across all tests in this class.
    private readonly GrainActivityCollector collector = new();

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Typical: register the default grain storage provider.
            siloBuilder.AddMemoryGrainStorageAsDefault();

            // Optional: show a secondary named provider as well.
            siloBuilder.AddMemoryGrainStorage("Orders");

            // Enable the activity collector.
            // AddGrainActivityCollector wires up grain call observation automatically.
            // CollectStorageActivityFromDefault enables observation for the default provider.
            // CollectStorageActivityFrom("Orders") shows how to observe a named provider too.
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
        {
            await cluster.DisposeAsync();
        }
    }

    [Fact]
    public async Task SubmitAsync_sets_status_to_submitted()
    {
        var grain = cluster!.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        // Trigger grain activity.
        await grain.SubmitAsync("laptop");

        // WaitForAssertionAsync retries the assertion each time grain activity
        // is detected (storage write or grain call), so the assertion runs
        // immediately, and again each time grain activity fires.
        await collector.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("submitted", await grain.GetStatusAsync());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitAsync_stores_item()
    {
        var grain = cluster!.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString());

        await grain.SubmitAsync("keyboard");

        // Grain-scoped variant: only activity from this specific grain retriggers the assertion.
        await collector.WaitForAssertionAsync(grain, async (g) =>
        {
            var status = await g.GetStatusAsync();
            Assert.NotNull(status);
            Assert.Equal("submitted", status);
        }, ct: TestContext.Current.CancellationToken);
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/GettingStartedSample.cs#L39-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-getting_started_test_class' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Notes:
- `InProcessTestCluster` starts a lightweight in-process Orleans silo — no external infrastructure needed.
- Use unique grain keys per test to avoid cross-test interference.
- The `WaitForAssertionAsync` call subscribes to activity before you trigger the operation. Signal delivery is signal-driven — no `Task.Delay` or polling loops.

## Using a shared assembly fixture

When many test classes share the same cluster, register it as an assembly fixture and have it
implement `IGrainActivityWaiter` so your tests can call `fixture.WaitForAssertionAsync(...)`
directly:

```csharp
// In one file: declare and register the fixture
[assembly: AssemblyFixture(typeof(OrleansTestClusterFixture))]

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

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken ct)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, ct);
}

// In each test class: inject the fixture
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

Notes:
- Assembly fixtures are shared across all test classes that inject them, so one cluster deployment supports the whole test suite.
- Use unique grain keys per test — `Guid.NewGuid().ToString()` or `CallerMemberName` + a GUID suffix both work well.
- Tests must not share mutable grain state across test methods.
