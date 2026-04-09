# Workshop 001 — P0 Scenarios (Given/When/Then)

Companion to `001-flash-session-demo-day-journey.md`, Phase 5.
Each scenario is a testable specification for a P0 slice from Phase 4.

**Format:** Given (precondition events in the stream) → When (command issued) → Then (new events produced and/or view state). Happy path first, then key edge cases relevant to the demo-day journey. Deeper edge cases are deferred to BC-focused workshops.

**Conventions:**
- Stream IDs use placeholder UUIDs for readability (e.g., `participant-001`, `listing-A`)
- Event field names use the CritterBids naming conventions: `DateTimeOffset` timestamps with `*At` suffix, aggregate ID as first property
- `sealed record` shapes are sketched, not final — BC workshops will refine field lists

---

## Tier 0 — Foundation

### Slice 0.2 — Start Anonymous Session

**Scenario: Happy path — new participant session**

```
Given:  (empty stream — no prior events for this participant)

When:   StartParticipantSession { }

Then:   ParticipantSessionStarted {
          ParticipantId: "participant-001",
          DisplayName: "SwiftFerret42",    // system-generated
          BidderId: "Bidder 42",           // system-assigned
          CreditCeiling: 500.00,           // randomly assigned, hidden from participant
          StartedAt: "2026-04-09T14:00:00Z"
        }
```

**Scenario: Display name is unique within active sessions**

```
Given:  ParticipantSessionStarted { ParticipantId: "participant-001", DisplayName: "SwiftFerret42" }

When:   StartParticipantSession { }

Then:   ParticipantSessionStarted {
          ParticipantId: "participant-002",
          DisplayName: "BoldPenguin7",     // different from any active session
          ...
        }
```

> **Deferred to BC workshop:** What happens when a participant scans the QR code twice? Rejoin existing session, or new session? Credit ceiling range and distribution strategy.

---

### Slice 0.3 — Register as Seller

**Scenario: Happy path — participant becomes a seller**

```
Given:  ParticipantSessionStarted { ParticipantId: "participant-001" }

When:   RegisterAsSeller { ParticipantId: "participant-001" }

Then:   SellerRegistrationCompleted {
          ParticipantId: "participant-001",
          CompletedAt: "2026-04-09T14:01:00Z"
        }
```

**Scenario: Reject — no active session**

```
Given:  (empty stream — no session for this participant)

When:   RegisterAsSeller { ParticipantId: "participant-999" }

Then:   (command rejected — participant must have an active session)
```

**Scenario: Reject — already registered**

```
Given:  ParticipantSessionStarted { ParticipantId: "participant-001" }
        SellerRegistrationCompleted { ParticipantId: "participant-001" }

When:   RegisterAsSeller { ParticipantId: "participant-001" }

Then:   (command rejected — idempotent, already registered)
```

---

## Tier 1 — Listing Lifecycle

### Slice 1.1 — Create Draft Listing

**Scenario: Happy path — seller creates a draft**

```
Given:  SellerRegistrationCompleted { ParticipantId: "participant-001" }

When:   CreateDraftListing {
          SellerId: "participant-001",
          Title: "Vintage Mechanical Keyboard",
          Description: "Cherry MX Blues, great condition",
          StartingBid: 25.00,
          ReservePrice: 50.00,           // confidential
          BuyItNowPrice: 100.00,         // optional
          ExtendedBiddingEnabled: true,
          ExtendedBiddingTriggerWindow: "00:00:30",  // 30 seconds
          ExtendedBiddingExtension: "00:00:15"       // 15 seconds
        }

Then:   DraftListingCreated {
          ListingId: "listing-A",
          SellerId: "participant-001",
          Title: "Vintage Mechanical Keyboard",
          StartingBid: 25.00,
          ReservePrice: 50.00,
          BuyItNowPrice: 100.00,
          ExtendedBiddingEnabled: true,
          ExtendedBiddingTriggerWindow: "00:00:30",
          ExtendedBiddingExtension: "00:00:15",
          CreatedAt: "2026-04-09T12:00:00Z"
        }
```

**Scenario: Reject — not a registered seller**

```
Given:  ParticipantSessionStarted { ParticipantId: "participant-002" }
        (no SellerRegistrationCompleted)

When:   CreateDraftListing { SellerId: "participant-002", ... }

Then:   (command rejected — participant is not a registered seller)
```

