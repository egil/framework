using System.Collections.Immutable;
using Egil.Orleans.Testing;
using TimeProviderExtensions;

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
        var value = $"cancel-{Guid.NewGuid():N}";
        using var gate = OutboxProcessorPostmanGate.Create(value);

        var publish = source.PublishWithCancellationAsync(value);
        await gate.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.TimeProvider.Advance(TimeSpan.FromHours(1));

        var exceptionType = await publish.WaitAsync(TestContext.Current.CancellationToken);
        var state = await source.GetStateAsync();

        Assert.Equal(nameof(TaskCanceledException), exceptionType);
        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Equal(1, state.Outbox?.Count);

        await source.DiscardPendingAsync();
    }

    [Fact]
    public async Task PostAsync_timeout_from_keyed_postman_leaves_item_pending()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorKeyedTimeoutPostmanGrain>(Guid.NewGuid());
        var value = $"timeout-{Guid.NewGuid():N}";
        using var gate = OutboxProcessorPostmanGate.Create(value);

        var publish = source.PublishWithTimeoutAsync(value);
        await gate.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.TimeProvider.Advance(TimeSpan.FromHours(1));

        var exceptionType = await publish.WaitAsync(TestContext.Current.CancellationToken);
        var state = await source.GetStateAsync();

        Assert.Equal(nameof(TimeoutException), exceptionType);
        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Equal(1, state.Outbox?.Count);

        await source.DiscardPendingAsync();
    }

    [Fact]
    public async Task PostAsync_timeout_arms_retry_and_later_run_delivers_pending_item()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorTimeoutRetryGrain>(Guid.NewGuid());
        var value = $"timeout-retry-{Guid.NewGuid():N}";
        using var gate = OutboxProcessorPostmanGate.Create(value);

        var publish = source.PublishWithTimeoutAsync(value);
        await gate.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.TimeProvider.Advance(TimeSpan.FromHours(1));

        var exceptionType = await publish.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(nameof(TimeoutException), exceptionType);
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
    public async Task PostAsync_dispatches_items_for_same_postman_sequentially()
    {
        var source = fixture.GrainFactory.GetGrain<IOutboxProcessorConcurrentPostmanGrain>(Guid.NewGuid());
        var first = $"first-{Guid.NewGuid():N}";
        var second = $"second-{Guid.NewGuid():N}";
        using var firstGate = OutboxProcessorPostmanGate.Create(first);
        using var secondGate = OutboxProcessorPostmanGate.Create(second);

        var publish = source.PublishTwoAsync(first, second);
        await firstGate.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(secondGate.Started.Task.IsCompleted);

        firstGate.AllowCompletion.SetResult();
        await secondGate.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        secondGate.AllowCompletion.SetResult();

        await publish.WaitAsync(TestContext.Current.CancellationToken);
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
        var primary = $"primary-{Guid.NewGuid():N}";
        var secondary = $"secondary-{Guid.NewGuid():N}";
        using var primaryGate = OutboxProcessorPostmanGate.Create(primary);
        using var secondaryGate = OutboxProcessorPostmanGate.Create(secondary);

        var publish = source.PublishTwoPostmenAsync(primary, secondary);
        await Task.WhenAll(
            primaryGate.Started.Task.WaitAsync(TestContext.Current.CancellationToken),
            secondaryGate.Started.Task.WaitAsync(TestContext.Current.CancellationToken));
        primaryGate.AllowCompletion.SetResult();
        secondaryGate.AllowCompletion.SetResult();

        var state = await publish.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal([primary, secondary], state.PostedValues.Sort());
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
        var value = $"background-{Guid.NewGuid():N}";
        using var gate = OutboxProcessorPostmanGate.Create(value);

        await source.PublishInBackgroundAsync(value);
        await gate.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        var state = await source.GetStateAsync();
        Assert.Equal(1, state.MaxConcurrentPostmen);
        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(1, state.Outbox?.Count);

        gate.AllowCompletion.SetResult();
        await fixture.WaitForAssertionAsync(
            source,
            async () =>
            {
                var completed = await source.GetStateAsync();
                Assert.Equal(1, completed.AcknowledgedCount);
                Assert.Equal(0, completed.Outbox?.Count);
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

internal sealed class OutboxProcessorPostmanGate : IDisposable
{
    private static readonly Dictionary<string, OutboxProcessorPostmanGate> Gates = [];
    private readonly string value;

    private OutboxProcessorPostmanGate(string value)
    {
        this.value = value;
    }

    public TaskCompletionSource Started { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource AllowCompletion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static OutboxProcessorPostmanGate Create(string value)
    {
        var gate = new OutboxProcessorPostmanGate(value);
        lock (Gates)
        {
            Gates.Add(value, gate);
        }

        return gate;
    }

    public static OutboxProcessorPostmanGate For(string value)
    {
        lock (Gates)
        {
            return Gates[value];
        }
    }

    public static OutboxProcessorPostmanGate? Find(string value)
    {
        lock (Gates)
        {
            return Gates.GetValueOrDefault(value);
        }
    }

    public void Dispose()
    {
        AllowCompletion.TrySetResult();
        lock (Gates)
        {
            Gates.Remove(value);
        }
    }
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

    Task DiscardPendingAsync();
}

public interface IOutboxProcessorKeyedTimeoutPostmanGrain : IGrainWithGuidKey
{
    Task<string?> PublishWithTimeoutAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();

    Task DiscardPendingAsync();
}

public interface IOutboxProcessorTimeoutRetryGrain : IGrainWithGuidKey
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

    [Id(5)]
    public int FirstProcessorReminderCount { get; set; }
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
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostmanWithToken<OutboxProcessorTestEvent>(PublishEnvelopeAsync);

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
        OutboxProcessorTestEvent envelope,
        OutboxSequenceToken token,
        CancellationToken cancellationToken)
    {
        var sink = GrainFactory.GetGrain<IOutboxProcessorSinkGrain>(this.GetPrimaryKey());
        var streamId = StreamManager.CreateStreamId(
            OutboxProcessorTestNamespaces.Events,
            sink.GetGrainId());
        var stream = this.GetStreamProvider(OutboxProcessorTestProviderNames.Events)
            .GetStream<DeliveredOutboxEvent>(
                streamId);

        _ = stream.OnNextAsync(new DeliveredOutboxEvent(envelope, token));
        return Task.CompletedTask;
    }

    private async ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken)
    {
        var outbox = EnsureOutbox();
        foreach (var item in items)
        {
            outbox = outbox.Remove(item.Id);
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
            outbox = outbox.Remove(failure.Item.Id);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorNoPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorNoPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
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
            outbox = outbox.Remove(failure.Item.Id);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorFailingPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorFailingPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(FailAsync);

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

    private static ValueTask FailAsync(OutboxProcessorTestEvent envelope)
    {
        throw new InvalidOperationException($"Cannot post {envelope.Value}.");
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
            outbox = outbox.Remove(failure.Item.Id);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorKeyedPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorKeyedPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(OutboxProcessorTestPostmanNames.Success);

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
            outbox = outbox.Remove(item.Id);
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
            outbox = outbox.Remove(failure.Item.Id);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorKeyedFailingPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorKeyedFailingPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(OutboxProcessorTestPostmanNames.Failure);

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
            outbox = outbox.Remove(failure.Item.Id);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorKeyedCancellationPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state,
    ManualTimeProvider timeProvider)
    : Grain, IOutboxProcessorKeyedCancellationPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            ProcessingTimeout = TimeSpan.FromHours(2),
            TimeProvider = timeProvider,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(OutboxProcessorTestPostmanNames.Delay);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<string?> PublishWithCancellationAsync(string value)
    {
        var outbox = EnsureOutbox();
        state.State.Outbox = outbox.Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromHours(1), timeProvider);
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

    public async Task DiscardPendingAsync()
    {
        state.State.Outbox = EnsureOutbox().Clear();
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

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
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorKeyedTimeoutPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state,
    ManualTimeProvider timeProvider)
    : Grain, IOutboxProcessorKeyedTimeoutPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            ProcessingTimeout = TimeSpan.FromHours(1),
            TimeProvider = timeProvider,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(OutboxProcessorTestPostmanNames.Delay);

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

    public async Task DiscardPendingAsync()
    {
        state.State.Outbox = EnsureOutbox().Clear();
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

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
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorTimeoutRetryGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state,
    ManualTimeProvider timeProvider)
    : Grain, IOutboxProcessorTimeoutRetryGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;
    private int attempts;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = (_, _) => ValueTask.CompletedTask,
            ProcessingTimeout = TimeSpan.FromHours(1),
            TimeProvider = timeProvider,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(FirstAttemptBlocksAsync);

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

    private async Task FirstAttemptBlocksAsync(
        OutboxProcessorTestEvent envelope,
        CancellationToken cancellationToken)
    {
        if (++attempts == 1)
        {
            var gate = OutboxProcessorPostmanGate.For(envelope.Value);
            gate.Started.TrySetResult();
            await gate.AllowCompletion.Task.WaitAsync(cancellationToken);
        }
    }

    private async ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken)
    {
        var outbox = EnsureOutbox();
        foreach (var item in items)
        {
            outbox = outbox.Remove(item.Id);
        }

        state.State.Outbox = outbox;
        state.State.AcknowledgedCount += items.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorConcurrentPostmanGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorConcurrentPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;
    private int activePostmen;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        EnsureOutbox();

        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(PostWithGateAndTrackConcurrencyAsync);

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

    private async Task PostWithGateAndTrackConcurrencyAsync(
        OutboxProcessorTestEvent envelope,
        CancellationToken cancellationToken)
    {
        activePostmen++;
        state.State.MaxConcurrentPostmen = Math.Max(state.State.MaxConcurrentPostmen, activePostmen);

        try
        {
            var gate = OutboxProcessorPostmanGate.For(envelope.Value);
            gate.Started.TrySetResult();
            await gate.AllowCompletion.Task.WaitAsync(cancellationToken);
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
            outbox = outbox.Remove(item.Id);
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
            outbox = outbox.Remove(failure.Item.Id);
            state.State.LastFailureType = failure.Error.GetType().Name;
        }

        state.State.Outbox = outbox;
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox()
    {
        return state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
    }
}

public sealed class OutboxProcessorOrderedPostmanGrain
    : Grain, IOutboxProcessorOrderedPostmanGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorOrderedMessage>? processor;
    private ImmutableArray<OutboxMessageEnvelope<OutboxProcessorOrderedMessage>> pending = [];
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
        pending = Outbox<OutboxProcessorOrderedMessage>.Create()
            .Add(new OutboxProcessorPrimaryMessage(primary))
            .Add(new OutboxProcessorSecondaryMessage(secondary))
            .ToImmutableArray();

        await processor!.PostAsync();
        return CreateState();
    }

    public async Task<OutboxProcessorOrderedPostmanState> PublishBlockedPostmanBatchAsync(
        string failingPrimary,
        string secondary,
        string blockedPrimary)
    {
        pending = Outbox<OutboxProcessorOrderedMessage>.Create()
            .Add(new OutboxProcessorPrimaryMessage(failingPrimary))
            .Add(new OutboxProcessorSecondaryMessage(secondary))
            .Add(new OutboxProcessorPrimaryMessage(blockedPrimary))
            .ToImmutableArray();

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

            if (OutboxProcessorPostmanGate.Find(message.Value) is { } gate)
            {
                gate.Started.TrySetResult();
                await gate.AllowCompletion.Task.WaitAsync(cancellationToken);
            }
        }
        finally
        {
            activePostmen--;
        }
    }

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorOrderedMessage>> items,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            pending = pending.Remove(item);
            postedValues = postedValues.Add(item.Message.Value);
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorOrderedMessage> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        foreach (var failure in failures)
        {
            failedValues = failedValues.Add(failure.Item.Message.Value);
        }

        return ValueTask.CompletedTask;
    }

    private OutboxProcessorOrderedPostmanState CreateState() =>
        new(
            postedValues,
            failedValues,
            pending.Select(static item => item.Message.Value).ToImmutableArray(),
            attemptedValues,
            maxConcurrentPostmen);
}

