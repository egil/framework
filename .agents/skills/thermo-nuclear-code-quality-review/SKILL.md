---
name: thermo-nuclear-code-quality-review
description: Run an extremely strict maintainability review for abstraction quality, giant files, and spaghetti-condition growth. Use for a thermo-nuclear code quality review, thermonuclear review, deep code quality audit, or especially harsh maintainability review.
disable-model-invocation: true
---

# Thermo-Nuclear Code Quality Review

Use this skill for an unusually strict review focused on implementation quality, maintainability, abstraction quality, and codebase health.

Above all, this skill should push the reviewer to be **ambitious** about code structure. Do not merely identify local cleanup opportunities. Actively search for "code judo" moves: restructurings that preserve behavior while making the implementation dramatically simpler, smaller, more direct, and more elegant.

## Core Prompt

Start from this baseline:

> Perform a deep code quality audit of the current branch's changes.
> Rethink how to structure / implement the changes to meaningfully improve code quality without impacting behavior.
> Work to improve abstractions, modularity, reduce Spaghetti code, improve succinctness and legibility.
> Be ambitious, if there is a clear path to improving the implementation that involves restructuring some of the codebase, go for it.
> Be extremely thorough and rigorous. Measure twice, cut once.

If no branch diff is available, ask the user to specify the scope of changes to review, or review the provided files as a whole against these standards.

## Non-Negotiable Additional Standards

Apply the baseline prompt above, plus these explicit review rules. Treat these as the single authoritative checklist for findings, approval decisions, and suggested remedies.

0. **Be ambitious about structural simplification.**
   - Flag complicated implementations, refactors that merely move complexity around, and missed "code judo" moves that could delete whole helpers, branches, modes, or layers.
   - Prefer reframing the state model, ownership boundary, or default flow so the implementation becomes smaller and feels inevitable in hindsight.

1. **Do not let a PR push a file from under 1k lines to over 1k lines without a very strong reason.**
   - Flag the threshold crossing as a strong code-quality smell by default, especially when the new code can be split out.
   - Prefer focused modules, extracted helpers, subcomponents, or local abstractions unless there is a compelling structural reason to keep the file whole.

2. **Do not allow random spaghetti growth in existing code.**
   - Flag ad-hoc conditionals, scattered special cases, one-off booleans, nullable modes, temporary branches, and edge-case handling inside already busy functions.
   - Prefer a dedicated abstraction, helper, state machine, policy object, explicit dispatcher, typed model, or single clearer flow.

3. **Bias toward cleaning the design, not just accepting working code.**
   - Flag working implementations that make the codebase messier, more coupled, less modular, or harder to scan.
   - Prefer behavior-preserving restructures that remove moving pieces; do not settle for rename-only feedback when the real issue is structural.

4. **Prefer direct, boring, maintainable code over hacky or magical code.**
   - Flag brittle behavior, generic magic hiding simple assumptions, thin abstractions, identity wrappers, and pass-through helpers that do not buy clarity.
   - Prefer deleting unearned indirection, using a direct flow, or making assumptions explicit.

5. **Push hard on type and boundary cleanliness when they affect maintainability.**
   - Flag unnecessary nullability, `object`/`dynamic`, unchecked or pattern casts, over-broad generics, ad-hoc anonymous/tuple shapes, optional parameters, and silent fallbacks that obscure the real invariant.
   - Treat the null-forgiving `!` operator and broad `#pragma warning disable` / `SuppressMessage` on nullability or analyzer rules as red flags: they paper over an unclear invariant instead of making the boundary explicit.
   - Prefer explicit typed models, nullable reference annotations that reflect reality, shared contracts, and clearer boundaries.

6. **Keep logic in the canonical layer and reuse existing helpers.**
   - Flag feature logic leaking into shared paths, implementation details leaking through APIs, copy-pasted logic, duplicated helpers, and feature checks scattered through shared code.
   - Prefer the canonical utility and the package, service, abstraction, or module that already owns the concept.

7. **Treat unnecessary sequential orchestration and non-atomic updates as design smells when the cleaner structure is obvious.**
   - Flag serialized independent work, orchestration tangled with business logic, and related updates that can leave state half-applied.
   - Prefer clearer orchestration, parallel independent work when it improves the flow, and more atomic update structures.

## Review Tone

Be direct, serious, and demanding about quality.
Do not be rude, but do not soften major maintainability issues into mild suggestions.
If the code is making the codebase messier, say so clearly.
If the implementation missed an opportunity for a dramatic simplification, say that clearly too.

Good phrases:

- `this pushes the file past 1k lines. can we decompose this first?`
- `this adds another special-case branch into an already busy flow. can we move this behind its own abstraction?`
- `this works, but it makes the surrounding code more spaghetti. let's keep the behavior and restructure the implementation.`
- `this feels like feature logic leaking into a shared path. can we isolate it?`
- `this abstraction seems unnecessary. can we just keep the direct flow?`
- `why does this need a cast or `!` here? can we make the nullability/contract explicit instead?`
- `this looks like a bespoke helper for something we already have elsewhere. can we reuse the canonical one?`
- `i think there's a code-judo move here that makes this much simpler. can we reframe this so these branches disappear?`
- `this refactor moves complexity around, but doesn't really delete it. is there a way to make the model itself simpler?`

## Output Expectations

Prioritize findings in this order:

1. Structural code-quality regressions
2. Missed opportunities for dramatic simplification / code-judo restructuring
3. Spaghetti / branching complexity increases
4. Boundary / abstraction / type-contract problems that make the code harder to reason about
5. File-size and decomposition concerns
6. Modularity and abstraction issues
7. Legibility and maintainability concerns

Do not flood the review with low-value nits if there are larger structural issues.
Prefer a smaller number of high-conviction comments over a long list of cosmetic notes.

## Approval Bar

Do not approve merely because behavior seems correct.
The bar for approval is that the change satisfies the non-negotiable standards above, or any exception is justified clearly.

If those conditions are not met, leave explicit, actionable feedback and push for a cleaner decomposition.