# Workshop 002 — Auctions BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-04-09
**Status:** Complete — all 3 phases done

**Scope:** The Auctions BC internals. Aggregate state machines, saga designs, DCB boundary model, and resolution of parked questions from Workshop 001.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. ProductOwner on standby.

**Companion file:** [`002-scenarios.md`](./002-scenarios.md) — Phase 3 Given/When/Then scenarios for all Auctions BC internals.

---

## Narrative Cross-References

The following narratives implement slices that this workshop deep-dives. Each narrative cites its slices via `Implements:` lines on its Moments; this section is the inverse index per the narratives README v0.1 bidirectional-referencing convention.

- **[Narrative 005 - Seller Watches Flash Auction Close (Happy Path)](../narratives/005-seller-watches-flash-auction-close.md)** implements W001 slices 2.3 (forward-spec session-start cascade; Moment 1), 3.1 (place bid; Moment 2 multi-bid), 5.2 (reserve met; Moment 2 sub-beat), 5.1 (extended bidding triggered; Moment 3), 3.3 (scheduled close; Moment 4). Single-seller observer-protagonist perspective (GreyOwl12); happy-path; companion to narrative 001 Moments 4-7 at the Auctions-saga grain. Mixed posture: Moment 1 forward-spec for M4-S5/S6 session-start cascade; Moments 2-4 lived M3 + M4-S1. Zero new findings surfaced; the lived M3 code matches W002 + M3-S5 / M3-S5b retros; narrative 001 Finding 011 (`TryComputeExtension` bug) verified as fixed in place via Phase 2.5 PR #14; narrative 001 Finding 012 (saga loads `SellerId` via `AggregateStreamAsync`) routed `document-as-intentional` and the lived inline comment preserves the design rationale. Cross-narrative consistency with narrative 001 Moments 4-7 (the same auction from SwiftFerret42's bidder-perspective) holds.

---

## Ubiquitous Language

