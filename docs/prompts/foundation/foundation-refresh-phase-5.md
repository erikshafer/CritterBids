# Foundation Refresh Phase 5 - Operational Adoption

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-28 |
| **Author of record** | Erik Shafer (with prior-session AI collaborator review of Phases 1, 2, 2.5, 3, 4 retros) |
| **Phase** | 5 of 5 (final) |
| **Authoritative scope** | [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15 |
| **Target retro** | `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` |
| **PR shape** | Multi-PR (~6 PRs across multiple sessions; see §5 Commit and PR sequence) |
| **Branch prefix** | `foundation-refresh/p5-*` (one branch per PR) |

---

## 1. Read this section first

Phase 5 is the **operational adoption phase** of the foundation refresh. It is the final phase. Phases 1 through 4 produced methodology infrastructure (ADRs 016-018, narratives directory, rules directory, methodology log, prompts subdivision, narrative 001, convention rollouts on W001-W004, ADR-018 Reqnroll position, parked items P-001 and P-002, decision notes for glossary and learnings). Phase 5 commits the operational layer to using that infrastructure. **Mostly docs work; one queued narrative-authoring session per backfill BC; the cutover gate is the visible signal of foundation-refresh closure.**

The phase has **four items** scoped in [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15. This prompt does not re-specify them. It supplies:

- The order to address them in.
- The PR shape (multi-PR; this is a structurally different phase than 1, 3, 4 which each landed as one PR).
- What Phases 1-4 informed for each item.
- Per-item acceptance gates.
- The cutover-gate definition and the M5 prerequisites it implies.

Three things to keep loaded throughout:

1. **`foundation-refresh-handoff.md` §15 is authoritative** for each item's framing and intent. This prompt is the execution shape, not the spec. Mirrors the Phases 3 and 4 prompts' relationship to handoff §6 and §7 (AUTHORING.md rule 3).
2. **Phase 5 is multi-PR, not one PR.** Item 1 alone is four narrative-authoring sessions, each of which mirrors Phase 2's PR shape (single narrative + findings file + narrative-internal retro). Treating Phase 5 as one PR would violate AUTHORING.md rule 1. Each session's per-narrative prompt lives under `docs/prompts/narratives/00X-<slug>.md`.
3. **Phase 4's outputs subtract scope from Phase 5's amendments.** ADR-018 (decline Reqnroll) means the AUTHORING.md amendment does not allocate space for executable-spec mentions. Q2's per-BC-only glossary disposition means the amendments do not introduce a project-level glossary section. Q3's skip learnings disposition means the retrospectives README amendment does not add a learnings-file pointer. See Phase 4 retrospective §"Phase 5 readiness" for the source list.

The em-dash convention from §13 of the handoff applies. Hyphens for all new prose; audit-after-write per file before each commit per Phase 3 retro Key Learning 4.

---

## 2. Phase context

### 2.1 What Phases 1-4 produced that Phase 5 operationalizes

| Source | Convention to operationalize | Phase 5 item |
|---|---|---|
| Phase 1 Item 1 (ADR 016 Spec-Anchored Development) | Narratives are jointly authoritative with workshop slices; drift is caught at retrospective time | Item 2 (AUTHORING.md rule 3 grows); Item 3 (retro template "Findings against narrative") |
| Phase 1 Item 2 (ADR 017 Design-Phase Workflow) | Staged sequence including the Narratives step; per-BC opt-in for Steps 1-2 going forward | Item 4 (cutover signals adoption of the staged sequence for M5) |
| Phase 1 Item 3 (`docs/narratives/README.md`) | Narrative format primitives: bounded frontmatter, prose-paragraph Moments, Cast/Setting, seven disposition tags | Item 1 (four backfill narratives use this format verbatim) |
| Phase 2 (narrative 001 + findings + Phase 2.5 absorption) | Findings discipline (four routing lanes) against lived code; narrative-internal retro shape | Item 1 (each backfill narrative runs the same discipline); Item 3 (retro section name and structure follow narrative-internal-retro precedent) |
| Phase 3 Item 2 (per-row vs consolidated bidirectional reference forms) | Per-row form is default for BC-focused workshops citing 1-3 slices | Item 1 (each backfill narrative implements 1-3 slices on its BC's workshop, so per-row form applies; W002, W003, W004 receive narrative back-references this phase) |
| Phase 4 outputs (ADR-018, decision notes, parked items) | Each subtracts scope from amendments | Items 2 and 3 (subtraction list per Phase 4 retro §"Phase 5 readiness") |

### 2.2 Item shape and PR multiplicity

Unlike Phases 1, 3, and 4 (each one PR with one commit per item plus a retro commit), Phase 5 spreads across multiple PRs:

| Item | Sessions | PR count | Why |
|---|---|---|---|
| Items 2 and 3 (combined: amendments to AUTHORING.md and retrospectives README) | 1 | 1 | Mechanical doc edits; shared review surface; same kind of artifact |
| Item 1 (four backfill narratives) | 4 | 4 | Each narrative runs the full Phase 2 discipline. Per-narrative orientation, Cast/Setting, Moment-by-Moment sign-off, findings discipline against lived code, narrative-internal retro. AUTHORING.md rule 1 (one prompt = one PR) prohibits batching |
| Item 4 (cutover gate) | 1 | 1 | Authors M5-S1 prompt at `docs/prompts/implementations/M5-S1-<slug>.md` citing a narrative as jointly authoritative scope; this is the closure signal |
| Phase 5 retrospective | (folded into cutover PR or its own) | 0 or 1 | Mirrors Phases 1, 2.5, 3, 4 retros under `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` |

So Phase 5 is **5-6 PRs total** across multiple sessions. Each per-narrative prompt lives at `docs/prompts/narratives/00X-<slug>.md`. The Phase 5 retrospective lives at `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` and may either close the cutover-gate PR or stand alone as a final closing PR.

### 2.3 Item ordering rationale

Per [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15 (no ordering specified), this prompt commits to:

1. **Items 2 and 3 first** - mechanical amendments to AUTHORING.md and retrospectives README. Single docs PR; clears the deck and pins the contract before substance lands.
2. **Item 1a Settlement BC backfill narrative** - forward-spec narrative (no lived code; smaller audit surface). Unblocks the Item 4 cutover (M5 = Settlement; M5-S1 cites this narrative).
3. **Item 1b Participants BC backfill narrative** - smallest lived surface; companion to narrative 001 Moment 1 at finer grain.
4. **Item 1c Selling BC backfill narrative** - medium lived surface; M2 listing-pipeline + M4-S2 WithdrawListing.
5. **Item 1d Auctions BC backfill narrative** - largest lived surface; M3 + M4-S1 span; biggest audit surface and most likely to surface `code-update` findings.
6. **Item 4 cutover gate** - authors M5-S1 prompt citing the Settlement narrative. Phase 5 (and the foundation refresh) closes when this prompt lands.

Smallest-amendment-first then forward-spec-narrative-first ordering. Settlement first because it gates M5 cutover; the lived-BC narratives follow in increasing audit-surface order. Item 4 last because it depends on at least the Settlement narrative existing and on Items 2/3 amendments being live (the M5-S1 prompt is the first lived test of the new template).

Alternative ordering (any backfill narrative first; lived-BC narratives in a different complexity order) is also defensible. Items 2/3 first and Item 4 last are structurally fixed; the four backfill narratives between them can reorder. Flag at session start if the alternative fits better.

---

## 3. Items

### 3.1 Items 2 and 3 (combined) - Amendments to AUTHORING.md and retrospectives README

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15.3 and §15.4.

**Files edited:**
- `docs/prompts/AUTHORING.md`
- `docs/retrospectives/README.md`

**Scope of AUTHORING.md amendment:**

- **Rule 3 grows.** Current text: "The milestone doc is authoritative for scope. Implementation prompts reference it; they do not duplicate it. If a prompt and the milestone doc disagree, the milestone doc wins and the prompt is wrong." New text appends: where a narrative covers the slice's journey at the bidder, seller, auctioneer, or operator perspective being implemented, the milestone doc and the relevant narrative are jointly authoritative. If no narrative covers the journey, author one before the slice runs.
- **Implementation prompt template grows a `Narrative:` line** in its metadata block. The line cites the narrative the slice implements (e.g., `**Narrative:** docs/narratives/00X-<slug>.md`). Slices that do not anchor to a narrative explain why in the metadata block.
- **Adapting section gets a narrative-prompt template reference.** The "Adapting the template for non-implementation prompts" section already covers narratives, decisions, workshops, skills, foundation. Confirm the Phase 2 narrative-authoring prompt as the canonical adaptation example; no new template is authored.

**Scope of retrospectives README amendment:**

- **Adds a "Findings against narrative" section** to the retrospective template (between "Key learnings" and "Verification checklist" is a natural placement; executor's call). The section records: did the slice implement the narrative as drafted, or did the slice surface drift? Findings route to the four lanes per the standing discipline (`narrative-update`, `workshop-update`, `code-update`, `document-as-intentional`).
- **Names the rare-slice exception.** For slices that do not anchor to a narrative (likely operator runbooks, infrastructure-only changes), the section explains why and notes whether a follow-up narrative is warranted.
- **Does not introduce a learnings-file pointer.** Per Phase 4 Q3 (skip), the retro template stays as the canonical session-grain learnings layer; the methodology log carries cross-cutting observations.

**Acceptance:**
- [ ] AUTHORING.md rule 3 carries the joint-authority clause for milestone doc plus narrative.
- [ ] Implementation prompt template carries a `Narrative:` line in the metadata block.
- [ ] Retrospectives README carries a "Findings against narrative" section in the template.
- [ ] Neither file references Reqnroll, executable specs, project-level glossary, or a learnings file (per Phase 4 subtractions).
- [ ] Em-dash audit clean on both files.

### 3.2 Item 1a - Backfill narrative for Settlement BC

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15.2 (Settlement bullet).

**Working title and slug (default; confirm at session kickoff):** `002-winner-clears-settlement` or `002-bidder-pays-after-flash-win`.

**Default protagonist and perspective:** SwiftFerret42 as the winner (continuity with narrative 001), but at finer grain than narrative 001 Moment 8 - dramatizes the saga's intermediate events, reserve-check behavior, charge-completion, and seller payout from the bidder's window.

**Workshop input:** [`docs/workshops/003-settlement-bc-deep-dive.md`](../../workshops/003-settlement-bc-deep-dive.md) and `003-scenarios.md`.

**Lived-code audit surface:** None. Settlement BC is unshipped (M5 territory). The audit runs against W003 and the integration-events vocabulary in `CritterBids.Contracts.Settlement`. `code-update` findings should be near-zero; `workshop-update` findings if W003 is stale; `narrative-update` and `document-as-intentional` findings as the dominant lanes.

**Per-narrative prompt:** Authored at session kickoff at `docs/prompts/narratives/002-<slug>.md`, adapting from `docs/prompts/narratives/001-bidder-wins-flash-auction.md`. The per-narrative prompt is itself a Phase 5 deliverable.

**Acceptance:**
- [ ] Narrative file at `docs/narratives/002-<slug>.md` with `status: accepted` in frontmatter.
- [ ] Findings file at `docs/narratives/002-findings.md` (or noted-absent if zero findings surfaced).
- [ ] Narrative-internal retro appended in the narrative file.
- [ ] W003 carries `Narratives: [002-<slug>]` per-row references on the slices the narrative implements (per-row form per Phase 3 Item 2's BC-workshop default).
- [ ] `docs/narratives/README.md` Index table updated.

### 3.3 Item 1b - Backfill narrative for Participants BC

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15.2 (Participants bullet).

**Working title and slug (default; confirm at session kickoff):** `003-bidder-starts-anonymous-session`.

**Default protagonist and perspective:** A new anonymous bidder (BoldPenguin7, surfaced from narrative 001's offstage cast onto stage here) to vary the protagonist surface across narratives. Alternative: SwiftFerret42 at finer grain than narrative 001 Moment 1.

**Workshop input:** No dedicated Participants BC workshop exists today (Participants is covered implicitly through W001 Tier 0). The narrative's workshop reference is W001 §"Tier 0 - Bidder onboarding" plus the lived `CritterBids.Participants` code.

**Lived-code audit surface:** M1 baseline + Participants BC scaffold (M1-S2 retro). Small surface; `code-update` findings unlikely beyond what narrative 001 Moment 1 already documented.

**Per-narrative prompt:** `docs/prompts/narratives/003-<slug>.md`.

**Acceptance:**
- [ ] Narrative file at `docs/narratives/003-<slug>.md` with `status: accepted`.
- [ ] Findings file at `docs/narratives/003-findings.md` or conscious-skip note in the narrative's retro.
- [ ] Narrative-internal retro appended.
- [ ] W001 Tier 0 carries narrative back-references per the consolidated form (W001 already uses the consolidated `Narrative Cross-References` block; extend it).
- [ ] `docs/narratives/README.md` Index table updated.

### 3.4 Item 1c - Backfill narrative for Selling BC

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15.2 (Selling bullet).

**Working title and slug (default; confirm at session kickoff):** `004-seller-publishes-and-withdraws-listing`.

**Default protagonist and perspective:** GreyOwl12 (the offstage seller from narrative 001) brought onstage. Single-seller perspective on listing creation, submission, approval, publish, and withdrawal. Includes the M4-S2 WithdrawListing flow.

**Workshop input:** [`docs/workshops/004-selling-bc-deep-dive.md`](../../workshops/004-selling-bc-deep-dive.md) and `004-scenarios.md`.

**Lived-code audit surface:** M2 listing-pipeline slices + M4-S2 WithdrawListing. Medium surface; `code-update` and `workshop-update` findings expected.

**Per-narrative prompt:** `docs/prompts/narratives/004-<slug>.md`.

**Acceptance:**
- [ ] Narrative file at `docs/narratives/004-<slug>.md` with `status: accepted`.
- [ ] Findings file at `docs/narratives/004-findings.md` or conscious-skip note.
- [ ] Narrative-internal retro appended.
- [ ] W004 carries `Narratives: [004-<slug>]` per-row references on implemented slices.
- [ ] `docs/narratives/README.md` Index table updated.
- [ ] `code-update` findings (if any) get stub follow-up prompts at `docs/prompts/implementations/<slug>.md` per Phase 2.5 discipline.

### 3.5 Item 1d - Backfill narrative for Auctions BC

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15.2 (Auctions bullet).

**Working title and slug (default; confirm at session kickoff):** `005-seller-watches-flash-auction-close` or `005-extended-bidding-saga-terminal-paths`.

**Default protagonist and perspective:** GreyOwl12 (seller-perspective) on a winning Flash auction with extended bidding. Alternative: operator-perspective on the same. Companion to narrative 001's bidder-perspective; covers the M3-S5b auction-closing saga's terminal paths, extended-bidding window mechanics, and reserve-met threshold semantics from the seller's window.

**Workshop input:** [`docs/workshops/002-auctions-bc-deep-dive.md`](../../workshops/002-auctions-bc-deep-dive.md) and `002-scenarios.md`.

**Lived-code audit surface:** M3 (S1-S6) + M4-S1. Largest lived surface in the project. `code-update` and `workshop-update` findings most likely to surface here.

**Per-narrative prompt:** `docs/prompts/narratives/005-<slug>.md`.

**Acceptance:**
- [ ] Narrative file at `docs/narratives/005-<slug>.md` with `status: accepted`.
- [ ] Findings file at `docs/narratives/005-findings.md` or conscious-skip note.
- [ ] Narrative-internal retro appended.
- [ ] W002 carries `Narratives: [005-<slug>]` per-row references on implemented slices.
- [ ] `docs/narratives/README.md` Index table updated.
- [ ] `code-update` findings (if any) get stub follow-up prompts per Phase 2.5 discipline.

### 3.6 Item 4 - Cutover gate

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15.5.

**Definition:** The foundation refresh closes when M5's first slice prompt cites a narrative as jointly authoritative scope alongside the M5 milestone doc. That citation is the visible signal that the workflow has switched.

**Deliverable:** `docs/prompts/implementations/M5-S1-<slug>.md` (slug determined by the M5 milestone doc's slice scope; expected to be a Settlement-BC foundation-decisions slice equivalent to M3-S1 and M4-S1, per Phase 4 retro §"What's the next non-methodology slice").

**Cutover-prompt requirements:**
- Carries a `**Narrative:**` line in the metadata block citing `docs/narratives/002-<slug>.md` (the Settlement backfill narrative).
- Names the M5 milestone doc as authoritative for slice scope.
- Follows the Item 2 amended implementation prompt template (i.e., the cutover prompt is the first lived test of the new template).
- **Out of scope for Phase 5:** actually running M5-S1 (that is M5 work, not Phase 5 work).

**M5 milestone doc prerequisite:** No `docs/milestones/M5-<name>.md` exists today (M4 is the latest). The M5 milestone doc is a prerequisite for the cutover prompt's authoring. Two paths:

1. **Author M5 milestone doc as part of Item 4** (recommended if scope is straightforward enough to land in the cutover PR).
2. **Author M5 milestone doc in a separate prior PR** (if scope decisions warrant their own session; the cutover prompt then references the existing milestone doc).

Flag at Item 4 kickoff; user decides.

**Acceptance:**
- [ ] M5 milestone doc exists at `docs/milestones/M5-<name>.md` (either authored in this PR or in a prior session).
- [ ] M5-S1 prompt exists at `docs/prompts/implementations/M5-S1-<slug>.md`.
- [ ] M5-S1 prompt carries a `**Narrative:**` line citing `docs/narratives/002-<slug>.md`.
- [ ] M5-S1 prompt follows the amended implementation prompt template from Item 2.
- [ ] No `src/` or `tests/` files edited.

---

## 4. Working pattern

### 4.1 Cadence

Per-PR interactive sign-off. Items 2/3 amendments land first as a single docs PR. Each backfill narrative runs as its own session with Phase 2's Moment-by-Moment sign-off discipline. The cutover gate is its own session.

### 4.2 Session-prompt-first discipline

Each backfill narrative session (Items 1a-1d) requires its per-narrative prompt to be authored and committed *before* the narrative session itself runs. Mirrors Phase 2's discipline: `docs/prompts/narratives/00X-<slug>.md` is committed first, then the narrative session executes against it.

### 4.3 Findings discipline

Each backfill narrative runs the four-lane findings discipline per [`docs/narratives/README.md`](../../narratives/README.md) and the Phase 2 narrative-authoring prompt §"Findings discipline":

- `narrative-update` and `document-as-intentional` resolve in the narrative's own PR.
- `workshop-update` resolves in the narrative's own PR by editing the workshop directly.
- `code-update` produces a stub follow-up implementation prompt; the slice runs in subsequent product work, not in Phase 5.

The Settlement narrative (Item 1a) is forward-spec; expect zero `code-update` findings. The other three are against lived code; expect a mix.

### 4.4 Em-dash sweep

The no-em-dash convention (handoff §13) applies to all prose this phase writes. Audit-after-write per file before staging, per Phase 3 retro Key Learning 4.

### 4.5 Methodology log

Per Phase 4 retrospective §"Methodology log time-box review", the time-box updated to "after Phase 5 closes, or after the methodology log carries three entries, whichever comes first." The four backfill narratives are the lived chance for Entry 001 to land. Apply the entry-criteria gate per session: if a genuinely cross-cutting observation about narrative authoring against lived code surfaces (or about the absence of it for Settlement), write the entry. Silence is fine; conscious-skip notes in narrative-internal retros suffice if no entry warranted.

### 4.6 Per-narrative-internal retro discipline

Each backfill narrative ends with its own narrative-internal retrospective (mirrors `docs/narratives/001-bidder-wins-flash-auction.md` §"Retrospective"). The Phase 5 cross-narrative retrospective at `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` aggregates per-narrative observations rather than duplicating them.

---

## 5. Commit and PR sequence (proposed)

| Order | PR | Branch | Commits |
|---|---|---|---|
| 1 | Items 2+3 amendments | `foundation-refresh/p5-amendments` | 1 commit on AUTHORING.md, 1 on retrospectives README, optionally combined |
| 2 | Item 1a Settlement narrative | `foundation-refresh/p5-narrative-002-settlement` | Per-narrative prompt commit, Cast/Setting commit, per-Moment commits, findings commit, retro commit |
| 3 | Item 1b Participants narrative | `foundation-refresh/p5-narrative-003-participants` | Same pattern |
| 4 | Item 1c Selling narrative | `foundation-refresh/p5-narrative-004-selling` | Same pattern; may include `code-update` stub prompts |
| 5 | Item 1d Auctions narrative | `foundation-refresh/p5-narrative-005-auctions` | Same pattern; most likely to include `code-update` stubs |
| 6 | Item 4 cutover gate | `foundation-refresh/p5-cutover-m5-s1` | M5 milestone doc (if folded), M5-S1 prompt, Phase 5 retrospective |

The Phase 5 retrospective lands on the cutover-gate PR by default (closes the foundation refresh on the same PR that crosses the cutover). Alternative: separate closing PR if the cutover PR is heavy.

If user prefers a different PR shape (e.g., two narratives per PR), flag at Phase 5 kickoff and decide before starting.

---

## 6. Acceptance criteria (Phase 5 close gate)

- [ ] AUTHORING.md rule 3 carries the joint-authority clause for milestone doc plus narrative.
- [ ] Implementation prompt template carries a `Narrative:` line in the metadata block.
- [ ] Retrospectives README carries a "Findings against narrative" section in the retro template.
- [ ] Four backfill narratives exist at `docs/narratives/002-<slug>.md` through `005-<slug>.md` with `status: accepted`.
- [ ] Each backfill narrative has a corresponding findings file (or conscious-skip note in the narrative's retro).
- [ ] W001, W002, W003, W004 carry narrative back-references for the slices each backfill narrative implements (consolidated form on W001; per-row form on W002, W003, W004 per Phase 3 Item 2 defaults).
- [ ] `docs/narratives/README.md` Index table lists all five narratives (001 plus the four backfills).
- [ ] `code-update` findings (if any) have stub follow-up prompts at `docs/prompts/implementations/<slug>.md` per Phase 2.5 discipline. These are not run in Phase 5.
- [ ] M5 milestone doc exists at `docs/milestones/M5-<name>.md`.
- [ ] M5-S1 prompt exists at `docs/prompts/implementations/M5-S1-<slug>.md` and cites `docs/narratives/002-<slug>.md` as jointly authoritative scope alongside the M5 milestone doc.
- [ ] No file under `src/` or `tests/` edited in Phase 5.
- [ ] No new ADR, no new skill file, no new convention rollout (rollouts were Phase 3 territory).
- [ ] No em dashes in any committed prose authored by this phase (audit-after-write per file).
- [ ] `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` exists and mirrors the structure of Phases 1, 2, 2.5, 3, 4 retros.
- [ ] Phase 5 retrospective contains:
  - A "Backfill narrative summary" subsection enumerating the four backfill narratives, their findings counts per lane, and the cross-narrative observations (if any) that warrant methodology log entries.
  - A "Cutover gate" subsection confirming M5-S1's prompt-citation of the Settlement narrative.
  - A "Methodology log disposition" subsection per the time-box review.
  - A "What's next" subsection naming M5-S1 execution as the next product slice (running the prompt, not authoring it).

---

## 7. Explicitly out of scope

- **Running M5-S1.** Phase 5 authors the cutover prompt; running it is M5 work and the first non-methodology slice per Phase 4 retro.
- **Running any `code-update` follow-up slices surfaced by backfill narratives.** Stubs are created; execution is product work after Phase 5 closes (Phase 2.5 discipline applies if multiple stubs accumulate).
- **New ADRs.** Phase 4 was the ADR-resolution phase; Phase 5 introduces no new methodology decisions. If a backfill narrative surfaces an ADR-grade question, capture in the narrative-internal retro and route to a future session.
- **New skill files.** Same shape as new ADRs; capture and route, do not author.
- **Workshop scenario file edits beyond what `workshop-update` findings produce.** The four scenarios files (`001-scenarios.md` through `004-scenarios.md`) are touched only when a backfill narrative's findings discipline routes a discrepancy to `workshop-update`.
- **Project-level glossary at `docs/vision/glossary.md`.** Per Phase 4 Q2 disposition (per-BC only), this file is not authored.
- **Learnings file at `docs/learnings.md` or `docs/skills/<bc>/learnings.md`.** Per Phase 4 Q3 disposition (skip), this file is not authored.
- **Reqnroll executable specs.** Per Phase 4 Q1 / ADR-018 disposition (decline at MVP), no `.feature` files are authored in any backfill narrative.
- **Operations runbook content (Q5 / PARKED.md P-001) and demo-script runbook content (Q4 / PARKED.md P-002).** Both parked with triggers per Phase 4; Phase 5 does not draft either.
- **Code refactoring of any kind.** No code work in Phase 5.
- **Re-authoring narrative 001.** Narrative 001 is `status: accepted`; backfill narratives are siblings, not replacements. If a backfill surfaces a contradiction with narrative 001, route via `narrative-update` against narrative 001 in the surfacing narrative's PR.
- **Phase 5 spawning Phase 6.** This is the final phase. The closing retro names what comes next as product work (M5-S1 execution), not another methodology phase.

---

## 8. Open questions to flag at kickoff (not decide)

- **Item ordering override.** Default is amendments → Settlement → Participants → Selling → Auctions → cutover. Alternatives: any backfill narrative first; lived-BC narratives in a different complexity order. Items 2/3 first and Item 4 last are structurally fixed; the four backfills can reorder. User decides at Phase 5 kickoff.
- **M5 milestone doc shape and timing.** Does it land as part of the cutover PR (Item 4) or in a separate prior session? Default is "fold into cutover PR if scope is straightforward." The M5 milestone doc itself may need its own scoping conversation (which Settlement BC slices, in what order, against what M3 / M4 milestone-doc precedent); that conversation is methodology-adjacent but is M5 product-planning work. User decides at Item 4 kickoff.
- **Backfill narrative protagonist choices.** Defaults proposed (SwiftFerret42 winner-perspective for Settlement; BoldPenguin7 for Participants; GreyOwl12 for Selling and Auctions). User can override per narrative at session start; protagonists do not have to come from narrative 001's cast.
- **Backfill narrative slug names.** Working titles proposed (`002-winner-clears-settlement`, `003-bidder-starts-anonymous-session`, `004-seller-publishes-and-withdraws-listing`, `005-seller-watches-flash-auction-close`). User can override per session.
- **PR shape per backfill narrative.** Default is one PR per narrative (matches Phase 2's narrative 001 PR shape and AUTHORING.md rule 1). Alternative is two narratives per PR if the audit surfaces are small enough. User decides; default recommended.
- **Phase 5 retrospective shape.** Default is one cross-narrative retrospective at `docs/retrospectives/foundation-refresh-phase-5-retrospective.md` aggregating per-narrative observations. Each narrative still carries its own internal retro. Alternative: skip the cross-narrative retrospective if per-narrative retros suffice. User decides at session close.
- **Methodology log Entry 001.** The four backfill narratives are the lived chance per Phase 4 retro's updated time-box. Apply the entry-criteria gate per session and at Phase 5 close. Silence fine.
- **Cutover-PR fold.** Should the Phase 5 retrospective land on the cutover-gate PR or stand alone? Default: fold into cutover PR. User decides at Item 4 kickoff.

---

## 9. Starting move

When Phase 5 begins:

1. Read this document fully.
2. Re-read [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15 (the authoritative scope).
3. Skim [`foundation-refresh-phase-4-retrospective.md`](../../retrospectives/foundation-refresh-phase-4-retrospective.md), particularly §"Phase 5 readiness" (Phase 4 outputs that subtract scope from amendments) and §"What's the next non-methodology slice" (M5-S1 framing).
4. Skim [`foundation-refresh-phase-2-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-retrospective.md) and [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) for narrative-authoring discipline reference.
5. Confirm with the user:
   - Item ordering (default: amendments → Settlement → Participants → Selling → Auctions → cutover).
   - PR shape per backfill narrative (default: one PR each).
   - Backfill narrative protagonists and slugs (defaults per §3).
   - M5 milestone doc shape and timing (default: folded into cutover PR if scope allows).
   - Whether the Phase 5 retrospective folds into the cutover PR or stands alone.
6. Begin with the Items 2+3 amendments PR. Do not start backfill narratives until amendments are committed.
7. Each backfill narrative session: author the per-narrative prompt at `docs/prompts/narratives/00X-<slug>.md` first, commit, then run the narrative session against it.
8. Final session: author the M5 milestone doc (if not yet authored), author the M5-S1 prompt with the cutover-gate citation, write the Phase 5 retrospective.

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

- **v0.1** (2026-04-28): Authored at Phase 4 close as the Phase 5 session prompt. References [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §15 as authoritative for scope; supplies execution shape (item ordering, multi-PR sequence, per-narrative scoping defaults, cutover-gate definition) per AUTHORING.md rule 3. Mirrors the Phase 4 prompt's structure with adaptations for the multi-PR phase shape: Item 1 expanded into 1a-1d for the four backfill narratives; PR sequence section replaces single-commit sequence; Item 4 carries the M5 milestone-doc prerequisite as an open question.
