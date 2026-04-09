# Workshop 002 — Auctions BC Scenarios (Given/When/Then)

Companion to `002-auctions-bc-deep-dive.md`, Phase 3.
Implementation-ready scenarios for all Auctions BC internals: DCB boundary model, Auction Closing saga, Proxy Bid Manager saga, and Session aggregate.

**Conventions:**
- Placeholder UUIDs for readability (e.g., `listing-A`, `participant-002`)
- Timestamps as relative offsets from bidding open (e.g., `T+0:30`, `T+4:40`)
- Bid increment: $1 under $100, $5 at $100+ (Workshop 002 Phase 2 decision)
- MaxDuration: 2x original duration (platform default)
- `BidRejected` events go to a separate stream (not the listing's primary stream)

**Test setup assumptions:**
- Listing-A: starting bid $25, reserve $50, Buy It Now $100, extended bidding enabled (30s window, 15s extension), session duration 5 minutes
- Participant-001: seller, credit ceiling irrelevant
- Participant-002 ("SwiftFerret42"): credit ceiling $500
- Participant-003 ("BoldPenguin7"): credit ceiling $200

---

## 1. DCB Boundary Model — PlaceBid Handler

### 1.1 First bid on a listing

```
Given:  BiddingOpened { ListingId: listing-A, StartingBid: 25.00,
          ReserveThreshold: 50.00, BuyItNowPrice: 100.00,
          ScheduledCloseAt: T+5:00 }
        ParticipantSessionStarted { ParticipantId: participant-002,
          CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-002,
          Amount: 30.00, IsProxy: false }

Then:   BidPlaced { ListingId: listing-A, BidderId: participant-002,
          Amount: 30.00, BidCount: 1, IsProxy: false }
        BuyItNowOptionRemoved { ListingId: listing-A }
```

> First bid at or above starting bid is accepted. Buy It Now is removed atomically with the first bid.

---

### 1.2 Outbid — new bid higher than current

```
Given:  BiddingOpened { ListingId: listing-A, StartingBid: 25.00 }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 30.00, BidCount: 1 }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 35.00 }

Then:   BidPlaced { ListingId: listing-A, BidderId: participant-003,
          Amount: 35.00, BidCount: 2, IsProxy: false }
```

> No BuyItNowOptionRemoved — already removed on first bid.

---

### 1.3 Reject — below starting bid (first bid)

```
Given:  BiddingOpened { ListingId: listing-A, StartingBid: 25.00 }
        ParticipantSessionStarted { ParticipantId: participant-002, CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-002, Amount: 20.00 }

Then:   BidRejected { ListingId: listing-A, BidderId: participant-002,
          AttemptedAmount: 20.00, CurrentHighBid: 0,
          Reason: "BelowMinimumBid" }
```

> Minimum first bid is the starting bid. BidRejected goes to a separate stream.

---

### 1.4 Reject — below current high bid plus increment

```
Given:  BiddingOpened { ListingId: listing-A, StartingBid: 25.00 }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 30.00, BidCount: 1 }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 30.50 }

Then:   BidRejected { ListingId: listing-A, BidderId: participant-003,
          AttemptedAmount: 30.50, CurrentHighBid: 30.00,
          Reason: "BelowMinimumBid" }
```

> Minimum subsequent bid is current high + increment ($1 for bids under $100). $30.50 < $31.00.

---

### 1.5 Reject — exceeds credit ceiling

```
Given:  BiddingOpened { ListingId: listing-A, StartingBid: 25.00 }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 250.00 }

Then:   BidRejected { ListingId: listing-A, BidderId: participant-003,
          AttemptedAmount: 250.00, Reason: "ExceedsCreditCeiling" }
```

---

### 1.6 Reject — listing not open (no BiddingOpened)

```
Given:  ListingPublished { ListingId: listing-A }
        ParticipantSessionStarted { ParticipantId: participant-002, CreditCeiling: 500.00 }
        (no BiddingOpened for listing-A)

When:   PlaceBid { ListingId: listing-A, BidderId: participant-002, Amount: 30.00 }

Then:   BidRejected { ListingId: listing-A, Reason: "ListingNotOpen" }
```

---

### 1.7 Reject — listing closed

```
Given:  BiddingOpened { ListingId: listing-A }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 55.00 }
        BiddingClosed { ListingId: listing-A }
        ListingSold { ListingId: listing-A, WinnerId: participant-002 }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 100.00 }

Then:   BidRejected { ListingId: listing-A, Reason: "ListingClosed" }
```

---

### 1.8 Reject — seller bidding on own listing

```
Given:  BiddingOpened { ListingId: listing-A, SellerId: participant-001 }
        ParticipantSessionStarted { ParticipantId: participant-001, CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-001, Amount: 30.00 }

Then:   BidRejected { ListingId: listing-A, BidderId: participant-001,
          Reason: "SellerCannotBid" }
```

---

### 1.9 Reserve met — bid crosses threshold

```
Given:  BiddingOpened { ListingId: listing-A, ReserveThreshold: 50.00 }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 45.00, BidCount: 3 }
        BuyItNowOptionRemoved { ListingId: listing-A }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 55.00 }

Then:   BidPlaced { ListingId: listing-A, BidderId: participant-003,
          Amount: 55.00, BidCount: 4 }
        ReserveMet { ListingId: listing-A }
```

> ReserveMet is produced atomically with BidPlaced. Only fires once — subsequent bids above reserve do not re-fire.

---

### 1.10 Reserve already met — no duplicate signal

```
Given:  BiddingOpened { ListingId: listing-A, ReserveThreshold: 50.00 }
        BidPlaced { ListingId: listing-A, Amount: 55.00 }
        ReserveMet { ListingId: listing-A }
        ParticipantSessionStarted { ParticipantId: participant-002, CreditCeiling: 500.00 }

When:   PlaceBid { ListingId: listing-A, BidderId: participant-002, Amount: 60.00 }

Then:   BidPlaced { ListingId: listing-A, Amount: 60.00 }
        (no ReserveMet — already fired)
```

---

### 1.11 Extended bidding — bid in trigger window

```
Given:  BiddingOpened { ListingId: listing-A, ScheduledCloseAt: T+5:00,
          ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: 00:00:30,
          ExtendedBiddingExtension: 00:00:15 }
        BidPlaced { ListingId: listing-A, Amount: 30.00 }
        BuyItNowOptionRemoved { ListingId: listing-A }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }
        (current time: T+4:40 — 20 seconds before close)

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 35.00 }

Then:   BidPlaced { ListingId: listing-A, Amount: 35.00 }
        ExtendedBiddingTriggered { ListingId: listing-A,
          PreviousCloseAt: T+5:00, NewCloseAt: T+4:55,
          TriggeredByBidderId: participant-003 }
```

---

### 1.12 Extended bidding — bid outside trigger window

```
Given:  BiddingOpened { ListingId: listing-A, ScheduledCloseAt: T+5:00,
          ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: 00:00:30 }
        ParticipantSessionStarted { ParticipantId: participant-002, CreditCeiling: 500.00 }
        (current time: T+2:00 — 3 minutes before close)

When:   PlaceBid { ListingId: listing-A, BidderId: participant-002, Amount: 30.00 }

Then:   BidPlaced { ListingId: listing-A, Amount: 30.00 }
        BuyItNowOptionRemoved { ListingId: listing-A }
        (no ExtendedBiddingTriggered)
```

---

### 1.13 Extended bidding disabled — no extension regardless of timing

```
Given:  BiddingOpened { ListingId: listing-A, ScheduledCloseAt: T+5:00,
          ExtendedBiddingEnabled: false }
        BidPlaced { ListingId: listing-A, Amount: 30.00 }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }
        (current time: T+4:50 — 10 seconds before close)

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 35.00 }

Then:   BidPlaced { ListingId: listing-A, Amount: 35.00 }
        (no ExtendedBiddingTriggered — seller disabled it)
```

---

### 1.14 Extended bidding — MaxDuration reached, no further extension

```
Given:  BiddingOpened { ListingId: listing-A, ScheduledCloseAt: T+5:00,
          ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: 00:00:30,
          ExtendedBiddingExtension: 00:00:15 }
        (multiple prior extensions — current ScheduledCloseAt is T+9:50)
        (MaxDuration = 2x original = 10 minutes, so max close = T+10:00)
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }
        (current time: T+9:40 — 10 seconds before close, in trigger window)

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 90.00 }

Then:   BidPlaced { ListingId: listing-A, Amount: 90.00 }
        (no ExtendedBiddingTriggered — newCloseAt would be T+9:55, but
         that is within MaxDuration so this DOES extend)
```

> Clarification: MaxDuration caps the *new close time*, not the trigger. If `now + extension <= originalClose + MaxDuration`, extension fires. If the new close time would exceed the cap, no extension.

---

### 1.15 Extended bidding — MaxDuration exceeded, extension blocked

```
Given:  BiddingOpened { ListingId: listing-A, ScheduledCloseAt: T+5:00,
          ExtendedBiddingEnabled: true, ExtendedBiddingExtension: 00:00:15 }
        (multiple prior extensions — current ScheduledCloseAt is T+9:55)
        (MaxDuration = 10 minutes, max close = T+10:00)
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }
        (current time: T+9:50 — 5 seconds before close, in trigger window)

When:   PlaceBid { ListingId: listing-A, BidderId: participant-003, Amount: 95.00 }

Then:   BidPlaced { ListingId: listing-A, Amount: 95.00 }
        (no ExtendedBiddingTriggered — newCloseAt would be T+10:05,
         exceeding MaxDuration cap of T+10:00)
```

---

## 2. DCB Boundary Model — Buy It Now Handler

### 2.1 Buy It Now — happy path (no prior bids)

```
Given:  BiddingOpened { ListingId: listing-A, BuyItNowPrice: 100.00 }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }
        (no BidPlaced events — Buy It Now still available)

When:   BuyNow { ListingId: listing-A, BuyerId: participant-003 }

Then:   BuyItNowPurchased { ListingId: listing-A, BuyerId: participant-003,
          Price: 100.00 }
```

---

### 2.2 Buy It Now — rejected, option already removed

```
Given:  BiddingOpened { ListingId: listing-A, BuyItNowPrice: 100.00 }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 30.00 }
        BuyItNowOptionRemoved { ListingId: listing-A }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   BuyNow { ListingId: listing-A, BuyerId: participant-003 }

Then:   (command rejected — Buy It Now no longer available)
```

---

### 2.3 Buy It Now — rejected, exceeds credit ceiling

```
Given:  BiddingOpened { ListingId: listing-A, BuyItNowPrice: 100.00 }
        ParticipantSessionStarted { ParticipantId: participant-004, CreditCeiling: 50.00 }

When:   BuyNow { ListingId: listing-A, BuyerId: participant-004 }

Then:   (command rejected — Buy It Now price exceeds credit ceiling)
```

---

### 2.4 Buy It Now — rejected, listing closed

```
Given:  BiddingOpened { ListingId: listing-A, BuyItNowPrice: 100.00 }
        BiddingClosed { ListingId: listing-A }
        ListingPassed { ListingId: listing-A }
        ParticipantSessionStarted { ParticipantId: participant-003, CreditCeiling: 200.00 }

When:   BuyNow { ListingId: listing-A, BuyerId: participant-003 }

Then:   (command rejected — listing is closed)
```

---

## 3. Auction Closing Saga

### 3.1 Saga starts on BiddingOpened

```
Given:  (no saga exists for listing-A)

When:   BiddingOpened { ListingId: listing-A, ScheduledCloseAt: T+5:00,
          ExtendedBiddingEnabled: true, ... }

Then:   AuctionClosingSaga created {
          Id: listing-A,
          Status: AwaitingBids,
          ScheduledCloseAt: T+5:00,
          BidCount: 0,
          ReserveHasBeenMet: false
        }
        CloseAuction { ListingId: listing-A } scheduled at T+5:00
```

---

### 3.2 Saga transitions AwaitingBids → Active on first bid

```
Given:  AuctionClosingSaga { Id: listing-A, Status: AwaitingBids, BidCount: 0 }

When:   BidPlaced { ListingId: listing-A, BidderId: participant-002,
          Amount: 30.00, BidCount: 1 }

Then:   AuctionClosingSaga {
          Status: Active,
          CurrentHighBidderId: participant-002,
          CurrentHighBid: 30.00,
          BidCount: 1
        }
```

---

### 3.3 Saga tracks ReserveMet

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Active,
          ReserveHasBeenMet: false }

