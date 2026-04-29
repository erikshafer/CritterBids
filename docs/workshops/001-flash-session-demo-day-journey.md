# Workshop 001 — Flash Session Demo-Day Journey

**Type:** User Journey (cross-cutting)
**Date started:** 2026-04-09
**Status:** Complete — all 5 phases done

**Scope:** The complete happy-path demo scenario. A presenter runs a Flash Session with live audience participation, from QR scan through obligation fulfillment.

**Personas active:** All eight.

**Tradeoffs acknowledged:** Horizontal map (BC handoffs, integration events, milestone scope). Defers aggregate internals, saga state machines, DCB designs, and failure paths to BC-focused workshops.

**Companion file:** [`001-scenarios.md`](./001-scenarios.md) — Phase 5 Given/When/Then scenarios for all P0 slices.

---

## Cast

The Flash demo journey is a multi-actor scenario. Specific named protagonists belong to narratives that implement subsets of this workshop; this Cast names the workshop-grain roles and the bounded contexts each role exercises. See [Narrative 001](../narratives/001-bidder-wins-flash-auction.md) §"Cast" for one canonical instantiation (single bidder, happy path) with named protagonists.

**Human actors:**

- **The bidder** - any participant placing bids. Anonymous, system-named via the `<Adjective><Animal><Number>` convention, system-assigned `BidderId`, hidden credit ceiling drawn from the band $200-$1000 in $100 steps. Multiple bidders may compete on the same listing concurrently. Onstage in Tiers 0, 3-5, and 8-9.
- **The seller** - any participant who has completed seller registration and published one or more listings to the operator's Flash session. Identified by `SellerId`. Onstage in Tier 1; offstage during the auction lifecycle.
- **The Buy It Now purchaser** - a bidder variant who exercises the BIN option (slice 5.3) rather than placing competing bids. Same anonymous-bidder profile as the regular bidder.
- **The proxy bidder** - a bidder who registers a proxy bid (slice 5.5) with a maximum amount and lets the system bid on their behalf (slice 5.6). Same anonymous-bidder profile.
- **The auction operator** - the human running the demo. Creates Flash sessions (slice 2.1), attaches listings (slice 2.2), starts the session (slice 2.3), and monitors via the ops dashboard. Distinct from bidders and sellers; uses the ops SPA (Tier 9 P0 slices 9.6-9.7).

**Bounded contexts:**

- **Participants** - owns anonymous-bidder session lifecycle, `BidderId`, hidden credit ceiling. Onstage in Tier 0.
- **Selling** - owns the listing's pre-publish lifecycle (draft, submit, approve, publish, revise, end-early). Onstage in Tier 1.
- **Auctions** - owns the in-flight bidding lifecycle: `BiddingOpened`, `BidPlaced`, the bid-placement DCB, `ExtendedBiddingTriggered`, the Auction Closing saga, terminal `ListingSold` / `ListingPassed` / `BuyItNowPurchased`. Onstage in Tiers 2, 3, and 5.
- **Listings** - owns the read-side: `CatalogListingView`, catalog browse, listing detail. Bidders read from Listings throughout. Onstage in Tiers 1, 3, and 8.
- **Relay** - owns SignalR pushes: BiddingHub for participants, OperationsHub for ops, Outbid notifications. Onstage in Tier 4.
- **Settlement** - owns the post-resolution financial workflow. Onstage in Tier 6.
- **Obligations** - owns the post-sale coordination saga: tracking, delivery, fulfillment. Onstage in Tier 7.
- **Operations** - owns the ops-facing read models: `LiveLotBoardView`, `BidFeedView`, `SettlementProgressView`, `ObligationStatusView`, `SessionManagementView`. Onstage in Tiers 2-7 alongside the BC owning each event.

Onstage/offstage status per slice or per Moment is finer-grained than the workshop tracks; each named narrative implementing a subset of W001 dramatizes specific protagonists at that grain. Consult the implementing narrative for per-Moment Cast detail.

## Setting

The workshop covers the complete Flash demo journey from QR-code scan through obligation fulfillment. The Setting establishes the policy posture inherited by every slice; specific numerical examples (reserve amounts, hammer prices, fee percentages, named bidders) belong to narratives that dramatize specific journeys.

**The Flash demo session.** The conference-demo vehicle. Sessions are session-bounded auctions where multiple listings open and close around the same five-to-ten-minute hot-phase window. Distinct from the eBay-style Timed Auction format (days-long; listings open and close independently). Operators create sessions, attach published listings, and start the session manually; bidders join anonymously via QR scan. The MVP default Flash session duration is five minutes from start; the operator may set the duration at session-create time.

