using System.Collections.Immutable;
using Egil.Orleans.Testing;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

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

    [Fact]
    public async Task PostInBackgroundAsync_resolves_keyed_postman_and_acknowledges()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorKeyedPostmanGrain>(Guid.NewGuid());

        await source.PublishInBackgroundAsync("keyed");

        await fixture.WaitForAssertionAsync(
            source,
            async () =>
            {
                var state = await source.GetStateAsync();
                Assert.Equal(1, state.AcknowledgedCount);
                Assert.Equal(0, state.FailedCount);
                Assert.Equal(0, state.Outbox?.Count ?? 0);
            },
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PostInBackgroundAsync_reports_keyed_postman_failure_and_reconciles_pending_item()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorKeyedFailingPostmanGrain>(Guid.NewGuid());

        await source.PublishInBackgroundAsync("keyed-failure");

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

    [Fact]
    public async Task PostAsync_propagates_cancellation_to_keyed_postman_and_leaves_item_pending()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorKeyedCancellationPostmanGrain>(Guid.NewGuid());

        var exceptionType = await source.PublishWithCancellationAsync("cancel");
        var state = await source.GetStateAsync();

        Assert.Equal(nameof(TaskCanceledException), exceptionType);
        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Equal(1, state.Outbox?.Count);
    }

    [Fact]
    public async Task PostAsync_timeout_from_keyed_postman_leaves_item_pending()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorKeyedTimeoutPostmanGrain>(Guid.NewGuid());

        var exceptionType = await source.PublishWithTimeoutAsync("timeout");
        var state = await source.GetStateAsync();

        Assert.Equal(nameof(TimeoutException), exceptionType);
        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Equal(1, state.Outbox?.Count);
    }

    [Fact]
    public async Task PostAsync_dispatches_items_for_same_postman_sequentially()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorConcurrentPostmanGrain>(Guid.NewGuid());

        await source.PublishTwoAsync("first", "second");
        var state = await source.GetStateAsync();

        Assert.Equal(2, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Equal(0, state.Outbox?.Count);
        Assert.Equal(1, state.MaxConcurrentPostmen);
    }

    [Fact]
    public async Task PostAsync_dispatches_different_postmen_concurrently()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorOrderedPostmanGrain>(Guid.NewGuid());

        var state = await source.PublishTwoPostmenAsync("primary", "secondary");

        Assert.Equal(["primary", "secondary"], state.PostedValues.Sort());
        Assert.Empty(state.FailedValues);
        Assert.Empty(state.PendingValues);
        Assert.Equal(2, state.MaxConcurrentPostmen);
    }

    [Fact]
    public async Task PostAsync_blocks_later_items_for_same_postman_after_failure()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorOrderedPostmanGrain>(Guid.NewGuid());

        var state = await source.PublishBlockedPostmanBatchAsync("fail-first", "secondary", "blocked-later");

        Assert.Equal(["secondary"], state.PostedValues);
        Assert.Equal(["fail-first"], state.FailedValues);
        Assert.Equal(["fail-first", "blocked-later"], state.PendingValues);
        Assert.DoesNotContain("blocked-later", state.AttemptedValues);
    }

    [Fact]
    public async Task PostInBackgroundAsync_allows_other_calls_while_postman_is_awaiting()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorConcurrentPostmanGrain>(Guid.NewGuid());

        await source.PublishInBackgroundAsync("background");

        await fixture.WaitForAssertionAsync(
            source,
            async () =>
            {
                var state = await source.GetStateAsync();
                Assert.Equal(1, state.MaxConcurrentPostmen);
                Assert.Equal(0, state.AcknowledgedCount);
                Assert.Equal(1, state.Outbox?.Count);
            },
            ct: TestContext.Current.CancellationToken);
    }
}

internal static class OutboxProcessorTestNamespaces
{
    public const string Events = "outbox-processor-events";
}

internal static class OutboxProcessorTestProviderNames
{
    public const string Events = "outbox-processor-stream-provider";
}

