namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorReminderTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_reminder_state_change()
    {
        var grain = fixture.GetUniqueGrain<IReminderActivityGrain>();

        // Register the reminder — it has a 1-minute due time matching the clock's minimum.
        await grain.StartReminderAsync("ready");

        // Start waiting for the assertion. The collector retries each time grain activity is observed.
        var waitTask = fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            ct: TestContext.Current.CancellationToken);

        // Advance the deterministic clock past the reminder due time to trigger the callback.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_observes_reminder_callbacks()
    {
        var grain = fixture.GetUniqueGrain<IReminderActivityGrain>();

        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            grain,
            context => context.MethodName == nameof(IRemindable.ReceiveReminder),
            ct: TestContext.Current.CancellationToken);

        await grain.StartReminderAsync("ready");

        // Advance the deterministic clock past the reminder due time.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        await waitTask;
    }
}