**Auction policy posture.** Bid increments are two-tier: $1 under $100, $5 at $100+. Platform default; not seller-configurable. Extended bidding is enabled per-listing at publish time, with a per-listing trigger window (default 30 seconds) and extension (default 15 seconds); chains until the platform-level `MaxDuration` cap. Reserve existence and value remain confidential between seller and Settlement until a bid first crosses the threshold; the bidder learns of the reserve only via the `ReserveMet` push from Relay. Buy It Now is invalidated by the first regular bid (`BuyItNowOptionRemoved`); the BIN price is constrained at submission to be greater than or equal to the reserve.

**Bidder policy posture.** Anonymous bidder sessions are created on QR scan via `StartParticipantSession`; no email, no password. The bidder receives a system-assigned `BidderId` and a system-named display name (e.g., `SwiftFerret42`). Each bidder receives a hidden credit ceiling drawn at session-mint time from the band $200-$1000 in $100 steps; the ceiling is enforced per-bid by the bid-placement DCB and is not displayed to the bidder. Credit ceiling is a per-bid maximum, not a running balance, so multiple wins do not exhaust it.

**Seller policy posture.** Sellers register via `RegisterAsSeller` (slice 0.3); the registration is verified by the API gateway pattern before subsequent listing operations. Listings progress through Draft → Submitted → Approved → Published in MVP via a single `SubmitListing` command (atomic three-event chain; auto-approval). Post-publish revisions are restricted to Title, Description, and ShippingTerms; the price, reserve, format, and BIN price are immutable after publish. Sellers ending early after bids do not receive payment.

**Settlement and infrastructure posture.** MVP runs virtual credit: no real payment processor, no chargebacks, no fraud reversals. Settlement records financial events for audit but no real money moves. SignalR delivery to all connected clients is healthy; the BiddingHub and OperationsHub are both reachable. RabbitMQ is up; the Wolverine outbox drains cleanly. There are no rate limits in play, no surge effects, and no manual operator interventions during the demo. All endpoints carry `[AllowAnonymous]` through M6 per the project's intentional pre-auth posture.

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

Listings BC (Marten): `CatalogListingView`, `WatchlistView`, `ParticipantBidHistoryView` (tentative)
Operations BC (Polecat): `SessionManagementView`, `LiveLotBoardView`, `BidFeedView`, `SettlementProgressView`, `ObligationStatusView`
Relay BC (SignalR): `LiveBidOverlay`

> **Note (post-M3-S6):** The originally-planned `ListingDetailView` was unified into `CatalogListingView` during M3-S6 under OQ2 Path A (string `Status` field, symmetry with `Format`). The detail-read endpoint at `/api/listings/{id}` loads the same `CatalogListingView` document by primary key. See narrative 001 Finding 003.

---

## Phase 4 — Identify Slices

*(Full slice tables, dependency graph, and milestone mapping retained below. This is the primary reference for implementation planning.)*

Each slice is a vertical cut through the storyboard: Screen → Command → Event(s) → View. A slice is independently deliverable and testable. Slices are organized into dependency tiers — a tier cannot start until the tiers above it are complete.

### Narrative Cross-References

The following slices are implemented by published narratives. Each narrative cites its slices via `Implements:` lines on its Moments; this section is the inverse index per the narratives README v0.1 bidirectional-referencing convention. Phase 3 of the foundation refresh adds the broader retroactive backfill across all four CritterBids workshops; this section is populated only with the directly-implemented slices for each narrative as it lands.

