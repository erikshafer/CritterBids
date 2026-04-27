# Narrative 001 - Findings

Findings surfaced while authoring `001-bidder-wins-flash-auction.md` against lived M3 and M4 code. Each finding is routed via the four-lane discipline established in ADR 016 and detailed in the narrative-authoring prompt at `docs/prompts/narratives/001-bidder-wins-flash-auction.md`:

| Lane | Resolved in this PR? |
|---|---|
| `narrative-update` | Yes. Narrative edited. |
| `workshop-update` | Yes. Workshop edited. |
| `code-update` | No. Stub follow-up prompt under `docs/prompts/implementations/`. Resolved in Phase 2.5. |
| `document-as-intentional` | Yes. Relationship documented. |

---

### Finding 001 - Setting's credit-ceiling band drifted from lived code

**Routing:** narrative-update

**Surfaced at:** Moment 1

**Discrepancy.** The narrative's Setting paragraph 3 originally claimed the credit ceiling was "between $200 and $500". Lived code at `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs:62` derives the ceiling as `200m + (bytes[14] % 9) * 100m`, yielding nine discrete values from $200 to $1000 in $100 steps. The M1-S5 retrospective (`docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md`) confirms the 200-1000 range. The Setting paragraph was authored without first reading the lived code; the narrower band claim was a fabrication introduced at Cast-and-Setting drafting time.

**Resolution.** Setting paragraph 3 edited from "between $200 and $500" to "drawn from one of nine values between $200 and $1000 in $100 steps". SwiftFerret42's specific value of $500 remains valid (it is the median of the band).

---

### Finding 002 - Workshop slice 0.2 asserts hard display-name uniqueness; lived code provides probabilistic uniqueness only

**Routing:** workshop-update

**Surfaced at:** Moment 1

**Discrepancy.** The workshop scenario "Display name is unique within active sessions" in `docs/workshops/001-scenarios.md` slice 0.2 asserts a hard uniqueness invariant ("DisplayName: 'BoldPenguin7' // different from any active session"). The lived code at `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs:49-52` derives the display name from UUID v7 random bytes (one of twenty-five Adjectives × one of twenty-nine Animals × a 1-9999 number suffix, roughly 7.25M tuples) with no active-session lookup or collision check. For the demo audience size (~40 concurrent bidders), collision probability is well below 0.001%, but it is non-zero. The M1-S5 retrospective at `docs/retrospectives/M1-S5-slice-0-2-start-participant-session.md` records the same misclaim ("Uniqueness guaranteed by stream ID uniqueness") - which is a category error: stream-ID uniqueness does not propagate through a lossy derivation onto a finite tuple space. The code comment at `StartParticipantSession.cs:12` carries the misclaim too, but its correction is a separate `code-update` candidate not blocking this PR.

**Resolution.** Workshop scenario in `docs/workshops/001-scenarios.md` slice 0.2 reframed: the heading rewritten from "Display name is unique within active sessions" to "Display names are probabilistically unique within active sessions"; the inline comment on `BoldPenguin7` rewritten from "different from any active session" to "probabilistically distinct, derived from UUID v7 random bytes"; a "Note on uniqueness" block added below the scenario describing the MVP posture and the trigger condition under which a uniqueness index would become warranted (audience size at which collisions become practically observable).

---

### Finding 003 - W001 view inventory lists `ListingDetailView` as a separate Marten view; M3-S6 unified it into `CatalogListingView`

**Routing:** workshop-update

**Surfaced at:** Moment 2

**Discrepancy.** W001 §"Phase 3 - Storyboarding" View Inventory at `docs/workshops/001-flash-session-demo-day-journey.md:47` names `CatalogListingView` and `ListingDetailView` as separate Listings-BC Marten views. The slice table at line 81 shows slice 1.4 (Listing detail read path) producing `ListingDetailView`; line 115 shows slice 5.4 producing `ListingDetailView, CatalogListingView`. M3-S6 collapsed them under OQ2 Path A: a single `CatalogListingView` record carries the M2 base fields plus ten auction-status fields (`Status`, `ScheduledCloseAt`, `CurrentHighBid`, `CurrentHighBidderId`, `BidCount`, `HammerPrice`, `WinnerId`, `PassedReason`, `FinalHighestBid`, `ClosedAt`). Slice 1.4's `GET /api/listings/{id}` at `src/CritterBids.Listings/Features/Catalog/CatalogEndpoints.cs:36` calls `session.LoadAsync<CatalogListingView>(id)` rather than loading a distinct `ListingDetailView`. The M3-S6 retrospective at `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` records the design history (OQ2 Path A symmetry with `Format`).

**Resolution.** W001 §"Phase 3 - Storyboarding" View Inventory edited: `ListingDetailView` removed from the Listings BC Marten line; a one-line note added explaining that M3-S6 unified the catalog and detail projections into `CatalogListingView` under OQ2 Path A symmetry with `Format`. Slice 1.4 row at line 81 updated to `CatalogListingView`; slice 5.4 row at line 115 updated to drop the redundant `ListingDetailView` and reference only `CatalogListingView`.

