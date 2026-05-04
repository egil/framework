# Egil.Orleans.Testing Library Plan

## Problem

The PricingEngine project contains reusable Orleans testing infrastructure for deterministic async assertions based on grain call collection and storage observation. This code should be extracted into a standalone NuGet package (`Egil.Orleans.Testing`) in the `egil/framework` monorepo, following the conventions established by `Egil.SystemTextJson.Migration`.

## Architecture: Unified GrainActivityCollector

A single `GrainActivityCollector` receives events from two optional, independently-enabled sources:

1. **Grain call monitoring** — an internal `GrainCallCollectionFilter` (`IIncomingGrainCallFilter`)
2. **Storage monitoring** — internal `StorageObserver` decorators wrapping `IGrainStorage` providers

Both sources emit lean `GrainActivity` events into internal channels. The collector also maintains typed channels for advanced methods (`StorageOperation`, `IIncomingGrainCallContext`). Users interact exclusively through public methods on `GrainActivityCollector` — no interfaces, no manual channel subscription.

### Event flow

```
IIncomingGrainCallFilter ──→ GrainCallCollectionFilter (internal)
                                 │ (filters assertion-scoped calls)
                                 ├──→ GrainActivity channel (lean)
                                 └──→ IIncomingGrainCallContext channel (typed)

IGrainStorage decorator ──→ StorageObserver (internal)
                                 │ (filters assertion-scoped ops)
                                 ├──→ GrainActivity channel (lean)
                                 └──→ StorageOperation channel (typed)

                          GrainActivityCollector
                                 │
                    ┌────────────┼────────────┐
                    ▼            ▼            ▼
            WaitForAssertion  WaitForStorage  WaitForGrainCall
            (lean channel)    (typed channel)  (typed channel)
```

### Registration API

```csharp
var collector = new GrainActivityCollector();

siloBuilder.AddGrainActivityCollector(collector)
    .CollectStorageActivityFromDefault()
    .CollectStorageActivityFrom("MyCustomStorage");
```

- `AddGrainActivityCollector(collector)` registers the internal call filter and returns a `GrainActivityCollectorBuilder`
- `CollectStorageActivityFromDefault()` wraps the default storage provider with a `StorageObserver`
- `CollectStorageActivityFrom(string name)` wraps a named storage provider
- If the user doesn't register storage, only grain calls are monitored (and vice versa)

### Public API — GrainActivityCollector methods (10 total)

```csharp
public sealed class GrainActivityCollector
{
    // ══════════════════════════════════════════════════════════════
    // Standard WaitForAssertion — triggers on ANY grain activity
    // ══════════════════════════════════════════════════════════════

    public Task WaitForAssertionAsync(
        Func<Task> assertion,
        TimeSpan? timeout = null, CancellationToken ct = default);

    public Task<TResult> WaitForAssertionAsync<TResult>(
        Func<Task<TResult>> assertion,
        TimeSpan? timeout = null, CancellationToken ct = default);

    // ══════════════════════════════════════════════════════════════
    // Grain-scoped WaitForAssertion — triggers only for a specific grain
    // Without grain in lambda
    // ══════════════════════════════════════════════════════════════

    public Task WaitForAssertionAsync<TGrain>(
        TGrain grain, Func<Task> assertion,
        TimeSpan? timeout = null, CancellationToken ct = default)
        where TGrain : IGrain;

    public Task<TResult> WaitForAssertionAsync<TGrain, TResult>(
        TGrain grain, Func<Task<TResult>> assertion,
        TimeSpan? timeout = null, CancellationToken ct = default)
        where TGrain : IGrain;

    // ══════════════════════════════════════════════════════════════
    // Grain-scoped WaitForAssertion — with grain passed to lambda
    // ══════════════════════════════════════════════════════════════

    public Task WaitForAssertionAsync<TGrain>(
        TGrain grain, Func<TGrain, Task> assertion,
        TimeSpan? timeout = null, CancellationToken ct = default)
        where TGrain : IGrain;

    public Task<TResult> WaitForAssertionAsync<TGrain, TResult>(
        TGrain grain, Func<TGrain, Task<TResult>> assertion,
        TimeSpan? timeout = null, CancellationToken ct = default)
        where TGrain : IGrain;

    // ══════════════════════════════════════════════════════════════
    // IAsyncEnumerable feeds — collect events with LINQ composition
    // ══════════════════════════════════════════════════════════════

    public IAsyncEnumerable<GrainActivity> GetGrainActivityAsync(
        bool includeExisting = false, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<StorageOperation> GetStorageOperationsAsync(
        bool includeExisting = false, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<StorageOperation> GetStorageOperationsAsync<TGrain>(
        TGrain grain, bool includeExisting = false, CancellationToken cancellationToken = default)
        where TGrain : IGrain;

    public IAsyncEnumerable<IIncomingGrainCallContext> GetGrainCallsAsync(
        bool includeExisting = false, CancellationToken cancellationToken = default);

    public IAsyncEnumerable<IIncomingGrainCallContext> GetGrainCallsAsync<TGrain>(
        TGrain grain, bool includeExisting = false, CancellationToken cancellationToken = default)
        where TGrain : IGrain;
}
```

