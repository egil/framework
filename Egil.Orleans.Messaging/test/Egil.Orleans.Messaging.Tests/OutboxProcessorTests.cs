using System.Collections.Immutable;
using Egil.Orleans.Testing;
using Orleans.Streams;

namespace Egil.Orleans.Messaging.Tests;

public sealed class OutboxProcessorTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public async Task PostInBackgroundAsync_delivers_outbox_envelope_through_stream_and_acknowledges()
    {
        var grainKey = Guid.NewGuid();
        var sink = fixture.GrainFactory.GetGrain<IOutboxProcessorSinkGrain>(grainKey);
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorSourceGrain>(grainKey);
        await sink.EnsureActiveAsync();

        await source.PublishInBackgroundAsync("price-changed");

        await fixture.WaitForAssertionAsync(
            sink,
            async () =>
            {
                var sinkState = await sink.GetStateAsync();
                Assert.Contains("price-changed", sinkState.ReceivedValues);
            },
            ct: TestContext.Current.CancellationToken);

        await fixture.WaitForAssertionAsync(
            source,
            async () =>
            {
                var sourceState = await source.GetStateAsync();
                Assert.Equal(1, sourceState.AcknowledgedCount);
                Assert.Equal(0, sourceState.Outbox?.Count ?? 0);
            },
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PostInBackgroundAsync_reports_no_postman_failure_and_reconciles_pending_item()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorNoPostmanGrain>(Guid.NewGuid());

        await source.PublishInBackgroundAsync("unhandled");

        await fixture.WaitForAssertionAsync(
            source,
            async () =>
            {
                var state = await source.GetStateAsync();
                Assert.Equal(nameof(NoPostmanRegisteredException), state.LastFailureType);
                Assert.Equal(1, state.FailedCount);
                Assert.Equal(0, state.Outbox?.Count ?? 0);
            },
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PostInBackgroundAsync_reports_postman_failure_and_reconciles_pending_item()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorFailingPostmanGrain>(Guid.NewGuid());

        await source.PublishInBackgroundAsync("boom");

        await fixture.WaitForAssertionAsync(
            source,
            async () =>
            {
                var state = await source.GetStateAsync();
                Assert.Equal(nameof(InvalidOperationException), state.LastFailureType);
                Assert.Equal(1, state.FailedCount);
                Assert.Equal(0, state.AcknowledgedCount);
                Assert.Equal(0, state.Outbox?.Count ?? 0);
            },
            ct: TestContext.Current.CancellationToken);
    }
}

internal static class OutboxProcessorTestNamespaces
{
    public const string Events = "outbox-processor-events";
}

public interface IOutboxProcessorSourceGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorNoPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorFailingPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorSinkGrain : IGrainWithGuidKey
{
    Task EnsureActiveAsync();

    Task<OutboxProcessorSinkState> GetStateAsync();
}

[GenerateSerializer]
public sealed class OutboxProcessorSourceState
{
    [Id(0)]
    public Outbox<OutboxProcessorTestEvent>? Outbox { get; set; }

    [Id(1)]
    public int AcknowledgedCount { get; set; }

    [Id(2)]
    public int FailedCount { get; set; }

    [Id(3)]
    public string? LastFailureType { get; set; }
}

[GenerateSerializer]
public sealed class OutboxProcessorSinkState
{
    [Id(0)]
    public MessageTracker Tracker { get; set; } = new();

    [Id(1)]
    public ImmutableArray<string> ReceivedValues { get; set; } = [];
}

[GenerateSerializer]
public sealed record OutboxProcessorTestEvent([property: Id(0)] string Value);

public sealed class OutboxProcessorSourceGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorSourceGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxMessageEnvelope<OutboxProcessorTestEvent>>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(PublishEnvelopeAsync);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishInBackgroundAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private Task PublishEnvelopeAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> envelope,
        CancellationToken cancellationToken)
    {
        var stream = this.GetStreamProvider(OutboxProcessorTestNamespaces.Events)
            .GetStream<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(
                StreamId.Create(OutboxProcessorTestNamespaces.Events, this.GetPrimaryKey()));

        _ = stream.OnNextAsync(envelope);
        return Task.CompletedTask;
    }

    private async ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken)
    {
        var outbox = EnsureOutbox();
        foreach (var item in items)
        {
            outbox = outbox.Remove(item.Token);
        }

        state.State.Outbox = outbox;
        state.State.AcknowledgedCount += items.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private async ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        var outbox = EnsureOutbox();
        foreach (var failure in failures)
        {
            outbox = outbox.Remove(failure.Item.Token);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
    }
}

public sealed class OutboxProcessorNoPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorNoPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxMessageEnvelope<OutboxProcessorTestEvent>>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        });

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishInBackgroundAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private async ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        var outbox = EnsureOutbox();
        foreach (var failure in failures)
        {
            outbox = outbox.Remove(failure.Item.Token);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
    }
}

public sealed class OutboxProcessorFailingPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorFailingPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxMessageEnvelope<OutboxProcessorTestEvent>>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(FailAsync);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishInBackgroundAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private static ValueTask FailAsync(OutboxMessageEnvelope<OutboxProcessorTestEvent> envelope)
    {
        throw new InvalidOperationException($"Cannot post {envelope.Message.Value}.");
    }

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private async ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        var outbox = EnsureOutbox();
        foreach (var failure in failures)
        {
            outbox = outbox.Remove(failure.Item.Token);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
    }
}

public sealed class OutboxProcessorSinkGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSinkState> state)
    : Grain, IOutboxProcessorSinkGrain
{
    private StreamManager? streamManager;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        streamManager = this.RegisterStreamManager(state.State.Tracker)
            .AddSubscription<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(
                OutboxProcessorTestNamespaces.Events,
                HandleEnvelopeAsync);

        await streamManager.SubscribeAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task<OutboxProcessorSinkState> GetStateAsync() => Task.FromResult(state.State);

    private async ValueTask HandleEnvelopeAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> envelope,
        StreamSequenceToken? token)
    {
        if (!state.State.Tracker.ProcessMessage(envelope.Token, out var next))
        {
            return;
        }

        state.State.Tracker = next;
        state.State.ReceivedValues = state.State.ReceivedValues.Add(envelope.Message.Value);
        await state.WriteStateAsync();
    }
}