public sealed class KeyedOutboxProcessorSuccessPostman :
    IPostman<OutboxProcessorTestEvent>
{
    public ValueTask PostAsync(
        OutboxProcessorTestEvent message,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

public sealed class KeyedOutboxProcessorFailingPostman :
    IPostman<OutboxProcessorTestEvent>
{
    public ValueTask PostAsync(
        OutboxProcessorTestEvent message,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException($"Cannot post {message.Value}.");
    }
}

public sealed class KeyedOutboxProcessorDelayingPostman :
    IPostman<OutboxProcessorTestEvent>
{
    public async ValueTask PostAsync(
        OutboxProcessorTestEvent message,
        CancellationToken cancellationToken)
    {
        var gate = OutboxProcessorPostmanGate.For(message.Value);
        gate.Started.TrySetResult();
        await gate.AllowCompletion.Task.WaitAsync(cancellationToken);
    }
}

public sealed class OutboxProcessorSinkGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSinkState> state)
    : Grain, IOutboxProcessorSinkGrain
{
    private StreamManager? streamManager;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        streamManager = this.RegisterStreamManager(() => state.State.Tracker)
            .ConfigureExplicitSubscription<DeliveredOutboxEvent>(
                OutboxProcessorTestProviderNames.Events,
                OutboxProcessorTestNamespaces.Events,
                HandleEnvelopeAsync);

        await streamManager.EnsureExplicitSubscriptionsAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }

    public Task EnsureActiveAsync() => Task.CompletedTask;

    public Task<OutboxProcessorSinkState> GetStateAsync() => Task.FromResult(state.State);

    private async ValueTask HandleEnvelopeAsync(
        DeliveredOutboxEvent envelope,
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

[GenerateSerializer]
public sealed record DeliveredOutboxEvent(
    [property: Id(0)] OutboxProcessorTestEvent Message,
    [property: Id(1)] OutboxSequenceToken Token);
