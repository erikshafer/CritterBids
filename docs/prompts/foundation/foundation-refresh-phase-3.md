# Foundation Refresh Phase 3 - Convention Rollouts

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-27 |
| **Author of record** | Erik Shafer (with prior-session AI collaborator review of Phase 1 and Phase 2 retros) |
| **Phase** | 3 of 5 (the four-phase plan plus Phase 5; contingent Phase 2.5 closed) |
| **Authoritative scope** | [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6 |
| **Target retro** | `docs/retrospectives/foundation-refresh-phase-3-retrospective.md` |
| **PR shape** | One PR for the whole phase, one commit per item plus the retro commit (six commits total) |

---

## 1. Read this section first

Phase 3 is the **convention-rollout phase** of the foundation refresh. Phases 1 and 2 produced new methodology primitives (ADRs 016 and 017, the narratives directory, the rules directory, the methodology log, the prompts subdivision, narrative 001). Phase 3 applies those conventions retroactively across the four CritterBids workshops and the event-modeling skill file. **Pure docs work; no code, no new methodology, no new ADRs.**

The phase has **five items** scoped in [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6. This prompt does not re-specify them. It supplies:

- The order to execute them in.
- The commit sequence and PR shape.
- What's *already done* (Phase 2's deviation already partially absorbed Item 2's scope).
- Current-state observations about the four workshops and the skill file.
- Per-item acceptance gates.

Three things to keep loaded throughout:

1. **`foundation-refresh-handoff.md` §6 is authoritative** for what each item produces. This prompt is the execution shape, not the spec.
2. **No code refactoring in Phase 3.** Findings discipline (the four routing lanes) belongs to narrative sessions; Phase 3 surfaces no `code-update` lane work. If a workshop edit reveals lived code drift mid-session, capture it as a candidate for the next narrative pass and route it there. Do not absorb mid-Phase-3.
3. **The em-dash convention from §13 of the handoff applies** to all prose this session writes. Hyphens are fine; em dashes are out. Pre-existing em dashes on rows not under edit retain the grandfather clause (per Phase 2's Finding 006 sweep precedent); rows under edit should sweep their em dashes to hyphens opportunistically.

---

## 2. Phase context

### 2.1 What Phases 1 and 2 produced that Phase 3 rolls out

| Source | Convention to roll out | Phase 3 item |
|---|---|---|
| Phase 1 Item 2 (ADR 017) | Design-phase workflow sequence; per-BC opt-in for Steps 1-2 going forward | (Informs Phase 3 framing; not a rollout item) |
| Phase 1 Item 3 (`docs/narratives/README.md`) | Narrative format primitives: Cast, Setting, Moments, bounded frontmatter, bidirectional referencing | Items 2 (cross-references), 3 (Cast/Setting on W001) |
| Phase 1 Item 4 (`docs/rules/README.md`, `structural-constraints.md`) | Three-layer rules architecture; Layer 2 (per-BC ubiquitous language) deferred to Phase 3 | Item 4 (per-BC UL in W002-W004 feeds Layer 2) |
| Phase 1 Item 5 (prompt subdivision; AUTHORING.md) | Prompts organized by artifact type; ten authoring rules | (Already adopted; nothing to roll out) |
| Phase 2 (narrative 001) | Status frontmatter referenced via `slices_implemented`; Klefter/Bruun pattern names referenced in narrative 001 and W001 prose; consolidated bidirectional reference form on W001 | Items 1 (status frontmatter on workshop slices), 5 (Klefter/Bruun in skill), 2 (codify consolidated form) |

### 2.2 What's already done that Item 2 originally covered

Phase 2's "Deviations from the prompt's acceptance criteria" section in [`foundation-refresh-phase-2-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-retrospective.md) records that the per-slice `Narratives: [001-bidder-wins-flash-auction]` cross-reference lines on W001 were resolved as a **single consolidated `Narrative Cross-References` note** at the start of W001 §"Phase 4 - Identify Slices" rather than ten per-slice inline edits, with user approval at session close. The retro flagged: "Phase 3 may revisit the consolidated-vs-per-row choice if it becomes load-bearing."

Item 2's effective Phase 3 scope is therefore **convention codification**, not new edits to W001:

- Confirm the consolidated form as the standing convention.
- Amend `docs/narratives/README.md` §"Bidirectional referencing (forward-looking)" to name both the per-row and consolidated forms as valid, with consolidated as the default for journey-grain workshops citing many slices and per-row as the default for BC-focused workshops with one or two cited slices.
- W002-W004 carry no narrative back-references yet (their narratives land in Phase 5). No edit to those workshops is needed in Phase 3 Item 2.

### 2.3 Current state of the workshops and the skill file

- **W001 (`001-flash-session-demo-day-journey.md`)**: User-journey workshop. Phase 1 sections heavily condensed; Phase 4 carries the slice tables organized by Tier 0-9 with rows shaped `# | Slice | Command | Events | View | BC | Priority`. Already carries the consolidated `Narrative Cross-References` note. **No Cast or Setting blocks today.**
- **W002 (`002-auctions-bc-deep-dive.md`)**: BC-focused workshop. Heavily condensed Phase 1; no §3 Ubiquitous Language section today. Phase 3 (scenarios) covers slice scenarios at component grain (DCB, Auction Closing Saga, Proxy Bid Manager, Session Aggregate).
- **W003 (`003-settlement-bc-deep-dive.md`)**: BC-focused workshop. Settlement BC scope. No §3 UL today. Settlement code is unshipped (M5 territory); UL terms drawn from the workshop's prose and the integration-events vocabulary.
- **W004 (`004-selling-bc-deep-dive.md`)**: BC-focused workshop. Selling BC scope. No §3 UL today. Lived M2 and M4 code; UL terms drawn from `Listing` aggregate, `DraftListing`, `ListingPublished`, `WithdrawListing` flow.
- **`docs/skills/event-modeling/SKILL.md`**: 245 lines today. References Bruun's `*` suffix convention via the narratives README's notation section but does not name **Klefter** or **Bruun** as such. The natural insertion point is a new top-level section after "Structured Output Format for Slices" and before "Output Artifacts" titled "Adjunct patterns" or similar.

### 2.4 Item ordering rationale

Per [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §12 ("Items can run in any order"), this prompt commits to:

1. **Item 1 first** - mechanical warm-up across all four workshops; produces a clean baseline of slice-status visibility before any other edit lands.
2. **Item 4 second** - per-BC UL §3 sections in W002-W004; large surface but mechanical-with-domain-thinking; lays Layer 2 groundwork for the rules system.
3. **Item 5 third** - Klefter/Bruun in the skill file; small, self-contained edit; reads cleanly once the workshop UL terms (from Item 4) have refreshed the executor's domain vocabulary.
4. **Item 3 fourth** - Cast/Setting on W001; the most interpretive item, benefits from sharpened context after walking the workshops twice (Items 1 and 4) and the skill file (Item 5).
5. **Item 2 last** - Convention codification on `docs/narratives/README.md`; absorbs any patterns surfaced during Items 1-4 about how cross-references should be shaped.

This ordering is a recommendation. If mid-session the executor wants to reorder, document the reordering and rationale in the retrospective.

---

## 3. Items

### 3.1 Item 1 - `status:` frontmatter on workshop slices

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6.1.

**Vocabulary:** `design | planned | in progress | done`. Source convention: Martin Dilger SDD via `C:\Code\CritterCab\docs\research\sdd-event-model-to-code.md` §"Step 2 - Event Model as Source of Truth".

**Form:** Slices in CritterBids workshops are **table rows**, not files, so true YAML frontmatter is not the natural shape. Add a `Status` column to the existing slice tables (W001 Tier tables; W002-W004 component tables). The column lands as the rightmost column or immediately after `BC` - executor's call based on table width.

**Status assignment rules of thumb:**
- A slice with lived code under `src/` and a corresponding retrospective under `docs/retrospectives/` is `done`.
- A slice with a corresponding implementation prompt under `docs/prompts/implementations/` but no retrospective is `in progress`.
- A slice with a milestone-doc allocation but no prompt yet is `planned`.
- A slice mentioned in the workshop but not yet assigned to a milestone is `design`.

**Edge cases to surface, not decide:**
- W001 slice 5.2 (Reserve met signal) has lived production-side code (M3-S4, M3-S5) but its defining View is unshipped (Relay BC). The workshop's existing Note (post-M3-S5, narrative 001 Finding 010) explains the partial state. Status: `in progress` is the natural call; flag for confirmation.
- W001 slice 1.4 (Listing detail) was unified into 1.3's view per M3-S6 / Finding 003. The slice still exists as a row but is `done` via the unified `CatalogListingView`. The existing post-M3-S6 Note covers this; status `done` is the call.
- W003 slices: most are `design` since Settlement BC is unshipped. Confirm against the M5 milestone allocations.
- W004 slices: split between `done` (M2 listing-pipeline slices, M4-S2 WithdrawListing) and `design` or `planned` for the post-MVP Selling work.

**Acceptance:**
- Every slice row in W001-W004 carries a status value from the four-vocabulary set.
- Status assignments are checked against the milestone docs and retrospective list.
- The executor flags any slice whose status is genuinely ambiguous as an open question rather than guessing.

### 3.2 Item 2 - Codify the bidirectional referencing form

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6.2; modulated by Phase 2's deviation in [`foundation-refresh-phase-2-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-retrospective.md) §"Deviations from the prompt's acceptance criteria".

**What's already done:** W001 carries a consolidated `Narrative Cross-References` note (commit `eb54594`).

**What Phase 3 Item 2 produces:** An amendment to `docs/narratives/README.md` §"Bidirectional referencing (forward-looking)" naming both forms:

- **Per-row form** (`Narratives: [001-<slug>]` line on each implemented slice). Default for BC-focused workshops where one narrative implements one or two slices.
- **Consolidated form** (single `Narrative Cross-References` block at the top of the slice section, listing all narrative-implemented slices). Default for journey-grain workshops where one narrative implements many slices (W001 with narrative 001 implementing ten slices is the precedent).

The amendment names the trade-off: per-row form is more legible per-slice but visually fragments tightly-formatted slice tables; consolidated form keeps tables clean but requires the reader to look in two places. Phase 5's backfill narratives will exercise the per-row form on BC workshops (each backfill narrative likely implements 1-3 slices), so naming both as valid is more useful than picking one.

**Acceptance:**
- `docs/narratives/README.md` §"Bidirectional referencing" carries both forms with their respective defaults.
- No edit to W001's existing consolidated note (it already conforms).
- No edit to W002-W004 (their narrative back-references land in Phase 5).

### 3.3 Item 3 - Cast and Setting blocks added to W001

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6.3.

**Source for Cast and Setting:** [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) §"Cast" and §"Setting".

**Adaptation:** The narrative's Cast names ten actors with onstage/offstage status; the Setting establishes canonical numbers (reserve $50, hammer $55, fee 10%, credit ceiling band $500) and three-paragraph policy posture. W001 is a journey workshop that pre-dates the narrative; adding Cast and Setting retroactively makes W001 a hybrid workshop/narrative artifact.

**Cast adaptation:**
- Lift the named protagonists (SwiftFerret42, BoldPenguin7, GreyOwl12, et al.) from the narrative.
- W001's Cast may need to be slightly broader than narrative 001's because the workshop covers all 34 P0/P1/P2 slices, not just the ten the narrative implements - some Cast members appear only in slices the narrative doesn't reach (the Buy It Now purchaser, the proxy bidder, the seller-perspective Settlement actor).
- Onstage/offstage status applies per slice, not per workshop - flag as an open question if the workshop-grain Cast format makes onstage/offstage rigid.

**Setting adaptation:**
- The narrative's Setting establishes Flash session canonical numbers for one happy-path bidder. The workshop's Setting needs to absorb broader policy posture: P0/P1/P2 slice priorities, bid increment policy ($1 under $100, $5 at $100+ per W002 Phase 2 resolution), `MaxDuration` cap policy, credit ceiling band, demo-mode timeout policy.
- Three paragraphs is the narrative's shape; the workshop's Setting may need four or five paragraphs to cover the broader policy surface. Length is not the constraint; coherence is.

**Placement:** Between the workshop's title-block metadata and Phase 1. The existing Phase 1 condensed paragraph stays.

**Acceptance:**
- W001 carries `## Cast` and `## Setting` sections immediately after the title-block metadata.
- Cast covers actors the workshop's slices reach (broader than narrative 001's Cast).
- Setting covers policy posture inherited by the workshop's slices (broader than narrative 001's per-bidder Setting).
- The narrative's Cast and Setting language is preserved verbatim where it overlaps; additions are flagged as workshop-grain extensions.

### 3.4 Item 4 - Per-BC Ubiquitous Language §3 sections in W002-W004

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6.4.

**Source pattern:** `C:\Code\CritterCab\docs\workshops\001-dispatch-event-model.md` §3 ("Ubiquitous Language").

**Pattern shape:** A `Term | Definition | Notes` table. Each row carries a one-line definition and optional "what it is *not*" notes. Cross-reference any term that overlaps with another BC's vocabulary.

**Per-workshop scope:**

- **W002 (Auctions BC):** Terms from the workshop's prose and the lived Auctions code: `BidConsistencyState` (DCB), `Auction Closing Saga`, `Proxy Bid Manager Saga`, `Session Aggregate`, `Reserve`, `Hammer Price`, `Buy It Now`, `Extended Bidding`, `MaxDuration`, `Trigger Window`, `Credit Ceiling`, `Bid Increment`, `Bidder`, `Listing`, `Flash Session`, `Timed Auction`, `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `ProxyBidRegistered`, `ProxyBidExhausted`, `BidRejected`. Cross-reference: `Listing` overlaps with W004 (Selling) and W001 (journey-grain).
- **W003 (Settlement BC):** Terms from the workshop's prose and Settlement integration contracts: `Settlement Saga`, `SettlementInitiated`, `SettlementCompleted`, `Reserve Check`, `ReserveCheckCompleted`, `Sale Closing`, `Buyer Charge`, `Seller Payout`, `Hammer Price` (cross-ref W002), `Platform Fee`, `Settlement Window`. Settlement is unshipped; terms come from W003 prose, not from lived code.
- **W004 (Selling BC):** Terms from the workshop's prose and the lived Selling code: `Draft Listing`, `Listing` (cross-ref W002), `Listing Submission`, `Listing Approval`, `Listing Publish`, `Listing Withdrawal`, `Listing State Machine` (Draft, Submitted, Published, Withdrawn), `Reserve` (cross-ref W002), `Buy It Now Price` (cross-ref W002), `Seller`, `Seller Registration`, `Auction Format` (Timed, Flash). Cross-reference handles the term overlap with W002.

**Placement:** Between the workshop's title-block metadata and Phase 1 (mirroring CritterCab's W001 §3 placement). For W002 specifically, this lands before "Phase 1 - Brain Dump: Internal Structure".

**Acceptance:**
- W002, W003, W004 each carry a §3 Ubiquitous Language section with `Term | Definition | Notes` shape.
- Cross-BC overlapping terms are noted (`Listing`, `Hammer Price`, `Reserve`, `Buy It Now`).
- The vocabulary lists are sourced from workshop prose and lived code (where applicable), not invented.
- W001 does not get a §3 UL (it's a journey workshop, not BC-focused); its UL surfaces through Setting and the narrative cross-references.

### 3.5 Item 5 - Klefter and Bruun pattern names in event-modeling skill

**Authoritative spec:** [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6.5.

**Source:** `C:\Code\CritterCab\docs\research\agents-in-event-models.md` and `C:\Code\CritterCab\docs\research\event-modeling-workshop-guide.md`.

**Target:** `C:\Code\CritterBids\docs\skills\event-modeling\SKILL.md`.

**Insertion point:** A new top-level section between "Structured Output Format for Slices" and "Output Artifacts" titled "Adjunct patterns" or similar. The section names three patterns:

1. **Klefter translation-decision events.** Local first-class event when a slice coordinates with an external system AND a decision is made locally. CritterBids example: Auctions BC reading from Listings to make a `ReserveMet` decision (Auctions DCB handler); Settlement asking Participants for credit ceiling and recording the outcome would be another. Pattern signal: an outbound query whose result you commit as a local event.
2. **Bruun temporal-automation slice pattern.** Todo-list read models with asterisk-suffix names; clock-rewind glyph on time-driven automation stickies. CritterBids example: the auction-closing saga's scheduled `CloseAuction` is a temporal-automation slice; an `AuctionsAwaitingClose*` projection (if introduced) would carry the asterisk. Pattern signal: an automation whose trigger is the passage of time, not an incoming domain event.
3. **Configuration-as-events (Bruun).** Operator-tunable policy parameters as events on a singleton stream rather than a settings table. CritterBids candidate: the auction-closing saga's `triggerWindow`, `extension`, and `maxDuration` parameters could adopt this if the project chooses; today they are constants. Pattern signal: settings that need an audit trail and version history.

**Naming, not committing:** These are pattern *names*, not commitments to refactor existing CritterBids code. The skill file becomes legible to readers who encounter the patterns elsewhere (CritterCab, Bruun's published material, narrative 001's notation conventions).

**Cross-reference:** The narratives README §"Notation conventions" already mentions Bruun's `*` suffix convention; this skill update names the underlying pattern. Add a back-reference from the narratives README to the skill section if the prose flows naturally; do not force it.

**Acceptance:**
- `docs/skills/event-modeling/SKILL.md` carries a new section naming Klefter, Bruun temporal-automation, and Bruun configuration-as-events patterns.
- Each pattern names its CritterBids example (lived or candidate).
- The narratives README's existing Bruun reference at §"Notation conventions" is left intact (or updated with a back-pointer if the prose flow allows).
- No code changes; no workshop changes beyond Items 1-4.

---

## 4. Working pattern

### 4.1 Cadence

Per-item interactive sign-off. Mirror the Phase 1 commit cadence: propose the item's edits, get sign-off, commit, move on. Do not batch multiple items into a single commit.

### 4.2 Reference-doc discipline

Reference-doc discipline (M3-S5b convention) applies to any first-use claim about Wolverine/Marten/Polecat behavior. None of Phase 3's items should produce such claims; if one surfaces, cite per the discipline.

### 4.3 Em-dash sweep

The no-em-dash convention (handoff §13) applies to all prose this session writes. Pre-existing em dashes in workshops on rows not under edit retain the grandfather clause; em dashes on rows under edit (Item 1 status column additions, Item 4 §3 inserts, Item 3 Cast/Setting block) sweep to hyphens opportunistically.

### 4.4 Surface drift, do not absorb it

If a workshop edit reveals lived code drift mid-session, capture it as a candidate for the next narrative pass and flag it in the retrospective. Do not absorb mid-Phase-3. The findings discipline belongs to narrative sessions; Phase 3 is convention rollout, not code audit.

### 4.5 Per-Phase-3-close discipline

At session close, write the retrospective at `docs/retrospectives/foundation-refresh-phase-3-retrospective.md` mirroring the structure of [`foundation-refresh-phase-1-retrospective.md`](../../retrospectives/foundation-refresh-phase-1-retrospective.md) and [`foundation-refresh-phase-2-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-retrospective.md). Apply the methodology-log entry-criteria gate per Phase 2's precedent; silence is fine. If a cross-cutting observation about convention rollout against lived workshops warrants Entry 001 (or, more likely, Entry 002 - the Phase 2 retro deferred Entry 001 explicitly to the Phase 5 backfill cohort), write it; otherwise note the conscious skip in the retro.

---

## 5. Commit sequence (proposed)

1. `docs(workshops): add status column to W001-W004 slice tables (Phase 3 Item 1)` - adds the `Status` column to all slice tables across the four workshops with values from `design | planned | in progress | done`.
2. `docs(workshops): add Ubiquitous Language §3 sections to W002-W004 (Phase 3 Item 4)` - inserts the three §3 UL tables; cross-references shared terms.
3. `docs(skills): name Klefter and Bruun patterns in event-modeling skill (Phase 3 Item 5)` - adds the "Adjunct patterns" section to the skill file with three pattern names and CritterBids examples.
4. `docs(workshops): add Cast and Setting blocks to W001 (Phase 3 Item 3)` - inserts the two sections between title-block metadata and Phase 1.
5. `docs(narratives): codify per-row and consolidated bidirectional reference forms (Phase 3 Item 2)` - amends `docs/narratives/README.md` §"Bidirectional referencing" to name both forms with their defaults.
6. `docs: write Phase 3 retrospective` - the session-close retro.

This is one PR (`foundation-refresh/p3-conventions`), six commits, no code changes.

---

## 6. Acceptance criteria (Phase 3 close gate)

- [ ] All four workshops (W001, W002, W003, W004) carry a `Status` column or equivalent on every slice row, with values from the four-vocabulary set.
- [ ] W002, W003, W004 each carry a §3 Ubiquitous Language section with `Term | Definition | Notes` shape; cross-BC overlapping terms are noted.
- [ ] `docs/skills/event-modeling/SKILL.md` carries a new section naming Klefter, Bruun temporal-automation, and Bruun configuration-as-events patterns with CritterBids examples.
- [ ] W001 carries `## Cast` and `## Setting` sections; the narrative's Cast and Setting language is preserved verbatim where it overlaps; workshop-grain extensions are flagged as such.
- [ ] `docs/narratives/README.md` §"Bidirectional referencing" names both per-row and consolidated forms with their respective defaults.
- [ ] No file under `src/` or `tests/` was edited in this session.
- [ ] No new ADR, no new skill file, no new narrative.
- [ ] No em dashes in any committed prose authored by this session (handoff §13). Pre-existing em dashes on rows not edited remain per the grandfather clause.
- [ ] `docs/retrospectives/foundation-refresh-phase-3-retrospective.md` exists and mirrors the structure of Phases 1 and 2 retrospectives.
- [ ] Methodology-log entry written or conscious skip recorded in the Phase 3 retro.

---

## 7. Explicitly out of scope

- **Backfill narratives for W002, W003, W004.** Those narratives are Phase 5 Item 1 work. Phase 3 Item 2 codifies the bidirectional reference form so that Phase 5's narratives can adopt it; Phase 3 does not author the narratives themselves.
- **Layer 3 (code conventions) of the rules system.** Layer 2 (per-BC ubiquitous language) feeds from Item 4's W002-W004 §3 sections; Layer 3 is its own future session and does not run in Phase 3.
- **Editing lived code.** The no-code-refactoring rule (handoff §8 rule 6) applies. If Item 1's status assignment surfaces a slice whose status is ambiguous because of lived-code drift, flag in the retro; do not absorb.
- **New ADRs, new narratives, new skill files.** Phase 3 rolls out existing conventions; it does not introduce new ones. Phase 4 handles ADR-grade open questions.
- **W001 sub-tier reorganization or slice renumbering.** Item 1 adds a column; it does not reshape the existing tier structure or renumber slices.
- **Workshop scenario file edits (`001-scenarios.md` through `004-scenarios.md`).** Phase 3 edits the workshop docs themselves; the companion scenario files stay untouched. Scenario-level corrections belong to narrative passes (Phase 5) where they surface as `workshop-update` findings.
- **Methodology-log entry forced authorship.** Apply the entry-criteria gate per Phase 2's precedent. If no genuinely cross-cutting observation surfaces, note the conscious skip in the retro.
- **PARKED-QUESTIONS.md updates.** That file is its own discipline; Phase 3 does not retire or add parked questions.

---

## 8. Open questions to flag (not decide)

- **W001 Cast onstage/offstage rigidity at workshop grain.** Narrative 001 carries onstage/offstage per Moment. W001 covers 34 slices; per-Moment status does not apply. Item 3 needs to decide whether W001's Cast carries a single onstage/offstage status (likely "all onstage at some point in the journey") or omits the field entirely. Flag at Item 3 kickoff; user decides.
- **Item 1 status assignment for partial-shipped slices.** W001 slice 5.2 (Reserve met signal) is production-side shipped, view-side unshipped. The narrative's Finding 010 names the partial state. Likely status: `in progress`. Confirm; do not guess.
- **Item 4 cross-references between W002 and W004 for `Listing`.** The term lives in both BCs at different lifecycle stages (Selling owns draft-through-publish; Auctions owns published-through-close). Whether the §3 entries cross-reference each other or define the term in scope per BC is an authorship choice. Flag and propose; user decides.
- **Item 5 placement in the skill file.** The proposed insertion point is between "Structured Output Format for Slices" and "Output Artifacts". An alternate placement is as a sub-section of "CritterBids Integration". Flag and propose; user decides at item kickoff.
- **Methodology-log entry for Phase 3.** Phase 2 deferred Entry 001 to the Phase 5 backfill cohort. Phase 3 may or may not warrant its own entry. Apply the gate at session close; flag in the retro either way.

---

## 9. Starting move

When Phase 3 begins:

1. Read this document fully.
2. Re-read [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6 (the authoritative scope).
3. Skim [`foundation-refresh-phase-1-retrospective.md`](../../retrospectives/foundation-refresh-phase-1-retrospective.md) and [`foundation-refresh-phase-2-retrospective.md`](../../retrospectives/foundation-refresh-phase-2-retrospective.md) for cross-phase context (especially the Phase 2 deviations section that informs Item 2's reduced scope).
4. Confirm with the user:
   - Item ordering (default: 1 → 4 → 5 → 3 → 2).
   - Status vocabulary for slices that span partial-ship states (likely `in progress`).
   - Cross-BC `Listing` cross-reference shape in Item 4.
   - Item 5 placement in the skill file (default: new top-level "Adjunct patterns" section).
5. Begin Item 1. Do not start Item 4 until Item 1 is committed.

---

## 10. Memory and preferences inheritance

These preferences (from CritterBids' lived sessions and the foundation-refresh handoff §13) apply to this work:

- Depth over brevity when explaining trade-offs.
- Auction-domain ubiquitous language (Listing, Bidder, Seller, Auctioneer, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction).
- DDD / CQRS / Event Sourcing / EDA assumed background.
- Lean opinions on questions - propose a default with rationale rather than open-ended elicitation.
- No em dashes in any committed prose. Hyphens (regular `-`) and en dashes are fine; em dashes are out.
- Punchy prose; no AI-tool references in committed text.

---

## Document history

- **v0.1** (2026-04-27): Authored at Phase 2.5 close as the Phase 3 session prompt. References [`foundation-refresh-handoff.md`](./foundation-refresh-handoff.md) §6 as authoritative for scope; supplies execution shape (item ordering, commit sequence, current-state observations) per AUTHORING.md rule 3 (milestone doc authoritative; prompt references, does not duplicate). Records Phase 2's Item 2 deviation and the resulting reduction in Item 2's effective Phase 3 scope.
