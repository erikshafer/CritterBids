# Foundation Refresh Phase 4: Retrospective

**Date:** 2026-04-27
**Phase:** Phase 4 (Open questions resolved by ADR) of the foundation refresh
**Prompt:** [`docs/prompts/foundation/foundation-refresh-phase-4.md`](../prompts/foundation/foundation-refresh-phase-4.md)
**Authoritative scope:** [`docs/prompts/foundation/foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §7
**Branch:** `foundation-refresh/p4-adrs`

## What landed

Five questions resolved across six commits (the prompt setup commit plus one commit per question; this retro is the seventh). Question ordering followed user override at session start (Q1 first, then smallest-to-largest for the remainder).

| Question | Disposition | Commit | Artifact |
|---|---|---|---|
| Setup | Phase 4 session prompt | `e918652` | [`docs/prompts/foundation/foundation-refresh-phase-4.md`](../prompts/foundation/foundation-refresh-phase-4.md) (282 lines) |
| Q1 Reqnroll position | Accepted ADR (Decline at MVP) | `2d2e58c` | [`docs/decisions/018-reqnroll-position.md`](../decisions/018-reqnroll-position.md) |
| Q5 Ops runbook | Parked with trigger | `80082d5` | [`docs/decisions/PARKED.md`](../decisions/PARKED.md) §"Active" entry P-001 |
| Q3 Learnings file scope | Decision note (Skip) | `389d833` | [`docs/research/learnings-file-scope.md`](../research/learnings-file-scope.md) |
| Q2 Glossary strategy | Decision note (Per-BC only) | `3d6e5a1` | [`docs/vision/glossary-strategy.md`](../vision/glossary-strategy.md) |
| Q4 Demo-script runbook | Parked with trigger | `43530e9` | `docs/decisions/PARKED.md` §"Active" entry P-002 |

Five new files, one updated file (`docs/decisions/README.md` for the ADR-018 Status Ledger entry and `PARKED.md` discoverability link). Net: 1 ADR, 2 decision notes, 1 ledger (with 2 entries), 1 prompt, 1 retro.

## Items folded in beyond the original scope

**`docs/decisions/PARKED.md` as a project ledger.** The Phase 4 prompt's default for Q4 and Q5 was "recorded in the Phase 4 retrospective" with the alternative being "stub `docs/parked-decisions.md` (or similar)." User overrode to the stub-file shape at session start; the file landed at `docs/decisions/PARKED.md` (capital filename mirroring the existing `docs/workshops/PARKED-QUESTIONS.md` convention). The ledger's append-only Active/Closed structure plus `P-NNN` numbering (matching narrative `001-NNN` and ADR `NNN-` numbering) sets a project-grade pattern for future parked decisions beyond Phase 4. Discoverability link added to `docs/decisions/README.md` §"References."

**Decision notes adjacent to where the alternative artifact would have lived.** Q2's decision note landed at `docs/vision/glossary-strategy.md` because the alternative was `docs/vision/glossary.md`. Q3's landed at `docs/research/learnings-file-scope.md` because the alternative was `docs/research/learnings.md`-or-similar. The "decision note lives where the alternative would have lived" pattern emerged from Phase 4 and is worth naming as a future convention candidate alongside ADRs and the PARKED ledger - the question's *would-have-been-here* location is the natural home for *why-it-isn't-here*.

## Open questions surfaced during Phase 4

Three items warrant flagging; none warrant their own ADR before Phase 5 starts.

- **Methodology log time-box.** Original time-box: "close of Phase 2 or after the third entry, whichever comes first." Phase 2 closed without an entry; Phase 3 closed without an entry; Phase 4 closed without an entry (continued deferral per user override). The original time-box has now passed by two phases. See "Methodology log time-box review" below for the deferred-once-more disposition.
- **CritterCab coordination on Reqnroll.** ADR-018 was authored independently; CritterCab can backreference if/when it authors its matching ADR. CritterCab's matching position is unknown at Phase 4 close; if CritterCab adopts a different option (e.g., Adopt at MVP), the cross-project methodology layer becomes inconsistent. Not a CritterBids problem to solve; flag for awareness.
- **PARKED ledger duplication risk.** The `docs/decisions/PARKED.md` ledger and the existing `docs/workshops/PARKED-QUESTIONS.md` cover similar shapes (parked items with triggers) at different grains (methodology and architecture vs. domain modeling). Future contributors may wonder which file to add a new parked item to. The intro paragraph in PARKED.md names the distinction; if the distinction proves load-bearing, it could become a brief skill-file entry.

## Key learnings

1. **Decision notes at adjacent locations are lighter ceremony than ADRs and earn their keep when the decision is not hard-to-reverse, not cross-cutting, and not likely to surprise contributors.** Q2 (glossary at `docs/vision/`) and Q3 (learnings at `docs/research/`) both fit. The pattern - decision notes live where the alternative artifact would have lived - is candidate convention material for the next narratives or decisions README pass.

2. **Parked-items ledger as a first-class project artifact.** `docs/decisions/PARKED.md` carries items across many phases without clutter via the append-only Active/Closed structure. The `P-NNN` numbering matches narrative and ADR numbering schemes for consistency. Phase 4 produced two entries; future phases or sessions can add entries without further structural decisions.

3. **The "trigger condition" discipline for parked items is non-trivial and worth naming.** P-001's trigger (production-leaning deployment scheduled with specific environment) and P-002's trigger (conference talk scheduled with date, venue, presenter) are both specific and observable. Vague triggers ("when needed," "if it becomes important") don't earn their keep because they never fire; specific triggers do because they tie to events that the project will recognize when they happen.

4. **Phase 4 was the lightest phase by file count (5 new files: 1 ADR, 2 decision notes, 1 ledger, 1 prompt) but produced disproportionate methodology decisions.** The methodology refresh's center of gravity has shifted from artifact production (Phases 1-3 produced narratives, workshop sections, skill-file additions, retros) to disposition recording (Phase 4 records what was decided not to do). This shift is expected at the end of a methodology refresh; Phase 5's operational adoption will return to artifact production (four backfill narratives, AUTHORING.md and retrospectives README amendments).

5. **One-way coordination between sibling projects.** Q1's CritterCab coordination point was framed as one-way (CritterBids ADR-018 stands alone; CritterCab can backreference) per user override. This pattern of one-way coordination between sibling Critter Stack projects keeps each project's methodology layer self-contained without forcing simultaneous decisions or shared maintenance. Future cross-project methodology decisions can default to this shape.

## Methodology log time-box review

Per [`foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §7.6: "Is the methodology log carrying its weight? Apply the time-box review."

The methodology log at [`docs/research/methodology-log.md`](../research/methodology-log.md) currently has zero entries. The original time-box from the methodology log itself: "Decision to keep, fold, or remove the file is revisited at the close of Phase 2 or after the third entry, whichever comes first." Phase 2 closed without an entry (Entry 001 deferred to Phase 5 backfill cohort per Phase 2 retro). Phase 3 closed without an entry (deferred). Phase 4 closes without an entry (continued deferral per Phase 4 prompt §9 sign-off override).

**Disposition: continued deferral, time-box updated.** The new trigger: "after Phase 5 closes, or after the methodology log carries three entries, whichever comes first." Phase 5's backfill cohort produces four narrative sessions; if any of those surface a genuinely cross-cutting observation that meets the entry-criteria gate ("predicts something about how the methodology will or should evolve"), Entry 001 lands then. If Phase 5 closes with zero entries, the next review applies the same gate to the file's continued existence.

The entry-criteria gate has held through three phases of conscious skips. Silence is fine. The file's failure mode is not silence but writing entries that don't earn their keep; the gate has prevented that failure mode without producing the alternative failure (premature deletion of a file that earns its keep later).

## Phase 5 readiness

Phase 5 (Operational adoption) becomes the next phase per the foundation-refresh handoff §15. Phase 5 is the final phase of the foundation refresh and where the new workflow becomes the operational default.

Phase 5's four items per handoff §15:

1. **Backfill narratives** for the four lived BCs (Auctions, Settlement, Selling, Participants). Each authored under the Phase 2 narrative discipline.
2. **Amend `docs/prompts/AUTHORING.md`.** Rule 3 grows ("milestone doc and relevant narrative are jointly authoritative"); implementation prompt template grows a `Narrative:` line.
3. **Amend `docs/retrospectives/README.md`.** Adds a "Findings against narrative" section to the retrospective template.
4. **Declare and cross the cutover gate.** M5's first slice prompt cites a narrative as jointly authoritative scope.

Phase 4's outputs feed Phase 5 in three places: ADR-018's "Decline Reqnroll" means Phase 5's AUTHORING.md amendment doesn't need to allocate space for executable-spec mentions; Q2's per-BC-only glossary disposition means Phase 5's amendments don't need to introduce a project-level glossary section; Q3's "skip" learnings disposition means Phase 5 doesn't need to amend the retrospective template with a learnings-file pointer.

## What's the next non-methodology slice

Per handoff §7.6 the retro should answer "What's the next non-methodology slice (i.e., back to product work)?"

**M5 (Settlement BC).** After Phase 5 closes, the foundation refresh is complete. The first non-methodology slice is the M5 milestone's foundation-decisions session - the equivalent of M3-S1 (Auctions Foundation Decisions) and M4-S1 (Auctions Completion Foundation Decisions) for Settlement. M5-S1 (or equivalent slug) would resolve any open Settlement decisions surfaced from W003's Phase 1 (decider-pattern semantics, ProcessManager<TState> vs. Wolverine Saga implementation choice, PendingSettlement projection schema) and open the Settlement BC for slice work.

The cutover gate from Phase 5 §15.5 applies: M5-S1's prompt cites the Settlement BC narrative (Phase 5's backfill narrative for Settlement) as jointly authoritative scope alongside the M5 milestone doc. Without that citation, the cutover hasn't happened.