When:   ReserveMet { ListingId: listing-A }

Then:   AuctionClosingSaga { ReserveHasBeenMet: true }
```

---

### 3.4 Saga reschedules on ExtendedBiddingTriggered

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Active,
          ScheduledCloseAt: T+5:00 }

When:   ExtendedBiddingTriggered { ListingId: listing-A,
          PreviousCloseAt: T+5:00, NewCloseAt: T+5:15 }

Then:   AuctionClosingSaga {
          Status: Extended,
          ScheduledCloseAt: T+5:15
        }
        CloseAuction at T+5:00 cancelled
        CloseAuction { ListingId: listing-A } scheduled at T+5:15
```

---

### 3.5 Close — ListingSold (reserve met, bids exist)

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Active,
          CurrentHighBidderId: participant-002, CurrentHighBid: 85.00,
          BidCount: 12, ReserveHasBeenMet: true }

When:   CloseAuction { ListingId: listing-A }

Then:   BiddingClosed { ListingId: listing-A }
        ListingSold { ListingId: listing-A, WinnerId: participant-002,
          HammerPrice: 85.00, BidCount: 12 }
        AuctionClosingSaga { Status: Resolved }
        Saga marked completed
```

---

### 3.6 Close — ListingPassed (reserve not met)

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Active,
          CurrentHighBidderId: participant-003, CurrentHighBid: 40.00,
          BidCount: 5, ReserveHasBeenMet: false }

When:   CloseAuction { ListingId: listing-A }

Then:   BiddingClosed { ListingId: listing-A }
        ListingPassed { ListingId: listing-A, Reason: "ReserveNotMet",
          HighestBid: 40.00, BidCount: 5 }
        AuctionClosingSaga { Status: Resolved }
        Saga marked completed
```

