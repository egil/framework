# Streams

## Explicit stream subscriptions

With explicit subscriptions, the subscriber grain calls `stream.SubscribeAsync(this)` to register for events. The test must subscribe the grain before publishing.

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleans-test-cluster-fixture)

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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L19-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-explicit_stream_grain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test

<!-- snippet: explicit_stream_test -->
<a id='snippet-explicit_stream_test'></a>
```cs
public sealed class ExplicitStreamTests(StreamFixture fixture) : IClassFixture<StreamFixture>
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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L107-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-explicit_stream_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Key points:**
- Call `grain.SubscribeAsync()` before publishing — the grain must be subscribed to receive the message.
- Trigger the stream action first, then `await WaitForAssertionAsync(...)` as the assert step.
- The grain's `OnNextAsync` writes state, triggering a storage activity signal that retries the assertion.

## Implicit stream subscriptions

With implicit subscriptions, Orleans automatically delivers stream events to grains decorated with `[ImplicitStreamSubscription(...)]`. The grain activates on first message delivery — no manual subscribe step is needed.

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleans-test-cluster-fixture)

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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L64-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-implicit_stream_grain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test

<!-- snippet: implicit_stream_test -->
<a id='snippet-implicit_stream_test'></a>
```cs
public sealed class ImplicitStreamTests(StreamFixture fixture) : IClassFixture<StreamFixture>
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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L134-L155' title='Snippet source file'>snippet source</a> | <a href='#snippet-implicit_stream_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Key points:**
- No `SubscribeAsync` call needed — Orleans routes the message to the grain automatically.
- The implicit listener grain activates when the first message arrives and handles both stream delivery and queries via a single grain interface.
- `WaitForAssertionAsync` can be used as the assert step after publishing; it retries each time storage activity (from the implicit listener's `OnNextAsync` → `WriteStateAsync`) is detected.

## Fixture setup

Both explicit and implicit stream tests need a stream provider registered on the silo **and** the client:

```csharp
builder.ConfigureSilo((_, siloBuilder) =>
{
    siloBuilder.AddMemoryGrainStorage("Default");
    siloBuilder.AddMemoryGrainStorage("PubSubStore");
    siloBuilder.AddMemoryStreams("StreamProvider");
    siloBuilder.AddGrainActivityCollector(collector)
        .CollectStorageActivityFromDefault();
});
builder.ConfigureClient(clientBuilder =>
    clientBuilder.AddMemoryStreams("StreamProvider"));
```

- **`PubSubStore`** — required by Orleans for implicit subscription metadata.
- **`AddMemoryStreams`** on both silo and client — the client needs the provider to publish messages, and the silo needs it to deliver them to grains.
- Keep stream tests on a normal fixture with real time. Do not share a reminder-specific fixture or `ManualTimeProvider` with stream tests, because frozen time can stall Orleans stream internals.
