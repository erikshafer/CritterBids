# CritterBids — Bounded Contexts

This document describes each bounded context's purpose, ownership, storage, and integration points. It is a high-level design reference, not an implementation specification. Detailed event models, aggregate shapes, and handler designs are produced through Event Modeling workshops.

No BC references another BC's internals directly. The only shared dependency is `CritterBids.Contracts`, which defines the integration event types that cross BC boundaries via Wolverine messages over RabbitMQ.

---

## The Eight Bounded Contexts

### Participants

**Purpose:** Manages anonymous participant sessions. This is CritterBids' entire identity story. No email, no password, no account creation. A participant is created the moment they scan a QR code or hit the platform URL.

**What it owns:**

- `ParticipantSession` aggregate — display name (generated), hidden credit ceiling (randomly assigned), bidder ID, session state
- `SellerProfile` aggregate — tracks whether a participant has completed seller registration and their selling history
- Seller registration flow — a one-time gate before a participant can create listings

**Key design decisions:**

- Credit ceilings are assigned randomly and never revealed to the participant. They create natural bidding constraints without requiring real money.
- Display names are generated (e.g. "SwiftFerret42") — fun, anonymous, legible on the ops dashboard.
- Bidder IDs are the participant's identifier across all BCs. Formatted as "Bidder 42" or similar in UI contexts.

**Storage:** PostgreSQL via Marten (ADR 011 — All-Marten Pivot). Originally designed for SQL Server / Polecat for BI tooling access; migrated to Marten in M2-S5 to eliminate the Wolverine dual-store conflict (ADR 010) and align with the idiomatic Critter Stack bootstrap pattern. The BI tooling rationale is preserved as a stretch goal in ADR 003.

**Integration out:** `ParticipantSessionStarted`, `SellerRegistrationCompleted`, `ParticipantSessionEnded`

---

### Selling

**Purpose:** The self-service seller flow. A verified seller creates a listing, configures its parameters, and publishes it. This BC is the entry point for all auction activity — everything downstream is ultimately triggered by `ListingPublished`.

**What it owns:**

- `SellerListing` aggregate — draft lifecycle, all seller-configured parameters
- Listing validation — automated, no human review in MVP
- Post-publish seller actions — revise, end early, relist

**Key design decisions:**

- Sellers own all listing parameters: starting bid, reserve price (confidential), Buy It Now price, duration, extended bidding toggle and configuration, shipping terms.
- The reserve price is passed downstream as an opaque value. The Auctions BC never sees the raw reserve — it only receives `ReserveMet` or `ReserveNotMet` signals. Settlement does the actual comparison.
- Extended bidding is seller-configurable: toggle on/off, trigger window, extension amount.
- Flash listings (Session-based) inherit their duration from the Session rather than a seller-chosen duration.

**Listing lifecycle:**

```
Draft → Submitted → Approved → Published
                 ↘ Rejected → (back to Draft)
```

**Storage:** PostgreSQL via Marten

**Integration out:** `ListingPublished`, `ListingRevised`, `ListingEndedEarly`, `ListingRelisted`

---

### Auctions

**Purpose:** The core BC. Owns the bidding mechanics, lot lifecycle, DCB enforcement, the Auction Closing saga, and the Proxy Bid Manager saga. Everything that happens between a listing opening for bids and a winner being declared lives here.

**What it owns:**

- `Listing` aggregate — bidding state, current high bid, bid count, reserve status, close time
- `Session` aggregate — flash auction container, coordinates simultaneous listing opens (optional, for demo format only)
- **Auction Closing saga** — initiated by a scheduled close message. Handles reserve check, winner declaration, anti-snipe extension (cancel and reschedule close message if bid arrives in configured window), and no-sale resolution.
- **Proxy Bid Manager saga** — one instance per bidder per listing. Reacts to competing `BidPlaced` events, auto-bids up to the bidder's proxy maximum, terminates on listing close or maximum exceeded. Correlated on `(ListingId, BidderId)`.
- DCB boundary models via Marten's `EventTagQuery` + `[BoundaryModel]` — enforces bid consistency under concurrent bidder load.

**Key design decisions:**

- `BiddingClosed` (mechanical fact — bidding stopped) is intentionally separate from `ListingSold` / `ListingPassed` (business outcomes). Downstream BCs subscribe to the outcome events, not the mechanical close event.
- Buy It Now removes itself from a listing after the first bid is placed (`BuyItNowOptionRemoved`).
- The Session concept exists only inside this BC and only for flash/demo auctions. Regular timed listings have no container.
- The Proxy Bid Manager is a Wolverine saga, not a special framework type. Saga correlation is crisp: composite key of `ListingId + BidderId`.

**Storage:** PostgreSQL via Marten — highest-throughput BC, DCB APIs require Marten.

**Integration in:** `ListingPublished` (Selling), `ParticipantSessionStarted` (Participants)

**Integration out:** `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`, `BiddingOpened`, `BidPlaced`, `BidRejected`, `BuyItNowPurchased`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `ListingSold`, `ListingPassed`, `ListingWithdrawn`

---

### Listings

