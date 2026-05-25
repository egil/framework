using Microsoft.Extensions.Logging.Abstractions;
using Orleans.Providers.Streams.Common;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class StreamManagerResumeTests
{
    [Fact]
    public async Task EnsureExplicitSubscriptionsAsync_uses_tracker_resume_token_when_creating_subscription()
    {
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var manager = CreateManager(tracker, stream);

        await manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .EnsureExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EventSequenceToken(7), stream.SubscribeToken);
        Assert.Equal(1, stream.SubscribeCount);
    }

    [Fact]
    public async Task ResumeExplicitSubscriptionsAsync_uses_tracker_resume_token_when_resuming_existing_handle()
    {
        var handle = new FakeSubscriptionHandle<string>("provider-a", StreamId.Create("orders", "one"));
        var stream = new FakeStream<string>("provider-a", StreamId.Create("orders", "one"));
        stream.Handles.Add(handle);
        var tracker = CreateTracker("provider-a", "orders", sequenceNumber: 7);
        var manager = CreateManager(tracker, stream);

        await manager
            .ConfigureExplicitSubscription<string>("provider-a", "orders", static (_, _) => ValueTask.CompletedTask)
            .ResumeExplicitSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new EventSequenceToken(7), handle.ResumeToken);
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
        var manager = CreateManager(tracker, new FakeStream<string>("provider-a", streamId));

        await ((IStreamManagerComponent)manager
            .ConfigureImplicitSubscription<string>("orders", static (_, _) => ValueTask.CompletedTask))
            .OnSubscribedAsync(handleFactory);

        Assert.Equal(new EventSequenceToken(7), handleFactory.StringHandle.ResumeToken);
        Assert.Equal(1, handleFactory.StringHandle.ResumeCount);
    }

    private static StreamManager CreateManager<TEvent>(
        MessageTracker tracker,
        FakeStream<TEvent> stream)
    {
        var owner = new FakeGrainBase();
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
                return typedStream;
            }

            throw new InvalidOperationException($"Unexpected stream type '{typeof(T).FullName}'.");
        }
    }

    private sealed class FakeStream<T>(
        string providerName,
        StreamId streamId) : IAsyncStream<T>
    {
        public List<StreamSubscriptionHandle<T>> Handles { get; } = [];

        public int SubscribeCount { get; private set; }

        public StreamSequenceToken? SubscribeToken { get; private set; }

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
            SubscribeCount++;
            SubscribeToken = token;
            var handle = new FakeSubscriptionHandle<T>(providerName, streamId);
            Handles.Add(handle);

            return Task.FromResult<StreamSubscriptionHandle<T>>(handle);
        }

        public Task OnNextAsync(T item, StreamSequenceToken? token = null) => Task.CompletedTask;

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
    }

    private sealed class FakeSubscriptionHandle<T>(
        string providerName,
        StreamId streamId) : StreamSubscriptionHandle<T>
    {
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

    private sealed class FakeStreamSubscriptionHandleFactory(
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