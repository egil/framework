using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public enum OutboxDrainRequestHoldPhase
{
    None,
    Delivery,
    Acknowledgement,
}

[GenerateSerializer]
public sealed record OutboxDrainRequestSettings
{
    [Id(0)]
    public OutboxDrainRequestHoldPhase HoldPhase { get; init; }

    [Id(1)]
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    [Id(2)]
    public bool FailFirstItem { get; init; }

    [Id(3)]
    public bool AppendDuringSecondDelivery { get; init; }

    [Id(4)]
    public bool HoldAcknowledgementTimer { get; init; }
}

public interface IOutboxDrainRequestGrain : IGrainWithGuidKey
{
    Task InitializeAsync(OutboxDrainRequestSettings settings);
    Task PublishAsync(string value, bool reminder);
    Task PublishInForegroundAsync(string value);
    Task RequestAsync(bool reminder);
    Task RunQueuedDispatchTickAsync();
    Task PauseDispatchTimerAsync();
    Task ResumeDispatchTimerAsync();
    Task StopAsync();
    Task<OutboxProcessorSourceState> GetStateAsync();
}

public sealed class OutboxDrainRequestGrain(
    [PersistentState("state", "Default")] IPersistentState<OutboxProcessorSourceState> state)
    : Grain, IOutboxDrainRequestGrain, IOutboxGrain
{
    private OutboxProcessor<OutboxProcessorTestEvent> processor = null!;
    private OutboxDrainRequestGate gate = null!;
    private OutboxDrainRequestSettings settings = null!;
    private bool firstDelivery = true;

    internal OutboxDrainRequestGate Gate => gate;
    internal bool HoldAcknowledgementTimer => settings.HoldPhase == OutboxDrainRequestHoldPhase.Acknowledgement || settings.HoldAcknowledgementTimer;

    public Task InitializeAsync(OutboxDrainRequestSettings settings)
    {
        this.settings = settings;
        gate = OutboxDrainRequestGate.Get(this.GetPrimaryKey());
        processor = this.RegisterOutboxProcessor(() => state.State.Outbox ?? [], options =>
        {
            options.AcknowledgePostedAsync = AcknowledgePostedAsync;
            options.AcknowledgeFailuresAsync = AcknowledgeFailuresAsync;
            options.RetryDelay = settings.RetryDelay;
            // Holding an acknowledgement must still allow the test's second
            // business write to enter the grain; delivery already interleaves.
            options.InterleaveAcknowledgementCallbacks = HoldAcknowledgementTimer;
        }).AddPostman<OutboxProcessorTestEvent>(async (message, _, ct) => await DeliverAsync(message, ct));
        return Task.CompletedTask;
    }

    public async Task PublishAsync(string value, bool reminder)
    {
        await AppendAsync(value);
        await RequestAsync(reminder);
    }

    public async Task PublishInForegroundAsync(string value)
    {
        await AppendAsync(value);
        var post = processor.PostAsync().AsTask();
        gate.ForegroundStarted.TrySetResult();
        await post;
    }

    public async Task RequestAsync(bool reminder)
    {
        if (reminder)
        {
            await ((IRemindable)this).ReceiveReminder(processor.ReminderName, default);
        }
        else
        {
            await processor.PostInBackgroundAsync();
        }
    }

    public async Task StopAsync()
    {
        state.State.Outbox = EnsureOutbox().Clear();
        await state.WriteStateAsync();
        await processor.PostInBackgroundAsync();
    }

    public async Task RunQueuedDispatchTickAsync()
    {
        gate.DispatchTimer!.Change(TimeSpan.Zero, settings.RetryDelay);
        gate.DispatchTickRequested.TrySetResult();
        await gate.RequestedDispatchCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public Task PauseDispatchTimerAsync()
    {
        gate.DispatchTimer!.Pause();
        return Task.CompletedTask;
    }

    public Task ResumeDispatchTimerAsync()
    {
        gate.DispatchTimer!.Resume();
        return Task.CompletedTask;
    }

    public Task<OutboxProcessorSourceState> GetStateAsync() => Task.FromResult(state.State);

    private async Task AppendAsync(string value)
    {
        state.State.Outbox = EnsureOutbox().Add(new OutboxProcessorTestEvent(value));
        await state.WriteStateAsync();
    }

    private async Task DeliverAsync(OutboxProcessorTestEvent message, CancellationToken cancellationToken)
    {
        if (firstDelivery)
        {
            firstDelivery = false;
            gate.DispatchStarted.TrySetResult();
            if (settings.HoldPhase == OutboxDrainRequestHoldPhase.Delivery)
            {
                await gate.AllowDispatch.Task.WaitAsync(cancellationToken);
            }
        }

        if (message.Value == "A" && settings.FailFirstItem)
        {
            gate.RecordFailure();
            throw new InvalidOperationException("Leave A pending for retry.");
        }

        if (message.Value == "B" && settings.AppendDuringSecondDelivery)
        {
            await AppendAsync("C");
            await processor.PostInBackgroundAsync(cancellationToken);
        }

        gate.Delivered.Enqueue(message.Value);
        if (message.Value == "B")
        {
            gate.SecondDelivered.TrySetResult();
        }
        else if (message.Value == "C")
        {
            gate.ThirdDelivered.TrySetResult();
        }
    }

    private async ValueTask AcknowledgePostedAsync(
        ImmutableArray<OutboxMessageEnvelope<OutboxProcessorTestEvent>> items,
        CancellationToken cancellationToken)
    {
        // Remove from the current outbox by ID so a business write made while
        // the callback was held is retained for the next dispatch snapshot.
        var outbox = EnsureOutbox();
        foreach (var item in items)
        {
            outbox = outbox.Remove(item.Id);
        }

        state.State.Outbox = outbox;
        state.State.AcknowledgedCount += items.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private async ValueTask AcknowledgeFailuresAsync(
        ImmutableArray<(OutboxMessageEnvelope<OutboxProcessorTestEvent> Item, Exception Error, int Attempt)> failures,
        CancellationToken cancellationToken)
    {
        state.State.FailedCount += failures.Length;
        await state.WriteStateAsync(cancellationToken);
    }

    private Outbox<OutboxProcessorTestEvent> EnsureOutbox() =>
        state.State.Outbox ??= Outbox<OutboxProcessorTestEvent>.Create();
}

internal sealed class OutboxDrainRequestGate : IDisposable
{
    private static readonly ConcurrentDictionary<Guid, OutboxDrainRequestGate> Gates = new();
    private readonly Guid grainKey;
    private int failureAttempts;

    private OutboxDrainRequestGate(Guid grainKey) => this.grainKey = grainKey;

    public TaskCompletionSource DispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AcknowledgementTimerStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ForegroundStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AllowDispatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AllowAcknowledgement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource SecondDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ThirdDelivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource RequestedDispatchCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource DispatchTickRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AcknowledgementCallbackCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public GatedOutboxDispatchTimer? DispatchTimer { get; set; }
    public TaskCompletionSource<long> FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<long> SecondFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<long> ThirdFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ConcurrentQueue<string> Delivered { get; } = new();

    public static OutboxDrainRequestGate Create(Guid grainKey)
    {
        var gate = new OutboxDrainRequestGate(grainKey);
        if (!Gates.TryAdd(grainKey, gate))
        {
            throw new InvalidOperationException("A gate already exists for this grain.");
        }

        return gate;
    }

    public static OutboxDrainRequestGate Get(Guid grainKey) => Gates[grainKey];

    public void RecordFailure()
    {
        var signal = Interlocked.Increment(ref failureAttempts) switch
        {
            1 => FirstFailure,
            2 => SecondFailure,
            _ => ThirdFailure,
        };
        signal.TrySetResult(Stopwatch.GetTimestamp());
    }

    public void Dispose()
    {
        // Release blocked callbacks even when an assertion or watchdog fails.
        // Grains retain their gate reference until the cluster tears down.
        AllowDispatch.TrySetResult();
        AllowAcknowledgement.TrySetResult();
        DispatchTimer?.Dispose();
        Gates.TryRemove(grainKey, out _);
    }
}