**Purpose:** The public browsable catalog surface. Listing search, filtering by category and price, watchlists, watch counts (social proof). Read-heavy, no write contention.

**What it owns:**

- Catalog read models — projected from Selling and Auctions events via Marten multi-stream projections
- Search index — full-text search via Marten/PostgreSQL
- Watchlist — per-participant watch list entries

**Key design decisions:**

- Listings is a projection-first BC. It originates almost no domain events. Its read models are built entirely from events produced by Selling and Auctions.
- The catalog shows listing status (open, closed, sold, passed) but deliberately does not show live bid amounts. Live bidding data comes from the Relay BC via SignalR.
- Watch counts are social proof — visible to all participants. The watchlist itself is private to the participant who created it.

**Storage:** PostgreSQL via Marten

**Integration in:** `ListingPublished`, `ListingRevised`, `ListingEndedEarly`, `ListingRelisted` (Selling); `ListingAttachedToSession`, `SessionStarted`, `BiddingOpened`, `ListingSold`, `ListingPassed`, `ListingWithdrawn` (Auctions)

**Integration out:** `LotWatchAdded`, `LotWatchRemoved`

---

### Settlement

**Purpose:** Resolves what is owed after a listing closes with a winner. Charges the winner's credit ceiling, calculates the platform's final value fee, records the seller payout. Virtual in demo mode but structurally identical to a real payment integration.

**What it owns:**

- Settlement saga — triggered by `ListingSold` or `BuyItNowPurchased`
- Reserve comparison — Settlement holds the opaque reserve value from `ListingPublished` and performs the final comparison against the hammer price
- Fee calculation — final value fee as a configurable percentage of the hammer price
- Seller payout record — hammer price minus fee

**Key design decisions:**

- Settlement is financially authoritative. Credit ceiling charges and seller payouts are recorded here.
- The reserve check lives in Settlement, not Auctions. Auctions publishes `ReserveMet` as a signal based on threshold crossing, but the binding comparison is done by Settlement using the confidential reserve value it received from `ListingPublished`.
- No real payment processor in MVP. Credit ceilings are virtual. The settlement flow is structurally real — the seam for Stripe, Square, or similar is clean.

**Storage:** PostgreSQL via Marten (ADR 011 — All-Marten Pivot). Originally designed for SQL Server / Polecat for financial audit reporting; migrated to Marten to unify the storage layer. The financial reporting rationale is preserved as a stretch goal in ADR 003.

**Integration in:** `ListingSold`, `BuyItNowPurchased` (Auctions); `ListingPublished` (Selling, for reserve value)

**Integration out:** `SellerPayoutIssued`, `PaymentFailed`, `SettlementCompleted`

---

### Obligations

**Purpose:** Coordinates the post-sale handoff between winner and seller. Watches both parties honor their commitments. Drives a scheduled reminder and escalation chain, cancels messages when obligations are met early, and manages disputes if someone does not follow through.

**What it owns:**

- Post-sale coordination saga — triggered by `SettlementCompleted`
- Shipping reminder chain — scheduled messages with cancellation on early fulfillment
- Escalation path — missed deadlines escalate to Operations staff review
- Dispute sub-workflow — open, resolve, close
- Carrier tracking seam — stubbed in MVP, real webhook receiver in production

**Key design decisions:**

- The obligations saga is a timeout chain with cancellable scheduled messages. This is the canonical "cancel and reschedule" pattern for Wolverine sagas.
- Disputes are simple in MVP: open, ops resolve, close. No appeals, no multi-round negotiation.
- Tracking info provision by the seller cancels pending reminders immediately via `bus.ScheduleAsync()` cancellation.

**Storage:** PostgreSQL via Marten

**Integration in:** `SettlementCompleted` (Settlement)

**Integration out:** `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`

---

### Relay

**Purpose:** All outbound communication and real-time push. Routes integration events from every other BC to the right participant via the right channel — SignalR for in-session alerts, email/SMS seams for production.

**What it owns:**

- SignalR hub connections — manages participant connections and delivers real-time push
- Notification routing — maps integration events to the correct participant(s) and channel
- Notification history projection — participants can view their notification feed

**Key design decisions:**

- Relay is a pure consumer. It never originates domain events.
- It is intentionally named Relay rather than Notifications or Correspondence to reflect its actual role — it receives signals from every BC and forwards them outward. It does not generate content; it routes it.
- Two SignalR hubs live here: `BiddingHub` (participant-facing, real-time bid feed) and `OperationsHub` (staff-facing, live ops dashboard feed).

**Storage:** PostgreSQL via Marten (notification history only)

**Integration in:** Events from every other BC.

**Integration out:** None (outbound to external channels only — SignalR, email seam, SMS seam)

---

### Operations

**Purpose:** Internal staff view and the projector-facing dashboard for live demonstrations. Aggregates read models across all BCs. Provides real-time visibility into lot activity, saga states, settlement queue, obligation pipeline, and flagged sessions. Also owns staff-initiated commands and the demo reset capability.

**What it owns:**

