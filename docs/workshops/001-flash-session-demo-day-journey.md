# Workshop 001 — Flash Session Demo-Day Journey

**Type:** User Journey (cross-cutting)
**Date started:** 2026-04-09
**Status:** In progress — Phase 5 next

**Scope:** The complete happy-path demo scenario. A presenter runs a Flash Session with live audience participation, from QR scan through obligation fulfillment.

**Personas active:** All eight.

**Tradeoffs acknowledged:** Horizontal map (BC handoffs, integration events, milestone scope). Defers aggregate internals, saga state machines, DCB designs, and failure paths to BC-focused workshops.

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

Each slice is a vertical cut through the storyboard: Screen → Command → Event(s) → View. A slice is independently deliverable and testable. Slices are organized into dependency tiers — a tier cannot start until the tiers above it are complete.

### Slice Principles

- **Backend-first.** Every slice can be implemented as API + events + tests before any frontend exists. Frontend is layered on afterward. This means the first milestones are backend-heavy and the frontend milestone comes later as a "wire it up" pass.
- **One BC per slice where possible.** Cross-BC slices exist (e.g., "Start Session" touches Auctions, Listings, Relay, Operations) but should be minimized.
- **Slices produce testable facts.** Each slice has a Given/When/Then scenario (Phase 5). If you can't write a test for it, it's not a slice — it's a task inside a slice.
- **Frontend slices are separate.** "Build the ListingDetailScreen" is a frontend slice. "Place a bid" is a backend slice. They're connected but independently deliverable.

### Tier 0 — Foundation

These slices establish identity, the project structure, and the shared infrastructure. Nothing else works without them.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 0.1 | Project scaffolding | — | — | — | All | P0 |
| 0.2 | Start anonymous session | `StartParticipantSession` | `ParticipantSessionStarted` | — | Participants | P0 |
| 0.3 | Register as seller | `RegisterAsSeller` | `SellerRegistrationCompleted` | — | Participants | P0 |

**0.1 — Project scaffolding:** The `CritterBids.Api` host, all 8 BC class library projects, `CritterBids.Contracts`, Docker Compose (PostgreSQL + SQL Server + RabbitMQ), Marten and Polecat configuration per BC with `AutoApplyTransactions()`, Wolverine + RabbitMQ wiring, `AddXyzModule()` registration for each BC. No domain logic — just the empty shell that compiles, starts, and connects to infrastructure. This is a milestone unto itself.

**0.2 — Start anonymous session:** The QR scan landing. Participant hits a URL, system creates a session with a generated display name and hidden credit ceiling, returns the session info. This is the Participants BC's primary write path.

**0.3 — Register as seller:** One-time gate. Participant completes seller registration. Required before they can create listings. In MVP this might be a single endpoint call with no real verification.

### Tier 1 — Listing Lifecycle

The Selling BC's write path. Listings must exist before sessions can reference them.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 1.1 | Create draft listing | `CreateDraftListing` | `DraftListingCreated` | — | Selling | P0 |
| 1.2 | Submit and publish listing | `SubmitListing` | `ListingSubmitted`, `ListingApproved`, `ListingPublished` | `CatalogListingView` | Selling + Listings | P0 |
| 1.3 | Catalog browse (read path) | — (GET) | — | `CatalogListingView` | Listings | P0 |
| 1.4 | Listing detail (read path) | — (GET) | — | `ListingDetailView` | Listings | P0 |

**1.2 notes:** Automated approval in MVP — `SubmitListing` triggers the full chain in one handler. Three events, one user action. `ListingPublished` crosses to Listings (catalog projection), Auctions, and Settlement (reserve value).

**1.3 + 1.4:** Read-only slices. The Listings BC Marten projections must be built and queryable. These are the first projections in the system. No frontend yet — just API endpoints returning JSON.

### Tier 2 — Flash Session Setup

