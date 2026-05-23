# TDD workflow

Default for **new code**. Red → green → refactor, one behavior at a time.

> For tests added to **existing** production code, use the validation cycle in [test-after.md](test-after.md) instead.

## Anti-pattern: horizontal slices

**DO NOT write all tests first, then all implementation.** Bulk-written tests test *imagined* behavior, end up coupled to data shapes and signatures, and are insensitive to real changes.

```
WRONG (horizontal):
  RED:   test1, test2, test3, test4, test5
  GREEN: impl1, impl2, impl3, impl4, impl5

RIGHT (vertical):
  RED→GREEN: test1→impl1
  RED→GREEN: test2→impl2
  RED→GREEN: test3→impl3
```

Each test responds to what you learned from the previous cycle.

## Workflow

### 1. Plan

Before writing any code:

- Confirm with user what interface changes are needed.
- Confirm which behaviors to test (prioritize — you can't test everything).
- Identify opportunities for [deep modules](interface-design.md): small interface, deep implementation.
- Design interfaces for testability — see [interface-design.md](interface-design.md).
- List behaviors (not implementation steps).
- Use the project's domain glossary; respect existing ADRs in the area you're touching.

### 2. Tracer bullet

Write ONE test that confirms ONE thing end-to-end:

```
RED:   write test for first behavior → fails
GREEN: minimal code to pass          → passes
```

Proves the path works.

### 3. Incremental loop

For each remaining behavior:

```
RED:   write next test → fails
GREEN: minimal code to pass → passes
```

Rules:

- One test at a time.
- Only enough code to pass the current test.
- Don't anticipate future tests.
- Keep tests focused on observable behavior.
- Use [builders.md](builders.md) and [fakes.md](fakes.md) from the start so the test reads at one abstraction level.

### 4. Refactor

After all tests pass, look for [refactor candidates](refactoring.md):

- Extract duplication.
- Deepen modules — move complexity behind simple interfaces.
- Apply SOLID where natural.
- Consider what new code reveals about existing code.
- Run tests after each refactor step.

**Never refactor while RED.** Get to GREEN first.

## Per-cycle checklist

- [ ] Test name reads as a scenario sentence.
- [ ] Test describes behavior, not implementation.
- [ ] Test uses public interface only.
- [ ] Test would survive an internal refactor.
- [ ] Code is minimal for this test — no speculative features.
- [ ] AAA blocks are at the same abstraction level (see [refactoring.md](refactoring.md)).
- [ ] No mocks (use fakes from [fakes.md](fakes.md)).
- [ ] Domain builders used for non-essential setup (see [builders.md](builders.md)).
- [ ] `ManualTimeProvider` used wherever a timestamp influences an assertion or determinism (incidental timestamps may use `UtcNow`).
- [ ] Typed IDs (`LocationId`, `EvseId`, `SessionId`) — no raw strings/Guids.