## Design decisions

| Decision | Choice |
|----------|--------|
| **Package** | Single NuGet package `Egil.Orleans.Testing` |
| **Target framework** | `net10.0` |
| **C# 14 extensions** | Not needed — no provider interface, all methods on `GrainActivityCollector` directly |
| **Test framework dependency** | None — framework-agnostic core |
| **R3 dependency** | None — channel-based from the start |
| **Namespace** | All public types in `Egil.Orleans.Testing` |
| **Provider interface** | None — `GrainActivityCollector` is a concrete class, fixtures expose it directly |
| **Visibility: GrainCallCollectionFilter** | **Internal** — exposed via builder registration only |
| **Visibility: StorageObserver** | **Internal** — exposed via builder registration only |
| **Visibility: StorageOperation** | **Public** — used in `GetStorageOperationsAsync` feed methods |
| **Visibility: GrainActivity / GrainActivityKind** | **Public** — lean event type for the standard wait methods |
| **Visibility: channels** | **Internal** — users never subscribe manually |
| **Id-type overloads** | Dropped — callers resolve grains themselves |
| **Timeout API** | `TimeSpan? timeout = null, CancellationToken ct = default` on every method |
| **Default timeout** | 5 seconds, overridable via `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` env var, bypassed when `Debugger.IsAttached` |
| **Default timeout location** | `WaitForAssertionDefaults.Timeout` (public static class) |
| **Exception type** | `WaitForAssertionTimeoutException` — wraps last assertion failure as `InnerException`, includes context (grain ID, elapsed time) |
| **Self-trigger prevention** | Both sources check `RequestContext["test-assertion"]` and skip emitting during assertion scopes |
| **Registration** | Builder pattern: `siloBuilder.AddGrainActivityCollector(collector).CollectStorageActivityFrom*(...)` |
| **SiloFixture** | Reference implementation in test project (not shipped in NuGet) |
| **Tests** | Unit tests (from skill sample) + integration tests with `InProcessTestCluster` |
| **CI workflow** | Created in this PR |
| **XML documentation** | All public types and methods get full `<summary>`, `<param>`, `<returns>`, `<exception>`. Advanced methods get `<remarks>` warning about tight coupling to production internals. |

## XML documentation guidelines

All public types and methods must have thorough XML doc comments:

### Standard `WaitForAssertionAsync` methods

- `<summary>` — explain the retry-on-activity semantic: the assertion runs immediately, then re-runs each time grain activity is detected, until it passes or timeout is reached.
- `<param>` — document every parameter including timeout default behavior (env var, debugger bypass).
- `<returns>` — what the task/value represents on success.
- `<exception cref="WaitForAssertionTimeoutException">` — when thrown and what `InnerException` contains.
- `<example>` — short usage snippet showing typical assertion pattern.

