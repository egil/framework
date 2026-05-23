---
name: testing
description: 'Backend testing for this repo. Routes to topic sub-docs covering TDD, the test-after red-green workflow, good-vs-bad tests, hand-built fakes, fluent domain builders, Orleans grain testing, parallelization, prod/test run discipline, refactoring triggers, and interface design. Load when writing, running, repairing, or reviewing any backend test.'
---

# Testing

This skill is an **orchestrator**. The core philosophy and decision flow below apply to every test change. Load the matching sibling doc on demand for depth.

## Philosophy

Tests verify **behavior through public interfaces**, not implementation details. Code can change entirely; tests shouldn't. Good tests read like a specification — they describe *what* the system does, not *how* it does it.

We prefer **integration-style tests**: real code paths through real interfaces, with hand-built fakes only at system boundaries (HTTP clients, blob storage, time, randomness). The Orleans test silo (`PricingEngineSiloFixture`) uses in-memory storage providers, in-memory streams, and in-process grains — that *is* the integration boundary for grain code.

See [tests.md](tests.md) for examples and [fakes.md](fakes.md) for boundary rules.

## Decision flow

```
Adding a test?
├── Production code does not yet exist → TDD     → see [tdd-workflow.md]
└── Production code already exists     → test-after → see [test-after.md]

Setting up test data?
├── 1–2 trivial params           → raw constructor
├── Multi-field, reused, or shaped per scenario → builder → see [builders.md]
└── External boundary (HTTP/DB/time/queue) → fake     → see [fakes.md]

Test target is an Orleans grain?
├── Grain implements IAsyncObserver<T>            → direct OnNextAsync, assert
├── Trigger fans out via stream/[OneWay]/timer    → WaitForAssertionAsync
└── Need to observe the event stream itself       → GetGrainCallsAsync / GetStorageOperationsAsync feeds
                                                  → see [orleans.md]

About to run tests or commit?
├── Both src/** and test/** changed since last green run → re-run, see [commit-discipline.md]
└── Every commit checks out green                       → ship
```

## Properties to aim for

