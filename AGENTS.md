# Repository Guidelines

## Audience & Scope
This guide is for AI agents and human contributors working anywhere in this mono-repo. Per-project `AGENTS.md` files (for example, `Egil.SystemTextJson.Migration/AGENTS.md`, `Egil.Orleans.Testing/AGENTS.md`) extend these rules with project-specific build, test, and style conventions. When a per-project file conflicts with this one, the per-project file wins for changes in that project.

## Projects in this Mono-Repo
Each top-level project ships as its own NuGet package and has its own CI workflow under `.github/workflows/`. Use the project's short scope code in every Conventional Commit subject:

| Project directory                | Conventional Commit scope |
|----------------------------------|---------------------------|
| `Egil.SystemTextJson.Migration/` | `stjm`                    |
| `Egil.Orleans.Testing/`          | `ot`                      |
| `Egil.StronglyTypedPrimitives/`  | `stp`                     |

Cross-cutting changes that don't belong to a single package may use:
- `ci` for CI/workflow changes under `.github/`
- `build` for repo-wide build/tooling changes (for example, root `Directory.Build.props`)
- `docs` for repo-level documentation outside any one project (root `README.md`, etc.)

Do not invent new scopes. Do not omit the scope. Every commit that touches files inside one of the project directories above must use that project's scope.

## Commit & Pull Request Guidelines
Commits and PR titles must follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <short summary>

<body explaining what and why>

<footers>
```

- **Type**: `feat`, `fix`, `chore`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `style`.
- **Scope**: required, from the table above.
- **Body**: required for `feat`/`fix` and any change a package consumer should know about. Write user-facing prose explaining *what* changed and *why*. Avoid internal-only context (chat logs, agent session IDs, implementation minutiae).
- **Breaking changes**: add a `BREAKING CHANGE: <description>` footer and/or use `<type>(<scope>)!:` in the subject.

Do not mix unrelated commit types in a single commit. Tests that validate a `feat` or `fix` belong in the same commit as that `feat`/`fix`. Use `test(<scope>)` only for test-only additions to existing behavior. Refactors, docs, and fixes each go in their own commits.

PR titles must use the same Conventional Commit format as the primary commit they ship.

### Release Notes and `[skip notes]`
Each project generates its release notes from its own commit history (see `<project>/scripts/generate-release-notes.ps1`). The commit subject becomes the changelog entry heading and the commit body becomes the entry detail.

To exclude a commit from release notes — for example, build/CI/tooling/refactor work that uses `feat` or `fix` types but isn't relevant to package consumers — add `[skip notes]` anywhere in the subject or body.

Examples of when to use `[skip notes]`:
- Internal refactors that don't change observable behavior.
- CI workflow tweaks committed with `fix(ci): ...`.
- Dependency bumps that don't affect the public API.
- Docs updates already covered by another commit in the same release.

Examples of when **not** to use `[skip notes]`:
- Any user-visible feature, fix, or behavior change.
- Performance improvements consumers can observe.
- Breaking changes (these must always appear in notes).

Trailers (such as `Co-authored-by:`) are stripped from the body before inclusion in release notes, so they are safe to add freely.

## Development Process
- Work one project at a time. Don't bundle unrelated changes across projects in one commit.
- Commit logical units of work; keep commits small and focused.
- `Release` builds fail on warnings. Make sure each commit is warning-free.
- Follow the repo test skill at `.agents/skills/test/SKILL.md` for TDD, test-after, refactoring, fake/builder usage, and production/test change discipline.
- Coding agents must always follow the code comment guidelines in `.agents/skills/code-comments/SKILL.md` when adding, editing, or reviewing code comments.

## Build, Test, and Tooling
Each project has its own solution file (`<project>/<project>.slnx`). Run `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet pack` against the relevant project's solution. Per-project `AGENTS.md` files document the exact commands and any project-specific tooling (BenchmarkDotNet, mdsnippets, etc.).
