# Repository Guidelines

## Audience & Scope
This guide is primarily for AI agents. Human contributors should follow all rules here that reasonably apply to manual workflows.

## Project Structure & Module Organization
This project is a focused .NET library with tests:
- `src/Egil.Orleans.Testing/`: main package code.
- `test/Egil.Orleans.Testing.Tests/`: xUnit v3 behavior and integration tests.

Treat `bin/`, `obj/`, and `StrykerOutput/` as generated output. Dependency versions are centrally managed in `Directory.Packages.props`.
This project is part of a mono-repo with shared inherited artifacts (for example, `.editorconfig` and `../Directory.Build.props`).

## SDK Selection (`global.json`)
This project intentionally keeps its own `global.json`; the SDK policy it declares is **package-specific, not repo-wide**. The mono-repo has no root `global.json`, and the other packages do not need one. Two settings make it specific to this package:

- `sdk.rollForward: latestMajor` (with `allowPrerelease: false`): the package and its tests target `net10.0`, so builds and tooling roll forward to the newest installed major .NET SDK.
- `test.runner: Microsoft.Testing.Platform`: required because the test project (`test/Egil.Orleans.Testing.Tests`) is Microsoft Testing Platform-only — it references `xunit.v3.mtp-v2` and intentionally omits `Microsoft.NET.Test.Sdk`/`xunit.runner.visualstudio`. This setting tells `dotnet test` to use the Microsoft Testing Platform runner. The sibling packages keep the legacy VSTest adapters as a fallback, so they do not require this `global.json` entry.

Do not promote this file to the repository root unless every package adopts the same MTP-only test setup.

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
- Advanced methods that expose implementation details (e.g., `GetStorageOperationsAsync`, `GetGrainCallsAsync`) must include a `<remarks>` section warning about tight coupling to production code internals.

## Testing Guidelines
- Follow the repo test skill at `../.agents/skills/test/SKILL.md` for TDD, test-after, refactoring, fake/builder usage, and production/test change discipline.
- Test framework: `xunit.v3.mtp-v2` on Microsoft Testing Platform, with `Microsoft.Testing.Extensions.CodeCoverage` for coverage collection.
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
- Use `../.agents/skills/test/SKILL.md` for the detailed testing workflow instead of duplicating it here.

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