---

### 3.7 Close — ListingPassed (no bids)

```
Given:  AuctionClosingSaga { Id: listing-A, Status: AwaitingBids,
          BidCount: 0, ReserveHasBeenMet: false }

When:   CloseAuction { ListingId: listing-A }

Then:   BiddingClosed { ListingId: listing-A }
        ListingPassed { ListingId: listing-A, Reason: "NoBids",
          HighestBid: null, BidCount: 0 }
        AuctionClosingSaga { Status: Resolved }
        Saga marked completed
```

---

### 3.8 BuyItNowPurchased — saga completes immediately

```
Given:  AuctionClosingSaga { Id: listing-A, Status: AwaitingBids }

When:   BuyItNowPurchased { ListingId: listing-A, BuyerId: participant-003, Price: 100.00 }

Then:   AuctionClosingSaga { Status: Resolved, BuyItNowExercised: true }
        Saga marked completed
        (no BiddingClosed, no ListingSold — BuyItNowPurchased is the terminal event)
```

---

### 3.9 CloseAuction after BuyItNow — no-op

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Resolved,
          BuyItNowExercised: true }
        (saga already completed from BuyItNowPurchased)

When:   CloseAuction { ListingId: listing-A }
        (scheduled timer fires after saga completed)

Then:   (no-op — saga already completed, message is discarded)
```

---

### 3.10 ListingWithdrawn — saga terminates without evaluation

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Active,
          CurrentHighBid: 55.00, BidCount: 6, ReserveHasBeenMet: true }

When:   ListingWithdrawn { ListingId: listing-A, WithdrawnBy: "ops-staff" }

Then:   AuctionClosingSaga { Status: Resolved }
        Saga marked completed
        (no BiddingClosed, no ListingSold, no ListingPassed —
         ListingWithdrawn is the terminal event, no reserve evaluation)
```

