# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Source of truth: AGENTS.md

**Read [AGENTS.md](AGENTS.md) first.** It is the authoritative guide for this repository — commit/scope conventions, release-notes rules (`[skip notes]`), development process, and build/test tooling. This file adds nothing that contradicts it; if the two ever disagree, AGENTS.md wins.

Then read the per-project `AGENTS.md` for whichever project you are touching. A per-project file overrides the root one for changes in that project:

| Project directory                | Solution                                    | Project AGENTS.md |
|----------------------------------|---------------------------------------------|-------------------|
| `Egil.SystemTextJson.Migration/` | `Egil.SystemTextJson.Migration.slnx`        | yes               |
| `Egil.Orleans.Messaging/`        | `Egil.Orleans.Messaging.slnx`               | no — use root     |
| `Egil.Orleans.Testing/`          | `Egil.Orleans.Testing.slnx`                 | yes               |
| `Egil.StronglyTypedPrimitives/`  | `Egil.StronglyTypedPrimitives.sln`          | no — use root     |

## Required skills

AGENTS.md mandates these repo skills; load the relevant `SKILL.md` before doing the work:

- `.agents/skills/test/SKILL.md` — TDD, test-after, refactoring, fakes/builders, and production-vs-test change discipline. Applies to any test work.
- `.agents/skills/code-comments/SKILL.md` — applies whenever you add, edit, or review a code comment.

Other repo skills live under `.agents/skills/` (e.g. `dotnet-inspect`, `thermo-nuclear-code-quality-review`).

## Working shape

This is a mono-repo of independent NuGet packages. Each project has its own solution, `global.json`, `Directory.Packages.props`, `version.json`, and CI workflow under `.github/workflows/`. Work one project at a time and run `dotnet` commands against that project's solution, not the repo root:

```bash
dotnet build  <project>/<project>.slnx
dotnet test   <project>/<project>.slnx
dotnet test   <project>/<project>.slnx --filter "FullyQualifiedName~SomeTestClass"
```

`Release` builds fail on warnings — keep every commit warning-free.
