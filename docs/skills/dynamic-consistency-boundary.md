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
| Two or more **known** streams with IDs on the command | Cross-stream aggregate handler with multiple `[WriteAggregate]` parameters — see `marten-event-sourcing.md` §9 |
| Multiple event streams, one BC, one immediate decision, streams selected by query | Consider DCB |
| Long-running workflow, cross-BC coordination, retries, external side effects | Saga / process manager |

The key split is **known IDs vs dynamic selection**. If the command carries the stream IDs and you load fixed streams, multiple `[WriteAggregate]` parameters are simpler. If the set of streams is selected by a tag query at handler time ("all active sessions for this listing", "all courses with available capacity"), DCB is the right tool.

---

## Two Patterns, One Pattern Preferred

Wolverine exposes two ways to write a DCB handler, and it matters which one you pick:

- **Canonical: `[BoundaryModel]` pattern.** Attribute-driven. Two static methods (`Load`, `Handle`) with an optional pipeline hook. Wolverine codegen wires up the tag-query fetch and the atomic append. This is Jeremy Miller's demoed shape — the `#region sample_wolverine_dcb_boundary_model_handler` target in the Wolverine repo. **Prefer this unless you have a specific reason not to.**
- **Manual: `FetchForWritingByTags` pattern.** Imperative. A single `Handle(command, IDocumentSession)` method that calls `session.Events.FetchForWritingByTags<T>(query)` and works against the returned `IEventBoundary<T>` directly. This is the low-level escape hatch — useful when you need direct control over the boundary (e.g. conditional append of multiple events, inspection of `boundary.Events` before deciding, idempotency checks against `boundary.Aggregate` presence). Skip unless you genuinely need it.

