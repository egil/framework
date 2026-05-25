using System.Collections.Immutable;
using Egil.Orleans.Testing;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxPostmanHelperTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public async Task Stream_postman_delivers_outbox_item_and_acknowledges()
    {
        var grainKey = Guid.NewGuid();
        var sink = fixture.GrainFactory.GetGrain<IOutboxProcessorSinkGrain>(grainKey);
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorStreamPostmanGrain>(grainKey);
        await sink.EnsureActiveAsync();

        await source.PublishInBackgroundAsync("stream-helper");

        await fixture.WaitForAssertionAsync(
            sink,
            async () =>
            {
                var sinkState = await sink.GetStateAsync();
                Assert.Contains("stream-helper", sinkState.ReceivedValues);
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
    public async Task Projected_stream_postman_delivers_projected_event_and_acknowledges()
    {
        var grainKey = Guid.NewGuid();
        var sink = fixture.GrainFactory.GetGrain<IOutboxProcessorProjectedSinkGrain>(grainKey);
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorProjectedStreamPostmanGrain>(grainKey);
        await sink.EnsureActiveAsync();

        await source.PublishInBackgroundAsync("projected-stream-helper");

        await fixture.WaitForAssertionAsync(
            sink,
            async () =>
            {
                var sinkState = await sink.GetStateAsync();
                Assert.Contains("projected-stream-helper", sinkState.ReceivedValues);
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
    public async Task Grain_postman_delivers_to_resolved_grain_and_acknowledges()
    {
        var grainKey = Guid.NewGuid();
        var target = fixture.GrainFactory.GetGrain<IOutboxProcessorGrainPostmanTargetGrain>(grainKey);
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorGrainPostmanSourceGrain>(grainKey);

        await source.PublishInBackgroundAsync("grain-helper");

        await fixture.WaitForAssertionAsync(
            target,
            async () =>
            {
                var targetState = await target.GetStateAsync();
                Assert.Contains("grain-helper", targetState.ReceivedValues);
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
}

public interface IOutboxProcessorStreamPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorProjectedStreamPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorProjectedSinkGrain : IGrainWithGuidKey
{
    Task EnsureActiveAsync();

    Task<OutboxProcessorSinkState> GetStateAsync();
}

public interface IOutboxProcessorGrainPostmanSourceGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorGrainPostmanTargetGrain : IGrainWithGuidKey
{
    Task ReceiveAsync(string value);

    Task<OutboxProcessorSinkState> GetStateAsync();
}

public sealed class OutboxProcessorStreamPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorStreamPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(CreateOptions())
            .AddStreamPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(
                OutboxProcessorTestProviderNames.Events,
                envelope => StreamId.Create(OutboxProcessorTestNamespaces.Events, this.GetPrimaryKey()));

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

    private OutboxProcessorOptions<OutboxMessageEnvelope<OutboxProcessorTestEvent>> CreateOptions() => new()
    {
        PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
        AcknowledgePostedAsync = AcknowledgePostedAsync,
        ReconcileFailedAsync = ReconcileFailedAsync,
        RetryDelay = TimeSpan.FromMilliseconds(100)
    };

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

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
}

public sealed class OutboxProcessorProjectedStreamPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorProjectedStreamPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(CreateOptions())
            .AddStreamPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>, OutboxProcessorTestEvent>(
                OutboxProcessorTestProviderNames.Events,
                envelope => StreamId.Create(OutboxProcessorTestNamespaces.Events, this.GetPrimaryKey()),
                envelope => envelope.Message);

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

    private OutboxProcessorOptions<OutboxMessageEnvelope<OutboxProcessorTestEvent>> CreateOptions() => new()
    {
        PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
        AcknowledgePostedAsync = AcknowledgePostedAsync,
        ReconcileFailedAsync = ReconcileFailedAsync,
        RetryDelay = TimeSpan.FromMilliseconds(100)
    };

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

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
}

public sealed class OutboxProcessorProjectedSinkGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSinkState> state)
    : Grain, IOutboxProcessorProjectedSinkGrain
{
    private StreamManager? streamManager;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        streamManager = this.RegisterStreamManager(state.State.Tracker)
            .ConfigureExplicitSubscription<OutboxProcessorTestEvent>(
                OutboxProcessorTestProviderNames.Events,
                OutboxProcessorTestNamespaces.Events,
                HandleEventAsync);

        await streamManager.EnsureExplicitSubscriptionsAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task<OutboxProcessorSinkState> GetStateAsync() => Task.FromResult(state.State);

    private async ValueTask HandleEventAsync(
        OutboxProcessorTestEvent message,
        StreamCursor cursor)
    {
        if (!state.State.Tracker.ProcessMessage(cursor.StreamNamespace, cursor.Token, out var next))
        {
            return;
        }

        state.State.Tracker = next;
        state.State.ReceivedValues = state.State.ReceivedValues.Add(message.Value);
        await state.WriteStateAsync();
    }
}

public sealed class OutboxProcessorGrainPostmanSourceGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorGrainPostmanSourceGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(CreateOptions())
            .AddGrainPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>, IOutboxProcessorGrainPostmanTargetGrain>(
                (_, grainFactory) => grainFactory.GetGrain<IOutboxProcessorGrainPostmanTargetGrain>(this.GetPrimaryKey()),
                (target, envelope) => target.ReceiveAsync(envelope.Message.Value));

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

    private OutboxProcessorOptions<OutboxMessageEnvelope<OutboxProcessorTestEvent>> CreateOptions() => new()
    {
        PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
        AcknowledgePostedAsync = AcknowledgePostedAsync,
        ReconcileFailedAsync = ReconcileFailedAsync,
        RetryDelay = TimeSpan.FromMilliseconds(100)
    };

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

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
}

public sealed class OutboxProcessorGrainPostmanTargetGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSinkState> state)
    : Grain, IOutboxProcessorGrainPostmanTargetGrain
{
    public async Task ReceiveAsync(string value)
    {
        state.State.ReceivedValues = state.State.ReceivedValues.Add(value);
        await state.WriteStateAsync();
    }

    public Task<OutboxProcessorSinkState> GetStateAsync() => Task.FromResult(state.State);
}