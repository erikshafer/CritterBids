# M3-S4: DCB PlaceBid Handler — Retrospective

**Date:** 2026-04-18
**Milestone:** M3 — Auctions BC
**Slice:** S4 of 8 (paired with S4b for BuyNow; this session covers PlaceBid only)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/M3-S4-dcb-place-bid.md`

---

## Baseline

- 47 tests passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 4 Auctions + 31 Selling)
- `dotnet build` — 0 errors, 0 warnings
- `src/CritterBids.Auctions/Listing.cs` carries `ScaffoldPlaceholder` + `Apply(ScaffoldPlaceholder)` no-op; no real Apply methods
- `dynamic-consistency-boundary.md` corrected pre-flight (2026-04-17) against the canonical Wolverine University example — treated as authoritative for this session
- `AuctionsTestFixture` is Listings-minimal and direct-invocation-only from S3

## Session outcome

- 63 tests passing (+15 scenarios in `PlaceBidHandlerTests`, +1 dispatch in `PlaceBidDispatchTests`)
- `dotnet build` — 0 errors, 0 warnings
- First production DCB handler in CritterBids is live; a timed listing now takes real bids under real consistency rules
- `ScaffoldPlaceholder` retired; real `Apply(BiddingOpened)` populates all 15 `Listing` properties
- `BidConsistencyState` is registered as both a DCB boundary aggregate AND a Marten 8 document (the Id requirement was empirical)
- The canonical `[BoundaryModel]` auto-append shape did NOT fit — tag-inference incompatibility forced a manual-tag, manual-append implementation; concurrency guarantee preserved via `FetchForWritingByTags` queuing `AssertDcbConsistency` at fetch time

---

## Items completed

| Item | Description | Commit |
|------|-------------|--------|
| 1 | Real `Apply(BiddingOpened)` on `Listing` — populates all 15 properties from event payload | `b953cda` |
| 2 | `ScaffoldPlaceholder` + `Apply(ScaffoldPlaceholder)` removed | `b953cda` |
| 3 | `AuctionsModule` event registrations (6 total) + tag type + both concurrency retry policies | `267d512`, `9068f1b` |
| 4 | `BidRejected` internal event + `BidRejectionAudit` stream type with XOR-derived per-listing keys | `267d512`, `9068f1b` |
| 5 | `BidConsistencyState` DCB boundary model with `Apply(T)` methods; `public Guid Id { get; set; }` added empirically | `ff09fc9`, `9068f1b` |
| 6 | `PlaceBidHandler` covering all 15 §1 scenarios | `9068f1b` |
| 7 | `PlaceBidHandlerTests.cs` — 15 integration tests, method names exactly per milestone doc §7 | `9068f1b` |
| 8 | `PlaceBidDispatchTests.cs` — 1 dispatch test through `IMessageBus` | `9068f1b` |
| 9 | `dynamic-consistency-boundary.md` — "CritterBids M3-S4 Learnings" section appended | `ab2cc7d` |
| 10 | This retrospective | (this commit) |

---

## Item 1 + 2 — `Listing.Apply(BiddingOpened)` lands; `ScaffoldPlaceholder` retires

Chose `Apply(BiddingOpened)` over `Create(BiddingOpened)` — answers **Open Question 1**. Rationale: the skill guidance in `marten-event-sourcing.md` treats `Apply` as the default shape; `Create` is reserved for cases where the aggregate has non-default construction cost (expensive constructors, validation that must fail on first event). `Listing` is a plain property bag — `Apply` reads naturally as "mutate state from this event."

`OriginalCloseAt` is seeded from `ScheduledCloseAt` on `BiddingOpened`. This matters for scenario 1.15 where the extension math has to compare `candidate > OriginalCloseAt + MaxDuration` — if seeding used a separate `OriginalCloseAt`, the test would be simulating an already-extended listing by appending `ExtendedBiddingTriggered` while keeping `OriginalCloseAt` unchanged.

`ScaffoldPlaceholder` removal shipped in the same commit as item 1 — the prompt's explicit rule ("Never land item 1 without item 2") held.

---

## Item 3 — `AuctionsModule` registrations

| Registration | Count | Notes |
|--------------|-------|-------|
| `AddEventType<T>()` | 6 | `BiddingOpened`, `BidPlaced`, `BidRejected`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved` |
| `RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>()` | 1 | Two side-effects: registers the tag type AND registers `BidConsistencyState` as a document |
| `OnException<ConcurrencyException>().RetryWithCooldown(...)` | 1 | JasperFx exception |
| `OnException<DcbConcurrencyException>().RetryWithCooldown(...)` | 1 | `Marten.Events.Dcb` exception — sibling, not child of above |

