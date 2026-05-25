# Maintenance Baseline

This repo contains several independently released libraries. Maintenance automation should keep shared build, test, analyzer, SDK, and CI concerns aligned without forcing every package to use the same runtime dependencies.

## Dependency Policy

- Shared baseline dependencies are repo-level concerns. Drift in these packages should produce a cross-cutting issue or a grouped update PR.
- Runtime dependencies remain package-specific unless the packages intentionally share a product family, such as Orleans, Azure SDKs, Aspire, or Roslyn.
- Floating package versions are not used for library builds. Every dependency update should be represented by a committed version change.
- Transitive pinning remains disabled by default for package-authoring projects unless a specific package issue justifies it.

## SDK Policy

- `net10.0` is the preferred active target for packages that do not need older consumer compatibility.
- `net9.0` and `net8.0` remain support-window targets until November 2026, but they should be tracked by maintenance issues so migration work is planned.
- `netstandard2.0` is allowed only where consumer compatibility requires it.
- CI should avoid .NET preview SDKs unless a preview-readiness issue explicitly tracks that work.

## Test Harness Policy

- New or actively modernized packages should converge on xUnit v3 plus Microsoft Testing Platform when practical.
- Existing `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` based projects are allowed short-term, but the inventory workflow tracks them as test-harness drift.
- Each package CI should restore, build, test, pack, and validate unless an open maintenance issue explains the exception.

## Automation

- Renovate opens low-noise grouped PRs for NuGet packages, GitHub Actions, and dotnet local tools.
- The maintenance inventory workflow creates or updates deterministic issues for cross-cutting drift that needs judgment or coding-agent work.
- Generated reports are written under `artifacts/maintenance/` and uploaded from CI.
