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
