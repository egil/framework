# Orleans grain testing

How to test Orleans grains in this repo. Async waiting is provided by [Egil.Orleans.Testing](https://github.com/egil/framework/tree/main/Egil.Orleans.Testing) — its `GrainActivityCollector` observes grain calls and storage operations on the silo, and retries assertions whenever activity is detected. `PricingEngineSiloFixture` wires the collector in via `siloBuilder.AddGrainActivityCollector(Collector)` and implements `IGrainActivityWaiter` so tests call `fixture.WaitForAssertionAsync(...)` directly.

## 1. Direct grain RPC via IAsyncObserver (preferred — no waiting needed)

Many grain interfaces inherit `IAsyncObserver<TEvent>` (e.g. `ILocationGrain : IAsyncObserver<ILocationInputEvent>`). When you call `grain.OnNextAsync(event)` directly (bypassing the stream), ALL processing — including group membership evaluation, state persistence, and downstream fan-out — completes before the call returns. No waiting needed.

```csharp
// ✅ Direct RPC — fully synchronous
var locationGrain = fixture.GetLocationGrain(locationId);
await locationGrain.OnNextAsync(installationUpdate);
// State is committed — assert immediately
var location = await locationGrain.GetLocationAsync();
Assert.Equal("Expected", location.Name);
```

**When to use direct RPC:**

- Setting up location, EVSE, or IdTag state from `InstallationHistoryUpdated` events.
- Sending `LocationMessage`, `SessionMessageWrapper`, or `VatUpdateEvent` directly to grains.
- Any grain whose interface inherits `IAsyncObserver<T>`.

**Important:** `InstallationHistoryUpdated` implements both `ILocationInputEvent` and `IEvseInputEvent`. Send it to BOTH grains when setting up test fixtures:

```csharp
await fixture.GetLocationGrain(locationId).OnNextAsync(update);
await fixture.GetEvseGrain(evseId).OnNextAsync(update);
```

## 2. WaitForAssertionAsync — for inter-grain async delivery

When `GrainA` sends a message to `GrainB` through streams, `[OneWay]` calls, or grain timers, direct RPC is not enough. Use `WaitForAssertionAsync`, which retries an assertion every time the collector observes a relevant grain call or storage operation:

```csharp
// GrainA (IdTagGrain) fans out to GrainB (TokenIdGrain) via stream
await idTagGrain.OnNextAsync(idTagHistoryUpdate);

// Wait for the fan-out to TokenIdGrain to complete
await fixture.WaitForAssertionAsync(tokenId, async grain =>
{
    Assert.Equal(expectedIdTag, await grain.GetIdTagAsync(utcNow));
});
```

**How it works:**

- Pre-flight: evaluates the assertion once immediately — if it passes, returns instantly.
- If pre-flight fails: subscribes to grain-activity signals (calls + storage writes) and retries on each new signal.
- Grain-scoped overloads (preferred) restrict retriggers to that grain's activity only — fastest and most precise.
- Unscoped overloads retry on any silo activity — useful when multiple grains contribute to the outcome.
- Safety-net timeout: 5 seconds default. Override per call, or via `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` env var (set to 20 on Azure pipelines for slower machines).
- Timeout bypassed when `Debugger.IsAttached` so step-through debugging works.
- On timeout, rethrows the LAST assertion exception (not a generic `TaskCanceledException`).

**Overload selection guide:**

- **Repo-specific helper extensions** live in [`DomainWaitForAssertionExtensions`](../../../test/Clever.PricingEngine.TestingUtils/DomainWaitForAssertionExtensions.cs). They wrap the `Egil.Orleans.Testing` collector so tests can call `fixture.WaitForAssertionAsync(...)` with domain IDs or existing grain references instead of spelling out collector/generic plumbing.
- **Typed-ID overloads (preferred):**
  - `WaitForAssertionAsync(IdTag, ...)` → `IIdTagGrain`
  - `WaitForAssertionAsync(TokenId, ...)` → `ITokenIdGrain`
  - `WaitForAssertionAsync(EvseId, ...)` → `IEvseGrain`
  - `WaitForAssertionAsync(SessionId, ...)` → `IChargingSessionGrain`
  - `WaitForAssertionAsync(LocationId, ...)` → `ILocationGrain`
  - `WaitForAssertionAsync(SubscriptionLineId, ...)` → `ISubscriptionLineGrain`
  - `TOutput` is inferred; no type params required.
- **Existing grain reference:** `WaitForAssertionAsync<TGrain>(grain, ...)` or the `IConsumptionPriceGroupGrain` overloads.
- **String-key with return value:** `WaitForAssertionAsync<TGrainInterface, TOutput>(string, ...)`.
- **String-key void:** `WaitForAssertionAsync<TGrainInterface>(string, ...)`.

If you need a typed-ID overload for a grain not yet listed above, add it to `DomainWaitForAssertionExtensions` rather than reaching for the string-key form in tests.

## 3. IAsyncEnumerable activity feeds — for fine-grained event observation

`Egil.Orleans.Testing` also exposes the collector's signals as `IAsyncEnumerable<T>` feeds, composable with LINQ:

- `Collector.GetGrainCallsAsync(...)` — stream of grain method invocations.
- `Collector.GetStorageOperationsAsync(...)` — stream of grain-storage reads/writes.
- Both accept `includeExisting: true` to replay recent history.

Use these when an assertion-retry loop is the wrong shape — e.g. you need to *count* operations, observe their *order*, or wait for the Nth specific event. Reference: `FakeLocationProjectionStorageReader` consumes `GetStorageOperationsAsync()` to react to projection writes.

Prefer `WaitForAssertionAsync` when "is the post-condition true yet?" is the actual question. Reach for the feeds only when the test genuinely cares about the *event stream* itself.

## Anti-patterns

| Anti-pattern | Why it's bad | Use instead |
|---|---|---|
| `Task.Delay()` / `Thread.Sleep` | Arbitrary timing, flaky on slow CI | `WaitForAssertionAsync` with post-condition |
| Forwarder stream + manual wait | Adds latency, couples to stream topology | Direct `grain.OnNextAsync()` |
| Polling storage state in a loop | Reinvents the collector, races on timing | `WaitForAssertionAsync` or `GetStorageOperationsAsync` feed |
| Asserting on internal write counts | Couples test to implementation | Assert on observable grain state via `WaitForAssertionAsync` |
| Fixed timeouts for assertions | Too long = slow suite, too short = flaky | `WaitForAssertionAsync` auto-retries on activity |
