# Timers and Reminders

## Grain timers

Grain timers are supported. A timer callback is a grain-internal method call and also typically writes grain state, so it produces **both** grain call and storage activity signals that `WaitForAssertionAsync` can observe.

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
```
<sup><a href='/samples/Egil.Orleans.Testing.Samples/TimerSample.cs#L64-L81' title='Snippet source file'>snippet source</a> | <a href='#snippet-timer_test' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The storage write inside `OnTimerTickAsync` triggers the collector, causing `WaitForAssertionAsync` to retry the assertion.

## Orleans reminders

Orleans reminders use a durable, distributed-clock mechanism managed by the Orleans runtime. In the fast `InProcessTestCluster` test harness, reminders registered with `UseInMemoryReminderService()` may fire, but the timing is coarse-grained and not as reliable as grain timers for deterministic testing.

> **Tip:** Recent Orleans versions add `TimeProvider` support to reminders. If your Orleans version supports it, you can register a `ManualTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) in the silo and advance time programmatically. This lets you drive reminder callbacks deterministically without waiting for real wall-clock time.

For most testing scenarios, prefer grain timers (which fire deterministically and quickly in `InProcessTestCluster`) for fast feedback. Reserve reminder-based tests for longer-running integration test suites where the silo has sufficient time to tick the reminder service, or use `ManualTimeProvider` if your Orleans version supports it.
