# Workshop 001 — Flash Session Demo-Day Journey

**Type:** User Journey (cross-cutting)
**Date started:** 2026-04-09
**Status:** In progress — Phase 4 next

**Scope:** The complete happy-path demo scenario from the overview doc. A presenter at a conference runs a Flash Session with live audience participation, from QR scan through obligation fulfillment. This is the demo-day spine.

**Personas active:** All eight (`@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@FrontendDeveloper`, `@QA`, `@ProductOwner`, `@UX`)

**Tradeoffs acknowledged:** This cross-cutting journey produces the horizontal map (BC handoffs, integration events, milestone scope). It defers to BC-focused workshops: aggregate internals, saga state machine details, DCB boundary model design, compensation/failure paths, and the deeper "what if" edge cases.

---

## Phase 1 — Verification Brain Dump

CritterBids already has an event vocabulary (`docs/vision/domain-events.md`). Phase 1 is a verification pass: walk the demo-day journey beat by beat and confirm the vocabulary accounts for everything that happens.

### Act 1 — Setup (before the audience arrives)

**Beat 1: Seller creates listings in advance**
Events: `DraftListingCreated` → `DraftListingUpdated` → `ListingSubmitted` → `ListingApproved` → `ListingPublished`
All in Selling BC. All accounted for.

**Beat 2: Operations staff creates a Flash Session and attaches listings**
Events: `SessionCreated`, `ListingAttachedToSession`
Both in Auctions BC. `ListingAttachedToSession` was **missing** — added during this phase.

**Beat 3: Infrastructure ready**
Stable URL, QR code. Not a domain event.

### Act 2 — Audience arrives

**Beat 4:** `ParticipantSessionStarted` (Participants → Auctions, Relay). Accounted for.

**Beat 5:** Catalog browse. Read path only.

> **Parked (UX):** What does a listing show before the session starts?

### Act 3 — Session goes live

**Beat 6:** `SessionStarted` → `BiddingOpened` (×N). Accounted for.

> **Parked (Architect):** Fan-out handler design.

### Act 4 — Bidding

**Beats 7-13:** `BidPlaced`, `ProxyBidRegistered`, `ProxyBidExhausted`, `BuyItNowPurchased`, `BuyItNowOptionRemoved`, `ReserveMet`. All accounted for.

> **Parked (QA):** Should `ProxyBidExhausted` be integration?

### Act 5 — Close

**Beats 14-17:** `ExtendedBiddingTriggered`, `BiddingClosed`, `ListingSold`/`ListingPassed`. All accounted for.

> **Parked (QA):** Multiple sequential extensions. Reserve check authority tension.

### Act 6 — Settlement

**Beats 18-23:** `SettlementInitiated` → `ReserveCheckCompleted` → `WinnerCharged` → `FinalValueFeeCalculated` → `SellerPayoutIssued` → `SettlementCompleted`. All accounted for.

### Act 7 — Obligations

**Beats 24-28:** `PostSaleCoordinationStarted` → `ShippingReminderSent` → `TrackingInfoProvided` → `DeliveryConfirmed` → `ObligationFulfilled`. All accounted for.

### Phase 1 Summary

**Vocabulary changes:** Added `ListingAttachedToSession` (🔵 Integration, Auctions BC).

**Parked questions:**

| # | Question | Persona | Target |
|---|----------|---------|--------|
| 1 | Listing UI before session starts? | `@UX` | Frontend / Listings |
| 2 | `SessionStarted` → N × `BiddingOpened` fan-out | `@Architect` | Auctions BC |
| 3 | Promote `ProxyBidExhausted` to integration? | `@QA` | Auctions BC |
| 4 | Multiple sequential extended bidding triggers | `@QA` | Auctions BC |
| 5 | Reserve check authority: Auctions vs Settlement | `@QA`/`@Architect` | Auctions + Settlement |

---

## Phase 2 — Storytelling

Phase 2 arranges events into a temporal narrative. Key insight: **time has four distinct speeds** in this journey.

### The Timeline

#### T-days: Prep

Selling BC: `DraftListingCreated` → ... → `ListingPublished` (×3-5)
Auctions BC: `SessionCreated` → `ListingAttachedToSession` (×3-5)
Listings BC: catalog shows listings as "upcoming"