---

### Slice 1.2 — Submit and Publish Listing

**Scenario: Happy path — automated approval chain**

```
Given:  DraftListingCreated { ListingId: "listing-A", SellerId: "participant-001", ... }

When:   SubmitListing { ListingId: "listing-A" }

Then:   ListingSubmitted { ListingId: "listing-A", SubmittedAt: ... }
        ListingApproved { ListingId: "listing-A", ApprovedAt: ... }
        ListingPublished {
          ListingId: "listing-A",
          SellerId: "participant-001",
          Title: "Vintage Mechanical Keyboard",
          StartingBid: 25.00,
          ReservePrice: 50.00,             // opaque — passed to Settlement
          BuyItNowPrice: 100.00,
          ExtendedBiddingEnabled: true,
          ExtendedBiddingTriggerWindow: "00:00:30",
          ExtendedBiddingExtension: "00:00:15",
          PublishedAt: "2026-04-09T12:05:00Z"
        }

View:   CatalogListingView {
          ListingId: "listing-A",
          Title: "Vintage Mechanical Keyboard",
          StartingBid: 25.00,
          Status: "upcoming",              // not yet attached to a session or bidding open
          HasBuyItNow: true,
          HasReserve: true                 // boolean only — amount never exposed
        }
```

**Scenario: Reject — listing already submitted**

```
Given:  DraftListingCreated { ListingId: "listing-A" }
        ListingSubmitted { ListingId: "listing-A" }

When:   SubmitListing { ListingId: "listing-A" }

Then:   (command rejected — listing is no longer in draft state)
```

---

### Slices 1.3 + 1.4 — Catalog Browse and Listing Detail

**Scenario: Catalog returns published listings**

```
Given:  ListingPublished { ListingId: "listing-A", Title: "Vintage Mechanical Keyboard", ... }
        ListingPublished { ListingId: "listing-B", Title: "Rare Pokemon Card", ... }

When:   GET /api/listings

Then:   CatalogListingView[] containing both listings, each with Status: "upcoming"
```

**Scenario: Listing detail returns full information**

```
Given:  ListingPublished { ListingId: "listing-A", Title: "Vintage Mechanical Keyboard",
          StartingBid: 25.00, BuyItNowPrice: 100.00 }

When:   GET /api/listings/listing-A

Then:   ListingDetailView {
          ListingId: "listing-A",
          Title: "Vintage Mechanical Keyboard",
          StartingBid: 25.00,
          BuyItNowPrice: 100.00,
          Status: "upcoming",
          CurrentHighBid: null,
          BidCount: 0
        }
```

> **Note:** Reserve price is NEVER included in catalog or detail views. Only `HasReserve: true/false`.

---

## Tier 2 — Flash Session Setup

### Slice 2.1 — Create Flash Session

**Scenario: Happy path**

```
Given:  (no prior sessions)

When:   CreateSession {
          Title: "Nebraska.Code() Live Auction",
          DurationMinutes: 5
        }

Then:   SessionCreated {
          SessionId: "session-001",
          Title: "Nebraska.Code() Live Auction",
          DurationMinutes: 5,
          CreatedBy: "ops-staff",
          CreatedAt: "2026-04-09T13:50:00Z"
        }

View:   SessionManagementView {
          SessionId: "session-001",
          Title: "Nebraska.Code() Live Auction",
          Status: "created",
          ListingCount: 0,
          ParticipantCount: 0
        }
```

---

### Slice 2.2 — Attach Listing to Session

**Scenario: Happy path**

```
Given:  ListingPublished { ListingId: "listing-A", ... }
        SessionCreated { SessionId: "session-001", ... }

When:   AttachListingToSession { SessionId: "session-001", ListingId: "listing-A" }

Then:   ListingAttachedToSession {
          SessionId: "session-001",
          ListingId: "listing-A",
          AttachedAt: "2026-04-09T13:51:00Z"
        }

View:   SessionManagementView { SessionId: "session-001", ListingCount: 1 }
        CatalogListingView { ListingId: "listing-A", SessionId: "session-001", Status: "upcoming" }
```

**Scenario: Reject — listing not published**

