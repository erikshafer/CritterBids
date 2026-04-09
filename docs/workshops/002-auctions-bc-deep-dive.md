# Workshop 002 — Auctions BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-04-09
**Status:** In progress — Phase 3 next

**Scope:** The Auctions BC internals. Aggregate state machines, saga designs, DCB boundary model, and resolution of parked questions from Workshop 001.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. ProductOwner on standby.

---

## Phase 1 — Brain Dump: Internal Structure

*(Condensed. See git history for full Phase 1 output with code sketches.)*

### Key Design Decisions

**Session fan-out (Parked #2):** Option B adopted. Session aggregate produces `SessionStarted`. A separate Wolverine handler reacts and produces `BiddingOpened` per listing.

**Reserve check authority (Parked #5):** Auctions owns the real-time UX signal (`ReserveMet`, fired from DCB handler). Settlement owns the financial authority (`ReserveCheckCompleted`). Same source data, different roles. `ListingPublished` carries the reserve value to both BCs.

**ProxyBidExhausted (Parked #3):** Promoted to 🔵 Integration. Relay pushes a distinct "your proxy has been exceeded" notification, separate from the generic outbid alert.

**Extended bidding chaining (Parked #4):** No count limit. Extensions can chain. `MaxDuration` config caps total listing duration.

**Proxy bids and extended bidding (Parked #8):** Yes, proxy bids can trigger extended bidding. The DCB handler is bid-source-agnostic.

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

### Open Questions from Phase 1

| # | Question | Persona |
|---|----------|---------|
| 1 | Bid increment strategy? | `@DomainExpert` |
| 2 | `BidRejected` stream placement? | `@Architect` |
| 3 | Saga tracks bids incrementally vs reads DCB at close? | `@Architect` |
| 4 | `ListingWithdrawn` saga interaction? | `@QA` |
| 5 | Proxy bid rejection handling? | `@QA` |
| 6 | `MaxDuration` ownership (seller vs platform)? | `@ProductOwner` |

---

## Phase 2 — Storytelling: A Listing's Complete Lifecycle

This phase walks a single listing through its entire lifecycle inside the Auctions BC. At each step, we show which component acts, what state changes, and where the components interact. We'll resolve the Phase 1 open questions as they naturally arise.

The listing is "Vintage Mechanical Keyboard" in a Flash Session. Three participants are involved: the seller (Participant-001), and two bidders (Participant-002 "SwiftFerret42" with a $500 credit ceiling, Participant-003 "BoldPenguin7" with a $200 credit ceiling).

Listing config: starting bid $25, reserve $50, Buy It Now $100, extended bidding enabled (30-second trigger window, 15-second extension), session duration 5 minutes.

---

### Step 1: BiddingOpened — Everything Starts

**Trigger:** Handler receives `SessionStarted`, produces `BiddingOpened` for this listing.

```
Event: BiddingOpened {
  ListingId: listing-A,
  SessionId: session-001,
  SellerId: participant-001,
  StartingBid: 25.00,
  ReserveThreshold: 50.00,
  BuyItNowPrice: 100.00,
  ExtendedBiddingEnabled: true,
  ExtendedBiddingTriggerWindow: 00:00:30,
  ExtendedBiddingExtension: 00:00:15,
  ScheduledCloseAt: T+5:00,
  OpenedAt: T+0:00
}
```

**Auction Closing Saga starts:**

```
AuctionClosingSaga {
  Id: listing-A,
  Status: AwaitingBids,
  ScheduledCloseAt: T+5:00,
  CurrentHighBid: 0,
  BidCount: 0,
  ReserveHasBeenMet: false,
  BuyItNowExercised: false
}
→ Schedules CloseAuction { ListingId: listing-A } at T+5:00
```

**DCB state after this event:**

```
BidConsistencyState {
  IsOpen: true,
  IsClosed: false,
  CurrentHighBid: 0,
  BidCount: 0,
  HasBuyItNow: true,
  BuyItNowRemoved: false,
  ReserveHasBeenMet: false
}
```

No Proxy Bid Managers exist yet. Nobody has bid. The listing is open and the clock is ticking.

---

### Step 2: First Bid — Three Things Happen Atomically

**T+0:30:** Participant-002 (SwiftFerret42) bids $30.

**DCB handler receives `PlaceBid`:**

The `EventTagQuery` loads events from the listing-A stream (just `BiddingOpened`) and from participant-002's stream (`ParticipantSessionStarted` with credit ceiling $500). Projects into `BidConsistencyState`.

Validation passes: listing is open, $30 >= starting bid of $25, $30 <= credit ceiling of $500, bidder is not the seller.

**Handler produces three events atomically:**

```
1. BidPlaced { ListingId: listing-A, BidderId: participant-002,
     Amount: 30.00, BidCount: 1, IsProxy: false, PlacedAt: T+0:30 }

2. BuyItNowOptionRemoved { ListingId: listing-A, RemovedAt: T+0:30 }
   (first bid removes Buy It Now — business rule)

3. (no ReserveMet — $30 < $50 reserve threshold)
   (no ExtendedBiddingTriggered — T+0:30 is nowhere near close at T+5:00)
```

**Auction Closing Saga reacts to `BidPlaced`:**

```
AuctionClosingSaga {
  Status: AwaitingBids → Active,
  CurrentHighBidderId: participant-002,
  CurrentHighBid: 30.00,
  BidCount: 1
}
```

**`@Architect` — resolving Question #2: Where does `BidRejected` go?**

This bid was accepted, but the question is relevant: when a bid IS rejected, does `BidRejected` go into the listing stream?

The answer depends on what reads them. The DCB tag query for `PlaceBid` loads `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `BuyItNowOptionRemoved`, `ReserveMet`, `BuyItNowPurchased`. It does NOT load `BidRejected`. Rejections don't affect bidding state — they're observational facts, not state-changing facts.

If `BidRejected` goes in the listing stream, it inflates the stream length without contributing to the DCB's decision. Every tag query loads extra events it ignores. In a hot auction with many rejected bids (credit ceiling hits, below-minimum bids), this could be significant.

If `BidRejected` goes in a separate stream (e.g., tagged with the listing ID but in a `rejections` stream), the DCB never loads it. Operations can still project it for the ops dashboard via its own tag query.

> **Decision: `BidRejected` goes into its own stream, not the listing's primary stream.** Tagged with `ListingId` so Operations and Relay can find it, but excluded from the DCB's tag query. This keeps the hot path lean. The stream ID convention: `bid-rejection-{listingId}-{bidId}` or a dedicated rejections stream per listing.

---

### Step 3: Competing Bid — The Outbid Moment

**T+1:00:** Participant-003 (BoldPenguin7) bids $35.

**DCB handler:** Loads listing-A events (now includes `BiddingOpened`, `BidPlaced($30)`, `BuyItNowOptionRemoved`) plus participant-003's `ParticipantSessionStarted` (credit ceiling $200). Projects state: current high bid is $30, listing is open.

Validation passes: $35 > $30, $35 <= $200 credit ceiling.

```
BidPlaced { ListingId: listing-A, BidderId: participant-003,
  Amount: 35.00, BidCount: 2, IsProxy: false, PlacedAt: T+1:00 }
```

**Auction Closing Saga updates:**

```
CurrentHighBidderId: participant-003,
CurrentHighBid: 35.00,
BidCount: 2
```

**Relay (outside Auctions BC):** Receives `BidPlaced`, pushes to BiddingHub. Identifies participant-002 as the previous high bidder, pushes "Outbid!" notification to them specifically.

No proxy bid managers active yet — neither bidder has registered one.

---

### Step 4: Proxy Bid Registration

**T+1:15:** Participant-002 (SwiftFerret42) registers a proxy bid with max $75.

**Command:** `RegisterProxyBid { ListingId: listing-A, BidderId: participant-002, MaxAmount: 75.00 }`

**Handler produces:**

```
ProxyBidRegistered { ListingId: listing-A, BidderId: participant-002,
  MaxAmount: 75.00, RegisteredAt: T+1:15 }
```

**Proxy Bid Manager Saga starts:**

```
ProxyBidManagerSaga {
  Id: UuidV5(AuctionsNS, "listing-A:participant-002"),
  ListingId: listing-A,
  BidderId: participant-002,
  MaxAmount: 75.00,
  Status: Active
}
```

**`@DomainExpert` — resolving Question #1: Bid increment strategy.**

The proxy needs to know how much to bid above the competing bid. eBay uses tiered increments based on the current price level:

| Current Price | Increment |
|---|---|
| $0.01 - $0.99 | $0.05 |
| $1.00 - $4.99 | $0.25 |
| $5.00 - $24.99 | $0.50 |
| $25.00 - $99.99 | $1.00 |
| $100.00 - $249.99 | $2.50 |
| $250.00 - $499.99 | $5.00 |
| $500.00+ | $10.00 |

This is elegant but complex for MVP. CritterBids deals in virtual credit with smaller ranges (ceilings typically $50-$500).

**`@ProductOwner` input:** For MVP, a simplified tiered system is fine. Two tiers cover our range:

| Current Price | Increment |
|---|---|
| Under $100 | $1.00 |
| $100 and above | $5.00 |

This is simple to implement, easy to explain in a demo, and produces legible bid sequences. Post-MVP can adopt the full eBay tiers if needed.

> **Decision: Two-tier bid increment.** $1 under $100, $5 at $100+. The increment strategy lives as a pure function in the Auctions BC — not configurable per listing in MVP. Question #1 resolved.

**Immediate consequence:** Participant-002's proxy is now active, and participant-003 is the current high bidder at $35. The proxy sees the competing bid and immediately fires:

```
Proxy sends: PlaceBid { ListingId: listing-A, BidderId: participant-002,
  Amount: 36.00, IsProxy: true }
  ($35 + $1 increment)
```

---

### Step 5: Proxy Auto-Bid Fires

**T+1:15 (same tick):** The `PlaceBid` command from the proxy enters the DCB handler.

**DCB handler:** Loads state. Current high is $35 (participant-003). Validates: $36 > $35, $36 <= $500 ceiling. Passes.

```
BidPlaced { ListingId: listing-A, BidderId: participant-002,
  Amount: 36.00, BidCount: 3, IsProxy: true, PlacedAt: T+1:15 }
```

**Auction Closing Saga updates:**

```
CurrentHighBidderId: participant-002,
CurrentHighBid: 36.00,
BidCount: 3
```

**Proxy Bid Manager (participant-002) receives its own `BidPlaced`:** Recognizes it's own bid (`message.BidderId == BidderId`), updates `LastBidAmount = 36.00`, stays Active.

**Relay:** Pushes `BidPlaced` to all watchers. Pushes "Outbid!" to participant-003. From the audience's perspective, the bid counter jumped from 2 to 3 almost instantly. On the ops dashboard, the proxy flag is visible.

---

### Step 6: Bidder vs Proxy — The Escalation

**T+2:00:** Participant-003 manually bids $45.

**DCB handler:** $45 > $36 current high. Passes.

```
BidPlaced { ListingId: listing-A, BidderId: participant-003,
  Amount: 45.00, BidCount: 4, IsProxy: false, PlacedAt: T+2:00 }
```

**Proxy Bid Manager (participant-002) receives `BidPlaced`:** Someone else bid. Next bid would be $45 + $1 = $46. $46 <= max of $75. Fires:

```
PlaceBid { ListingId: listing-A, BidderId: participant-002,
  Amount: 46.00, IsProxy: true }
```

**DCB handler:** $46 > $45. Passes.

```
BidPlaced { ListingId: listing-A, BidderId: participant-002,
  Amount: 46.00, BidCount: 5, IsProxy: true, PlacedAt: T+2:00 }
```

**This is the "ghost bidding" pattern.** Participant-003 bid $45, and within a fraction of a second the price jumped to $46. From the audience's phone, it looks like someone instantly countered. On the ops dashboard, the proxy flag tells the story.

---

### Step 7: Reserve Met

**T+2:30:** Participant-003 bids $55.

**DCB handler:** $55 > $46. $55 >= reserve threshold of $50. Passes.

```
1. BidPlaced { ListingId: listing-A, BidderId: participant-003,
     Amount: 55.00, BidCount: 6, IsProxy: false, PlacedAt: T+2:30 }

2. ReserveMet { ListingId: listing-A, MetAt: T+2:30 }
   ($55 crossed the $50 reserve threshold — first time)
```

**Auction Closing Saga updates:**

```
CurrentHighBidderId: participant-003,
CurrentHighBid: 55.00,
BidCount: 6,
ReserveHasBeenMet: true   ← important for close evaluation
```

**Proxy Bid Manager (participant-002):** $55 + $1 = $56. $56 <= max of $75. Fires proxy bid for $56.

```
BidPlaced { ListingId: listing-A, BidderId: participant-002,
  Amount: 56.00, BidCount: 7, IsProxy: true, PlacedAt: T+2:30 }
```

**Relay:** Pushes `ReserveMet` to all watchers. "Reserve met!" badge appears on the listing. This is a trust signal — the listing will sell if the current bid holds.

---

### Step 8: Proxy Exhaustion

**T+3:00:** Participant-003 bids $70.

**DCB handler:** $70 > $56. Passes.

```
BidPlaced { ListingId: listing-A, BidderId: participant-003,
  Amount: 70.00, BidCount: 8 }
```

**Proxy Bid Manager (participant-002):** $70 + $1 = $71. $71 <= max of $75. Fires proxy for $71.

```
BidPlaced { ... Amount: 71.00, BidCount: 9, IsProxy: true }
```

**T+3:15:** Participant-003 bids $75.

```
BidPlaced { ... BidderId: participant-003, Amount: 75.00, BidCount: 10 }
```

**Proxy Bid Manager (participant-002):** $75 + $1 = $76. **$76 > max of $75.** The proxy is exhausted.

```
ProxyBidExhausted { ListingId: listing-A, BidderId: participant-002,
  MaxAmount: 75.00, ExhaustedAt: T+3:15 }

ProxyBidManagerSaga → Status: Exhausted → MarkCompleted()
```

**Relay:** Pushes `ProxyBidExhausted` to participant-002: "Your proxy bid maximum of $75.00 has been exceeded on Vintage Mechanical Keyboard. Bid manually to stay in." Distinct from the generic outbid notification.

Participant-002 must now decide: bid manually above $75, or let it go.

**`@QA` — resolving Question #5: What if the proxy's `PlaceBid` command is rejected?**

Consider: the proxy tries to bid $71, but between the time `BidPlaced($70)` was received and the proxy's `PlaceBid($71)` command reaches the DCB, another bidder placed $72. The DCB rejects with "BelowMinimumBid."

The proxy receives `BidRejected` (or more precisely, a new `BidPlaced` at $72 arrives, which triggers the proxy again). The proxy doesn't need explicit rejection handling because it reacts to `BidPlaced` events, not to the outcome of its own commands. The flow:

1. Proxy sees `BidPlaced($70)`, sends `PlaceBid($71)`
2. Meanwhile, `BidPlaced($72)` arrives (someone else was faster)
3. Proxy's `PlaceBid($71)` is rejected by DCB (below $72)
4. Proxy sees `BidPlaced($72)`, evaluates: $72 + $1 = $73 <= $75 max, sends `PlaceBid($73)`

The proxy is self-correcting. It reacts to the latest `BidPlaced` on the stream, not to its own command outcomes. If its bid was rejected, the competing `BidPlaced` that caused the rejection will arrive and trigger a new attempt at the correct amount.

The one edge case: what if the proxy's `PlaceBid` is rejected for "ExceedsCreditCeiling"? The proxy set a max of $75, but the bidder's credit ceiling is only $60. The proxy will try to bid $71 (above $60 ceiling), get rejected, and then the next competing `BidPlaced` will trigger another attempt at the same failing amount. This would loop.

**Fix:** The Proxy Bid Manager should cap its auto-bid at `min(nextBid, bidderCreditCeiling)`. But the proxy doesn't have the credit ceiling — that's in the Participants BC. Two options:

**Option A:** The proxy carries the bidder's credit ceiling at registration time. `RegisterProxyBid` handler reads the ceiling and stores it on the saga.

**Option B:** The proxy just trusts the DCB to reject and reacts to `BidRejected` for its own bids. If the proxy sees a `BidRejected` with reason "ExceedsCreditCeiling" for its own BidderId, it self-terminates with `ProxyBidExhausted`.

> **Decision: Option A for MVP.** Store `BidderCreditCeiling` on the proxy saga at registration. The proxy caps: `min(competingBid + increment, MaxAmount, BidderCreditCeiling)`. If the resulting amount can't beat the competing bid, exhaustion. Clean, no retry loops. Question #5 resolved.

---

### Step 9: Extended Bidding — The Snipe and Counter

**T+4:40:** Participant-003 bids $80. This is 20 seconds before the scheduled close at T+5:00, inside the 30-second trigger window.

**DCB handler:** $80 > $75 current high. $80 <= $200 ceiling. Passes. And it's in the trigger window.

```
1. BidPlaced { ListingId: listing-A, BidderId: participant-003,
     Amount: 80.00, BidCount: 11, PlacedAt: T+4:40 }

2. ExtendedBiddingTriggered {
     ListingId: listing-A,
     PreviousCloseAt: T+5:00,
     NewCloseAt: T+4:55,     // now + 15 seconds
     TriggeredByBidderId: participant-003,
     TriggeredAt: T+4:40
   }
```

**Auction Closing Saga reacts to `ExtendedBiddingTriggered`:**

```
ScheduledCloseAt: T+5:00 → T+4:55
Status: Active → Extended
→ Cancels CloseAuction at T+5:00
→ Schedules new CloseAuction at T+4:55
```

**Relay:** Pushes "Extended! New close time: [T+4:55]" to all watchers. The countdown timer on everyone's phone resets.

---

### Step 10: Second Extension — Chaining Demonstrated

**T+4:50:** Participant-002 (now bidding manually, proxy exhausted) bids $85. This is 5 seconds before the new close at T+4:55, inside the 30-second window.

```
1. BidPlaced { ... Amount: 85.00, BidCount: 12, PlacedAt: T+4:50 }

2. ExtendedBiddingTriggered {
     PreviousCloseAt: T+4:55,
     NewCloseAt: T+5:05,     // now + 15 seconds
     TriggeredByBidderId: participant-002,
     TriggeredAt: T+4:50
   }
```

**Saga updates again:** `ScheduledCloseAt: T+4:55 → T+5:05`. Cancels and reschedules.

**`@Architect` — resolving Question #6 (partial) and the MaxDuration cap:**

The listing originally closed at T+5:00 (5-minute duration). After two extensions, it now closes at T+5:05. If this kept going, a 5-minute listing could run 8, 10, 15 minutes.

`MaxDuration` caps this. If configured at 2x original (10 minutes), the listing cannot extend beyond T+10:00. The DCB handler's extended bidding check adds:

```csharp
// Extended bidding check — with MaxDuration cap
if (state.ExtendedBiddingEnabled)
{
    var timeUntilClose = state.ScheduledCloseAt - now;
    if (timeUntilClose <= state.ExtendedBiddingTriggerWindow)
    {
        var newCloseAt = now + state.ExtendedBiddingExtension;
        var maxCloseAt = state.OriginalCloseAt + state.MaxDuration;

        if (newCloseAt <= maxCloseAt)
        {
            events.Add(new ExtendedBiddingTriggered(..., newCloseAt));
        }
        // else: bid in trigger window but MaxDuration reached — no extension
    }
}
```

> **Decision on Question #6: `MaxDuration` is a platform default for MVP.** Set globally (e.g., 2x original duration). Not seller-configurable in MVP. Can be exposed as a seller option post-MVP. The Auctions BC reads it from configuration, not from `ListingPublished`. Question #6 resolved.

---

### Step 11: Close — The Moment of Truth

**T+5:05:** `CloseAuction` scheduled message fires. No more bids arrived after T+4:50.

**`@Architect` — resolving Question #3: Does the saga track bids or read the DCB?**

The current design has the saga tracking `CurrentHighBidderId`, `CurrentHighBid`, `BidCount`, and `ReserveHasBeenMet` incrementally via its `Handle(BidPlaced)` and `Handle(ReserveMet)` handlers.

The alternative: the saga's `Handle(CloseAuction)` handler loads the DCB state at close time and reads the current values. This would be more authoritative (no risk of the saga missing a `BidPlaced` message) but adds a database read to the close path.

The pragmatic answer: **incremental tracking is correct for Wolverine sagas.** Wolverine guarantees message delivery. If a `BidPlaced` message is published, the saga will process it. The risk of the saga's tracked state diverging from the stream is a Wolverine delivery failure, which would be a platform-level bug. Trust the framework.

The incremental approach is also simpler — the saga's `Handle(CloseAuction)` uses its own state to make the sold/passed decision without loading anything.

> **Decision: The Auction Closing saga tracks bids incrementally via its handlers.** No DCB read at close time. Trust Wolverine's delivery guarantees. Question #3 resolved.

**The `Handle(CloseAuction)` executes:**

```
Saga state at close time:
  CurrentHighBidderId: participant-002
  CurrentHighBid: 85.00
  BidCount: 12
  ReserveHasBeenMet: true
  BuyItNowExercised: false

Decision: BidCount > 0 AND ReserveHasBeenMet → ListingSold
```

**Events produced:**

```
1. BiddingClosed { ListingId: listing-A, ClosedAt: T+5:05 }
   (internal — mechanical fact)

2. ListingSold {
     ListingId: listing-A,
     WinnerId: participant-002,
     HammerPrice: 85.00,
     BidCount: 12,
     SoldAt: T+5:05
   }
   (integration — business outcome → Settlement, Listings, Relay, Operations)
```

**Saga completes:** `Status → Resolved`, `MarkCompleted()`.

The listing's lifecycle in the Auctions BC is over. Everything downstream (Settlement, Obligations) is other BCs' concern.

---

### Alternate Path A: ListingPassed

If at close time `ReserveHasBeenMet == false` (bidding never reached $50):

```
BiddingClosed { ListingId: listing-A, ClosedAt: T+5:00 }
ListingPassed {
  ListingId: listing-A,
  Reason: "ReserveNotMet",
  HighestBid: 45.00,
  BidCount: 6,
  PassedAt: T+5:00
}
```

If `BidCount == 0` (nobody bid at all):

```
ListingPassed {
  ListingId: listing-A,
  Reason: "NoBids",
  HighestBid: null,
  BidCount: 0,
  PassedAt: T+5:00
}
```

---

### Alternate Path B: BuyItNowPurchased

If at T+0:20, before any bids, participant-003 hits Buy It Now:

**DCB handler for `BuyNow` command:** Validates: listing is open, Buy It Now is available (not removed), amount matches the configured BuyItNowPrice.

```
BuyItNowPurchased {
  ListingId: listing-A,
  BuyerId: participant-003,
  Price: 100.00,
  PurchasedAt: T+0:20
}
```

**Auction Closing Saga reacts:** `BuyItNowExercised = true`, `Status → Resolved`, `MarkCompleted()`. The scheduled `CloseAuction` message becomes a no-op when it fires (saga already completed).

**Settlement** receives `BuyItNowPurchased` directly — it's an integration event. No `ListingSold` needed; `BuyItNowPurchased` carries the buyer and price.

---

### Alternate Path C: ListingWithdrawn — Ops Force-Close

**`@QA` — resolving Question #4:**

An Operations staff member force-closes a listing (problem with the listing, participant misconduct, or demo reset). The command is `WithdrawListing` and it's an Operations-initiated action routed to the Auctions BC.

**Handler produces:**

```
ListingWithdrawn {
  ListingId: listing-A,
  WithdrawnBy: "ops-staff",
  Reason: "Withdrawn by operations",
  WithdrawnAt: T+3:00
}
```

**Auction Closing Saga reacts to `ListingWithdrawn`:**

```csharp
public void Handle(ListingWithdrawn message)
{
    Status = AuctionClosingStatus.Resolved;
    MarkCompleted();
    // No ListingSold, no ListingPassed.
    // ListingWithdrawn IS the terminal event.
    // Settlement does NOT react to withdrawals — no money moves.
}
```

**Active Proxy Bid Managers on this listing also terminate:**

```csharp
public void Handle(ListingWithdrawn message)
{
    if (message.ListingId != ListingId) return;
    Status = ProxyBidStatus.ListingClosed;
    MarkCompleted();
}
```

The scheduled `CloseAuction` message fires later and finds the saga completed — no-op.

**`@DomainExpert` note:** `ListingWithdrawn` is not `ListingPassed`. A passed listing didn't sell because of market conditions (reserve not met, no bids). A withdrawn listing was forcibly removed by the platform. Different business meaning, different downstream reactions. Listings BC marks it differently in the catalog ("Withdrawn" vs "Passed"). No settlement, no obligations.

> **Decision: `ListingWithdrawn` terminates both the Auction Closing saga and any active Proxy Bid Manager sagas immediately. No reserve check, no sold/passed evaluation.** Question #4 resolved.

---

### Step 12: `BidRejected` — The Stream Question in Action

**T+2:45:** Participant-003 tries to bid $45 but the current high is already $56 (proxy bid at step 6).

**DCB handler:** $45 < $56. Rejected.

```
BidRejected {
  ListingId: listing-A,
  BidderId: participant-003,
  AttemptedAmount: 45.00,
  CurrentHighBid: 56.00,
  Reason: "BelowMinimumBid",
  RejectedAt: T+2:45
}
```

Per the Phase 2 decision (Question #2): this event goes to its own stream, NOT the listing-A primary stream. Tagged with `ListingId: listing-A` for Operations and Relay to find, but excluded from the DCB's `EventTagQuery`. The listing stream stays lean — only events that affect bidding state.

---

## Phase 2 Summary

**Vocabulary changes:** None. All events remain as established.

**Questions resolved:**

| # | Question | Resolution |
|---|----------|------------|
| 1 | Bid increment strategy | Two-tier: $1 under $100, $5 at $100+. Platform default, not per-listing. |
| 2 | `BidRejected` stream placement | Separate stream, tagged with `ListingId`. Excluded from DCB tag query. |
| 3 | Saga tracks vs DCB read at close | Incremental tracking via handlers. Trust Wolverine delivery guarantees. |
| 4 | `ListingWithdrawn` saga interaction | Terminates both Auction Closing saga and all Proxy Bid Manager sagas. No reserve check, no sold/passed. |
| 5 | Proxy bid rejection handling | Proxy stores `BidderCreditCeiling` at registration. Caps auto-bid at `min(next, max, ceiling)`. Self-corrects via `BidPlaced` event stream. |
| 6 | `MaxDuration` ownership | Platform default for MVP (e.g., 2x original). Not seller-configurable until post-MVP. |

**All Phase 1 questions resolved.** Zero carry-forward.

**Key lifecycle insights:**

The listing lifecycle has one primary path (open → bids → close → sold/passed) and two short-circuit paths (Buy It Now and withdrawal). The Auction Closing saga is the single authority on close evaluation — it tracks state incrementally and decides the outcome. The DCB is stateless per request — it loads, validates, produces events, and forgets. The Proxy Bid Manager is a reactive loop — it watches `BidPlaced` events and fires back until exhaustion or close.

The three components never call each other directly. They communicate entirely through events in the stream: DCB produces `BidPlaced` → saga and proxy react. DCB produces `ExtendedBiddingTriggered` → saga reschedules. Proxy produces `PlaceBid` command → DCB handles it. Clean separation via the event stream.

**New questions surfaced:**

| # | Question | Persona | Notes |
|---|----------|---------|-------|
| 7 | Should `BidRejected` events in a separate stream use a dedicated Marten stream type, or a general "audit" pattern? | `@BackendDeveloper` | Implementation detail — affects Marten configuration |
| 8 | Two proxy bid managers on the same listing: do they create a bidding war with each other? | `@QA` | Yes, by design — each proxy reacts to the other's `BidPlaced`. They escalate until one exhausts. This is correct eBay behavior but worth a specific test scenario |
| 9 | Should `BiddingOpened` carry the full listing config, or should the saga load it from the listing's event stream? | `@Architect` | Currently sketched as carrying it all in the event. Keeps the saga start self-contained |

---

## Phase 3 — Scenarios (Given/When/Then)

*Next: Implementation-ready scenarios for all Auctions BC internals, including the edge cases surfaced in this lifecycle walkthrough.*

*(to be continued)*