### Grain-scoped overloads

- Same as above, plus explain that only activity from the specified grain triggers retries.
- For the `Func<TGrain, ...>` overloads, note that the grain reference is passed to the lambda for convenience.

### Feed methods (`GetStorageOperationsAsync`, `GetGrainCallsAsync`, `GetGrainActivityAsync`)

- `<summary>` — explain that these return IAsyncEnumerable feeds that can be composed with LINQ operators.
- `<remarks>` — **must include a coupling warning**:
  > ⚠️ **Coupling risk:** This method inspects low-level implementation details of your grain
  > (storage operations / incoming call context). Tests using this method are tightly coupled
  > to how grains persist state or which grain-to-grain calls occur internally. If the grain's
  > implementation changes (e.g., switching storage providers, refactoring internal call patterns),
  > these tests will break even if the grain's external behavior is unchanged.
  >
  > Prefer the standard `WaitForAssertionAsync` methods, which assert on observable grain behavior
  > (the "what") rather than implementation mechanics (the "how"). Use these advanced methods only
  > when the standard approach cannot express the assertion you need.

### Other public types

- `GrainActivityCollector` class — `<summary>` explaining its role as the central hub, how to register it, and the two levels of waiting (standard vs advanced).
- `GrainActivity` / `GrainActivityKind` — brief descriptions.
- `StorageOperation` / `StorageOperationKind` — document each field and enum value.
- `WaitForAssertionDefaults` — document the env var name, default value, and debugger bypass.
- `WaitForAssertionTimeoutException` — document what `InnerException` contains and how to read the message.
- `GrainActivityCollectorBuilder` — document builder chaining and each method.
- `RequestContextScope` — document purpose (assertion-scope marker for self-trigger prevention).

## Public types

| Type | Kind | Description |
|------|------|-------------|
| `GrainActivityCollector` | `sealed class` | Central collector — all 18 `WaitFor*` methods live here |
| `GrainActivity` | `readonly record struct` | Lean event: `GrainId`, `GrainActivityKind`, `DateTimeOffset` |
| `GrainActivityKind` | `enum` | `GrainCall`, `StorageWrite`, `StorageRead`, `StorageClear` |
| `GrainActivityCollectorBuilder` | `class` | Returned by `AddGrainActivityCollector`, has `CollectStorageActivityFrom*` methods |
| `GrainActivityCollectorSiloBuilderExtensions` | `static class` | `ISiloBuilder.AddGrainActivityCollector(collector)` |
| `StorageOperation` | `readonly record struct` | Detailed storage event: `GrainId`, `GrainType`, `Kind`, `ETag`, `State` |
| `StorageOperationKind` | `enum` | `Read`, `Write`, `Clear` |
| `WaitForAssertionDefaults` | `static class` | Default timeout constant + env var override |
| `WaitForAssertionTimeoutException` | `class` | Custom exception wrapping assertion failure |
| `RequestContextScope` | `static class` | `ForAssertion()` — creates scoped assertion context |

## Internal types

| Type | Description |
|------|-------------|
| `GrainCallCollectionFilter` | `IIncomingGrainCallFilter` impl — captures calls, emits to channels |
| `StorageObserver` | `IGrainStorage` decorator — captures storage ops, emits to channels |

## File structure