> Even though reserve was met and bids existed, withdrawal skips all evaluation. No money moves. ListingWithdrawn is published by the DCB handler, not the saga.

---

### 3.11 Close after extended bidding — uses updated close time

```
Given:  AuctionClosingSaga { Id: listing-A, Status: Extended,
          ScheduledCloseAt: T+5:15, CurrentHighBid: 85.00,
          BidCount: 12, ReserveHasBeenMet: true }

When:   CloseAuction { ListingId: listing-A }
        (fires at T+5:15, the rescheduled time)

Then:   BiddingClosed { ListingId: listing-A }
        ListingSold { ListingId: listing-A, HammerPrice: 85.00 }
        Saga marked completed
```

> Same close logic regardless of whether the listing was extended. The saga just uses its current tracked state.

---

## 4. Proxy Bid Manager Saga

### 4.1 Proxy registration starts saga

```
Given:  BiddingOpened { ListingId: listing-A }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 35.00 }
        (participant-002 is current high bidder)

When:   RegisterProxyBid { ListingId: listing-A, BidderId: participant-002,
          MaxAmount: 75.00 }

Then:   ProxyBidRegistered { ListingId: listing-A, BidderId: participant-002,
          MaxAmount: 75.00 }
        ProxyBidManagerSaga created {
          Id: UuidV5(AuctionsNS, "listing-A:participant-002"),
          ListingId: listing-A,
          BidderId: participant-002,
          MaxAmount: 75.00,
          BidderCreditCeiling: 500.00,
          Status: Active
        }
```

