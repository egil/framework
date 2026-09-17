# Repository Guidelines

## Audience & Scope
This guide is primarily for AI agents working in `Egil.Orleans.Messaging`. Human
contributors should follow all rules here that reasonably apply to manual workflows.

Read the root [AGENTS.md](../AGENTS.md) first — it remains authoritative for commit
and scope conventions, release-notes rules, development process, and build/test
tooling. This file only records where this package deliberately differs.

## API Stability Before 1.0
This package prioritises getting the API shape right over source and binary
compatibility until it reaches 1.0. Design each change on its merits. A narrow
source break — for example an overload addition that turns a previously valid call
into an ambiguity error — is not a reason to redesign a change, add a compatibility
shim, or offer the maintainer a breaking and non-breaking variant to choose between.

Because of that, breaking changes here do **not** need the Conventional Commit
breaking-change ceremony the root guide describes: skip the `!` suffix on the subject
and the `BREAKING CHANGE:` footer unless the maintainer asks for them.

Skipping the markers does not mean skipping the explanation. Breaking changes are
still documented in two places:

- The commit body, in the same user-facing prose the root guide requires, so the
  history states what changed and why.
- The **Beta API changes** section of [README.md](README.md), which is the migration
  guide consumers follow when moving to a newer version. Add an entry describing what
  to change, not just what broke.

Revisit this section at 1.0. From that point the root guide's breaking-change rules
apply here without exception.