The Auctions BC's session management and the Operations BC's first read models.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 2.1 | Create Flash Session | `CreateSession` | `SessionCreated` | `SessionManagementView` | Auctions + Operations | P0 |
| 2.2 | Attach listing to session | `AttachListingToSession` | `ListingAttachedToSession` | `SessionManagementView`, `CatalogListingView` | Auctions + Listings + Operations | P0 |
| 2.3 | Start session (the cascade) | `StartSession` | `SessionStarted`, `BiddingOpened` (×N) | `LiveLotBoardView`, `CatalogListingView` | Auctions + Listings + Operations | P0 |

**2.3 is the most cross-cutting slice in the system.** `StartSession` produces `SessionStarted` (integration), which triggers the handler that produces `BiddingOpened` for each attached listing (also integration). Listings, Relay, and Operations all react. This slice is the demo-day button — it must work correctly under concurrent consumer load.

**Dependency:** 2.2 requires Tier 1 (listings must be published). 2.3 requires 2.2 (listings must be attached).

### Tier 3 — Core Bidding

The Auctions BC's primary write path. This is the hot loop of the system.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 3.1 | Place a bid (happy path) | `PlaceBid` | `BidPlaced` | `LiveLotBoardView`, `BidFeedView` | Auctions + Operations | P0 |
| 3.2 | Reject a bid | `PlaceBid` | `BidRejected` | — | Auctions | P0 |
| 3.3 | Close listing — sold | *(scheduled)* | `BiddingClosed`, `ListingSold` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P0 |
| 3.4 | Close listing — passed | *(scheduled)* | `BiddingClosed`, `ListingPassed` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P0 |

**3.1 is the DCB slice.** This is where Marten's `EventTagQuery` + `[BoundaryModel]` enforce consistency under concurrent bidder load. The core mechanic of CritterBids. Implementation details are an Auctions BC workshop topic, but the slice is: a bid is accepted and becomes the new high bid, or it's rejected (3.2).

**3.3 + 3.4 are the Auction Closing saga.** A scheduled close message fires at `BiddingOpened.scheduledCloseAt`. The saga evaluates reserve status and publishes either `ListingSold` or `ListingPassed`. Two slices because the outcomes are distinct and testable independently.

**Dependency:** 3.1 requires Tier 2 (bidding must be open). 3.3/3.4 require 3.1 (bids must exist, or explicitly not exist for "passed").

### Tier 4 — Real-Time Layer

SignalR infrastructure. This tier enables the live demo experience.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 4.1 | BiddingHub — participant SignalR | — | `BidPlaced`, `ListingSold`, `ListingPassed` | `LiveBidOverlay` | Relay | P0 |
| 4.2 | OperationsHub — ops SignalR | — | All integration events | `LiveLotBoardView` (real-time) | Relay | P0 |
| 4.3 | Outbid notification | — | `BidPlaced` | — (push to previous high bidder) | Relay | P0 |

**4.1 + 4.2 are the SignalR hubs.** `BiddingHub` pushes to participant browsers. `OperationsHub` pushes to the ops dashboard. Both are Wolverine handlers in the Relay BC that receive integration events and forward them to SignalR groups.

**4.3 is the first "routed" notification.** When `BidPlaced` arrives, Relay must identify the previous high bidder and push an outbid alert specifically to them. This is the simplest case of Relay's routing logic.

**Dependency:** Tier 4 can be built in parallel with Tier 3 (the handlers exist, they just need events to consume). But testing requires Tier 3 to produce events.

### Tier 5 — Auction Mechanics

