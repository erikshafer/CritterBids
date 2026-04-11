# Dynamic Consistency Boundary (DCB)

## What Is DCB?

The **Dynamic Consistency Boundary** pattern enforces consistency in event-driven systems without rigid per-aggregate transactional boundaries. Introduced by Sara Pellegrini in *"Killing the Aggregate"* and documented at [dcb.events](https://dcb.events/).

In standard event sourcing, a consistency boundary maps 1:1 to an aggregate and its stream. DCB relaxes that constraint by allowing a single consistency boundary to span **multiple event streams**, selected dynamically at command-handling time via **event tags**.

The core idea: instead of pulling two aggregates into a saga to enforce a cross-entity invariant, you query for all events tagged with the relevant identifiers, project them into a single decision model, and write your new event(s) atomically — with optimistic concurrency checked against that same tag query. Multi-stream consistency without sagas.

---

## When to Reach for DCB

DCB is appropriate when:

- A command must enforce invariants that naturally span two or more entities
- A saga feels like accidental complexity — you're coordinating state that is fundamentally a single decision
- You want one event to represent one fact, not two compensating events representing the same business outcome

Do **not** reach for DCB when a single aggregate stream is sufficient. It adds moving parts and should earn its place.

### Quick Decision Guide

| Scenario | Tool |
|---|---|
| Single aggregate stream, single invariant boundary | Normal event-sourced aggregate handler |
| Multiple event streams, one BC, one immediate decision | Consider DCB |
| Long-running workflow, cross-BC coordination, retries, external side effects | Saga / process manager |

---

## The Wolverine DCB API

### `EventTagQuery`

A fluent API specifying which tagged events to load from Marten/Polecat before your handler runs. Defined in a `Load()` method on your handler class.

```csharp
public static EventTagQuery Load(PlaceBid command)
    => EventTagQuery
        .For(new ListingStreamId(command.ListingId))
        .AndEventsOfType<BiddingOpened, BidPlaced, BiddingClosed, ExtendedBiddingTriggered>()
        .Or(new BidderStreamId(command.BidderId))
        .AndEventsOfType<ParticipantSessionStarted>();
```

The event store loads all events matching **any** tag criteria and projects them into your aggregate state via the standard `Apply()` methods.

### `[BoundaryModel]` Attribute

Marks a handler parameter as the projected state built from the DCB tag query. The parameter type is a plain C# class with `Apply()` methods — not tied to a single stream.

```csharp
public static BidPlaced Handle(
    PlaceBid command,
    [BoundaryModel] BidConsistencyState state)
{
    if (!state.IsOpen)
        throw new InvalidOperationException("Listing is not open for bidding");

    if (state.SellerBiddingOnOwnListing(command.BidderId))
        throw new InvalidOperationException("Seller cannot bid on their own listing");

    if (command.Amount <= state.CurrentHighBid)
        throw new InvalidOperationException("Bid must exceed current high bid");

    if (state.WouldExceedCreditCeiling(command.Amount))
        throw new InvalidOperationException("Bid exceeds available credit");

    return new BidPlaced(command.ListingId, command.BidId, command.BidderId, command.Amount, ...);
}
```

### `IEventBoundary<T>`

For cases where you need direct control over event appending rather than returning events as a value:

```csharp
public static void Handle(
    PlaceBid command,
    [BoundaryModel] IEventBoundary<BidConsistencyState> boundary)
{
    var state = boundary.Aggregate;
    // validation...
    boundary.AppendOne(new BidPlaced(...));
}
```

### Return Value Patterns

The DCB workflow supports the same return patterns as the standard aggregate handler workflow:

- A single event object — appended directly
- `IEnumerable<object>` or `Events` — multiple events appended
- `IAsyncEnumerable<object>` — async event enumeration
- `OutgoingMessages` — cascading messages (not appended as events)
- `ISideEffect` — standard side effect handling

### Concurrency

Wolverine/Marten (or Wolverine/Polecat) enforces optimistic concurrency using the same tag query that loaded events. If any matching event was appended between your load and your save, an exception is thrown. No saga coordination, no compensating events.

- **Marten:** throws `DcbConcurrencyException` (from `Marten.Exceptions`)
- **Polecat:** throws `DcbConcurrencyException` (from `Polecat.Events.Dcb`) — same type name, different namespace

---

## The Boundary State Aggregate

A plain class with `Apply()` methods, projecting events from **multiple logical streams** because the event store loads by tag, not by stream ID.

```csharp
public class BidConsistencyState
{
    // Required by Marten/Polecat — boundary models are registered as documents
    public Guid Id { get; set; }

    // From Listing stream
    public Guid? ListingId { get; private set; }
    public Guid? SellerId { get; private set; }
    public bool IsOpen { get; private set; }
    public decimal CurrentHighBid { get; private set; }

    // From ParticipantSession stream
    public Guid? BidderId { get; private set; }
    public decimal CreditCeiling { get; private set; }
    public decimal CreditUsed { get; private set; }

    public bool SellerBiddingOnOwnListing(Guid bidderId) =>
        SellerId.HasValue && SellerId.Value == bidderId;

    public bool WouldExceedCreditCeiling(decimal amount) =>
        (CreditUsed + amount) > CreditCeiling;

    public void Apply(BiddingOpened e) { ListingId = e.ListingId; SellerId = e.SellerId; IsOpen = true; }
    public void Apply(BidPlaced e) { CurrentHighBid = e.Amount; CreditUsed += e.Amount; }
    public void Apply(BiddingClosed e) { IsOpen = false; }
    public void Apply(ParticipantSessionStarted e) { BidderId = e.BidderId; CreditCeiling = e.CreditCeiling; }
}
```

---

## Unit Testing

DCB handlers receive plain state objects — no infrastructure required:

```csharp
var state = new BidConsistencyState();
state.Apply(new BiddingOpened(listingId, sellerId, startingBid: 10m));
state.Apply(new ParticipantSessionStarted(bidderId, creditCeiling: 100m));

var result = BidHandler.Handle(
    new PlaceBid(listingId, Guid.NewGuid(), bidderId, amount: 25m),
    state);

result.ShouldBeOfType<BidPlaced>();
result.Amount.ShouldBe(25m);
```

Integration tests should also verify: tag selection loads the intended boundary, concurrent matching writes trigger `DcbConcurrencyException`, duplicate bids behave correctly under race conditions.

---

## Implementation Checklist

Follow these steps when introducing DCB to a BC.

**1. Define strong-typed tag ID records** — one per stream type. Must NOT use raw `Guid` — .NET 10 added `Variant` and `Version` as public instance properties, breaking Marten's `ValueTypeInfo` validation which requires exactly one public instance property.

```csharp
public sealed record ListingStreamId(Guid Value);
public sealed record BidderStreamId(Guid Value);
```

**2. Register tag types in the BC's Marten/Polecat configuration:**

```csharp
// Marten or Polecat — same API
opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<Listing>();
opts.Events.RegisterTagType<BidderStreamId>("bidder").ForAggregate<ParticipantSession>();
```

**3. Add both `ConcurrencyException` and `DcbConcurrencyException` retry policies.** They are siblings, not parent-child. A separate `opts.OnException<DcbConcurrencyException>()` entry is required.

```csharp
opts.OnException<ConcurrencyException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
opts.OnException<DcbConcurrencyException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
```

**4. Tag events explicitly in every handler writing to DCB-managed streams.** `[WriteAggregate]`, `IStartStream`, and raw `session.Events.Append(streamId, rawObject)` do NOT populate tag tables.

The tagging API differs between Marten and Polecat:

```csharp
// Marten — AddTag()
var wrapped = session.Events.BuildEvent(evt);
wrapped.AddTag(new ListingStreamId(listingId));
session.Events.Append(listingId, wrapped);

// Polecat — WithTag() (variadic, can tag multiple in one call)
var wrapped = session.Events.BuildEvent(evt);
wrapped.WithTag(new ListingStreamId(listingId), new BidderStreamId(bidderId));
session.Events.Append(listingId, wrapped);
```

**5. Define the boundary state class** with `Apply()` methods for all event types from both streams. Include `public Guid Id { get; set; }` — without it, test cleanup operations throw during teardown causing cascading failures.

**6. Write the DCB handler with three methods:**
- `Load()` returning `EventTagQuery` — spans both streams
- `Before()` with boundary state as a plain parameter (no `[BoundaryModel]` on `Before()`)
- `Handle()` with `[BoundaryModel] IEventBoundary<TState>` — use `boundary.AppendOne()` for atomic append

---

## Gotchas and Non-Obvious Behavior

**`StartStream` drops tags.** When passing a pre-tagged `IEvent` to `StartStream`, the store re-wraps the object and drops the tags. Use `Append` instead — it correctly preserves pre-wrapped `IEvent` objects. Streams are created implicitly on first append.

**`AndEventsOfType` is required, not optional.** Calling `.For(tagValue)` or `.Or(tagValue)` alone creates no query condition. Each tag arm must be followed by `.AndEventsOfType<T1, T2, ...>()`. Without it, `FetchForWritingByTags` throws `ArgumentException` at runtime.

**`[BoundaryModel]` on `Handle()` only.** Adding it to `Before()` as well causes Wolverine codegen error CS0128 (duplicate local variable in generated code). `Before()` receives the projected state as a plain parameter automatically.

**`DcbConcurrencyException` vs `ConcurrencyException` are separate types, and Marten vs Polecat namespaces differ.**

| | Marten | Polecat |
|---|---|---|
| `DcbConcurrencyException` | `Marten.Exceptions.MartenException` subclass | `Polecat.Events.Dcb.DcbConcurrencyException` |
| `ConcurrencyException` | `JasperFx.ConcurrencyException` | `JasperFx.ConcurrencyException` (same) |

Catching one does not catch the other. Both need explicit retry policies regardless of store.

**Tagging API differs between Marten and Polecat.** Use `wrapped.AddTag(tagValue)` for Marten, `wrapped.WithTag(tagValues...)` for Polecat. See step 4 in the implementation checklist above.

**Tag tables are strictly opt-in at write time.** Every handler appending to a DCB-managed stream must use `BuildEvent()` + `AddTag()`/`WithTag()` + `Append()` (or `boundary.AppendOne()` in the DCB handler itself).

**Boundary models need a `Guid Id` property.** Without it, test cleanup operations throw `InvalidDocumentException` during teardown, causing cascading test failures.

---

## CritterBids Usage

### Canonical Example — Auctions BC: Bid Placement

Bid placement is CritterBids' primary DCB scenario. A valid bid must simultaneously enforce:

- The listing is open for bidding (Listing stream)
- The bid amount exceeds the current high bid (Listing stream)
- The bidder is not the seller (Listing stream + ParticipantSession stream)
- The bid would not exceed the bidder's hidden credit ceiling (ParticipantSession stream)

These invariants span two streams. A saga would be overkill — this is a single atomic decision. DCB is the right tool.

The `BidConsistencyState` boundary model (shown above) projects the relevant facts from both streams. The `PlaceBidHandler` makes the decision and appends `BidPlaced` atomically with cross-stream optimistic concurrency.

### Decision Boundary Guardrails

- **Auctions BC** — bid placement is the canonical DCB use case
- **Selling BC** — single-stream listing lifecycle; no DCB needed
- **Settlement BC** — sequential saga steps; not a single-decision boundary
- Any scenario requiring cross-BC coordination, external calls, or delayed completion → saga, not DCB

---

## References

- [dcb.events](https://dcb.events/) — pattern specification
- [Wolverine Docs: DCB with Marten](https://wolverinefx.io/guide/durability/marten/event-sourcing.html#dynamic-consistency-boundary-dcb)
- [Wolverine Docs: DCB with Polecat](https://wolverinefx.net/guide/durability/polecat/event-sourcing)
- [Sara Pellegrini — "Killing the Aggregate"](https://sara.event-thinking.io/2023/04/kill-aggregate-chapter-1-I-will-tell-you-a-story.html)
