using Orleans.Streams;
using System.Collections.Immutable;
using Egil.Orleans.Testing;
using Egil.Orleans.Messaging.Tests.Outboxes;
using Orleans.Concurrency;

namespace Egil.Orleans.Messaging.Tests.Streams;

public sealed class OutboxStreamRetryTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_acknowledgement_retries_without_repeating_the_durable_business_effect(bool customPostman)
    {
        var key = Guid.NewGuid();
        IStreamRetrySource source = customPostman ? fixture.GrainFactory.GetGrain<ICustomStreamRetrySource>(key)
            : fixture.GrainFactory.GetGrain<IStreamRetrySource>(key);
        var receiver = fixture.GrainFactory.GetGrain<IStreamRetryReceiver>(key);
        await receiver.ReadAsync();
        var publishing = source.PublishAsync(key, failAcknowledgement: true);
        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(1, (await receiver.ReadAsync()).Effects), ct: TestContext.Current.CancellationToken);
        await source.ReleaseAcknowledgementAsync();
        var firstActivation = await publishing;

        var retryActivation = await source.RetryAsync();

        Assert.NotEqual(firstActivation, retryActivation);
        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(2, (await receiver.ReadAsync()).Attempts), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await source.PendingAsync());
        var deliveries = await receiver.DeliveriesAsync();
        Assert.Equal(2, deliveries.Length);
        await receiver.DeactivateAsync();
        Assert.Equal(1, (await receiver.ReadAsync()).Effects);
        Assert.NotNull(deliveries[0].OutboxToken);
        Assert.Equal(deliveries[0].OutboxToken, deliveries[1].OutboxToken);
        Assert.NotEqual(deliveries[0].Token, deliveries[1].Token);
        await source.PublishAsync(key, failAcknowledgement: false);
        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(2, (await receiver.ReadAsync()).Effects), ct: TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task Concurrent_postman_groups_deliver_higher_then_lower_sequences_and_deduplicate_both_on_retry()
    {
        var key = Guid.NewGuid();
        var source = fixture.GrainFactory.GetGrain<IStreamRetrySource>(key);
        var receiver = fixture.GrainFactory.GetGrain<IStreamRetryReceiver>(key);
        await receiver.ReadAsync();
        var publishing = source.PublishPairAsync(key);
        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(2, (await receiver.ReadAsync()).Effects), ct: TestContext.Current.CancellationToken);
        var first = await receiver.DeliveriesAsync();
        Assert.Equal([2L, 1L], first.Select(cursor => cursor.OutboxToken!.SequenceNumber));
        await source.ReleaseAcknowledgementAsync();
        var originalActivation = await publishing;

        Assert.NotEqual(originalActivation, await source.RetryAsync());

        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(4, (await receiver.ReadAsync()).Attempts), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, await source.PendingAsync());
        await receiver.DeactivateAsync();
        Assert.Equal(2, (await receiver.ReadAsync()).Effects);
    }

    [Fact]
    public async Task Receiver_write_failure_leaves_the_identity_eligible_after_reactivation()
    {
        var key = Guid.NewGuid();
        var receiver = fixture.GrainFactory.GetGrain<IStreamRetryReceiver>(key);
        var initial = await receiver.ReadAsync();
        var stream = fixture.GetStream<StreamRetryEvent>(OutboxProcessorTestProviderNames.Events, "stream-retry", receiver.GetGrainId());
        var token = new OutboxSequenceToken(1, GrainId.Create("sender", key.ToString()), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        await receiver.FailNextCommitAsync();
        await stream.PublishFromOutboxAsync(new(key), token);
        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.NotEqual(initial.Activation, await ActivationAfterStorageFailureAsync(receiver)), ct: TestContext.Current.CancellationToken);
        Assert.Equal(0, (await receiver.ReadAsync()).Effects);

        await stream.PublishFromOutboxAsync(new(key), token);

        await fixture.WaitForAssertionAsync(receiver, async () =>
            Assert.Equal(1, (await receiver.ReadAsync()).Effects), ct: TestContext.Current.CancellationToken);
        await receiver.DeactivateAsync();
        Assert.Equal(1, (await receiver.ReadAsync()).Effects);
    }

    private static async Task<Guid> ActivationAfterStorageFailureAsync(IStreamRetryReceiver receiver)
    {
        try
        {
            return await receiver.ActivationAsync();
        }
        catch (InvalidOperationException error) when (error.Message == "Injected state write failure.")
        {
            // Orleans rejects RPCs queued on the failed activation with its deactivation reason.
            // One new RPC observes the replacement rather than reusing fenced state.
            return await receiver.ActivationAsync();
        }
    }

}

[System.Text.Json.Serialization.JsonPolymorphic]
[System.Text.Json.Serialization.JsonDerivedType(typeof(HighStreamRetryEvent), "high")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(LowStreamRetryEvent), "low")]
[GenerateSerializer]
public record StreamRetryEvent([property: Id(0)] Guid Target);

