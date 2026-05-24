# Fakes

We use hand-built fakes for new test code.

## Where to fake

Use fakes at system boundaries that are hard to control, slow, or nondeterministic:

- External HTTP clients / adapters
- Storage and message brokers
- Clocks (`TimeProvider`/`ManualTimeProvider`)
- Randomness

Do not fake domain services that are part of the behavior you want to verify.

## Mocks are a default red flag

If you reach for a mock library, document the justification:

- production code quality would worsen, or
- maintenance cost would increase, or
- test would become flaky/non-deterministic.

"Faster to set up" is not enough justification.

## Naming

`Fake<RealTypeName>` is the preferred naming pattern.

Examples:

- `FakePaymentClient` ← `IPaymentClient`
- `FakeInventoryStore` ← `IInventoryStore`
- `FakeNotifier` ← `INotifier`

## Fake design

- Implement the real interface.
- Model behavior, not just fixed returns.
- Expose state you want to assert on (collections, counters, call records).
- Accept and use the test-time `TimeProvider` when timing is relevant.

## Fixture integration

For shared test contexts:

1. Add the fake to the test fixture.
2. Register it in fixture setup/DI.
3. Expose it to tests.

Lift reusable fakes to `test/<Project>.Tests/Fakes` as soon as more than one test uses them.