---

### Finding 004 - `HasReserve` boolean signal asserted in workshop scenarios is absent from the lived `CatalogListingView`

**Routing:** document-as-intentional

**Surfaced at:** Moment 2

**Discrepancy.** Workshop scenarios for slice 1.2 and slice 1.4 in `docs/workshops/001-scenarios.md` assert `HasReserve: true` on the catalog and detail views with the comment "boolean only - amount never exposed". The lived `src/CritterBids.Listings/CatalogListingView.cs` carries no `HasReserve` field and no reserve-related field at all. The integration contract `src/CritterBids.Contracts/Selling/ListingPublished.cs` carries `ReservePrice: decimal?`, but `src/CritterBids.Listings/ListingPublishedHandler.cs:21-31` reads only eight fields (`ListingId`, `SellerId`, `Title`, `Format`, `StartingBid`, `BuyItNow`, `Duration`, `PublishedAt`) and silently discards `ReservePrice`. M3-S6 did not add a reserve-related field. The bidder has no upfront awareness that any reserve exists.

**Resolution.** The design intent is reserve-invisible-until-met: reserve existence and amount are both confidential between seller and Settlement until a bid first crosses the threshold, at which point the `ReserveMet` event (slice 5.2) signals existence to the bidder via the BiddingHub. Two valid expressions of the domain exist: the workshop's `HasReserve: true` boolean (an upfront signal that *some* reserve exists, amount confidential) and the lived view's silence (existence and amount equally confidential until met). Erik's design call confirmed reserve-invisible-until-met as the intended UX. The narrative's Moment 2 `Why this matters` paragraph renders this asymmetry as deliberate. Workshop scenario in `001-scenarios.md` slice 1.2 view block edited to drop the `HasReserve: true` line; the slice 1.4 reserve Note rewritten from "Reserve price is NEVER included; only `HasReserve: true/false`" to a longer note describing the reserve-invisible-until-met design and the `ReserveMet` signal. Whether a code-side stub for `HasReserve` ever lands depends on a future product decision; for the foreseeable future, the design holds.

---

### Finding 005 - Workshop status vocabulary is lowercase; lived code uses PascalCase, and the workshop's `"upcoming"` does not exist in code

**Routing:** workshop-update

**Surfaced at:** Moment 2

**Discrepancy.** Workshop scenarios across slices 1.2, 1.3, 1.4, 2.2, 2.3, 3.3, and 3.4 in `docs/workshops/001-scenarios.md` use lowercase status values: `"upcoming"`, `"open"`, `"sold"`, `"passed"`. Lived `src/CritterBids.Listings/CatalogListingView.cs` defaults `Status = "Published"` and the M3-S6 design comment documents transitions `"Published" → "Open" → "Closed" → "Sold"/"Passed"`. The workshop's `"upcoming"` is not a runtime status; `"Published"` is the equivalent post-publish pre-bidding state. The workshop also lacks `"Closed"` as the intermediate that M3-S6 uses on the BIN-and-bidding-close path. M3-S6 retrospective records the choice (OQ2 Path A symmetry with `Format` is `string`, PascalCase across the board).

**Resolution.** Eight `Status` references in `001-scenarios.md` updated from lowercase to PascalCase across slices 1.2, 1.3, 1.4, 2.2, 2.3, 3.3, 3.4. The four `"upcoming"` occurrences (slices 1.2, 1.3, 1.4, 2.2) replaced with `"Published"` to match the lived initial-status value. The two `"open"` occurrences (slice 2.3) replaced with `"Open"`. The two `"sold"` occurrences (slice 3.3) replaced with `"Sold"`. The two `"passed"` occurrences (slice 3.4) replaced with `"Passed"`. The `"created"` (slice 2.1, `SessionManagementView`) and `"complete"` (slice 6.1, `SettlementProgressView`) values are left unchanged - those views have their own vocabularies and are out of `CatalogListingView`'s scope.

---

### Finding 006 - W001 milestone mapping says M3 delivers Tier 2 (Flash session setup); reality is M3 delivered Tier 3 plus Auctions Timed-only foundation. Tier 2 is M4-S5 and M4-S6

**Routing:** workshop-update

**Surfaced at:** Moment 3