These are **properties**, not hard rules — in the spirit of [CUPID](https://cupid.dev/properties/). Each captures a quality we want a test to have, and they are the **default**.

**Deviating from a property requires an argued justification.** Acceptable reasons are narrow:

- Following the property would lower production code quality (e.g. forces a hack into prod just to make a test work).
- Following it would lower test maintainability (e.g. produces a test that's harder to read, longer, or more fragile than the alternative).
- Following it would make tests slow.
- Following it would make tests non-deterministic (the SUT depends on a clock you don't own — see [tests.md](tests.md)).

If the agent proposes deviating from a property, it must state which of the above reasons applies and why the alternative is worse. "It's easier this way" or "existing code does it" are not reasons. Items marked 🚩 are red flags: deviation should be exceptional and explicitly argued.

1. **xUnit v3.** `[Fact]` / `[Theory]`, `TestContext.Current.CancellationToken`, `IClassFixture<T>` for shared silos.
2. 🚩 **Cyclomatic complexity = 1 in test methods.** `foreach`, `try/catch/finally`, `if/else`, `switch`, ternaries, and LINQ with side effects are red flags in test code — they make tests harder to read and hide which assertion failed. Prefer `Assert.All` over `foreach`, `IAsyncDisposable` scoping over `try/finally`, and separate test methods over conditional branches. A test method should be a straight-line sequence of arrange → act → assert steps.
3. **Name the scenario, not the method.** `Add_item_to_empty_shopping_cart`, not `TestAdd1`.
4. 🚩 **Hand-built fakes, not mocks.** Mock libraries (NSubstitute, Moq) are a red flag in new code. Sole legacy zone: `Clever.PricingEngine.Client.Tests`. See [fakes.md](fakes.md).
5. **Domain builders for non-essential setup.** Builders use high-level business language (`WithTimeOfDayPeriods(2)`), not naive `WithProperty(value)` — though raw `WithX` is fine for genuinely one-off setup. See [builders.md](builders.md).
6. **`ManualTimeProvider` when time matters.** Use `ManualTimeProvider` (from `TimeProviderExtensions`) whenever a timestamp influences an assertion or is needed for determinism. When the timestamp is incidental — a builder default, a fake's bookkeeping field that no test reads — `DateTimeOffset.UtcNow` is fine and avoids threading a `TimeProvider` everywhere. See [parallelization.md](parallelization.md) for time-advance isolation.
7. **Typed IDs.** Prefer `LocationId`, `EvseId`, `SessionId` over raw `string` / `Guid` in tests — the compiler catches confusion and tests read at the domain level.
8. **Explicit assertions preferred over snapshot tests.** Use Verify when the snapshot *is* the contract (wire formats, API specs, replay debugging). Reach for explicit assertions otherwise — snapshots hide which part of the output is the behavior under test.
9. **Strongly prefer custom assertions via `extension(Assert)`.** When a multi-field assertion is reused across tests, extract it as an `extension(Assert)` method (C# 14 extension blocks) in the `Xunit` namespace — keeps test assertions consistent with built-in `Assert.*` calls. If the helper is only needed by one test class, keep it file-scoped in that test file. Place broader domain-specific extensions as `internal` in the owning test project, and general reusable extensions as `public` in `TestingUtils/Xunit/AssertExtensions.cs`.
   ```csharp
   // Xunit namespace → available everywhere Assert is
   internal static class IngestionAssertExtensions
   {
       extension(Assert)
       {
           public static void IngestorRunning(IngestionSourceDto source) { ... }
       }
   }
   // Usage: Assert.IngestorRunning(source);  Assert.All(sources, Assert.IngestorRunning);
   ```
10. **AAA at the same abstraction level.** If Arrange has many low-level steps but Act/Assert are high-level, refactor Arrange into a builder method or local factory. See [refactoring.md](refactoring.md).
11. **Direct grain RPC first.** For grains implementing `IAsyncObserver<T>`, call `grain.OnNextAsync(event)` directly — no waiting needed. Reach for `WaitForAssertionAsync` only when crossing a stream / `[OneWay]` boundary. 🚩 `Task.Delay` / `Thread.Sleep` are a red flag (sole exception: clocks you don't own — see [tests.md](tests.md)). See [orleans.md](orleans.md).
12. **Commit discipline.** Between any two test-suite runs, change *either* prod or test, never both — otherwise a regression has no attributable cause. Every commit must check out green. See [commit-discipline.md](commit-discipline.md).
13. **Prune low-value tests.** Don't keep TDD scaffolding (asserting an enum value exists, a type is registered) once a higher-level integration test covers the same behavior.
14. **Test files mirror source layout.** `test/<Project>.Tests/<Folder>/<Class>Tests.cs` ↔ `src/<Project>/<Folder>/<Class>.cs`. Builders and fakes in `TestingUtils` mirror the same domain-area folders.

## Sub-docs

| Topic | File |
|---|---|
| Red-green-refactor for new code | [tdd-workflow.md](tdd-workflow.md) |
| Validation cycle for tests added to existing code | [test-after.md](test-after.md) |
| Good vs bad tests, AAA, naming, snapshots | [tests.md](tests.md) |
| Hand-built fakes, where they live, naming | [fakes.md](fakes.md) |
| Fluent domain builders, high-level language | [builders.md](builders.md) |
| Orleans grain RPC, `WaitForAssertionAsync`, anti-patterns | [orleans.md](orleans.md) |
| End-to-end tests against the real `AppHost` (`ClusterFixture`) | [e2e.md](e2e.md) |
| Class-level parallelism, fixture isolation, time-advance | [parallelization.md](parallelization.md) |
| Prod/test run discipline, every commit green | [commit-discipline.md](commit-discipline.md) |
| PE-specific refactor triggers | [refactoring.md](refactoring.md) |
| Designing interfaces for testability, deep modules | [interface-design.md](interface-design.md) |

## Running tests

Canonical commands live in [development-commands](../development-commands/SKILL.md). Highlights for tight loops:

- Whole solution: `dotnet test`.
- One project: `dotnet test test/<Project>.Tests/<Project>.Tests.csproj`.
- One class / test: `dotnet test --filter "FullyQualifiedName~MyClassTests"` — fastest feedback during TDD or test-after cycles.
- Hang-prone runs: append `-- RunConfiguration.TestSessionTimeout=30000`.

## Managing Verify snapshots

Use the `dotnet verify` global tool to review snapshot diffs interactively:

- `dotnet verify review` — open each pending diff one at a time; choose accept / reject / skip.
- `dotnet verify accept` — accept all pending diffs (use only when you've already reviewed them in your diff tool).
- `dotnet verify reject` — discard all pending `.received.*` files.

If the tool isn't installed: `dotnet tool install -g Verify.Tool`. After running tests that produce `*.received.*` files, run `dotnet verify review` from the repo root and walk the diffs. Commit only the `.verified.*` files; the `.received.*` files are gitignored.

## Cross-links

- Coverage tooling: [coverage-analysis](../coverage-analysis/SKILL.md), [dotnet-coverlet](../dotnet-coverlet/SKILL.md)
- Generic xUnit (non-repo-specific): [dotnet-xunit](../dotnet-xunit/SKILL.md)
- Commit message format: [commit-and-pr-guidelines](../commit-and-pr-guidelines/SKILL.md)
