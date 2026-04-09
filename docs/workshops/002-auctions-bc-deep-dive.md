# Workshop 002 — Auctions BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-04-09
**Status:** In progress — Phase 1

**Scope:** The Auctions BC internals. Aggregate state machines, saga designs, DCB boundary model, and resolution of parked questions from Workshop 001 that target this BC.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`. ProductOwner on standby for scope questions.

**Prerequisite:** Workshop 001 (Flash Session Demo-Day Journey) completed all 5 phases. This workshop assumes familiarity with that output.

**Parked questions from Workshop 001 targeting this BC:**

| # | Question | Persona |
|---|----------|---------|
| 2 | `SessionStarted` → N × `BiddingOpened` fan-out: handler design | `@Architect` |
| 3 | Promote `ProxyBidExhausted` to integration? | `@QA` |
| 4 | Multiple sequential extended bidding triggers | `@QA` |
| 5 | Reserve check authority: Auctions vs Settlement | `@QA`/`@Architect` |
| 8 | Can a proxy bid trigger extended bidding? | `@QA` |

---

## What Workshop 001 Established

From the user journey workshop, the Auctions BC has:

**Two aggregates:** `Listing` (bidding state) and `Session` (flash auction container)

**Two sagas:** Auction Closing saga (close timer, reserve evaluation, winner declaration) and Proxy Bid Manager saga (auto-bid per bidder per listing)

**One DCB boundary model:** enforces bid consistency under concurrent bidder load

**13 domain events:** 3 session-related (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`), 10 bidding/close-related

**8 P0 slices** assigned to this BC: 2.1, 2.2, 2.3, 3.1, 3.2, 3.3, 3.4, 5.1

This workshop goes deeper: what state lives on each aggregate, how do the sagas transition, what does the DCB boundary model project, and where are the edge cases?

---

## Phase 1 — Brain Dump: Internal Structure

For a BC-focused workshop, the brain dump is about **internal architecture** rather than events. What are the moving parts, how do they relate, and what questions does each raise?

### The Three Moving Parts

The Auctions BC has three distinct state containers, each with a different persistence pattern:

