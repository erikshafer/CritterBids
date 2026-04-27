# Foundation Refresh Phase 2: Retrospective

**Date:** 2026-04-27
**Phase:** Phase 2 (First narrative session) of the foundation refresh
**Prompt:** `docs/prompts/narratives/001-bidder-wins-flash-auction.md`
**Branch:** `foundation-refresh/p2-narrative-001`
**Narrative produced:** `docs/narratives/001-bidder-wins-flash-auction.md` (status: accepted)

## What landed

Eleven commits on the Phase 2 branch ahead of `origin/main`. Each Moment plus session-close artifact landed as its own commit for review-ability.

| Commit | Description |
|---|---|
| `9647fc4` | Cast and Setting: SwiftFerret42 protagonist, BoldPenguin7 competing bidder, GreyOwl12 seller, ten-actor cast, three-paragraph Setting with canonical numbers (reserve $50, hammer $55, fee 10%, credit ceiling $500). |
| `97a399b` | Moment 1 (anonymous session start) + Findings 001, 002. Setting credit-ceiling band patched (Finding 001); workshop slice 0.2 uniqueness scenario reframed (Finding 002). |
| `4ca010f` | Moment 2 (catalog browse + listing detail) + Findings 003, 004, 005. W001 view inventory edited (Finding 003); workshop scenarios `HasReserve` claim and reserve Note rewritten (Finding 004); status vocabulary across seven scenario blocks updated to PascalCase matching lived `CatalogListingView` (Finding 005). |
| `7ea8055` | Moment 3 (forward-spec Flash session-start cascade) + Findings 006, 007. W001 milestone mapping table edited and re-named M3 row from "Flash Session Core" to "Auctions Core" (Finding 006); narrative intro paragraph forward-spec count corrected from "Two of the eight" to "Three of the eight" (Finding 007). |
| `f4dd0a2` | Moment 4 (place a bid via DCB) + Findings 008, 009. Both `document-as-intentional`: `BuyItNowOptionRemoved` atomic with `BidPlaced` (Finding 008); M3 transitional `CreditCeiling` on the command (Finding 009). |
| `5b1dbee` | Moment 5 (BiddingHub echo + targeted Outbid push). Forward-spec; Relay BC unshipped. No findings. |
| `816f561` | Moment 6 (re-bid plus extended bidding) + Finding 010. W001 slice 5.2 partial-shipment Note added below Tier 5 table (Finding 010). |
| `2a0a0c6` | Moment 7 (gavel falls; `ListingSold`) + Findings 011, 012. Finding 011 (`code-update`): `TryComputeExtension` produces non-monotone reschedules. Finding 012 (`document-as-intentional`): saga reads `SellerId` via `AggregateStreamAsync<Listing>` at close. |
| `3e57ed6` | Moment 8 (settlement saga; forward-spec). Settlement BC unshipped. No findings. |
| `3492926` | Session close A: status flipped to accepted; cumulative `## Deferred from this narrative` section bucketed by the seven disposition tags; narrative-internal retrospective appended; Document History block. |
| `eb54594` | Session close B: `docs/narratives/README.md` Index row 001; W001 consolidated Narrative Cross-References note. |
| `6090c97` | Session close C: `docs/prompts/implementations/phase2-5-extension-calculation-fix.md` stub for Finding 011. |

This retrospective is the twelfth commit on the branch.

## Findings summary

Twelve findings filed in `docs/narratives/001-findings.md` across four routing lanes:

| Lane | Count | Findings |
|---|---|---|
| `narrative-update` | 2 | 001 (Setting credit-ceiling band drifted), 007 (intro forward-spec count understated) |
| `workshop-update` | 5 | 002 (slice 0.2 uniqueness asserted hard, code probabilistic), 003 (`ListingDetailView` collapsed into `CatalogListingView` per M3-S6), 005 (status vocabulary lowercase in workshop, PascalCase in lived code), 006 (M3 milestone scope said Tiers 2+3, actual was Tier 3 plus Auctions foundation), 010 (slice 5.2 production-side fully shipped despite P1 marker) |
| `code-update` | 1 | 011 (`TryComputeExtension` non-monotone reschedule) |
| `document-as-intentional` | 4 | 004 (`HasReserve` invisible-until-met design), 008 (`BuyItNowOptionRemoved` atomic with `BidPlaced` first-bid), 009 (M3 transitional `CreditCeiling` on `PlaceBid` command), 012 (saga reads `SellerId` via `AggregateStreamAsync` at close) |

## Items folded in beyond the original scope

**Status vocabulary sweep across seven scenario blocks (Finding 005).** Originally a single Moment 2 finding about the slice 1.2 view block's `"upcoming"` status; the discrepancy turned out to span slices 1.2, 1.3, 1.4, 2.2, 2.3, 3.3, and 3.4. Resolved with four `replace_all` edits on `001-scenarios.md` covering all eight occurrences of lowercase status values, plus one targeted edit for the slice 2.3 `Status → "open"` arrow form. Folded into Moment 2's commit because the resolution was mechanical and atomic with the rest of Moment 2's workshop edits.