`BuyItNowPurchased` is absent as scoped; it lands with M3-S4b.

---

## Item 4 — `BidRejected` stream placement

`BidRejected` is an **internal** event type (`CritterBids.Auctions.BidRejected`), not in `CritterBids.Contracts.Auctions.*`. Stays per W002-7.

`BidRejectionAudit` is the stream-type marker, one stream per listing. Stream key is derived deterministically from the listing id via XOR against a fixed namespace Guid:

```csharp
private static readonly Guid Namespace = new("b1d4a123-0000-0000-0000-000000000001");
public static Guid StreamKey(Guid listingId)
{
    Span<byte> listing = stackalloc byte[16];
    Span<byte> ns = stackalloc byte[16];
    listingId.TryWriteBytes(listing);
    Namespace.TryWriteBytes(ns);
    for (var i = 0; i < 16; i++) listing[i] ^= ns[i];
    return new Guid(listing);
}
```

A SHA1-based UUID v5 would have been overkill for a single-domain, fixed-prefix derivation; the XOR shape is sufficient to guarantee the audit stream's Guid never collides with the listing's primary stream Guid.

`BidRejectionAudit` carries `public Guid Id { get; set; }` because `UseMandatoryStreamTypeDeclaration = true` forces every new stream to declare its type at `StartStream<T>`.

---

## Item 5 — `BidConsistencyState` and the Guid Id requirement

**Open Question 2 answer: `public Guid Id { get; set; }` is required.**

First attempt followed the canonical Wolverine University state classes (`SubscriptionState`, `CourseState`) which omit the Id property. That failed at fixture teardown with:

```
Marten.Exceptions.InvalidDocumentException : Invalid document type BidConsistencyState.
Could not determine an Id/id for this document type.
```

Root cause: `opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>()` under Marten 8 registers the aggregate type as a **document**. Documents require an identity property. The University example presumably ran under an earlier Marten revision where this chaining was either opt-in or documented differently.

Fix: added `public Guid Id { get; set; }`. The value is set by Marten — the handler does not populate it. No other behavior changed.

---

## Item 6 — `PlaceBidHandler` — three handler shapes attempted, final is manual-tag + manual-append

### Why the `[BoundaryModel]` auto-append shape did NOT fit

Canonical pattern in the Wolverine University example:

```csharp
public static Events Handle(PlaceBid command, [BoundaryModel] BidConsistencyState state)
{
    // return Events ... auto-appended via IEventBoundary.AppendMany
}
```

That path infers tags by scanning the returned events for a property whose **type** exactly matches the registered tag type. CritterBids' contract events expose `Guid ListingId`, not `ListingStreamId ListingTag`. Dispatch failed at runtime with:

```
Cannot route event of type CritterBids.Contracts.Auctions.BidPlaced via IEventBoundary
```

Alternatives considered:

1. **Register `Guid` itself as the tag type** — failed at boot:
   ```
   JasperFx.Core.Reflection.InvalidValueTypeException : System.Guid is an invalid tag value type.
   ```
   .NET 10 added `Variant` and `Version` public properties on `Guid`. `ValueTypeInfo.ForType` requires exactly one gettable public instance property.

2. **Add a `ListingStreamId ListingTag` property to the contract events** — rejected on principle. Contracts are consumed by other BCs (Listings, Settlement, Operations) and should not leak Marten wrapper types.

3. **Manual-tag, manual-append** — chosen. Handler calls `FetchForWritingByTags<T>` directly, then for each accepted event does `BuildEvent` + `AddTag(new ListingStreamId(...))` + `Append(listingId, wrapped)`.

