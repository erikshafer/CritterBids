# Foundation Refresh Phase 5 — Retrospective

**Date:** 2026-04-29
**Phase:** 5 of 5 (final)
**Authoritative scope:** [`foundation-refresh-handoff.md`](../prompts/foundation/foundation-refresh-handoff.md) §15 + [`foundation-refresh-phase-5.md`](../prompts/foundation/foundation-refresh-phase-5.md)
**Closing artifact:** this retrospective

---

## What landed

Phase 5 closes the foundation refresh by committing the operational layer to the methodology infrastructure Phases 1-4 built. Six PRs across the phase:

| PR | Item | Status |
|---|---|---|
| #18 | Items 2+3 — AUTHORING.md + retrospectives README amendments | Merged 2026-04-28 |
| #20 | Item 1a — Narrative 002 (Settlement BC backfill) + session prompt | Merged 2026-04-29 |
| #21 | Item 1b — Narrative 003 (Participants BC backfill) + session prompt | Merged 2026-04-29 |
| #22 | Item 1c — Narrative 004 (Selling BC backfill) + session prompt | Merged 2026-04-29 |
| #23 | Item 1d — Narrative 005 (Auctions BC backfill) + session prompt | Merged 2026-04-29 |
| (this PR) | Item 4 — M5 milestone doc + M5-S1 cutover prompt + Phase 5 retro | This PR |

Concrete deliverables:

- **AUTHORING.md rule 3** grew the joint-authority clause for milestone doc plus narrative.
- **Implementation prompt template** gained a `**Narrative:**` line in its metadata block.
- **Retrospectives README** gained a "Findings against narrative" section to the retro template.
- **Five-narrative library** complete: narratives 001 (bidder spine), 002 (settlement), 003 (Participants), 004 (Selling), 005 (Auctions). All `status: accepted`. The library covers all four lived BCs plus Settlement (forward-spec) plus the cross-BC integration boundaries.
- **Per-narrative prompts** authored at `docs/prompts/narratives/00X-<slug>.md` for each backfill narrative.
- **Per-narrative findings files** (or conscious-skip notes) for each backfill: `002-findings.md`, `003-findings.md`, `004-findings.md`; narrative 005's findings file is consciously skipped per its retro since zero new findings surfaced.
- **Two stub follow-up implementation prompts** authored: `n003-fu-get-participant-endpoint.md` (F002 from narrative 003) and `n004-fu-submit-listing-endpoint.md` (F002 from narrative 004). Both queued for future product work; not run in Phase 5.
- **Two in-PR code fixes** landed: narrative 003 F001 (lived comment misclaim correction in `StartParticipantSession.cs`) and a path-citation correction in narrative 004's prompt at session-time. No `src/` or `tests/` substantive code changes beyond these.
- **Methodology log Entry 001** written at narrative 005 close: audit-floor heterogeneity is the structurally expected mode for narrative authoring.
- **W003 minimum-scope storage-staleness correction** (narrative 002 F003) folded into PR #20.
- **Narrative 001 Moment 8 saga-event payload corrections** (narrative 002 F001) folded into PR #20.
- **W003 follow-up amendments** (F002, F004, F005 from narrative 002) folded into M5-S1's prompt scope rather than a separate workshop-cleanup PR.
- **M5 milestone doc** at `docs/milestones/M5-settlement-bc.md`: full Settlement BC scope per the M3 / M4 milestone-doc structural precedent.
- **M5-S1 prompt** at `docs/prompts/implementations/M5-S1-settlement-bc-foundation-decisions.md` carrying the `**Narrative:**` line citing narrative 002 — the cutover gate's visible signal.
- **W001, W002, W003, W004** all carry narrative cross-references: W001 consolidated form (extended through narrative 005); W002, W003, W004 each new sections authored at their respective backfill-narrative PR.

---

## Backfill narrative summary

The four backfill narratives (Items 1a-1d) plus narrative 001 constitute CritterBids' five-narrative library.