**Hard dependencies:** `ListingPublished` before `ListingAttachedToSession`. `SessionCreated` before attachment. `SellerRegistrationCompleted` before any listing creation. No time pressure.

#### T-minutes: Arrival

`ParticipantSessionStarted` (×30-40, concurrent, independent). Relay enrolls each in SignalR groups. Participants browse catalog. Ops dashboard shows participant count climbing. This is the "countdown" moment.

#### T-0: The Start

`SessionStarted` → `BiddingOpened` (×N). Single command, cascade. Milliseconds. All listings flip to "open" simultaneously via SignalR push. The "go" moment.

#### T+0 to T+5min: Hot Phase

Multiple parallel event streams. Each listing independent: own DCB, own proxy bid managers, own close timer. Buy It Now can short-circuit a listing to Settlement while others still bid. Proxy bids create "ghost bidding." Peak concurrency.

#### T+4:30 to T+5:30: Close

Extended bidding desynchronizes the close — staggered, not simultaneous. `BiddingClosed` (internal) → `ListingSold`/`ListingPassed`. The climax. `ListingPassed` is a legitimate business outcome, not a failure.

**The `BiddingClosed` → outcome gap:** Milliseconds. Participant sees "Closing..." then "Sold!" The UI should not declare the outcome on timer expiry — only on receiving `ListingSold`/`ListingPassed` via SignalR.

#### T+5 to T+8min: Settlement

Per-listing, concurrent. Fast (virtual credit, no external APIs). `SettlementInitiated` → ... → `SettlementCompleted`. Buy It Now settlements are already done by now.

#### T+8min onward: Obligations

Extends beyond demo window. `PostSaleCoordinationStarted` → scheduled reminders → `TrackingInfoProvided` → `ObligationFulfilled`.

> **PO decision captured:** Sagas need demo-mode timeout configuration with a cap to prevent indefinite chains. Details in Obligations BC workshop.

### Dependency Chain

```
SellerRegistrationCompleted → ListingPublished → SessionCreated →
  ListingAttachedToSession → SessionStarted → BiddingOpened →
    BidPlaced (×N) → BiddingClosed → ListingSold →
      SettlementCompleted → PostSaleCoordinationStarted → ObligationFulfilled
```

### Phase 2 Summary

**No vocabulary changes.** Timing insights captured. New parked questions:

| # | Question | Persona | Target |
|---|----------|---------|--------|
| 6 | Demo-mode timeout config for Obligations? | `@ProductOwner` | Obligations BC |
| 7 | UI state between timer-zero and outcome event? | `@UX`/`@FrontendDeveloper` | Frontend |
| 8 | Can a proxy bid trigger extended bidding? | `@QA` | Auctions BC |

---

## Phase 3 — Storyboarding

Phase 3 adds two layers: **screens** above the timeline and **views** (read models) below. Two audiences, two SPAs.

### Screen Inventory

#### Participant Screens (`critterbids-web`)

| Screen | Purpose | When active |
|---|---|---|
| **LandingScreen** | QR scan landing. Generated display name, bidder number, "Browse Listings" CTA. | First load |
| **CatalogScreen** | Browse listings. Cards: title, image, starting bid, status. Filterable. | Arrival through post-close |
| **ListingDetailScreen** | Single listing. Current high bid (SignalR), bid count, countdown timer, bid form, Buy It Now button. | Tap on listing |
| **PlaceBidSheet** | Modal over ListingDetail. Bid amount input, current high bid reference. | Tap "Place Bid" |
| **ProxyBidSheet** | Modal. Max proxy amount input, explanation of proxy mechanics. | Tap "Set Proxy Bid" |
| **MyActivityScreen** | Participant dashboard. Bids (won/active/outbid), watchlist, session info. | Via nav, any time |

#### Ops Screens (`critterbids-ops`)

| Screen | Purpose | When active |
|---|---|---|
| **SessionManagerScreen** | Create sessions, attach listings, preflight check. Participant count. "Start Session" button. | Prep phase |
| **LiveBoardScreen** | Projector view. All session listings: status, current bid, bid count, time remaining, winner. | Session start through close |
| **BidFeedScreen** | Chronological `BidPlaced` stream. Display name, listing, amount, proxy flag, timestamp. | Hot phase |
| **SettlementScreen** | Per-listing saga progress. Status: pending → charging → calculating → paying → complete. | After close |
| **ObligationsScreen** | Per-listing obligation status. Reminder timeline, tracking info, dispute flags. | Post-settlement |

