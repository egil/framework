# Style and conventions
- C# with `Nullable` enabled and `ImplicitUsings` enabled.
- `LangVersion` is `latest` from root `Directory.Build.props`.
- Analyzer settings enabled in build (`AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`).
- In `Release` configuration, warnings are treated as errors (`TreatWarningsAsErrors=true`).
- XML documentation is expected for public APIs in `Egil.Orleans.StateMigration`.
- Add comments for non-obvious logic focusing on *why*.
- Commit messages should follow Conventional Commits (for this workstream).
- Prefer TDD flow: add tests, implement minimal code, keep tests green.