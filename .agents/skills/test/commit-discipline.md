# Commit discipline

Between any two full suite runs, change either production code or test code, never both in the same run. This keeps regressions attributable.

## The rule

1. Run test suite and record green state.
2. Change one side only: `src/**` or `test/**`.
3. Run the suite again.
4. Change the other side.
5. Run again.

## Every commit should remain green

Rebases and `git bisect` are only meaningful when each commit can pass tests alone.

If an interface change requires stub implementation on a fake, keep that mechanical change in the same change window.

## Commit message intent

Use `feat:` / `fix:` / `refactor:` for production changes, `test:` for test changes, `refactor(test):` for test-only refactors. Keep scope from this repository rules.
