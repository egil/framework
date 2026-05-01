using Microsoft.Extensions.DependencyInjection;
using Orleans.Streams;
using Orleans.TestingHost;

namespace Egil.Orleans.Testing.Samples.Streams;

// -- Constants ---------------------------------------------------------------

internal static class StreamConstants
{
    public const string StreamProviderName = "StreamProvider";
    public const string ExplicitNamespace = "explicit-ns";
    public const string ImplicitNamespace = "implicit-ns";
}

// -- Explicit stream grain ---------------------------------------------------

#region explicit_stream_grain
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
#endregion

// -- Implicit stream grain ---------------------------------------------------

#region implicit_stream_grain
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
#endregion

public sealed class ListenerState
{
    public string? LastMessage { get; set; }
}

// -- Tests -------------------------------------------------------------------

#region explicit_stream_test
public sealed class ExplicitStreamTests(StreamFixture fixture) : IClassFixture<StreamFixture>
{
    [Fact]
    public async Task Explicit_stream_delivers_message_to_subscriber_grain()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IExplicitListenerGrain>(grainKey);

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
#endregion

#region implicit_stream_test
public sealed class ImplicitStreamTests(StreamFixture fixture) : IClassFixture<StreamFixture>
{
    [Fact]
    public async Task Implicit_stream_delivers_message_to_subscriber_grain()
    {
        var grainKey = Guid.NewGuid();

        // Get the stream from the client.
        var stream = fixture.GetStream<string>(StreamConstants.ImplicitNamespace, grainKey);

        // The implicit listener grain activates automatically when the message arrives.
        var grain = fixture.GrainFactory.GetGrain<IImplicitListenerGrain>(grainKey);

        await stream.OnNextAsync("world");

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("world", await grain.GetLastMessageAsync()),
            timeout: TimeSpan.FromSeconds(2),
            ct: TestContext.Current.CancellationToken);
    }
}
#endregion

// -- Fixture -----------------------------------------------------------------

public sealed class StreamFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public IGrainFactory GrainFactory => cluster!.Client;

    public IAsyncStream<T> GetStream<T>(string @namespace, Guid key)
    {
        var provider = cluster!.Client.GetStreamProvider(StreamConstants.StreamProviderName);
        return provider.GetStream<T>(StreamId.Create(@namespace, key));
    }

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);
        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryStreams(StreamConstants.StreamProviderName);
            siloBuilder.AddGrainActivityCollector(Collector)
                .CollectStorageActivityFromDefault();
        });
        builder.ConfigureClient(clientBuilder =>
            clientBuilder.AddMemoryStreams(StreamConstants.StreamProviderName));
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

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);
}
