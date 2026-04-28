# Decision: Learnings File Scope

**Status:** Resolved as "skip"
**Date:** 2026-04-27
**Source:** [`docs/prompts/foundation/foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §7.3
**Phase:** Foundation refresh Phase 4 Q3

CritterCab's research has explored Dilger SDD's slice-grain learnings file pattern (the §4 "Learnings" section in `C:\Code\CritterCab\docs\research\sdd-event-model-to-code.md`). CritterBids has session-grain retrospectives in `docs/retrospectives/` plus the cross-cutting methodology log at [`docs/research/methodology-log.md`](./methodology-log.md). Foundation refresh Phase 4 Q3 considered whether to add a slice-grain learnings file as a third layer.

## Options Considered

1. **Skip.** Retrospectives plus the methodology log are sufficient.
2. **Per-BC learnings file** (`docs/skills/<bc>/learnings.md` per BC). Slice-grain; persists across sessions; stable rules migrate into skill files over time.
3. **Project-wide learnings file** (single `docs/learnings.md`).

## Decision

**Option 1: Skip.** No slice-grain learnings file is created. The two existing layers carry the load:

- **Session-grain retrospectives** in `docs/retrospectives/` capture per-session learnings with explicit cross-references between prompt, retro, and outcome. CritterBids has practiced this from M1; the discipline is mature.
- **Cross-cutting methodology log** at `docs/research/methodology-log.md` carries observations that span sessions or artifact layers. Restrictive entry-criteria gate ("predicts something about how the methodology will or should evolve"); silence is fine.

A slice-grain learnings file would be a third layer between the per-session retrospective and the cross-cutting methodology log. The marginal value is unclear in CritterBids' current shape: M3-S4b's terminal-paths retro, M3-S5b's saga-skeleton retro, and foundation-refresh Phase 2.5's regression-defense test retro all carry slice-grain observations effectively without a separate file. Adding a third layer risks fragmenting where slice-grain learnings live (some in the retro, some in the file, with reviewer cognitive load to know which goes where).

This decision is recorded as a decision note rather than a full ADR per `docs/decisions/README.md` §"When to Write an ADR" - the choice is not hard to reverse (a learnings file can be introduced later), not architecturally cross-cutting, and not likely to surprise contributors. A short decision note suffices.

## Trigger for Revisit

If the foundation-refresh Phase 5 backfill cohort (four narrative sessions for Auctions, Settlement, Selling, Participants BCs) produces slice-grain observations that do not fit cleanly in either the per-session retro or the methodology log - specifically, observations too narrow for the methodology-log entry-criteria gate but too broad for a single retro to carry - this decision is reopened.

A second trigger: if the project ever adopts the Ralph Loop (Dilger SDD's agent operating cycle: find planned slices, implement, test, record learnings, clear context, repeat), the learnings-file-per-cycle pattern becomes operationally relevant and the decision is reconsidered alongside that adoption.

## References

- `docs/retrospectives/README.md`: session-grain retrospective template (the existing layer)
- `docs/research/methodology-log.md`: cross-cutting methodology observations (the existing layer)
- `C:\Code\CritterCab\docs\research\sdd-event-model-to-code.md` §4: Dilger SDD's slice-grain learnings pattern (the alternative considered)
- `docs/prompts/foundation/foundation-refresh-handoff.md` §7.3: the question framing this decision note resolves
