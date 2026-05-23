# Refactor candidates

Run the checklist after every TDD cycle (post-GREEN, never RED) and after each test-after sequence.

## General triggers

- **Duplication** → extract function / class.
- **Long methods** → break into private helpers (keep tests on the public interface).
- **Shallow modules** → combine or deepen — see [interface-design.md](interface-design.md).
- **Feature envy** → move logic to where the data lives.
- **Primitive obsession** → introduce value objects or typed IDs.
- **Existing code** the new code reveals as problematic.

## PE-specific triggers

- **AAA abstraction-level mismatch.** Arrange has many low-level steps but Act / Assert are high-level. Lift Arrange into a builder method (high-level, business-language) or a local factory in the test class. See [tests.md](tests.md).
- **Inline fake outgrows one test class.** Move it to `test/Clever.PricingEngine.TestingUtils/Fakes/` so the next test gets it for free. See [fakes.md](fakes.md).
- **Setup duplicated across 2+ test classes.** Lift a high-level method onto the existing builder (e.g., `WithTimeOfDayPeriods(2)`) rather than copy-pasting `WithX` chains. See [builders.md](builders.md).
- **Raw `string` / `Guid` IDs in tests.** Replace with typed IDs (`LocationId`, `EvseId`, `SessionId`) — tests read better and the compiler catches confusion.
- **Time-advancing test depends on shared `PricingEngineSiloFixture`.** Other tests in the class will see advanced time. Extract the component under test so it can be tested in isolation with its own `ManualTimeProvider`. See [parallelization.md](parallelization.md).
- **Snapshot test obscures intent.** If a Verify snapshot is the assertion but the test is checking one specific value, replace with an explicit assertion. See [tests.md](tests.md).
- **Mock library usage in new code.** Replace with a hand-built fake. The only exception is the legacy NSubstitute usage in `Clever.PricingEngine.Client`. See [fakes.md](fakes.md).

## Rules

- **Never refactor while RED.** Get to GREEN first.
- **Run the full suite after each refactor step** — refactoring is prod-side or test-side per step; respect [commit-discipline.md](commit-discipline.md).
- **Stop when the test reads at one abstraction.** Refactoring isn't done when "the code is clean"; it's done when the *test* communicates clearly without the reader needing to leave the file.