```
┌─────────────────────────────────────────────────────────┐
│ Auctions BC                                             │
│                                                         │
│  ┌──────────────┐                                       │
│  │   Session     │ ◄── Event-sourced aggregate          │
│  │   Aggregate   │     (Marten, own stream per session) │
│  └──────┬───────┘                                       │
│         │ SessionStarted triggers N × BiddingOpened      │
│         ▼                                               │
│  ┌──────────────┐     ┌─────────────────────────┐       │
│  │   Listing     │ ◄──│   DCB Boundary Model     │      │
│  │   (per lot)   │    │   (BidConsistencyState)   │      │
│  │              │     │   EventTagQuery loads     │      │
│  │              │     │   from listing + bidder   │      │
│  │              │     │   streams                 │      │
│  └──────┬───────┘     └─────────────────────────┘       │
│         │                                               │
│         │ BiddingOpened schedules close timer            │
│         ▼                                               │
│  ┌──────────────────────┐  ┌──────────────────────┐     │
│  │ Auction Closing Saga  │  │ Proxy Bid Manager    │     │
│  │ (1 per listing)       │  │ (1 per listing×bidder)│    │
│  │ Marten document       │  │ Marten document       │    │
│  └──────────────────────┘  └──────────────────────┘     │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

### Part 1: The `Session` Aggregate

**What it owns:**

- Session identity (SessionId, Title)
- Configuration (DurationMinutes)
- Attached listing IDs
- Session state (Created → Started → Completed)
- Staff identity of who created/started it

**What it does NOT own:**

- Individual listing bidding state (that's the Listing/DCB)
- Close timing per listing (that's the Auction Closing saga)
- Whether individual listings have bids (it doesn't track)

**Events it produces:**

- `SessionCreated` — staff creates the container
- `ListingAttachedToSession` — a published listing is assigned
- `SessionStarted` — staff hits the button, all listings open

**State machine:**

```
Created ──[AttachListingToSession]──► Created (listing count increases)
Created ──[StartSession]────────────► Started
Started ──(all listings closed)─────► Completed
```

**`@Architect` note:** The Session aggregate is simple. It's a coordination container, not a domain-rich entity. Its job is to hold the list of listings and enforce two rules: (1) can't attach to a started session, (2) can't start an empty session. Once started, it doesn't do much — the individual listings run independently.

**`@BackendDeveloper` — the fan-out question (Parked #2):**

When `StartSession` is handled, the Session aggregate produces `SessionStarted`. But who produces the N `BiddingOpened` events?

**Option A: The Session aggregate handler produces all of them.**
The `Handle(StartSession)` method returns `SessionStarted` plus N `BiddingOpened` events. Simple, atomic, but the Session aggregate is reaching into listing-level concerns.

**Option B: A Wolverine handler reacts to `SessionStarted` and produces `BiddingOpened` per listing.**
The Session aggregate produces only `SessionStarted`. A separate handler (not a saga, just a handler) receives `SessionStarted`, iterates over `ListingIds`, and produces `BiddingOpened` for each. Cleaner separation but introduces a second step.

**Option C: A Wolverine handler reacts to `SessionStarted` and sends `OpenBidding` commands per listing.**
Same as B, but instead of directly producing events, it sends one `OpenBidding` command per listing. Each command is handled independently by the listing's own handler. Most decoupled, but most moving parts.

**`@Architect` recommendation:** Option B. The Session aggregate's job is to say "this session started." The listing-level consequence (bidding opens) is a reaction, not part of the session's own state. A handler that receives `SessionStarted` and produces `BiddingOpened` per listing keeps the separation clean without the extra command indirection of Option C. The handler has access to the `ListingIds` from the `SessionStarted` event payload.

For timed listings (no Session), `BiddingOpened` is produced by the handler that receives `ListingPublished` and sets up the close timer directly. No Session involved.

**`@QA` question:** What happens if the handler that produces `BiddingOpened` from `SessionStarted` partially fails — say 3 of 5 listings get `BiddingOpened` and then it crashes? Wolverine's inbox/outbox and retry guarantees handle this — the message will be retried and idempotency on the listing side prevents duplicate opens. But worth verifying in testing.

> **Decision: Option B adopted.** Session aggregate produces `SessionStarted`. A Wolverine handler reacts to `SessionStarted` and produces `BiddingOpened` for each attached listing. Parked question #2 resolved.

---

### Part 2: The Listing — DCB Boundary Model

This is the core of the Auctions BC. Bidding on a listing is not handled by a traditional single-stream aggregate. It uses the DCB pattern because the consistency boundary spans two concerns: the listing's bidding state AND the bidder's credit ceiling.

**Why DCB, not a plain aggregate?**

A plain `Listing` aggregate could enforce "bid must exceed current high bid" by reading its own stream. But it can't enforce "bid must not exceed bidder's credit ceiling" without reaching into the Participants BC. The DCB lets us project state from multiple tagged streams into a single decision model, checked atomically.

**`@Architect` — the `BidConsistencyState` boundary model:**

```csharp
public sealed class BidConsistencyState
{
    // From BiddingOpened
    public bool IsOpen { get; private set; }
    public Guid ListingId { get; private set; }
    public Guid SellerId { get; private set; }
    public decimal StartingBid { get; private set; }
    public DateTimeOffset ScheduledCloseAt { get; private set; }
    public bool ExtendedBiddingEnabled { get; private set; }
    public TimeSpan ExtendedBiddingTriggerWindow { get; private set; }
    public TimeSpan ExtendedBiddingExtension { get; private set; }
    public bool HasBuyItNow { get; private set; }
    public decimal? BuyItNowPrice { get; private set; }

    // From BidPlaced events
    public decimal CurrentHighBid { get; private set; }
    public Guid? CurrentHighBidderId { get; private set; }
    public int BidCount { get; private set; }

    // From ReserveMet
    public bool ReserveHasBeenMet { get; private set; }

    // From BiddingClosed / ListingSold / ListingPassed
    public bool IsClosed { get; private set; }

    // From BuyItNowOptionRemoved
    public bool BuyItNowRemoved { get; private set; }

    // From ParticipantSessionStarted (bidder's stream)
    public decimal BidderCreditCeiling { get; private set; }

