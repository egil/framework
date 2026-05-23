# Test parallelization

How tests are parallelized in this repo and the diagnostic heuristic for bulk-failure investigation.

## Parallelization model

xUnit v3 runs test **classes** in parallel but tests **within a class** sequentially. To maximize parallelism:

- **Split large test classes** into multiple smaller classes sharing the same fixture type — each class gets its own `IClassFixture<T>` instance = own silo + own `ManualTimeProvider`.
- **Use unique grain IDs per test** — avoid reusing `LocationId` / `EvseId` / `SessionId` across tests in the same class.
- **Avoid shared mutable state** across classes; rely on fixture instance isolation.

## ManualTimeProvider isolation

Each `PricingEngineSiloFixture` instance has its own `ManualTimeProvider`. Tests that advance time within one fixture do **not** affect another fixture instance.

**But**: tests sharing the same fixture share that `ManualTimeProvider`. Tests that advance time can collide:

- Test A advances time by 1h to trigger a timer.
- Test B (same class, runs after A) assumes time at fixture's initial value — fails.

**Mitigation strategies, in order of preference:**

1. **Isolate the component.** If the code under test is a pure function or a self-contained service, test it directly with its own `ManualTimeProvider`, not through `PricingEngineSiloFixture`. No fixture sharing → no time conflicts.
2. **One time-advancing test per class.** Put it alone in its own test class; xUnit gives that class its own fixture instance.
3. **Order-dependent suite (last resort).** Document the order requirement in the test class header. Fragile — prefer the first two options.

## Diagnostic heuristic

If all tests in a test class fail for the same reason, check the shared setup first — a broken setup is the most common cause of bulk failures:

- `CreateSut` factory method.
- Fixture constructor.
- `IAsyncLifetime.InitializeAsync`.
- `SetupDefaultPricingRules` overrides.

A single per-test failure is more likely a real bug; a bulk failure is almost always shared scaffolding.

## Live-environment fixture

`PricingEngineLiveEnvironmentFixture` (derives from `PricingEngineSiloFixture`) is a **debugging tool**, not a regular test fixture. It uses real CPMS clients and downloads production grain state via `GrainStateDownloader` to recreate production problems locally. Tests using it are typically marked `[Fact(Skip = "Example")]` and run on demand by a developer investigating an incident.

Replay examples: `ReplayChargingSessionTests`, `ReplayLocationMessageTests`. Don't model normal test scenarios on these — they hit the network, depend on Azure auth, and use real `UtcNow`.

## Specialized fixtures

The `PricingEngineSiloFixture(bool useRealCpmsClient)` constructor and `protected virtual ConfigureSilo` / `SetupDefaultPricingRules` exist so you can derive a more specialized fixture (the live-env one is the current example). Lift fixture customization into a derived class when two or more test classes need the same non-default silo configuration; otherwise keep it inline.