```
Given:  DraftListingCreated { ListingId: "listing-X" }
        SessionCreated { SessionId: "session-001" }

When:   AttachListingToSession { SessionId: "session-001", ListingId: "listing-X" }

Then:   (command rejected — listing must be in published state)
```

**Scenario: Reject — session already started**

```
Given:  SessionCreated { SessionId: "session-001" }
        SessionStarted { SessionId: "session-001" }
        ListingPublished { ListingId: "listing-Z" }

When:   AttachListingToSession { SessionId: "session-001", ListingId: "listing-Z" }

Then:   (command rejected — cannot attach listings to a started session)
```

---

### Slice 2.3 — Start Session (the cascade)

**Scenario: Happy path — session starts, all listings open**

```
Given:  SessionCreated { SessionId: "session-001", DurationMinutes: 5 }
        ListingAttachedToSession { SessionId: "session-001", ListingId: "listing-A" }
        ListingAttachedToSession { SessionId: "session-001", ListingId: "listing-B" }
        ListingAttachedToSession { SessionId: "session-001", ListingId: "listing-C" }

When:   StartSession { SessionId: "session-001" }

Then:   SessionStarted {
          SessionId: "session-001",
          ListingIds: ["listing-A", "listing-B", "listing-C"],
          StartedAt: "2026-04-09T14:00:00Z"
        }
        BiddingOpened { ListingId: "listing-A", SessionId: "session-001",
          ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        BiddingOpened { ListingId: "listing-B", SessionId: "session-001",
          ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        BiddingOpened { ListingId: "listing-C", SessionId: "session-001",
          ScheduledCloseAt: "2026-04-09T14:05:00Z" }

View:   LiveLotBoardView contains 3 listings, all Status: "open"
        CatalogListingView for each listing: Status → "open"
```

**Scenario: Reject — session has no listings**

```
Given:  SessionCreated { SessionId: "session-001" }
        (no ListingAttachedToSession events)

When:   StartSession { SessionId: "session-001" }

Then:   (command rejected — cannot start a session with no listings)
```

**Scenario: Reject — session already started**

```
Given:  SessionCreated { SessionId: "session-001" }
        ListingAttachedToSession { SessionId: "session-001", ListingId: "listing-A" }
        SessionStarted { SessionId: "session-001" }

When:   StartSession { SessionId: "session-001" }

Then:   (command rejected — session already started)
```

---

## Tier 3 — Core Bidding

### Slice 3.1 — Place a Bid (happy path)

**Scenario: First bid on a listing**

```
Given:  BiddingOpened { ListingId: "listing-A", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        ParticipantSessionStarted { ParticipantId: "participant-001", CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00 }

Then:   BidPlaced {
          ListingId: "listing-A",
          BidderId: "participant-001",
          Amount: 30.00,
          BidCount: 1,
          IsProxy: false,
          PlacedAt: "2026-04-09T14:00:30Z"
        }

View:   LiveLotBoardView { ListingId: "listing-A", CurrentBid: 30.00, BidCount: 1, HighBidder: "participant-001" }
        BidFeedView: new entry { BidderId: "participant-001", Amount: 30.00, ListingId: "listing-A" }
```

**Scenario: Outbid — new bid higher than current**

```
Given:  BiddingOpened { ListingId: "listing-A" }
        BidPlaced { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00, BidCount: 1 }
        ParticipantSessionStarted { ParticipantId: "participant-002", CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-002", Amount: 35.00 }

Then:   BidPlaced {
          ListingId: "listing-A",
          BidderId: "participant-002",
          Amount: 35.00,
          BidCount: 2,
          IsProxy: false,
          PlacedAt: ...
        }
```

> **Note:** The outbid notification to participant-001 is handled by Relay (Slice 4.3), not by this slice.

---

### Slice 3.2 — Reject a Bid

**Scenario: Bid below current high bid**

```
Given:  BiddingOpened { ListingId: "listing-A" }
        BidPlaced { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00 }
        ParticipantSessionStarted { ParticipantId: "participant-002", CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-002", Amount: 25.00 }

Then:   BidRejected {
          ListingId: "listing-A",
          BidderId: "participant-002",
          AttemptedAmount: 25.00,
          CurrentHighBid: 30.00,
          Reason: "BelowCurrentHighBid",
          RejectedAt: ...
        }
```