> **Note (`@FrontendDeveloper`):** Ops screens could be tabs within a single dashboard page for the projector. Detail for frontend workshop.

### View (Read Model) Inventory

| View | Owning BC | Type | Source Events | Used By |
|---|---|---|---|---|
| `CatalogListingView` | Listings | Marten projection | `ListingPublished`, `ListingAttachedToSession`, `BiddingOpened`, `ListingSold`, `ListingPassed`, `ListingWithdrawn`, `BuyItNowOptionRemoved` | CatalogScreen |
| `ListingDetailView` | Listings | Marten projection | Same as above + `ListingRevised` | ListingDetailScreen (base) |
| `LiveBidOverlay` | Relay | SignalR push (not persisted) | `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered` | ListingDetailScreen (real-time) |
| `WatchlistView` | Listings | Marten projection | `LotWatchAdded`, `LotWatchRemoved` | MyActivityScreen |
| `ParticipantBidHistoryView` | Listings (tentative) | Marten projection | `BidPlaced`, `BidRejected`, `ListingSold`, `ListingPassed` | MyActivityScreen |
| `SessionManagementView` | Operations | Polecat projection | `SessionCreated`, `ListingAttachedToSession`, `ParticipantSessionStarted` | SessionManagerScreen |
| `LiveLotBoardView` | Operations | Polecat projection | All Auctions integration events | LiveBoardScreen |
| `BidFeedView` | Operations | Polecat projection | `BidPlaced` (all listings) | BidFeedScreen |
| `SettlementProgressView` | Operations | Polecat projection | Settlement integration events | SettlementScreen |
| `ObligationStatusView` | Operations | Polecat projection | Obligations integration events | ObligationsScreen |

> **Open question (`@Architect`):** Where does `ParticipantBidHistoryView` live? Leaning toward Listings BC since it already projects from Auctions events and is the participant-facing read-model BC. Confirm in BC workshop.

### Storyboard Walkthrough

Each row: one interaction point connecting screen, command, events, and view updates.

#### Prep Phase

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 1 | Selling UI | Presenter (seller) | `CreateDraftListing` | `DraftListingCreated` | — |
| 2 | Selling UI | Presenter | `SubmitListing` | `ListingSubmitted`, `ListingApproved`, `ListingPublished` | `CatalogListingView` (appears as "upcoming") |
| 3 | SessionManagerScreen | Presenter (ops) | `CreateSession` | `SessionCreated` | `SessionManagementView` |
| 4 | SessionManagerScreen | Presenter (ops) | `AttachListingToSession` (×N) | `ListingAttachedToSession` (×N) | `SessionManagementView`, `CatalogListingView` |

#### Arrival Phase

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 5 | LandingScreen | Participant | `StartParticipantSession` (implicit) | `ParticipantSessionStarted` | `SessionManagementView` (+1 count) |
| 6 | CatalogScreen | Participant | — (browse) | — | Reads `CatalogListingView` |

> **UX:** Landing → Catalog transition should be automatic after a 1-2 second welcome showing display name. No explicit "continue."

#### Session Start

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 7 | SessionManagerScreen | Presenter (ops) | `StartSession` | `SessionStarted`, `BiddingOpened` (×N) | `LiveLotBoardView` (all "open"), `CatalogListingView` (×N → "open") |

> **UX:** Participant CatalogScreen updates via SignalR. Ops dashboard shifts to LiveBoardScreen.

#### Bidding

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 8 | PlaceBidSheet | Participant | `PlaceBid` | `BidPlaced` | `LiveBidOverlay`, `LiveLotBoardView`, `BidFeedView`, `ParticipantBidHistoryView` |
| 9 | ListingDetailScreen | Other participant | — (watching) | `BidPlaced` via SignalR | `LiveBidOverlay`: "Outbid!" notification |
| 10 | ProxyBidSheet | Participant | `RegisterProxyBid` | `ProxyBidRegistered` (internal) | — (proxy silently active) |
| 11 | — | Proxy system | `PlaceBid` (auto) | `BidPlaced` (isProxy: true) | Same as row 8, proxy flag in `BidFeedView` |
| 12 | ListingDetailScreen | Participant | `BuyNow` | `BuyItNowPurchased` | `CatalogListingView` (→ "sold"), `LiveLotBoardView` |
| 13 | ListingDetailScreen | (system) | — | `BuyItNowOptionRemoved` | `ListingDetailView` (button disappears) |
| 14 | ListingDetailScreen | (system) | — | `ReserveMet` | `LiveBidOverlay` ("Reserve met!" badge) |

