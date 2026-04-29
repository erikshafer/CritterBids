# Prompt 002 - Author the Settlement-BC Backfill Narrative: Winner Clears Settlement

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-29 |
| **Phase** | Foundation Refresh, Phase 5, Item 1a |
| **Subdirectory** | `docs/prompts/narratives/` |
| **Journey** | The winner of a Flash auction is charged, the seller is paid out, and Settlement closes (happy path) |
| **Protagonist** | SwiftFerret42 (continuity with narrative 001) |
| **Target artifact** | `docs/narratives/002-winner-clears-settlement.md` (to be produced) |
| **Companion artifact** | `docs/narratives/002-findings.md` (to be produced; conscious-skip note acceptable if zero findings) |
| **Source-of-truth dependencies** | W003 workshop and `003-scenarios.md`; narrative 001 Moment 8 as the coarser companion. No lived Settlement BC code (M5 territory); no `CritterBids.Contracts.Settlement` namespace yet. |
| **Workflow position** | Second narrative authored under the NDD-informed regime; first of four Phase 5 backfill narratives; gates the Item 4 cutover by giving M5-S1 a narrative to cite. |

---

## Framing

This session authors the Settlement BC's first narrative. It is a deliberate companion to narrative 001 (`001-bidder-wins-flash-auction.md`): same protagonist (SwiftFerret42), same Flash auction, same hammer outcome, but at finer grain than narrative 001 Moment 8. Where narrative 001 collapsed the entire Settlement saga into a single bidder-visible beat with a multi-paragraph `Response.` block, narrative 002 dramatises each saga phase as its own Moment, with the narrator carrying weight when the bidder is offscreen.

Settlement is structurally unique among the four Phase 5 backfills. The other three (Participants, Selling, Auctions) are **lived-code audits**. Settlement is **forward-spec**: M1 through M4 have shipped, but Settlement BC has not. There is no `src/CritterBids.Settlement/` project today, no `CritterBids.Contracts.Settlement` namespace, no Wolverine handlers or Marten projections to read against the narrative. The audit surface is W003 (`003-settlement-bc-deep-dive.md`), `003-scenarios.md`, and the narrative-internal continuity check against narrative 001 Moment 8.

This forward-spec posture flips the findings-lane mix. Narratives authored against lived code expect a `code-update`-heavy distribution. Narrative 002 should expect `narrative-update`, `workshop-update`, and `document-as-intentional` to dominate; `code-update` is near-impossible because there is no Settlement code to be wrong about. If a `code-update` finding does surface, it almost certainly belongs to one of the other lived BCs (Auctions emitting `ListingSold`, Selling emitting `ListingPublished`, Relay broadcasting the final notification) and routes as a stub follow-up under `docs/prompts/implementations/` per the Phase 2.5 discipline.

Phase 5's framing places narrative 002 first in the four-narrative backfill sequence for two reasons: (1) the audit surface is smallest (no lived Settlement code), so the session settles patterns the three lived-BC narratives will inherit; (2) M5 is the Settlement BC milestone, and Item 4's cutover gate requires M5-S1's prompt to cite a narrative as jointly authoritative scope. Narrative 002 is that narrative. Until it ships with `status: accepted`, the cutover cannot land.

ADR 016 (Spec-Anchored Development) governs the relationship: specs describe intent; code is authoritative for runtime; drift is caught at retrospective time. Narrative 002's lived audit deferrals are appropriate per ADR 016 because there is no runtime to be authoritative against yet. The narrative renders the journey as the BC is *designed* to run; lived-code audit will happen Moment-by-Moment in the M5 slices that ship Settlement.

---

## Goal

Author the Settlement BC's backfill narrative covering SwiftFerret42's experience as the saga charges her credit, calculates the platform fee, pays GreyOwl12 the seller payout, and emits `SettlementCompleted`. Audit W003 and `003-scenarios.md` against the narrative as drafted, route every disagreement through the four-lane findings discipline, surface the W003 storage-layer staleness against ADR 011 (All-Marten Pivot), and add per-row narrative back-references on the W003 slices the narrative implements.

---

## Orientation files (read in order before starting)

