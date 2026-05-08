# Egil.Orleans.Testing

[![NuGet](https://img.shields.io/nuget/v/Egil.Orleans.Testing.svg)](https://www.nuget.org/packages/Egil.Orleans.Testing)

Deterministic async assertion helpers for [Microsoft Orleans](https://learn.microsoft.com/dotnet/orleans/) test suites.

## Overview

`Egil.Orleans.Testing` provides a `GrainActivityCollector` that monitors grain calls and storage operations during integration tests. Instead of arbitrary `Task.Delay` waits, your assertions are retried automatically each time the collector detects grain activity, making tests both fast and reliable.

The library is **test-framework-agnostic**: it works with xUnit, NUnit, MSTest, or any other framework. The examples below use xUnit syntax like `[Fact]` only for concreteness; the collector and waiter patterns are not tied to xUnit.

## TLDR

Once your Orleans test cluster is configured with a `GrainActivityCollector`, tests can trigger work on a grain and wait deterministically for the asynchronous follow-up work to complete:

```cs
public sealed class OrderGrainTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task StartSubmissionProcessingAsync_sets_last_submitted_item()
    {
        var grain = fixture.GetUniqueGrain<IOrderGrain>();

        // Starts async follow-up work on the grain, such as a one-way call,
        // grain timer, or reminder, and returns before that work completes.
        await grain.StartSubmissionProcessingAsync("laptop");

        // Retries the assertion only when this grain reports activity.
        await fixture.WaitForAssertionAsync(grain, async () =>
        {
            Assert.Equal("laptop", await grain.GetLastSubmittedItemAsync());
        });
    }
}
```

## Getting started

### 1. Install the package

```shell
dotnet add package Egil.Orleans.Testing
```

### 2. Inline setup in a test

The following is a complete example that builds an `InProcessTestCluster` directly in the test arrange step:

<!-- snippet: readme_inline_setup -->
<a id='snippet-readme_inline_setup'></a>
```cs
// -- Grain interface & state -------------------------------------------------

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

// -- Grain implementation ----------------------------------------------------

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

// -- Tests -------------------------------------------------------------------

/// <summary>
/// Example: build an <see cref="InProcessTestCluster"/> directly in the test arrange step.
/// </summary>
public sealed class OrderGrainInlineSetupTests
{
    [Fact]
    public async Task SubmitAsync_sets_last_submitted_item()
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

        var grain = cluster.Client.GetGrain<IOrderGrain>(Guid.NewGuid().ToString("N"));

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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/GettingStartedSample.cs#L6-L103' title='Snippet source file'>snippet source</a> | <a href='#snippet-readme_inline_setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### OrleansTestClusterFixture Reusable Helper

When many tests share the same cluster, it is convenient to wrap the cluster and collector in a reusable object that implements `IGrainActivityWaiter`. In xUnit this could be a class fixture or collection fixture. In other frameworks it could be any shared helper with setup and teardown.

<!-- snippet: orleans_test_cluster_fixture -->
<a id='snippet-orleans_test_cluster_fixture'></a>
```cs
/// <summary>
/// Minimal reusable Orleans test cluster fixture for the sample project.
/// Copy this into your own test project when several tests need the same cluster setup.
/// </summary>
/// <remarks>
/// The fixture combines four responsibilities:
/// 1. Own the lifecycle of an <see cref="InProcessTestCluster"/>.
/// 2. Expose a ready-to-use <see cref="IGrainFactory"/> for test code.
/// 3. Forward <see cref="IGrainActivityWaiter"/> calls to a <see cref="GrainActivityCollector"/>
///    so tests can call <c>fixture.WaitForAssertionAsync(...)</c> directly.
/// 4. Include optional stream support so stream-based samples can use the same shared fixture.
///
/// The protected hook methods let derived fixtures keep the common cluster setup while
/// adding feature-specific behavior such as deterministic reminder time control.
/// </remarks>
public class OrleansTestClusterFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    // The collector observes grain calls and, by default, storage writes inside the silo.
    // WaitForAssertionAsync uses those activity signals to know when to retry assertions.
    public GrainActivityCollector Collector { get; } = new();

    // Expose the client grain factory so tests do not need to reach into the cluster directly.
    public IGrainFactory GrainFactory => cluster?.Client ?? throw new InvalidOperationException("Test cluster not initialized.");

    /// <summary>
    /// Creates a unique <see cref="GrainId"/> for the current test method.
    /// </summary>
    /// <remarks>
    /// This is useful when a test needs a stable identifier that must be shared between
    /// a grain reference and some other Orleans concept such as a stream id or reminder name.
    /// The generated key includes the calling test method name and grain interface name,
    /// which makes copied snippets easier to reason about while still avoiding collisions.
    /// </remarks>
    public GrainId CreateUniqueGrainId<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName).GetGrainId();

    /// <summary>
    /// Gets a grain reference with a test-unique key.
    /// </summary>
    /// <remarks>
    /// Prefer this helper over hard-coded keys in sample-style tests.
    /// It keeps parallel tests isolated from each other and removes boilerplate
    /// around choosing the correct Orleans key type for the grain interface.
    /// </remarks>
    public TGrain GetUniqueGrain<TGrain>([CallerMemberName] string memberName = "")
        where TGrain : IGrain
        => CreateUniqueGrainReference<TGrain>(memberName);

    /// <summary>
    /// Gets a stream from the shared in-memory stream provider configured on the sample cluster.
    /// </summary>
    public IAsyncStream<T> GetStream<T>(string @namespace, Guid key)
    {
        var provider = cluster?.Client.GetStreamProvider(SampleClusterStreamDefaults.ProviderName)
            ?? throw new InvalidOperationException("Test cluster not initialized.");
        return provider.GetStream<T>(StreamId.Create(@namespace, key));
    }

    /// <summary>
    /// Controls whether the base fixture should observe writes to the <c>Default</c> grain storage provider.
    /// </summary>
    /// <remarks>
    /// Most sample fixtures should leave this enabled because storage writes are a strong signal for
    /// <see cref="IGrainActivityWaiter.WaitForAssertionAsync{TResult}"/>. Derived fixtures can turn it off
    /// when grain-call observation alone is sufficient.
    /// </remarks>
    protected virtual bool CollectStorageActivityFromDefault => true;

    public async ValueTask InitializeAsync()
    {
        // Build a one-silo in-process test cluster. Most samples only need one silo,
        // which keeps startup cost and overall test complexity low.
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        // Let derived fixtures register cluster-wide concerns before the common silo setup runs.
        // ReminderFixture uses this to attach a deterministic TimeProvider.
        ConfigureClusterBuilder(builder);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            // Register default infrastructure needed by the sample grains.
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(SampleClusterStreamDefaults.ProviderName);

            // AddGrainActivityCollector wires up grain call observation automatically.
            // Derived fixtures can opt out of the default storage observer if they only need call signals.
            var activityCollectorBuilder = siloBuilder.AddGrainActivityCollector(Collector);
            if (CollectStorageActivityFromDefault)
            {
                activityCollectorBuilder.CollectStorageActivityFromDefault();
            }

            // Let derived fixtures add their own silo services after the baseline test setup is in place.
            ConfigureSiloBuilder(siloBuilder);
        });

        builder.ConfigureClient(clientBuilder =>
        {
            clientBuilder.AddMemoryStreams(SampleClusterStreamDefaults.ProviderName);
            ConfigureClientBuilder(clientBuilder);
        });

        // Build first, then deploy. DeployAsync starts the silo and makes the client available.
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Give derived fixtures a chance to clean up resources that should go away
        // before the cluster itself is torn down. ReminderFixture uses this for its manual clock.
        await DisposeAsyncCore();

        // Always tear the cluster down after the test run so ports, timers, and other resources
        // are not kept alive across unrelated tests.
        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    // Forward the waiting API through the fixture so tests can stay focused on intent:
    //   await fixture.WaitForAssertionAsync(...)
    // instead of:
    //   await fixture.Collector.WaitForAssertionAsync(...)
    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);

    /// <summary>
    /// Allows a derived fixture to customize the <see cref="InProcessTestClusterBuilder"/>
    /// before the shared silo configuration is applied.
    /// </summary>
    protected virtual void ConfigureClusterBuilder(InProcessTestClusterBuilder builder)
    {
    }

    /// <summary>
    /// Allows a derived fixture to append feature-specific registrations to the silo.
    /// </summary>
    protected virtual void ConfigureSiloBuilder(ISiloBuilder siloBuilder)
    {
    }

    /// <summary>
    /// Allows a derived fixture to append feature-specific client registrations.
    /// </summary>
    protected virtual void ConfigureClientBuilder(IClientBuilder clientBuilder)
    {
    }

    /// <summary>
    /// Allows a derived fixture to dispose reminder clocks, streams, or other resources
    /// before the cluster itself is shut down.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    private TGrain CreateUniqueGrainReference<TGrain>(string memberName)
        where TGrain : IGrain
    {
        var grainType = typeof(TGrain);
        var grainName = grainType.Name;

        // Match Orleans key-shape conventions based on the grain interface marker.
        // This lets the same helper work for string, Guid, integer, and compound-key grains.
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/OrleansTestClusterFixture.cs#L13-L199' title='Snippet source file'>snippet source</a> | <a href='#snippet-orleans_test_cluster_fixture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: readme_fixture_usage -->
<a id='snippet-readme_fixture_usage'></a>
```cs
public sealed class OrderGrainFixtureTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task SubmitAsync_sets_last_submitted_item()
    {
        var grain = fixture.GetUniqueGrain<IOrderGrain>();
        await grain.SubmitAsync("monitor");

        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("monitor", await grain.GetLastSubmittedItemAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/GettingStartedSample.cs#L105-L120' title='Snippet source file'>snippet source</a> | <a href='#snippet-readme_fixture_usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

In xUnit, see the official docs for fixture registration options:
[Sharing Context between Tests](https://xunit.net/docs/shared-context)

## Features

- **Grain-scoped assertions** — pass a grain reference to restrict retriggers to that grain only. This is the best default when one grain owns the state you are asserting.
- **Standard assertions** — `WaitForAssertionAsync` without a grain scope retries on any detected grain activity (calls or storage operations). Useful when multiple grains contribute to the observed outcome.
- **IAsyncEnumerable feeds** — `GetStorageOperationsAsync` and `GetGrainCallsAsync` return `IAsyncEnumerable<T>` feeds that can be composed with LINQ operators such as `Where`, `Take`, and `Select` for fine-grained event observation. Use `includeExisting: true` to replay recent history.
- **Configurable timeout** — defaults to 5 seconds, overridable per call or via the `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` environment variable. Timeout is automatically bypassed when a debugger is attached.
- **Test-framework-agnostic** — no runtime dependency on any test framework.

## Recipes

See [docs/recipes](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing/docs/recipes/) for scenario-driven guides covering:

- [Assertion patterns](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing/docs/recipes/assertion-patterns.md)
- [Advanced assertions](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing/docs/recipes/advanced-assertions.md)
- [Timers and reminders](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing/docs/recipes/timers-and-reminders.md)
- [Streams](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing/docs/recipes/streams.md)

## License

[MIT](https://github.com/egil/framework/blob/main/LICENSE)
