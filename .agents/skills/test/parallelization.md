# Test parallelization

## Parallelization model

xUnit runs test classes in parallel by default; test methods within a class are usually sequential. Use class boundaries to increase isolation.

- Keep classes small and fixtures explicit.
- Generate unique ids per test.
- Avoid mutable shared state across classes.

## Time-provider isolation

If tests run in a shared fixture and one advances time (manual clock), keep time-advancing scenarios isolated:

1. Test the pure component with its own `ManualTimeProvider`.
2. Keep one time-advancing test per class.
3. As a last resort, document deterministic order requirements in the test header.

## Bulk failure signal

If a whole class fails in the same way, first inspect shared setup (`InitializeAsync`, fixture factory helpers, default state/setup).