The DCB optimistic-concurrency guarantee survives the third approach because `FetchForWritingByTags` queues an `AssertDcbConsistency` operation on the session at fetch time — it fires at `SaveChanges` regardless of whether the write went through `IEventBoundary.AppendMany` or `IDocumentSession.Events.Append`.

### Handler structure after

```csharp
public static async Task HandleAsync(PlaceBid command, IDocumentSession session, TimeProvider time)
{
    var query = BuildQuery(command.ListingId);
    var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
    var state = boundary.Aggregate ?? new BidConsistencyState();
    var now = time.GetUtcNow();
    var reason = EvaluateRejection(command, state, now);

    if (reason is not null)
    {
        await AppendRejectionAudit(session, command, state, reason, now);
        return;
    }

    foreach (var evt in AcceptanceEvents(command, state, now))
    {
        var wrapped = session.Events.BuildEvent(evt);
        wrapped.AddTag(new ListingStreamId(command.ListingId));
        session.Events.Append(command.ListingId, wrapped);
    }
}
```

### Handler shape metrics

| Metric | Canonical (University) | CritterBids (actual) |
|--------|----------------------|----------------------|
| Handler signature | `(Cmd, [BoundaryModel] State) => Events` | `(Cmd, IDocumentSession, TimeProvider) => Task` |
| Tag inference | Automatic (via event property type) | Manual (`AddTag(new ListingStreamId(...))`) |
| Append path | `IEventBoundary.AppendMany` | `IDocumentSession.Events.Append(streamKey, wrapped)` |
| Consistency check | Queued via `FetchForWritingByTags`, fires at `SaveChanges` | Same — survives because the assertion is queued at fetch time, not at append time |
| `[BoundaryModel]` parameter | Yes | No (removed) |

### ValidateAsync + [BoundaryModel] do not compose

An earlier intermediate shape used a sibling `ValidateAsync` returning `Task<HandlerContinuation>` to handle rejection paths. Six rejection tests landed with empty audit streams — the handler never ran because Wolverine's compound-handler discovery did not route the validation sibling when paired with a `[BoundaryModel]` parameter on `HandleAsync`.

Folding validation into `HandleAsync` (early return on rejection, route to `AppendRejectionAudit`) was the fix. Additionally: two static methods whose names start with `Handle` on the same handler class break discovery with `NoHandlerForEndpointException` — when a pure-function sibling is useful for unit testing, name it something other than `Handle*` (we used `Decide`).

### Sibling `Decide` method

`PlaceBidHandler.Decide(command, state, time) => Events` is a pure function exposed for scenarios that don't need the bus. Acceptance-path tests (1.1, 1.2, 1.9, 1.10, 1.11–1.15) use `Decide`; rejection-path tests (1.3–1.8) go through the bus to assert `BidRejected` landed in the audit stream.

---

## Item 7 — 15 scenario tests, method names match milestone doc §7

| Scenario | Method | Path |
|----------|--------|------|
| 1.1 | `FirstBid_ProducesBidPlaced_AndBuyItNowOptionRemoved` | `Decide` |
| 1.2 | `Outbid_ProducesBidPlaced_NoBuyItNowOptionRemoved` | `Decide` |
| 1.3 | `BelowStartingBid_ProducesBidRejected` | Bus |
| 1.4 | `BelowIncrement_ProducesBidRejected` | Bus |
| 1.5 | `ExceedsCreditCeiling_ProducesBidRejected` | Bus |
| 1.6 | `NoBiddingOpened_ProducesBidRejected` | Bus |
| 1.7 | `ListingClosed_ProducesBidRejected` | Bus |
| 1.8 | `SellerCannotBidOnOwnListing_ProducesBidRejected` | Bus |
| 1.9 | `ReserveCrossed_ProducesReserveMet` | `Decide` |
| 1.10 | `ReserveAlreadyMet_NoDuplicateSignal` | `Decide` |
| 1.11 | `BidInTriggerWindow_ProducesExtendedBiddingTriggered` | `Decide` |
| 1.12 | `BidOutsideTriggerWindow_NoExtendedBiddingTriggered` | `Decide` |
| 1.13 | `ExtendedBiddingDisabled_NoExtension` | `Decide` |
| 1.14 | `ExtensionWithinMaxDuration_Fires` | `Decide` |
| 1.15 | `ExtensionExceedsMaxDuration_Blocked` | `Decide` |