**Scenario: Bid exceeds credit ceiling**

```
Given:  BiddingOpened { ListingId: "listing-A" }
        ParticipantSessionStarted { ParticipantId: "participant-003", CreditCeiling: 50.00 }

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-003", Amount: 75.00 }

Then:   BidRejected {
          ListingId: "listing-A",
          BidderId: "participant-003",
          AttemptedAmount: 75.00,
          Reason: "ExceedsCreditCeiling",
          RejectedAt: ...
        }
```

**Scenario: Bid on closed listing**

```
Given:  BiddingOpened { ListingId: "listing-A" }
        BiddingClosed { ListingId: "listing-A" }
        ListingSold { ListingId: "listing-A", ... }

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-001", Amount: 100.00 }

Then:   BidRejected {
          ListingId: "listing-A",
          Reason: "ListingNotOpen",
          RejectedAt: ...
        }
```

---

### Slice 3.3 — Close Listing — Sold

**Scenario: Happy path — reserve met, winner declared**

```
Given:  ListingPublished { ListingId: "listing-A", ReservePrice: 50.00, ... }
        BiddingOpened { ListingId: "listing-A", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        BidPlaced { ListingId: "listing-A", BidderId: "participant-001", Amount: 55.00, BidCount: 3 }
        ReserveMet { ListingId: "listing-A" }

When:   (scheduled close timer fires at 2026-04-09T14:05:00Z)

Then:   BiddingClosed {
          ListingId: "listing-A",
          ClosedAt: "2026-04-09T14:05:00Z"
        }
        ListingSold {
          ListingId: "listing-A",
          WinnerId: "participant-001",
          HammerPrice: 55.00,
          BidCount: 3,
          SoldAt: "2026-04-09T14:05:00Z"
        }

View:   CatalogListingView { ListingId: "listing-A", Status: "sold" }
        LiveLotBoardView { ListingId: "listing-A", Status: "sold", Winner: "participant-001", HammerPrice: 55.00 }
```

---

### Slice 3.4 — Close Listing — Passed

**Scenario: Reserve not met**

```
Given:  ListingPublished { ListingId: "listing-D", ReservePrice: 50.00, ... }
        BiddingOpened { ListingId: "listing-D", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        BidPlaced { ListingId: "listing-D", BidderId: "participant-002", Amount: 40.00 }
        (no ReserveMet event — highest bid below reserve)

When:   (scheduled close timer fires at 2026-04-09T14:05:00Z)

Then:   BiddingClosed { ListingId: "listing-D", ClosedAt: ... }
        ListingPassed {
          ListingId: "listing-D",
          Reason: "ReserveNotMet",
          HighestBid: 40.00,
          BidCount: 1,
          PassedAt: "2026-04-09T14:05:00Z"
        }

View:   CatalogListingView { ListingId: "listing-D", Status: "passed" }
        LiveLotBoardView { ListingId: "listing-D", Status: "passed" }
```

**Scenario: No bids placed**

```
Given:  ListingPublished { ListingId: "listing-D", ... }
        BiddingOpened { ListingId: "listing-D", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        (no BidPlaced events)

When:   (scheduled close timer fires)

Then:   BiddingClosed { ListingId: "listing-D", ClosedAt: ... }
        ListingPassed {
          ListingId: "listing-D",
          Reason: "NoBids",
          HighestBid: null,
          BidCount: 0,
          PassedAt: ...
        }
```

---

## Tier 4 — Real-Time Layer

Real-time slices are tested as integration tests with SignalR test clients, not pure event-sourcing scenarios. The "Given" is events arriving at Relay handlers; the "Then" is what's pushed to which SignalR groups.

### Slice 4.1 — BiddingHub (participant SignalR)

**Scenario: BidPlaced pushes to all participants watching the listing**

```
Given:  Participant "participant-001" connected to BiddingHub, subscribed to listing-A
        Participant "participant-002" connected to BiddingHub, subscribed to listing-A
        Participant "participant-003" connected to BiddingHub, subscribed to listing-B (different listing)

When:   Relay handler receives BidPlaced { ListingId: "listing-A", BidderId: "participant-002", Amount: 35.00 }

Then:   BiddingHub pushes to listing-A group:
          { type: "BidPlaced", listingId: "listing-A", bidderDisplayName: "BoldPenguin7", amount: 35.00 }
        Participant-001 receives the push (watching listing-A)
        Participant-002 receives the push (watching listing-A, is also the bidder)
        Participant-003 does NOT receive the push (watching listing-B)
```

