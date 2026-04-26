# Session Retrospectives

This directory holds the retrospectives produced at the close of each
CritterBids implementation session — one retro per session prompt under
`docs/prompts/`. The retro is the durable record of what happened when that
prompt ran, what was learned, and what the next session needs to know.

Retros are living documents. **The template and the rules below evolve
through M1 retrospectives** — after each M1 session lands, revisit this file
and fold in whatever the retro itself surfaced about the format. M1 is where
the shape of a "good CritterBids retrospective" gets discovered; don't treat
anything here as frozen until M1 is closed out.

## Naming convention

```
M{milestone}-S{slice}[{letter}]-{kebab-case-summary}.md
```

Examples: `M1-S1-solution-baseline.md`,
`M1-S2-participants-bc-scaffold.md`. The `letter` suffix is
reserved for follow-up sessions that revise or replace the prior session's
output (e.g. `S1B` replacing `S1` after a workaround is swapped for the real
implementation). The slug mirrors the matching prompt filename so prompt and
retro sort next to each other when listed.

## Template format

Every retrospective file follows this structure:

```markdown
# M{n}-S{n}: {Title} — Retrospective

**Date:** YYYY-MM-DD
**Milestone:** M{n} — {name}
**Slice:** S{n} — {slice name}
**Agent:** @{PSA|QAE|PO|UXE|DOE}
**Prompt:** `docs/prompts/implementations/M{n}-S{n}-{slug}.md`
[**Duration:** ~Xh]

## Baseline

Three to five bullets capturing the starting state: build errors/warnings,
test counts (BC-scoped and full solution), and the structural facts the
session's diff will be measured against.

## Items completed

| Item | Description |
|------|-------------|
| S{n}a | … |

Mirror the session prompt's item codes exactly so prompt ↔ retro
traceability is mechanical.

## S{n}{letter}: {Title}

One subsection per non-trivial item. Each contains whichever of these apply:

- **Why this approach** — when a Critter Stack idiom was chosen or rejected,
  say *why* the alternative was rejected. These passages are the slice's
  contribution to the broader pattern library.
- **Handler / structure after** — short code block showing the resulting shape.
- **Structural metrics table** — `Metric | Before | After` for line count,
  class type, injected dependencies, return type. The most distinctive
  recurring element of a useful retro.
- **Discovery / resolution** — when something failed and was worked around,
  document the error message verbatim, the root cause, and the resolution.
- **Edge cases preserved or fixed** — bugs found incidentally.

## Test results

| Phase | {BC} Tests | Result |
|-------|-----------|--------|

A phased table showing pass count after each item or sub-step. Always end
with the final state and call out whether test count changed.

## Build state at session close

Bullets covering: errors, warnings (with delta from baseline and explanation
if changed), and BC-scoped grep-style metrics that prove the work landed
("`session.Events.Append()` calls: 0", "handlers using `[WriteAggregate]`:
3"). These negative-space assertions are stronger than prose and grep-able
by future sessions.

## Key learnings

Numbered list of generalizable insights. Each is one or two sentences naming
the principle and the evidence. Reserve this section for things future
sessions in other slices or BCs will need to know; do not restate item-level
details.

## Verification checklist

- [x] One item per acceptance criterion from the session prompt.

Mirrors the prompt's acceptance-criteria checklist 1:1 so the retro doubles
as sign-off.

## What remains / next session should verify

Bullets calling out deferred work, follow-ups, and explicit non-goals.
Distinguish "in scope for the milestone, deferred to S{n+1}" from "out of
scope, tracked elsewhere."
```

## Optional sections

Use only when the session warrants them:

- **{BC} assessment (after S0+S1+S2…)** — for milestones that complete a BC,
  a numbered summary of what the BC now demonstrates idiomatically.
- **Files changed** — categorized list (New / Modified / Deleted / Tests /
  Docs) with one-line annotations. Use only when the change set spans many
  files or projects.
- **API surface explored** — when a session is research-heavy against an
  unfamiliar Critter Stack feature, document what was tried, what worked,
  and what didn't.
- **Comparison vs prior session** — `Component | Before | After` spanning
  the whole session. Use when one session revises or replaces the output of
  the prior session.

## The ten rules

These are the prose rules every retro in this directory obeys. They exist
so retros stay terse, factual, and useful to the next session that has to
build on this one.

1. **One retro equals one session.** A session that produced one PR
   produces one retrospective. Multi-session summaries belong in the
   milestone doc, not here.

2. **The session prompt is the spine.** Retros mirror the prompt's item
   codes, acceptance criteria, and scope boundaries. If the prompt and retro
   disagree about what was attempted, the retro is wrong.

3. **Concrete over narrative.** Tables, signatures, counts, file paths,
   verbatim error messages. Prose is reserved for explaining tradeoffs the
   tables can't capture.

4. **Name the idiom.** When an idiomatic choice is made, say *why* the
   alternative was rejected. The "why not" is more valuable to future
   sessions than the "what."

5. **Cross-reference other BCs.** When a pattern is reused, name the
   reference BC ("same shape as Listings", "matches the Auctions handler").
   This builds the implicit cross-BC index every future session benefits
   from.

6. **Verbatim error messages for failures.** Compiler errors, runtime
   exceptions, and test output are useful precisely because they're
   searchable by future sessions hitting the same wall. Quote them, don't
   paraphrase them.

7. **Negative assertions are first-class.** "`IDocumentSession` usage: 0"
   is a stronger and more grep-able claim than any prose paragraph claiming
   the refactor is "complete."

8. **No marketing voice.** A short BC assessment paragraph is the upper
   bound of self-congratulation; everything else stays factual. No "elegant
   solution," no "clean architecture."

9. **Preserve the prompt's item codes.** S1a, S1b, S2a — never renumber,
   even if the work landed in a different order than the prompt listed it.

10. **Retros are committed in the same PR as the session's code.** A
    session without a retro is a session whose lessons evaporate. The retro
    is part of the deliverable, not a follow-up.

## What a retro is not

- **Not a changelog.** Files-changed lists are optional and only useful
  when the change set is large or distributed.
- **Not a design doc.** ADRs handle the "why we chose this architecture"
  question; the retro records what happened during execution and what was
  learned in the process.
- **Not a tutorial.** Code blocks show the resulting shape, not how to
  teach the pattern to a newcomer. Skill files do the teaching.

## Known gaps

Until M1 retrospectives say otherwise, expect the template and the rules
to move. Propose changes by PR against this file with a short note
describing which session surfaced the gap.

See `../prompts/README.md` for the matching session prompt template —
together they form the prompt → execute → retro loop that every M1 session
runs through.