| Narrative | BC | Posture | Moments | Findings (n-update / w-update / c-update / d-as-intentional) |
|---|---|---|---|---|
| 001 (bidder spine) | cross-BC | mixed (5 lived, 3 forward-spec) | 8 | 2 / 5 / 1 / 4 = **12 total** |
| 002 (settlement) | Settlement | fully forward-spec | 5 | 1 / 3 / 0 / 1 = **5 total** |
| 003 (participants) | Participants | fully lived | 3 | 0 / 0 / 2 / 0 = **2 total** |
| 004 (selling) | Selling | mixed (4 lived, 1 forward-spec M4-S2) | 5 | 0 / 0 / 1 / 2 = **3 total** |
| 005 (auctions) | Auctions | mixed (1 forward-spec M4-S5/S6, 3 lived) | 4 | 0 / 0 / 0 / 0 = **0 total** |

Cumulative: **22 findings** filed across the library, **3 narrative-update + 8 workshop-update + 4 code-update + 7 document-as-intentional**. The lane mix tracks the audit-floor posture exactly per Methodology Log Entry 001's prediction: forward-spec narratives produce workshop-grade findings; lived narratives produce code-update plus document-as-intentional; cross-BC and seller / observer narratives produce narrative-update against earlier narratives' bidder-side coverage.

Cross-narrative observations:

- **Five perspectives stack on the keyboard's auction journey.** Narrative 001 dramatises the bidder side (SwiftFerret42's QR scan through her settlement charge); narrative 002 dramatises Settlement after the gavel; narrative 003 dramatises BoldPenguin7's session-start (her competitor-perspective on the same Flash session); narrative 004 dramatises GreyOwl12's listing-publication (the keyboard plus a sibling Vintage Folding Camera); narrative 005 dramatises GreyOwl12's seller-perspective on the auction itself. Five perspectives, one keyboard. The cross-narrative consistency audit (narrative 005 against narrative 001 Moments 4-7) confirmed the project's narrative library is internally coherent — same bidders, same dollar amounts, same sequence rendered consistently.
- **Anchored cross-narrative values compound.** Narrative 001 anchored the keyboard's listing-time fields. Narrative 003 anchored BoldPenguin7's `BidderId` ("Bidder 4523") and credit ceiling ($700). Narrative 004 anchored the Vintage Folding Camera's listing-time fields. Future narrative authoring inherits these anchors verbatim.
- **Two stub follow-up prompts queued** for future product work: GET `/api/participants/{id}` endpoint (narrative 003 F002) and POST `/api/listings/{id}/submit` endpoint (narrative 004 F002). Both have the same structural shape — missing HTTP entry point on a Wolverine handler — and both are M6 frontend MVP territory. The pattern suggests future seller / bidder-dashboard scoping will surface analogous gaps that prior backfill narratives have already named.
- **Forward-spec posture surfaces workshop staleness reliably.** Narrative 002's W003 audit surfaced F003 (Polecat / SQL Server framing predating ADR 011); narrative 004's W004 audit confirmed W004 was clean; narrative 005's W002 audit confirmed W002 was clean. The mix is W003 stale, W002 / W004 clean — meaning W003's pre-ADR-011 authoring carried storage staleness while W002 / W004 were already aligned. Pattern prediction: workshops authored before architectural pivots accumulate staleness against those pivots; forward-spec narratives are the catch.

---

## Cutover gate

The foundation refresh's cutover gate per handoff §15.5 / Phase 5 prompt §3.6: "M5's first slice prompt cites a narrative as jointly authoritative scope alongside its milestone doc." The signal lands at:

```
docs/prompts/implementations/M5-S1-settlement-bc-foundation-decisions.md
```

with the metadata block carrying:

```markdown
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
```

per AUTHORING.md rule 3's joint-authority clause (added in PR #18 as Phase 5 Items 2+3 amendments).

The cutover is not a soft transition. M5-S1 is the first slice prompt under the new NDD-informed regime; subsequent M5 slice prompts (M5-S2 through M5-S6) inherit the discipline and carry their own `**Narrative:**` lines. CritterBids has switched from workshop-anchored implementation to NDD-informed narrative-anchored implementation as its operational default.

