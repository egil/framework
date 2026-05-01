# Streams

## Explicit stream subscriptions

With explicit subscriptions, the subscriber grain calls `stream.SubscribeAsync(this)` to register for events. The test must subscribe the grain before publishing.

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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L18-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-explicit_stream_grain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test

<!-- snippet: explicit_stream_test -->
<a id='snippet-explicit_stream_test'></a>
```cs
[Fact]
public async Task Explicit_stream_delivers_message_to_subscriber_grain()
{
    var grainKey = Guid.NewGuid();
    var grain = fixture.GrainFactory.GetGrain<IExplicitListenerGrain>(grainKey);

    // Subscribe the grain to the stream first.
    await grain.SubscribeAsync();

    // Get the stream from the client so we can publish.
    var stream = fixture.GetStream<string>(StreamConstants.ExplicitNamespace, grainKey);

    // Start waiting *before* publishing so the collector does not miss events.
    var waitTask = fixture.WaitForAssertionAsync(
        async () => Assert.Equal("hello", await grain.GetLastMessageAsync()),
        timeout: TimeSpan.FromSeconds(2),
        ct: TestContext.Current.CancellationToken);

    // Publish a message — the grain's OnNextAsync writes state asynchronously.
    await stream.OnNextAsync("hello");

    await waitTask;
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L108-L132' title='Snippet source file'>snippet source</a> | <a href='#snippet-explicit_stream_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Key points:**
- Call `grain.SubscribeAsync()` before publishing — the grain must be subscribed to receive the message.
- Start `WaitForAssertionAsync` before `stream.OnNextAsync(...)` so the collector does not miss the resulting storage write.
- The grain's `OnNextAsync` writes state, triggering a storage activity signal that retries the assertion.

## Implicit stream subscriptions

With implicit subscriptions, Orleans automatically delivers stream events to grains decorated with `[ImplicitStreamSubscription(...)]`. The grain activates on first message delivery — no manual subscribe step is needed.

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
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L63-L97' title='Snippet source file'>snippet source</a> | <a href='#snippet-implicit_stream_grain' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Test

<!-- snippet: implicit_stream_test -->
<a id='snippet-implicit_stream_test'></a>
```cs
[Fact]
public async Task Implicit_stream_delivers_message_to_subscriber_grain()
{
    var grainKey = Guid.NewGuid();

    // Get the stream from the client.
    var stream = fixture.GetStream<string>(StreamConstants.ImplicitNamespace, grainKey);

    // The implicit listener grain activates automatically when the message arrives.
    var grain = fixture.GrainFactory.GetGrain<IImplicitListenerGrain>(grainKey);

    var waitTask = fixture.WaitForAssertionAsync(
        async () => Assert.Equal("world", await grain.GetLastMessageAsync()),
        timeout: TimeSpan.FromSeconds(2),
        ct: TestContext.Current.CancellationToken);

    await stream.OnNextAsync("world");

    await waitTask;
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/StreamSample.cs#L134-L155' title='Snippet source file'>snippet source</a> | <a href='#snippet-implicit_stream_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

**Key points:**
- No `SubscribeAsync` call needed — Orleans routes the message to the grain automatically.
- The implicit listener grain activates when the first message arrives and handles both stream delivery and queries via a single grain interface.
- `WaitForAssertionAsync` retries the assertion each time storage activity (from the implicit listener's `OnNextAsync` → `WriteStateAsync`) is detected.

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
