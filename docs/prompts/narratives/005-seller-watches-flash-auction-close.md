# Prompt 005 - Author the Auctions-BC Backfill Narrative: Seller Watches Flash Auction Close

| Field | Value |
|---|---|
| **Status** | Pending |
| **Authored** | 2026-04-29 |
| **Phase** | Foundation Refresh, Phase 5, Item 1d |
| **Subdirectory** | `docs/prompts/narratives/` |
| **Journey** | A seller watches his published Flash listing go to auction, the reserve cross, extended bidding trigger, and the gavel fall in his favor (happy path) |
| **Protagonist** | GreyOwl12 (continuing as protagonist from narrative 004; seller-perspective on the keyboard's auction) |
| **Target artifact** | `docs/narratives/005-seller-watches-flash-auction-close.md` (to be produced) |
| **Companion artifact** | `docs/narratives/005-findings.md` (to be produced; conscious-skip note acceptable if zero findings) |
| **Source-of-truth dependencies** | W002 (`002-auctions-bc-deep-dive.md`) and W002 scenarios (`002-scenarios.md`); lived `src/CritterBids.Auctions/` code; M3-S1 through M3-S6, M3-S5b, M3-auctions-bc-retrospective, M4-S1 retros; narrative 001 Setting paragraph 2 (the keyboard's listing-time fields) and narrative 001 Moments 4-7 (bidder-perspective on the same auction) for cross-narrative continuity |
| **Workflow position** | Final of four Phase 5 backfill narratives. Largest lived audit surface in the project. Closes the lived-BC narrative wave before Item 4's cutover gate. |

---

## Framing

This session authors the Auctions BC's first dedicated narrative and CritterBids' first observer-perspective narrative. GreyOwl12 — protagonist of narrative 004 where he registered, drafted, and published the keyboard listing — now watches what the system does with it. He scheduled the keyboard for the demo session days ago; today is conference day; the operator is about to start the Flash session. The narrative dramatises what GreyOwl12 perceives over the next few minutes: bid notifications rolling in on his seller dashboard, a `ReserveMet` signal when SwiftFerret42's $55 bid crosses his confidential $50 threshold, the close timer extending when she retaliates in the trigger window, and the gavel falling on a $55 hammer. Narrative 002 picks up at this terminal beat and dramatises the settlement that follows.

Narrative 005 is structurally an **observer narrative**. GreyOwl12 does not act during the auction — he has no commands to send, no bids to place, no UI buttons to click. He watches state changes that are wholly system-driven and bidder-driven. The narrator's responsibility is rendering an observer-protagonist's experience while still dramatising the system's internal saga at finer grain than narrative 001 reached. The Voice section's role expands here: the narrator must preserve journey-grain ("the price ticked up to $35") while also dramatising saga-grain mechanics ("the auction-closing saga handles the `ReserveMet` evaluation against the bid stream") that GreyOwl12's window doesn't fully expose.

Narrative 005's audit surface is the **largest lived surface in the project**. M3 (slices S1 through S6) is fully shipped: BiddingOpened consumer, DCB place-bid handler with `BidConsistencyState`, BuyNow handler, the auction-closing saga skeleton plus terminal paths, the listings-catalog auction-status projection. M4-S1 (foundation decisions for M4 completion work) shipped its retrospective. **M4-S5 (Auctions-side `SessionStartedHandler` fan-out) and M4-S6 (Listings-side `SessionMembershipHandler`) have not shipped** — there are no implementation prompts at `docs/prompts/implementations/M4-S5*` or `M4-S6*` either. So Moment 1 (the operator starts the Flash session and the keyboard becomes bid-able) is **forward-spec without a prompt-grade reference**: the spec source is W002's coverage of the Flash session aggregate plus narrative 001 Setting paragraph 2's listing-time-field ground. The narrator renders the journey as W002 designs it; the lived-code audit lane defers under `defer` until M4-S5 and M4-S6 ship.

Findings expectations: high probability of `code-update` candidates given the M3 lived surface's scale (narrative 001 already surfaced one — Finding 011's `TryComputeExtension` bug, resolved in Phase 2.5). W002 may carry storage staleness against ADR 011 like W003 did (Finding 003 there); narrative 002 found it on W003 but narrative 004's audit confirmed W004 was clean. W002's posture is unknown until narrative 005 audits it.

ADR 016 (Spec-Anchored Development) governs throughout. For Moments 2-4, the audit floor is shipped M3 code; for Moment 1, the audit floor is W002 plus narrative 001 Setting.

---

## Goal

Author the Auctions BC's backfill narrative covering GreyOwl12's seller-perspective experience as the keyboard goes to auction in the demo Flash session: the operator starts the session and the keyboard becomes bid-able (forward-spec for M4-S5/S6), the bids build through the trigger window, the reserve crosses at SwiftFerret42's $55 retaliation, extended bidding triggers and the close timer extends, the gavel falls and the keyboard sells at $55 hammer. Audit W002, W002 scenarios, lived `src/CritterBids.Auctions/` code, and narrative 001 Moments 4-7 for cross-narrative consistency. Route disagreements through the four-lane findings discipline. Add per-row narrative back-references on W001 (slices 2.3, 3.1, 3.3, 5.1) and a new Narrative Cross-References section on W002 (or extend it if one exists). Establish anchored cross-narrative values for the bid-by-bid sequence at finer grain than narrative 001 Moments 4-6 reached.

---

## Orientation files (read in order before starting)

1. `C:\Code\CritterBids\CLAUDE.md` — routing layer and global conventions.
2. `C:\Code\CritterBids\docs\narratives\README.md` — format manual v0.1.
3. `C:\Code\CritterBids\docs\narratives\001-bidder-wins-flash-auction.md` — Setting paragraph 2 (the keyboard's listing-time fields and bid-by-bid happy-path declarations) plus Moments 4 (place first bid), 5 (outbid push), 6 (retaliation in trigger window), 7 (gavel falls). Narrative 005 covers the same auction from the seller's window; cross-narrative consistency is essential. The narrative 001 retrospective also matters: Key Learning entries on the M3-S5b reading discipline are directly applicable here.
4. `C:\Code\CritterBids\docs\narratives\002-winner-clears-settlement.md` — the immediate-downstream narrative; narrative 005 closes at the moment narrative 002 opens (`ListingSold` integration event in flight).
5. `C:\Code\CritterBids\docs\narratives\004-seller-publishes-and-withdraws-listing.md` and `004-findings.md` — narrative 005 inherits patterns: mixed-posture, sibling-listing (not used here), path-citation pre-check, code-comment-as-routing-evidence, em-dash hygiene drop.
6. `C:\Code\CritterBids\docs\workshops\002-auctions-bc-deep-dive.md` and `002-scenarios.md` — the workshop the narrative implements. Phase 1 (Brain Dump, including key design decisions and architecture summary), Phase 2 (Storytelling), and Phase 3 (Scenarios) are the principal references.
7. `C:\Code\CritterBids\docs\retrospectives\M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` — design-time decisions for the closing saga's terminal-paths shape; narrative 001 retrospective Key Learning 1 highlights this retro as load-bearing for understanding the saga's reaction code and for catching the `TryComputeExtension` bug.

Per-Moment lived-code reads under `src/CritterBids.Auctions/`:
- `Listing.cs` (the Auctions-side aggregate; distinct from Selling's `SellerListing`)
- `ListingPublishedHandler.cs` (consumes the integration `ListingPublished`; opens the bidding stream for Timed format; the Flash-format branch is where M4-S5/S6 forward-spec lives)
- `PlaceBidHandler.cs` (DCB place-bid; carries the `TryComputeExtension` bug per narrative 001 Finding 011 — already resolved in Phase 2.5)
- `BidConsistencyState.cs` (the DCB consistency record; key shape for Moment 2)
- `BuyNow.cs` and `BuyNowHandler.cs` (BIN flow; out of scope for this narrative — narrative 005 is bidding-source, not BIN)
- `AuctionClosingSaga.cs` (the closing saga; key for Moments 3 and 4)
- `AuctionClosingStatus.cs` (saga state enum)
- `StartAuctionClosingSagaHandler.cs` (the saga starter)
- `CloseAuction.cs` (the close command)
- `BidRejected.cs` (rejection event; alternate-path territory)
- `AuctionsModule.cs` (DI wiring)
- `AuctionsIdentityNamespaces.cs` (UUID v5 namespace constants)

Additional retros worth reading per-Moment: M3-S2 (BC scaffold) for foundation; M3-S3 (BiddingOpened consumer) for Moment 1; M3-S4 (DCB place-bid) for Moment 2; M3-S5 (saga skeleton) and M3-S5b (saga terminal paths) for Moments 3 and 4; M3-S6 (listings-catalog auction-status) for cross-BC projection details; M4-S1 (auctions completion foundation decisions) for the M4 design context.

The orientation list is at seven items per AUTHORING.md rule 9; per-Moment retro reads are session-time work.

---

## Working pattern

Same interactive cadence as narratives 002, 003, 004. Cast and Setting first; Moment-by-Moment thereafter with sign-off and commit per beat.

For each Moment:
- Read the implementing slice from W001 or M4-S5/S6 framing; for Moment 1, read narrative 001 Setting + W002 Phase 2 (Storytelling) for the Flash session-start cascade spec.
- Read the matching W002 scenario sections and `002-scenarios.md` GWT entries.
- Read the lived code path under `src/CritterBids.Auctions/` (Moments 2-4); Moment 1 has no lived code to audit (M4-S5/S6 unshipped).
- Read the relevant retro (per the orientation guide above).
- Read narrative 001's matching Moment for cross-narrative consistency check.
- Draft the Moment in the README's Guardrail-1 shape.
- Identify findings as the draft is written.
- Sign-off, commit.

Multi-paragraph `Response.` blocks expected for Moment 2 (the bid cascade across $30 / $35 over the bidding window; possibly compressed into one Moment with multiple Response paragraphs) and Moment 4 (the gavel-fall through `BiddingClosed` + `ListingSold` events plus the integration event landing on the cross-BC bus for narrative 002's Settlement entry).

Pre-Moment surrounding-directory reads (narrative-003 lesson refined through narratives 003 and 004) apply: `Glob src/CritterBids.Auctions/*` plus `Grep` for any registration patterns before drafting.

Path-citation pre-check (narrative-004 lesson) applies: confirm any retro path cited via `Glob` before committing the prompt or the narrative.

---

## Voice and perspective

**Single-named-protagonist plus omniscient narrator** is locked. Narrative 005 uses the **seller-perspective** slot (second use after narrative 004's GreyOwl12-as-seller; this is GreyOwl12-as-observer of his own listing's auction).

Protagonist is GreyOwl12, observer-protagonist. The narrative's defining Voice characteristic is that he does not act — he watches. The narrator dramatises what he perceives (bid notifications on his seller dashboard, status changes, push notifications, the eventual sale confirmation) while also rendering the saga-grain mechanics (DCB place-bid validation, the closing saga's state transitions, the cross-BC integration-event commit) that GreyOwl12's window does not fully expose. The narrator carries the system-internal beats; the protagonist carries the journey arc through observation.

The contrast to narratives 001 / 003 / 004 (where protagonists actively interact with the system) is structurally the most interesting Voice question. The narrator must avoid two failure modes: (1) over-narrating saga internals to the point that GreyOwl12's experience becomes incidental, and (2) under-narrating saga internals to the point that the narrative becomes a thin journal of "GreyOwl12 saw a notification, then another notification, then the gavel fell." The right grain dramatises the saga as a series of beats that GreyOwl12 perceives the surface of; the narrator names the events and state transitions; the protagonist's window stays the dramatic anchor.

---

## Findings discipline (largest lived surface, mixed-posture by Moment)

Audit-floor splits by Moment:

| Lane | Moment 1 (forward-spec) | Moments 2-4 (lived M3) |
|---|---|---|
| `narrative-update` | Low. The narrator follows W002 + narrative 001 Setting; the spec source is precise. | Moderate. First-pass drafts will need correction against lived `Listing.cs`, `PlaceBidHandler.cs`, `AuctionClosingSaga.cs` behavior. Cross-narrative consistency with narrative 001 Moments 4-7 is the additional surface. |
| `workshop-update` | Possible. W002 may carry storage-layer staleness against ADR 011 like W003 did (narrative 002 F003); narrative 005's audit confirms or refutes. W002's Flash session aggregate framing (Phase 2 Storytelling) may have drift relative to the M4-S5/S6 design that has not yet been fully specified. | Moderate. M3 lived code is mature; W002 was authored mid-M3 and may have payload-drift or naming-drift against the lived events. |
| `code-update` | **Structural impossibility.** M4-S5/S6 code is unshipped. Cross-BC `code-update` against M3 consumers of the eventual `SessionStarted` event remains possible if such consumers exist. | **Real and likely lane.** M3 has the largest lived surface; narrative 001 already surfaced Finding 011 here (`TryComputeExtension` bug). Other candidates: aggregate-state shape, saga state-machine completeness, integration-event payload-shape consistency, DCB consistency-state lifecycle. |
| `document-as-intentional` | Low. The forward-spec posture limits intentional-but-undocumented design surface. | Moderate. M3-S5b retro records design-history decisions (Path A / B / C labels) that may surface as `document-as-intentional` if W002's framing is less precise than the retro's design choices. |

Lived `code-update` findings produce stub follow-up implementation prompts at `docs/prompts/implementations/<slug>.md` per the Phase 2.5 discipline if the resolution exceeds a one-line edit. One-line comment edits or similar trivial fixes land in-PR (per narratives 003 F001 and 004 F002 precedents).

### Findings file shape

Same schema as narratives 001-004. Per foundation-refresh handoff §4.4.

### Heads-up sources of likely findings

Do not pre-decide outcomes. Be ready when these come up:

1. **W002 storage-layer references against ADR 011.** If W002 carries Polecat / SQL Server framing like W003 did, narrative 005 surfaces it as `workshop-update`. Lean: minimum-scope correction in this PR if found.
2. **`TryComputeExtension` bug aftermath.** Narrative 001 Finding 011 surfaced this; Phase 2.5 resolved it. Narrative 005 should confirm the fix landed correctly by reading the post-fix `PlaceBidHandler.cs:TryComputeExtension`. If the post-fix code has new drift, route as `code-update`.
3. **Cross-narrative reconciliation with narrative 001 Moments 4-7.** Narrative 001 documented specific bid amounts ($25 starting → $30 → $35 → $55 → close at extended-bidding terminal). Narrative 005 must render the same sequence with the same amounts; deviations route as `narrative-update` against narrative 001 (cite-and-edit per Phase 5 §7) or as `narrative-update` against narrative 005's own draft.
4. **Auctions-side `Listing` aggregate shape vs Selling-side `SellerListing`.** Two distinct aggregates, two distinct lifecycles, both keyed on `ListingId`. The narrator may need to distinguish them explicitly; W002 covers the Auctions-side aggregate at finer grain than W004 covers Selling.
5. **Auction-closing saga's state machine.** `AuctionClosingSaga.cs` plus `AuctionClosingStatus.cs` plus the `M3-S5b` retro. The saga's terminal paths (sold / passed / withdrawn) plus extended-bidding-window-extension behavior plus reserve-met evaluation are the key audit territory.
6. **Integration `ListingSold` payload at cross-BC handoff.** Narrative 002 Setting confirmed `ListingSold(ListingId, SellerId, WinnerId, HammerPrice, BidCount, SoldAt)` per `src/CritterBids.Contracts/Auctions/ListingSold.cs`. Narrative 005's Moment 4 commits this exact event; cross-narrative consistency is the audit surface.
7. **`ReserveMet` event timing.** Per W001 slice 5.2 (P1 in narrative 001 retrospective), `ReserveMet` is the UX-grade signal; Settlement's `ReserveCheckCompleted` is the authority-grade signal (narrative 002 Moment 2 dramatised the latter). Narrative 005 dramatises the former. Whether `ReserveMet` is shipped lived in M3 or remains forward-spec for a future slice is the audit question — if forward-spec, route Moment 2's reserve-cross beat under `defer`.

---

## Cross-reference discipline

- Each Moment cites its slice via `**Implements:** slice X.Y[, slice X.Z, ...]`. Narrative 005 implements W001 slices 2.3 (session start cascade — Moment 1; forward-spec), 3.1 (place bid — Moment 2; multi-bid cascade), 3.3 (scheduled close — Moment 4), 5.1 (extended bidding triggered — Moment 3). Slice 5.2 (ReserveMet) is referenced if the audit confirms it ships in Moment 2.
- Domain event names render in code-style backticks: `BiddingOpened`, `BidPlaced`, `BidRejected`, `ExtendedBiddingTriggered`, `BiddingClosed`, `ListingSold`, `ReserveMet`. Plain text for ordinary nouns: Auction, Bid, Bidder Session, Reserve, Hammer Price, Trigger Window, Extended Bidding, Closing Saga.
- Do not restate `002-scenarios.md` content. Reference the workshop section number and the W001 slice number; the workshop is the test specification, the narrative is the journey.
- W001's consolidated Narrative Cross-References block already exists (extended through narrative 004 in PR #22). Narrative 005 adds a new bullet for narrative 005 implementing slices 2.3, 3.1, 3.3, 5.1 (and 5.2 if confirmed).
- W002 cross-reference form: confirm at session start whether W002 has a Narrative Cross-References section already (per Phase 3 Item 2's broader backfill scope) or if narrative 005 needs to add one. Lean: per-row form per Phase 3 Item 2's BC-workshop default since narrative 005 implements only 4-5 W001 slices on W002 territory.

---

## What the narrative does NOT carry

- **No code or pseudocode.** Aggregate methods, saga state-machine transitions, DCB consistency-state expressions described in prose.
- **No implementation choices.** Marten primitive choices, Wolverine handler routing, scheduled-message cancellation patterns belong to skill files.
- **No architectural decisions.** ADR candidates surface in the deferred section.
- **No GWT test specifications.** Reference `002-scenarios.md` section numbers; do not restate.
- **No UX or UI design.** Render at the seller-experience grain ("the seller dashboard's price ticker advances to $35"); do not design the screens. Forward-spec UI for the seller-side auction-watching dashboard is M6 frontend territory.
- **No re-authoring of narratives 001, 002, or 004.** All are `status: accepted`. Single-paragraph cite-and-edit fixes against narrative 001's Moments 4-7 are permitted per Phase 5 §7 if narrative 005's audit surfaces drift; structural rewrite is not.

---

## In scope (proposed Moment list)

| Moment | Slice(s) from W001 / source | Posture | Seller experience |
|---|---|---|---|
| 1 | 2.3 (session start cascade) | **Forward-spec** | The operator clicks Start Session. The Flash session aggregate begins, the keyboard receives a `BiddingOpened` event via the cross-BC fan-out, the keyboard's `Listing` aggregate (Auctions-side) opens its bidding stream, the `BidConsistencyState` for the keyboard is initialised. GreyOwl12 sees his seller dashboard tick from "scheduled" to "live" for the keyboard. |
| 2 | 3.1 (place bid; multi-bid cascade), 5.2 (reserve met if shipped) | Lived M3 | Bids roll in. SwiftFerret42 places $30; BoldPenguin7 places $35; SwiftFerret42 places $55 in the trigger window. Each bid runs through the DCB place-bid handler, validates against the credit ceiling and current high bid, and commits as `BidPlaced` against the keyboard's stream. At $55, the reserve threshold is crossed; if `ReserveMet` is shipped lived, the event fires here. GreyOwl12's dashboard shows the bid count climb (1, 2, 3) and the current high bid tick up ($30, $35, $55). At $55, his confidential reserve is met — his dashboard surfaces a `ReserveMet` indicator. |
| 3 | 5.1 (extended bidding triggered) | Lived M3 | SwiftFerret42's $55 bid landed inside the close timer's 30-second trigger window. The auction-closing saga (already started for the keyboard's scheduled close per M3-S5 wiring) handles the trigger by emitting `ExtendedBiddingTriggered` and rescheduling the close 15 seconds later than the original. GreyOwl12's dashboard shows the close timer extending. |
| 4 | 3.3 (scheduled close → BiddingClosed → ListingSold) | Lived M3 | The new close timer fires. The closing saga handles `CloseAuction`, emits `BiddingClosed` and `ListingSold`. Listings BC's `CatalogListingView` updates to `Status: "Sold"`. The integration event `ListingSold(ListingId, SellerId: GreyOwl12, WinnerId: SwiftFerret42, HammerPrice: $55.00, BidCount: 3, SoldAt: <now>)` is appended to the Wolverine outbox for cross-BC delivery — Settlement BC will consume it (narrative 002 picks up here). GreyOwl12's dashboard shows the keyboard's status flip to "Sold" and the hammer price $55 displayed. |

Four Moments. Bookended by mixed-posture: forward-spec opener, lived saga-driven closer. Moment 1 is a single beat compressing the multi-event session-start cascade (multi-paragraph `Response.` may be appropriate); Moment 2 compresses three bids into one Moment with multi-paragraph `Response.`; Moment 3 is a tight saga-trigger beat; Moment 4 closes with the cross-BC integration handoff to narrative 002.

Alternative groupings flagged at session start:

1. **Five Moments** — split Moment 2 by reserve-met. The first sub-Moment covers $30 and $35 (reserve unmet); the second covers $55 (reserve crossed). Argument: the reserve-met beat is GreyOwl12's most significant in-auction perception. Counter-argument: the journey grain combines bids 1-3 into a continuous price-climb that GreyOwl12 perceives as one accelerating beat, with the reserve-met indicator as a state change embedded in it; splitting may dramatise more than his window experiences.
2. **Three Moments** — collapse Moment 3 (extended bidding) into Moment 2's tail or Moment 4's opening. Argument: the extended-bidding mechanic is fast (one event, one timer reschedule) and may not warrant its own Moment. Counter-argument: extended bidding is a defining characteristic of Flash auctions; the timer-extension beat is what makes the keyboard's auction structurally distinct from a Timed auction's natural close.
3. **Six Moments** — split Moment 4 into separate "the gavel falls" and "the integration event lands for Settlement" beats. Argument: the cross-BC handoff is a meaningful dramatic anchor. Counter-argument: from GreyOwl12's window the two are simultaneous; he sees "Sold" once.

Lean: four Moments. Flag at session start if a different grain fits.

---

## Out of scope for this session

- **The bidder-side experience.** Narrative 001 covers this; narrative 005's seller-perspective is observer-grade and does not re-render bidder-side action.
- **BIN purchase path.** The keyboard does not get bought-it-now; this is a `separate-narrative` alternate path. (Narrative 002's Setting also confirms BIN was not exercised.)
- **Auction-passed terminal path.** The keyboard sells (reserve met). The passed-without-reserve-met terminal is `alternate-path-failure`.
- **Auction-withdrawn terminal path.** The keyboard is not withdrawn (narrative 001 ground; narrative 004 dramatised the WithdrawListing flow on the camera, not the keyboard). `alternate-path-failure` for any keyboard-withdrawal counterfactual; `separate-narrative` for any future seller-withdraws-during-auction journey.
- **Proxy-bidding flow (M4-S4).** No bidder uses proxy bidding in this narrative; the M4-S4 Proxy Bid Manager saga is `separate-narrative` for any proxy-perspective future narrative.
- **Settlement-side beats.** Narrative 002 covers settlement; narrative 005 closes at the `ListingSold` integration-event commit.
- **Operator-perspective beats.** The operator creates the session, attaches listings, starts it. Narrative 005 dramatises this from GreyOwl12's offstage observation; the operator's actions are forward-spec context, not seller-experience. `separate-narrative` for any future operator-perspective work.
- **The Auctions-side `Listing` aggregate's full state shape.** The narrator names key state transitions but does not enumerate every field; full coverage is `implementation-detail`.
- **Listings BC's `CatalogListingView` projection-handler logic in detail.** Narrative 005 names the view's status flips; detailed projection logic is `separate-narrative` (covered in narrative 001 Moments 2-3 from the bidder's window).
- **Any code refactor.** `code-update` findings produce in-PR fixes (one-line edits) or stub follow-up prompts (anything larger).
- **W001 broad backfill of narrative back-references.** Only slices the narrative directly implements get a back-reference entry.
- **Methodology format changes.** README v0.1 dialect remains locked.
- **Phase 5 cross-narrative retrospective.** Item 4 territory.

---

## Deliverable plan

Per Phase 5 prompt §3.5 acceptance gates:

1. **Narrative file** at `docs/narratives/005-seller-watches-flash-auction-close.md`. Frontmatter v1, prose-paragraph Moments, single-named-seller-as-observer voice. `status: accepted` at session close.
2. **Findings file** at `docs/narratives/005-findings.md`, OR a conscious-skip note in the narrative-internal retro if zero findings surface (unlikely given the M3 lived-surface scale).
3. **Stub follow-up prompts** at `docs/prompts/implementations/<slug>.md`, one per `code-update` finding whose resolution exceeds a one-line edit.
4. **Narratives README Index update** in `docs/narratives/README.md`. Row 005 added.
5. **W001 cross-reference extension** on the consolidated Narrative Cross-References block: a new bullet for narrative 005 listing the slices it implements.
6. **W002 cross-reference addition** as a new top-level Narrative Cross-References section (or per-row entries depending on the form chosen at session start).
7. **Methodology log Entry 001 final consideration.** This is the final lived-BC narrative before Phase 5 closes. The cumulative cross-cutting observations across narratives 002-005 are now available; the entry-criteria gate applies. Conscious-skip note acceptable.
8. **Narrative-internal retrospective** appended in the narrative file after `## Deferred from this narrative`.

---

## Acceptance criteria

- [ ] `docs/narratives/005-seller-watches-flash-auction-close.md` exists. Frontmatter conforms to v1 vocabulary. `status: accepted`.
- [ ] Every Moment cites its slice via `Implements:`.
- [ ] No bulleted lists appear inside any Moment body (Guardrail 1).
- [ ] No frontmatter keys outside the v1 vocabulary (Guardrail 2).
- [ ] Each Moment has a `Context.`, `Interaction.`, `Response.` body. `Why this matters to the seller.` is present where it adds meaning.
- [ ] `## Deferred from this narrative` exists. Items are bucketed by the seven disposition tags.
- [ ] `docs/narratives/005-findings.md` exists with at least one finding, OR the narrative-internal retro contains an explicit conscious-skip note with rationale.
- [ ] Each `code-update` finding (if any) has a stub follow-up prompt at `docs/prompts/implementations/<slug>.md`, except for one-line edits resolvable in-PR.
- [ ] `docs/narratives/README.md` Index table contains row 005.
- [ ] W001's consolidated Narrative Cross-References block carries a new bullet for narrative 005.
- [ ] W002 carries a Narrative Cross-References section (new or extended) listing narrative 005's coverage.
- [ ] Narrative-internal retro appended.
- [ ] No file under `src/` or `tests/` was edited in this session beyond any in-PR resolution of small `code-update` findings.

---

## Open questions to flag (not decide)

These are session-start decisions; surface them and ask the user before locking Cast and Setting.

- **Four Moments vs three vs five vs six.** §"In scope" leans four. Trade-offs documented in §"In scope". Lean: four.
- **`ReserveMet` shipped-lived audit.** Whether the M3 lived code emits `ReserveMet` per W001 slice 5.2 is the audit question. If shipped, Moment 2 includes the reserve-met beat as a lived-code finding-able beat. If not shipped, Moment 2's reserve-cross beat routes under `defer` (forward-spec until slice 5.2 ships) or the narrator renders the threshold-cross at the journey grain without naming a specific event. Decide at the session-start lived-code review of `PlaceBidHandler.cs` and `AuctionClosingSaga.cs`.
- **W002 cross-reference form (per-row vs consolidated).** Per Phase 3 Item 2, BC-focused workshops citing 1-3 slices use per-row by default; narrative 005 implements 4-5 slices, putting it on the boundary. Confirm at session start. Lean: consolidated form for narrative 005 since the slices are tightly grouped within W002's scope.
- **PR shape: fold prompt + narrative session into one PR vs separate prompt PR.** Default per the Phase 5 Item 1a/1b/1c precedent: fold. Confirm at session start.
- **Cross-narrative consistency edits to narrative 001.** Narrative 005 may surface drift in narrative 001's Moments 4-7 (specific bid amounts, event payloads, timing claims). Phase 5 §7 permits cite-and-edit single-paragraph fixes. The most likely drift is on `BidPlaced` event payload shape if narrative 001 misnamed a field. Decide at the relevant Moment.
- **Methodology log Entry 001 final consideration.** The four lived-BC narratives have completed (002 forward-spec, 003 lived-Participants, 004 lived-Selling-plus-forward-spec-WithdrawListing, 005 lived-Auctions-largest-surface). The cumulative pattern observations across the four are the entry-criteria gate's evidence. If a genuinely cross-cutting observation exists, write the entry; otherwise the conscious-skip note in the narrative-internal retro suffices. The cutover-gate Item 4 retrospective will also have a chance.

---

## Memory inheritance

Phase 1, Phase 2, narrative 002 / 003 / 004 session memories apply unchanged. Notable carryforwards:

- **Depth over brevity** when explaining tradeoffs.
- **Ubiquitous language** (auction-domain, Auctions-flavored): Auction, Bid, Bidder, Seller, Auctioneer, Reserve, Hammer Price, Buy It Now, Flash Session, Timed Auction, Bidder Session, Trigger Window, Extended Bidding, Closing Saga, BidConsistencyState, DCB.
- **DDD, CQRS, Event Sourcing, EDA** assumed background.
- **SDD and NDD methodology vocabulary is NOT assumed background.** Define on first use.
- **Lean opinions on questions.** Propose a default with rationale rather than open-ended elicitation.
- **Em-dash hygiene does NOT apply to internal docs.** Per the memory clarification at narrative 002 close.
- **Path-citation pre-check at prompt-authoring time.** Per narrative 004's lesson.
- **Code-comment-as-routing-evidence.** Per narrative 004's discipline. Apply when auditing M3 code.
- **Pre-Moment surrounding-directory reads.** Per narrative 003 lesson; refined through narrative 004.
- **Punchy prose; no AI-tool references in committed text.**
- **No `git push` to `main`** without explicit authorization. Commit freely on the narrative-005 branch; push only when asked.

---

## Starting move

When the session begins:

1. Re-read this prompt and `docs/narratives/README.md` v0.1 in full.
2. Re-read narrative 001 Setting paragraph 2 (the keyboard's listing-time fields and bid-by-bid happy-path declarations) and Moments 4-7 (bidder-perspective on the same auction). These are the cross-narrative ground narrative 005 must remain consistent with.
3. Skim narratives 002 and 004 closing sections for inherited patterns.
4. Confirm with user: Moment grain (4 vs 3 vs 5 vs 6), W002 cross-reference form (per-row vs consolidated), PR shape (fold vs separate). Lock these before drafting Cast.
5. Run a quick `Glob` / `Grep` on `src/CritterBids.Auctions/` for `ReserveMet` to determine Moment 2's audit surface (shipped lived vs forward-spec). Adjust Moment 2's framing accordingly.
6. Quickly check W002 for Polecat / SQL Server staleness (analogous to narrative 002's W003 audit). If found, queue F-candidate; if not, narrative 005's findings ledger doesn't include a workshop-storage finding.
7. Propose Cast and Setting. Sign-off. Commit.
8. Walk Moment-by-Moment per the working pattern. For each Moment: read implementing slice → read W002 scenario section → read lived code (Moments 2-4) or narrative 001 + W002 spec (Moment 1) → read relevant retro → draft Moment → surface findings → sign-off → commit.
9. At session close: classify all findings, write any `code-update` stub prompts, update the narratives README Index, extend W001's consolidated Narrative Cross-References block, add or extend W002's Narrative Cross-References section, evaluate methodology-log Entry 001 (final lived-BC chance), append the narrative-internal retro, flip the narrative's `status:` to `accepted`.

---

## Document history

- **v0.1** (2026-04-29): Authored as foundation-refresh Phase 5 Item 1d session prompt. Adapts the Phase 5 Item 1c (narrative 004) prompt template for largest-lived-audit-surface posture and observer-protagonist Voice. Mixed posture: Moment 1 forward-spec for M4-S5/S6 session-start cascade (no implementation prompt exists; spec source is W002 + narrative 001 Setting); Moments 2-4 lived M3. Four-Moment proposal covers session-start cascade, multi-bid cascade with reserve-cross, extended-bidding trigger, and gavel-fall closing handoff to narrative 002. Cross-narrative consistency with narrative 001 Moments 4-7 is the principal new audit surface. The Phase 5 prompt §3.5's framing of "Most likely seller-perspective on a winning Flash auction with extended bidding" maps directly to this prompt's Moment list. Em-dash hygiene drop applies; no audit step. Path-citation pre-check confirms M4-S5 / M4-S6 implementation prompts do not exist (Moment 1 forward-spec base is W002 + narrative 001 only).