Rejection precedence in `EvaluateRejection`: `ListingNotOpen` → `ListingClosed` → `SellerCannotBid` → `ExceedsCreditCeiling` → `BelowMinimumBid`. Matches the workshop ordering.

Extended-bidding math edge cases (answers **Open Question 4**):

- Trigger window check is strictly `remaining > window` to reject (i.e., `remaining <= window` fires extension) — matches "arrives within the trigger window."
- `MaxDuration` check is strictly `candidate > maxClose` to reject — so a candidate exactly equal to `OriginalCloseAt + MaxDuration` fires. No sub-second skew handling is layered in; if the scheduled close is already in the past, the rejection chain catches it via `ListingClosed` before extension math runs.

---

## Item 8 — Dispatch test passes without fixture changes

**Open Question 5 answer: `AuctionsTestFixture` needed no modifications.** The sticky-handler `NoHandlerForEndpointException` pattern that bit S3 applied to messages routed to a RabbitMQ queue. `PlaceBid` is dispatched via `IMessageBus.InvokeMessageAndWaitAsync` but **not** routed to a queue (it's internal to Auctions, not cross-BC) — so the sticky-handler failure mode never triggered.

The dispatch test seeds a `BiddingOpened`, dispatches `PlaceBid` via the bus, and asserts through `FetchForWritingByTags<BidConsistencyState>` that `CurrentHighBid` / `BidCount` / `BuyItNowAvailable` reflect the bid. This covers the "handler registered and routable via IMessageBus" half of the M3-S4 exit criterion the 15 scenario tests don't.

---

## Item 9 — DCB skill doc append

Sections added to `docs/skills/dynamic-consistency-boundary.md` under "CritterBids M3-S4 Learnings":

1. `EventTagQuery` shape (fluent `.For().AndEventsOfType<>()`)
2. Why the `[BoundaryModel]` auto-append shape did NOT fit
3. Why `ListingStreamId` wraps `Guid` (.NET 10 Variant/Version)
4. `BidConsistencyState` needs `public Guid Id` (Open Question 2 empirical answer)
5. `ValidateAsync + [BoundaryModel]` non-composition
6. `[BoundaryModel]` state parameter is nullable
7. `UseMandatoryStreamTypeDeclaration` seeding workflow
8. Live aggregation + DCB-appended events (Open Question 3 answer)
9. Concurrency policies: both exception types

No edits to existing skill-file sections — append-only per prompt.

---

## Open Questions — summary

| # | Question | Answer |
|---|----------|--------|
| 1 | `Apply(BiddingOpened)` vs `Create(BiddingOpened)` | `Apply` — the aggregate is a plain property bag, skill guidance treats `Apply` as the default |
| 2 | Does `BidConsistencyState` need `public Guid Id`? | Yes — `RegisterTagType.ForAggregate<T>` registers the aggregate as a Marten 8 document; omitting Id throws `InvalidDocumentException` at fixture teardown |
| 3 | DCB writes vs `Listing` live-stream projection | Works for the manual-append path — events are both appended to the primary stream AND tagged, so `AggregateStreamAsync<Listing>` picks them up with no special handling. The auto-append (`IEventBoundary.AppendMany`) path was not exercised in S4; S5 should re-verify if it switches |
| 4 | Extended-bidding math edges | Trigger window uses `remaining <= window`, max-duration uses `candidate > maxClose`; both scenarios 1.14 and 1.15 pass with these boundaries |
| 5 | `AuctionsTestFixture` changes | None needed — sticky-handler failure only applies to queue-routed messages; `PlaceBid` is bus-internal |

---

## Test results

| Phase | Auctions tests | Total | Result |
|-------|---------------:|------:|:------|
| Baseline (M3-S3 close) | 4 | 47 | All green |
| After `Apply(BiddingOpened)` (`b953cda`) | 4 | 47 | All green |
| After registrations (`267d512`) | 4 | 47 | All green |
| After `BidConsistencyState` (`ff09fc9`) | 4 | 47 | All green |
| After `PlaceBidHandler` + 16 new tests (`9068f1b`) | 19 | 63 | **All green** |
| After skill append (`ab2cc7d`) | 19 | 63 | All green (docs-only) |

---

## Build state at session close

- `dotnet build` — 0 errors, 0 warnings
- `dotnet test` — 63 passed, 0 failed, 0 skipped
- `CritterBids.Auctions.csproj` `ProjectReference` count: 1 (Contracts only)
- `src/CritterBids.Auctions/` `IMessageBus` references: 0
- `src/CritterBids.Auctions/` `BuyNowHandler.cs` files: 0
- `src/CritterBids.Auctions/` `BuyNow` command references: 0
- `src/CritterBids.Api/Program.cs` — unchanged from S3 close (verified by diff)

---

## What M3-S4b should know

- **`BuyNowHandler` does NOT mirror `PlaceBidHandler`'s signature.** The prompt for S4 assumed `[BoundaryModel]` would be the precedent; it wasn't. S4b's `BuyNowHandler` must choose: follow the same manual-tag + manual-append path (proven here) OR revisit the auto-append shape if S4b's contract event carries `ListingStreamId` directly (it doesn't — `BuyItNowPurchased` also carries `Guid ListingId`). Default recommendation: same manual shape as `PlaceBidHandler`.
- **`BidConsistencyState` already carries `BuyItNowPrice` and `BuyItNowAvailable`** populated from `Apply(BiddingOpened)`. S4b reads both for its acceptance decision. No new fields needed on the state model for S4b.
- **`BidConsistencyState` has `public Guid Id { get; set; }`** — do not remove it when extending. Marten 8 requires it.
- **Tag query shape** is `EventTagQuery.For(new ListingStreamId(id)).AndEventsOfType<...>()`. For S4b, the query should add `BuyItNowPurchased` to the type list so a second BuyNow attempt on a sold listing is rejected against the same state.
- **Audit stream**: `BidRejectionAudit.StreamKey(listingId)` is the XOR-derived per-listing key. If S4b introduces a `BuyNowRejectionAudit`, use a different namespace Guid to avoid stream-ID collision.
- **`AuctionsTestFixture` is safe to reuse as-is.** Bus dispatch works for non-queue-routed internal commands.

## What M3-S5 should know

- **The Auction Closing saga consumes `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`** produced here. All three are appended to the listing's primary stream via `session.Events.Append(listingId, wrapped)` — visible to `AggregateStreamAsync<Listing>` and to any projection subscribed to the stream.
- **`BidRejected` is isolated on a dedicated audit stream.** The saga should NOT observe it by listing id — if saga logic needs to react to rejections, it queries `BidRejectionAudit.StreamKey(listingId)` explicitly.
- **`ExtendedBiddingTriggered.NewCloseAt` is the saga's reschedule signal.** The scheduled-close timestamp moves forward on every extension, and the saga's `CloseAuction` message must be cancelled-and-rescheduled in response.
- **`OriginalCloseAt` is preserved in the `Listing` aggregate** across extensions — the saga uses `OriginalCloseAt + MaxDuration` as the hard cap, not the current `ScheduledCloseAt + MaxDuration`.
- **Live aggregation over DCB-tagged events works** for the manual-append path S4 used. If S5 switches any DCB write to the auto-append (`IEventBoundary.AppendMany`) path, re-verify that the live `Listing` aggregate still sees those events.
- **Both `ConcurrencyException` and `DcbConcurrencyException` retry policies** are registered in `AuctionsModule` — saga-side retry configuration can rely on them, but if the saga introduces additional write paths targeting non-DCB aggregates, the `ConcurrencyException` policy is what governs them.
- **`IsProxy: false` hardcoded** on every `BidPlaced`. M4's proxy handler flips this with zero contract change — S5 saga logic should not branch on `IsProxy` if it can avoid doing so.