The cutover prompt itself is M5-S1's prompt, not its execution. **Running M5-S1** (closing ADR-019, folding W003 amendments F002 / F004 / F005, authoring three contract stubs) is M5 work — the next non-methodology slice — and is out of scope for Phase 5.

---

## Methodology log disposition

The methodology log (`docs/research/methodology-log.md`) was a time-boxed pilot per its v0.1 framing: "Decision to keep, fold, or remove the file is revisited at the close of foundation-refresh Phase 2 or after the third entry lands, whichever comes first." Phase 4's retrospective updated the time-box to "after Phase 5 closes, or after the methodology log carries three entries, whichever comes first."

**Disposition:** retain the file as an ongoing primitive.

Rationale: Entry 001 was written at narrative 005's session close (PR #23) and captures a load-bearing methodology observation (audit-floor heterogeneity is the structurally expected mode for narrative authoring). The single-entry threshold did not trigger any "delete or fold" condition; the file is operating as designed. The Phase 5 close closes the time-box; the file is retained as a permanent fixture in `docs/research/`. Future entries, if they meet the entry-criteria gate, land below Entry 001. No proactive deletion or fold.

If a future entry materially refines or contradicts Entry 001's audit-floor-heterogeneity prediction, the file's append-only convention applies: a new entry records the refinement; Entry 001 stays unchanged.

---

## Items folded in beyond the original scope

Phase 5's planning per handoff §15 + Phase 5 prompt scoped four items: backfill narratives, AUTHORING.md amendments, retrospectives README amendments, cutover gate. The phase folded in seven additional items as the work progressed:

