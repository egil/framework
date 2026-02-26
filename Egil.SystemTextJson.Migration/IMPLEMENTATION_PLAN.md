# Egil.SystemTextJson.Migration Implementation Plan

## Goals

- Ship `Egil.SystemTextJson.Migration` as a production-ready `v0.x` package.
- Keep non-migration runtime overhead as close to plain `System.Text.Json` as possible.
- Be AOT-friendly by default with explicit migrator registration.
- Support optional, explicitly-scoped assembly scanning for migrators.
- Support STJ source-generated `JsonSerializerContext` usage.
- Add migration-state tracking via a target interface property set during deserialization.

## TDD Protocol (Locked)

- Always add/adjust tests first, then verify failure for the expected reason.
- Between test runs, change only test code or production code, not both.
- Use small red/green/refactor steps.
- After each completed step with green tests, create a Conventional Commit with a short step chat summary in commit body.
- Run performance tests when code changes can affect performance.
- Add comments that explain why non-obvious implementation choices exist.

## Phase Checklist

### Phase 0: Baseline and infrastructure

- [x] Fix `.NET 10` test runner configuration for MTP and get `dotnet test` green.
- [ ] Add package metadata, library README, `version.json`, and CI workflow.

### Phase 1: Core library extraction

- [x] Move sample migration implementation from tests into `src`.
- [x] Keep tests using only public APIs from `Egil.SystemTextJson.Migration`.

### Phase 2: AOT-safe registration and scanning

- [x] Add explicit registration API (`RegisterMigrator<T...>` variants).
- [x] Add optional assembly-scoped scanning API.
- [x] Validate duplicate registration and unsupported signatures at setup.

### Phase 3: Tracking contract

- [x] Add `IJsonMigrationTracked` and set `MigratedDuringDeserialization` for migrated and legacy payload paths.

### Phase 4: Source generation support

- [x] Ensure compatibility with user-provided STJ `JsonSerializerContext`.
- [x] Add failure diagnostics for missing metadata.

### Phase 5: Performance testing

- [ ] Add BenchmarkDotNet perf project inspired by Orleans migration perf tests.
- [ ] Benchmark all scenarios, including non-JsonMigratable manual-migration counterparts.

## Perf Gates

- No assembly scanning or reflection-based discovery on read/write hot path.
- Cached metadata/delegates used at runtime.
- Compare fast-path costs against plain STJ baseline.

## Risks

- Source-generation metadata availability can fail at runtime if required types are missing.
- Reflection-based migration invocation can regress perf if not replaced with cached delegates.
- Optional scanning must not become implicit default behavior.

## Progress Notes

- 2026-02-26: Added initial production API and converter implementation in `src`, and removed embedded sample implementation from tests so tests now exercise the library project.
- 2026-02-26: Added registration, tracking, precedence, scoped scanning, and source-generation test coverage (`RegistrationAndTrackingTests`) and verified behavior with all tests green.
