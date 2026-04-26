# CritterBids Prompts Index

Prompts are the **session-trigger artifacts** in CritterBids' session-driven workflow. Each prompt captures the goal, context, working pattern, and deliverables for a single working session: workshop, narrative authoring, ADR drafting, skill-file authoring, or implementation. The session produces a corresponding artifact in `docs/workshops/`, `docs/narratives/`, `docs/decisions/`, `docs/skills/`, or in code, plus a retrospective in `docs/retrospectives/`.

This directory is part of the **`narrative → prompt → execute → retrospective`** loop documented in [`CLAUDE.md`](../../CLAUDE.md). The prompt is the durable, version-controlled record of intent for a session; the retrospective closes the loop after the session completes.

For the rules and template that govern *how* to author a prompt, see [`AUTHORING.md`](./AUTHORING.md). For the human-facing operational loop (how to run a session, review against acceptance criteria, feed retros back into the template), see [`WORKFLOW.md`](./WORKFLOW.md).

---

## Subdirectory layout

Prompts are organized by the **kind of artifact** they trigger, mirroring the structure of `docs/`:

| Subdirectory | Triggers a session that produces |
|---|---|
| [`workshops/`](./workshops/) | An Event Modeling, Domain Storytelling, or Context Mapping artifact in `docs/workshops/`. |
| [`narratives/`](./narratives/) | An NDD-informed narrative in `docs/narratives/`. |
| [`skills/`](./skills/) | A component-scoped skill file in `docs/skills/`. |
| [`decisions/`](./decisions/) | An ADR in `docs/decisions/`. |
| [`implementations/`](./implementations/) | A code-implementation session targeting one or more slices from a workshop or narrative pair. |
| [`foundation/`](./foundation/) | A multi-phase orchestration prompt that governs a program of work spanning multiple sessions. |

Subdirectories appear as their first prompt lands; an empty subdirectory is not pre-created. As of this README's authoring, `implementations/` and `foundation/` are populated; the others land as their first prompt is authored.

---

## Naming convention

Per-subdirectory variants reflect the artifact each subdirectory produces:

| Subdirectory | Naming pattern | Example |
|---|---|---|
| `implementations/` | `M{milestone}-S{slice}-{slug}.md` | `M3-S5b-auction-closing-saga-terminal-paths.md` |
| `narratives/` | `{nnn}-{slug}.md` (mirrors the narrative file's slug) | `001-bidder-wins-flash-auction.md` |
| `decisions/` | `{nnn}-{slug}.md` (mirrors the ADR being authored) | `016-spec-anchored-development.md` |
| `workshops/` | `{nnn}-{slug}.md` (mirrors the workshop being authored) | `005-settlement-bc-deep-dive.md` |
| `skills/` | `{slug}.md` (mirrors the skill file being authored) | `dynamic-consistency-boundary.md` |
| `foundation/` | descriptive slug | `foundation-refresh-handoff.md` |

Counters are per-subdirectory (each subdirectory has its own `001-...`, `002-...` series for narratives/decisions/workshops). Implementation prompts use the pre-existing `M{milestone}-S{slice}` convention from M1 to M4 (preserved by the foundation refresh; not renumbered).

---

## Cross-references

Each prompt cross-references its target artifact (and vice versa). The artifact's "Document History" or session-log section names the prompt that drove the session; the prompt's metadata block names the artifact it produced.

Retrospectives in `docs/retrospectives/` carry a `**Prompt:**` header line pointing at the prompt that drove the session.

When a session re-runs (rare: typically only when the original deliverable was abandoned and re-authored), the new prompt gets the next numeric prefix in its subdirectory rather than overwriting the original. The prompt-history is itself part of the project's record.

---

## When to create a new prompt

Create a new prompt for any new session, including follow-ups. Do not edit a prompt after the session it triggered has run: the prompt is a historical record of intent at session start, not a living document. If a session's scope expands mid-flight, capture the expansion in that session's retrospective and (if warranted) author a follow-up prompt for the additional work.

For the full ten rules (one PR per prompt, scope by milestone and slice, milestone doc authoritative, etc.) and the template skeleton, see [`AUTHORING.md`](./AUTHORING.md).

---

## Format conventions inside a prompt file

Each prompt file should include, at minimum:

- **Metadata block** at the top: status, target artifact (path or planned slug), date authored, optionally a one-line outcome once the session completes.
- **Framing**: one or two sentences explaining why this session exists in the project's arc.
- **Goal**: a single declarative sentence stating what the session produces.
- **Orientation files**: ordered list of files the session-runner should read before starting.
- **Working pattern**: interactive cadence, sign-off discipline, what gets committed when.
- **Deliverable plan**: what files the session should produce or modify.
- **Out of scope**: explicit list of things the session should not pull in opportunistically.

Subsequent sections are prompt-specific. The full prompt template skeleton with section examples lives in [`AUTHORING.md`](./AUTHORING.md). Existing prompts in the subdirectories serve as references for shape.

---

## Current contents

### Implementations (`implementations/`)

27 prompts spanning M1 through M4-S2. Sessions ran in order from `M1-S1-solution-baseline.md` through `M4-S2-selling-withdraw-listing.md`. M3-S6 onward shipped between the foundation-refresh hand-off prompt's authoring and its execution. See per-prompt files for status; their corresponding retrospectives in `docs/retrospectives/` confirm completion.

### Foundation (`foundation/`)

- [`foundation-refresh-handoff.md`](./foundation/foundation-refresh-handoff.md): the multi-phase methodology refresh prompt that governs ADR 016, ADR 017, the narratives directory, the rules directory, this subdivision, the methodology log, and downstream Phase 2 to 4 work. Currently in execution; Phase 1 is in progress as of this README's authoring.

### Narratives, Decisions, Workshops, Skills

Empty as of this README's authoring. The first narrative prompt lands in foundation-refresh Phase 2 (planned slug: `narratives/001-bidder-wins-flash-auction.md`).

---

## Document history

- **v0.2** (2026-04-26): Rewritten as part of foundation-refresh Phase 1 Item 5. Adopts CritterCab's index-shape: subdirectory table, per-subdirectory naming convention, cross-reference convention, current contents per subdirectory. The pre-existing ten-rules and template-skeleton content moves to [`AUTHORING.md`](./AUTHORING.md), keeping this README lean as an index. The pre-existing implementation prompt naming convention (`M{milestone}-S{slice}-{slug}.md`) is preserved verbatim for the `implementations/` subdirectory. `foundation/` subdirectory introduced for multi-phase orchestration prompts.
- **v0.1** (M1 era): Initial template + ten rules + Known gaps. Ten rules were the main content carried by the README.
