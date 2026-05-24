# Orleans grain testing

For Orleans, this repo provides deterministic async assertions via [Egil.Orleans.Testing](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing): grain calls and storage activity are observed and can trigger assertion retries.

## 1. Direct grain calls first

If a grain exposes a direct method for the state change you need to trigger, call it and assert immediately.

```csharp
var grain = fixture.GetGrain<IMyGrain>("id");
await grain.ApplyAsync(input);
var state = await grain.GetStateAsync();
Assert.True(state.IsComplete);
```

Use direct calls when there is no async boundary (stream fan-out, one-way, reminder, timer).

## 2. WaitForAssertionAsync for async boundaries

Use `WaitForAssertionAsync` when work completes asynchronously (streams, reminders, timers, one-way calls).

```csharp
await sourceGrain.PublishAsync(event);
await fixture.WaitForAssertionAsync(async () =>
{
    var result = await derivedGrain.GetResultAsync();
    Assert.Equal(expected, result);
});
```

How it works:

- Immediate pre-flight assert.
- Retry on matching activity signals.
- Default timeout with per-call override.
- Debugger attached bypasses timeout behavior.

## 3. Collector feeds

`GetGrainCallsAsync` and `GetStorageOperationsAsync` expose operation streams when you need ordering/counting semantics.

## Anti-patterns

| Anti-pattern | Better approach |
|---|---|
| `Task.Delay` / `Thread.Sleep` | `WaitForAssertionAsync` |
| Polling state manually | Event-driven retry via `WaitForAssertionAsync` |
| Forwarder streams + manual wait | Direct call when possible |
| Asserting internal storage internals | Assert observable state/behavior |
