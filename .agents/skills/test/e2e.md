# End-to-end and host-level tests

Use this doc when your project has an executable host (Aspire/AppHost, ASP.NET, worker service, etc.) and you need to validate cluster- or process-wide behavior.

## Host fixture pattern

A class fixture that boots the host once per class and exposes:

- Authenticated `HttpClient`
- Optional typed API clients
- Utilities for starting optional secondary nodes/instances

Keep it narrow and explicit. There is no universal pattern, so adapt names to your app.

## Typical knobs

Pass host-specific args/flags through your test host factory to reduce setup ambiguity and keep tests stable:

- feature flags for test-only code paths
- deterministic ports/resources for local-only integration tests
- toggles to disable UI/frontends when not under test
- knobs to disable heavy external dependencies

Record each accepted value in one location, and prefer constants over inline strings.

## Authentication in e2e tests

Prefer deterministic test auth mode (for example, fake header-based auth) so scenarios stay deterministic and avoid signing infrastructure.

## Practical guidance

- Avoid mixing host-level integration and grain-only/in-memory tests in the same class.
- Keep host-level fixtures isolated by purpose.
- Mark long-running/fragile scenarios explicitly when needed (for example, `[Fact(Skip = "...")]` for replay-like investigations).
- Prefer fast deterministic assertions; avoid blanket sleeps.

If this repository does not currently use host-level tests, keep this doc as a template and prune it when integrating a concrete host fixture.




