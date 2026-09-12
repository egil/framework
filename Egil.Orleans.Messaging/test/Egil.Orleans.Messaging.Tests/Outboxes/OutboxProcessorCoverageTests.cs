using System.Collections.Immutable;
using Egil.Orleans.Testing;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxProcessorCoverageTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Fact]
    public async Task PostAsync_schedules_reentrant_post_request_after_current_drain()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReentrantPostGrain>(Guid.NewGuid());

        var state = await grain.PublishWithReentrantPostAsync("first", "second");

        Assert.Equal(1, state.AcknowledgedCount);
        Assert.Equal(1, state.Outbox?.Count);

        await fixture.WaitForAssertionAsync(
            grain,
            async () =>
            {
                var current = await grain.GetStateAsync();
                Assert.Equal(2, current.AcknowledgedCount);
                Assert.Equal(0, current.Outbox?.Count);
            },
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PostAsync_concurrent_call_waits_then_drains_current_outbox()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorConcurrentManualPostGrain>(grainKey);
        var gate = OutboxProcessorSchedulingGate.For(grainKey);

        try
        {
            var firstPost = grain.PublishAndBlockPostAsync("first");
            await gate.DispatchStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var secondPost = grain.PublishAndPostAsync("second");
            await gate.ForegroundPostStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(secondPost.IsCompleted);

            gate.AllowDispatch.SetResult();
            await firstPost.WaitAsync(TestContext.Current.CancellationToken);

            var state = await secondPost.WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, state.AcknowledgedCount);
            Assert.Equal(0, state.Outbox?.Count);
        }
        finally
        {
            OutboxProcessorSchedulingGate.Remove(grainKey);
        }
    }

    [Fact]
    public async Task PostInBackgroundAsync_with_failed_item_left_pending_schedules_retry()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorRetryPendingGrain>(Guid.NewGuid());

        await grain.PublishInBackgroundAsync("retry");

        await fixture.WaitForAssertionAsync(
            grain,
            async () =>
            {
                var state = await grain.GetStateAsync();
                Assert.True(state.FailedCount >= 1);
                Assert.Equal(1, state.Outbox?.Count);
            },
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Failed_dispatch_registers_reminder_and_successful_retry_unregisters_it()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorRetryPendingGrain>(Guid.NewGuid());

        var failedState = await grain.PublishAndFailAsync("retry");

        Assert.Equal(1, failedState.FailedCount);
        Assert.Equal(1, failedState.Outbox?.Count);
        Assert.True(await grain.HasReminderAsync());

        var recoveredState = await grain.RetrySuccessfullyAsync();

        Assert.Equal(1, recoveredState.AcknowledgedCount);
        Assert.Equal(0, recoveredState.Outbox?.Count);
        Assert.False(await grain.HasReminderAsync());
    }

    [Fact]
    public async Task ReceiveReminderAsync_ignores_unrelated_reminder_name()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReminderCoverageGrain>(Guid.NewGuid());

        var state = await grain.PublishAndReceiveUnrelatedReminderAsync("ignored");

        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(1, state.Outbox?.Count);
    }

    [Fact]
    public async Task ReceiveReminderAsync_schedules_matching_reminder_name()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReminderCoverageGrain>(Guid.NewGuid());

        await grain.PublishAndReceiveOutboxReminderAsync("matched");

        await fixture.WaitForAssertionAsync(
            grain,
            async () =>
            {
                var state = await grain.GetStateAsync();
                Assert.Equal(1, state.AcknowledgedCount);
                Assert.Equal(0, state.Outbox?.Count);
            },
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PostInBackgroundAsync_with_empty_pending_snapshot_disables_retry()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var state = await grain.PostEmptyPendingInBackgroundAsync();

        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Null(state.Outbox);
    }

    [Fact]
    public async Task PostAsync_with_default_pending_snapshot_disables_retry()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var state = await grain.PostDefaultPendingAsync();

        Assert.Equal(0, state.AcknowledgedCount);
        Assert.Equal(0, state.FailedCount);
        Assert.Null(state.Outbox);
    }

    [Fact]
    public async Task PostAsync_with_default_pending_snapshot_after_acknowledgement_disables_retry()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var state = await grain.PostPendingThenDefaultAfterAcknowledgeAsync();

        Assert.Equal(1, state.AcknowledgedCount);
        Assert.Null(state.Outbox);
    }

    [Fact]
    public async Task PostInBackgroundAsync_honors_canceled_token()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var exceptionType = await grain.PostInBackgroundWithCanceledTokenAsync();

        Assert.Equal(nameof(OperationCanceledException), exceptionType);
    }

    [Fact]
    public async Task AddStreamPostman_two_argument_overload_validates_stream_id_delegate()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var paramName = await grain.ValidateNullStreamIdAsync();

        Assert.Equal("streamId", paramName);
    }

    [Fact]
    public async Task RegisterOutboxProcessor_rejects_non_positive_processing_timeout()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var paramName = await grain.ValidateProcessingTimeoutAsync();

        Assert.Equal("options", paramName);
    }

    [Fact]
    public async Task RegisterOutboxProcessor_rejects_non_positive_retry_delay()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var paramName = await grain.ValidateRetryDelayAsync();

        Assert.Equal("options", paramName);
    }

    [Fact]
    public async Task Registering_second_outbox_processor_throws_without_replacing_first_reminder_component()
    {
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorValidationCoverageGrain>(Guid.NewGuid());

        var error = await grain.RegisterSecondProcessorAndReceiveFirstReminderAsync();
        var state = await grain.GetStateAsync();

        Assert.Equal(
            "Only one outbox processor can be registered per grain activation.",
            error);
        Assert.Equal(1, state.FirstProcessorReminderCount);
    }

    [Fact]
    public async Task Background_dispatch_allows_other_calls_while_postman_is_awaiting()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReconciliationSchedulingGrain>(grainKey);
        var gate = OutboxProcessorSchedulingGate.For(grainKey);

        try
        {
            await grain.PublishInBackgroundAsync("dispatch", interleaveReconciliation: false);
            await gate.DispatchStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            await grain.RecordWriteAsync("during-dispatch");

            gate.AllowDispatch.SetResult();
            await gate.ReconciliationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            gate.AllowReconciliation.SetResult();

            await fixture.WaitForAssertionAsync(
                grain,
                async () =>
                {
                    var state = await grain.GetSchedulingStateAsync();
                    Assert.Contains("during-dispatch", state.Writes);
                    Assert.Equal(1, state.AcknowledgedCount);
                },
                ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            OutboxProcessorSchedulingGate.Remove(grainKey);
        }
    }

    [Fact]
    public async Task PostAsync_coalesces_with_background_dispatch_before_non_interleaving_reconciliation()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReconciliationSchedulingGrain>(grainKey);
        var gate = OutboxProcessorSchedulingGate.For(grainKey);

        try
        {
            await grain.PublishInBackgroundAsync("dispatch", interleaveReconciliation: false);
            await gate.DispatchStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var postAgain = grain.PostAgainAsync(TestContext.Current.CancellationToken);
            await gate.ForegroundPostStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            gate.AllowDispatch.SetResult();
            gate.AllowReconciliation.SetResult();

            await postAgain;
            await gate.ReconciliationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            await fixture.WaitForAssertionAsync(
                grain,
                async () =>
                {
                    var state = await grain.GetSchedulingStateAsync();
                    Assert.Equal(1, state.AcknowledgedCount);
                },
                ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.AllowDispatch.TrySetResult();
            gate.AllowReconciliation.TrySetResult();
            OutboxProcessorSchedulingGate.Remove(grainKey);
        }
    }

    [Fact]
    public async Task Empty_background_post_does_not_cancel_active_reconciliation()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReconciliationSchedulingGrain>(grainKey);
        var gate = OutboxProcessorSchedulingGate.For(grainKey);

        try
        {
            await grain.PublishInBackgroundAsync("dispatch", interleaveReconciliation: true);
            await gate.DispatchStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            gate.AllowDispatch.SetResult();

            await gate.ReconciliationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
            await grain.ClearAndPostInBackgroundAsync(TestContext.Current.CancellationToken);
            gate.AllowReconciliation.SetResult();

            await fixture.WaitForAssertionAsync(
                grain,
                async () =>
                {
                    var state = await grain.GetSchedulingStateAsync();
                    Assert.Equal(1, state.AcknowledgedCount);
                },
                ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            gate.AllowDispatch.TrySetResult();
            gate.AllowReconciliation.TrySetResult();
            OutboxProcessorSchedulingGate.Remove(grainKey);
        }
    }

    [Fact]
    public async Task Background_reconciliation_does_not_interleave_with_other_calls_by_default()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReconciliationSchedulingGrain>(grainKey);
        var gate = OutboxProcessorSchedulingGate.For(grainKey);

        try
        {
            await grain.PublishInBackgroundAsync("non-interleaving-reconciliation", interleaveReconciliation: false);
            gate.AllowDispatch.SetResult();
            await gate.ReconciliationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            var writeTask = grain.RecordWriteAsync("during-reconciliation");

            gate.AllowReconciliation.SetResult();
            await writeTask.WaitAsync(TestContext.Current.CancellationToken);

            var state = await grain.GetSchedulingStateAsync();
            Assert.Equal(["acknowledged", "during-reconciliation"], state.Writes);
        }
        finally
        {
            OutboxProcessorSchedulingGate.Remove(grainKey);
        }
    }

    [Fact]
    public async Task Background_reconciliation_can_be_configured_to_interleave_with_other_calls()
    {
        var grainKey = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IOutboxProcessorReconciliationSchedulingGrain>(grainKey);
        var gate = OutboxProcessorSchedulingGate.For(grainKey);

        try
        {
            await grain.PublishInBackgroundAsync("interleaving-reconciliation", interleaveReconciliation: true);
            gate.AllowDispatch.SetResult();
            await gate.ReconciliationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

            await grain.RecordWriteAsync("during-reconciliation");
            gate.AllowReconciliation.SetResult();

            await fixture.WaitForAssertionAsync(
                grain,
                async () =>
                {
                    var state = await grain.GetSchedulingStateAsync();
                    Assert.Equal(["during-reconciliation", "acknowledged"], state.Writes);
                    Assert.Equal(1, state.AcknowledgedCount);
                },
                ct: TestContext.Current.CancellationToken);
        }
        finally
        {
            OutboxProcessorSchedulingGate.Remove(grainKey);
        }
    }
}

