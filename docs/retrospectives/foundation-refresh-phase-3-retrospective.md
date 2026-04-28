# Foundation Refresh Phase 3: Retrospective

**Date:** 2026-04-27
**Phase:** Phase 3 (Convention rollouts) of the foundation refresh
**Prompt:** [`docs/prompts/foundation/foundation-refresh-phase-3.md`](../prompts/foundation/foundation-refresh-phase-3.md)
**Authoritative scope:** [`docs/prompts/foundation/foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §6
**Branch:** `foundation-refresh/p3-conventions`

## What landed

Five items plus two pre-Phase-3 setup commits, each as its own commit on the Phase 3 branch. The branch is seven commits ahead of `origin/main` at retrospective time (this retro is the eighth).

| Item | Commit | Files | Description |
|---|---|---|---|
| Setup A: filename rename | `7c8afe9` | 3 changed | Renames `docs/retrospectives/phase2-5-extension-calculation-fix-retrospective.md` to `foundation-refresh-phase-2-5-retrospective.md` so all foundation-refresh phase retros share a slug pattern; updates the three references. |
| Setup B: Phase 3 session prompt | `c2ccf49` | 1 new | Authors the Phase 3 session prompt at `docs/prompts/foundation/foundation-refresh-phase-3.md` (303 lines, item ordering 1-4-5-3-2, references handoff §6 as authoritative scope). |
| 1: Status column on W001-W004 | `334f4bf` | 4 changed | Adds `Status` column to W001 (10 Tier tables, 34 slices), W002 (Coverage by Component, 5 rows), W004 (Coverage by Component, 7 rows) with values from the four-vocabulary {design \| planned \| in progress \| done}. W003 carries a clarifying paragraph instead (no slice table; workshop is itself in Phase 1). |
| 4: Ubiquitous Language §3 sections | `27300f4` | 3 changed | Adds §3 Ubiquitous Language to W002 (16 terms), W003 (14 terms), W004 (15 terms) using Option A (define once per BC, cross-reference). Cross-references via markdown anchor links to other §3s. Domain events are not duplicated in §3; readers are pointed at `docs/vision/domain-events.md`. |
| 5: Adjunct Patterns in skill | `2c0c023` | 2 changed | Adds "Adjunct Patterns" section to `docs/skills/event-modeling/SKILL.md` naming Klefter translation-decision events, Bruun temporal-automation slice pattern, and Bruun configuration-as-events. Updates the narratives README's existing forward-looking note about Bruun's `*` suffix to a concrete back-reference into the new skill section. |
| 3: Cast and Setting on W001 | `713c93d` | 1 changed | Adds `## Cast` (5 human-actor roles + 8 BCs) and `## Setting` (5 policy-posture paragraphs) to W001 between the title-block metadata and Phase 1. Role-grain Cast (not protagonist-grain); Tier-grain onstage status (not per-Moment). Cross-reference to Narrative 001 §"Cast" anchors the role-to-protagonist mapping. |
| 2: Bidirectional referencing codification | `38d53da` | 1 changed | Amends `docs/narratives/README.md` §"Bidirectional referencing" to name both per-row and consolidated forms with their respective defaults; drops the "(forward-looking)" parenthetical now that the convention is exercised. Phase 2's deviation becomes a standing convention. |

## Items folded in beyond the original five

**Pre-Phase-3 setup (Setup A and B above).** The Phase 2.5 retro filename was named by its prompt slug (`phase2-5-extension-calculation-fix-retrospective.md`) rather than the foundation-refresh-phase-N convention used by Phases 1 and 2. Caught at session start when reviewing Phase 2.5 context for Phase 3 prep; renamed before any Phase 3 item began. The Phase 3 session prompt itself was also authored as session-setup rather than during a prior phase, mirroring Phase 1's foundation-refresh-handoff pattern. Both setup commits live as their own atomic units on the Phase 3 branch rather than being mixed into Item 1's commit.

**W004 unit-test coverage gap surfaced by Item 1.** Item 1's status assignment for W004 §5 (Validation rules) flipped from `done` to `in progress` per user override during the proposal-and-sign-off step. The override carries a project-level recommendation: a future implementation slice should bring full pure-function unit-test coverage to all 14 validation rules. Captured in Item 1's commit message and surfaced again in the W004 §3 Validation Service entry's Notes cell ("Full unit-test coverage is the implication of the W004 §5 status `in progress`"). The recommendation does not absorb into Phase 3; it routes to a future M-something or Phase 5 implementation slice via the spec-anchored discipline.

**Em-dash sweep audit pattern.** Phase 3 prompt §4.3 named the convention ("rows under edit sweep to hyphens; pre-existing em dashes retain the grandfather clause") but did not name the verification method. After Items 4 and 5, six authored em dashes slipped into §3 table cells and the skill file insert; an audit-after-write grep pattern caught them and they swept cleanly. By Item 3 (Cast and Setting), the discipline had internalized; the new content was em-dash-clean from authoring. The "audit after write" step deserves a place in the working-pattern guidance for future docs-only sessions.

## Open questions surfaced during Phase 3

Three items warrant flagging; none warrant their own ADR before Phase 4 starts.

- **Layer 3 (code conventions) of the rules system remains deferred.** Phase 1 retro noted Layer 2 (per-BC ubiquitous language) lands during Phase 3 Item 4; Layer 2 is now seeded by W002/W003/W004 §3 sections plus W001's existing implicit vocabulary. The natural next step is authoring `docs/rules/<layer-2-ubiquitous-language>.md` distilling the §3 entries into directive sentences, but that's a future session per the rules README's deferral note - not Phase 3 scope. Layer 3 (code conventions) follows Layer 2 and remains a future-future session.
- **M4-S2 retrospective absence remains unresolved.** Phase 2 retro flagged this as an operational note ("WithdrawListing implementation without a retrospective"). Phase 3 surfaced it again via W004 §4 (End early and relist) status `in progress`. The retro absence is a project-grade discipline break, not a Phase 3 issue. Whether to backfill M4-S2's retro retroactively or accept the gap as historical is a project-level call - flag for Phase 4 or Phase 5 disposition.
- **Hybrid workshop/narrative format.** W001 now carries Cast and Setting (narrative-format primitives) alongside its Phase 1-5 slice walk and milestone mapping (workshop primitives). The blended artifact reads naturally as both a workshop and a narrative-anchor, but the narratives README does not currently name "hybrid workshop/narrative" as a documented pattern. If Phase 5's W001 backfill or future journey workshops continue the hybridization, the narratives README may want a §"Hybrid format" subsection to legitimize the pattern. Not Phase 3's responsibility to author.

## Key learnings

1. **Phase 2 deviations become Phase 3 conventions.** The consolidated bidirectional-reference form was a Phase 2 deviation (one-off; user-approved mid-session). Phase 3 Item 2 codified it as a standing convention alongside the per-row form. The deviation-to-convention pipeline is how methodology evolves under the spec-anchored discipline: a deviation that survives review becomes precedent; a precedent that recurs becomes convention; a convention that needs enforcement becomes a guardrail. This is one data point for the pipeline; Phase 5's backfill narratives may produce more.

2. **Workshop hybridization creates a useful blended artifact.** Adding narrative-format Cast and Setting to W001 (a journey workshop) makes it readable as both a workshop (slice tables, milestone mapping, Phase 5 scenarios) and a narrative-anchor (Cast, Setting, role/protagonist relationships). The hybrid form preserves the workshop's slice-walking discipline while exposing the actor and policy posture that narratives anchor on. Phase 5's backfill narratives will exercise the inverse - taking journey-grain Cast/Setting and dramatizing specific protagonists at Moment-grain - which will validate or challenge the hybrid pattern.

3. **Cross-reference graph density grew significantly in one session.** Pre-Phase-3: workshops rarely cited each other; narratives cited workshops via `Implements:`; cross-BC vocabulary was implicit. Post-Phase-3: workshops have explicit per-BC §3 with cross-BC markdown anchor links; narratives README links to the skill file's Adjunct Patterns; skill file links to the workshops' §3 sections; W001 links to Narrative 001 §"Cast". The bidirectional graph is now navigable from any node in one click. This was emergent from per-item edits, not pre-designed - the Item 4 cross-reference shape was determined at Item 4 kickoff (Option A); the back-references in Item 5 and Item 3 followed the same pattern. The shape held.

4. **Em-dash discipline at row-grain works but requires audit-after-write.** The "rows under edit sweep to hyphens; pre-existing em dashes retain the grandfather clause" rule from Phase 3 prompt §4.3 produced cleaner tables (Item 1) and surface-clean prose (Items 4-5 after sweep audit; Item 3 from authoring). Six em dashes slipped into Items 4-5's authored content despite the rule; a regex audit for the em-dash codepoint (`U+2014`) after each major insert caught them. The discipline scales with audit; it does not scale with author-time vigilance alone. Future docs-only phases should bake the audit step into the working pattern as a checkpoint, not just a closing check.

5. **Per-item commit cadence is bisect-friendly.** Five item commits plus two setup commits, no commits mixed concerns. A reviewer landing on the PR can see each item's scope cleanly; a bisect on convention-related regressions can isolate to the item that introduced the convention. This mirrors Phase 1's six-item cadence and Phase 2's twelve-Moment cadence; the per-item shape is robust across phases regardless of item count.

6. **The handoff doc as authoritative scope worked end-to-end.** AUTHORING.md rule 3 ("milestone doc authoritative; prompt references rather than duplicates") was tested by Phase 3's prompt deferring to handoff §6 for item specs and supplying only execution shape. Each Item edit could be cross-checked against handoff §6.x, and the prompt itself remained tight (303 lines vs. the handoff's 800). The pattern of "phase prompt = execution shape + observations + open questions; phase scope = handoff §N" is reusable for future phase prompts (Phase 4 will follow the same shape).