---

### 4.2 Competing bid — proxy auto-bids

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          BidderId: participant-002, MaxAmount: 75.00, Status: Active }

When:   BidPlaced { ListingId: listing-A, BidderId: participant-003, Amount: 45.00 }

Then:   Proxy sends PlaceBid { ListingId: listing-A, BidderId: participant-002,
          Amount: 46.00, IsProxy: true }
        (competing $45 + $1 increment = $46, which is <= max $75)
```

---

### 4.3 Competing bid — proxy exhausted

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          BidderId: participant-002, MaxAmount: 75.00, Status: Active }

When:   BidPlaced { ListingId: listing-A, BidderId: participant-003, Amount: 75.00 }

Then:   ProxyBidExhausted { ListingId: listing-A, BidderId: participant-002,
          MaxAmount: 75.00 }
        ProxyBidManagerSaga { Status: Exhausted }
        Saga marked completed
        (next bid would be $75 + $1 = $76, which exceeds max $75)
```

---

### 4.4 Own bid arrives — track, don't react

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          BidderId: participant-002, MaxAmount: 75.00, Status: Active }

When:   BidPlaced { ListingId: listing-A, BidderId: participant-002,
          Amount: 46.00, IsProxy: true }

Then:   ProxyBidManagerSaga { LastBidAmount: 46.00, Status: Active }
        (no outgoing PlaceBid — this is our own bid)
```

---

### 4.5 Own manual bid — track, stay active

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          BidderId: participant-002, MaxAmount: 75.00, Status: Active }

When:   BidPlaced { ListingId: listing-A, BidderId: participant-002,
          Amount: 50.00, IsProxy: false }

Then:   ProxyBidManagerSaga { LastBidAmount: 50.00, Status: Active }
        (manual bid from the same bidder — proxy stays active, just tracks)
```

---

### 4.6 ListingSold — proxy terminates

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          Status: Active }

When:   ListingSold { ListingId: listing-A }

Then:   ProxyBidManagerSaga { Status: ListingClosed }
        Saga marked completed
```

---

### 4.7 ListingPassed — proxy terminates

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          Status: Active }

When:   ListingPassed { ListingId: listing-A }

Then:   ProxyBidManagerSaga { Status: ListingClosed }
        Saga marked completed
```

---

### 4.8 ListingWithdrawn — proxy terminates

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          Status: Active }

When:   ListingWithdrawn { ListingId: listing-A }

Then:   ProxyBidManagerSaga { Status: ListingClosed }
        Saga marked completed
```

---

### 4.9 Proxy auto-bid capped by credit ceiling

```
Given:  ProxyBidManagerSaga { Id: proxy-002-A, ListingId: listing-A,
          BidderId: participant-002, MaxAmount: 300.00,
          BidderCreditCeiling: 200.00, Status: Active }
        (proxy max is $300, but credit ceiling is only $200)

When:   BidPlaced { ListingId: listing-A, BidderId: participant-003, Amount: 195.00 }

