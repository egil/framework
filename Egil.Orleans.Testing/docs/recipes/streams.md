# Streams

## Explicit stream subscriptions

With explicit subscriptions, the subscriber grain calls `stream.SubscribeAsync(this)` to register for events. The test must subscribe the grain before publishing.

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

### Subscriber grain

<!-- snippet: explicit_stream_grain -->
<a id='snippet-explicit_stream_grain'></a>
```cs
public interface IExplicitListenerGrain : IGrainWithGuidKey
{
    Task SubscribeAsync();

    Task<string?> GetLastMessageAsync();
}

public sealed class ExplicitListenerGrain(
    [PersistentState("state", "Default")] IPersistentState<ListenerState> state,
    IGrainContext grainContext)
    : Grain, IExplicitListenerGrain, IAsyncObserver<string>
{
    private StreamSubscriptionHandle<string>? subscription;

    public async Task SubscribeAsync()
    {
        if (subscription is not null)
        {
            return;
        }

        var provider = grainContext.ActivationServices
            .GetRequiredKeyedService<IStreamProvider>(StreamConstants.StreamProviderName);
        var stream = provider.GetStream<string>(
            StreamId.Create(StreamConstants.ExplicitNamespace, this.GetPrimaryKey()));
        subscription = await stream.SubscribeAsync(this);
    }

    public Task<string?> GetLastMessageAsync() => Task.FromResult(state.State.LastMessage);

    public async Task OnNextAsync(string item, StreamSequenceToken? token = null)
    {
        state.State.LastMessage = item;
        await state.WriteStateAsync();
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex) => Task.FromException(ex);
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L17-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-explicit_stream_grain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test

<!-- snippet: explicit_stream_test -->
<a id='snippet-explicit_stream_test'></a>
```cs
public sealed class ExplicitStreamTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task Explicit_stream_delivers_message_to_subscriber_grain()
    {
        var grain = fixture.GetUniqueGrain<IExplicitListenerGrain>();
        var grainKey = grain.GetPrimaryKey();

        // Subscribe the grain to the stream first.
        await grain.SubscribeAsync();

        // Get the stream from the client so we can publish.
        var stream = fixture.GetStream<string>(StreamConstants.ExplicitNamespace, grainKey);

        // Publish a message — the grain's OnNextAsync writes state asynchronously.
        await stream.OnNextAsync("hello");

        // Assert after triggering the action. WaitForAssertionAsync retries until the async delivery completes.
        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("hello", await grain.GetLastMessageAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L105-L130' title='Snippet source file'>snippet source</a> | <a href='#snippet-explicit_stream_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Key points:**
- Call `grain.SubscribeAsync()` before publishing — the grain must be subscribed to receive the message.
- Trigger the stream action first, then `await WaitForAssertionAsync(...)` as the assert step.
- The grain's `OnNextAsync` writes state, triggering a storage activity signal that retries the assertion.

## Implicit stream subscriptions

With implicit subscriptions, Orleans automatically delivers stream events to grains decorated with `[ImplicitStreamSubscription(...)]`. The grain activates on first message delivery — no manual subscribe step is needed.

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleanstestclusterfixture-reusable-helper)

### Subscriber grain

<!-- snippet: implicit_stream_grain -->
<a id='snippet-implicit_stream_grain'></a>
```cs
public interface IImplicitListenerGrain : IGrainWithGuidKey
{
    Task<string?> GetLastMessageAsync();
}

[ImplicitStreamSubscription(StreamConstants.ImplicitNamespace)]
public sealed class ImplicitListenerGrain(
    [PersistentState("state", "Default")] IPersistentState<ListenerState> state,
    IGrainContext grainContext)
    : Grain, IImplicitListenerGrain, IAsyncObserver<string>
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var provider = grainContext.ActivationServices
            .GetRequiredKeyedService<IStreamProvider>(StreamConstants.StreamProviderName);
        var stream = provider.GetStream<string>(
            StreamId.Create(StreamConstants.ImplicitNamespace, this.GetPrimaryKey()));
        await stream.SubscribeAsync(this);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task<string?> GetLastMessageAsync() => Task.FromResult(state.State.LastMessage);

    public async Task OnNextAsync(string item, StreamSequenceToken? token = null)
    {
        state.State.LastMessage = item;
        await state.WriteStateAsync();
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public Task OnErrorAsync(Exception ex) => Task.FromException(ex);
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L62-L96' title='Snippet source file'>snippet source</a> | <a href='#snippet-implicit_stream_grain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test

<!-- snippet: implicit_stream_test -->
<a id='snippet-implicit_stream_test'></a>
```cs
public sealed class ImplicitStreamTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task Implicit_stream_delivers_message_to_subscriber_grain()
    {
        var grain = fixture.GetUniqueGrain<IImplicitListenerGrain>();
        var grainKey = grain.GetPrimaryKey();

        // Get the stream from the client.
        var stream = fixture.GetStream<string>(StreamConstants.ImplicitNamespace, grainKey);

        // The implicit listener grain activates automatically when the message arrives.
        await stream.OnNextAsync("world");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("world", await grain.GetLastMessageAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L132-L153' title='Snippet source file'>snippet source</a> | <a href='#snippet-implicit_stream_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Key points:**
- No `SubscribeAsync` call needed — Orleans routes the message to the grain automatically.
- The implicit listener grain activates when the first message arrives and handles both stream delivery and queries via a single grain interface.
- `WaitForAssertionAsync` can be used as the assert step after publishing; it retries each time storage activity (from the implicit listener's `OnNextAsync` → `WriteStateAsync`) is detected.

## Fixture setup

Both explicit and implicit stream tests can use the same `OrleansTestClusterFixture` pattern as the rest of the samples, extended with stream provider setup plus a `GetStream<T>(...)` helper:

<!-- snippet: stream_fixture -->
<a id='snippet-stream_fixture'></a>
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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/OrleansTestClusterFixture.cs#L12-L200' title='Snippet source file'>snippet source</a> | <a href='#snippet-stream_fixture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

- **`PubSubStore`** — required by Orleans for implicit subscription metadata.
- **`AddMemoryStreams`** on both silo and client — the client needs the provider to publish messages, and the silo needs it to deliver them to grains.
- **`GetStream<T>(...)`** — keeps stream lookup logic in the fixture so individual tests stay focused on the behavior under test.
- Keep stream tests on a normal fixture with real time. Do not share a reminder-specific fixture or `ManualTimeProvider` with stream tests, because frozen time can stall Orleans stream internals.