The Auctions BC owns the in-flight bidding lifecycle: from `BiddingOpened` through resolution (`ListingSold` / `ListingPassed` / `BuyItNowPurchased`). Pre-publish lifecycle is owned by Selling ([W004 §3](./004-selling-bc-deep-dive.md#ubiquitous-language)); post-resolution settlement is owned by Settlement ([W003 §3](./003-settlement-bc-deep-dive.md#ubiquitous-language)).

Each term carries a one-line definition with optional cross-references and "what it is *not*" notes. Domain events are catalogued in [`docs/vision/domain-events.md`](../vision/domain-events.md) and in this workshop's Phase 1 architecture summary; events are not duplicated here.

| Term | Definition | Notes |
|---|---|---|
| **Listing** | The auctionable unit, identified by `ListingId`. From the Auctions BC perspective, the lifecycle runs from `BiddingOpened` through `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, or `ListingWithdrawn`. | Pre-publish lifecycle (Draft, Submitted, Approved, Published, Revised) is owned by Selling BC; see W004 §3. |
| **Bidder** | A participant placing bids, identified by `BidderId`. | Same `BidderId` is the `ParticipantId` from Participants BC. |
| **Flash Session** | A short, session-bounded auction format (5-10 minute hot phase) where multiple listings open and close around the same window. The conference-demo vehicle. | Distinct from Timed Auction. Owns the `SessionAggregate` lifecycle (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`). M3 ships the Timed-only foundation; Flash session machinery deferred to M4-S5/M4-S6. |
| **Timed Auction** | An eBay-style days-long auction format. Listings open and close independently. | Distinct from Flash Session. The Auctions BC's Timed-only path is fully shipped in M3. |
| **Reserve** | The minimum hammer price below which the listing does not sell at auction. Carried as `ReservePrice` on the listing; may be null (no reserve). | Settlement BC (W003 §3) is the financial authority for the binding reserve comparison; Auctions emits `ReserveMet` as a real-time UX signal only. Same source data, different roles. |
| **Hammer Price** | The final accepted bid amount when bidding closes; the price at which the listing sells. Recorded on `ListingSold`. | If hammer price is below reserve at close, `ListingPassed` fires instead. |
| **Buy It Now** | A fixed-price sale option presented alongside auction bidding. The first regular bid invalidates BIN (`BuyItNowOptionRemoved`); a buyer may purchase at the BIN price (`BuyItNowPurchased`). | BIN price must be >= reserve per the Selling BC invariant (W004 §3); Settlement skips reserve check on BIN settlements. |
| **Extended Bidding** | A timer-extension mechanism that pushes the auction's scheduled close forward when a bid lands inside the trigger window. | Producer-monotone after Phase 2.5 (Finding 011). Chains until `MaxDuration` cap. Proxy bids can trigger extension (W002 Phase 1 #4). |
| **MaxDuration** | A platform-level cap on how long extended bidding can stretch a listing's total close time. | Platform default for MVP; not seller-configurable until post-MVP. Workshop scenarios 1.14 / 1.15 demonstrate the boundary. |
| **Trigger Window** | The time interval before the scheduled close during which a bid triggers extended bidding. | Carried on the listing's `BiddingOpened` payload at session start. |
| **Credit Ceiling** | A per-bid maximum carried on the bidder's session, enforced by the DCB at bid placement. | Per-bid maximum, not a running balance - see W003 §3 for the Settlement-side rationale (W003 Phase 1 Part 4). |
| **Bid Increment** | The minimum amount above the current high bid that the next bid must meet. CritterBids policy: $1 under $100, $5 at $100+. | Platform default, not per-listing. |
| **BidConsistencyState (DCB)** | The Dynamic Consistency Boundary state object loaded from listing-stream + bidder-stream events at bid placement. Stateless per request: loads, validates, produces events, forgets. | The DCB is the authority for bid acceptance/rejection. See [`docs/skills/dynamic-consistency-boundary.md`](../skills/dynamic-consistency-boundary.md). |
| **Auction Closing Saga** | A Marten-document saga, one per listing, that schedules the `CloseAuction` timer at `BiddingOpened` and resolves the auction at close to `ListingSold` or `ListingPassed`. | States: AwaitingBids, Active, Extended, Closing, Resolved. Terminates on `ListingWithdrawn`. |
| **Proxy Bid Manager Saga** | A Marten-document saga, one per (listing, bidder), that watches `BidPlaced` events and re-bids automatically up to the proxy's max amount. | Composite key: UUID v5 from `ListingId` + `BidderId`. Reactive - registration alone does not bid; waits for the next competing bid. |
| **Session Aggregate** | The Marten event-sourced aggregate owning the Flash Session lifecycle (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`). | Deferred to M4-S5/M4-S6 per the post-M4-S1 Note in W001. |

---

## Phase 1 — Brain Dump: Internal Structure

*(Condensed. See git history for full Phase 1 output with code sketches.)*

### Key Design Decisions

**Session fan-out (W001 Parked #2):** Option B adopted. Session aggregate produces `SessionStarted`. A separate Wolverine handler reacts and produces `BiddingOpened` per listing.

**Reserve check authority (W001 Parked #5):** Auctions owns the real-time UX signal (`ReserveMet`, fired from DCB handler). Settlement owns the financial authority (`ReserveCheckCompleted`). Same source data, different roles. `ListingPublished` carries the reserve value to both BCs.

**ProxyBidExhausted (W001 Parked #3):** Promoted to 🔵 Integration. Relay pushes a distinct "your proxy has been exceeded" notification, separate from the generic outbid alert.

**Extended bidding chaining (W001 Parked #4):** No count limit. Extensions can chain. `MaxDuration` config caps total listing duration.

**Proxy bids and extended bidding (W001 Parked #8):** Yes, proxy bids can trigger extended bidding. The DCB handler is bid-source-agnostic.

### Architecture Summary

```
Session Aggregate (Marten, event-sourced)
  → SessionCreated, ListingAttachedToSession, SessionStarted

DCB Boundary Model: BidConsistencyState
  → EventTagQuery loads from listing stream + bidder stream
  → PlaceBid handler produces: BidPlaced, BidRejected, BuyItNowOptionRemoved,
    ReserveMet, ExtendedBiddingTriggered, BuyItNowPurchased

Auction Closing Saga (Marten document, 1 per listing)
  → States: AwaitingBids → Active → Extended → Closing → Resolved
  → Starts on BiddingOpened, schedules CloseAuction timer

Proxy Bid Manager Saga (Marten document, 1 per listing×bidder)
  → States: Active → Exhausted / ListingClosed
  → Composite key: UUID v5 from ListingId + BidderId
```

---

## Phase 2 — Storytelling: A Listing's Complete Lifecycle

*(Condensed. See git history for full Phase 2 output with 12-step walkthrough and 3 alternate paths.)*

### Questions Resolved in Phase 2

| # | Question | Resolution |
|---|----------|------------|
| 1 | Bid increment strategy | Two-tier: $1 under $100, $5 at $100+. Platform default, not per-listing. |
| 2 | `BidRejected` stream placement | Separate stream, tagged with `ListingId`. Excluded from DCB tag query. |
| 3 | Saga tracks vs DCB read at close | Incremental tracking via handlers. Trust Wolverine delivery guarantees. |
| 4 | `ListingWithdrawn` saga interaction | Terminates both Auction Closing saga and all Proxy Bid Manager sagas. No reserve check, no sold/passed. |
| 5 | Proxy bid rejection handling | Proxy stores `BidderCreditCeiling` at registration. Caps auto-bid at `min(next, max, ceiling)`. Self-corrects via `BidPlaced` event stream. |
| 6 | `MaxDuration` ownership | Platform default for MVP (e.g., 2x original). Not seller-configurable until post-MVP. |

### Key Lifecycle Insights

The listing lifecycle has one primary path (open → bids → close → sold/passed) and two short-circuit paths (Buy It Now and withdrawal). The three components never call each other directly — they communicate entirely through events in the stream.

- **DCB** is stateless per request: loads, validates, produces events, forgets
- **Auction Closing Saga** tracks state incrementally, decides the outcome at close
- **Proxy Bid Manager** is a reactive loop: watches `BidPlaced`, fires back until exhaustion or close

---

## Phase 3 — Scenarios (Given/When/Then)

**48 scenarios** covering all Auctions BC internals: 16 happy-path, 32 edge/rejection cases.

Full scenarios in companion file: **[`002-scenarios.md`](./002-scenarios.md)**

### Coverage by Component

| Component | Scenarios | Happy Path | Edge/Rejection | Status |
|---|---|---|---|---|
| DCB - PlaceBid | 15 | 4 | 11 | done |
| DCB - BuyNow | 4 | 1 | 3 | done |
| Auction Closing Saga | 11 | 4 | 7 | done |
| Proxy Bid Manager | 11 | 4 | 7 | planned |
| Session Aggregate | 7 | 3 | 4 | planned |

### Key Scenario Highlights

**Proxy bidding war (4.10):** Two proxies on the same listing escalate against each other until the weaker proxy exhausts. The stronger proxy wins at one increment above the weaker's max. Correct eBay behavior, completes in milliseconds.

**MaxDuration cap (1.14, 1.15):** Extended bidding is allowed when the new close time stays within the cap, blocked when it would exceed it. Two scenarios demonstrate the boundary.

**Credit ceiling cap on proxy (4.9):** The proxy caps its auto-bid at `min(nextBid, maxAmount, creditCeiling)`. When the capped amount can't beat the competing bid, the proxy exhausts rather than entering a retry loop.

**ListingWithdrawn terminates everything (3.10, 4.8):** Both the Auction Closing saga and any active Proxy Bid Manager sagas terminate immediately on withdrawal. No reserve evaluation, no sold/passed. Different from ListingPassed.

**Proxy registration timing (4.11):** Proxy is reactive, not proactive. Registration alone doesn't trigger a bid. The proxy waits for the next competing `BidPlaced` to fire.

### Remaining Open Questions

| # | Question | Persona | Notes |
|---|----------|---------|-------|
| 7 | `BidRejected` stream: dedicated Marten type or general audit pattern? | `@BackendDeveloper` | Implementation detail |
| 8 | Two-proxy bidding war: worth a specific integration test? | `@QA` | Yes — scenario 4.10 covers it |
| 9 | `BiddingOpened` payload: carry full config or saga loads from stream? | `@Architect` | Currently carries all config. Keeps saga start self-contained |

These are implementation-level questions that can be resolved during coding rather than requiring another workshop phase.

---

## Workshop 002 — Complete Output Summary

| Artifact | Count |
|---|---|
| Workshop 001 parked questions resolved | 5 of 5 targeting Auctions BC |
| Phase 1 internal questions raised | 6 |
| Phase 2 internal questions resolved | 6 of 6 (zero carry-forward) |
| Vocabulary changes | 1 (`ProxyBidExhausted` → integration) |
| Aggregate designs | 1 (Session) |
| DCB boundary models | 1 (BidConsistencyState) |
| Saga state machines | 2 (Auction Closing, Proxy Bid Manager) |
| Given/When/Then scenarios | 48 (16 happy, 32 edge) |
| Remaining questions (implementation-level) | 3 |