public interface IOutboxProcessorReentrantPostGrain : IGrainWithGuidKey
{
    Task<OutboxProcessorSourceState> PublishWithReentrantPostAsync(string first, string second);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorConcurrentManualPostGrain : IGrainWithGuidKey
{
    Task<OutboxProcessorSourceState> PublishAndBlockPostAsync(string value);

    Task<OutboxProcessorSourceState> PublishAndPostAsync(string value);
}

public interface IOutboxProcessorRetryPendingGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value);

    Task<OutboxProcessorSourceState> PublishAndFailAsync(string value);

    Task<OutboxProcessorSourceState> RetrySuccessfullyAsync();

    Task<bool> HasReminderAsync();

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorReminderCoverageGrain : IGrainWithGuidKey
{
    Task<OutboxProcessorSourceState> PublishAndReceiveUnrelatedReminderAsync(string value);

    Task PublishAndReceiveOutboxReminderAsync(string value);

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorValidationCoverageGrain : IGrainWithGuidKey
{
    Task<OutboxProcessorSourceState> PostEmptyPendingInBackgroundAsync();

    Task<OutboxProcessorSourceState> PostDefaultPendingAsync();

    Task<OutboxProcessorSourceState> PostPendingThenDefaultAfterAcknowledgeAsync();

    Task<string?> PostInBackgroundWithCanceledTokenAsync();

    Task<string?> ValidateNullStreamIdAsync();

    Task<string?> ValidateProcessingTimeoutAsync();

    Task<string?> ValidateRetryDelayAsync();

    Task<string?> RegisterSecondProcessorAndReceiveFirstReminderAsync();

    Task<OutboxProcessorSourceState> GetStateAsync();
}

public interface IOutboxProcessorReconciliationSchedulingGrain : IGrainWithGuidKey
{
    Task PublishInBackgroundAsync(string value, bool interleaveReconciliation);

    Task PostAgainAsync(CancellationToken cancellationToken);

    Task ClearAndPostInBackgroundAsync(CancellationToken cancellationToken);

    Task RecordWriteAsync(string value);

    Task<OutboxProcessorSchedulingState> GetSchedulingStateAsync();
}

[GenerateSerializer]
public sealed record OutboxProcessorSchedulingState(
    [property: Id(0)] int AcknowledgedCount,
    [property: Id(1)] ImmutableArray<string> Writes);

internal sealed class OutboxProcessorSchedulingGate
{
    private static readonly Dictionary<Guid, OutboxProcessorSchedulingGate> Gates = [];

    public TaskCompletionSource DispatchStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource AllowDispatch { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ForegroundPostStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReconciliationStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource AllowReconciliation { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static OutboxProcessorSchedulingGate For(Guid grainKey)
    {
        lock (Gates)
        {
            return Gates.TryGetValue(grainKey, out var gate)
                ? gate
                : Gates[grainKey] = new OutboxProcessorSchedulingGate();
        }
    }

    public static void Remove(Guid grainKey)
    {
        lock (Gates)
        {
            Gates.Remove(grainKey);
        }
    }
}

public sealed class OutboxProcessorReentrantPostGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorReentrantPostGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;
    private string? followUpValue;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(CreateOptions())
            .AddPostman<OutboxProcessorTestEvent>(async (message, _, cancellationToken) => await PostAndRequestAnotherDrainAsync(message, cancellationToken));

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<OutboxProcessorSourceState> PublishWithReentrantPostAsync(string first, string second)
    {
        followUpValue = second;
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(first));
        await state.WriteStateAsync();

        await processor!.PostAsync();
        return state.State;
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private async Task PostAndRequestAnotherDrainAsync(
        OutboxProcessorTestEvent envelope,
        CancellationToken cancellationToken)
    {
        if (followUpValue is null)
        {
            return;
        }

        var nextValue = followUpValue;
        followUpValue = null;
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(nextValue));
        await processor!.PostAsync(cancellationToken);
    }

    private OutboxProcessorOptions<OutboxProcessorTestEvent> CreateOptions() => new()
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
            outbox = outbox.Remove(item.Id);
        }

        state.State.Outbox = outbox;
        state.State.AcknowledgedCount += items.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
}