[GenerateSerializer]
public sealed record HighStreamRetryEvent(Guid Target) : StreamRetryEvent(Target);
[GenerateSerializer]
public sealed record LowStreamRetryEvent(Guid Target) : StreamRetryEvent(Target);

[GenerateSerializer]
public sealed record StreamRetrySourceState
{
    [Id(0)] public Outbox<StreamRetryEvent> Outbox { get; init; } = Outbox<StreamRetryEvent>.Create();
}

public interface IStreamRetrySource : IGrainWithGuidKey
{
    Task<Guid> PublishAsync(Guid target, bool failAcknowledgement);
    Task<Guid> PublishPairAsync(Guid target);
    [AlwaysInterleave] Task ReleaseAcknowledgementAsync();
    Task<Guid> RetryAsync();
    Task<int> PendingAsync();
}

public class StreamRetrySource : Grain, IStreamRetrySource, IOutboxGrain
{
    private readonly Guid activation = Guid.NewGuid();
    private readonly TaskCompletionSource releaseAcknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StreamRetryStorage<StreamRetrySourceState> storage;
    private readonly IStateManager<StreamRetrySourceState> state;
    private readonly OutboxProcessor<StreamRetryEvent> processor;
    private bool failAcknowledgement;

    public StreamRetrySource([PersistentState("stream-retry-source", "Payload")] IPersistentState<StreamRetrySourceState> storage)
        : this(storage, customPostman: false) { }

    protected StreamRetrySource(IPersistentState<StreamRetrySourceState> storage, bool customPostman)
    {
        this.storage = new StreamRetryStorage<StreamRetrySourceState>(storage);
        state = this.RegisterStateManager("Payload", this.storage);
        processor = this.RegisterOutboxProcessor(() => state.State.Outbox, options =>
        {
            options.RetryDelay = TimeSpan.FromMinutes(10);
            options.AcknowledgePostedAsync = AcknowledgeAsync;
        });
        var highPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.AddPostman<HighStreamRetryEvent>(async ValueTask (message, token) =>
        {
            await this.GetStreamProvider(OutboxProcessorTestProviderNames.Events).GetStream<StreamRetryEvent>(Destination(message))
                .PublishFromOutboxAsync(new(message.Target), token);
            highPublished.TrySetResult();
        }).AddPostman<LowStreamRetryEvent>(async ValueTask (message, token, cancellationToken) =>
        {
            await highPublished.Task.WaitAsync(cancellationToken);
            await this.GetStreamProvider(OutboxProcessorTestProviderNames.Events).GetStream<StreamRetryEvent>(Destination(message))
                .PublishFromOutboxAsync(new(message.Target), token);
        });
        if (customPostman)
            processor.AddPostman<StreamRetryEvent>(async Task (message, token) =>
            {
                await Task.Yield();
                await this.GetStreamProvider(OutboxProcessorTestProviderNames.Events).GetStream<StreamRetryEvent>(Destination(message))
                    .PublishFromOutboxAsync(message, token);
            });
        else
            processor.AddStreamPostman<StreamRetryEvent>(OutboxProcessorTestProviderNames.Events, Destination);
    }

    private StreamId Destination(StreamRetryEvent message) =>
        StreamManager.CreateStreamId("stream-retry", GrainFactory.GetGrain<IStreamRetryReceiver>(message.Target).GetGrainId());

    public async Task<Guid> PublishAsync(Guid target, bool failAcknowledgement)
    {
        this.failAcknowledgement = failAcknowledgement;
        await state.WriteAsync(state.State with { Outbox = state.State.Outbox.Add(new(target)) });
        return await PostWithFailedAcknowledgementAsync();
    }

    public async Task<Guid> PublishPairAsync(Guid target)
    {
        failAcknowledgement = true;
        await state.WriteAsync(state.State with
        {
            Outbox = state.State.Outbox.Add(new LowStreamRetryEvent(target)).Add(new HighStreamRetryEvent(target))
        });
        return await PostWithFailedAcknowledgementAsync();
    }

    private async Task<Guid> PostWithFailedAcknowledgementAsync()
    {
        try
        {
            await processor.PostAsync();
        }
        catch (InvalidOperationException) when (failAcknowledgement)
        {
            DeactivateOnIdle();
        }
        return activation;
    }