**Scenario: ListingSold pushes outcome to all participants watching**

```
Given:  Participants connected and subscribed to listing-A

When:   Relay handler receives ListingSold { ListingId: "listing-A", WinnerId: "participant-001", HammerPrice: 55.00 }

Then:   BiddingHub pushes to listing-A group:
          { type: "ListingSold", listingId: "listing-A", winnerDisplayName: "SwiftFerret42", hammerPrice: 55.00 }
```

---

### Slice 4.2 — OperationsHub (ops SignalR)

**Scenario: All integration events push to ops dashboard**

```
Given:  Ops staff connected to OperationsHub

When:   Relay handler receives BidPlaced { ListingId: "listing-A", ... }

Then:   OperationsHub pushes to ops group:
          { type: "BidPlaced", listingId: "listing-A", bidderDisplayName: "SwiftFerret42",
            amount: 30.00, isProxy: false, placedAt: ... }
```

> **Note:** OperationsHub pushes ALL integration events from all BCs. The ops dashboard decides what to display. Relay doesn't filter.

---

### Slice 4.3 — Outbid Notification

**Scenario: Previous high bidder receives outbid alert**

```
Given:  Participant "participant-001" connected to BiddingHub
        BidPlaced { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00 }
            (participant-001 is current high bidder)

When:   Relay handler receives BidPlaced { ListingId: "listing-A", BidderId: "participant-002", Amount: 35.00 }

Then:   BiddingHub pushes targeted notification to participant-001 only:
          { type: "Outbid", listingId: "listing-A", newHighBid: 35.00, yourBid: 30.00 }
        BiddingHub pushes general BidPlaced to listing-A group (per Slice 4.1)
```

> **Note:** Relay must track who the current high bidder is per listing to route the outbid notification. This is state Relay must project internally, not query from Auctions.

---

## Tier 5 — Extended Bidding

### Slice 5.1 — Extended Bidding

**Scenario: Bid in trigger window extends the close**

```
Given:  ListingPublished { ListingId: "listing-A",
          ExtendedBiddingEnabled: true,
          ExtendedBiddingTriggerWindow: "00:00:30",
          ExtendedBiddingExtension: "00:00:15" }
        BiddingOpened { ListingId: "listing-A", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        BidPlaced { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00 }
        (current time: 2026-04-09T14:04:40Z — 20 seconds before close, within 30-second trigger window)

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-002", Amount: 35.00 }

Then:   BidPlaced { ListingId: "listing-A", BidderId: "participant-002", Amount: 35.00 }
        ExtendedBiddingTriggered {
          ListingId: "listing-A",
          PreviousCloseAt: "2026-04-09T14:05:00Z",
          NewCloseAt: "2026-04-09T14:05:15Z",       // extended by 15 seconds
          TriggeredByBidderId: "participant-002",
          TriggeredAt: "2026-04-09T14:04:40Z"
        }
        (Auction Closing saga: previous scheduled close message cancelled, new one scheduled for 14:05:15Z)
```

**Scenario: Bid outside trigger window does NOT extend**

```
Given:  ListingPublished { ListingId: "listing-A",
          ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: "00:00:30" }
        BiddingOpened { ListingId: "listing-A", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        (current time: 2026-04-09T14:02:00Z — 3 minutes before close, outside 30-second window)

When:   PlaceBid { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00 }

Then:   BidPlaced { ListingId: "listing-A", BidderId: "participant-001", Amount: 30.00 }
        (no ExtendedBiddingTriggered — bid was not in the trigger window)
```

**Scenario: Extended bidding disabled by seller**

```
Given:  ListingPublished { ListingId: "listing-B", ExtendedBiddingEnabled: false }
        BiddingOpened { ListingId: "listing-B", ScheduledCloseAt: "2026-04-09T14:05:00Z" }
        (current time: 2026-04-09T14:04:50Z — 10 seconds before close)

When:   PlaceBid { ListingId: "listing-B", BidderId: "participant-001", Amount: 30.00 }

Then:   BidPlaced { ... }
        (no ExtendedBiddingTriggered — seller disabled extended bidding)
```

