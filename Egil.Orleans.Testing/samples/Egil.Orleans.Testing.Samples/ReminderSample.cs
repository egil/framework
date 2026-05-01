using Orleans.TestingHost;
using Orleans.Timers;

namespace Egil.Orleans.Testing.Samples.Reminders;

// -- Grain definitions -------------------------------------------------------

public interface IReminderGrain : IGrainWithStringKey
{
    Task ScheduleAsync(string value);

    Task<string?> GetLastValueAsync();
}

public sealed class ReminderGrainState
{
    public string? PendingValue { get; set; }

    public string? LastValue { get; set; }
}

#region reminder_grain_implementation
public sealed class ReminderGrain(
    [PersistentState("state", "Default")] IPersistentState<ReminderGrainState> state,
    IReminderRegistry reminderRegistry,
    IGrainContext grainContext)
    : Grain, IReminderGrain, IRemindable
{
    private const string ReminderName = "process-reminder";
    private IGrainReminder? reminder;

    public async Task ScheduleAsync(string value)
    {
        state.State.PendingValue = value;
        await state.WriteStateAsync();
        reminder = await reminderRegistry.RegisterOrUpdateReminder(
            grainContext.GrainId,
            ReminderName,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(5));
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        state.State.LastValue = state.State.PendingValue;
        await state.WriteStateAsync();

        if (reminder is not null)
        {
            await reminderRegistry.UnregisterReminder(grainContext.GrainId, reminder);
            reminder = null;
        }
    }
}
#endregion

// -- Tests -------------------------------------------------------------------

#region reminder_test
public sealed class ReminderGrainTests(ReminderFixture fixture) : IClassFixture<ReminderFixture>
{
    [Fact]
    public async Task Reminder_callback_updates_state()
    {
        var grain = fixture.GetUniqueGrain<IReminderGrain>();

        // Arrange — register a reminder that fires after 1 minute.
        await grain.ScheduleAsync("reminder-value");

        // Advance the deterministic clock past the reminder due time.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        // Assert after triggering the callback. WaitForAssertionAsync retries until the reminder work is visible.
        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("reminder-value", await grain.GetLastValueAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
#endregion

#region reminder_fixture
/// <summary>
/// Reusable Orleans reminder test fixture.
/// Copy this into your own test project when reminder-driven tests need deterministic time.
/// </summary>
/// <remarks>
/// This fixture extends <see cref="OrleansTestClusterFixture"/> with the one extra capability
/// reminder tests need: a dedicated <see cref="ReminderTestClock"/> that tests can advance explicitly.
/// Keep this fixture separate from general-purpose fixtures because the manual clock stops normal
/// time progression inside the cluster.
/// </remarks>
public sealed class ReminderFixture : OrleansTestClusterFixture
{
    // This manual clock is the key reminder-specific feature.
    // Tests advance it explicitly to trigger reminder callbacks without waiting on wall-clock time.
    public ReminderTestClock ReminderClock { get; } = new();

    // Reminder callbacks arrive as grain calls, so this fixture can rely on call observation alone.
    // That keeps the sample focused on the reminder-specific behavior instead of storage monitoring.
    protected override bool CollectStorageActivityFromDefault => false;

    protected override void ConfigureClusterBuilder(InProcessTestClusterBuilder builder)
    {
        // Attach the deterministic clock before configuring the silo so Orleans reminder infrastructure
        // uses the manual time provider from the start.
        ReminderTestClock.Attach(builder, ReminderClock);
    }

    protected override void ConfigureSiloBuilder(ISiloBuilder siloBuilder)
    {
        // Enable the in-memory reminder service for the test cluster.
        siloBuilder.UseInMemoryReminderService();
    }

    protected override ValueTask DisposeAsyncCore()
    {
        // Dispose the manual clock first so any reminder-specific resources are cleaned up promptly.
        ReminderClock.Dispose();
        return ValueTask.CompletedTask;
    }
}
#endregion
