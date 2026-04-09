# Workshop 001 — Flash Session Demo-Day Journey

**Type:** User Journey (cross-cutting)
**Date started:** 2026-04-09
**Status:** Complete — all 5 phases done

**Scope:** The complete happy-path demo scenario. A presenter runs a Flash Session with live audience participation, from QR scan through obligation fulfillment.

**Personas active:** All eight.

**Tradeoffs acknowledged:** Horizontal map (BC handoffs, integration events, milestone scope). Defers aggregate internals, saga state machines, DCB designs, and failure paths to BC-focused workshops.

**Companion file:** [`001-scenarios.md`](./001-scenarios.md) — Phase 5 Given/When/Then scenarios for all P0 slices.

---

## Phase 1 — Verification Brain Dump

*(Condensed. See git history for full Phase 1 output.)*

Walked the demo-day journey (28 beats). Vocabulary covers the journey. One event added: `ListingAttachedToSession`. Five questions parked for BC workshops.

---

## Phase 2 — Storytelling

*(Condensed. See git history for full Phase 2 output.)*

Four speeds of time: Prep (days) → Arrival (minutes) → Hot Phase (5-10 min) → Resolution (seconds). Extended bidding desynchronizes the close. Peak concurrency: N parallel listing streams, settlement sagas, SignalR delivery to 40+ clients. PO decision: sagas need demo-mode timeout config with a cap. Three additional questions parked.

---

## Phase 3 — Storyboarding

*(Condensed. See git history for full Phase 3 output.)*

11 screens identified (6 participant, 5 ops). 10 read models across Listings (Marten), Operations (Polecat), and Relay (SignalR). Storyboard walkthrough: 22 rows connecting screens → commands → events → views. Key takeaway: participant complexity is in real-time updates within screens, not navigation. Five additional questions parked.

**Screen Inventory (reference):**

Participant (`critterbids-web`): LandingScreen, CatalogScreen, ListingDetailScreen, PlaceBidSheet, ProxyBidSheet, MyActivityScreen

Ops (`critterbids-ops`): SessionManagerScreen, LiveBoardScreen, BidFeedScreen, SettlementScreen, ObligationsScreen

**View Inventory (reference):**

Listings BC (Marten): `CatalogListingView`, `ListingDetailView`, `WatchlistView`, `ParticipantBidHistoryView` (tentative)
Operations BC (Polecat): `SessionManagementView`, `LiveLotBoardView`, `BidFeedView`, `SettlementProgressView`, `ObligationStatusView`
Relay BC (SignalR): `LiveBidOverlay`

---

## Phase 4 — Identify Slices

*(Full slice tables, dependency graph, and milestone mapping retained below. This is the primary reference for implementation planning.)*

Each slice is a vertical cut through the storyboard: Screen → Command → Event(s) → View. A slice is independently deliverable and testable. Slices are organized into dependency tiers — a tier cannot start until the tiers above it are complete.

### Slice Principles

- **Backend-first.** Every slice can be implemented as API + events + tests before any frontend exists.
- **One BC per slice where possible.** Cross-BC slices exist but should be minimized.
- **Slices produce testable facts.** Each slice has a Given/When/Then scenario in `001-scenarios.md`.
- **Frontend slices are separate.** "Build the ListingDetailScreen" is a frontend slice. "Place a bid" is a backend slice.

### Tier 0 — Foundation

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 0.1 | Project scaffolding | — | — | — | All | P0 |
| 0.2 | Start anonymous session | `StartParticipantSession` | `ParticipantSessionStarted` | — | Participants | P0 |
| 0.3 | Register as seller | `RegisterAsSeller` | `SellerRegistrationCompleted` | — | Participants | P0 |

### Tier 1 — Listing Lifecycle

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 1.1 | Create draft listing | `CreateDraftListing` | `DraftListingCreated` | — | Selling | P0 |
| 1.2 | Submit and publish listing | `SubmitListing` | `ListingSubmitted`, `ListingApproved`, `ListingPublished` | `CatalogListingView` | Selling + Listings | P0 |
| 1.3 | Catalog browse (read path) | — (GET) | — | `CatalogListingView` | Listings | P0 |
| 1.4 | Listing detail (read path) | — (GET) | — | `ListingDetailView` | Listings | P0 |

### Tier 2 — Flash Session Setup

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 2.1 | Create Flash Session | `CreateSession` | `SessionCreated` | `SessionManagementView` | Auctions + Operations | P0 |
| 2.2 | Attach listing to session | `AttachListingToSession` | `ListingAttachedToSession` | `SessionManagementView`, `CatalogListingView` | Auctions + Listings + Operations | P0 |
| 2.3 | Start session (the cascade) | `StartSession` | `SessionStarted`, `BiddingOpened` (×N) | `LiveLotBoardView`, `CatalogListingView` | Auctions + Listings + Operations | P0 |