## Methodology log Entry 001 conscious skip

Per the foundation-refresh handoff §4.7 and Phase 2's precedent, methodology log Entry 001 was considered at session close and consciously skipped. Phase 2 deferred Entry 001 to the Phase 5 backfill cohort, when a comparison ratio of `narrative-update`/`workshop-update`/`code-update`/`document-as-intentional` finding lanes becomes available across multiple narratives. Phase 3 produces no findings (it is a docs-only convention rollout, not a narrative session); the candidate observation from this phase is the deviation-to-convention pipeline (Key Learning 1), but one data point does not yet predict where else this will happen. The entry-criteria gate's "predicts something about how the methodology will or should evolve" requirement has not yet earned its keep. Defer Entry 001 to the Phase 5 backfill cohort, where finding-lane ratios across four narratives plus narrative 001 give the gate something to lean on. The methodology log's silence remains honest; the entry-criteria gate held.

## Phase 4 readiness

Phase 4 (Open questions resolved by ADR) becomes the next phase per the foundation-refresh handoff. Phase 4 is the natural endpoint of the methodology layer: each open methodology question (Reqnroll position, project-level glossary strategy, learnings-file scope, demo-script runbook, operations runbook) becomes either an accepted ADR or a consciously parked item with a named trigger. Phase 5 (Operational adoption) follows Phase 4 and is where the new workflow becomes the default operating mode.

