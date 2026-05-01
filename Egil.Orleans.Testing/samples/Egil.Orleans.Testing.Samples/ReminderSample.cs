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

public sealed class ReminderGrainTests(ReminderFixture fixture) : IClassFixture<ReminderFixture>
{
    #region reminder_test
    [Fact]
    public async Task Reminder_callback_updates_state()
    {
        var grain = fixture.GrainFactory.GetGrain<IReminderGrain>(Guid.NewGuid().ToString());

        // Arrange — register a reminder that fires after 1 minute.
        await grain.ScheduleAsync("reminder-value");

        // Start waiting — WaitForAssertionAsync retries each time any grain
        // activity is observed, including the ReceiveReminder callback.
        var waitTask = fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("reminder-value", await grain.GetLastValueAsync());
        }, ct: TestContext.Current.CancellationToken);

        // Advance the deterministic clock past the reminder due time.
        await fixture.ReminderClock.AdvanceAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        await waitTask;
    }
    #endregion
}

// -- Fixture -----------------------------------------------------------------

#region reminder_fixture
public sealed class ReminderFixture : IAsyncLifetime, IGrainActivityWaiter
{
    private InProcessTestCluster? cluster;

    public GrainActivityCollector Collector { get; } = new();

    public ReminderTestClock ReminderClock { get; } = new();

    public IGrainFactory GrainFactory => cluster!.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new InProcessTestClusterBuilder(initialSilosCount: 1);

        // Attach the deterministic clock before configuring the silo.
        ReminderTestClock.Attach(builder, ReminderClock);

        builder.ConfigureSilo((_, siloBuilder) =>
        {
            siloBuilder.AddMemoryGrainStorage("Default");
            siloBuilder.UseInMemoryReminderService();

            // Only grain call monitoring — no storage observer needed.
            siloBuilder.AddGrainActivityCollector(Collector);
        });
        cluster = builder.Build();
        await cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        ReminderClock.Dispose();

        if (cluster is not null)
        {
            await cluster.DisposeAsync();
        }
    }

    Task<TResult> IGrainActivityWaiter.WaitForAssertionAsync<TResult>(
        Func<ValueTask<TResult>> assertion,
        Predicate<GrainActivity>? filter,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => ((IGrainActivityWaiter)Collector).WaitForAssertionAsync(assertion, filter, timeout, cancellationToken);
}
#endregion