### Tier 3 — Core Bidding

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 3.1 | Place a bid (happy path) | `PlaceBid` | `BidPlaced` | `LiveLotBoardView`, `BidFeedView` | Auctions + Operations | P0 |
| 3.2 | Reject a bid | `PlaceBid` | `BidRejected` | — | Auctions | P0 |
| 3.3 | Close listing — sold | *(scheduled)* | `BiddingClosed`, `ListingSold` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P0 |
| 3.4 | Close listing — passed | *(scheduled)* | `BiddingClosed`, `ListingPassed` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P0 |

### Tier 4 — Real-Time Layer

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 4.1 | BiddingHub — participant SignalR | — | `BidPlaced`, `ListingSold`, `ListingPassed` | `LiveBidOverlay` | Relay | P0 |
| 4.2 | OperationsHub — ops SignalR | — | All integration events | `LiveLotBoardView` (real-time) | Relay | P0 |
| 4.3 | Outbid notification | — | `BidPlaced` | — (push to previous high bidder) | Relay | P0 |

### Tier 5 — Auction Mechanics

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 5.1 | Extended bidding | `PlaceBid` (in trigger window) | `BidPlaced`, `ExtendedBiddingTriggered` | `LiveBidOverlay`, `LiveLotBoardView` | Auctions + Relay + Operations | P0 |
| 5.2 | Reserve met signal | *(system, on threshold)* | `ReserveMet` | `LiveBidOverlay` | Auctions + Relay | P1 |
| 5.3 | Buy It Now purchase | `BuyNow` | `BuyItNowPurchased` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P1 |
| 5.4 | Buy It Now removal | *(system, on first bid)* | `BuyItNowOptionRemoved` | `ListingDetailView`, `CatalogListingView` | Auctions + Listings | P1 |
| 5.5 | Register proxy bid | `RegisterProxyBid` | `ProxyBidRegistered` | — | Auctions | P1 |
| 5.6 | Proxy auto-bid | *(system, on competing bid)* | `BidPlaced` (isProxy: true) | Same as 3.1 | Auctions | P1 |

### Tier 6 — Settlement

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 6.1 | Settlement saga (happy path) | *(system, on ListingSold)* | `SettlementInitiated` → `SettlementCompleted` | `SettlementProgressView` | Settlement + Operations | P0 |
| 6.2 | Settlement from Buy It Now | *(system, on BuyItNowPurchased)* | Same as 6.1 | Same as 6.1 | Settlement | P1 |
| 6.3 | Seller payout notification | — | `SellerPayoutIssued` | — (push to seller) | Relay | P1 |

### Tier 7 — Obligations

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 7.1 | Obligations saga start | *(system, on SettlementCompleted)* | `PostSaleCoordinationStarted`, `ShippingReminderSent` | `ObligationStatusView` | Obligations + Operations | P1 |
| 7.2 | Provide tracking | `ProvideTracking` | `TrackingInfoProvided` | `ObligationStatusView` | Obligations + Relay | P1 |
| 7.3 | Obligation fulfilled | *(system, on delivery)* | `DeliveryConfirmed`, `ObligationFulfilled` | `ObligationStatusView` | Obligations + Operations | P1 |
| 7.4 | Demo-mode timeout config | — | — | — | Obligations | P1 |

### Tier 8 — Participant Experience Polish

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 8.1 | Watchlist — add/remove | `AddToWatchlist` / `RemoveFromWatchlist` | `LotWatchAdded` / `LotWatchRemoved` | `WatchlistView` | Listings + Relay | P2 |
| 8.2 | Participant bid history | — (GET) | — | `ParticipantBidHistoryView` | Listings (tentative) | P1 |
| 8.3 | MyActivityScreen | — | — | Composite of 8.1 + 8.2 | Frontend | P1 |

### Tier 9 — Frontend

| # | Slice | Screen | Depends on Backend Slices | Priority |
|---|-------|--------|--------------------------|----------|
| 9.1 | Participant LandingScreen | LandingScreen | 0.2 | P0 |
| 9.2 | Participant CatalogScreen | CatalogScreen | 1.3, 1.4 | P0 |
| 9.3 | Participant ListingDetailScreen + PlaceBidSheet | ListingDetailScreen | 3.1, 4.1 | P0 |
| 9.4 | Participant ProxyBidSheet | ProxyBidSheet | 5.5 | P1 |
| 9.5 | Participant MyActivityScreen | MyActivityScreen | 8.2, 8.3 | P1 |
| 9.6 | Ops SessionManagerScreen | SessionManagerScreen | 2.1, 2.2, 2.3 | P0 |
| 9.7 | Ops LiveBoardScreen | LiveBoardScreen | 3.1, 4.2 | P0 |
| 9.8 | Ops BidFeedScreen | BidFeedScreen | 3.1, 4.2 | P1 |
| 9.9 | Ops SettlementScreen | SettlementScreen | 6.1 | P1 |
| 9.10 | Ops ObligationsScreen | ObligationsScreen | 7.1 | P2 |

### Slice Summary

**Total: 34 slices** — 18 P0, 13 P1, 3 P2.

