# Workshop 002 — Auctions BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-04-09
**Status:** Complete — all 3 phases done

**Scope:** The Auctions BC internals. Aggregate state machines, saga designs, DCB boundary model, and resolution of parked questions from Workshop 001.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. ProductOwner on standby.

**Companion file:** [`002-scenarios.md`](./002-scenarios.md) — Phase 3 Given/When/Then scenarios for all Auctions BC internals.

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

| Component | Scenarios | Happy Path | Edge/Rejection |
|---|---|---|---|
| DCB — PlaceBid | 15 | 4 | 11 |
| DCB — BuyNow | 4 | 1 | 3 |
| Auction Closing Saga | 11 | 4 | 7 |
| Proxy Bid Manager | 11 | 4 | 7 |
| Session Aggregate | 7 | 3 | 4 |

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