internal static class OutboxProcessorTestPostmanNames
{
    public const string Success = "outbox-processor-success";
    public const string Failure = "outbox-processor-failure";
    public const string Delay = "outbox-processor-delay";
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

public interface IOutboxProcessorKeyedPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorKeyedFailingPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorKeyedCancellationPostmanGrain : IGrainWithGuidKey
{
    Task<string?> PublishWithCancellationAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorKeyedTimeoutPostmanGrain : IGrainWithGuidKey
{
    Task<string?> PublishWithTimeoutAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorConcurrentPostmanGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task PublishTwoAsync(string first, string second);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorOrderedPostmanGrain : IGrainWithGuidKey
{
    Task<OutboxProcessorOrderedPostmanState> PublishTwoPostmenAsync(string primary, string secondary);

    Task<OutboxProcessorOrderedPostmanState> PublishBlockedPostmanBatchAsync(
        string failingPrimary,
        string secondary,
        string blockedPrimary);
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

    [Id(4)]
    public int MaxConcurrentPostmen { get; set; }
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
public sealed record OutboxProcessorOrderedPostmanState(
    [property: Id(0)] ImmutableArray<string> PostedValues,
    [property: Id(1)] ImmutableArray<string> FailedValues,
    [property: Id(2)] ImmutableArray<string> PendingValues,
    [property: Id(3)] ImmutableArray<string> AttemptedValues,
    [property: Id(4)] int MaxConcurrentPostmen);

[GenerateSerializer]
public sealed record OutboxProcessorTestEvent([property: Id(0)] string Value);

public abstract record OutboxProcessorOrderedMessage(string Value);

public sealed record OutboxProcessorPrimaryMessage(string Value) : OutboxProcessorOrderedMessage(Value);

public sealed record OutboxProcessorSecondaryMessage(string Value) : OutboxProcessorOrderedMessage(Value);

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
        var stream = this.GetStreamProvider(OutboxProcessorTestProviderNames.Events)
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

public sealed class OutboxProcessorKeyedPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorKeyedPostmanGrain, IOutboxGrain
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
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(OutboxProcessorTestPostmanNames.Success);

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

public sealed class OutboxProcessorKeyedFailingPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorKeyedFailingPostmanGrain, IOutboxGrain
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
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(OutboxProcessorTestPostmanNames.Failure);

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

public sealed class OutboxProcessorKeyedCancellationPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorKeyedCancellationPostmanGrain, IOutboxGrain
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
            ProcessingTimeout = TimeSpan.FromSeconds(5),
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(OutboxProcessorTestPostmanNames.Delay);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<string?> PublishWithCancellationAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        try
        {
            await processor!.PostAsync(cancellation.Token);
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
    }
}

public sealed class OutboxProcessorKeyedTimeoutPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorKeyedTimeoutPostmanGrain, IOutboxGrain
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
            ProcessingTimeout = TimeSpan.FromMilliseconds(20),
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(OutboxProcessorTestPostmanNames.Delay);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<string?> PublishWithTimeoutAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();

        try
        {
            await processor!.PostAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create(GrainContext.GrainId);
    }
}

public sealed class OutboxProcessorConcurrentPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorConcurrentPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxMessageEnvelope<OutboxProcessorTestEvent>>? processor;
    private int activePostmen;

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
        .AddPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(DelayAndTrackConcurrencyAsync);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishTwoAsync(string first, string second)
    {
        var outbox = EnsureOutbox()
            .Add(new OutboxProcessorTestEvent(first))
            .Add(new OutboxProcessorTestEvent(second));

        state.State.Outbox = outbox;
        await state.WriteStateAsync();
        await processor!.PostAsync();
    }