### Dependency Graph (P0 only)

```
0.1 (scaffolding)
 ├── 0.2 (session) ─────────────────────────── 9.1 (LandingScreen)
 ├── 0.3 (seller reg)
 │    └── 1.1 (draft) → 1.2 (publish) ──┬── 1.3 (catalog) ── 9.2 (CatalogScreen)
 │                                        ├── 1.4 (detail)
 │                                        └── 2.1 (create session)
 │                                             └── 2.2 (attach) ── 9.6 (SessionManagerScreen)
 │                                                  └── 2.3 (start session)
 │                                                       └── 3.1 (place bid) ──┬── 9.3 (ListingDetailScreen)
 │                                                       │    3.2 (reject bid) │
 │                                                       │    5.1 (extended)   │
 │                                                       │                     │
 │                                                       ├── 3.3 (close-sold)  │
 │                                                       │    3.4 (close-pass) │
 │                                                       │    └── 6.1 (settlement)
 │                                                       │
 │                                                       └── 4.1 (BiddingHub)
 │                                                            4.2 (OpsHub) ── 9.7 (LiveBoardScreen)
 │                                                            4.3 (outbid)
```

### Milestone Mapping (proposed)

| Milestone | Scope | Deliverable |
|---|---|---|
| M1 — Skeleton | Tier 0 | `docker compose up`, participant session via API |
| M2 — Listings Pipeline | Tier 1 | Listings appear in catalog via API |
| M3 — Flash Session Core | Tiers 2 + 3 | Full auction lifecycle via API and tests |
| M4 — Real-Time + Extended | Tier 4 + 5.1 | SignalR push, extended bidding, outbid alerts |
| M5 — Settlement | Tier 6 | `ListingSold` → full settlement flow |
| M6 — Frontend MVP | Tier 9 P0 | Both SPAs, core screens, demo-runnable from browser |
| M7 — Polish | P1 slices | Buy It Now, proxy, obligations, remaining ops screens |

---

## Phase 5 — Scenarios (Given/When/Then)

**34 scenarios** covering all 18 P0 slices: 18 happy-path, 16 edge/rejection cases.

Full scenarios are in the companion file: **[`001-scenarios.md`](./001-scenarios.md)**

Scenarios cover: anonymous session creation, seller registration (with rejection cases), listing draft and publish, catalog and detail read paths, Flash Session create/attach/start (with rejection for unpublished listings and already-started sessions), bid placement and rejection (below high bid, credit ceiling, closed listing), auction close sold and passed (reserve not met, no bids), SignalR push routing (BiddingHub, OperationsHub, outbid notification), extended bidding (in-window, outside-window, disabled), and the full settlement saga.

Each scenario follows the format:
- **Given:** precondition events already in the stream
- **When:** command issued (or scheduled timer fires)
- **Then:** new events produced and/or view state assertions

Scenarios are testable as specifications using the Critter Stack testing patterns (Alba + Testcontainers + xUnit + Shouldly). See `docs/skills/critter-stack-testing-patterns.md`.

---

## All Parked Questions

Consolidated from all phases:

| # | Question | Persona | Target | Phase |
|---|----------|---------|--------|-------|
| 1 | Listing UI before session starts? | `@UX` | Frontend / Listings | 1 |
| 2 | `SessionStarted` → N × `BiddingOpened` fan-out | `@Architect` | Auctions BC | 1 |
| 3 | Promote `ProxyBidExhausted` to integration? | `@QA` | Auctions BC | 1 |
| 4 | Multiple sequential extended bidding triggers | `@QA` | Auctions BC | 1 |
| 5 | Reserve check authority: Auctions vs Settlement | `@QA`/`@Architect` | Auctions + Settlement | 1 |
| 6 | Demo-mode timeout config for Obligations? | `@ProductOwner` | Obligations BC | 2 |
| 7 | UI state between timer-zero and outcome event? | `@UX`/`@FrontendDeveloper` | Frontend | 2 |
| 8 | Can a proxy bid trigger extended bidding? | `@QA` | Auctions BC | 2 |
| 9 | Where does `ParticipantBidHistoryView` live? | `@Architect` | Listings or Auctions BC | 3 |
| 10 | Ops screens: separate routes or tabbed dashboard? | `@FrontendDeveloper` | Frontend | 3 |
| 11 | Auto-navigate ops to LiveBoard on session start? | `@UX` | Frontend | 3 |
| 12 | "Closing..." UI state between timer-zero and outcome? | `@FrontendDeveloper` | Frontend / Auctions | 3 |
| 13 | How does seller provide tracking? Dedicated screen or inline? | `@UX` | Frontend / Obligations | 3 |
| 14 | Automated approval: single handler chain or separate steps? | `@BackendDeveloper` | Selling BC | 4 |
| 15 | Frontend milestone: one or split participant/ops? | `@ProductOwner` | Milestone scoping | 4 |

**PO decisions captured:**
- Sagas need demo-mode timeout configuration with a cap (Phase 2)