    // Apply methods project events into this state
    public void Apply(BiddingOpened e) { ... }
    public void Apply(BidPlaced e) { ... }
    public void Apply(BiddingClosed e) { ... }
    public void Apply(BuyItNowOptionRemoved e) { ... }
    public void Apply(ReserveMet e) { ... }
    public void Apply(ParticipantSessionStarted e) { ... }
}
```

**`@BackendDeveloper` — the `EventTagQuery`:**

```csharp
public static EventTagQuery Load(PlaceBid command)
    => EventTagQuery
        .For(new ListingStreamId(command.ListingId))
        .AndEventsOfType<BiddingOpened, BidPlaced, BiddingClosed,
                         BuyItNowOptionRemoved, ReserveMet, BuyItNowPurchased>()
        .Or(new BidderStreamId(command.BidderId))
        .AndEventsOfType<ParticipantSessionStarted>();
```

This loads all relevant events from the listing's stream (bidding state) AND the bidder's stream (credit ceiling) into the `BidConsistencyState` boundary model. Marten checks optimistic concurrency across both tag sets.

**`@BackendDeveloper` — the `PlaceBid` handler:**

```csharp
public static object[] Handle(
    PlaceBid command,
    [BoundaryModel] BidConsistencyState state,
    DateTimeOffset now)
{
    if (!state.IsOpen)
        return [new BidRejected(command.ListingId, command.BidderId,
            command.Amount, state.CurrentHighBid, "ListingNotOpen", now)];

    if (state.IsClosed)
        return [new BidRejected(command.ListingId, command.BidderId,
            command.Amount, state.CurrentHighBid, "ListingClosed", now)];

    if (command.BidderId == state.SellerId)
        return [new BidRejected(command.ListingId, command.BidderId,
            command.Amount, state.CurrentHighBid, "SellerCannotBid", now)];

    var minimumBid = state.BidCount == 0 ? state.StartingBid : state.CurrentHighBid + 0.01m;
    if (command.Amount < minimumBid)
        return [new BidRejected(command.ListingId, command.BidderId,
            command.Amount, state.CurrentHighBid, "BelowMinimumBid", now)];

    if (command.Amount > state.BidderCreditCeiling)
        return [new BidRejected(command.ListingId, command.BidderId,
            command.Amount, state.CurrentHighBid, "ExceedsCreditCeiling", now)];

    var events = new List<object>();

    var bidPlaced = new BidPlaced(command.ListingId, Guid.NewGuid(),
        command.BidderId, command.Amount, state.BidCount + 1,
        command.IsProxy, now);
    events.Add(bidPlaced);

    // First bid removes Buy It Now
    if (state.BidCount == 0 && state.HasBuyItNow && !state.BuyItNowRemoved)
        events.Add(new BuyItNowOptionRemoved(command.ListingId, now));

    // Reserve met check
    if (!state.ReserveHasBeenMet && command.Amount >= state.ReserveThreshold)
        events.Add(new ReserveMet(command.ListingId, now));

    // Extended bidding check
    if (state.ExtendedBiddingEnabled)
    {
        var timeUntilClose = state.ScheduledCloseAt - now;
        if (timeUntilClose <= state.ExtendedBiddingTriggerWindow)
        {
            var newCloseAt = now + state.ExtendedBiddingExtension;
            events.Add(new ExtendedBiddingTriggered(
                command.ListingId, state.ScheduledCloseAt, newCloseAt,
                command.BidderId, now));
        }
    }

    return events.ToArray();
}
```

**`@QA` — critical observations about this handler:**

1. **`BidRejected` is an event, not an exception.** The handler returns `BidRejected` as a domain event, not throwing. This means rejected bids are recorded in the stream — they're facts. This is important for audit and for the ops dashboard.

2. **`BuyItNowOptionRemoved` is a side effect of the first bid.** It's produced in the same handler call, atomically with `BidPlaced`. The participant who placed the first bid didn't request the removal — it's a business rule.

3. **`ReserveMet` is produced by the bid handler, not by a separate process.** The handler checks the reserve threshold and produces `ReserveMet` atomically with `BidPlaced`. This is the "threshold signal" from the Auctions side — it's not authoritative for settlement, but it's the trigger for the UI "Reserve met!" badge.

4. **Extended bidding is checked in the same handler.** If the bid lands in the trigger window, `ExtendedBiddingTriggered` is produced atomically with `BidPlaced`. No separate process needed.

**`@Architect` — the reserve threshold question (Parked #5, partial resolution):**

The `BidConsistencyState` has a `ReserveThreshold` field. Where does this come from? The reserve price is confidential — it's passed from Selling via `ListingPublished` and received by Settlement. Auctions is not supposed to "see" the raw reserve.

Two approaches:

**Approach A: Auctions receives the reserve value but treats it as opaque.** `ListingPublished` carries `ReservePrice`, Auctions stores it in its state, and the DCB uses it for the threshold check. Simple, but Auctions now "knows" the reserve, violating the confidentiality principle from the bounded-contexts doc.

**Approach B: Auctions never sees the reserve. `ReserveMet` is produced by Settlement.** Settlement receives `BidPlaced`, compares to the reserve it holds from `ListingPublished`, and publishes `ReserveMet` if the threshold is crossed. This keeps Auctions clean but introduces a round-trip: bid placed → Settlement checks → `ReserveMet` published → Auctions/Relay react. This adds latency to a real-time flow.

**Approach C: A compromise — Auctions receives only a hashed or threshold-only value.** `ListingPublished` carries a `ReserveThreshold` field (the raw dollar amount) but Auctions treats it as a comparison number, not as "the reserve price." Settlement does the binding check. The semantic difference is thin, but it allows Auctions to produce `ReserveMet` in real-time without a round-trip.

**`@DomainExpert` perspective:** On eBay, the reserve check is instantaneous — the moment your bid crosses reserve, the listing shows "Reserve met" immediately. There's no "pending reserve check" state. A round-trip through Settlement would introduce a visible delay that doesn't match participant expectations.

> **Decision: Approach C adopted.** `ListingPublished` carries the reserve value (or a threshold-only field). Auctions stores it for the real-time threshold signal. Settlement independently holds the reserve for the binding financial check. The Auctions-side `ReserveMet` is a UX signal, not a financial commitment. Settlement's `ReserveCheckCompleted` is the authoritative financial check. They should never disagree in practice (same source data), but if they did, Settlement wins.
>
> **Parked question #5 resolved.** The "authority tension" is resolved by giving them different roles: Auctions owns the real-time signal, Settlement owns the financial authority.

---

### Part 3: The Auction Closing Saga

The Auction Closing saga manages the lifecycle of a single listing from "bidding opened" through "winner declared" (or "passed").

**`@Architect` — saga state:**

```csharp
public sealed class AuctionClosingSaga : Saga
{
    public Guid Id { get; set; }           // = ListingId (one saga per listing)