```
Egil.Orleans.Testing/
├── .config/
│   └── dotnet-tools.json
├── .gitignore
├── AGENTS.md
├── Directory.Packages.props
├── Egil.Orleans.Testing.slnx
├── Global.json
├── README.md
├── version.json
├── scripts/
│   └── generate-release-notes.ps1
├── src/
│   └── Egil.Orleans.Testing/
│       ├── Egil.Orleans.Testing.csproj
│       ├── GrainActivity.cs
│       ├── GrainActivityCollector.cs
│       ├── GrainActivityCollectorBuilder.cs
│       ├── GrainActivityCollectorSiloBuilderExtensions.cs
│       ├── GrainActivityKind.cs
│       ├── GrainCallCollectionFilter.cs          (internal)
│       ├── RequestContextScope.cs
│       ├── StorageObserver.cs                     (internal)
│       ├── StorageOperation.cs
│       ├── StorageOperationKind.cs
│       ├── WaitForAssertionDefaults.cs
│       └── WaitForAssertionTimeoutException.cs
└── test/
    └── Egil.Orleans.Testing.Tests/
        ├── Egil.Orleans.Testing.Tests.csproj
        ├── GrainCallCollectionFilterTests.cs
        ├── RequestContextScopeTests.cs
        ├── FakeIncomingGrainCallContext.cs
        ├── SiloFixture.cs
        └── IntegrationTests.cs
```

## Todos

### scaffold-project — Scaffold project structure

Create the library scaffolding following `Egil.SystemTextJson.Migration` conventions:

- `Egil.Orleans.Testing.slnx` — new-format solution with src/test folders
- `Directory.Packages.props` — central package management with Orleans, xunit, analyzers
- `Global.json` — SDK rollforward + `Microsoft.Testing.Platform` test runner
- `version.json` — Nerdbank.GitVersioning, version `0.1-alpha`, pathFilters, release/tag patterns
- `.config/dotnet-tools.json` — nbgv, stryker, mdsnippets
- `.gitignore` — StrykerOutput
- `src/Egil.Orleans.Testing/Egil.Orleans.Testing.csproj` — NuGet-packable, `net10.0`, embedded debug, license/readme packing, release notes target
- `test/Egil.Orleans.Testing.Tests/Egil.Orleans.Testing.Tests.csproj` — xUnit v3, MTP runner, coverlet
- `README.md` — initial readme with library description
- `AGENTS.md` — agent instructions (adapted from `Egil.SystemTextJson.Migration/AGENTS.md`)

**AGENTS.md** — adopt from `Egil.SystemTextJson.Migration/AGENTS.md` with these adaptations:

