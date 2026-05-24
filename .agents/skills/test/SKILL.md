---
name: test
description: 'Orchestration and guidance for backend testing changes, from unit and Orleans tests to host-level and commit discipline.'
---

# Testing

This skill is an **orchestrator** for backend test changes. Use sibling docs on demand depending on the task.

## Philosophy

Tests validate behavior through **public interfaces** and should be resilient to internal refactors. Prefer integration-style tests with real components and hand-built fakes only at true boundaries (HTTP clients, storage, time, randomness).

In this repo, Orleans tests usually exercise real grains through a test cluster and let an `IGrainActivityCollector` observe asynchronous follow-up work.

See [tests.md](tests.md) for examples and [fakes.md](fakes.md) for boundary rules.

## Decision flow

```text
Adding a test?
├── Production code does not yet exist → TDD     → see [tdd-workflow.md](tdd-workflow.md)
└── Production code already exists     → test-after → see [test-after.md](test-after.md)

Setting up test data?
├── 1–2 trivial params           → raw constructor
├── Reused shape / non-trivial setup → builder → see [builders.md](builders.md)
└── External boundary (HTTP/storage/time/randomness) → fake → see [fakes.md](fakes.md)

Test target is an Orleans grain?
├── Grain implements observer/event method directly  → direct call, then assert
├── Asynchronous path via stream/one-way/timer/reminder → WaitForAssertionAsync
└── Need low-level operation traces                    → collector feeds
                                                  → see [orleans.md](orleans.md)

About to run tests or commit?
├── Both src/ and test/ changed since last green run → re-run, see [commit-discipline.md](commit-discipline.md)
└── Every commit green                                  → ship
```

## Properties to aim for

These are **properties**, not hard rules.

1. **xUnit v3 style**. `[Fact]` / `[Theory]`, `TestContext.Current.CancellationToken`, and `IClassFixture<T>`/shared fixtures for heavy setup.
2. 🚩 **Keep test methods straight-line**. `foreach`, `if/else`, `try/finally`, `switch`, and LINQ with side effects are red flags.
3. **Name the scenario, not the method**. `Add_item_to_empty_shopping_cart`, not `Test1`.
4. 🚩 **Hand-built fakes, not mocks** for new tests. Use mocks only for legacy zones you explicitly retain.
5. **Builders for non-essential setup**. Use builders to expose meaningful intent, not raw parameter noise.
6. **`ManualTimeProvider` when time is part of behavior**. Deterministic time wins.
7. **Typed IDs where your codebase has them** (e.g., `OrderId`, `CustomerId`).
8. **Prefer explicit assertions over snapshots**; use Verify only when the contract is the full payload/spec.
9. **Prefer shared assert helpers**. Repeated multi-field checks should become `Assert` extensions in an appropriate test utility namespace.
10. **AAA at matching abstraction level**. If arrange is low-level while act/assert are domain level, raise an abstraction helper/factory.
11. **Direct grain RPC first**. For grains exposing direct methods, call and assert directly. Use `WaitForAssertionAsync` only for async boundaries.
12. **Commit discipline**. Between suite runs, change either prod or tests, not both.
13. **Drop low-value tests** once higher-level tests cover the same behavior.
14. **Mirror source/test structure** (`test/<Project>.Tests/<Folder>/<Class>Tests.cs` ↔ `src/<Project>/<Folder>/<Class>.cs`).

## Sub-docs

| Topic | File |
|---|---|
| Red-green-refactor for new code | [tdd-workflow.md](tdd-workflow.md) |
| Validation cycle for tests on existing code | [test-after.md](test-after.md) |
| Good vs bad tests and naming | [tests.md](tests.md) |
| Hand-built fakes | [fakes.md](fakes.md) |
| Fluent domain builders | [builders.md](builders.md) |
| Orleans grain testing | [orleans.md](orleans.md) |
| End-to-end / host-level tests | [e2e.md](e2e.md) |
| Parallelism and fixture isolation | [parallelization.md](parallelization.md) |
| Commit discipline and green-state commits | [commit-discipline.md](commit-discipline.md) |
| Refactor triggers | [refactoring.md](refactoring.md) |
| Interface design for testability | [interface-design.md](interface-design.md) |

## Running tests

Use solution-level commands when possible:

- Whole solution: `dotnet test <solution>.slnx`
- One project: `dotnet test <test-project>.csproj`
- One class/test: `dotnet test --filter "FullyQualifiedName~MyClassTests"`
- Hang-prone runs: append `-- RunConfiguration.TestSessionTimeout=30000` (where supported)

## Managing Verify snapshots

Use `dotnet verify`:

- `dotnet verify review`
- `dotnet verify accept`
- `dotnet verify reject`

Commit only `.verified.*` files.

## Cross-links

- If you are working in this repo, this skill pairs well with [Egil.Orleans.Testing docs](../Egil.Orleans.Testing/README.md)


