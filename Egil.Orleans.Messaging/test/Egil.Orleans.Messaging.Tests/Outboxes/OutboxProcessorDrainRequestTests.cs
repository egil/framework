using System.Diagnostics;
using Egil.Orleans.Testing;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxProcessorDrainRequestTests(MessagingTestClusterFixture fixture) : IClassFixture<MessagingTestClusterFixture>
{
    [Theory]
    [InlineData(OutboxDrainRequestHoldPhase.Delivery, false)]
    [InlineData(OutboxDrainRequestHoldPhase.Delivery, true)]
    [InlineData(OutboxDrainRequestHoldPhase.Acknowledgement, false)]
    [InlineData(OutboxDrainRequestHoldPhase.Acknowledgement, true)]
    public async Task Requests_during_delivery_or_acknowledgement_deliver_new_item_without_waiting_for_retry(
        OutboxDrainRequestHoldPhase holdPhase, bool reminder)
    {
        var grainKey = Guid.NewGuid();
        using var gate = OutboxDrainRequestGate.Create(grainKey);
        var grain = fixture.GrainFactory.GetGrain<IOutboxDrainRequestGrain>(grainKey);
        await grain.InitializeAsync(new OutboxDrainRequestSettings
        {
            HoldPhase = holdPhase,
        });

        await grain.PublishAsync("A", reminder: false);
        var heldPhase = holdPhase == OutboxDrainRequestHoldPhase.Delivery
            ? gate.DispatchStarted.Task
            : gate.AcknowledgementTimerStarted.Task;
        await heldPhase.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PublishAsync("B", reminder);
        await grain.RequestAsync(reminder);
        await grain.RequestAsync(reminder);

        var queuedTick = grain.RunQueuedDispatchTickAsync();
        await gate.DispatchTickRequested.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.AllowDispatch.TrySetResult();
        await queuedTick.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.AllowAcknowledgement.TrySetResult();

        await gate.AcknowledgementCallbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await gate.SecondDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.WaitForAssertionAsync(grain, async () =>
        {
            var state = await grain.GetStateAsync();
            Assert.Equal(2, state.AcknowledgedCount);
            Assert.Equal(0, state.Outbox?.Count);
            Assert.Equal(["A", "B"], gate.Delivered.ToArray());
        }, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Failed_delivery_without_a_new_request_retries_at_the_configured_delay()
    {
        var grainKey = Guid.NewGuid();
        using var gate = OutboxDrainRequestGate.Create(grainKey);
        var grain = fixture.GrainFactory.GetGrain<IOutboxDrainRequestGrain>(grainKey);
        var retryDelay = TimeSpan.FromSeconds(1);
        await grain.InitializeAsync(new OutboxDrainRequestSettings
        {
            FailFirstItem = true,
            RetryDelay = retryDelay,
        });

        await grain.PublishAsync("A", reminder: false);
        var firstAttempt = await gate.FirstFailure.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var retry = await gate.SecondFailure.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Orleans owns the real timer. Allow a small timer-resolution margin,
        // while rejecting a request flag that causes an immediate retry loop.
        Assert.True(Stopwatch.GetElapsedTime(firstAttempt, retry) >= retryDelay - TimeSpan.FromMilliseconds(100));
        await fixture.WaitForAssertionAsync(grain, async () =>
        {
            var state = await grain.GetStateAsync();
            Assert.Equal(2, state.FailedCount);
            Assert.Equal(1, state.Outbox?.Count);
        }, ct: TestContext.Current.CancellationToken);
        await grain.StopAsync();
    }

    [Fact]
    public async Task Explicit_request_during_a_failed_batch_allows_one_prompt_retry_then_returns_to_retry_delay()
    {
        var grainKey = Guid.NewGuid();
        using var gate = OutboxDrainRequestGate.Create(grainKey);
        var grain = fixture.GrainFactory.GetGrain<IOutboxDrainRequestGrain>(grainKey);
        var retryDelay = TimeSpan.FromSeconds(2);
        await grain.InitializeAsync(new OutboxDrainRequestSettings
        {
            HoldPhase = OutboxDrainRequestHoldPhase.Acknowledgement,
            FailFirstItem = true,
            RetryDelay = retryDelay,
        });

        await grain.PublishAsync("A", reminder: false);
        await gate.AcknowledgementTimerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PublishAsync("B", reminder: false);
        await grain.RunQueuedDispatchTickAsync();
        var acknowledgementReleased = Stopwatch.GetTimestamp();
        gate.AllowAcknowledgement.TrySetResult();

        var promptRetry = await gate.SecondFailure.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(Stopwatch.GetElapsedTime(acknowledgementReleased, promptRetry) < retryDelay / 2);
        await fixture.WaitForAssertionAsync(grain, async () =>
        {
            var state = await grain.GetStateAsync();
            Assert.Equal(2, state.FailedCount);
            Assert.Equal(2, state.Outbox?.Count);
        }, ct: TestContext.Current.CancellationToken);
        var scheduledRetry = await gate.ThirdFailure.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(Stopwatch.GetElapsedTime(promptRetry, scheduledRetry) >= retryDelay - TimeSpan.FromMilliseconds(100));
        await grain.StopAsync();
    }

    [Fact]
    public async Task Stale_acknowledgement_after_foreground_takeover_preserves_requested_follow_up()
    {
        var grainKey = Guid.NewGuid();
        using var gate = OutboxDrainRequestGate.Create(grainKey);
        var grain = fixture.GrainFactory.GetGrain<IOutboxDrainRequestGrain>(grainKey);
        await grain.InitializeAsync(new OutboxDrainRequestSettings
        {
            HoldPhase = OutboxDrainRequestHoldPhase.Delivery,
            HoldAcknowledgementTimer = true,
            AppendDuringSecondDelivery = true,
        });

        await grain.PublishAsync("A", reminder: false);
        await gate.DispatchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.PauseDispatchTimerAsync();
        var foreground = grain.PublishInForegroundAsync("B");
        await gate.ForegroundStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        gate.AllowDispatch.TrySetResult();
        await foreground.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await gate.AcknowledgementTimerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        gate.AllowAcknowledgement.TrySetResult();
        await gate.AcknowledgementCallbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await grain.ResumeDispatchTimerAsync();

        await gate.ThirdDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await fixture.WaitForAssertionAsync(grain, async () =>
        {
            var state = await grain.GetStateAsync();
            Assert.Equal(3, state.AcknowledgedCount);
            Assert.Equal(0, state.Outbox?.Count);
            Assert.Equal(["A", "B", "C"], gate.Delivered.ToArray());
        }, ct: TestContext.Current.CancellationToken);
    }
}
