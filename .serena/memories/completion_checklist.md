# Task completion checklist
- Run `dotnet outdated -u` checks for library + tests before each implementation phase.
- Ensure `dotnet test ... -c Release` passes (warnings treated as errors).
- Keep edits scoped to the target project unless explicitly requested.
- Ensure public APIs have XML docs where required.
- Ensure non-obvious logic has short why-comments.
- Commit phase changes with Conventional Commit message.
- Verify clean working tree after each commit (`git status --short`).