[global::Orleans.Concurrency.Reentrant]
public sealed class OutboxProcessorConcurrentManualPostGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorConcurrentManualPostGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(async (message, _, cancellationToken) => await PostWithGateAsync(message, cancellationToken));

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<OutboxProcessorSourceState> PublishAndBlockPostAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostAsync();
        return state.State;
    }

    public async Task<OutboxProcessorSourceState> PublishAndPostAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        var post = processor!.PostAsync().AsTask();
        OutboxProcessorSchedulingGate.For(this.GetPrimaryKey()).ForegroundPostStarted.SetResult();
        await post;
        return state.State;
    }

    private async Task PostWithGateAsync(
        OutboxProcessorTestEvent envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.Value != "first")
        {
            return;
        }

        var gate = OutboxProcessorSchedulingGate.For(this.GetPrimaryKey());
        gate.DispatchStarted.SetResult();
        await gate.AllowDispatch.Task.WaitAsync(cancellationToken);
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

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
}

public sealed class OutboxProcessorRetryPendingGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorRetryPendingGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;
    private bool failPosting = true;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMinutes(2)
        })
        .AddPostman<OutboxProcessorTestEvent>(PostItemAsync);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task PublishInBackgroundAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostInBackgroundAsync();
    }

    public async Task<OutboxProcessorSourceState> PublishAndFailAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.PostAsync();
        return state.State;
    }

    public async Task<OutboxProcessorSourceState> RetrySuccessfullyAsync()
    {
        failPosting = false;
        await processor!.PostAsync();
        return state.State;
    }

    public async Task<bool> HasReminderAsync() =>
        await this.GetReminder(processor!.ReminderName) is not null;

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private ValueTask PostItemAsync(OutboxProcessorTestEvent _) =>
        failPosting
            ? new ValueTask(Task.FromException(new InvalidOperationException("Keep pending for retry.")))
            : ValueTask.CompletedTask;

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
        state.State.FailedCount += failures.Length;
        state.State.LastFailureType = failures[0].Error.GetType().Name;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
}

public sealed class OutboxProcessorReminderCoverageGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorReminderCoverageGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMinutes(2)
        })
        .AddPostman<OutboxProcessorTestEvent>(static _ => ValueTask.CompletedTask);

        await base.OnActivateAsync(cancellationToken);
    }

    public async Task<OutboxProcessorSourceState> PublishAndReceiveUnrelatedReminderAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.ReceiveReminderAsync("not-the-outbox-reminder", default);
        return state.State;
    }

    public async Task PublishAndReceiveOutboxReminderAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor!.ReceiveReminderAsync(processor.ReminderName, default);
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

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
}

public sealed class OutboxProcessorValidationCoverageGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorValidationCoverageGrain, IOutboxGrain
{
    public async Task<OutboxProcessorSourceState> PostEmptyPendingInBackgroundAsync()
    {
        var processor = this.RegisterOutboxProcessor(CreateOptions(() => []));
        await processor.PostInBackgroundAsync();
        return state.State;
    }

    public async Task<OutboxProcessorSourceState> PostDefaultPendingAsync()
    {
        var processor = this.RegisterOutboxProcessor(CreateOptions(static () => default));
        await processor.PostAsync();
        return state.State;
    }

    public async Task<OutboxProcessorSourceState> PostPendingThenDefaultAfterAcknowledgeAsync()
    {
        var pending = Outbox<OutboxProcessorTestEvent>
            .Create()
            .Add(new OutboxProcessorTestEvent("default-after-ack"))
            .ToImmutableArray();
        var returnDefault = false;

        var processor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => returnDefault ? default : pending,
            AcknowledgePostedAsync = (items, _) =>
            {
                state.State.AcknowledgedCount += items.Length;
                returnDefault = true;
                return ValueTask.CompletedTask;
            },
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        })
        .AddPostman<OutboxProcessorTestEvent>(static _ => ValueTask.CompletedTask);

        await processor.PostAsync();
        return state.State;
    }

    public async Task<string?> PostInBackgroundWithCanceledTokenAsync()
    {
        var processor = this.RegisterOutboxProcessor(CreateOptions(() => []));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        try
        {
            await processor.PostInBackgroundAsync(cancellation.Token);
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    public Task<string?> ValidateNullStreamIdAsync()
    {
        try
        {
            _ = this.RegisterOutboxProcessor(CreateOptions(() => []))
                .AddStreamPostman<OutboxProcessorTestEvent>(
                    OutboxProcessorTestProviderNames.Events,
                    null!);
            return Task.FromResult<string?>(null);
        }
        catch (ArgumentNullException ex)
        {
            return Task.FromResult<string?>(ex.ParamName);
        }
    }

    public Task<string?> ValidateProcessingTimeoutAsync()
    {
        try
        {
            _ = this.RegisterOutboxProcessor(CreateOptions(
                () => [],
                processingTimeout: TimeSpan.Zero));
            return Task.FromResult<string?>(null);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Task.FromResult<string?>(ex.ParamName);
        }
    }

    public Task<string?> ValidateRetryDelayAsync()
    {
        try
        {
            _ = this.RegisterOutboxProcessor(CreateOptions(
                () => [],
                retryDelay: TimeSpan.Zero));
            return Task.FromResult<string?>(null);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Task.FromResult<string?>(ex.ParamName);
        }
    }

    public async Task<string?> RegisterSecondProcessorAndReceiveFirstReminderAsync()
    {
        var firstProcessor = this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () =>
            {
                state.State.FirstProcessorReminderCount++;
                return [];
            },
            AcknowledgePostedAsync = static (_, _) => ValueTask.CompletedTask,
            RetryDelay = TimeSpan.FromMilliseconds(100)
        });

        string? error = null;
        try
        {
            _ = this.RegisterOutboxProcessor(new OutboxProcessorOptions<string>
            {
                PendingItems = static () => [],
                AcknowledgePostedAsync = static (_, _) => ValueTask.CompletedTask
            });
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
        }

        await ((IRemindable)this).ReceiveReminder(firstProcessor.ReminderName, default);
        return error;
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private OutboxProcessorOptions<OutboxProcessorTestEvent> CreateOptions(
        Func<ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>>> pendingItems,
        TimeSpan? processingTimeout = null,
        TimeSpan? retryDelay = null) => new()
        {
            PendingItems = pendingItems,
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            ProcessingTimeout = processingTimeout ?? TimeSpan.FromSeconds(20),
            RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100)
        };

    private ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken)
    {
        state.State.AcknowledgedCount += items.Length;
        return ValueTask.CompletedTask;
    }

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        state.State.FailedCount += failures.Length;
        return ValueTask.CompletedTask;
    }
}

