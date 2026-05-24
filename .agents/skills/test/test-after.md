# Test-after approach

Use this workflow when production code already exists.

## Workflow

1. Add a single test with intentionally incorrect assertion.
2. Run and confirm expected RED reason (wrong assert).
3. Fix assertion.
4. Re-run and pass.
5. If still red, inspect production code.
6. Repeat one test at a time.

## Why

- Confirms test can still fail for real reasons.
- Reduces false-positive tests.

## Don’t

- Batch many reversed assertions first.
- Use this workflow for greenfield code.