- **[Narrative 001 - Bidder Wins a Flash Auction (Happy Path)](../narratives/001-bidder-wins-flash-auction.md)** implements slices 0.2, 1.3, 1.4, 2.3, 3.1, 3.3, 4.1, 4.3, 5.1, and 6.1. Single-bidder perspective; happy-path; covers Tier 0 through Tier 6 P0 slices. Slices 2.3, 4.1, 4.3, and 6.1 are forward-spec because the Auctions-side Flash session aggregate (M4-S5/M4-S6), Relay BC (M4 Tier 4), and Settlement BC (M5) are unshipped at narrative authoring time.
- **[Narrative 002 - Winner Clears Settlement (Happy Path)](../narratives/002-winner-clears-settlement.md)** implements slices 6.1 and 6.3. Single-bidder perspective (SwiftFerret42 carrying continuity from narrative 001); happy-path; companion to narrative 001 Moment 8 at finer grain (the Settlement saga's per-phase progression Initiated, ReserveChecked, WinnerCharged, FeeCalculated, PayoutIssued, Completed). Both slices are forward-spec because the Settlement BC (slice 6.1) and the Relay BC's settlement-completed broadcast (slice 6.3) are unshipped at narrative authoring time.

### Slice Principles

- **Backend-first.** Every slice can be implemented as API + events + tests before any frontend exists.
- **One BC per slice where possible.** Cross-BC slices exist but should be minimized.
- **Slices produce testable facts.** Each slice has a Given/When/Then scenario in `001-scenarios.md`.
- **Frontend slices are separate.** "Build the ListingDetailScreen" is a frontend slice. "Place a bid" is a backend slice.

### Tier 0 - Foundation

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 0.1 | Project scaffolding | - | - | - | All | P0 | done |
| 0.2 | Start anonymous session | `StartParticipantSession` | `ParticipantSessionStarted` | - | Participants | P0 | done |
| 0.3 | Register as seller | `RegisterAsSeller` | `SellerRegistrationCompleted` | - | Participants | P0 | done |

### Tier 1 - Listing Lifecycle

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 1.1 | Create draft listing | `CreateDraftListing` | `DraftListingCreated` | - | Selling | P0 | done |
| 1.2 | Submit and publish listing | `SubmitListing` | `ListingSubmitted`, `ListingApproved`, `ListingPublished` | `CatalogListingView` | Selling + Listings | P0 | done |
| 1.3 | Catalog browse (read path) | - (GET) | - | `CatalogListingView` | Listings | P0 | done |
| 1.4 | Listing detail (read path) | - (GET) | - | `CatalogListingView` | Listings | P0 | done |

### Tier 2 - Flash Session Setup

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 2.1 | Create Flash Session | `CreateSession` | `SessionCreated` | `SessionManagementView` | Auctions + Operations | P0 | planned |
| 2.2 | Attach listing to session | `AttachListingToSession` | `ListingAttachedToSession` | `SessionManagementView`, `CatalogListingView` | Auctions + Listings + Operations | P0 | planned |
| 2.3 | Start session (the cascade) | `StartSession` | `SessionStarted`, `BiddingOpened` (×N) | `LiveLotBoardView`, `CatalogListingView` | Auctions + Listings + Operations | P0 | planned |

### Tier 3 - Core Bidding

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 3.1 | Place a bid (happy path) | `PlaceBid` | `BidPlaced` | `LiveLotBoardView`, `BidFeedView` | Auctions + Operations | P0 | done |
| 3.2 | Reject a bid | `PlaceBid` | `BidRejected` | - | Auctions | P0 | done |
| 3.3 | Close listing - sold | *(scheduled)* | `BiddingClosed`, `ListingSold` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P0 | done |
| 3.4 | Close listing - passed | *(scheduled)* | `BiddingClosed`, `ListingPassed` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P0 | done |

### Tier 4 - Real-Time Layer

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 4.1 | BiddingHub - participant SignalR | - | `BidPlaced`, `ListingSold`, `ListingPassed` | `LiveBidOverlay` | Relay | P0 | planned |
| 4.2 | OperationsHub - ops SignalR | - | All integration events | `LiveLotBoardView` (real-time) | Relay | P0 | planned |
| 4.3 | Outbid notification | - | `BidPlaced` | - (push to previous high bidder) | Relay | P0 | planned |

### Tier 5 - Auction Mechanics

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 5.1 | Extended bidding | `PlaceBid` (in trigger window) | `BidPlaced`, `ExtendedBiddingTriggered` | `LiveBidOverlay`, `LiveLotBoardView` | Auctions + Relay + Operations | P0 | done |
| 5.2 | Reserve met signal | *(system, on threshold)* | `ReserveMet` | `LiveBidOverlay` | Auctions + Relay | P1 | in progress |
| 5.3 | Buy It Now purchase | `BuyNow` | `BuyItNowPurchased` | `CatalogListingView`, `LiveLotBoardView` | Auctions + Listings + Operations | P1 | done |
| 5.4 | Buy It Now removal | *(system, on first bid)* | `BuyItNowOptionRemoved` | `CatalogListingView` | Auctions + Listings | P1 | done |
| 5.5 | Register proxy bid | `RegisterProxyBid` | `ProxyBidRegistered` | - | Auctions | P1 | planned |
| 5.6 | Proxy auto-bid | *(system, on competing bid)* | `BidPlaced` (isProxy: true) | Same as 3.1 | Auctions | P1 | planned |

> **Note (post-M3-S5, see narrative 001 Finding 010):** Slice 5.2's Auctions-side event production (`PlaceBidHandler` emits `ReserveMet` when a bid first crosses the reserve threshold) and saga consumption (`AuctionClosingSaga.Handle(ReserveMet)`) are fully shipped in M3 (M3-S4 and M3-S5). The slice retains P1 priority because its defining View - the bidder-facing `LiveBidOverlay` push from Relay's BiddingHub - remains M4 work. Until Relay ships, the reserve meeting is invisible to bidders even though the event and saga state both fire correctly.

### Tier 6 - Settlement

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 6.1 | Settlement saga (happy path) | *(system, on ListingSold)* | `SettlementInitiated` → `SettlementCompleted` | `SettlementProgressView` | Settlement + Operations | P0 | planned |
| 6.2 | Settlement from Buy It Now | *(system, on BuyItNowPurchased)* | Same as 6.1 | Same as 6.1 | Settlement | P1 | planned |
| 6.3 | Seller payout notification | - | `SellerPayoutIssued` | - (push to seller) | Relay | P1 | planned |

### Tier 7 - Obligations

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 7.1 | Obligations saga start | *(system, on SettlementCompleted)* | `PostSaleCoordinationStarted`, `ShippingReminderSent` | `ObligationStatusView` | Obligations + Operations | P1 | planned |
| 7.2 | Provide tracking | `ProvideTracking` | `TrackingInfoProvided` | `ObligationStatusView` | Obligations + Relay | P1 | planned |
| 7.3 | Obligation fulfilled | *(system, on delivery)* | `DeliveryConfirmed`, `ObligationFulfilled` | `ObligationStatusView` | Obligations + Operations | P1 | planned |
| 7.4 | Demo-mode timeout config | - | - | - | Obligations | P1 | planned |

### Tier 8 - Participant Experience Polish

| # | Slice | Command | Events | View | BC | Priority | Status |
|---|-------|---------|--------|------|----|----------|--------|
| 8.1 | Watchlist - add/remove | `AddToWatchlist` / `RemoveFromWatchlist` | `LotWatchAdded` / `LotWatchRemoved` | `WatchlistView` | Listings + Relay | P2 | design |
| 8.2 | Participant bid history | - (GET) | - | `ParticipantBidHistoryView` | Listings (tentative) | P1 | planned |
| 8.3 | MyActivityScreen | - | - | Composite of 8.1 + 8.2 | Frontend | P1 | planned |

### Tier 9 - Frontend

| # | Slice | Screen | Depends on Backend Slices | Priority | Status |
|---|-------|--------|--------------------------|----------|--------|
| 9.1 | Participant LandingScreen | LandingScreen | 0.2 | P0 | planned |
| 9.2 | Participant CatalogScreen | CatalogScreen | 1.3, 1.4 | P0 | planned |
| 9.3 | Participant ListingDetailScreen + PlaceBidSheet | ListingDetailScreen | 3.1, 4.1 | P0 | planned |
| 9.4 | Participant ProxyBidSheet | ProxyBidSheet | 5.5 | P1 | planned |
| 9.5 | Participant MyActivityScreen | MyActivityScreen | 8.2, 8.3 | P1 | planned |
| 9.6 | Ops SessionManagerScreen | SessionManagerScreen | 2.1, 2.2, 2.3 | P0 | planned |
| 9.7 | Ops LiveBoardScreen | LiveBoardScreen | 3.1, 4.2 | P0 | planned |
| 9.8 | Ops BidFeedScreen | BidFeedScreen | 3.1, 4.2 | P1 | planned |
| 9.9 | Ops SettlementScreen | SettlementScreen | 6.1 | P1 | planned |
| 9.10 | Ops ObligationsScreen | ObligationsScreen | 7.1 | P2 | design |

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
| M1 - Skeleton | Tier 0 | `docker compose up`, participant session via API |
| M2 - Listings Pipeline | Tier 1 | Listings appear in catalog via API |
| M3 - Auctions Core | Tier 3 + Auctions Timed-only foundation | Bidding lifecycle for Timed listings via API and tests (Flash session aggregate deferred to M4-S5/M4-S6) |
| M4 - Flash Sessions + Real-Time + Extended | Tier 2 (M4-S5/M4-S6) + Tier 4 + 5.1 | Flash session lifecycle, SignalR push, extended bidding, outbid alerts |
| M5 - Settlement | Tier 6 | `ListingSold` → full settlement flow |
| M6 - Frontend MVP | Tier 9 P0 | Both SPAs, core screens, demo-runnable from browser |
| M7 - Polish | P1 slices | Buy It Now, proxy, obligations, remaining ops screens |

> **Note (post-M4-S1, see narrative 001 Finding 006):** M3 originally scoped Tier 2 (Flash Session Setup) alongside Tier 3 (Core Bidding). Lived M3 shipped Tier 3 and the Auctions-side Timed-only foundation only - the `Listing` aggregate, the auction-closing saga, and the catalog status projections. The Flash session aggregate, `StartSession` command handler, `SessionStartedHandler` fan-out, and Listings-side `SessionMembershipHandler` were deferred to M4-S5 and M4-S6 respectively per the M4-S1 foundation-decisions retrospective.

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
