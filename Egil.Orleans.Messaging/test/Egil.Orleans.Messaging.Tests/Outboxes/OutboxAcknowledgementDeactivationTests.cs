using System.Collections.Immutable;

namespace Egil.Orleans.Messaging.Tests.Outboxes;

public sealed class OutboxAcknowledgementDeactivationTests(OutboxReminderFixture fixture)
    : IClassFixture<OutboxReminderFixture>
{
    [Fact]
    public async Task Deactivation_waits_for_acknowledgement_failure_before_registering_recovery()
    {
        var grain = fixture.GetUniqueGrain<IOutboxAcknowledgementDeactivationGrain>();
        var gate = OutboxProcessorSchedulingGate.For(grain.GetPrimaryKey());
        try
        {
            await grain.PublishAsync();
            await gate.AcknowledgementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(fixture.Deactivations.WaitForEntryAsync(grain.GetGrainId()).IsCompleted);
            Assert.Empty((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
            gate.AllowAcknowledgement.TrySetResult();
            await fixture.Deactivations.WaitForEntryAsync(grain.GetGrainId())
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await fixture.Deactivations.WaitForCompletionAsync(grain.GetGrainId())
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var reminder = Assert.Single((await fixture.Reminders.ReadRows(grain.GetGrainId())).Reminders);
            Assert.Equal(TimeSpan.FromMinutes(5), reminder.Period);
            Assert.Equal(new ReminderApiCounts(0, 1, 0), fixture.ReminderApi.For(grain.GetGrainId()));
            Assert.Equal(1, await grain.GetPendingCountAsync());
        }
        finally
        {
            gate.AllowAcknowledgement.TrySetResult();
            OutboxProcessorSchedulingGate.Remove(grain.GetPrimaryKey());
        }
    }
}

public interface IOutboxAcknowledgementDeactivationGrain : IGrainWithGuidKey
{
    Task PublishAsync();
    Task<int> GetPendingCountAsync();
}

public sealed class OutboxAcknowledgementDeactivationGrain(
    [PersistentState("state", "Payload")] IPersistentState<OutboxReminderState> state,
    OutboxDeactivationProbe deactivations) : Grain, IOutboxAcknowledgementDeactivationGrain, IOutboxGrain
{
    private OutboxProcessor<string> processor = null!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        processor = this.RegisterOutboxProcessor(() => state.State.Outbox, options =>
        {
            options.AcknowledgePostedAsync = AcknowledgeAsync;
        }).AddPostman<string>(static _ => ValueTask.CompletedTask);
        return Task.CompletedTask;
    }

    public async Task PublishAsync()
    {
        state.State.Outbox = state.State.Outbox.Add("pending");
        await state.WriteStateAsync();
        deactivations.Observe(GrainContext);
        await processor.PostInBackgroundAsync();
    }

    public Task<int> GetPendingCountAsync() => Task.FromResult(state.State.Outbox.Count);

    private async ValueTask AcknowledgeAsync(
        ImmutableArray<OutboxMessageEnvelope<string>> items, CancellationToken cancellationToken)
    {
        var gate = OutboxProcessorSchedulingGate.For(this.GetPrimaryKey());
        state.State.Outbox = state.State.Outbox.RemoveRange(items);
        // Hold the persistence outcome after clearing memory, with deactivation
        // requested, to verify the processor's shutdown hook sees the final state.
        DeactivateOnIdle();
        gate.AcknowledgementStarted.TrySetResult();
        await gate.AllowAcknowledgement.Task;
        // Restore the durable snapshot after the failed write, as an owning
        // grain must do before exposing its outbox accessor to another turn.
        await state.ReadStateAsync();
        throw new InvalidOperationException("Acknowledgement persistence failed.");
    }
}
