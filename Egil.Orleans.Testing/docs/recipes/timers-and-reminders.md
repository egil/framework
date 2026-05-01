# Timers and Reminders

## Grain timers

Grain timers are supported. A timer callback is a grain-internal method call and also typically writes grain state, so it produces **both** grain call and storage activity signals that `WaitForAssertionAsync` can observe.

Example: register a grain timer that fires after 1 ms:

```csharp
public async Task StartTimerAsync(string value)
{
    state.State.PendingValue = value;
    await state.WriteStateAsync();

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

private async Task OnTimerTickAsync(CancellationToken cancellationToken)
{
    state.State.LastValue = state.State.PendingValue;
    await state.WriteStateAsync();
}
```

Assert the timer callback result:

```csharp
await grain.StartTimerAsync("timer-value");

await collector.WaitForAssertionAsync(async () =>
{
    Assert.Equal("timer-value", await grain.GetLastValueAsync());
});
```

The storage write inside `OnTimerTickAsync` triggers the collector, causing `WaitForAssertionAsync` to retry the assertion.

## Orleans reminders

Orleans reminders are **not supported** in fast `InProcessTestCluster` tests.

Reminders are durable, distributed-clock events managed by the Orleans runtime. Even with `UseInMemoryReminderService()` registered and a 1-second due time, reminders do not reliably fire within the fast in-process test harness.

If you need to test reminder-driven behavior, use a dedicated longer-running integration test environment where the silo has sufficient time to tick the reminder service, rather than a shared fast test suite.