**W001 milestone-mapping table em-dash sweep (Finding 006 resolution).** The mapping table's M3 and M4 rows were already being rewritten to clarify the Flash session aggregate scope; the existing em-dash separators between the milestone label and the milestone name (e.g. between `M3` and `Auctions Core`) were swept to hyphens to keep the table internally consistent with the new no-em-dash convention. Pre-existing em dashes on rows not being edited (M1, M2, M5, M6, M7) were also swept as an opportunistic cleanup since the table was already under edit. Documented in Finding 006's Resolution section.

**Phase 2 narrative target asymmetry from prompt v0.1 to execution.** The narrative-authoring prompt at `docs/prompts/narratives/001-bidder-wins-flash-auction.md` was authored 2026-04-26; this session executed 2026-04-27. No code changes between authoring and execution; the asymmetry was just-in-time orientation reads (per-Moment retro consultations) rather than a scope drift.

## Phase 2.5 disposition

**Phase 2.5 has scope.** One `code-update` finding (Finding 011) requires Phase 2.5 absorption. The stub follow-up prompt landed at `docs/prompts/implementations/phase2-5-extension-calculation-fix.md` in commit `6090c97`. Phase 2.5 fleshes the stub into a full implementation prompt at its kickoff session.

**No M3-S6 re-prompt needed.** The foundation-refresh handoff §4.5 originally named M3-S6 as a candidate Phase 2.5 re-prompt subject if the narrative reshaped its scope. M3-S6 shipped before Phase 2 began; this session audited it as lived code and surfaced two findings (003 view-inventory unification; 005 status-vocabulary sweep) - both `workshop-update`, both resolved in their respective Moment commits. M3-S6 needs no re-prompt.

## Methodology log Entry 001 conscious skip

Per the foundation-refresh handoff §4.7, methodology log Entry 001 was considered at session close and consciously skipped. The 5-`workshop-update`-to-1-`code-update` finding-lane ratio is interesting but the project has authored exactly one narrative session; one data point is not a load-bearing observation about drift accumulation. Defer Entry 001 to narrative #2's close (the Auctions-BC backfill per Phase 5 Item 1), when a comparison ratio becomes available and the entry-criteria gate's "predicts something about how the methodology will or should evolve" requirement has more to lean on. The methodology log's entry-criteria gate held; silence is fine.

## Open questions surfaced during Phase 2

**M4-S2 retrospective absence (operational, not load-bearing).** Commit `d5b6a76` shipped the WithdrawListing implementation without a retrospective in `docs/retrospectives/`. WithdrawListing is a seller-perspective flow not on this narrative's bidder happy path; M4-S2's retro absence did not block any Moment's audit. Worth noting as a project-grade cleanup item: the prompt-then-retro discipline is normally enforced; M4-S2 broke the pattern. Not Phase 2's responsibility to author M4-S2's retro retroactively.

**The narrative's three-Moment forward-spec rate.** Three of eight Moments authored as forward-spec is a high ratio for the first narrative. Phase 5's four backfill narratives will hit forward-spec at varying rates (Settlement-BC narrative will be heavily forward-spec until M5; Selling-BC narrative may be near fully audited; Participants-BC narrative will be fully audited). Worth tracking the per-narrative forward-spec ratio as a methodology-log seed for narrative #2.

**`document-as-intentional` items as deferred section non-rollers.** Two Moment 2 items were tagged `document-as-intentional` in their per-Moment subsections (`HasReserve` boolean signal; domain-vs-integration `ListingPublished` distinction). On consolidation into the cumulative `## Deferred from this narrative` section, these were omitted per the README's "backlog feeder, not transparency footnote" framing. Worth surfacing as a small README clarification candidate: `document-as-intentional` is a finding-routing lane only, not a deferral disposition. The narrative-internal retro at "Decisions about how to author" names this distinction; the README v1 may want to call it out explicitly.

## Key learnings

