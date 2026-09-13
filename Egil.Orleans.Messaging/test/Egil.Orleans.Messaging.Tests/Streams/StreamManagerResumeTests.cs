using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerResumeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Distinct_explicit_subscription_sources_can_be_configured(bool explicitId)
    {
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var manager = CreateManager(null, stream);
        Func<string, string, StreamManager> configure = explicitId
            ? (provider, name) => manager.ConfigureExplicitSubscription<string>(provider, StreamId.Create(name, "one"), static (_, _) => ValueTask.CompletedTask)
            : (provider, name) => manager.ConfigureExplicitSubscription<string>(provider, name, static (_, _) => ValueTask.CompletedTask);
        configure("provider-a", "orders");

        configure("provider-b", "orders");
        var configured = configure("provider-a", "other");

        Assert.Same(manager, configured);
    }

    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 9)]
    public async Task Legacy_position_resumes_subscription_unless_provider_has_its_own_position(bool hasProviderPosition, long expectedSequence)
    {
        var tracker = new MessageTracker();
        tracker.ProcessMessage(new StreamCursor("orders", new EventSequenceToken(7)), out tracker);
        if (hasProviderPosition)
        {
            tracker.ProcessMessage(new StreamCursor("orders", new EventSequenceToken(9), "provider-a"), out tracker);
        }
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var manager = CreateManager(() => tracker, stream);

        await manager.ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EventSequenceToken(expectedSequence), stream.SubscribeToken);
    }

    [Fact]
    public async Task Subscription_uses_tracker_adopted_after_registration()
    {
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 1);
        var manager = CreateManager(() => tracker, stream);
        tracker = CreateTracker("provider-a", "orders", sequenceNumber: 9);

        await manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EventSequenceToken(9), stream.SubscribeToken);
    }

    [Fact]
    public async Task EnsureExplicitSubscriptionsAsync_uses_tracker_resume_token_when_creating_subscription()
    {
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var manager = CreateManager(() => tracker, stream);

        await manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EventSequenceToken(7), stream.SubscribeToken);
        Assert.Equal(1, stream.SubscribeCount);
    }

    [Fact]
    public async Task EnsureExplicitSubscriptionsAsync_uses_null_resume_token_when_tracker_is_omitted()
    {
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var manager = CreateManager(null, stream);

        await manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Null(stream.SubscribeToken);
        Assert.Equal(1, stream.SubscribeCount);
    }

    [Fact]
    public async Task EnsureExplicitSubscriptionsAsync_uses_null_resume_token_when_subscription_opts_out()
    {
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var manager = CreateManager(() => tracker, stream);

        await manager
            .ConfigureExplicitSubscription<string>(
                "provider-a",
                "orders",
                static (_, _) => ValueTask.CompletedTask,
                useTrackedResumeToken: false)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Null(stream.SubscribeToken);
        Assert.Equal(1, stream.SubscribeCount);
    }

    [Fact]
    public async Task EnsureExplicitSubscriptionsAsync_uses_configured_stream_id()
    {
        var streamId = StreamId.Create("orders", "external");
        var stream = new FakeStream<string>("provider-a", streamId);
        var manager = CreateManager(null, stream);

        await manager
            .ConfigureExplicitSubscription<string>(
                "provider-a",
                streamId,
                static (_, _) => ValueTask.CompletedTask)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(streamId, stream.ResolvedStreamId);
        Assert.Equal(1, stream.SubscribeCount);
    }

    [Fact]
    public async Task ResumeExplicitSubscriptionsAsync_uses_tracker_resume_token_when_resuming_existing_handle()
    {
        var handle = new FakeSubscriptionHandle<string>("provider-a", StreamId.Create("orders", "one"));
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        stream.Handles.Add(handle);
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var manager = CreateManager(() => tracker, stream);

        await manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .ResumeExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EventSequenceToken(7), handle.ResumeToken);
        Assert.Equal(1, handle.ResumeCount);
        Assert.Equal(0, stream.SubscribeCount);
    }

    [Fact]
    public async Task ResumeExplicitSubscriptionsAsync_uses_null_resume_token_when_subscription_opts_out()
    {
        var handle = new FakeSubscriptionHandle<string>("provider-a", StreamId.Create("orders", "one"));
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        stream.Handles.Add(handle);
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var manager = CreateManager(() => tracker, stream);

        await manager
            .ConfigureExplicitSubscription<string>(
                "provider-a",
                "orders",
                static (_, _) => ValueTask.CompletedTask,
                useTrackedResumeToken: false)
            .ResumeExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Null(handle.ResumeToken);
        Assert.Equal(1, handle.ResumeCount);
        Assert.Equal(0, stream.SubscribeCount);
    }

    [Fact]
    public async Task Implicit_subscription_resume_uses_tracker_resume_token()
    {
        var streamId = StreamId.Create("orders", "one");
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var handleFactory = new FakeStreamSubscriptionHandleFactory(
            "provider-a",
            streamId,
            new FakeSubscriptionHandle<string>("provider-a", streamId));
        var manager = CreateManager(() => tracker, new FakeStream<string>("provider-a", streamId));

        await ((IStreamManagerComponent)manager
            .ConfigureImplicitSubscription<string>("orders", static (_, _) => ValueTask.CompletedTask))
            .OnSubscribedAsync(handleFactory);

        Assert.Equal(new EventSequenceToken(7), handleFactory.StringHandle.ResumeToken);
        Assert.Equal(1, handleFactory.StringHandle.ResumeCount);
    }

    [Fact]
    public async Task Implicit_subscription_resume_uses_null_resume_token_when_subscription_opts_out()
    {
        var streamId = StreamId.Create("orders", "one");
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var handleFactory = new FakeStreamSubscriptionHandleFactory(
            "provider-a",
            streamId,
            new FakeSubscriptionHandle<string>("provider-a", streamId));
        var manager = CreateManager(() => tracker, new FakeStream<string>("provider-a", streamId));

        await ((IStreamManagerComponent)manager
            .ConfigureImplicitSubscription<string>(
                "orders",
                static (_, _) => ValueTask.CompletedTask,
                useTrackedResumeToken: false))
            .OnSubscribedAsync(handleFactory);

        Assert.Null(handleFactory.StringHandle.ResumeToken);
        Assert.Equal(1, handleFactory.StringHandle.ResumeCount);
    }

    [Fact]
    public async Task Implicit_subscription_without_configured_handler_throws()
    {
        var streamId = StreamId.Create("orders", "one");
        var handleFactory = new FakeStreamSubscriptionHandleFactory(
            "provider-a",
            streamId,
            new FakeSubscriptionHandle<string>("provider-a", streamId));
        var manager = CreateManager(null, new FakeStream<string>("provider-a", streamId));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IStreamManagerComponent)manager).OnSubscribedAsync(handleFactory));

        Assert.Contains("No implicit stream subscription handler is configured", exception.Message);
        Assert.Contains("provider-a", exception.Message);
    }

    internal static StreamManager CreateManager<TEvent>(
        Func<MessageTracker?>? tracker,
        FakeStream<TEvent> stream,
        IGrainBase? owner = null)
    {
        owner ??= new FakeGrainBase();
        return StreamManager.Create(
            owner,
            tracker,
            _ => new FakeStreamProvider<TEvent>(stream),
            streamNamespace => StreamId.Create(streamNamespace, "one"),
            NullLogger<StreamManager>.Instance);
    }

    private static MessageTracker CreateTracker(
        string providerName,
        string streamNamespace,
        long sequenceNumber)
    {
        var tracker = new MessageTracker();
        tracker.ProcessMessage(
            new StreamCursor(
                streamNamespace,
                new EventSequenceToken(sequenceNumber),
                providerName),
            out tracker);

        return tracker;
    }

    private sealed class FakeStreamProvider<TEvent>(FakeStream<TEvent> stream) : IStreamProvider
    {
        public string Name => stream.ProviderName;

        public bool IsRewindable => stream.IsRewindable;

        public IAsyncStream<T> GetStream<T>(StreamId streamId)
        {
            if (stream is IAsyncStream<T> typedStream)
            {
                stream.MarkResolved(streamId);
                return typedStream;
            }

            throw new InvalidOperationException($"Unexpected stream type '{typeof(T).FullName}'.");
        }
    }

    internal sealed class FakeStream<T>(
        string providerName,
        StreamId streamId) : IAsyncStream<T>
    {
        public List<StreamSubscriptionHandle<T>> Handles { get; } = [];

        private IAsyncObserver<T>? observer;

        public int SubscribeCount { get; private set; }

        public StreamSequenceToken? SubscribeToken { get; private set; }

        public StreamId? ResolvedStreamId { get; private set; }

        public bool IsRewindable => true;

        public string ProviderName => providerName;

        public StreamId StreamId => streamId;

        public Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles() =>
            Task.FromResult<IList<StreamSubscriptionHandle<T>>>(Handles);

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer)
        {
            return SubscribeAsync(observer, token: null, filterData: null);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
            IAsyncObserver<T> observer,
            StreamSequenceToken? token,
            string? filterData = null)
        {
            this.observer = observer;
            SubscribeCount++;
            SubscribeToken = token;
            var handle = new FakeSubscriptionHandle<T>(providerName, streamId);
            Handles.Add(handle);

            return Task.FromResult<StreamSubscriptionHandle<T>>(handle);
        }

        public Task OnNextAsync(T item, StreamSequenceToken? token = null) =>
            (observer ?? throw new InvalidOperationException("No subscriber attached.")).OnNextAsync(item, token);

        public Task OnNextBatchAsync(IEnumerable<T> batch, StreamSequenceToken? token = null) => Task.CompletedTask;

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncBatchObserver<T> observer)
        {
            return SubscribeAsync(observer, token: null);
        }

        public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
            IAsyncBatchObserver<T> observer,
            StreamSequenceToken? token)
        {
            SubscribeCount++;
            SubscribeToken = token;
            var handle = new FakeSubscriptionHandle<T>(providerName, streamId);
            Handles.Add(handle);

            return Task.FromResult<StreamSubscriptionHandle<T>>(handle);
        }

        public int CompareTo(IAsyncStream<T>? other) =>
            string.Compare(StreamId.ToString(), other?.StreamId.ToString(), StringComparison.Ordinal);

        public bool Equals(IAsyncStream<T>? other) =>
            other is not null
            && string.Equals(ProviderName, other.ProviderName, StringComparison.Ordinal)
            && StreamId.Equals(other.StreamId);

        public void MarkResolved(StreamId streamId)
        {
            if (!StreamId.Equals(streamId))
            {
                throw new InvalidOperationException($"Unexpected stream id '{streamId}'.");
            }

            ResolvedStreamId = streamId;
        }
    }

    internal sealed class FakeSubscriptionHandle<T>(
        string providerName,
        StreamId streamId) : StreamSubscriptionHandle<T>
    {
        private IAsyncObserver<T>? observer;

        public Task DeliverAsync(T item) =>
            (observer ?? throw new InvalidOperationException("No subscriber attached.")).OnNextAsync(item);

        public int ResumeCount { get; private set; }

        public StreamSequenceToken? ResumeToken { get; private set; }

        public override StreamId StreamId => streamId;

        public override string ProviderName => providerName;

        public override Guid HandleId { get; } = Guid.NewGuid();

        public override Task UnsubscribeAsync() => Task.CompletedTask;

        public override Task<StreamSubscriptionHandle<T>> ResumeAsync(
            IAsyncObserver<T> observer,
            StreamSequenceToken? token)
        {
            this.observer = observer;
            ResumeCount++;
            ResumeToken = token;
            return Task.FromResult<StreamSubscriptionHandle<T>>(this);
        }

        public override Task<StreamSubscriptionHandle<T>> ResumeAsync(
            IAsyncBatchObserver<T> observer,
            StreamSequenceToken? token)
        {
            ResumeCount++;
            ResumeToken = token;
            return Task.FromResult<StreamSubscriptionHandle<T>>(this);
        }

        public override bool Equals(StreamSubscriptionHandle<T>? other) =>
            other?.HandleId == HandleId;
    }

    internal sealed class FakeStreamSubscriptionHandleFactory(
        string providerName,
        StreamId streamId,
        FakeSubscriptionHandle<string> stringHandle) : IStreamSubscriptionHandleFactory
    {
        public FakeSubscriptionHandle<string> StringHandle => stringHandle;

        public StreamId StreamId => streamId;

        public string ProviderName => providerName;

        public GuidId SubscriptionId => GuidId.GetGuidId(Guid.NewGuid());

        public StreamSubscriptionHandle<T> Create<T>()
        {
            if (stringHandle is StreamSubscriptionHandle<T> typedHandle)
            {
                return typedHandle;
            }

            throw new InvalidOperationException($"Unexpected stream type '{typeof(T).FullName}'.");
        }
    }

    private sealed class FakeGrainBase : IGrainBase
    {
        public IGrainContext GrainContext =>
            throw new InvalidOperationException("The fake StreamManager tests do not use the grain context.");

        public Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}