---

## Tier 6 — Settlement

### Slice 6.1 — Settlement Saga (happy path)

**Scenario: Full settlement from ListingSold**

```
Given:  ListingPublished { ListingId: "listing-A", ReservePrice: 50.00, SellerId: "participant-001" }
        ParticipantSessionStarted { ParticipantId: "participant-002", CreditCeiling: 500.00 }
        ListingSold { ListingId: "listing-A", WinnerId: "participant-002", HammerPrice: 55.00 }

When:   Settlement saga receives ListingSold

Then:   SettlementInitiated {
          SettlementId: "settlement-001",
          ListingId: "listing-A",
          WinnerId: "participant-002",
          SellerId: "participant-001",
          HammerPrice: 55.00,
          InitiatedAt: ...
        }
        ReserveCheckCompleted {
          SettlementId: "settlement-001",
          HammerPrice: 55.00,
          ReservePrice: 50.00,
          Result: "Met",
          CompletedAt: ...
        }
        WinnerCharged {
          SettlementId: "settlement-001",
          WinnerId: "participant-002",
          AmountCharged: 55.00,
          RemainingCredit: 445.00,         // 500.00 - 55.00
          ChargedAt: ...
        }
        FinalValueFeeCalculated {
          SettlementId: "settlement-001",
          HammerPrice: 55.00,
          FeePercentage: 10.0,             // configurable
          FeeAmount: 5.50,
          SellerPayout: 49.50,             // 55.00 - 5.50
          CalculatedAt: ...
        }
        SellerPayoutIssued {
          SettlementId: "settlement-001",
          SellerId: "participant-001",
          PayoutAmount: 49.50,
          FeeDeducted: 5.50,
          IssuedAt: ...
        }
        SettlementCompleted {
          SettlementId: "settlement-001",
          ListingId: "listing-A",
          WinnerId: "participant-002",
          SellerId: "participant-001",
          HammerPrice: 55.00,
          FeeAmount: 5.50,
          SellerPayout: 49.50,
          CompletedAt: ...
        }

View:   SettlementProgressView {
          SettlementId: "settlement-001",
          ListingId: "listing-A",
          Status: "complete",
          HammerPrice: 55.00,
          Fee: 5.50,
          SellerPayout: 49.50
        }
```

> **Deferred to BC workshop:** PaymentFailed scenario (winner credit insufficient). Reserve check disagreement with Auctions. Settlement for BuyItNowPurchased (Slice 6.2, P1).

---

## Scenario Coverage Summary

| Slice | Scenarios | Happy Path | Edge/Rejection |
|---|---|---|---|
| 0.2 Start session | 2 | 1 | 1 (unique names) |
| 0.3 Register seller | 3 | 1 | 2 (no session, already registered) |
| 1.1 Create draft | 2 | 1 | 1 (not a seller) |
| 1.2 Submit/publish | 2 | 1 | 1 (already submitted) |
| 1.3+1.4 Catalog/detail | 2 | 2 | — |
| 2.1 Create session | 1 | 1 | — |
| 2.2 Attach listing | 3 | 1 | 2 (not published, session started) |
| 2.3 Start session | 3 | 1 | 2 (no listings, already started) |
| 3.1 Place bid | 2 | 2 | — |
| 3.2 Reject bid | 3 | — | 3 (below high, credit, closed) |
| 3.3 Close — sold | 1 | 1 | — |
| 3.4 Close — passed | 2 | — | 2 (reserve not met, no bids) |
| 4.1 BiddingHub | 2 | 2 | — |
| 4.2 OperationsHub | 1 | 1 | — |
| 4.3 Outbid notification | 1 | 1 | — |
| 5.1 Extended bidding | 3 | 1 | 2 (outside window, disabled) |
| 6.1 Settlement saga | 1 | 1 | — |
| **Total** | **34** | **18** | **16** |

34 scenarios across 18 P0 slices. Each scenario is testable as a Given/When/Then specification using the Critter Stack testing patterns (Alba + Testcontainers + xUnit + Shouldly).