- Cross-BC aggregate projections — live lot board, bid activity feed, settlement queue, obligation status, dispute queue, participant activity
- Staff command handlers — force-close a listing, flag a participant session, resolve a dispute, start a Flash Session, reset demo state
- Live ops feed — SignalR hub for real-time dashboard updates
- Staff authentication seam — config-driven passphrase in MVP, extensible to full staff identity

**Key design decisions:**

- The Operations dashboard is the "look at the engine running" moment in a conference demo. It should be legible from a projector — real-time, high-contrast, showing saga state and message activity alongside business state.
- The demo reset mechanism lives here. In MVP, resetting between conference sessions is handled by Docker volume removal. A more graceful `DemoResetInitiated` command that cascades through BCs is a post-MVP milestone.
- The ops dashboard and participant-facing app are separate React SPAs pointing at the same API host. Two browser windows on the same projector tells the audience the story without narration.

**Storage:** PostgreSQL via Marten (ADR 011 — All-Marten Pivot). Originally designed for SQL Server / Polecat for BI tooling and live reporting; migrated to Marten to unify the storage layer. The BI rationale is preserved as a stretch goal in ADR 003.

**Integration in:** Integration events from all BCs.

**Integration out:** None (staff commands are intra-BC; Operations consumes but does not publish integration events)

---

## Integration Topology

```
Participants ─── ParticipantSessionStarted ─────────────► Auctions
             ─── ParticipantSessionStarted ─────────────► Relay
             ─── SellerRegistrationCompleted ───────────► Relay

Selling ─── ListingPublished ───────────────────────────► Auctions
        ─── ListingPublished ───────────────────────────► Listings
        ─── ListingPublished ───────────────────────────► Settlement (reserve value)
        ─── ListingRevised, ListingEndedEarly ──────────► Listings
        ─── ListingPublished, ListingRevised ───────────► Relay
        ─── all significant events ─────────────────────► Operations

Auctions ─── SessionCreated ────────────────────────────► Relay
         ─── SessionCreated ────────────────────────────► Operations
         ─── ListingAttachedToSession ──────────────────► Listings
         ─── ListingAttachedToSession ──────────────────► Operations
         ─── SessionStarted ────────────────────────────► Listings
         ─── SessionStarted ────────────────────────────► Relay
         ─── SessionStarted ────────────────────────────► Operations
         ─── BiddingOpened ─────────────────────────────► Listings
         ─── BidPlaced ─────────────────────────────────► Relay
         ─── ListingSold, BuyItNowPurchased ────────────► Settlement
         ─── ListingSold, ListingPassed, ListingWithdrawn ► Listings
         ─── all significant events ────────────────────► Relay
         ─── all significant events ────────────────────► Operations

Settlement ─── SettlementCompleted ─────────────────────► Obligations
           ─── SellerPayoutIssued, PaymentFailed ───────► Relay
           ─── all events ─────────────────────────────► Operations

Obligations ─── ObligationFulfilled, DisputeOpened ─────► Relay
            ─── all events ────────────────────────────► Operations

Listings ─── LotWatchAdded, LotWatchRemoved ────────────► Relay
```

**Transports:**

- **RabbitMQ** — all inter-BC integration events
- **Wolverine in-process** — intra-BC command and query handling
- **SignalR** — Relay `BiddingHub` to participant browsers, Relay `OperationsHub` to staff dashboard
- **Scheduled messages** — auction close timers, anti-snipe reschedules, obligation deadline chain
- **HTTP seam** — carrier tracking in Obligations (stubbed in demo mode)

---

## Storage Summary

| BC | Database | Engine | Rationale |
|---|---|---|---|
| Participants | PostgreSQL | Marten | All-Marten pivot (ADR 011) — migrated from Polecat/SQL Server |
| Selling | PostgreSQL | Marten | Event-sourced aggregate, standard Critter Stack path |
| Auctions | PostgreSQL | Marten | Highest-throughput BC, DCB APIs require Marten |
| Listings | PostgreSQL | Marten | Multi-stream projections, full-text search |
| Settlement | PostgreSQL | Marten | All-Marten pivot (ADR 011) — migrated from Polecat/SQL Server |
| Obligations | PostgreSQL | Marten | Saga with cancellable scheduled messages |
| Relay | PostgreSQL | Marten | Notification history projection |
| Operations | PostgreSQL | Marten | All-Marten pivot (ADR 011) — migrated from Polecat/SQL Server |

---

## What CritterSupply Calls This

For developers coming from CritterSupply, here is a rough mapping of analogous concerns:

| CritterBids | CritterSupply Analogue | Key Difference |
|---|---|---|
| Participants | Identity / Auth | Anonymous sessions, no persistent accounts |
| Selling | Catalog management (seller side) | Self-service, no platform intake workflow |
| Auctions | Ordering / Cart | Time-pressured, concurrent contention, DCB is core |
| Listings | Catalog (buyer side) | Projection-first, no write path of its own |
| Settlement | Payments | Virtual credit, not real money (MVP) |
| Obligations | Fulfillment / Shipping | Seller-initiated shipping, timeout-driven |
| Relay | Notifications | SignalR is load-bearing, not a nicety |
| Operations | Backoffice | Live dashboard for conference demos |