public sealed class OutboxProcessorReconciliationSchedulingGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxProcessorReconciliationSchedulingGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent>? processor;
    private ImmutableArray<string> writes = [];

    public async Task PublishInBackgroundAsync(string value, bool interleaveReconciliation)
    {
        processor ??= this.RegisterOutboxProcessor(new OutboxProcessorOptions<OutboxProcessorTestEvent>
        {
            PendingItems = () => state.State.Outbox?.ToImmutableArray() ?? [],
            AcknowledgePostedAsync = AcknowledgePostedAsync,
            ReconcileFailedAsync = ReconcileFailedAsync,
            RetryDelay = TimeSpan.FromMilliseconds(100),
            InterleaveReconciliationCallbacks = interleaveReconciliation
        })
        .AddPostman<OutboxProcessorTestEvent>(async (message, _, cancellationToken) => await PostWithGateAsync(message, cancellationToken));

        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
        await processor.PostInBackgroundAsync();
    }

    public async Task PostAgainAsync(CancellationToken cancellationToken)
    {
        var gate = OutboxProcessorSchedulingGate.For(this.GetPrimaryKey());
        gate.ForegroundPostStarted.SetResult();
        await processor!.PostAsync(cancellationToken);
    }

    public async Task ClearAndPostInBackgroundAsync(CancellationToken cancellationToken)
    {
        state.State.Outbox = EnsureOutbox().Clear();
        await state.WriteStateAsync(cancellationToken);
        await processor!.PostInBackgroundAsync(cancellationToken);
    }

    public Task RecordWriteAsync(string value)
    {
        writes = writes.Add(value);
        return Task.CompletedTask;
    }

    public Task<OutboxProcessorSchedulingState> GetSchedulingStateAsync() =>
        Task.FromResult(new OutboxProcessorSchedulingState(state.State.AcknowledgedCount, writes));

    private async Task PostWithGateAsync(
        OutboxProcessorTestEvent envelope,
        CancellationToken cancellationToken)
    {
        var gate = OutboxProcessorSchedulingGate.For(this.GetPrimaryKey());
        gate.DispatchStarted.SetResult();
        await gate.AllowDispatch.Task.WaitAsync(cancellationToken);
    }

    private async ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken)
    {
        var gate = OutboxProcessorSchedulingGate.For(this.GetPrimaryKey());
        gate.ReconciliationStarted.SetResult();
        await gate.AllowReconciliation.Task.WaitAsync(cancellationToken);

        var outbox = EnsureOutbox();
        foreach (var item in items)
        {
            outbox = outbox.Remove(item.Id);
        }

        state.State.Outbox = outbox;
        state.State.AcknowledgedCount += items.Length;
        writes = writes.Add("acknowledged");
        await state.WriteStateAsync(cancellationToken);
    }

    private ValueTask ReconcileFailedAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
}