Phase 3's outputs feed Phase 4 in two places: the Layer 2 ubiquitous-language seeds (Item 4's §3 sections) inform any Phase 4 ADR on glossary strategy (Question 2), and the Cast/Setting hybrid artifact (Item 3) is precedent for any Phase 4 ADR on demo-script runbook authoring (Question 4). Other Phase 4 questions are independent of Phase 3's work.

## Verification checklist (from Phase 3 prompt §6)

- [x] All four workshops (W001, W002, W003, W004) carry a `Status` column or equivalent. W001 has it on every Tier table; W002 and W004 on Coverage by Component; W003 carries a clarifying paragraph in lieu of a slice table.
- [x] W002, W003, W004 each carry a §3 Ubiquitous Language section with `Term | Definition | Notes` shape. Cross-BC overlapping terms (`Listing`, `Reserve`, `Hammer Price`, `Buy It Now`) are noted via Option A (define once per BC, cross-reference).
- [x] `docs/skills/event-modeling/SKILL.md` carries an "Adjunct Patterns" section naming Klefter, Bruun temporal-automation, and Bruun configuration-as-events with CritterBids examples.
- [x] W001 carries `## Cast` and `## Setting` sections; the narrative's Cast and Setting language is preserved verbatim where it overlaps; workshop-grain extensions (role-grain Cast, policy-posture Setting) are flagged as such.
- [x] `docs/narratives/README.md` §"Bidirectional referencing" names both per-row and consolidated forms with their respective defaults; the "(forward-looking)" parenthetical is dropped.
- [x] No file under `src/` or `tests/` was edited in this session.
- [x] No new ADR, no new skill file, no new narrative.
- [x] No em dashes in any committed prose authored by this session (all swept). Pre-existing em dashes in title-block metadata, H2 Phase headings, and prose paragraphs retain the grandfather clause.
- [x] `docs/retrospectives/foundation-refresh-phase-3-retrospective.md` exists and mirrors the structure of Phases 1 and 2 retrospectives (this file).
- [x] Methodology-log entry conscious skip recorded above with rationale (continued deferral to Phase 5 backfill cohort).

## Document history

- **v0.1** (2026-04-27): Authored at Phase 3 close as the eighth commit on the `foundation-refresh/p3-conventions` branch. Mirrors the structure of Phase 1 and Phase 2 retros; references the Phase 3 session prompt and the foundation-refresh handoff §6 as authoritative scope.