`[WriteAggregate]` also composes with DCB (verified in Wolverine's `ChangeCourseCapacity.cs` which puts all three handler shapes side-by-side for the same command), but is not the canonical DCB attribute — the `#region sample` tag is on `[BoundaryModel]`. Use `[BoundaryModel]` for DCB; reserve `[WriteAggregate]` for classic per-stream aggregate commands.

---

## The Canonical `[BoundaryModel]` Pattern

Two static methods on the handler class, plus an optional pipeline hook.

### `Load()` — returns the `EventTagQuery`

```csharp
public static EventTagQuery Load(PlaceBid command)
    => EventTagQuery
        .For(new ListingStreamId(command.ListingId))
        .AndEventsOfType<BiddingOpened, BidPlaced, BuyItNowOptionRemoved, BiddingClosed, ExtendedBiddingTriggered>()
        .Or(new BidderStreamId(command.BidderId))
        .AndEventsOfType<ParticipantSessionStarted>();
```

`Load` is static, takes the command, returns `EventTagQuery`. Wolverine calls it before the handler runs, uses the result to fetch the tagged events, and projects them into the boundary state.

### `Handle()` — takes the boundary state, returns events

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

Key points:
- `[BoundaryModel]` goes on the state parameter of `Handle` — never on any other method's state parameter
- The state class is plain C# with per-event `Apply(T)` methods (or an `Evolve(IEvent)` switch — either works)
- The method returns the event(s) to append — no `IEventBoundary<T>` parameter, no manual `AppendOne` call
- Wolverine appends the returned event(s) atomically with optimistic concurrency checked against the same tag query

### Return value shapes

- A single event object — appended directly
- A nullable event (`BidPlaced?`) — returning `null` is a valid no-op; nothing appended
- `IEnumerable<object>` or `Events` — multiple events appended in one boundary write
- `IAsyncEnumerable<object>` — async enumeration
- `OutgoingMessages` — cascading messages (not appended as events)
- `ISideEffect` — standard side-effect handling

### Optional pipeline hooks — `Before()` or `Validate()`

You can add a Wolverine middleware-style hook that runs before `Handle` and sees the same boundary state as a plain parameter (no attribute needed). Two conventional names:

```csharp
// Validate — returns HandlerContinuation for pipeline-aware bail-out
public static HandlerContinuation Validate(
    PlaceBid command,
    BidConsistencyState state,
    ILogger logger)
{
    if (!state.IsOpen)
    {
        logger.LogDebug("Listing {ListingId} is not open for bidding", command.ListingId);
        return HandlerContinuation.Stop;   // short-circuits — Handle never runs, nothing appended
    }
    return HandlerContinuation.Continue;
}
```

`Before()` works the same way; it's the older Wolverine convention name. `HandlerContinuation.Stop` is the clean way to bail without throwing — contrast with the exception-throwing pattern shown in `Handle()` above. Both are valid; `Validate` + `HandlerContinuation` gives better logging and fewer stack traces in normal rejection paths, while exceptions make sense when you genuinely want the dispatch to fail loudly.

**Do not put `[BoundaryModel]` on the `Before()` or `Validate()` state parameter** — doing so causes Wolverine codegen error CS0128 (duplicate local variable in generated code). The attribute is on `Handle` alone; pipeline hooks receive the already-loaded state by plain-parameter injection.

---

## The Manual `FetchForWritingByTags` Pattern

For cases where you need direct control over the boundary — conditional appends based on current event count, idempotency checks on `boundary.Aggregate is not null`, or inspection of `boundary.Events` before deciding. Skip this section if the canonical pattern fits.

```csharp
public static async Task Handle(SubscribeStudentToCourse command, IDocumentSession session)
{
    var query = new EventTagQuery()
        .Or<CourseCreated, CourseId>(command.CourseId)
        .Or<StudentEnrolledInFaculty, StudentId>(command.StudentId)
        .Or<StudentSubscribedToCourse, StudentId>(command.StudentId);

    var boundary = await session.Events.FetchForWritingByTags<SubscriptionState>(query);

    var state = boundary.Aggregate ?? new SubscriptionState();
    if (state.AlreadySubscribed)
        return;                           // idempotent no-op

    boundary.AppendOne(new StudentSubscribedToCourse(...));
    // SaveChangesAsync handled by Wolverine transactional middleware
}
```

Key differences from the canonical pattern:
- No `[BoundaryModel]` attribute anywhere
- Takes `IDocumentSession` directly
- Returns `Task` (or `Task<T>`), not the event
- Uses `session.Events.FetchForWritingByTags<T>(query)` returning `IEventBoundary<T>`
- Calls `boundary.AppendOne(evt)` to append; reads `boundary.Aggregate` for state, `boundary.Events` for the loaded events

If a handler with this shape should be registered as a Wolverine message handler, mark other handlers for the same command with `[WolverineIgnore]` — only one handler can be auto-discovered per command type. The Wolverine University example uses `[WolverineIgnore]` on the manual variant precisely so the `[BoundaryModel]` variant is the one Wolverine picks up.

---

## `EventTagQuery` — the shared query DSL

Both patterns use the same query shape. Two equivalent construction styles:

### Fluent (preferred for `[BoundaryModel]` handlers)

```csharp
EventTagQuery
    .For(command.CourseId)
    .AndEventsOfType<CourseCreated, CourseCapacityChanged, StudentSubscribedToCourse>()
    .Or(command.StudentId)
    .AndEventsOfType<StudentEnrolledInFaculty, StudentSubscribedToCourse>();
```

- `For(tag)` starts the query and sets the "current tag context"
- `AndEventsOfType<T1, T2, ...>()` adds one condition per event type for the current tag — supports up to six event types per call
- `Or(tag)` switches the current tag and adds a new OR-branch
- Each chain alternates `Or(tag)` and `AndEventsOfType<...>()` until the query is complete

### Imperative (preferred for manual handlers)

```csharp
new EventTagQuery()
    .Or<CourseCreated, CourseId>(command.CourseId)
    .Or<StudentEnrolledInFaculty, StudentId>(command.StudentId)
    .Or<StudentSubscribedToCourse, StudentId>(command.StudentId);
```

Each `.Or<TEvent, TTag>(tagValue)` adds one `EventTagQueryCondition(EventType, TagType, TagValue)`. More verbose but more explicit — every condition names its event type inline.

Both produce the same internal condition list. Pick whichever reads cleaner for the query at hand.

---

## The Boundary State Aggregate

A plain class with per-event `Apply(T)` methods, projecting events from **multiple logical streams** because the event store loads by tag, not by stream ID.

```csharp
public class BidConsistencyState
{
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

Private-setter properties are fine. Per-event `Apply(T)` is the common shape.

### Alternative: `Evolve(IEvent)` with a switch

Instead of per-event `Apply(T)` methods, the state can expose a single `Evolve(IEvent e)` method with a switch on `e.Data`:

```csharp
public void Evolve(IEvent e)
{
    switch (e.Data)
    {
        case BiddingOpened opened: /* ... */ break;
        case BidPlaced placed:     /* ... */ break;
        case BiddingClosed _:      IsOpen = false; break;
    }
}
```

Useful when the state needs access to `IEvent` metadata (version, timestamp, sequence) rather than just the payload. Both shapes are supported by `[BoundaryModel]` — don't mix them on the same state class.

### On `public Guid Id { get; set; }`

The canonical Wolverine University state classes (`SubscriptionState`, `CourseState`, `EnrolledStudentState`, `UnsubscriptionState`, `AllCoursesFullyBookedState`) do **not** carry a `Guid Id` property. DCB itself does not require it. Some CritterSupply test harnesses have observed `InvalidDocumentException` during teardown when tag-registered document types are cleaned up without an `Id` — in those cases, adding `public Guid Id { get; set; }` to the state class is the known workaround. Add it only when the test-harness failure is actually observed; do not add speculatively.

### Concurrency

Wolverine/Marten (or Wolverine/Polecat) enforces optimistic concurrency using the same tag query that loaded events. If any matching event was appended between your load and your save, an exception is thrown. No saga coordination, no compensating events.

- **Marten:** throws `DcbConcurrencyException` (from `Marten.Exceptions`)
- **Polecat:** throws `DcbConcurrencyException` (from `Polecat.Events.Dcb`) — same type name, different namespace

Register retry policies for both `ConcurrencyException` and `DcbConcurrencyException` — they are siblings, not parent-child. A separate `opts.OnException<DcbConcurrencyException>()` entry is required alongside the `ConcurrencyException` one.

---

## Tagging Writes

Events produced by the `[BoundaryModel]` handler are tagged automatically — Wolverine inherits the tag context from the `Load` query and applies it to the returned event(s) before appending through the boundary.

For **test seeding** and any direct `session.Events.Append` in production code outside the DCB handler, you must tag events explicitly. Two equivalent methods live on `IEvent` in `JasperFx.Events` (shared across Marten and Polecat):

```csharp
// AddTag — void, mutates the event in place
void AddTag<TTag>(TTag tag) where TTag : notnull;
void AddTag(EventTag tag);

// WithTag — fluent extension that wraps AddTag and returns the event
public static IEvent WithTag<TTag>(this IEvent e, TTag tag);
public static IEvent WithTag(this IEvent e, params object[] tags);   // variadic
```

Use `WithTag` when you want to chain:

```csharp
var wrapped = session.Events.BuildEvent(new BiddingOpened(...));
wrapped.WithTag(new ListingStreamId(listingId));
session.Events.Append(listingId, wrapped);
```

Or variadic for events that carry multiple tags at once:

```csharp
var wrapped = session.Events.BuildEvent(new BidPlaced(...));
wrapped.WithTag(new ListingStreamId(listingId), new BidderStreamId(bidderId));
session.Events.Append(listingId, wrapped);
```

`AddTag` and `WithTag` are interchangeable — pick whichever reads cleaner. Both work on Marten and Polecat identically because the methods live in `JasperFx.Events`, not in either store-specific library.

---

## Unit and Integration Testing

### Unit tests — no infrastructure

Canonical `[BoundaryModel]` handlers receive plain state objects, making them unit-testable with no event store:

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

Suitable for pure validation logic where no tag-query behavior is being tested.

### Integration tests — through the bus

When you need to verify tag selection, boundary load correctness, or concurrency behavior, invoke through Wolverine with tag types registered on the store:

```csharp
// Fixture setup
m.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>();
m.Events.RegisterTagType<BidderStreamId>("bidder").ForAggregate<BidConsistencyState>();
m.Events.AddEventType<BiddingOpened>();
// ... register every event type the tag query will load

// Test body
await SeedListingAndBidder(listingId, bidderId);
await host.InvokeMessageAndWaitAsync(new PlaceBid(listingId, bidId, bidderId, amount: 25m));

await using var session = store.LightweightSession();
var events = await session.Events.QueryByTagsAsync(
    new EventTagQuery().Or<ListingStreamId>(listingId));
events.ShouldContain(e => e.Data is BidPlaced);
```

Test seeding uses `session.Events.BuildEvent(evt)` + `wrapped.WithTag(tagValue)` + `session.Events.Append(streamKey, wrapped)` — see `boundary_model_workflow_tests.cs::SeedCourseAndStudent` in the Wolverine repo for the canonical shape.

Integration tests should also verify: tag selection loads the intended boundary, concurrent matching writes trigger `DcbConcurrencyException`, duplicate dispatches behave correctly under race conditions.

---

## Implementation Checklist

Follow these steps when introducing DCB to a BC.

**1. Define strong-typed tag ID records** — one per stream type. Must NOT use raw `Guid` — .NET 10 added `Variant` and `Version` as public instance properties, breaking Marten's `ValueTypeInfo` validation which requires exactly one public instance property.

```csharp
public sealed record ListingStreamId(Guid Value);
public sealed record BidderStreamId(Guid Value);
```

**2. Register tag types in the BC's Marten/Polecat configuration:**

The registration API is identical across stores — `opts.Events.RegisterTagType<T>(alias).ForAggregate<TState>()` works the same way on `StoreOptions` for Marten and on Polecat's `StoreOptions` equivalent:

```csharp
// Marten or Polecat — same API shape
opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>();
opts.Events.RegisterTagType<BidderStreamId>("bidder").ForAggregate<BidConsistencyState>();
```

`.ForAggregate<TState>()` binds the tag type to the boundary state type — the same tag type can be registered against multiple state types if different handlers need it.

> **Store coverage.** DCB works uniformly on Marten and Polecat today. `[BoundaryModel]`, `EventTagQuery`, and `IEventBoundary<T>` exist in both `Wolverine.Marten` / `Marten.Events.Dcb` and their Polecat equivalents. `EventTagQuery` itself lives in the shared `JasperFx.Events` core library. If you're reading a doc that claims DCB is Polecat-only, treat that as stale — see the Wolverine repo's `MartenTests/Dcb/` folder for canonical Marten coverage.

**3. Add both `ConcurrencyException` and `DcbConcurrencyException` retry policies.** They are siblings, not parent-child. A separate `opts.OnException<DcbConcurrencyException>()` entry is required.

```csharp
opts.OnException<ConcurrencyException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
opts.OnException<DcbConcurrencyException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
```

**4. Tag events explicitly in every seeding/production path outside the DCB handler.** Canonical `[BoundaryModel]` handlers get tagging for free via the attribute-driven codegen, but `[WriteAggregate]`, `IStartStream`, and raw `session.Events.Append(streamId, rawObject)` do NOT populate tag tables.

Use `session.Events.BuildEvent(evt)` to get an `IEvent` wrapper, then either `AddTag(tag)` (void, mutates) or `WithTag(tag)` (fluent, chains):

```csharp
var wrapped = session.Events.BuildEvent(evt);
wrapped.WithTag(new ListingStreamId(listingId));     // or AddTag — same effect
session.Events.Append(listingId, wrapped);
```

For multi-tag events, pass multiple tags to the variadic `WithTag(params object[])`:

```csharp
wrapped.WithTag(new ListingStreamId(listingId), new BidderStreamId(bidderId));
```

Both `AddTag` and `WithTag` live on `IEvent` in `JasperFx.Events` — they work identically on Marten and Polecat.

**5. Define the boundary state class** — plain C# class, per-event `Apply(T)` methods (or a single `Evolve(IEvent)` switch). Private-setter properties are fine. Do not add `public Guid Id { get; set; }` unless test-harness teardown actually fails without it — the canonical Wolverine state classes omit it.

**6. Write the DCB handler using the canonical `[BoundaryModel]` pattern:**
- `public static EventTagQuery Load(TCommand command)` — static, returns the tag query
- `public static TEvent? Handle(TCommand command, [BoundaryModel] TState state)` — static, returns the event(s) to append; nullable return acceptable for no-op cases
- *Optional:* `public static HandlerContinuation Validate(TCommand command, TState state, ILogger logger)` or `Before(...)` — pre-handler pipeline hook. State is a plain parameter — **no `[BoundaryModel]` attribute** (it would trigger codegen error CS0128)

Only reach for the manual `FetchForWritingByTags` pattern if the canonical pattern genuinely doesn't fit — most commands land cleanly in the canonical shape.

---

## Gotchas and Non-Obvious Behavior

**`StartStream` drops tags.** When passing a pre-tagged `IEvent` to `StartStream`, the store re-wraps the object and drops the tags. Use `Append` instead — it correctly preserves pre-wrapped `IEvent` objects. Streams are created implicitly on first append.

**`AndEventsOfType` is required, not optional.** In the fluent form, calling `.For(tagValue)` or `.Or(tagValue)` alone creates no query condition. Each tag arm must be followed by `.AndEventsOfType<T1, T2, ...>()`. Without it, `FetchForWritingByTags` throws `ArgumentException` at runtime. (The imperative form — `new EventTagQuery().Or<TEvent, TTag>(v)` — doesn't have this trap because event type and tag are specified together.)

**`[BoundaryModel]` on `Handle()` only.** Adding it to a `Before()` / `Validate()` method's state parameter causes Wolverine codegen error CS0128 (duplicate local variable in generated code). Pipeline hooks receive the projected state as a plain parameter automatically — no attribute needed.

**`DcbConcurrencyException` vs `ConcurrencyException` are separate types, with different namespaces per store.**

| | Marten | Polecat |
|---|---|---|
| `DcbConcurrencyException` | `Marten.Exceptions.MartenException` subclass | `Polecat.Events.Dcb.DcbConcurrencyException` |
| `ConcurrencyException` | `JasperFx.ConcurrencyException` | `JasperFx.ConcurrencyException` (same) |

Catching one does not catch the other. Both need explicit retry policies regardless of store.

**`AddTag` and `WithTag` are equivalent — both work on Marten and Polecat.** The earlier "Marten uses AddTag, Polecat uses WithTag" framing was incorrect. Both methods are defined on `IEvent` in `JasperFx.Events` (the shared core library). `WithTag` is a fluent extension that wraps `AddTag` and returns `IEvent` for chaining; `WithTag(params object[])` is the variadic form for events that carry multiple tags. Pick whichever reads cleaner.

**Tag tables are strictly opt-in at write time outside the DCB handler.** Canonical `[BoundaryModel]` handlers get tagging via codegen. Every other code path appending to a DCB-managed stream must use `BuildEvent()` + `AddTag()`/`WithTag()` + `Append()` — test seeding, migration scripts, and any non-DCB handler that writes a DCB-relevant event.

**Boundary models and `Guid Id` under test-harness teardown.** Not a DCB requirement — the canonical Wolverine state classes omit this property. But some CritterSupply test fixtures have observed `InvalidDocumentException` at teardown when tag-registered document types are cleaned up without an `Id`. If your test fixture throws at teardown, adding `public Guid Id { get; set; }` to the state class is the known workaround. Apply only when the failure is observed; do not add speculatively.

**One Wolverine handler per command type.** If you keep both a manual and a `[BoundaryModel]` handler for the same command type in the same assembly (e.g. for comparison or migration), mark the one you don't want auto-discovered with `[WolverineIgnore]` — otherwise discovery is ambiguous and codegen fails.

---

## CritterBids Usage

### Canonical Example — Auctions BC: Bid Placement

Bid placement is CritterBids' primary DCB scenario. A valid bid must simultaneously enforce:

- The listing is open for bidding (Listing stream)
- The bid amount exceeds the current high bid (Listing stream)
- The bidder is not the seller (Listing stream + ParticipantSession stream)
- The bid would not exceed the bidder's hidden credit ceiling (ParticipantSession stream)

These invariants span two streams. A saga would be overkill — this is a single atomic decision. DCB is the right tool.

The `BidConsistencyState` boundary model (shown above) projects the relevant facts from both streams. The `PlaceBidHandler` uses the canonical `[BoundaryModel]` pattern: `Load(command) => EventTagQuery` selects the accepted-bid event family; `Handle(command, [BoundaryModel] BidConsistencyState)` makes the decision and returns `BidPlaced` (or `BidRejected` via an internal audit path). The handler appends atomically with cross-stream optimistic concurrency.

### Decision Boundary Guardrails

- **Auctions BC** — bid placement is the canonical DCB use case
- **Selling BC** — single-stream listing lifecycle; no DCB needed
- **Settlement BC** — sequential saga steps; not a single-decision boundary
- Any scenario requiring cross-BC coordination, external calls, or delayed completion → saga, not DCB

### `BidRejected` Stream Placement (Auctions BC)

Decision recorded in M3-S1 (W002-7 resolution). Applied uniformly from M3-S4 `PlaceBid` /
`BuyNow` authoring onward.

**Rule:** `BidRejected` events go to a **dedicated Marten stream type per listing, tagged with
`ListingId`** — never to the listing's primary bidding stream, and not to a single global audit
stream.

**Shape:**

- One stream type (e.g., `BidRejectionAudit` — exact name finalized at S4 authoring time),
  one stream per listing, tagged with the existing `ListingStreamId` tag value.
- The DCB `EventTagQuery` for `PlaceBid` narrows its `AndEventsOfType<...>()` set to the
  accepted-bid event family only: `BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`,
  `BiddingClosed`, `ExtendedBiddingTriggered`. `BidRejected` is excluded by type, not by stream.
- The `BidConsistencyState` boundary model has no `Apply(BidRejected)` method — rejected bids
  are invisible to the acceptance-decision model by construction.

**Why dedicated per-listing streams, not the listing's primary stream:**

- The primary stream feeds the DCB boundary model. Mixing rejected events in would either
  corrupt `CurrentHighBid` / `BidCount` state or force every `Apply()` method to filter on
  acceptance. Either option is fragile under schema evolution and would surface as a silent
  bug if a new `Apply()` overload is added without the filter.

**Why dedicated per-listing streams, not a global audit stream:**

- Per-listing audit queries (ops tooling, support investigations into a disputed rejection)
  start from a listing ID, not a time range. Per-listing tagging matches the access pattern
  without full-table scans.
- Global audit streams are the correct pattern for "diagnostic firehose" cases. `BidRejected`
  is not diagnostic — it is a first-class audit trail scoped to a listing's lifecycle.
- Marten's `RegisterTagType<ListingStreamId>("listing").ForAggregate<Listing>()` pattern
  already gives the per-listing tag for free; extending it to a second stream type is a
  one-line registration, not new infrastructure.

**Why excluded from the DCB tag query by type-filter rather than by a separate stream-filter
predicate:**

- The `EventTagQuery` API takes `AndEventsOfType<T1, T2, ...>()`. Narrowing that type list is
  the idiomatic exclusion path. Additional stream-filter predicates are neither needed nor
  supported by the current API.
- Single source of truth: the list of accepted-bid event types that contribute to
  `BidConsistencyState` lives in exactly one place (the `EventTagQuery.For(...)` call in
  `PlaceBidHandler.Load`).

---

## References

- [dcb.events](https://dcb.events/) — pattern specification
- [Wolverine Docs: DCB with Marten](https://wolverinefx.io/guide/durability/marten/event-sourcing.html#dynamic-consistency-boundary-dcb)
- [Wolverine Docs: DCB with Polecat](https://wolverinefx.net/guide/durability/polecat/event-sourcing)
- [Sara Pellegrini — "Killing the Aggregate"](https://sara.event-thinking.io/2023/04/kill-aggregate-chapter-1-I-will-tell-you-a-story.html)
- Canonical code reference in the Wolverine repo: `src/Persistence/MartenTests/Dcb/University/` — `BoundaryModelSubscribeStudentToCourse.cs` is the `#region sample_wolverine_dcb_boundary_model_handler` target; `ChangeCourseCapacity.cs` shows three parallel handler shapes for one command (manual, `[BoundaryModel]`, `[WriteAggregate]`); `boundary_model_workflow_tests.cs` is the fixture-setup and test-shape reference.