Then:   ProxyBidExhausted { ListingId: listing-A, BidderId: participant-002,
          MaxAmount: 300.00 }
        ProxyBidManagerSaga { Status: Exhausted }
        Saga marked completed
        (next bid would be $196, which is <= max $300 but would fail DCB
         credit ceiling check at $200. Proxy caps at min(196, 300, 200) = $196,
         but since $196 still passes, let's reconsider...)
```

> Correction: the proxy calculates `nextBid = min(competingBid + increment, MaxAmount, BidderCreditCeiling)`. If `nextBid <= competingBid`, the proxy can't beat the current bid and exhausts. If `nextBid > competingBid`, the proxy fires the bid. So with competing bid $195, increment $1: `min(196, 300, 200) = 196`. Since $196 > $195, the proxy bids $196. The credit ceiling check in the DCB passes ($196 <= $200). Proxy stays active.

The real exhaustion from credit ceiling happens when:

```
Given:  ProxyBidManagerSaga { ... MaxAmount: 300.00, BidderCreditCeiling: 200.00 }

When:   BidPlaced { ListingId: listing-A, BidderId: participant-003, Amount: 200.00 }

Then:   ProxyBidExhausted { ... }
        (next bid = min(201, 300, 200) = 200. $200 is NOT > $200. Can't beat it. Exhausted.)
```

---

### 4.10 Two proxies on the same listing — bidding war

```
Given:  BiddingOpened { ListingId: listing-A }
        BidPlaced { ListingId: listing-A, BidderId: participant-002, Amount: 30.00 }
        ProxyBidManagerSaga { Id: proxy-002-A, BidderId: participant-002,
          MaxAmount: 50.00, Status: Active }
        ProxyBidManagerSaga { Id: proxy-003-A, BidderId: participant-003,
          MaxAmount: 45.00, Status: Active }
        (participant-002 is current high at $30, both proxies active)

When:   BidPlaced { ListingId: listing-A, BidderId: participant-003,
          Amount: 31.00, IsProxy: true }
        (proxy-003 fires first — someone else bid, so it counters)

Then:   Proxy-002 reacts: PlaceBid { Amount: 32.00, IsProxy: true }
          → BidPlaced { Amount: 32.00 }
        Proxy-003 reacts: PlaceBid { Amount: 33.00, IsProxy: true }
          → BidPlaced { Amount: 33.00 }
        Proxy-002 reacts: PlaceBid { Amount: 34.00, IsProxy: true }
          → BidPlaced { Amount: 34.00 }
        ... escalation continues ...
        Proxy-003 reacts to BidPlaced { Amount: 44.00 }:
          next = 45.00, max = 45.00 → bids $45
        Proxy-002 reacts to BidPlaced { Amount: 45.00 }:
          next = 46.00, max = 50.00 → bids $46
        Proxy-003 reacts to BidPlaced { Amount: 46.00 }:
          next = 47.00, max = 45.00 → EXHAUSTED
          → ProxyBidExhausted { BidderId: participant-003 }
        Final state: participant-002 wins at $46, proxy-003 exhausted
```

> This is correct eBay behavior. Two proxies on the same listing race to one's maximum. The stronger proxy wins at one increment above the weaker proxy's max. The escalation happens within a single message processing cycle (each BidPlaced triggers the other proxy), so it completes in milliseconds.

---

### 4.11 Proxy registration when already outbid — immediate reaction

```
Given:  BiddingOpened { ListingId: listing-A }
        BidPlaced { ListingId: listing-A, BidderId: participant-003, Amount: 40.00 }
        (participant-003 is current high bidder)

When:   RegisterProxyBid { ListingId: listing-A, BidderId: participant-002,
          MaxAmount: 60.00 }

Then:   ProxyBidRegistered { ... }
        ProxyBidManagerSaga created { Status: Active }
        (proxy does NOT immediately bid — it waits for the NEXT BidPlaced
         by someone other than participant-002 to trigger)
```

> The proxy is reactive. It only fires on incoming BidPlaced events from other bidders. Registration alone doesn't trigger a bid. If participant-002 wants to outbid the current high, they need to bid manually first. The proxy defends that position going forward.

---

## 5. Session Aggregate

### 5.1 Create session

```
Given:  (no prior sessions)

When:   CreateSession { Title: "Nebraska.Code() Live Auction", DurationMinutes: 5 }

Then:   SessionCreated { SessionId: session-001,
          Title: "Nebraska.Code() Live Auction", DurationMinutes: 5 }
```

---

### 5.2 Attach listing — happy path

```
Given:  SessionCreated { SessionId: session-001 }
        ListingPublished { ListingId: listing-A }

When:   AttachListingToSession { SessionId: session-001, ListingId: listing-A }

Then:   ListingAttachedToSession { SessionId: session-001, ListingId: listing-A }
```

---

### 5.3 Attach listing — reject, listing not published

```
Given:  SessionCreated { SessionId: session-001 }
        DraftListingCreated { ListingId: listing-X }
        (listing-X is still in draft, not published)

When:   AttachListingToSession { SessionId: session-001, ListingId: listing-X }

Then:   (command rejected — listing must be published)
```

---

### 5.4 Attach listing — reject, session already started

```
Given:  SessionCreated { SessionId: session-001 }
        ListingAttachedToSession { SessionId: session-001, ListingId: listing-A }
        SessionStarted { SessionId: session-001 }
        ListingPublished { ListingId: listing-B }

When:   AttachListingToSession { SessionId: session-001, ListingId: listing-B }

Then:   (command rejected — cannot attach to a started session)
```

---

### 5.5 Start session — happy path

```
Given:  SessionCreated { SessionId: session-001, DurationMinutes: 5 }
        ListingAttachedToSession { SessionId: session-001, ListingId: listing-A }
        ListingAttachedToSession { SessionId: session-001, ListingId: listing-B }

When:   StartSession { SessionId: session-001 }

Then:   SessionStarted { SessionId: session-001,
          ListingIds: [listing-A, listing-B] }
```

> The handler that reacts to SessionStarted produces BiddingOpened per listing (Option B from Phase 1). That's a downstream handler, not the Session aggregate itself.

---

### 5.6 Start session — reject, no listings

```
Given:  SessionCreated { SessionId: session-001 }
        (no ListingAttachedToSession events)

When:   StartSession { SessionId: session-001 }

Then:   (command rejected — cannot start a session with no listings)
```

---

### 5.7 Start session — reject, already started

```
Given:  SessionCreated { SessionId: session-001 }
        ListingAttachedToSession { SessionId: session-001, ListingId: listing-A }
        SessionStarted { SessionId: session-001 }

When:   StartSession { SessionId: session-001 }

Then:   (command rejected — session already started)
```

---

## Scenario Coverage Summary

| Component | Scenarios | Happy Path | Edge/Rejection |
|---|---|---|---|
| **DCB — PlaceBid** | 15 | 4 | 11 |
| **DCB — BuyNow** | 4 | 1 | 3 |
| **Auction Closing Saga** | 11 | 4 | 7 |
| **Proxy Bid Manager** | 11 | 4 | 7 |
| **Session Aggregate** | 7 | 3 | 4 |
| **Total** | **48** | **16** | **32** |

48 scenarios. Each is testable as a specification using the Critter Stack testing patterns (Alba + Testcontainers + xUnit + Shouldly). The 2:1 ratio of edge cases to happy paths reflects the depth of a BC-focused workshop — this is where the QA persona earns their keep.

### Scenarios That Address Workshop 002 Questions

| Question | Resolved By Scenario(s) |
|---|---|
| Bid increment strategy | 1.4 (minimum bid enforcement), 4.2/4.3 (proxy increment) |
| BidRejected stream placement | 1.3-1.8 (all rejections note separate stream) |
| Saga tracking vs DCB read | 3.2/3.3/3.5-3.7 (saga tracks incrementally) |
| ListingWithdrawn interaction | 3.10 (saga), 4.8 (proxy) |
| Proxy bid rejection handling | 4.9 (credit ceiling cap) |
| MaxDuration | 1.14/1.15 (extension allowed/blocked) |
| Two proxies bidding war | 4.10 (escalation to exhaustion) |
| Proxy registration timing | 4.11 (reactive, not immediate) |