## Verification checklist (from Phase 4 prompt §6)

- [x] Q5 (ops runbook) has a recorded disposition: parked-with-trigger via PARKED.md P-001.
- [x] Q3 (learnings file scope) has a recorded disposition: skip, via decision note `docs/research/learnings-file-scope.md`.
- [x] Q2 (glossary strategy) has a recorded disposition: per-BC only, via decision note `docs/vision/glossary-strategy.md`.
- [x] Q4 (demo-script runbook) has a recorded disposition: parked-with-trigger via PARKED.md P-002.
- [x] Q1 (Reqnroll position) lands as ADR-018 with a clear decision (Decline at MVP) and consequences.
- [x] `docs/decisions/README.md` Status Ledger reflects ADR-018; next-unreserved pointer advanced from 018 to 019.
- [x] No file under `src/` or `tests/` was edited in this session.
- [x] No new narrative, no new workshop, no new convention rollout.
- [x] No em dashes in any committed prose authored by this session (audit-after-write applied per file).
- [x] Phase 4 retrospective contains a "Methodology log time-box review" section per handoff §7.6 (continued deferral with new trigger after Phase 5 close).
- [x] Phase 4 retrospective answers "What's the next non-methodology slice?" per handoff §7.6 (M5 Settlement BC).

## Document history

- **v0.1** (2026-04-27): Authored at Phase 4 close as the seventh commit on the `foundation-refresh/p4-adrs` branch. Mirrors the structure of Phases 1, 2, 2.5, 3 retros; resolves the methodology-log time-box review and the next-non-methodology-slice question per handoff §7.6.
