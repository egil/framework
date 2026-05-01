namespace Egil.Orleans.Testing.Tests;

public class GrainActivityCollectorReminderTests(OrleansTestClusterFixture fixture)
{
    [Fact]
    public async Task WaitForAssertionAsync_observes_reminder_state_change()
    {
        var grain = fixture.GetUniqueGrain<IReminderActivityGrain>();
        var waitTask = fixture.WaitForAssertionAsync(
            async () => Assert.Equal("ready", await grain.GetLastValueAsync()),
            timeout: TimeSpan.FromSeconds(20),
            ct: TestContext.Current.CancellationToken);

        await grain.StartReminderAsync("ready");
        await waitTask;
    }

    [Fact]
    public async Task WaitForGrainCallAsync_observes_reminder_callbacks()
    {
        var grain = fixture.GetUniqueGrain<IReminderActivityGrain>();
        var waitTask = fixture.Collector.WaitForGrainCallAsync(
            grain,
            context => context.MethodName == nameof(IRemindable.ReceiveReminder),
            timeout: TimeSpan.FromSeconds(3),
            ct: TestContext.Current.CancellationToken);

        await grain.StartReminderAsync("ready");
        await waitTask;
    }
}