    public async Task PublishInBackgroundAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private async Task DelayAndTrackConcurrencyAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> envelope,
        CancellationToken cancellationToken)
    {
        activePostmen++;
        state.State.MaxConcurrentPostmen = Math.Max(state.State.MaxConcurrentPostmen, activePostmen);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        finally
        {
            activePostmen--;
        }
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

public sealed class OutboxProcessorOrderedPostmanGrain
    : Grain, IOutboxProcessorOrderedPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorOrderedMessage>? processor;
    private ImmutableArray<OutboxProcessorOrderedMessage> pending = [];
    private ImmutableArray<string> postedValues = [];
    private ImmutableArray<string> failedValues = [];
    private ImmutableArray<string> attemptedValues = [];
    private int activePostmen;
    private int maxConcurrentPostmen;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorOrderedMessage>
        {
            PendingItems = () => pending,
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMinutes(10)
        })
        .AddPostman<OutboxProcessorPrimaryMessage>(PostPrimaryAsync)
        .AddPostman<OutboxProcessorSecondaryMessage>(PostSecondaryAsync);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<OutboxProcessorOrderedPostmanState> PublishTwoPostmenAsync(
        string primary,
        string secondary)
    {
        pending =
        [
            new OutboxProcessorPrimaryMessage(primary),
            new OutboxProcessorSecondaryMessage(secondary)
        ];

        await processor!.PostAsync();
        return CreateState();
    }

    public async Task<OutboxProcessorOrderedPostmanState> PublishBlockedPostmanBatchAsync(
        string failingPrimary,
        string secondary,
        string blockedPrimary)
    {
        pending =
        [
            new OutboxProcessorPrimaryMessage(failingPrimary),
            new OutboxProcessorSecondaryMessage(secondary),
            new OutboxProcessorPrimaryMessage(blockedPrimary)
        ];

        await processor!.PostAsync();
        return CreateState();
    }

    private Task PostPrimaryAsync(
        OutboxProcessorPrimaryMessage message,
        CancellationToken cancellationToken) =>
        PostTrackedAsync(message, cancellationToken);

    private Task PostSecondaryAsync(
        OutboxProcessorSecondaryMessage message,
        CancellationToken cancellationToken) =>
        PostTrackedAsync(message, cancellationToken);

    private async Task PostTrackedAsync(
        OutboxProcessorOrderedMessage message,
        CancellationToken cancellationToken)
    {
        attemptedValues = attemptedValues.Add(message.Value);
        activePostmen++;
        maxConcurrentPostmen = Math.Max(maxConcurrentPostmen, activePostmen);

        try
        {
            if (message.Value.StartsWith("fail-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Cannot post {message.Value}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
        finally
        {
            activePostmen--;
        }
    }

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxProcessorOrderedMessage> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            pending = pending.Remove(item);
            postedValues = postedValues.Add(item.Value);
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxProcessorOrderedMessage Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        foreach (var failure in failures)
        {
            failedValues = failedValues.Add(failure.Item.Value);
        }

        return ValueTask.CompletedTask;
    }

    private OutboxProcessorOrderedPostmanState CreateState() =>
        new(
            postedValues,
            failedValues,
            pending.Select(static item => item.Value).ToImmutableArray(),
            attemptedValues,
            maxConcurrentPostmen);
}

public sealed class KeyedOutboxProcessorSuccessPostman :
    IPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>
{
    public ValueTask PostAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> message,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class KeyedOutboxProcessorFailingPostman :
    IPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>
{
    public ValueTask PostAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> message,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException($"Cannot post {message.Message.Value}.");
    }
}

public sealed class KeyedOutboxProcessorDelayingPostman :
    IPostman<OutboxMessageEnvelope<OutboxProcessorTestEvent>>
{
    public async ValueTask PostAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> message,
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
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
            .ConfigureExplicitSubscription<OutboxMessageEnvelope<OutboxProcessorTestEvent>>(
                OutboxProcessorTestProviderNames.Events,
                OutboxProcessorTestNamespaces.Events,
                HandleEnvelopeAsync);

        await streamManager.EnsureExplicitSubscriptionsAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task<OutboxProcessorSinkState> GetStateAsync() => Task.FromResult(state.State);

    private async ValueTask HandleEnvelopeAsync(
        OutboxMessageEnvelope<OutboxProcessorTestEvent> envelope,
        StreamCursor cursor)
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