1. `C:\Code\CritterBids\CLAUDE.md` - routing layer and global conventions. Pay attention to the All-Marten Pivot (ADR 011) reference, the `[AllowAnonymous]` posture through M6, and the no-em-dashes preference.
2. `C:\Code\CritterBids\docs\narratives\README.md` - format manual v0.1 (Phase 1 Item 3). Bounded frontmatter v1, prose-paragraph Moments, seven disposition tags, multi-slice convention, per-row vs consolidated bidirectional referencing.
3. `C:\Code\CritterBids\docs\narratives\001-bidder-wins-flash-auction.md` - especially Moment 8 (slice 6.1) and the narrative-internal retrospective. This is the coarser-grain companion narrative 002 zooms into.
4. `C:\Code\CritterBids\docs\workshops\003-settlement-bc-deep-dive.md` - the workshop the narrative implements. Phase 1 (Brain Dump) Parts 1 through 6 carry the architectural framing. Phase 1 Part 2 (Wolverine Saga vs `ProcessManager<TState>`) is hosting-detail and stays out of narrative body per `narratives/README.md` "What narratives carry, and don't".
5. `C:\Code\CritterBids\docs\workshops\003-scenarios.md` - 41 Given/When/Then scenarios across nine sections (decider, evolver, projection, workflow integration). Reference, do not restate. Sections 1, 2, 3, 4, 5, 6 cover the happy-path decider grain narrative 002 dramatises.
6. `C:\Code\CritterBids\docs\decisions\011-all-marten-pivot.md` (or its successor; the Status Ledger in `docs/decisions/README.md` is canonical) - the storage decision that supersedes W003's Polecat/SQL Server framing.
7. `C:\Code\CritterBids\docs\prompts\foundation\foundation-refresh-phase-5.md` §3.2 - the Phase 5 acceptance gate this prompt executes against.

Per-Moment reads remain session-time work. For the Settlement narrative specifically, the per-Moment reads are not lived-code reads (there is none); they are W003 Phase 1 Part references and `003-scenarios.md` section references for the saga phase the Moment dramatises.

The orientation list is finite (seven items) per AUTHORING.md rule 9.

---

## Working pattern

Same interactive cadence as Phase 2 (narrative 001) and the M3-S5b session. Carries one structural adaptation for Settlement's forward-spec posture: the per-Moment "lived code path" reading step is replaced with "W003 phase reference and matching scenarios section."

- **Cast and Setting first.** Propose protagonist continuity (SwiftFerret42 carrying from narrative 001's terminal state), supporting actors with onstage/offstage status (GreyOwl12 the seller, Settlement saga handler, Relay's BiddingHub, the Operations dashboard if it appears at all), and Setting (time, place, policy posture, inherited conditions including the Listings Auctions Settlement boundary already crossed by `ListingSold`). Sign-off before any Moment.
- **Moment-by-Moment thereafter.** For each Moment:
  1. Read the implementing slice from W001 (slice 6.1 is the spine; 6.3 enters in the seller-payout Moment if Relay broadcast is dramatised) and the matching W003 Phase 1 Part plus `003-scenarios.md` section.
  2. Acknowledge the lived-code absence. The narrative renders the journey as W003 designs it; the audit lane for Settlement-internal events is W003 staleness, not code drift.
  3. Draft the Moment in the README's Guardrail-1 shape: prose paragraphs labeled `Context.`, `Interaction.`, `Response.`, and optionally `Why this matters to the bidder.`. When the Moment is offstage from the bidder's window, `Why this matters to the bidder.` becomes load-bearing because it tells the reader what the saga's invisible work is doing for SwiftFerret42's journey arc.
  4. Identify findings as the draft is written. Surface them per the Findings discipline below before sign-off.
  5. Sign-off, commit.
- **Narrator-led Moments are expected.** Settlement's bidder-visible beats are at most the charge (her credit ceiling drops) and the final confirmation. The reserve check, the fee calculation, and the seller payout happen offscreen. The narrator's omniscience carries those Moments; the protagonist remains the dramatic anchor through inheritance from narrative 001's Moment 7 terminal state.
- **Per-Moment "deliberately not included" subsection.** Each Moment closes with a short list of what was consciously omitted, tagged with one of the seven disposition tags. They consolidate into `## Deferred from this narrative` at session close.