> **UX:** `BidRejected` is a direct response to the bidder, not a broadcast. PlaceBidSheet shows specific error: "Bid must be above $22" or "Exceeds your available credit."

> **UX:** `ReserveMet` is a trust signal. Badge, color shift, or animation to mark the transition. Tells watchers the listing will sell if the current bid holds.

#### Close

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 15 | ListingDetailScreen | Participant | `PlaceBid` (trigger window) | `BidPlaced`, `ExtendedBiddingTriggered` | `LiveBidOverlay` (new close time, "Extended!"), `LiveLotBoardView` |
| 16 | ListingDetailScreen | (timer) | — | `BiddingClosed` (int), `ListingSold` | `CatalogListingView` (→ "sold"), `ListingDetailView` (winner, price), `LiveLotBoardView` |
| 17 | ListingDetailScreen | (timer) | — | `BiddingClosed` (int), `ListingPassed` | `CatalogListingView` (→ "passed"), `LiveLotBoardView` |

> **UX (`@FrontendDeveloper`):** Countdown timer is client-side from `BiddingOpened.scheduledCloseAt`. `ExtendedBiddingTriggered` resets it via SignalR. Timer hitting zero shows "Closing..." — outcome confirmed only by `ListingSold`/`ListingPassed` via SignalR. Never declare result client-side.

#### Settlement

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 18 | SettlementScreen | (system) | — | `SettlementInitiated` → ... → `SettlementCompleted` | `SettlementProgressView` (status badges tick through) |
| 19 | (participant phone) | (Relay) | — | `SellerPayoutIssued` | Notification: "Payout for Listing B: $40.50" |

> **UX:** Participants have no settlement screen. They see outcomes on ListingDetail and receive notifications via Relay. Settlement is an ops concern.

#### Obligations

| # | Screen | Actor | Command | Events | View Updated |
|---|--------|-------|---------|--------|-------------|
| 20 | ObligationsScreen | (system) | — | `PostSaleCoordinationStarted` | `ObligationStatusView` (→ "awaiting shipping") |
| 21 | (seller phone) | Seller | `ProvideTracking` | `TrackingInfoProvided` | `ObligationStatusView`, notification to winner |
| 22 | ObligationsScreen | (system) | — | `ObligationFulfilled` | `ObligationStatusView` (→ "fulfilled") |

---

### Phase 3 Summary

**No vocabulary changes.**

**Screens identified:** 6 participant, 5 ops (11 total).

**Views identified:** 10 read models across Listings (5, Marten), Operations (5, Polecat), and Relay (1, SignalR push).

**New parked questions:**

| # | Question | Persona | Target |
|---|----------|---------|--------|
| 9 | Where does `ParticipantBidHistoryView` live? | `@Architect` | Listings or Auctions BC |
| 10 | Ops screens: separate routes or tabbed dashboard? | `@FrontendDeveloper` | Frontend |
| 11 | Auto-navigate ops to LiveBoard on session start? | `@UX` | Frontend |
| 12 | "Closing..." UI state between timer-zero and outcome? | `@FrontendDeveloper` | Frontend / Auctions |
| 13 | How does seller provide tracking? Dedicated screen or inline? | `@UX` | Frontend / Obligations |

**Key takeaway:** Participants interact with surprisingly few screens (Landing, Catalog, ListingDetail with sheets, MyActivity). The complexity is in real-time updates within screens, not navigation. The ops dashboard has more screens but they're all read-only projections. The only ops write action in the entire journey is `StartSession`.

---

## Phase 4 — Identify Slices

*Next: Draw vertical cuts through the storyboard. Each slice is one independently deliverable feature: Screen → Command → Event(s) → View.*

*(to be continued)*

---

## Phase 5 — Scenarios (Given/When/Then)

*(not yet started)*