    public Guid ListingId { get; set; }
    public Guid SellerId { get; set; }
    public decimal StartingBid { get; set; }
    public decimal? ReservePrice { get; set; }
    public bool ExtendedBiddingEnabled { get; set; }
    public TimeSpan ExtendedBiddingTriggerWindow { get; set; }
    public TimeSpan ExtendedBiddingExtension { get; set; }
    public DateTimeOffset ScheduledCloseAt { get; set; }

    // Bidding state tracked by the saga
    public Guid? CurrentHighBidderId { get; set; }
    public decimal CurrentHighBid { get; set; }
    public int BidCount { get; set; }
    public bool ReserveHasBeenMet { get; set; }
    public bool BuyItNowExercised { get; set; }

    public AuctionClosingStatus Status { get; set; }
}

public enum AuctionClosingStatus
{
    AwaitingBids,
    Active,
    Extended,
    Closing,
    Resolved
}
```

**State machine:**

```
                                 ┌─── BidPlaced ──┐
                                 ▼                │
AwaitingBids ──[BidPlaced]──► Active ─────────────┘
     │                           │
     │                           ├── BidPlaced (in trigger window)
     │                           │   → ExtendedBiddingTriggered
     │                           │   → cancel close, reschedule
     │                           │   → Status = Extended
     │                           │
     │                           ├── BuyItNowPurchased
     │                           │   → Status = Resolved
     │                           │   → MarkCompleted()
     │                           │
     │                           ▼
     │                       Extended ──[BidPlaced]──► Extended (can re-extend)
     │                           │
     ▼                           ▼
  [CloseAuction]            [CloseAuction]
  (scheduled timer)         (rescheduled timer)
     │                           │
     ▼                           ▼
  Closing ──────────────────► Closing
     │
     ├── ReserveHasBeenMet == true && BidCount > 0
     │   → ListingSold { WinnerId, HammerPrice }
     │   → Status = Resolved
     │   → MarkCompleted()
     │
     ├── ReserveHasBeenMet == false || BidCount == 0
     │   → ListingPassed { Reason }
     │   → Status = Resolved
     │   → MarkCompleted()
     │
     └── BuyItNowExercised == true (already resolved)
         → (no-op, saga already completed)
