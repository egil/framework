# test Skill Index

This directory contains repository instructions for test workflows under `.agents/skills/test`.

## Purpose

Use these documents when writing, maintaining, or reviewing tests in this repository.

## Files

- [`SKILL.md`](SKILL.md): Entry point that gives the decision flow and test priorities.
- [`tdd-workflow.md`](tdd-workflow.md): Process for new code.
- [`test-after.md`](test-after.md): Process for tests added to existing code.
- [`tests.md`](tests.md): What good tests look like and anti-patterns.
- [`fakes.md`](fakes.md): Guidelines for choosing fakes and avoiding mocks.
- [`builders.md`](builders.md): Patterns for fluent test data builders.
- [`orleans.md`](orleans.md): Orleans-specific grain test and `WaitForAssertionAsync`.
- [`parallelization.md`](parallelization.md): Fixture and timing isolation guidance.
- [`e2e.md`](e2e.md): Host-level/end-to-end test guidance template.
- [`commit-discipline.md`](commit-discipline.md): Keeping suite health between test/prod edits.
- [`refactoring.md`](refactoring.md): Post-cycle refactor triggers.
- [`interface-design.md`](interface-design.md): Designing testable interfaces.
- [`manifest.json`](manifest.json): Skill metadata used by the agent runtime.

## Repository alignment

This is intentionally generic to this repo and avoids Clever.PricingEngine-specific assumptions.

