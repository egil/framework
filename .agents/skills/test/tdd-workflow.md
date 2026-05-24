# TDD workflow

Prefer red → green → refactor for new behavior.

## Anti-pattern: horizontal slices

Write one behavior end-to-end, implement, green, refactor, then repeat.

## Workflow

### 1. Plan

- Confirm interface/behavior with stakeholders.
- Prioritize behaviors.
- Keep setup/builders/fakes ready from the start.

### 2. Tracer bullet

Write one test for one behavior, make it fail, implement minimal code, pass.

### 3. Incremental loop

Repeat test-by-test until covered behaviors pass.

### 4. Refactor

Refactor for readability and intent after green.

### 5. Checklist

- Scenario-style names
- Behavior-level assertions
- Public API only
- Minimal code for current test
- Avoid mocks in new code
- Builder/fake usage when appropriate