1. **Em-dash hygiene scope clarification** (mid-narrative-002 close, 2026-04-29). The no-em-dashes convention from CLAUDE.md / handoff §13 was being misapplied to internal docs; user clarified that the rule was intended for external-facing prose only. Memory updated (`feedback_em_dash_scope.md`); narratives 003-005 authored without em-dash audit step. Methodology-grade impact captured in narrative 003's retro and inherited through 004-005.
2. **Sibling-listing pattern** (narrative 004 invention). When a narrative's protagonist's primary subject already has a fixed terminal outcome from an earlier narrative (the keyboard's narrative-001 sale), introduce a sibling listing for any Moment that needs a different outcome (the Vintage Folding Camera for `WithdrawListing`). Reusable pattern for future narratives.
3. **Code-comment-as-routing-evidence discipline** (narrative 004 lesson; narrative 005 confirmation). Inline code comments that explicitly document a design choice flip findings routing from `code-update` to `document-as-intentional`. Narrative 003 surfaced this implicitly via F003's missing intermediate state; narrative 004 made it explicit; narrative 005 used it to keep F011 / F012 from narrative 001 from re-surfacing.
4. **Path-citation pre-check at prompt-author time** (narrative 004 lesson; narrative 005 confirmation clean). Before committing a session prompt that cites file paths, verify each path with `Glob` or `find`. Narrative 004's prompt cited `docs/prompts/M4-S2-...` instead of the actual `docs/prompts/implementations/M4-S2-...`; caught at narrative-004 Moment 5 by reading the M4-S2 prompt; bundled into the Moment 5 commit as a small fix.
5. **Pre-Moment surrounding-directory reads** (narrative 003 lesson, refined through 004 and 005). Reading just the implementing handler isn't enough; reading the surrounding `Features/` or BC directory for any `[WolverinePost]` / `[WolverineGet]` registrations catches missing-endpoint gaps the handler-only read misses. Narrative 003 F002 and narrative 004 F002 both landed via this discipline.
6. **Observer-protagonist Voice** (narrative 005 invention). Complementary to active-protagonist (narratives 001 / 003 / 004). The narrator's responsibility-split (protagonist's window ↔ saga-internal dramatisation) is the defining technique for any narrative whose protagonist's role is structurally passive (watching, monitoring, awaiting outcome).
7. **W003 amendments folded into M5-S1** instead of a separate workshop-cleanup PR. Narrative 002 deferred F002 / F004 / F005 to a coordinated W003 follow-up PR. M5 is the natural home for these amendments since the workflow-hosting decision (M5-S1's primary deliverable) sits adjacent to the Price/HammerPrice rename documentation, the SettlementInitiated payload reconciliation, and the bidder-credit projection definition. The previously-deferred follow-up PR is no longer needed; this saved one PR + one session and kept the W003 amendments in the same review surface as the implementation decisions they inform.

---

## Open questions surfaced during Phase 5

Phase 5's open-question surface narrowed as the phase progressed. The remaining questions at phase close all sit in M5-S1's "Open questions" section, since they're slice-level decisions M5-S1 will close:

1. **Saga vs `ProcessManager<TState>` workflow hosting** (M5-S1 will close via ADR-019). Erik (JasperFx core team) is actively designing `ProcessManager<TState>`; choosing it makes CritterBids the first lived example.
2. **F005 projection name** (M5-S1 will lock; lean `BidderCreditView` or `BidderCreditLedger`).
3. **F002 placement in W003** (M5-S1 will decide; lean Phase 1 Part 2 alongside the hosting comparison).
4. **F004 SettlementId rendering uniformity choice** (M5-S1 will decide; lean include SettlementId on all four §3 / §4 / §5 / §6 events for consistency).

Phase-5-grade open questions (i.e., questions that should have surfaced during the Phase 5 work itself rather than deferred to product slices) — none surfaced. The phase ran cleanly against its planned scope; the seven folded-in items above were positive expansions, not replanning.

---

## Key learnings

1. **The four-backfill-narrative wave can be authored within a single calendar day** when the working pattern stabilizes after the first narrative. Narrative 002 (Settlement, forward-spec) was the calibration session; narratives 003-005 inherited the discipline and ran progressively faster (003 on 2026-04-29 same-day; 004 same-day; 005 same-day). The cumulative cadence was carried by AUTHORING.md rule 3's joint-authority discipline plus the narratives README v0.1's bounded-frontmatter / prose-paragraph-Moments / disposition-tag conventions — none of which needed adjustment during the wave.

2. **Forward-spec narratives are not weaker than lived narratives; they are differently positioned audit surfaces.** Narrative 002's forward-spec posture surfaced more workshop-staleness findings than any of the lived narratives (3 `workshop-update` + 1 `document-as-intentional` against W003 + 1 `narrative-update` against narrative 001). Lived narratives surface code-update findings; forward-spec narratives surface workshop drift. Both are necessary for a project that has work both shipped and planned.

3. **The "no findings" outcome is meaningful, not silent.** Narrative 005 produced zero new findings — a quiet outcome that confirmed the project's prior findings discipline (F011's Phase 2.5 fix; F012's `document-as-intentional` routing) had reached terminal states correctly. A library of accepted narratives plus prior-fix verification is what produces zero-findings outcomes; the absence of findings is itself a positive result at the wave's close.

4. **Cumulative pattern refinement compounds.** Each narrative's retro generalized for the next: narrative 002 established the Phase-2 narrative-discipline; narrative 003 refined the per-Moment disposition-tag-at-draft-time discipline; narrative 004 added sibling-listing, code-comment-as-routing-evidence, and path-citation-pre-check; narrative 005 added observer-protagonist Voice. The seven Phase-5-folded-in items were not invented in Phase 5's planning; they emerged through the wave and inherited cleanly. The pattern predicts that future narrative-authoring sessions will continue inheriting + refining.

5. **AUTHORING.md rule 3's joint-authority clause is structurally load-bearing.** Once added in PR #18, every subsequent Phase 5 deliverable's `Narrative:` line traced authority to a specific narrative. The cutover gate's visible signal at M5-S1 is the cumulative endpoint of this chain. Without the rule 3 amendment, M5-S1 could carry a `Narrative:` line by convention but not by contract; the amendment makes the citation load-bearing for any future slice.

6. **W003 amendments folded into M5-S1 saved a PR** and kept the workshop-edit work adjacent to the implementation-decision work it informs. The original Phase 5 plan envisioned three deferred findings going to a separate W003 follow-up PR; folding them into M5-S1 is a structurally-better fit because the F002 rename documentation, F004 payload reconciliation, and F005 projection definition are all foundation-decision territory. Future workshop-cleanup-vs-implementation-foundation tradeoffs should default to "fold into the foundation slice" when the workshop edits are foundation-decision-adjacent.

7. **Em-dash hygiene scope clarification at mid-phase was a methodology-grade event** that required immediate memory update and propagation across the remaining narrative authoring. The clarification arrived during narrative 002's closing arc; by narrative 003 the audit step was dropped. This is the only mid-phase methodology shift that wasn't planned; future phases should anticipate that convention questions can surface mid-work and need fast resolution + propagation.

---

## Decisions about how Phase 5 ran (meta-decisions worth carrying forward)

- **Folding prompt + narrative session into one PR** for narratives 002-005 (vs the strict separate-PR-per-prompt-then-session shape AUTHORING.md rule 1 envisioned) was the right call. Per-commit cadence inside the branch preserved the rule-1 spirit (one session prompt → one reviewable artifact set) while keeping the PR boundary at the natural deliverable boundary.
- **Item ordering (1a Settlement → 1b Participants → 1c Selling → 1d Auctions)** held cleanly. Settlement first set the forward-spec discipline; Participants second exercised the lived-code discipline at smallest surface; Selling third extended to mixed-posture; Auctions fourth absorbed the largest lived surface. Each session inherited the prior session's patterns. The order was structurally right.
- **Per-Moment sign-off discipline** held throughout despite session-length variance (narratives ranged from 3 Moments to 8 Moments). The discipline scaled.
- **Methodology log Entry 001 timing** (written at narrative 005 close, the final lived chance) was correct. Earlier lived chances (narratives 003, 004) deferred per Phase-4-retro time-box-extension; the final chance had cumulative observation across all four narratives to support the entry. Earlier writing would have been premature.
- **Cutover-PR fold** (M5 milestone doc + M5-S1 prompt + Phase 5 retro all in this PR) keeps the foundation refresh's closure visible at one merge boundary. Splitting the retro into a separate closing PR would have added a PR without adding clarity.

---

## What's next

The foundation refresh closes here. CritterBids has switched from workshop-anchored implementation to NDD-informed narrative-anchored implementation as its operational default per the cutover-gate definition.

**The next non-methodology slice is M5-S1 execution.** Running the prompt at `docs/prompts/implementations/M5-S1-settlement-bc-foundation-decisions.md` against the milestone doc + narrative 002 jointly-authoritative scope. M5-S1 closes ADR-019 (workflow hosting), folds W003 amendments F002 / F004 / F005, authors three Settlement integration contract stubs. M5-S2 through M5-S6 follow per the milestone doc's slice breakdown.

Future narrative-authoring sessions inherit the Phase 5 discipline:

- Mixed-posture default (lived + forward-spec by Moment).
- Per-Moment surrounding-directory reads.
- Code-comment-as-routing-evidence routing discipline.
- Path-citation pre-check at prompt-author time.
- Em-dash hygiene scope: external-prose-only.
- Sibling-listing pattern for counterfactual outcomes when prior-narrative ground is fixed.
- Observer-protagonist Voice option for passive-role protagonists.
- Methodology log primitive for cross-cutting observations that don't fit any single retro.

The five-narrative library plus the M5-S1 prompt's joint-authority citation make CritterBids the first project in the Critter Stack family to operationalise NDD-informed development end-to-end against lived code. CritterCab (the source project for the methodology lifts per handoff §1.1) has the framework; CritterBids has the framework plus the operational adoption.

Two stub follow-up implementation prompts remain queued from Phase 5 (`n003-fu-get-participant-endpoint.md`, `n004-fu-submit-listing-endpoint.md`); both are M2 / M1 follow-ups, both are M6 frontend MVP territory in motivation, both are independent of M5. They run as standard product work whenever scheduled.

The W003 broader-sweep deferral (narrative 002 F003's untouched references at L29 / L649 / L663) remains queued; M5-S1 does not touch it. A future workshop-cleanup session sweeps it.

ADRs 016, 017, 018 (Phases 1 and 4 outputs) remain `accepted`. ADR-019 (Settlement workflow hosting) lands in M5-S1.

The methodology log carries Entry 001. Future entries land if and when the entry-criteria gate triggers.

---

## Verification checklist (from Phase 5 prompt §6)

- [x] AUTHORING.md rule 3 carries the joint-authority clause for milestone doc plus narrative (PR #18).
- [x] Implementation prompt template carries a `Narrative:` line in the metadata block (PR #18).
- [x] Retrospectives README carries a "Findings against narrative" section in the retro template (PR #18).
- [x] Four backfill narratives exist at `docs/narratives/002-<slug>.md` through `005-<slug>.md` with `status: accepted` (PRs #20, #21, #22, #23).
- [x] Each backfill narrative has a corresponding findings file or conscious-skip note in the narrative's retro (002, 003, 004 have findings files; 005 has a conscious-skip note in its retro).
- [x] W001, W002, W003, W004 carry narrative back-references for the slices each backfill narrative implements (W001 consolidated form extended through narrative 005; W002 / W003 / W004 each got new sections).
- [x] `docs/narratives/README.md` Index table lists all five narratives.
- [x] `code-update` findings have stub follow-up prompts at `docs/prompts/implementations/<slug>.md` per Phase 2.5 discipline (n003-fu-get-participant-endpoint.md, n004-fu-submit-listing-endpoint.md). These are not run in Phase 5.
- [x] M5 milestone doc exists at `docs/milestones/M5-settlement-bc.md` (this PR).
- [x] M5-S1 prompt exists at `docs/prompts/implementations/M5-S1-settlement-bc-foundation-decisions.md` and cites `docs/narratives/002-winner-clears-settlement.md` as jointly authoritative scope alongside the M5 milestone doc (this PR).
- [x] No file under `src/` or `tests/` edited in Phase 5 beyond the narrative-003 F001 lived-comment fix.
- [x] No new ADR (ADR-019 lands in M5-S1, not in Phase 5).
- [x] No new skill file authored (M5-S3 amendments to `marten-projections.md` are flagged for that slice's retro).
- [x] No new convention rollout (rollouts were Phase 3 territory).
- [x] `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` exists and mirrors the Phase 1 / 2 / 2.5 / 3 / 4 retro structure (this file).
- [x] Phase 5 retrospective contains:
  - [x] A "Backfill narrative summary" subsection enumerating the four backfill narratives, findings counts per lane, cross-narrative observations.
  - [x] A "Cutover gate" subsection confirming M5-S1's prompt-citation of narrative 002.
  - [x] A "Methodology log disposition" subsection per the time-box review.
  - [x] A "What's next" subsection naming M5-S1 execution as the next product slice (running the prompt, not authoring it).

---

## Document History

- **v0.1** (2026-04-29): Authored as foundation-refresh Phase 5 closing artifact. Closes the foundation refresh by recording: six PRs across five Phase 5 items (Items 2+3 amendments, four backfill narrative deliverables, this cutover-gate PR); five-narrative library complete with 22 cumulative findings filed; M5-S1's `**Narrative:**` citation as the cutover gate's visible signal; methodology log Entry 001 written at narrative 005 close; seven items folded in beyond the original Phase 5 scope (em-dash scope clarification, sibling-listing pattern, code-comment-as-routing-evidence, path-citation pre-check, pre-Moment surrounding-directory reads, observer-protagonist Voice, W003 amendments folded into M5-S1). Methodology log retained as ongoing primitive; time-box closed. The next non-methodology slice is M5-S1 execution.