```

**`@BackendDeveloper` — saga handlers:**

```csharp
// Saga starts when bidding opens
public static AuctionClosingSaga Start(BiddingOpened message, IMessageContext context)
{
    var saga = new AuctionClosingSaga
    {
        Id = message.ListingId,
        ListingId = message.ListingId,
        ScheduledCloseAt = message.ScheduledCloseAt,
        Status = AuctionClosingStatus.AwaitingBids,
        // ... configuration from message
    };

    // Schedule the close timer
    context.ScheduleAsync(
        new CloseAuction(message.ListingId),
        message.ScheduledCloseAt);

    return saga;
}

// React to bids — update tracking state
public void Handle(BidPlaced message)
{
    CurrentHighBidderId = message.BidderId;
    CurrentHighBid = message.Amount;
    BidCount = message.BidCount;
    if (Status == AuctionClosingStatus.AwaitingBids)
        Status = AuctionClosingStatus.Active;
}

// React to reserve met
public void Handle(ReserveMet message)
{
    ReserveHasBeenMet = true;
}

// React to extended bidding
public OutgoingMessages Handle(ExtendedBiddingTriggered message, IMessageContext context)
{
    ScheduledCloseAt = message.NewCloseAt;
    Status = AuctionClosingStatus.Extended;

    // Cancel the old close timer, schedule a new one
    // The new CloseAuction message replaces the old one
    return [context.ScheduleAsync(
        new CloseAuction(ListingId),
        message.NewCloseAt)];
}

// The close timer fires
public OutgoingMessages Handle(CloseAuction message)
{
    if (BuyItNowExercised)
    {
        MarkCompleted();
        return OutgoingMessages.Empty;
    }

    Status = AuctionClosingStatus.Closing;

    var events = new OutgoingMessages();

    events.Add(new BiddingClosed(ListingId, DateTimeOffset.UtcNow));

    if (BidCount > 0 && ReserveHasBeenMet)
    {
        events.Add(new ListingSold(ListingId, CurrentHighBidderId!.Value,
            CurrentHighBid, BidCount, DateTimeOffset.UtcNow));
    }
    else
    {
        var reason = BidCount == 0 ? "NoBids" : "ReserveNotMet";
        events.Add(new ListingPassed(ListingId, reason,
            BidCount > 0 ? CurrentHighBid : null, BidCount, DateTimeOffset.UtcNow));
    }

    Status = AuctionClosingStatus.Resolved;
    MarkCompleted();
    return events;
}