    public Task ReleaseAcknowledgementAsync()
    {
        releaseAcknowledgement.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task<Guid> RetryAsync()
    {
        await processor.PostAsync();
        return activation;
    }

    public Task<int> PendingAsync() => Task.FromResult(state.State.Outbox.Count);

    private async ValueTask AcknowledgeAsync(ImmutableArray<OutboxMessageEnvelope<StreamRetryEvent>> items, CancellationToken cancellationToken)
    {
        if (failAcknowledgement)
        {
            await releaseAcknowledgement.Task;
            storage.FailNextWrite = true;
        }
        await state.WriteAsync(state.State with { Outbox = state.State.Outbox.RemoveRange(items) }, cancellationToken);
    }


}

public interface ICustomStreamRetrySource : IStreamRetrySource;

public sealed class CustomStreamRetrySource([PersistentState("stream-retry-source", "Payload")] IPersistentState<StreamRetrySourceState> storage)
    : StreamRetrySource(storage, customPostman: true), ICustomStreamRetrySource;

[GenerateSerializer]
public sealed record StreamRetryReceiverState
{
    [Id(0)] public int Effects { get; init; }
    [Id(1)] public MessageTracker Tracker { get; init; } = new();
}

public interface IStreamRetryReceiver : IGrainWithGuidKey
{
    Task<(int Effects, int Attempts, Guid Activation)> ReadAsync();
    Task<Guid> ActivationAsync();
    Task<int> DeliverAggregateAsync(ImmutableArray<IBatchContainer> containers);
    Task FailNextCommitAsync();
    Task DeactivateAsync();
    Task<ImmutableArray<StreamCursor>> DeliveriesAsync();
}

public sealed class StreamRetryReceiver : Grain, IStreamRetryReceiver
{
    private readonly IStateManager<StreamRetryReceiverState> state;
    private readonly StreamRetryStorage<StreamRetryReceiverState> storage;
    private readonly Guid activation = Guid.NewGuid();
    private readonly StreamManager streams;
    private ImmutableArray<StreamCursor> deliveries = [];

    public StreamRetryReceiver([PersistentState("stream-retry-receiver", "Payload")] IPersistentState<StreamRetryReceiverState> storage)
    {
        this.storage = new(storage);
        state = this.RegisterStateManager("Payload", this.storage);
        streams = this.RegisterStreamManager(() => state.State.Tracker)
            .ConfigureExplicitSubscription<StreamRetryEvent>(OutboxProcessorTestProviderNames.Events, "stream-retry", ReceiveAsync,
                options => options.UseTrackedResumeToken = false);
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken) => streams.EnsureExplicitSubscriptionsAsync(cancellationToken);
    private async ValueTask ReceiveAsync(StreamRetryEvent message, StreamCursor cursor)
    {
        deliveries = deliveries.Add(cursor);
        if (!state.State.Tracker.TryAcceptMessage(cursor, out var next))
            return;
        await state.WriteAsync(state.State with { Effects = state.State.Effects + 1, Tracker = next });
    }
    public Task<(int Effects, int Attempts, Guid Activation)> ReadAsync() => Task.FromResult((state.State.Effects, deliveries.Length, activation));
    public async Task<int> DeliverAggregateAsync(ImmutableArray<IBatchContainer> containers)
    {
        // Discovery returns detached handles. Use the live handle retained by the manager,
        // whose observer is the one installed on this activation.
        var handles = (List<object>)typeof(StreamManager).GetField("subscriptionHandles",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(streams)!;
        var handle = Assert.IsAssignableFrom<StreamSubscriptionHandle<StreamRetryEvent>>(Assert.Single(handles));
        Assert.Contains("StreamSubscriptionHandleImpl", handle.GetType().Name);
        var aggregate = new BatchContainerBatch(containers.ToList());
        // The public handle has no provider delivery API. Invoke Orleans' actual consumer
        // entry point so the contract test exercises its per-container context switching.
        var deliver = handle.GetType().GetMethod("DeliverBatch")!;
        await (Task)deliver.Invoke(handle, [aggregate, null])!;
        return aggregate.BatchContainers.Count;
    }
    public Task<Guid> ActivationAsync() => Task.FromResult(activation);
    public Task FailNextCommitAsync()
    {
        storage.FailNextWrite = true;
        return Task.CompletedTask;
    }
    public Task<ImmutableArray<StreamCursor>> DeliveriesAsync() => Task.FromResult(deliveries);
    public Task DeactivateAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}



// Fault only storage writes; actual stream publication, serialization, and grain reactivation remain real.
    internal sealed class StreamRetryStorage<T>(IPersistentState<T> inner) : IPersistentState<T>
    {
        public bool FailNextWrite { get; set; }
        public T State { get => inner.State; set => inner.State = value; }
        public string? Etag => inner.Etag;
        public bool RecordExists => inner.RecordExists;
        public Task ReadStateAsync() => inner.ReadStateAsync();
        public Task ClearStateAsync() => inner.ClearStateAsync();
        public Task WriteStateAsync()
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("Injected state write failure.");
            }
            return inner.WriteStateAsync();
        }
        public Task ReadStateAsync(CancellationToken cancellationToken) => ReadStateAsync();
        public Task WriteStateAsync(CancellationToken cancellationToken) => WriteStateAsync();
        public Task ClearStateAsync(CancellationToken cancellationToken) => ClearStateAsync();
    }
