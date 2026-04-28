# Foundation Refresh Phase 4 - Open Questions Resolved by ADR

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-27 |
| **Author of record** | Erik Shafer (with prior-session AI collaborator review of Phases 1, 2, 2.5, 3 retros) |
| **Phase** | 4 of 5 |
| **Authoritative scope** | [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7 |
| **Target retro** | `docs/retrospectives/foundation-refresh-phase-4-retrospective.md` |
| **PR shape** | One PR for the whole phase, one commit per question plus the retro commit (six commits total) |
| **Branch** | `foundation-refresh/p4-adrs` |

---

## 1. Read this section first

Phase 4 is the **open-questions phase** of the foundation refresh. Each of five methodology questions surfaced during Phases 1-3 (and pre-refresh) becomes either an Accepted ADR, a short decision note, or a parked item with an explicit trigger. **Pure docs work; no code, no convention rollouts (those are Phase 3 territory), no narrative authoring (Phase 5 territory).**

The phase has **five questions** scoped in [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7. This prompt does not re-specify them. It supplies:

- The order to address them in.
- The proposed disposition for each (ADR / decision note / parked).
- The next available ADR numbers and their candidate slugs.
- What Phases 1-3 informed for each question.
- Acceptance gates per question.

Three things to keep loaded throughout:

1. **`foundation-refresh-handoff.md` §7 is authoritative** for each question's framing and options. This prompt is the execution shape, not the spec. Mirrors the Phase 3 prompt's relationship to handoff §6 (AUTHORING.md rule 3).
2. **No code work in Phase 4.** Methodology decisions only. If a question's resolution implies code work (e.g., Reqnroll adoption requires test-infra changes), the code work routes to a future implementation slice via the spec-anchored discipline; the ADR documents the decision, not the implementation.
3. **The em-dash convention from Phase 3's working pattern carries over.** Authored prose in new ADRs and decision notes uses hyphens; pre-existing em dashes in handoff §7 prose remain per the grandfather clause. Audit-after-write (regex for `U+2014`) before each commit per Phase 3 retro Key Learning 4.

---

## 2. Phase context

### 2.1 What Phases 1-3 informed for each question

| Question | Phase 1-3 input | How it shapes Phase 4 |
|---|---|---|
| Q1 Reqnroll position | Phase 2's narrative-anchored finding-lane discipline (`workshop-update` resolved 5 of 12 findings); Phase 2.5 closed Finding 011 via spec-anchored test re-anchor (workshop scenario 1.11 was the disagreement target). | The convention-based linkage between scenarios and tests already works. Reqnroll would mechanize what is currently disciplined. Question: does mechanization earn its keep, or does the existing discipline scale? |
| Q2 Glossary strategy | Phase 3 Item 4 added per-BC §3 Ubiquitous Language to W002, W003, W004 (45 terms; Option A cross-references). W001 has implicit vocabulary plus Phase 3 Item 3 Cast/Setting. | The per-BC glossary is now lived. Question is whether to add a cross-BC synthesis at `docs/vision/glossary.md`, or leave the per-BC §3 sections as the only authoritative source. Cross-references already span workshops via markdown anchors. |
| Q3 Learnings file scope | Phase 2 retro deferred Methodology Log Entry 001 to Phase 5 backfill cohort (the methodology log already exists; the question is whether a separate slice-grain learnings file earns its keep alongside session retros and the methodology log). | The retro layer + the methodology log already cover cross-cutting observations. A slice-grain learnings file (Dilger SDD style) would be a third layer; question is whether the third layer adds value. |
| Q4 Demo-script runbook | Phase 2's narrative 001 dramatizes the Flash demo journey from a single-bidder happy-path perspective. Phase 3 Item 3 added Cast/Setting to W001 (workshop hybridization). | A demo-script runbook is presenter-perspective (third lens after bidder-narrative and operator-workshop). The runbook may be a hybrid (presenter overlay on narrative 001), a separate file, or deferred until a conference talk demands it. |
| Q5 Ops runbook | None directly. Operations is currently undocumented as runbooks; deployment lives across ADRs 006 (Aspire) and 011 (All-Marten Pivot) without a unified ops doc. | The handoff anticipates parked-with-trigger ("when first production-leaning deployment is scheduled"). Phase 4 confirms the trigger and records the parking. |

### 2.2 ADR numbering plan

Per [`docs/decisions/README.md`](../../decisions/README.md), the next available ADR number is **018**. ADRs 014 (M4-S6) and 015 (conditional on M4-D4) remain reserved per the existing prose; Phase 4 does not draft into those slots.

Candidate ADR allocations (subject to per-question disposition):

| Question | Candidate ADR | Slug |
|---|---|---|
| Q1 Reqnroll position | ADR-018 | `018-reqnroll-position` |
| Q2 Glossary strategy (if ADR) | ADR-019 | `019-glossary-strategy` |
| Q3 Learnings file scope (if ADR) | ADR-020 | `020-learnings-file-scope` |

If Q2 or Q3 resolves as a short decision note rather than a full ADR, the corresponding number is unallocated and slides to the next ADR-grade question. Q4 and Q5 are not expected to consume ADR numbers (decision-note or parked dispositions per the handoff's framing).

### 2.3 Question ordering rationale

Per [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7 (no ordering specified), this prompt commits to:

1. **Q5 first** - operations runbook. Almost certainly parked-with-trigger; clears the simplest decision and warms up the phase.
2. **Q3 second** - learnings file scope. Smaller decision than Q1/Q2; default disposition is "skip" (retros + methodology log are sufficient).
3. **Q2 third** - glossary strategy. Builds on Phase 3 Item 4's lived §3 sections; the question is whether a cross-BC synthesis adds value.
4. **Q4 fourth** - demo-script runbook. Builds on Phase 3 Item 3's Cast/Setting hybrid as precedent; the question is whether to extend the hybridization to presenter perspective.
5. **Q1 last** - Reqnroll position. The heaviest decision in Phase 4; benefits from full session attention. CritterCab coordination point flagged at kickoff.

Smallest-decision-first ordering ensures the heavy Q1 decision lands with full context. Alternative ordering (Q1 first, while energy is fresh) is also defensible; flag at session start if the alternative fits better.

---

## 3. Questions

### 3.1 Q5 - Ops runbook / SRE-style docs

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7.5.

**Default disposition:** **Parked with trigger.** "When the first production-leaning deployment is scheduled." Specifically: when a CritterBids deployment is planned for any environment beyond a developer's local Aspire orchestration (e.g., Hetzner VPS, conference-demo cloud instance, public preview), the trigger fires and an Ops runbook session is opened.

**What lands in Phase 4 retro:** A "Phase 4 parked items" subsection naming Q5, the trigger condition, and the candidate runbook scope (Hetzner VPS topology, health checks, alerting, incident playbooks, deploy/rollback procedures).

**Acceptance:**
- [ ] Phase 4 retrospective contains a "Parked items with trigger" subsection.
- [ ] The trigger condition is specific and observable ("production-leaning deployment scheduled," not "someday").
- [ ] No file under `docs/ops/` or equivalent is created (the parking is recorded, not opened).

### 3.2 Q3 - Learnings file scope

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7.3.

**Default disposition:** **Decision note** (not full ADR-grade) recorded in `docs/research/methodology-log.md` or as a one-section addendum to a relevant docs README. Default option: **skip** (retros and the methodology log are sufficient).

**Default rationale:** CritterBids has session-grain retros that capture per-session learnings, and the methodology log captures cross-cutting observations. A separate slice-grain learnings file (Dilger SDD style) would be a third layer; the marginal value is unclear when the existing two layers already work. CritterBids retros under M3 already absorb slice-grain observations effectively (M3-S4b's terminal-paths retro, M3-S5b's saga-skeleton retro, Phase 2.5's regression-defense test retro all carry slice-grain learnings).

**Alternative dispositions to flag at kickoff:** if user prefers per-BC learnings files, the disposition becomes "Adopt option 2 (per-BC learnings); ADR-grade decision; allocate ADR-020." If user prefers project-wide learnings, "Adopt option 3; ADR-grade; allocate ADR-020."

**Acceptance:**
- [ ] Disposition recorded (decision note or ADR-020 created).
- [ ] If "skip": rationale recorded in `docs/research/methodology-log.md` or in the Phase 4 retrospective with explicit pointer to retros + methodology log as the canonical layers.
- [ ] No `docs/learnings.md` or `docs/skills/<bc>/learnings.md` is created unless the user picks option 2 or 3.

### 3.3 Q2 - Glossary strategy

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7.2.

**Default disposition:** **Decision note** (not full ADR-grade) recorded as a short page under `docs/vision/` or as an addendum to an existing vision doc. Default option: **option 1 (per-BC only)** - the workshop §3 sections from Phase 3 Item 4 are the authoritative source; no top-level glossary page.

**Default rationale:** Per-BC §3 sections are now lived (Phase 3 Item 4 added 45 terms across W002, W003, W004 with Option A cross-references via markdown anchors). The cross-BC navigation already works in O(1) - a reader landing on `Listing` in W002 §3 can click to W004 §3 in one hop. A separate `docs/vision/glossary.md` would duplicate content and create a maintenance ledger (does the synthesis stay in sync with the per-BC sections when they update?). CritterCab is in a similar position; the per-BC pattern is portable.

**Alternative dispositions to flag at kickoff:** if user prefers a synthesis page (option 2 or 3), the disposition becomes "Author `docs/vision/glossary.md` as a derived index pointing back at per-BC §3s." Could be ADR-grade (ADR-019) if the cross-BC overlap surface justifies the formal reasoning.

**Acceptance:**
- [ ] Disposition recorded (decision note in `docs/vision/`, ADR-019, or parked).
- [ ] If "per-BC only" (default): rationale recorded; the per-BC §3 sections are explicitly named as the canonical source; no top-level page is created.
- [ ] Cross-BC overlapping terms (Listing, Reserve, Hammer Price, Buy It Now, Bidder, Seller) are confirmed to navigate cleanly via the existing markdown anchor links.

### 3.4 Q4 - Demo-script runbook

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7.4.

**Default disposition:** **Parked with trigger.** "When the first conference talk is scheduled." Specifically: when a CritterBids demo is committed to a specific conference event (date, venue, presenter), the trigger fires and a demo-script runbook session is opened.

**Default rationale:** The Flash demo journey is dramatized in narrative 001 (single-bidder happy path) and walked at workshop grain in W001 (full Tier 0-9 storyboard). A presenter-perspective runbook adds a third lens but has no immediate consumer (no conference talk currently scheduled). Premature authoring runs the risk of bit-rotting before its first run; deferring with a clear trigger lets the runbook be authored against actual presentation constraints (talk length, audience composition, stage tooling).

**Alternative dispositions to flag at kickoff:** if user wants the runbook now (option 1 or 2), the disposition becomes "Author `docs/demo/flash-session-runbook.md` (option 2) or extend narrative 001 with a presenter overlay (option 1)." Phase 3 Item 3's Cast/Setting hybrid is precedent for option 1 (workshop hybridization). Option 2 may be cleaner since runbooks have stage-direction grain that does not fit narrative voice.

**Acceptance:**
- [ ] Disposition recorded (decision note, runbook authored, or parked with trigger).
- [ ] If parked (default): trigger condition recorded in Phase 4 retrospective alongside Q5's trigger.
- [ ] If authored: the runbook lives under `docs/demo/` (new directory) or as a sibling to narrative 001 under `docs/narratives/`; the retrospective records the structural choice.

### 3.5 Q1 - Reqnroll position

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7.1.

**Default disposition:** **ADR-018** with default option 4 ("Decline executable specs at MVP; revisit when scenario-test drift becomes load-bearing").

**Default rationale:** The narrative + skill + retrospective discipline is already carrying scenario-to-test linkage by convention. Phase 2.5's Finding 011 fix is the proof: workshop scenario 1.11 was wrong; the test passed against the wrong assertion; the narrative-vs-code audit caught it; the fix re-anchored the test to the workshop. The discipline works without mechanization. Reqnroll adoption would add tooling overhead (`.feature` file maintenance, generator config, IDE integration) without clear MVP value. The bar for revisiting: scenario-test drift becomes load-bearing (e.g., three or more `workshop-update` findings in a single narrative session that would have been caught earlier by mechanical generation).

**CritterCab coordination point:** Per the handoff, CritterCab is in the same place on this question; the ADR could be co-published or coordinated. Flag at kickoff: should Phase 4's ADR-018 reference CritterCab's matching decision (if authored), or stand alone? If CritterCab has not authored its matching ADR yet, CritterBids' ADR-018 lands as the prior art and CritterCab's ADR points back. Coordination is one-way until both projects have published matching positions.

**Alternative dispositions to flag at kickoff:** if user wants to adopt Reqnroll (option 1) or parallel-source (option 3), the disposition becomes "Adopt; ADR-018 captures the choice and Phase 5 or beyond carries the implementation work." Adoption likely also requires a new skill file (`docs/skills/reqnroll-executable-specs.md`) and a Wolverine/test-infra integration ADR; surface those as cascading work in the ADR's Consequences section.

**Acceptance:**
- [ ] ADR-018 created at `docs/decisions/018-reqnroll-position.md`.
- [ ] ADR follows the project's existing ADR template (status, context, decision, consequences, alternatives).
- [ ] If "Decline" (default): the ADR records the trigger for revisit ("when scenario-test drift becomes load-bearing"); names the existing discipline (Phase 2 finding-lane workflow) as the alternative that earns its keep.
- [ ] If "Adopt": the ADR records the cascading work (skill file, test-infra ADR) as Consequences; the cascading work routes via spec-anchored discipline to future implementation slices.
- [ ] `docs/decisions/README.md` Status Ledger updated with ADR-018 row.
- [ ] CritterCab coordination point recorded in the ADR's Consequences section (one sentence either way).

---

## 4. Working pattern

### 4.1 Cadence

Per-question interactive sign-off. Mirror the Phase 3 working pattern: propose the question's disposition, confirm with user, author the ADR or decision note, commit, move on.

### 4.2 ADR template

Use the project's existing ADR template (visible in any of `docs/decisions/001` through `017`). Standard sections:

- Title, Status, Date
- Context (what's true; why this question is being asked now)
- Decision (the choice; one paragraph)
- Consequences (what changes; cascading work; trigger for revisit)
- Alternatives considered (the other options from handoff §7.x with one-paragraph rationale for rejection)

### 4.3 Decision note shape

For Q2 or Q3 if "skip" or "decision note" is chosen: a short section in an existing docs file (e.g., `docs/vision/glossary-strategy.md` as a one-page decision note, or an addendum to `docs/research/methodology-log.md` for Q3). No ADR number; no formal acceptance gate; the rationale lives in prose with explicit pointers to the canonical sources that earn their keep.

### 4.4 Parked-with-trigger shape

For Q4 or Q5: recorded in the Phase 4 retrospective's "Parked items with trigger" subsection. Each parked item has:

- The question number and brief framing
- The trigger condition (specific and observable)
- The candidate scope of the future session
- Pointer back to handoff §7.x as the source spec

No file is created; the parking is the artifact.

### 4.5 Em-dash audit

Apply Phase 3's audit-after-write discipline. After each new ADR or decision note lands, run a regex audit for the em-dash codepoint (`U+2014`) before staging. Pre-existing em dashes in surrounding files retain the grandfather clause; new content is em-dash-clean.

---

## 5. Commit sequence (proposed)

1. `docs(decisions): record Q5 ops runbook parking with trigger (Phase 4 Q5)` - small commit; the parking is recorded in a stub file or in the eventual retro. May fold into commit 6 instead of standing alone; flag at kickoff.
2. `docs: record Q3 learnings file scope decision (Phase 4 Q3)` - decision note or ADR-020; default "skip" with rationale.
3. `docs: record Q2 glossary strategy decision (Phase 4 Q2)` - decision note or ADR-019; default "per-BC only" with rationale.
4. `docs: record Q4 demo runbook parking with trigger (Phase 4 Q4)` - small commit; default parked-with-trigger.
5. `docs(decisions): add ADR-018 Reqnroll position (Phase 4 Q1)` - the heavy commit; default "decline at MVP."
6. `docs: write Phase 4 retrospective` - the session-close retro.

If Q5 and Q4 both park without files, their commits can fold into the retro commit (commit 6); flag at kickoff for a leaner four-commit sequence.

---

## 6. Acceptance criteria (Phase 4 close gate)

- [ ] Q5 (ops runbook) has a recorded disposition: parked-with-trigger by default, or a decision note pointing at the trigger condition.
- [ ] Q3 (learnings file scope) has a recorded disposition: skip (default), or an authored decision note, or ADR-020.
- [ ] Q2 (glossary strategy) has a recorded disposition: per-BC only (default), or an authored synthesis, or ADR-019.
- [ ] Q4 (demo-script runbook) has a recorded disposition: parked-with-trigger (default), or an authored runbook, or ADR.
- [ ] Q1 (Reqnroll position) lands as ADR-018 with a clear decision and consequences.
- [ ] `docs/decisions/README.md` Status Ledger reflects any new ADRs (018, 019 if used, 020 if used).
- [ ] No file under `src/` or `tests/` was edited in this session.
- [ ] No new narrative, no new workshop, no new convention rollout.
- [ ] No em dashes in any committed prose authored by this session (Phase 3 §4.3 audit-after-write discipline).
- [ ] `docs/retrospectives/foundation-refresh-phase-4-retrospective.md` exists and mirrors the structure of Phases 1, 2, 3 retrospectives.
- [ ] Phase 4 retrospective contains:
  - A "Parked items with trigger" subsection naming Q4 and Q5 (or whichever resolved as parked).
  - A "What's the next non-methodology slice" answer per handoff §7.6.
  - A methodology-log time-box review per handoff §7.6 ("Is the methodology log carrying its weight?").

---

## 7. Explicitly out of scope

- **Reqnroll implementation work.** If Q1 lands as "Adopt," the implementation (skill file, test-infra changes, scenario porting) is out of Phase 4 scope. The ADR documents the decision; cascading implementation is future work routed via spec-anchored discipline.
- **`docs/vision/glossary.md` aggregation logic.** If Q2 lands as a synthesis, the actual aggregation is a future docs session; Phase 4 only authors the decision note or ADR.
- **Operations runbook content.** Q5 is parked; Phase 4 does not draft Hetzner VPS topology, alerting playbooks, or deploy/rollback procedures.
- **Demo-script runbook content.** Q4's default is parked; Phase 4 does not draft presenter overlays or stage directions.
- **Cross-BC vocabulary edits.** Phase 3 Item 4 already added the per-BC §3 sections; Phase 4 does not edit them. If Q2 surfaces a cross-BC overlap that the §3 sections handle poorly, flag in the retro for a Phase 5 follow-up.
- **New skill files.** No skill files are authored in Phase 4; any cascading skill-file work routes to a future session.
- **Phase 5 backfill narratives.** Phase 5 authors them; Phase 4 only records what Phase 5 will inherit.
- **Code refactoring of any kind.** No code work in Phase 4.

---

## 8. Open questions to flag at kickoff (not decide)

- **Question ordering override.** Default is Q5 → Q3 → Q2 → Q4 → Q1 (smallest decision first). Alternative is Q1 first while energy is fresh. User decides at session start.
- **Q1 CritterCab coordination shape.** Co-published (both projects' ADRs reference each other), one-way (CritterBids' ADR is prior art for CritterCab), or independent. User decides at Q1 kickoff.
- **Q2/Q3 ADR vs decision note grain.** Both questions can land as ADRs (formal numbered records) or short decision notes (lighter ceremony). User decides at each question's kickoff based on whether the decision is "hard to reverse" / "cross-cutting" / "likely to surprise" per `docs/decisions/README.md` §"When to Write an ADR."
- **Q5/Q4 parking shape.** Default is "recorded in Phase 4 retrospective" (no separate file). Alternative is a stub `docs/parked-decisions.md` or similar. User decides at session start.
- **Methodology-log time-box review.** Per handoff §7.6: "Is the methodology log carrying its weight? Apply the time-box review." The methodology log currently has zero entries (Phase 2 deferred Entry 001 to Phase 5 backfill cohort; Phase 3 also skipped). The time-box from `docs/research/methodology-log.md` says "Decision to keep, fold, or remove the file is revisited at the close of Phase 2 or after the third entry, whichever comes first." Phase 2 closed without an entry; Phase 3 closed without an entry; Phase 4 is past the original Phase-2-close trigger. User decides at retro authoring whether to keep, fold, or remove the methodology log.

---

## 9. Starting move

When Phase 4 begins:

1. Read this document fully.
2. Re-read [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7 (the authoritative scope).
3. Skim the closing summaries of [`foundation-refresh-phase-1-retrospective.md`](../../retrospectives/foundation-refresh-phase-1-retrospective.md), [`foundation-refresh-phase-2-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-retrospective.md), [`foundation-refresh-phase-2-5-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-5-retrospective.md), and [`foundation-refresh-phase-3-retrospective.md`](../../retrospectives/foundation-refresh-phase-3-retrospective.md) for cross-phase context (especially Phase 3 retro Key Learning 1 on the deviation-to-convention pipeline).
4. Confirm with the user:
   - Question ordering (default: Q5 → Q3 → Q2 → Q4 → Q1).
   - Q1 CritterCab coordination shape (default: independent; CritterCab can backreference if/when it authors its own).
   - Q2/Q3 ADR vs decision note grain (default: decision note for both with "skip / per-BC only" defaults).
   - Q5/Q4 parking shape (default: recorded in retrospective).
   - Methodology-log time-box review disposition (default: continued deferral pending Phase 5 backfill data points).
5. Begin Q5. Do not start Q3 until Q5's disposition is recorded.

---

## 10. Memory and preferences inheritance

These preferences (from CritterBids' lived sessions and the foundation-refresh handoff §13) apply:

- Depth over brevity when explaining trade-offs.
- Auction-domain ubiquitous language (Listing, Bidder, Seller, Auctioneer, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction).
- DDD / CQRS / Event Sourcing / EDA assumed background.
- Lean opinions on questions - propose a default with rationale rather than open-ended elicitation.
- No em dashes in any committed prose. Hyphens (regular `-`) and en dashes are fine; em dashes are out.
- Punchy prose; no AI-tool references in committed text.

---

## Document history

- **v0.1** (2026-04-27): Authored at Phase 3 close as the Phase 4 session prompt. References [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §7 as authoritative for scope; supplies execution shape (question ordering, default dispositions, ADR numbering plan, current-state observations) per AUTHORING.md rule 3. Mirrors the Phase 3 prompt's structure.
