namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorReminderTests(OrleansReminderTestClusterFixture fixture)
    : IClassFixture<OrleansReminderTestClusterFixture>
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_reminder_state_change()
    {
        var grain = fixture.GetUniqueGrain<IReminderActivityGrain>();

        // Register the reminder — it has a 1-minute due time matching the clock's minimum.
        await grain.StartReminderAsync("ready");

        // Advance the deterministic clock past the reminder due time to trigger the callback.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        await fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetGrainCallsAsync_observes_reminder_callbacks()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = fixture.GetUniqueGrain<IReminderActivityGrain>();

        var collectTask = fixture.Collector
            .GetGrainCallsAsync(grain, cancellationToken: ct)
            .Where(ctx => ctx.MethodName == nameof(IRemindable.ReceiveReminder))
            .Take(1)
            .ToListAsync(ct)
            .AsTask();

        await grain.StartReminderAsync("ready");

        // Advance the deterministic clock past the reminder due time.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), ct);

        var collected = await collectTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.Single(collected);
    }
}