**Discrepancy.** W001 milestone-mapping table at `docs/workshops/001-flash-session-demo-day-journey.md:193` originally read (em dashes preserved as hyphens to honor the project's no-em-dash convention) `M3 - Flash Session Core | Tiers 2 + 3 | Full auction lifecycle via API and tests`. Lived M3 code at `src/CritterBids.Auctions/ListingPublishedHandler.cs:46-61` opens the bidding stream on `ListingPublished` consumption (Timed-only behavior); the handler comment explicitly states "M3 is Timed-listings-only per docs/milestones/M3-auctions-bc.md §3; the Flash path belongs to the M4 Session aggregate." The lived M3 has no `Session` aggregate, no `StartSession` handler, no `SessionStartedHandler` for the cascade. M4-S1 (foundation-decisions) created six contract stubs in `CritterBids.Contracts/Auctions/` (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`, `RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted`) but did not wire producers or consumers. The M4-S1 retro at `docs/retrospectives/M4-S1-auctions-completion-foundation-decisions-retrospective.md:167-168` names M4-S5 (`SessionStartedHandler` fan-out in Auctions BC) and M4-S6 (`SessionMembershipHandler` in Listings BC) as the slices that ship the Flash session machinery.

**Resolution.** W001 milestone-mapping table edited: M3 row renamed from "Flash Session Core" to "Auctions Core" with scope clarified to "Tier 3 + Auctions Timed-only foundation" and a parenthetical noting Flash session aggregate deferred to M4-S5/M4-S6; M4 row renamed from "Real-Time + Extended" to "Flash Sessions + Real-Time + Extended" with scope updated to "Tier 2 (M4-S5/M4-S6) + Tier 4 + 5.1". A note block added below the table referencing this finding and the M4-S1 retrospective. Em dashes in the table swept to hyphens as a side effect since the rows were already being rewritten.

---

### Finding 007 - Narrative intro paragraph claims "Two of the eight Moments (5 and 8)" are forward-spec; reality is three Moments (3, 5, 8) are forward-spec

**Routing:** narrative-update

**Surfaced at:** Moment 3

**Discrepancy.** The narrative's intro paragraph at `docs/narratives/001-bidder-wins-flash-auction.md:17` originally read "Two of the eight Moments (5 and 8) describe BCs that have not yet shipped lived implementation - Relay (Moment 5) and Settlement (Moment 8)". Moment 3 (the Flash session-start cascade) is also forward-spec because the Auctions-side Flash session aggregate, `StartSession` handler, and `SessionStartedHandler` fan-out are scheduled for M4-S5; the Listings-side `SessionMembershipHandler` is M4-S6. The intro framing under-represented the forward-spec scope of the narrative; readers would have inferred that Moments 1-4 and 6-7 all audit lived code, when in fact Moment 3 cannot.

**Resolution.** Intro paragraph edited from "Two of the eight Moments (5 and 8)" to "Three of the eight Moments (3, 5, 8)"; the parenthetical extended to name the Auctions-BC Flash session machinery (M4-S5/M4-S6) as Moment 3's forward-spec scope alongside Relay (Moment 5) and Settlement (Moment 8).

---

### Finding 008 - `BuyItNowOptionRemoved` is emitted atomically with `BidPlaced` on the first bid; workshop separates them as slices 3.1 (P0) and 5.4 (P1)

**Routing:** document-as-intentional

**Surfaced at:** Moment 4

**Discrepancy.** Workshop slice 3.1 (P0) at `docs/workshops/001-flash-session-demo-day-journey.md:95` names only `BidPlaced` in its events column. Workshop slice 5.4 (P1) at line 115 names `BuyItNowOptionRemoved` "*(system, on first bid)*" as a separate slice with its own dependency on slice 3.1. Lived `src/CritterBids.Auctions/PlaceBidHandler.cs:117-119` emits both events atomically inside the same DCB acceptance write when `state.BuyItNowAvailable` is true and this is the first acceptance. M3-S4 implemented slice 3.1 and slice 5.4 in the same handler.

**Resolution.** Two valid expressions of the domain coexist. The workshop's slice-table separation is design intent: "BIN removal is a thing that happens on first bid" stands as a discrete journey beat. The lived implementation merges both into one atomic acceptance write, which is correct because the events share a DCB transactional envelope and any partial commit would corrupt boundary state. Narrative Moment 4 renders BIN removal as a side-effect of the first acceptance, atomic with `BidPlaced`, and references this finding inline. No workshop edit needed; readers comparing the slice table to the code will find the design history in this finding.

---

### Finding 009 - `PlaceBid` command carries `CreditCeiling` directly in M3; workshop scenarios show the ceiling on `ParticipantSessionStarted`, not on the command

**Routing:** document-as-intentional

**Surfaced at:** Moment 4

**Discrepancy.** Workshop slice 3.1 happy-path command shape in `docs/workshops/001-scenarios.md`: `PlaceBid { ListingId, BidderId, Amount }`. Workshop slice 3.2 ExceedsCreditCeiling rejection has the ceiling on `ParticipantSessionStarted { ParticipantId, CreditCeiling }`. Lived `src/CritterBids.Auctions/PlaceBid.cs:17-22` carries `CreditCeiling` on the command directly. The contract's docstring at `PlaceBid.cs:9-12` explains the M3 transitional shape: "Credit ceiling travels on the command in M3 because Participants does not yet emit a `ParticipantSessionStarted` event the Auctions boundary can load. M4's Session aggregate will carry the credit ceiling in its own stream, at which point this field drops off the command shape and is read from `BidConsistencyState`."

**Resolution.** The workshop's command shape stands as the design target; lived M3 is the stepping stone. M4-S5+ work converges to the workshop's design when the Session aggregate ships and projects credit-ceiling state into the bid-acceptance DCB. Narrative Moment 4's Interaction paragraph names the M3 transitional shape and references this finding. No workshop edit needed; the convergence is a tracked work item alongside Finding 006's Flash session machinery.