// Buy It Now short-circuits the saga
public OutgoingMessages Handle(BuyItNowPurchased message)
{
    BuyItNowExercised = true;
    Status = AuctionClosingStatus.Resolved;
    MarkCompleted();
    return OutgoingMessages.Empty;
    // Note: BuyItNowPurchased was already produced by the DCB handler.
    // The saga just marks itself complete. Settlement reacts to BuyItNowPurchased directly.
}
```

**`@QA` — Parked question #4 resolved: Multiple sequential extended bidding triggers.**

Each `ExtendedBiddingTriggered` updates `ScheduledCloseAt` on the saga and reschedules the `CloseAuction` message. This can happen multiple times in sequence — each bid in the trigger window extends again. The saga tracks the current `ScheduledCloseAt` so each extension is relative to the latest close time.

There's no theoretical limit to extensions. In practice, a 5-minute Flash Session listing with 15-second extensions could extend to 6, 7, 8 minutes if bidders keep sniping. This is intentional — it's the anti-snipe mechanic working as designed. The demo-mode timeout cap (PO decision from Workshop 001) should apply here: a maximum total duration per listing regardless of extensions.

> **Decision: No hard extension count limit, but a maximum total listing duration should be configurable.** Example: a listing cannot extend beyond 2× its original duration. This prevents runaway extensions in both demo and production modes. Implementation detail for the Auctions BC — add a `MaxDuration` field to the saga state.

**`@QA` — Parked question #8 resolved: Can a proxy bid trigger extended bidding?**

Yes. The `PlaceBid` handler in the DCB doesn't care whether the bid is manual or proxy. If a proxy auto-bid fires (Proxy Bid Manager sends a `PlaceBid` command with `IsProxy: true`) and that bid lands within the trigger window, `ExtendedBiddingTriggered` is produced. The Auction Closing saga's `Handle(ExtendedBiddingTriggered)` reschedules the close.

This is correct behavior. The anti-snipe mechanic protects against all last-second bids, not just manual ones. If a proxy bid extends the close, a competing bidder has more time to respond — which is exactly what anti-snipe is designed for.

> **Decision: Proxy bids can trigger extended bidding. No special handling needed.** The DCB handler is bid-source-agnostic. Parked question #8 resolved.

---

### Part 4: The Proxy Bid Manager Saga

One saga instance per (ListingId, BidderId) pair. It auto-bids on behalf of a participant up to their registered maximum.

**`@Architect` — saga state:**

```csharp
public sealed class ProxyBidManagerSaga : Saga
{
    public Guid Id { get; set; }  // Composite key: deterministic from ListingId + BidderId

    public Guid ListingId { get; set; }
    public Guid BidderId { get; set; }
    public decimal MaxAmount { get; set; }
    public decimal LastBidAmount { get; set; }
    public ProxyBidStatus Status { get; set; }
}

public enum ProxyBidStatus
{
    Active,
    Exhausted,
    ListingClosed
}
```

**`@BackendDeveloper` — correlation key:**

The saga ID must be deterministic from `ListingId + BidderId` so that when `BidPlaced` events arrive, Wolverine can route them to the correct saga instance. UUID v5 with a BC-specific namespace:

```csharp
public static Guid ProxyBidSagaId(Guid listingId, Guid bidderId)
    => UuidV5.Create(AuctionsNamespace, $"{listingId}:{bidderId}");
```

**State machine:**

```
(RegisterProxyBid) ──► Active
                          │
                          ├── BidPlaced (by someone else, on this listing)
                          │   amount < MaxAmount?
                          │   → send PlaceBid { Amount = competingBid + increment, IsProxy = true }
                          │   → stay Active
                          │
                          ├── BidPlaced (by someone else, on this listing)
                          │   amount >= MaxAmount?
                          │   → ProxyBidExhausted
                          │   → Status = Exhausted
                          │   → MarkCompleted()
                          │
                          ├── ListingSold / ListingPassed / ListingWithdrawn
                          │   → Status = ListingClosed
                          │   → MarkCompleted()
                          │
                          └── BidPlaced (by THIS bidder, manually)
                              → update LastBidAmount (manual bid overrides proxy tracking)
                              → stay Active
