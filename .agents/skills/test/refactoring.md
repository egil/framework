# Refactor candidates

Run this checklist after each testing cycle and whenever intent degrades.

## General triggers

- Duplication → extract.
- Long methods → split helpers.
- Feature envy → move toward owning module.
- Primitive obsession → add value object/typed IDs.

## Repo-agnostic triggers

- Arrange complexity mismatched with act/assert level → introduce builders or local factories.
- Inline fake grows beyond one test → move to `test/<Project>.Tests/Fakes/`.
- Repeated setup across classes → lift into shared helper.
- Test depends on raw ids where typed ids exist → migrate to typed ids.
- Time-advance coupling via shared fixture → isolate with local clock/provider.
- Snapshot check obscures intent → replace with explicit assertions when possible.

## Rules

- Do not refactor on a red test; get to green first.
- Keep refactors focused and small.
- Stop when intent is clear without cross-file context.
