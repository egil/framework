# Commit discipline

**Core tenet:** between any two test-suite runs, change *either* production code *or* test code, never both. Otherwise a regression has no attributable cause — you can't tell whether the prod change broke the test or the test change broke the assertion.

This applies in **both** TDD and test-after workflows.

## The rule

1. Run the full test suite. It must be green.
2. Make changes to *one side only* — `src/**` or `test/**`, not both.
3. Run the full suite again. Must stay green.
4. Now you may change the other side. Run again.

Commit boundaries don't have to align with this — you can group multiple alternations into one commit. The constraint is on **runs**, not commits.

## Every commit checks out green

The strongest invariant: any commit, checked out in isolation, must build and pass the full test suite. This makes:

- `git bisect` precise — every commit is a valid waypoint.
- Reverts surgical — drop one commit, suite stays green.
- Code review trustworthy — reviewer sees a consistent state at every step.

If you need to break this temporarily (e.g., a multi-step migration), make it explicit in the commit message and squash before merge.

## Compile-required test stub exception

If a prod change adds members to a public interface, any test fake that *directly* implements that interface (rather than deriving from a production base class that already supplies the members) needs stub implementations to keep the suite compiling. Those stubs are mechanical — no test behavior changes — and including them with the prod change is the only way to keep the suite green at the boundary. Note this in the commit body.

Pre-existing precedent: `FakeLocationGrain` got `OnNextAsync` / `OnErrorAsync` stubs when `ILocationGrain` first inherited `IAsyncObserver<ILocationInputEvent>`.

## Commit message prefixes

Use the prefixes from [commit-and-pr-guidelines](../commit-and-pr-guidelines/SKILL.md):

- `feat:` / `fix:` / `refactor:` for prod-side changes.
- `test:` / `refactor(test):` for test-side changes.

A single commit may be either, or both when the change is one logical unit (a TDD slice, a test-fake stub for a new interface member). The discipline is about **runs**, not commit message granularity.
