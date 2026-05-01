namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorReminderWithoutStorageObserverTests
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_reminder_without_storage_observer()
    {
        var collector = new GrainActivityCollector();
        var reminderClock = new ReminderTestClock();
        await using var cluster = await TestClusterFactory.DeployAsync(collector, collectStorageActivity: false, reminderClock: reminderClock);
        var grain = cluster.Client.GetGrain<IReminderActivityGrain>(Guid.NewGuid().ToString("N"));

        // Register the reminder.
        await grain.StartReminderAsync("ready");

        // Start waiting — WaitForAssertionAsync retries on grain call signals alone.
        var waitTask = collector.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            ct: TestContext.Current.CancellationToken);

        // Advance the deterministic clock to trigger the reminder callback.
        await reminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        await waitTask;
    }
}
