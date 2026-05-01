# Timers and Reminders

## Grain timers

Grain timers are supported. A timer callback is a grain-internal method call and also typically writes grain state, so it produces **both** grain call and storage activity signals that `WaitForAssertionAsync` can observe.

Fixture reference: [`OrleansTestClusterFixture`](../../README.md#orleans-test-cluster-fixture)

### Timer grain example

Register a grain timer that fires after 1 ms. When the callback runs, it writes state — producing a storage activity signal the collector observes:

<!-- snippet: timer_grain_implementation -->
<a id='snippet-timer_grain_implementation'></a>
```cs
public sealed class TimerGrain(
    [PersistentState("state", "Default")] IPersistentState<TimerGrainState> state,
    ITimerRegistry timerRegistry,
    IGrainContext grainContext)
    : Grain, ITimerGrain
{
    private IGrainTimer? timer;

    public async Task StartAsync(string value)
    {
        state.State.PendingValue = value;
        await state.WriteStateAsync();

        timer?.Dispose();
        timer = timerRegistry.RegisterGrainTimer(
            grainContext,
            static (grain, ct) => grain.OnTimerTickAsync(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromMilliseconds(1),
                Period = Timeout.InfiniteTimeSpan,
            });
    }

    public Task<string?> GetLastValueAsync() => Task.FromResult(state.State.LastValue);

    private async Task OnTimerTickAsync(CancellationToken cancellationToken)
    {
        state.State.LastValue = state.State.PendingValue;
        await state.WriteStateAsync();
        timer?.Dispose();
        timer = null;
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/TimerSample.cs#L22-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-timer_grain_implementation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Timer test example

Assert the timer callback result — `WaitForAssertionAsync` retries each time storage activity is detected from the timer callback:

<!-- snippet: timer_test -->
<a id='snippet-timer_test'></a>
```cs
public sealed class TimerGrainTests(OrleansTestClusterFixture fixture) : IClassFixture<OrleansTestClusterFixture>
{
    [Fact]
    public async Task Timer_callback_updates_state()
    {
        var grain = fixture.GrainFactory.GetGrain<ITimerGrain>(Guid.NewGuid().ToString());

        // Act — trigger the grain timer.
        await grain.StartAsync("timer-value");

        // Assert — the timer callback fires asynchronously; the collector retries
        // the assertion each time grain activity (the storage write inside the
        // timer callback) is observed.
        await fixture.WaitForAssertionAsync(async () =>
        {
            Assert.Equal("timer-value", await grain.GetLastValueAsync());
        }, ct: TestContext.Current.CancellationToken);
    }
}
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/TimerSample.cs#L62-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-timer_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The storage write inside `OnTimerTickAsync` triggers the collector, causing `WaitForAssertionAsync` to retry the assertion.

## Orleans reminders

Testing reminder-driven grains requires deterministic time control so that reminder callbacks fire predictably without waiting for real wall-clock time. `ReminderTestClock` replaces the silo `TimeProvider` with a `ManualTimeProvider` and tunes `ReminderOptions` for deterministic scheduling.

> Warning: keep reminder-specific time control isolated to reminder tests. A `ReminderTestClock` stops normal time progression inside the test cluster, which can interfere with unrelated features that expect real time to move forward, including Orleans streams.

### Reminder grain example

Register a reminder that fires after 1 minute. When the callback runs, it writes state:

<!-- snippet: reminder_grain_implementation -->
<a id='snippet-reminder_grain_implementation'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/ReminderSample.cs#L22-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-reminder_grain_implementation' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Reminder fixture setup

Create a fixture that owns a `ReminderTestClock` and attaches it to the cluster. Note that **no storage observer** is needed — `WaitForAssertionAsync` retries on grain call signals alone, which includes the `ReceiveReminder` callback:

<!-- snippet: reminder_fixture -->
<a id='snippet-reminder_fixture'></a>
```cs
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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/ReminderSample.cs#L86-L133' title='Snippet source file'>snippet source</a> | <a href='#snippet-reminder_fixture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Use a dedicated reminder fixture for this setup. Do not fold `ReminderTestClock.Attach(...)` into a shared fixture that is also used by streams or other time-sensitive tests.

### Reminder test example

Advance the deterministic clock to trigger the reminder, then assert the result:

<!-- snippet: reminder_test -->
<a id='snippet-reminder_test'></a>
```cs
public sealed class ReminderGrainTests(ReminderFixture fixture) : IClassFixture<ReminderFixture>
{
    [Fact]
    public async Task Reminder_callback_updates_state()
    {
        var grain = fixture.GrainFactory.GetGrain<IReminderGrain>(Guid.NewGuid().ToString());

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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/ReminderSample.cs#L61-L82' title='Snippet source file'>snippet source</a> | <a href='#snippet-reminder_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `ReceiveReminder` callback is an incoming grain call that the collector observes. Even without a storage observer, `WaitForAssertionAsync` retries the assertion each time a grain call signal arrives.