1. **Spec-anchored framing held under tension.** The `TryComputeExtension` bug (Finding 011) created a real authoring decision: render lived behavior (auction shortens; saga ignores; timer doesn't reset) or render workshop intent (auction extends; timer resets). ADR 016's spec-anchored framing authorized the latter, with the bug routing to `code-update` for Phase 2.5. The narrative voice held coherent across Moments 6 and 7 despite the bug; the framework absorbed the divergence cleanly.

2. **Per-Moment retro reads catch bugs the per-Moment code reads alone might miss.** Finding 011 surfaced by reading the `AuctionClosingSaga.Handle(ExtendedBiddingTriggered)` defensive guard, which mentioned a non-monotone-reschedule possibility in passing, then reading back to `PlaceBidHandler.TryComputeExtension` with that suspicion in mind. Code-only audits would have caught the bug eventually but slower; retro consultation accelerated discovery.

3. **Forward-spec routing is a normal Moment shape, not an exception.** Three of the eight Moments (3, 5, 8) were authored without lived code to audit. The convention "forward-spec Moments use the same prose shape as audited Moments; the difference shows up in the deferred subsection" worked: the journey arc remained coherent; the audit deferral routed cleanly under `defer`. Future narratives will routinely hit this pattern.

4. **Workshop-update is the dominant finding lane after four milestones of un-audited drift.** Five of twelve findings routed `workshop-update`. The workshop is the lagging artifact when no narrative layer mediates between it and lived code. The narrative session's first-pass audit produces a flush of corrections; future passes (per-narrative-#2, per-Phase-5-backfill) should produce fewer workshop-updates as the workshop converges on lived code.

5. **`document-as-intentional` is the second-most-common lane (4 of 12).** Four findings routed `document-as-intentional`, capturing design choices that lived code embodies but the workshop never recorded. These are not corrections; they are the narrative layer surfacing emergent design intent that previously lived only in code and retros. Phase 5 backfill narratives will likely produce more of these as Auctions, Selling, Participants, and Settlement code each contains its own un-narrated design choices.

6. **Workshop edits cascade naturally from workshop-update findings without scope creep.** Five `workshop-update` findings produced concrete W001 and `001-scenarios.md` edits across multiple Moment commits. The discipline "workshop-update findings resolve in the same PR by editing the workshop directly" held. No deferred workshop touchups; the workshop converges to lived code (or to documented design intent) within the session.

7. **The cumulative deferred section is substantial and that is the practice working.** Forty-plus items across seven disposition tags; the section is a project-level backlog feeder, not a transparency footnote. Phase 5 backfill narratives will pull from this list when scoping their own Moments and findings.

## Deviations from the prompt's acceptance criteria

The prompt's acceptance criteria at `docs/prompts/narratives/001-bidder-wins-flash-auction.md` §"Acceptance criteria" specified one item this session resolved differently:

> Each W001 slice the narrative directly implements carries a `Narratives: [001-bidder-wins-flash-auction]` line.

Resolution: a single consolidated "Narrative Cross-References" note at the start of W001 §"Phase 4 - Identify Slices" (commit `eb54594`) lists all ten implemented slices in one place rather than ten per-slice inline edits. Rationale: the W001 slice tables are tightly formatted; ten per-row edits would have visually fragmented the tables, and the README's "Workshop slices may cite the narratives that implement them via a `Narratives:` cross-reference line" wording leaves room for a consolidated form. User-approved at session close (sign-off question 2). Phase 3 of the foundation refresh handles the broader retroactive backfill across W001-W004 and may revisit the consolidated-vs-per-row choice if it becomes load-bearing.

All other acceptance criteria met (see verification checklist below).

## Verification checklist (from prompt §"Acceptance criteria")

- [x] `docs/narratives/001-bidder-wins-flash-auction.md` exists. Frontmatter conforms to v1 vocabulary. `status: accepted`.
- [x] Every Moment cites its W001 slice via `Implements:`.
- [x] No bulleted lists appear inside any Moment body (Guardrail 1).
- [x] No frontmatter keys outside the v1 vocabulary (Guardrail 2).
- [x] Each Moment has a `Context.`, `Interaction.`, `Response.` body. `Why this matters to the bidder.` is present where it adds meaning.
- [x] `## Deferred from this narrative` exists. Items are bucketed by the seven disposition tags.
- [x] `docs/narratives/001-findings.md` exists. Every finding has `Routing:`, `Surfaced at:`, `Discrepancy.`, `Resolution.`.
- [x] One stub prompt under `docs/prompts/implementations/` per `code-update` finding (Finding 011 → `phase2-5-extension-calculation-fix.md`).
- [x] `docs/narratives/README.md` Index table contains row 001.
- [~] Each W001 slice the narrative directly implements carries a `Narratives: [001-bidder-wins-flash-auction]` line. **Resolved as a single consolidated Narrative Cross-References note at the start of W001 §"Phase 4 - Identify Slices" rather than ten per-slice inline edits, with user approval. See "Deviations from the prompt's acceptance criteria" section above.**
- [x] `docs/research/methodology-log.md` has Entry 001, OR the phase retro names the conscious skip with rationale. **Conscious skip; rationale in "Methodology log Entry 001 conscious skip" section above.**
- [x] Narrative-internal retro appended in the narrative file after `## Deferred from this narrative`.
- [x] `docs/retrospectives/foundation-refresh-phase-2-retrospective.md` exists. Mirrors Phase 1's structure (this file).
- [x] No file under `src/` or `tests/` was edited in this session.
- [x] No em dashes in any committed prose authored by this session. Pre-existing em dashes in workshops and scenarios on rows not edited remain per the convention's grandfather clause; em dashes on rows under edit (W001 milestone mapping per Finding 006) were swept to hyphens.

## Document history

- **v0.1** (2026-04-27): Authored at Phase 2 close as the twelfth commit on the `foundation-refresh/p2-narrative-001` branch.
