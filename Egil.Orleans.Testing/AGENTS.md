# Repository Guidelines

## Audience & Scope
This guide is primarily for AI agents. Human contributors should follow all rules here that reasonably apply to manual workflows.

## Project Structure & Module Organization
This project is a focused .NET library with tests:
- `src/Egil.Orleans.Testing/`: main package code.
- `test/Egil.Orleans.Testing.Tests/`: xUnit v3 behavior and integration tests.

Treat `bin/`, `obj/`, and `StrykerOutput/` as generated output. Dependency versions are centrally managed in `Directory.Packages.props`.
This project is part of a mono-repo with shared inherited artifacts (for example, `.editorconfig` and `../Directory.Build.props`).

## Build, Test, and Development Commands
Use the solution file from repository root:
- `dotnet restore Egil.Orleans.Testing.slnx`: restore all projects.
- `dotnet build Egil.Orleans.Testing.slnx -c Release`: compile with analyzer checks.
- `dotnet test Egil.Orleans.Testing.slnx -c Release`: run test suite (xUnit + Microsoft Testing Platform).
- `dotnet pack src/Egil.Orleans.Testing/Egil.Orleans.Testing.csproj -c Release`: produce NuGet package.
- `dotnet outdated`: check for dependency updates.

## Coding Style & Naming Conventions
- Language/runtime: C# on `net10.0`, nullable enabled.
- Indentation: 4 spaces for C#; follow `.editorconfig` for other file types.
- Prefer file-scoped namespaces and explicit braces.
- Use `var` when the type is obvious; keep naming in PascalCase for types/methods.
- Interfaces use `I*` naming.
- All public types and methods must have full XML documentation comments (`<summary>`, `<param>`, `<returns>`, `<exception>` as applicable).
- Advanced methods that expose implementation details (e.g., `WaitForStorageOperationAsync`, `WaitForGrainCallAsync`) must include a `<remarks>` section warning about tight coupling to production code internals.

## Testing Guidelines
- Test framework: `xunit.v3` with `Microsoft.NET.Test.Sdk`.
- Put tests in `test/Egil.Orleans.Testing.Tests` and name files/classes with `*Tests`.
- Test method names should describe behavior clearly.
- Add/adjust tests for every behavior change, including edge cases.
- Avoid implementation details; tests should depend only on externally observable behavior.
- Coverage policy applies to production code under `/src` only.
- Core components must maintain `100%` branch coverage.
- All remaining production code under `/src` must maintain at least `95%` branch coverage.
- Test projects/files are excluded from coverage metrics.

## Development Process
- Use Context7 MCP to verify up-to-date documentation for libraries and APIs.
- Commit logical units of work that follow Conventional Commit principles.
- Commit code should be warning-free (`Release` builds fail on warnings).
- Break development tasks into individual steps; do not mix refactoring and new features in one commit.
- Never change both production code (under /src) and test code (under /test) at the same time without running tests between the two. Change one or the other, then run tests.

- When adding new features or fixing bugs in production code (under /src), follow this process:
  1. Write a failing test covering the change.
  2. Run the test, ensure it fails and fails for the expected reason.
  3. Implement the feature and/or fix in production code.
  4. Run the test again, confirm it now passes.
  5. When the new test passes, run all tests to ensure no other tests have broken.

- When adding new tests to existing features, follow this process:
  1. Write the test, but invert the assertion.
  2. Run the test, ensure it fails and fails for the expected reason.
  3. Correct the assertion in the test.
  4. Run the test again, confirm it now passes.

- When refactoring production code (under /src), follow this process:
  1. Refactor production code.
  2. Run all tests, confirm all still pass.

## Commit & Pull Request Guidelines
Git history favors Conventional Commit-style messages, often with scope `ot` for this project:
- `feat(ot): ...`
- `test(ot): ...`
- `chore(ot): ...`
- `fix(ot): ...`

Do not mix unrelated commit types in a single commit. Ensure Conventional Commit principles are upheld.
Tests that validate a `feat` or `fix` must be included in the same commit as that `feat`/`fix` (use the `feat` or `fix` type).
Use `test(...)` commits only for test-only changes, such as adding coverage for existing behavior without production-code changes.
Refactoring changes go into one commit, fixes into another commit, etc.

**Commit body text is included in package release notes and changelogs.** The subject line is the terse entry heading; the body provides the detail that package consumers read. Write the body as clear, user-facing prose that explains *what* changed and *why* it matters. Avoid internal-only context (chat logs, agent session IDs, implementation minutiae) — focus on information that is valuable in a changelog.

For breaking changes, add a `BREAKING CHANGE: <description>` footer in the body (per the Conventional Commits spec) in addition to — or instead of — the `!` suffix on the subject.

To exclude a commit from release notes (for example, build tooling or infrastructure changes that use `feat`/`fix` types but are not relevant to package consumers), add `[skip notes]` anywhere in the subject or body.

Example of a well-structured commit message:

```
feat(ot): add grain-scoped WaitForAssertionAsync overloads

WaitForAssertionAsync now accepts an IGrain parameter to scope retry
triggers to activity from a single grain. This prevents unrelated
grain activity from causing spurious assertion retries, improving
test reliability in multi-grain scenarios.
```

Keep commits small and focused. For PRs, include:
- What changed and why.
- Evidence from `dotnet test` and benchmark notes when performance is affected.
- Confirmation that CI build/test/pack validation passes.