```

**`@BackendDeveloper` — the auto-bid handler:**

```csharp
public OutgoingMessages Handle(BidPlaced message)
{
    // Ignore our own proxy bids and bids on other listings
    if (message.ListingId != ListingId) return OutgoingMessages.Empty;

    // If this is our own bid (manual or proxy), just track it
    if (message.BidderId == BidderId)
    {
        LastBidAmount = message.Amount;
        return OutgoingMessages.Empty;
    }

    // Someone else bid. Can we outbid them?
    var nextBid = message.Amount + BidIncrement;

    if (nextBid > MaxAmount)
    {
        Status = ProxyBidStatus.Exhausted;
        MarkCompleted();
        return [new ProxyBidExhausted(ListingId, BidderId, MaxAmount, DateTimeOffset.UtcNow)];
    }

    // Auto-bid
    return [new PlaceBid(ListingId, BidderId, nextBid, IsProxy: true)];
}
```

**`@QA` — Parked question #3: Promote `ProxyBidExhausted` to integration?**

The question: should Relay be able to push a "your proxy is done, you've been outbid" notification?

Current reasoning from Workshop 001: the participant already gets an outbid notification from `BidPlaced`. `ProxyBidExhausted` is redundant from a notification standpoint.

But there's a distinction. "You've been outbid" (from `BidPlaced`) is temporary — the proxy might auto-bid back. "Your proxy is exhausted" means the automatic defense is gone. The participant needs to manually bid if they want to continue. That's a different signal with different urgency.

**`@DomainExpert` perspective:** On eBay, proxy bidding is silent — you don't get a "your automatic bid is done" notification per se, but you do get notified when you're fully outbid. The "fully outbid" notification effectively means the same thing — proxy was either exhausted or you didn't have one. eBay doesn't distinguish.

**`@UX` perspective (consulted):** For CritterBids' demo context, the distinction matters more than it does on eBay. In a 5-minute Flash Session, a participant needs to know immediately that their proxy is spent so they can decide whether to bid manually. "You've been outbid" is ambiguous — does their proxy still have room? "Your proxy bid has been exceeded" is clear.

> **Decision: Promote `ProxyBidExhausted` to integration.** Relay consumes it to push a specific "Your proxy bid maximum of $X has been exceeded on [Listing]. Bid manually to stay in." notification. This is distinct from the generic outbid notification. Parked question #3 resolved.

---

## Phase 1 Summary

**Vocabulary changes:**

- `ProxyBidExhausted`: 🟠 Internal → 🔵 Integration. Consumed by Relay for a specific proxy exhaustion notification.

**Parked questions resolved:**

| # | Question | Resolution |
|---|----------|------------|
| 2 | Fan-out handler design | Option B: Session produces `SessionStarted`, handler reacts and produces `BiddingOpened` per listing |
| 3 | Promote `ProxyBidExhausted`? | Yes — promoted to integration. Relay pushes a specific "proxy exceeded" notification |
| 4 | Multiple sequential extensions | No count limit, but add `MaxDuration` config to cap total listing duration |
| 5 | Reserve check authority | Auctions owns the real-time UX signal (`ReserveMet`). Settlement owns the financial authority (`ReserveCheckCompleted`). Same source data, different roles |
| 8 | Can a proxy bid trigger extended bidding? | Yes — the DCB handler is bid-source-agnostic. Anti-snipe protects against all last-second bids |

**New design elements identified:**

- `BidConsistencyState` boundary model — sketched with `Apply` methods and `EventTagQuery`
- `AuctionClosingSaga` state machine — 5 states, all transitions defined
- `ProxyBidManagerSaga` state machine — 3 states, correlation key strategy defined
- `MaxDuration` configuration — prevents runaway extended bidding
- `BidIncrement` — the proxy bid manager needs a bid increment strategy (minimum increment above competing bid)

**New questions surfaced:**

| # | Question | Persona | Notes |
|---|----------|---------|-------|
| 1 | What is the bid increment strategy? Fixed amount, percentage, or tiered? | `@DomainExpert` | eBay uses tiered increments based on current price. CritterBids could simplify to a fixed $1 or percentage for MVP |
| 2 | Does `BidRejected` go into the listing stream or a separate stream? | `@Architect` | If it's in the listing stream, it inflates the event count. If separate, the DCB tag query doesn't load it (which is fine — rejections don't affect bidding state) |
| 3 | Should the Auction Closing saga track `BidPlaced` events or read from the DCB? | `@Architect` | Currently sketched as tracking via handler. Alternative: saga's `CloseAuction` handler reads the DCB state at close time instead of tracking incrementally |
| 4 | How does `ListingWithdrawn` (ops force-close) interact with the saga? | `@QA` | The saga needs a handler for this — it should skip the reserve check and go straight to Resolved/MarkCompleted |
| 5 | What happens if a `PlaceBid` command from the Proxy Bid Manager is rejected? | `@QA` | The proxy sent a bid but it was rejected (timing, credit, etc.). Does the proxy retry? Give up? This needs a defined behavior |
| 6 | Should `MaxDuration` be a per-listing config from the seller or a platform default? | `@ProductOwner` | Leaning toward platform default with seller override in post-MVP |

---

## Phase 2 — Storytelling (Aggregate/Saga Lifecycle)

*Next: Walk through the complete lifecycle of a single listing from `BiddingOpened` through resolution, showing how the DCB, Auction Closing saga, and Proxy Bid Manager interact at each step.*

*(to be continued)*

---

## Phase 3 — Scenarios (Given/When/Then)

*(not yet started — will produce implementation-ready scenarios for all Auctions BC internals)*
