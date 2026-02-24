# Egil.Orleans.StateMigration Implementation Plan

## Scope and constraints

- Implement only within `Egil.Orleans.StateMigration`.
- Target the latest .NET 10 SDK for this library/project.
- Use latest stable NuGet package versions required by this library.
- Do not upgrade or modify unrelated libraries/projects in this repository.
- Follow existing repository structure, naming, and coding style conventions.
- Apply TDD: write failing tests first, then implement, then refactor.

## Project scaffold

1. Create `Egil.Orleans.StateMigration.sln`.
2. Create library project:
   - `src/Egil.Orleans.StateMigration/Egil.Orleans.StateMigration.csproj`
3. Create test project:
   - `test/Egil.Orleans.StateMigration.Tests/Egil.Orleans.StateMigration.Tests.csproj`
4. Add solution items consistent with other solutions:
   - `.editorconfig`, `.gitattributes`, `.gitignore`
   - `Directory.Build.props`, `Directory.Packages.props`
   - `xunit.runner.json`
5. Configure packaging metadata (`PackageId`, README, LICENSE, repository links).
6. Add `version.json` for this library with appropriate path filters and release naming.
7. Add a dedicated CI workflow for this library (build, test, pack, validate package, release).

## TDD execution plan

### Phase 1: Contracts and migration resolution

1. Add tests for migration contracts:
   - `IMigrateFrom<TSource, TTarget>` (static type-owned migration)
   - `IMigrate<TSource, TTarget>` (external migrator instance)
2. Add tests for resolution order:
   - Prefer `IMigrateFrom<,>` on target type.
   - Fallback to external `IMigrate<,>`.
3. Add tests for failure/validation:
   - No migration path exists.
   - Duplicate external migrators for same `(TSource, TTarget)` pair.

### Phase 2: JSON format and writer behavior

1. Add tests that serialization writes `$type` first.
2. Add tests that identity uses Orleans `[Alias]` when present.
3. Add tests that identity falls back to full CLR type name when alias is absent.
4. Add round-trip tests for current type without migration (`MigratedDuringDeserialization == false`).

### Phase 3: Deserializer flow and migration behavior

1. Add tests for first-property `$type` contract:
   - matching target type fast-path
   - known older type triggers migration
2. Add tests for malformed `$type`:
   - null/empty/unknown `$type` fails fast with clear exception
3. Add tests for legacy payload (first property not `$type`):
   - deserialize as current `T`
   - `MigratedDuringDeserialization == true`
4. Add tests that any successful migration path sets `MigratedDuringDeserialization == true`.

### Phase 4: Orleans deserialization callback integration

1. Add tests for `IOnDeserialized` invocation after deserialization.
2. Add tests for callback invocation after migration path as well.
3. Add tests ensuring callback is not invoked multiple times per read.

### Phase 5: DI and startup wiring

1. Add registration API for external migrators (DI extensions).
2. Add startup scan/cache of migration mappings.
3. Add startup validation tests:
   - duplicate alias collisions
   - unresolved `$type` mappings
   - ambiguous registration scenarios
4. Ensure migration lookup is thread-safe and allocation-aware in read hot path.

## Implementation details and quality gates

1. Converter implementation:
   - `StorageJsonConverterFactory`
   - `StorageJsonConverter<TStateType>`
2. Low-allocation header probing:
   - use copied `Utf8JsonReader` to inspect first property only
3. Keep multi-hop migration unsupported by design:
   - require direct migration to latest target type
4. Add compatibility fixture tests with stored JSON samples from prior formats.
5. Add targeted performance guard test/benchmark for deserialization fast-path.
6. Ensure:
   - `dotnet build` passes
   - `dotnet test` passes
   - package can be packed and validated in CI

## Suggested work order

1. Scaffold solution/projects and baseline CI/versioning files.
2. Implement Phase 1 tests and minimum code to pass.
3. Implement Phases 2-3 tests and converter behavior.
4. Implement Phase 4 Orleans callback integration.
5. Implement Phase 5 DI/startup validation.
6. Add compatibility/perf guards and finalize docs.
