# Project overview
- Repository: `framework`
- Purpose: collection of C#/.NET libraries by Egil Hansen.
- Main libraries in repo:
  - `Egil.StronglyTypedPrimitives` (source generator for strongly-typed primitive wrappers)
  - `Egil.Orleans.Storage` (OpenTelemetry integrations for Orleans storage)
  - `Egil.Orleans.EventSourcing` (event sourcing helpers for Orleans)
  - `Egil.Orleans.StateMigration` (new library under development for migration-aware Orleans state serialization)
- Tech stack: .NET SDK projects (currently net9/net10 depending on subproject), C#, xUnit v3 tests, GitHub Actions CI, Nerdbank.GitVersioning.
- Repository is primarily class libraries and tests; no single runtime app entrypoint at root.