Do not batch the whole narrative into one output. Bullets are not allowed inside a Moment body (Guardrail 1). Frontmatter keys are bounded by README v1 (Guardrail 2).

---

## Voice and perspective

**Single-named-protagonist plus omniscient narrator** is locked by `docs/narratives/README.md` v0.1 and inherited from narrative 001.

The protagonist is SwiftFerret42. Other actors (GreyOwl12 the seller, the Settlement saga handler, Relay's BiddingHub, the Operations dashboard, downstream BCs) appear in Cast with onstage or offstage status. Multi-perspective and parallel approaches remain out of scope.

Narrative 002's twist: the narrator does more work than in narrative 001. Settlement's saga runs five intermediate events between the bidder-visible bookends ("You Won" inherited from narrative 001's Moment 7; "Charged $X. The keyboard is yours." closing this narrative's last Moment). The narrator dramatises the intermediate phases at the saga grain even when SwiftFerret42 perceives nothing. This is what permits per-phase Moments without violating the journey-voice constraint: the bidder is the dramatic anchor, the narrator is the camera that pans to the saga state when the bidder's window is dark.

---

## Findings discipline (forward-spec twist)

The four-lane discipline from narrative 001 carries forward unchanged in shape. The expected lane mix shifts because Settlement is unshipped:

| Lane | Meaning | Expected frequency in narrative 002 |
|---|---|---|
| `narrative-update` | Code or workshop is right; the narrative renders something inaccurate. | Moderate. The narrative is being authored fresh; first-pass drafts will need correction against W003. |
| `workshop-update` | Workshop is stale (event renamed, payload grew, slice intent shifted, storage decision superseded). | High. W003 was authored before ADR 011's All-Marten Pivot; Polecat and SQL Server references are stale-by-decision. |
| `code-update` | Code is wrong relative to domain understanding. | Near-zero for Settlement. The Settlement BC is unshipped; there is no code to be wrong. Findings that surface here probably belong to upstream BCs (Auctions' `ListingSold` shape, Selling's `ListingPublished` payload, Relay's broadcast contract) and route as Phase 2.5 stub follow-ups. |
| `document-as-intentional` | Workshop and narrative are both right; the apparent disagreement is two valid expressions of the domain. | Moderate. W003 is precise about implementation choices (Saga vs `ProcessManager<TState>`, deterministic SettlementId via UUID v5, BIN settlement skipping reserve check); the narrative may render journey-grain language that reads as drift but is the same intent at a different grain. |

**Code refactors do not happen in this session.** The narrative session writes the narrative, classifies findings, routes them. Any `code-update` finding (most likely on a non-Settlement BC) gets a stub follow-up prompt under `docs/prompts/implementations/` per the Phase 2.5 discipline. Settlement BC code is M5 work and out of scope for Phase 5 entirely.

### Findings file shape

Same schema as narrative 001's findings file. Per foundation-refresh handoff §4.4:

```
### Finding NNN - <one-line title>

**Routing:** narrative-update | workshop-update | code-update | document-as-intentional

**Surfaced at:** Moment X | per-Moment proposal | session close

**Discrepancy.** What disagrees with what. Cite the workshop slice or section,
the narrative Moment that surfaced it, and (for cross-BC code-update findings)
the affected code path.

**Resolution.** What was done in this PR (for narrative-update,
workshop-update, document-as-intentional). For code-update: the path to the
stub follow-up prompt under docs/prompts/implementations/.
```

A conscious-skip note in the narrative-internal retro suffices if zero findings surface. Given the W003 storage staleness alone, zero findings is unlikely.

### Heads-up sources of likely findings

Do not pre-decide outcomes. Be ready when these come up:

1. **W003 Polecat and SQL Server references against ADR 011 (All-Marten Pivot).** W003's "Storage: SQL Server via Polecat" framing in §"What Prior Workshops Established", the `PendingSettlement` "Polecat document projection" reference in Phase 1 Part 1, and the "Polecat-backed Financial Event Stream" framing in §"Ubiquitous Language" all predate the All-Marten Pivot. Route as `workshop-update`. Phase 5's no-em-dash audit-after-write applies to W003 edits.
2. **`ProcessManager<TState>` vs Wolverine Saga hosting choice.** W003 Phase 1 Part 2 carries an extended comparison and explicitly defers the choice. The narrative renders the journey, not the framework: any phrase that names a hosting primitive is a smell. Route as `implementation-detail` deferral if it surfaces in proposal; route as `narrative-update` if it slips into a draft Moment.
3. **Reserve-check authority duplication with narrative 001 Moment 8.** Narrative 001 already established that Settlement is the financially authoritative reserve check; Auctions' `ReserveMet` is a UX signal only. Narrative 002 must render this consistently (the seven-phase workflow's `ReserveCheckCompleted` is the binding decision). Disagreements with narrative 001 surface as `narrative-update` against narrative 001 in narrative 002's PR per Phase 5 §7's "no re-authoring narrative 001" constraint.
4. **`BuyItNowPurchased` settlement entry path.** W003 Phase 1 Part 5 specifies that BIN settlements branch directly into `ReserveChecked(WasMet: true)` to skip the reserve comparison. Narrative 002 is bidding-source happy path; BIN is a `separate-narrative` deferral. Surface explicitly so the deferred-section bucket is unambiguous.
5. **Settlement's `ReserveCheckCompleted` event payload.** W003 Phase 1 names the event with `WasMet: true | false`; narrative 001 Moment 8 names it with `Result: "Met"`. One of the two is stale relative to `003-scenarios.md` Section 2's authoritative payload shape. Reconcile against the scenarios; route the loser as `narrative-update` (against narrative 001) or `workshop-update` (against W003 Phase 1's prose).
6. **Cross-BC integration event continuity.** `ListingSold` (Auctions out, Settlement in), `ListingPublished` (Selling out, Settlement in for the `PendingSettlement` projection seed), `SettlementCompleted` (Settlement out). Each is the boundary across which narrative 002 inherits state from upstream BCs. Audit the contract namespace under `src/CritterBids.Contracts/` (Auctions and Selling subfolders exist; no Settlement subfolder yet) for payload-shape continuity. Cross-BC payload drift routes as `code-update` against the producing BC, not Settlement.
7. **Relay broadcast on `SettlementCompleted` (W001 slice 6.3).** Narrative 001 Moment 8 dramatises this beat at coarse grain. Narrative 002's final Moment may dramatise it at finer grain. Slice 6.3 is P1 and unshipped; route any cross-BC implementation gap as `defer` deferral, not `code-update`.

---

## Cross-reference discipline

- Each Moment cites its slice or slices via `**Implements:** slice X.Y[, slice X.Z, ...].`. Narrative 002's spine is W001 slice 6.1 with optional 6.3 in the closing Moment. W003's per-section scenarios (`003-scenarios.md` §1 through §6) are the GWT spec; the narrative cites the workshop slice number, not the scenario number.
- Domain event names render in code-style backticks: `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`, `SellerPayoutIssued`, `SettlementCompleted`, `ListingSold`, `ListingPublished`, `BuyItNowPurchased`, `PaymentFailed`. Plain text for ordinary nouns from the Ubiquitous Language: Settlement, Settlement Workflow, Reserve Check, Winner Charge, Final Value Fee, Seller Payout, PendingSettlement, Hammer Price, Credit Ceiling, Financial Event Stream.
- Do not restate the Given/When/Then content from `003-scenarios.md`. Reference the W003 section number and the W001 slice number; the workshop carries the test specification, the narrative carries the journey.
- W003 carries no `Narratives:` back-references today (parallel to W001 pre-Phase-2 and W002, W004 pre-Phase-3). Phase 3 Item 2 established per-row form as the default for BC-focused workshops citing one to three slices. Narrative 002 implements one or two W001 slices, so per-row form is correct. Each cited slice in W003 carries `Narratives: [002-winner-clears-settlement]` inline. Add only on the slices the narrative directly implements; broader W003 backfill is not in this session's scope.

---

## What the narrative does NOT carry

- **No code or pseudocode.**
- **No implementation choices.** Wolverine Saga vs `ProcessManager<TState>` is W003 Phase 1 Part 2 territory and stays in the workshop; UUID v5 derivation for `SettlementId` is workshop-grain; PendingSettlement projection mechanism is skill-file territory. The narrative renders the journey.
- **No architectural decisions.** Flag any that surface during authoring as ADR candidates and capture them in the narrative's Deferred section. Do not resolve in-narrative. The Reserve Check Authority parked question (W001 Q5, partially resolved in W002, completed in W003) is already-decided ground; do not re-open.
- **No GWT test specifications.** Reference `003-scenarios.md` section numbers and W001 slice numbers; do not restate.
- **No UX or UI design.** Narrate at the bidder-experience grain ("the credit-ceiling display ticks down to..."); do not design the screens.
- **No re-authoring of narrative 001.** Narrative 001 is `status: accepted`. If narrative 002 surfaces a contradiction with narrative 001, route as `narrative-update` against narrative 001 in narrative 002's PR per Phase 5 §7 (single edit-then-cite is acceptable; structural rewrite is not).

---

## In scope (proposed Moment list)

| Moment | Slice(s) from W001 | W003 reference | Bidder experience |
|---|---|---|---|
| 1 | 6.1 | Phase 1 Part 1 (PendingSettlement seed); §1 of `003-scenarios.md` | "You Won" banner inherited from narrative 001 Moment 7. Settlement consumes `ListingSold`, opens the SettlementId stream, loads the cached PendingSettlement seeded from `ListingPublished`. SwiftFerret42 sees nothing change yet; the saga is below her perception. |
| 2 | 6.1 (continuation) | Phase 1 Part 2 (workflow phase 1); §2 of `003-scenarios.md` | The reserve check fires. Settlement compares hammer price ($55) to reserve ($50), emits `ReserveCheckCompleted { WasMet: true }`. Bidder offscreen; narrator renders the binding nature of this check (Settlement is the financial authority; Auctions' `ReserveMet` was the UX promise). |
| 3 | 6.1 (continuation) | Phase 1 Part 2 (workflow phase 2); §3 of `003-scenarios.md` | The winner charge. Settlement debits SwiftFerret42's credit ledger by $55, emits `WinnerCharged`. **First bidder-visible beat:** her credit-ceiling display ticks down from $500 to $445. The "You Won" banner ticks forward to "Charged $55.00." |
| 4 | 6.1 (continuation) | Phase 1 Part 2 (workflow phases 3 and 4); §4 and §5 of `003-scenarios.md` | The fee calculation and seller payout. Platform fee = $5.50 (10% of $55), seller payout = $49.50. `FinalValueFeeCalculated` and `SellerPayoutIssued` emitted in sequence. Bidder offscreen; narrator names GreyOwl12's payout as the seller-side outcome the journey is closing for. |
| 5 | 6.1 (continuation), optionally 6.3 | Phase 1 Part 2 (workflow phase 5); §6 of `003-scenarios.md` | Settlement completes. `SettlementCompleted` emitted, saga document removed, financial event stream closed at terminal state. Relay broadcasts `SettlementCompleted` to SwiftFerret42's connection. **Final bidder-visible beat:** banner ticks forward to "Charged $55.00 to your credit. The keyboard is yours." Journey arc closes. |

Five Moments. Bookended by bidder-visible beats (Moments 1 and 5 perceived through the inherited "You Won" banner and the closing confirmation; Moment 3 perceived through the credit-ledger update); narrator-led through the offscreen interior (Moments 2, 4 mostly).

Alternative groupings are defensible. Three flagged at session start:

1. **Collapse fee + payout into Moment 3's tail** to put the charge, fee, and payout in a single Moment with a multi-paragraph `Response.`. Argument: matches narrative 001's Moment 8 pacing and avoids a fully-narrator Moment 4. Counter-argument: the Settlement narrative's whole purpose is finer grain than narrative 001 Moment 8; collapsing the interior re-collapses what the narrative is here to dramatise.
2. **Split Moment 1 into "Settlement consumes `ListingSold`" plus "PendingSettlement is loaded".** Argument: each is a distinct decider invocation per `003-scenarios.md`. Counter-argument: from the bidder's window both happen instantly; the saga grain is the narrator's territory but does not need its own Moment when no bidder beat marks it.
3. **Split Moment 5 to give the Relay broadcast (slice 6.3) its own Moment.** Argument: 6.3 is a different W001 slice from 6.1. Counter-argument: 6.3 is the bidder-perceived effect of `SettlementCompleted` (slice 6.1's terminal event); the perception does not warrant its own Moment when it lands inside the same protagonist-visible beat.

Lean on the proposed five-Moment list at session start; sign-off Cast and Setting before debating Moment splits.

---

## Out of scope for this session

- **Buy-It-Now settlement path (slice 6.2 P1).** W003 Phase 1 Part 5 spec is forward-looking; structurally distinct entry. Disposition: `separate-narrative`.
- **Failure paths.** `PaymentFailed` (insufficient credit, payment-provider rejection), reserve-not-met (sale-fails branch), ledger-divergence, BIN price below reserve. Each is a `separate-narrative` or `alternate-path-failure` deferral per the seven-tag discipline.
- **Seller-perspective on settlement.** GreyOwl12's experience as the seller receiving the payout notification is a candidate for narrative 004 (Selling BC backfill) or a future seller-perspective narrative; not in scope here. Disposition: `separate-narrative`.
- **Operator-perspective on settlement.** Operations dashboard surfacing settlement state, payment-failure routing to operator queues. Disposition: `separate-narrative` for any future operator-perspective work.
- **Wolverine Saga vs `ProcessManager<TState>` hosting.** W003 Phase 1 Part 2 territory; `implementation-detail` deferral if it surfaces.
- **Compensation paths.** W003 Phase 1 Part 3 explicitly defers compensation design beyond MVP. The narrative does not dramatise compensation or rollback; if a Moment seems to demand it, the demand itself is the finding (likely `narrative-update` to keep the Moment in MVP scope).
- **Settlement-from-`BuyItNowPurchased` path.** Same as slice 6.2 above; calling out separately because W003 Phase 1 Part 5 is its dedicated section.
- **Settlement code refactoring.** No Settlement code exists; nothing to refactor. Cross-BC `code-update` findings (Auctions, Selling, Relay) get stub follow-up prompts; the slices run in subsequent product work, not in Phase 5.
- **W003 broad backfill of narrative back-references.** Only the directly-implemented W001 slice (6.1, optionally 6.3) gets a `Narratives:` line on W003. Other W003 slices wait for their own dedicated narrative.
- **Narrative #3 candidate selection.** Phase 5 prompt §2.3 already commits to the four-narrative ordering; this session does not re-open it.
- **Methodology format changes.** The narratives README v0.1 dialect remains locked. Format issues that surface go to the narrative-internal retro, not into mid-session edits of the README.
- **Phase 5 cross-narrative retrospective.** Aggregates per-narrative observations at Item 4; this session writes only the narrative-internal retro inside `002-winner-clears-settlement.md`.

If any of the above gets pulled in opportunistically, surface the scope expansion explicitly before doing the work, and reflect it in the narrative-internal retrospective.

---

## Deliverable plan

Per Phase 5 prompt §3.2 acceptance gates:

1. **Narrative file** at `docs/narratives/002-winner-clears-settlement.md`. Frontmatter v1, prose-paragraph Moments, single-named-protagonist voice. `status: accepted` at session close.
2. **Findings file** at `docs/narratives/002-findings.md`, OR a conscious-skip note in the narrative-internal retro if zero findings surface. The W003 storage staleness alone makes a zero-findings outcome unlikely.
3. **Stub follow-up prompts** at `docs/prompts/implementations/<slug>.md`, one per cross-BC `code-update` finding (Auctions, Selling, Relay surface area). Each stub names the finding number, the affected code path, and the proposed slice scope. Settlement-internal `code-update` is structurally near-impossible (no code yet); zero stubs is the expected outcome.
4. **Narratives README Index update** in `docs/narratives/README.md`. Row 002 added with status, journey, scope, slices.
5. **W003 cross-references.** `Narratives: [002-winner-clears-settlement]` added per-row to the W001 slice 6.1 entry inside W003 (and 6.3 if the closing Moment dramatises Relay broadcast). Per-row form per Phase 3 Item 2's BC-workshop default.
6. **W003 storage-layer corrections.** If the narrative session surfaces W003's Polecat/SQL Server staleness as a `workshop-update` finding (highly likely), the correction lands in the same PR. Em-dash audit on every edited file.
7. **Methodology log Entry 001 candidate.** The four backfill narratives are the lived chance for Entry 001 per the Phase 4 retro time-box. Apply the entry-criteria gate: a genuinely cross-cutting observation about narrative authoring against forward-spec workshops warrants the entry. Conscious-skip note in the narrative-internal retro is acceptable.
8. **Narrative-internal retrospective** appended in the narrative file after `## Deferred from this narrative`, mirroring narrative 001's `## Retrospective` shape.

---

## Acceptance criteria

- [ ] `docs/narratives/002-winner-clears-settlement.md` exists. Frontmatter conforms to v1 vocabulary. `status: accepted`.
- [ ] Every Moment cites its W001 slice via `Implements:`.
- [ ] No bulleted lists appear inside any Moment body (Guardrail 1).
- [ ] No frontmatter keys outside the v1 vocabulary (Guardrail 2).
- [ ] Each Moment has a `Context.`, `Interaction.`, `Response.` body. `Why this matters to the bidder.` is present where it adds meaning, absent otherwise. Narrator-led Moments use `Why this matters to the bidder.` to anchor the bidder's journey arc when the saga state is offstage.
- [ ] `## Deferred from this narrative` exists. Items are bucketed by the seven disposition tags from `docs/narratives/README.md` v0.1.
- [ ] `docs/narratives/002-findings.md` exists with at least one finding (W003 storage staleness alone is expected to surface), OR the narrative-internal retro contains an explicit conscious-skip note with rationale.
- [ ] Each cross-BC `code-update` finding (if any) has a stub follow-up prompt at `docs/prompts/implementations/<slug>.md`.
- [ ] `docs/narratives/README.md` Index table contains row 002.
- [ ] W001 slice 6.1 (and 6.3 if the Relay broadcast Moment is included) carries `Narratives: [002-winner-clears-settlement]`. Note: W001's narrative back-references use the consolidated form per Phase 3 Item 2; the entry extends the existing block.
- [ ] W003 carries per-row `Narratives: [002-winner-clears-settlement]` on the slice or section the narrative implements.
- [ ] `docs/research/methodology-log.md` has Entry 001, OR the narrative-internal retro names the conscious skip with rationale.
- [ ] Narrative-internal retro appended after `## Deferred from this narrative`, mirroring narrative 001's structure.
- [ ] No file under `src/` or `tests/` was edited.
- [ ] No em dashes in any committed prose authored by this session. Audit-after-write per file before staging, per Phase 3 retro Key Learning 4.

---

## Open questions to flag (not decide)

These are session-start decisions; surface them and ask the user before locking Cast and Setting.

- **Five-Moment vs three-Moment grain.** §"In scope" proposes five Moments. The collapse-into-three alternative (Initiation, Charge-with-Reserve-and-Fee-and-Payout, Completion) matches narrative 001 Moment 8's pacing but defeats the finer-grain purpose of this narrative. Lean: keep the five-Moment proposal. The whole point of narrative 002 is to dramatise what narrative 001 collapsed.
- **Closing Moment scope: include Relay broadcast (slice 6.3) or defer.** The bidder's final perceived beat is the broadcast-driven banner update. If 6.3 is in scope, the closing Moment cites both 6.1 and 6.3. If deferred, the closing Moment cites only 6.1 and the broadcast goes to `## Deferred` under `separate-narrative` or `defer`. Lean: include in 6.1's response paragraph; cite as multi-slice. The broadcast is the bidder's perception of `SettlementCompleted` and lives inside the same beat.
- **Reserve-check `WasMet: true | false` vs `Result: "Met"` payload reconciliation.** Narrative 001 Moment 8 uses `Result: "Met"`; W003 Phase 1 and `003-scenarios.md` §2 use `WasMet: true | false`. The scenarios file is closer to test-spec and probably authoritative; narrative 001 may be the loser. Decide at Moment 2; route the loser as `narrative-update` (against narrative 001) or as `workshop-update` (against W003 Phase 1 prose).
- **W003 storage-layer correction scope.** Polecat and SQL Server are out per ADR 011. The minimum correction is the Phase 1 Part 1 PendingSettlement framing and the Ubiquitous Language Financial Event Stream entry. The maximum correction sweeps every Polecat reference in W003 (Phase 2 storytelling, Phase 3 scenarios cross-references, Ubiquitous Language definitions). Lean: minimum-correction on this PR (only the slices the narrative directly implements); broader sweep is its own follow-up. Surface the wider drift in the findings file.
- **Cross-BC payload audit depth.** `ListingSold` (Auctions) and `ListingPublished` (Selling) are the upstream contracts the narrative inherits. Audit: read the contract files in `src/CritterBids.Contracts/Auctions/` and `src/CritterBids.Contracts/Selling/`, compare against W003's expected payload, route any drift. Depth: confirm that the payload fields the narrative cites (`ListingId`, `WinnerId`, `SellerId`, `HammerPrice`, `BidCount` for `ListingSold`; reserve, fee percentage, BIN price for `ListingPublished`) exist in the lived contract. Drift routes as `code-update` against the producing BC, not `workshop-update`.
- **Narrative 001 Moment 8 continuity edits.** If reserve-payload reconciliation routes a `narrative-update` against narrative 001, the edit is a single-paragraph correction inside Moment 8's `Response.` block. Phase 5 §7 permits cite-and-edit but not structural rewrite; confirm the edit stays under one paragraph.

---

## Memory inheritance

Phase 1 and Phase 2 session memories apply unchanged:

- **Depth over brevity** when explaining tradeoffs.
- **Ubiquitous language** (auction-domain, Settlement-flavored): Listing, Bidder, Seller, Auctioneer, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction, Bidder Session, Credit Ceiling, Settlement, Settlement Workflow, PendingSettlement, Reserve Check, Winner Charge, Final Value Fee, Seller Payout, Financial Event Stream.
- **DDD, CQRS, Event Sourcing, EDA** assumed background.
- **SDD and NDD methodology vocabulary is NOT assumed background.** Define on first use: spec-anchored development, narrative-driven development, the findings discipline, the seven disposition tags, the Cast / Setting / Moment primitives, the Klefter pattern, the Bruun pattern, the Ralph Loop. Point at the foundation-refresh handoff §10 glossary, ADR 016, ADR 017, and the narratives README as durable references.
- **Lean opinions on questions.** Propose a default with rationale rather than open-ended elicitation.
- **No em dashes in any committed prose.** Hyphens (`-`) and en dashes are fine; em dashes (`U+2014`) are out. The narrative file is committed prose; this prompt is committed prose; the W003 corrections are committed prose; the retros are committed prose.
- **Punchy prose; no AI-tool references in committed text.**
- **No `git push` to `main`** without explicit authorization. Commit freely on the narrative-002 branch; push only when asked.

---

## Starting move

When the session begins:

1. Re-read this prompt and `docs/narratives/README.md` v0.1 in full.
2. Re-read narrative 001 Moment 8 specifically; it is the coarser-grain companion this narrative refines.
3. Confirm with user: five-Moment grain (vs three), closing Moment slice scope (include 6.3 or defer), W003 correction scope (minimum vs broader sweep). Lock these before drafting Cast.
4. Propose Cast and Setting. Sign-off. Commit.
5. Walk Moment-by-Moment per the working pattern. For each Moment: read the implementing W001 slice, read the matching W003 Phase 1 Part and `003-scenarios.md` section, draft the Moment, surface findings, sign-off, commit.
6. At session close: classify all findings, write any cross-BC `code-update` stub prompts, update the narratives README Index, add per-row W003 narrative back-references, apply minimum-scope W003 storage corrections, evaluate methodology-log Entry 001 (or document the skip in the narrative-internal retro), append the narrative-internal retro, flip the narrative's `status:` to `accepted`.

---

## Document history

- **v0.1** (2026-04-29): Authored as foundation-refresh Phase 5 Item 1a session prompt. Adapts the Phase 2 narrative-001 prompt template (`docs/prompts/narratives/001-bidder-wins-flash-auction.md`) to Settlement BC's forward-spec posture: orientation files swap lived-code reads for W003 phase references and `003-scenarios.md` sections; "Heads-up sources of likely findings" reframes the lane mix away from `code-update` (no Settlement code yet) toward `workshop-update` (W003 storage staleness against ADR 011) and cross-BC payload audits. Five-Moment proposal at finer grain than narrative 001 Moment 8 is the principal departure. AUTHORING.md rule 3's joint-authority clause (added in PR #18 as Phase 5 Items 2+3 amendments) governs the session's relationship to W003 and the M5 milestone doc.