Sections to **keep as-is** (adapted to this project's paths/names):
- Audience & Scope
- Build, Test, and Development Commands (update paths to `Egil.Orleans.Testing.slnx`, no perf project)
- Coding Style & Naming Conventions (update to this project's patterns)
- Testing Guidelines — TDD process is critical, keep all three workflows:
  1. New feature/bug fix: write failing test → verify failure → implement → verify pass → run all tests
  2. New tests for existing features: inverted assertion → verify failure → correct assertion → verify pass
  3. Refactoring: refactor → run all tests
- Coverage policy: 100% branch on core components, ≥95% on remaining `/src` code
- Rule: never change both `/src` and `/test` code without running tests in between
- Commit & Pull Request Guidelines — Conventional Commits with scope `ot` (instead of `stjm`)
- Commit body text included in release notes guidance
- Context7 MCP for up-to-date library/API documentation

Sections to **drop or modify**:
- Remove perf/benchmark references (no perf project in this library)
- Remove "Serena MVP server" reference (not applicable)
- Remove `Migrations/` path references
- Update project structure section to describe: `src/Egil.Orleans.Testing/` (library), `test/Egil.Orleans.Testing.Tests/` (tests)
- Update Conventional Commit scope from `stjm` to `ot`

### implement-core-types — Implement core types and defaults

All public types must have full XML doc comments (`<summary>`, `<param>`, `<returns>` as applicable). See **XML documentation guidelines** section.

**`GrainActivity.cs`**:
```csharp
public readonly record struct GrainActivity(
    GrainId GrainId,
    GrainActivityKind Kind,
    DateTimeOffset Timestamp);
```

**`GrainActivityKind.cs`**:
```csharp
public enum GrainActivityKind { GrainCall, StorageWrite, StorageRead, StorageClear }
```
- Document each enum value.

**`StorageOperation.cs`** — port from PricingEngine, namespace `Egil.Orleans.Testing`
- Document each property/field.

**`StorageOperationKind.cs`** — port from PricingEngine, namespace `Egil.Orleans.Testing`
- Document each enum value.

**`WaitForAssertionDefaults.cs`**:
- Public static class, `static readonly TimeSpan Timeout`
- Reads `WAIT_FOR_ASSERTION_TIMEOUT_SECONDS` env var, defaults to 5 seconds
- XML docs must name the env var, the default value, and the debugger bypass behavior.

**`WaitForAssertionTimeoutException.cs`**:
- Inherits `Exception`
- Constructor: message, inner exception (last assertion failure), context (grain ID, elapsed time)
- Mark throw sites with `[StackTraceHidden]`
- XML docs must explain what `InnerException` contains and how to read the message.

**`RequestContextScope.cs`** — port from PricingEngine, namespace `Egil.Orleans.Testing`
- XML docs explaining purpose: assertion-scope marker that prevents self-triggering wait loops.

### implement-collector — Implement GrainActivityCollector

The central type. Owns internal channels and exposes all 10 public `WaitFor*` methods.
All public methods must have thorough XML doc comments — see **XML documentation guidelines** section.

**Class-level docs**: `<summary>` explaining role as central hub, how to register via `AddGrainActivityCollector`, the two tiers (standard behavioral assertions vs advanced implementation-detail assertions).

**Internal state**:
- Lean `GrainActivity` channel (for standard WaitForAssertion)
- Typed `StorageOperation` channel (for advanced WaitForStorageOperation)
- Typed `IIncomingGrainCallContext`-captured-info channel (for advanced WaitForGrainCall)
- Thread-safe subscriber management (ConcurrentBag of channels or similar)

**Internal methods** (called by filter/observer):
- `internal void OnGrainCall(IIncomingGrainCallContext context)` — emits to both lean and typed channels
- `internal void OnStorageOperation(StorageOperation op)` — emits to both lean and typed channels

**Public methods** (10 total — see API section above):
- 2 non-grain-scoped `WaitForAssertionAsync` (Task/Task<T>)
  - XML docs: `<summary>` explaining retry-on-activity, `<param>` for each param, `<exception>`, `<example>`
- 4 grain-scoped `WaitForAssertionAsync` (2 without grain in lambda + 2 with grain in lambda)
  - XML docs: same as above, plus explain grain-scoping behavior
- 2 `GetStorageOperationsAsync` (global + grain-scoped) → `IAsyncEnumerable<StorageOperation>`
  - XML docs: `<summary>`, `<param>`, plus `<remarks>` with **coupling warning** (see guidelines)
- 2 `GetGrainCallsAsync` (global + grain-scoped) → `IAsyncEnumerable<IIncomingGrainCallContext>`
  - XML docs: `<summary>`, `<param>`, plus `<remarks>` with **coupling warning** (see guidelines)
- 1 `GetGrainActivityAsync` → `IAsyncEnumerable<GrainActivity>` (core stream)

Each method follows the same pattern:
1. Subscribe to the unified internal channel
2. Pre-flight check (run assertion/predicate immediately) for assertion methods
3. On each channel event, retry assertion/predicate
4. Wrap assertion in `RequestContextScope.ForAssertion()` (for assertion methods)
5. On timeout → throw `WaitForAssertionTimeoutException` with last failure
6. Unsubscribe on completion/timeout/cancellation

### implement-internal-sources — Implement internal event sources

**`GrainCallCollectionFilter.cs`** (internal):
- Implements `IIncomingGrainCallFilter`
- Holds reference to `GrainActivityCollector`
- In `Invoke`: check `RequestContext["test-assertion"]` → if set, skip emitting
- Otherwise: call `collector.OnGrainCall(context)` after `context.Invoke()`
- Then call next filter in pipeline

**`StorageObserver.cs`** (internal):
- Implements `IGrainStorage` (decorator pattern wrapping another `IGrainStorage`)
- Holds reference to `GrainActivityCollector`
- On `WriteStateAsync`/`ReadStateAsync`/`ClearStateAsync`: check `RequestContext["test-assertion"]` → if set, skip emitting
- Otherwise: delegate to inner storage, then call `collector.OnStorageOperation(...)` with appropriate `StorageOperation`

### implement-registration — Implement registration / builder

All public methods and types must have full XML doc comments.

**`GrainActivityCollectorSiloBuilderExtensions.cs`**:
```csharp
public static GrainActivityCollectorBuilder AddGrainActivityCollector(
    this ISiloBuilder builder, GrainActivityCollector collector)
```
- Registers the internal `GrainCallCollectionFilter` as `IIncomingGrainCallFilter` in DI
- Stores collector reference for the filter
- Returns `GrainActivityCollectorBuilder`

**`GrainActivityCollectorBuilder.cs`**:
```csharp
public class GrainActivityCollectorBuilder
{
    public GrainActivityCollectorBuilder CollectStorageActivityFromDefault();
    public GrainActivityCollectorBuilder CollectStorageActivityFrom(string providerName);
}
```
- `CollectStorageActivityFromDefault()` → wraps default `IGrainStorage` with `StorageObserver`
- `CollectStorageActivityFrom(name)` → wraps named `IGrainStorage` with `StorageObserver`
- Both use `IDecoratorServiceRegistration` or keyed service decoration to wrap storage providers

### implement-tests — Port and write tests

**Important**: All `implement-*` todos follow the TDD process from AGENTS.md. Production code and test code are never changed in the same step without running tests between them. The specific workflow is:
1. Write a failing test covering the behavior.
2. Run it — confirm it fails for the expected reason.
3. Implement the production code.
4. Run the test — confirm it passes.
5. Run all tests — confirm nothing else broke.

**Unit tests (from skill sample):**
- `GrainCallCollectionFilterTests.cs` — adapt from skill sample (uses `FakeIncomingGrainCallContext`)
- `RequestContextScopeTests.cs` — adapt from skill sample

**Integration tests:**
- `SiloFixture.cs` — reference `InProcessTestCluster` fixture, creates `GrainActivityCollector` and registers via builder
- `IntegrationTests.cs`:
  - Standard `WaitForAssertionAsync` triggered by grain call
  - Standard `WaitForAssertionAsync` triggered by storage write
  - Grain-scoped `WaitForAssertionAsync` with grain passed to lambda
  - Advanced `GetStorageOperationsAsync` feed with LINQ composition
  - Advanced `GetGrainCallsAsync` feed with LINQ composition
  - Timeout behavior (expect `WaitForAssertionTimeoutException`)
  - Value-returning assertions
  - Mixed signals (both call + storage active)

### create-ci — Create CI workflow

Create `.github/workflows/egil-orleans-testing-ci.yml` following `egil-systemtextjson-migration-ci.yml` pattern:

- Trigger on push/PR changes to `Egil.Orleans.Testing/**`
- Jobs: `create-nuget` → `validate-nuget` → `run-test` → `release`
- .NET 10.x SDK (preview)
- NuGet validation with `meziantou.validate-nuget-package`
- Release job with trusted publishing to NuGet
- Path filter triggers for the library directory

## NuGet package dependencies (src)

- `Microsoft.Orleans.Runtime` — `IIncomingGrainCallFilter`, `RequestContext`, `GrainId`, `ISiloBuilder`, `IGrainStorage`
- `Microsoft.Orleans.Core.Abstractions` — `IGrain`, grain key interfaces
- `DotNet.ReproducibleBuilds` — reproducible builds
- `Nerdbank.GitVersioning` — versioning (via Directory.Build.props)

## NuGet package dependencies (test)

- `xunit.v3.mtp-v2`
- `Microsoft.Orleans.TestingHost` — `InProcessTestCluster` for integration tests
- `Microsoft.Orleans.Server` — silo setup in fixture
- `Microsoft.Testing.Extensions.CodeCoverage` — MTP-native code coverage