These slices add the mechanics that make the demo dramatic. P0 for extended bidding (it's the anti-snipe demo moment), P1 for the rest.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 5.1 | Extended bidding | `PlaceBid` (in trigger window) | `BidPlaced`, `ExtendedBiddingTriggered` | `LiveBidOverlay`, `LiveLotBoardView` | Auctions + Relay + Operations | P0 |
| 5.2 | Reserve met signal | *(system, on threshold)* | `ReserveMet` | `LiveBidOverlay` | Auctions + Relay | P1 |
| 5.3 | Buy It Now purchase | `BuyNow` | `BuyItNowPurchased` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P1 |
| 5.4 | Buy It Now removal | *(system, on first bid)* | `BuyItNowOptionRemoved` | `ListingDetailView`, `CatalogListingView` | Auctions + Listings | P1 |
| 5.5 | Register proxy bid | `RegisterProxyBid` | `ProxyBidRegistered` | — | Auctions | P1 |
| 5.6 | Proxy auto-bid | *(system, on competing bid)* | `BidPlaced` (isProxy: true) | Same as 3.1 | Auctions | P1 |

**5.1 — Extended bidding** is P0 because it's the anti-snipe mechanic and a key demo moment. A bid in the trigger window cancels the scheduled close message and reschedules it. The audience sees the close time push out in real time. This is the Auction Closing saga's most interesting state transition.

**5.3 + 5.4 — Buy It Now** is P1 but strongly desirable for the demo. It shows an alternate path (listing goes straight to settlement, bypassing the close saga) and creates visual contrast on the ops dashboard (one listing sold instantly while others are still bidding).

**5.5 + 5.6 — Proxy bidding** is P1. The Proxy Bid Manager saga is a separate Wolverine saga per (ListingId, BidderId). It's a compelling "ghost bidding" demo moment but the demo works without it.

### Tier 6 — Settlement

The financial resolution. Required for the demo to feel complete.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 6.1 | Settlement saga (happy path) | *(system, on ListingSold)* | `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`, `SellerPayoutIssued`, `SettlementCompleted` | `SettlementProgressView` | Settlement + Operations | P0 |
| 6.2 | Settlement from Buy It Now | *(system, on BuyItNowPurchased)* | Same as 6.1 | Same as 6.1 | Settlement | P1 |
| 6.3 | Seller payout notification | — | `SellerPayoutIssued` | — (push to seller) | Relay | P1 |

**6.1 is the settlement saga from `ListingSold`.** Virtual credit ceiling debit, fee calculation, seller payout. The ops dashboard shows each step. This completes the "listing sold → money moves" arc that makes the demo satisfying.

**Dependency:** 6.1 requires Tier 3 (listings must close with winners). 6.2 requires 5.3 (Buy It Now).

### Tier 7 — Obligations

Post-sale coordination. The least urgent for the live demo but completes the full lifecycle.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 7.1 | Obligations saga start | *(system, on SettlementCompleted)* | `PostSaleCoordinationStarted`, `ShippingReminderSent` | `ObligationStatusView` | Obligations + Operations | P1 |
| 7.2 | Provide tracking | `ProvideTracking` | `TrackingInfoProvided` | `ObligationStatusView` | Obligations + Relay | P1 |
| 7.3 | Obligation fulfilled | *(system, on delivery)* | `DeliveryConfirmed`, `ObligationFulfilled` | `ObligationStatusView` | Obligations + Operations | P1 |
| 7.4 | Demo-mode timeout config | — | — | — | Obligations | P1 |

**7.4 — Demo-mode timeout config** is a configuration slice, not a feature slice. It provides the seam for short timeouts in demo mode so the presenter can show the full obligations lifecycle in 2-3 minutes instead of days. Per the PO decision from Phase 2.

**Dependency:** Tier 7 requires Tier 6 (`SettlementCompleted` triggers the saga).

### Tier 8 — Participant Experience Polish

These slices round out the participant-facing SPA.

| # | Slice | Command | Events | View | BC | Priority |
|---|-------|---------|--------|------|----|----------|
| 8.1 | Watchlist — add/remove | `AddToWatchlist` / `RemoveFromWatchlist` | `LotWatchAdded` / `LotWatchRemoved` | `WatchlistView` | Listings + Relay | P2 |
| 8.2 | Participant bid history | — (GET) | — | `ParticipantBidHistoryView` | Listings (tentative) | P1 |
| 8.3 | MyActivityScreen | — | — | Composite of 8.1 + 8.2 | Frontend | P1 |

### Tier 9 — Frontend

Frontend slices are separate from backend slices. Each screen is its own slice. The backend API must exist first.

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

---

### Slice Summary

**Total slices: 34**

| Priority | Count | Slices |
|----------|-------|--------|
| P0 | 18 | 0.1-0.3, 1.1-1.4, 2.1-2.3, 3.1-3.4, 4.1-4.3, 5.1, 6.1, 9.1-9.3, 9.6-9.7 |
| P1 | 13 | 5.2-5.6, 6.2-6.3, 7.1-7.4, 8.2-8.3, 9.4-9.5, 9.8-9.9 |
| P2 | 3 | 8.1, 9.10 |

**P0 slices by BC:**

| BC | P0 Slices |
|---|---|
| Participants | 0.2, 0.3 |
| Selling | 1.1, 1.2 |
| Listings | 1.3, 1.4 (projections from Selling + Auctions events) |
| Auctions | 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 5.1 |
| Settlement | 6.1 |
| Relay | 4.1, 4.2, 4.3 |
| Operations | (projections built as part of other slices) |
| Frontend | 9.1, 9.2, 9.3, 9.6, 9.7 |

**Auctions has the most P0 slices (8).** This is expected — it's the core BC. It's also the most complex, with the DCB, the Auction Closing saga, extended bidding, and the session cascade. This BC will need the deepest BC-focused workshop.

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

The tiers and priorities suggest a natural milestone structure:

**Milestone 1 — Skeleton:** Tier 0 (scaffolding + identity). All 8 BC projects created, infrastructure wired, basic Participants BC with session creation. Deliverable: `docker compose up` starts the system, you can create a participant session via API.

**Milestone 2 — Listings Pipeline:** Tier 1 (Selling + Listings). Create and publish listings, catalog browse and detail read paths. Deliverable: listings appear in the catalog via API.

**Milestone 3 — Flash Session Core:** Tiers 2 + 3 (session setup + bidding + close). The complete auction lifecycle backend. Deliverable: you can create a session, attach listings, start it, place bids, and listings close with sold/passed outcomes — all via API and tests.

**Milestone 4 — Real-Time + Extended Bidding:** Tier 4 + slice 5.1 (SignalR + extended bidding). Deliverable: bid events push via SignalR, extended bidding works, outbid notifications fire.

**Milestone 5 — Settlement:** Tier 6 (settlement saga). Deliverable: `ListingSold` triggers the full settlement flow, ops dashboard shows progress.

**Milestone 6 — Frontend MVP:** Tier 9 P0 slices (both SPAs, core screens only). Deliverable: the full demo can be run from a browser — participant phones and ops projector.

**Milestone 7 — Polish:** P1 slices (Buy It Now, proxy bidding, reserve met, obligations, MyActivity, remaining ops screens). Deliverable: the complete happy-path journey runs end to end with all mechanics.

> **Note (`@ProductOwner`):** These milestone boundaries are proposals from the workshop. They should be validated against capacity and timeline before becoming committed scope. Each milestone should have its own scoping document in `docs/milestones/`.

---

### Phase 4 Summary

**No vocabulary changes.**

**34 slices identified** across 10 tiers. 18 P0, 13 P1, 3 P2.

**7 milestones proposed** from scaffolding through polish.

**New parked questions:**

| # | Question | Persona | Target |
|---|----------|---------|--------|
| 14 | Is automated listing approval a single handler chain or separate steps? | `@BackendDeveloper` | Selling BC |
| 15 | Should Tier 9 frontend slices be one milestone or split participant/ops? | `@ProductOwner` | Milestone scoping |

**Key takeaway:** Auctions BC dominates the P0 slice count (8 of 18). This confirms it needs the first and deepest BC-focused workshop. The dependency graph shows a clear critical path: scaffolding → listings → session → bidding → settlement → frontend. Parallelism is limited in the early tiers but opens up significantly from Tier 3 onward (real-time, settlement, and frontend can proceed concurrently).

---

## Phase 5 — Scenarios (Given/When/Then)

*Next: Write acceptance scenarios for P0 slices. Each scenario becomes a test specification.*

*(to be continued)*

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
