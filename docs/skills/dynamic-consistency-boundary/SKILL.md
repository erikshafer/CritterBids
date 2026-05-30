---
name: dynamic-consistency-boundary
description: "Dynamic Consistency Boundary in CritterBids: BoundaryModel vs manual tag writes, EventTagQuery gotchas, retry policy, and Auctions bid decisions. Use for cross-stream invariants."
cluster: marten
tags: [marten, dcb, event-sourcing, concurrency, auctions]
---

# Dynamic Consistency Boundary (DCB)

> CritterBids DCB conventions and hard-won gotchas for cross-stream consistency.
> Generic DCB mechanics live in ai-skills `marten-advanced-dynamic-consistency-boundary` and `polecat-cross-stream-operations`; **this skill documents only the CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- A command must enforce an invariant spanning multiple streams selected by tags at handling time.
- Auctions bid placement needs to reason over listing state plus bidder/session facts.
- You need the CritterBids decisions around `[BoundaryModel]`, manual `FetchForWritingByTags`, tag registration, and `BidRejected` stream placement.
- A test fails around DCB tag seeding, `DcbConcurrencyException`, codegen CS0128, or boundary model identity.

Do NOT use this skill for: fixed known-stream updates with multiple `[WriteAggregate]` parameters (see `marten-event-sourcing` and upstream cross-stream operations), long-running workflows (see `wolverine-sagas`), or generic DCB intro material (read upstream first).

## Read upstream first

Generic DCB/cross-stream mechanics are covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of the topic:

1. `marten-advanced-dynamic-consistency-boundary` — DCB model, tag queries, boundary writes.
2. `polecat-cross-stream-operations` — sibling-store cross-stream reference and DCB contrast.

Those cover ~80% of the topic. This skill picks up at CritterBids' verified divergences and production findings.

## CritterBids DCB posture

CritterBids is all-Marten/PostgreSQL today (ADR 011). DCB itself is store-agnostic in JasperFx: `[BoundaryModel]`, `EventTagQuery`, and `IEventBoundary<T>` exist for both Marten and Polecat; claims that DCB is Polecat-only are stale. Keep Polecat knowledge as reference only unless the project pivots again.

| Scenario | Tool |
|---|---|
| Single stream owns the invariant | Normal event-sourced aggregate handler. |
| Command carries two or more fixed stream IDs | Cross-stream handler with multiple `[WriteAggregate]` parameters. |
| Streams are selected dynamically by tag query inside one BC | DCB. |
| Cross-BC coordination, external calls, delayed completion | Saga/process manager, not DCB. |

The key split is fixed IDs vs dynamic selection. If the command already names every stream to load, DCB is usually unnecessary.

## Two patterns, one preferred

### Canonical: `[BoundaryModel]`

Prefer this shape when tag inference and returned-event append fit:

```csharp
public static EventTagQuery Load(PlaceBid command) =>
    EventTagQuery
        .For(new ListingStreamId(command.ListingId))
        .AndEventsOfType<BiddingOpened, BidPlaced, BuyItNowOptionRemoved, BiddingClosed, ExtendedBiddingTriggered>()
        .Or(new BidderStreamId(command.BidderId))
        .AndEventsOfType<ParticipantSessionStarted>();

public static BidPlaced? Handle(
    PlaceBid command,
    [BoundaryModel] BidConsistencyState state)
{
    if (!state.IsOpen) return null;
    if (state.SellerBiddingOnOwnListing(command.BidderId)) return null;
    if (command.Amount <= state.CurrentHighBid) return null;

    return new BidPlaced(command.ListingId, command.BidId, command.BidderId, command.Amount);
}
```

Rules:

- `[BoundaryModel]` goes on the state parameter of `Handle` only.
- `Load` is static and returns `EventTagQuery`.
- `Handle` returns event(s) to append; nullable event is a valid no-op.
- The boundary state is a plain class with `Apply(T)` methods or one `Evolve(IEvent)` switch.

### Manual: `FetchForWritingByTags`

Use this escape hatch when you need to tag events manually, inspect `boundary.Events`, perform custom idempotency, or avoid leaking tag wrapper types into contracts:

```csharp
public static async Task Handle(PlaceBid command, IDocumentSession session)
{
    var query = BuildQuery(command.ListingId);
    var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
    var state = boundary.Aggregate ?? new BidConsistencyState();

    var accepted = Decide(command, state);
    if (accepted is null) return;

    var wrapped = session.Events.BuildEvent(accepted);
    wrapped.AddTag(new ListingStreamId(command.ListingId));
    session.Events.Append(command.ListingId, wrapped);
}
```

CritterBids' first production `PlaceBid` implementation used the manual append path because contract events expose `Guid ListingId`, not `ListingStreamId`, and the tag wrapper must not leak into `CritterBids.Contracts`.

## EventTagQuery rules and gotchas

Use fluent queries for readability:

```csharp
EventTagQuery
    .For(new ListingStreamId(listingId))
    .AndEventsOfType<BiddingOpened, BidPlaced, BuyItNowOptionRemoved, ReserveMet, ExtendedBiddingTriggered>();
```

Or imperative conditions when every event/tag pair should be explicit:

```csharp
new EventTagQuery()
    .Or<BiddingOpened, ListingStreamId>(new ListingStreamId(listingId))
    .Or<BidPlaced, ListingStreamId>(new ListingStreamId(listingId));
```

CritterBids gotchas:

- `.AndEventsOfType<...>()` is required after each fluent `.For(...)` / `.Or(...)`; a tag arm alone creates no condition and fails at runtime.
- Exclude events by narrowing the `AndEventsOfType` list. Do not invent stream-filter predicates.
- Expose query builders as `public static BuildQuery(...)` when tests need to assert accepted/rejected event families directly.

## Six-step implementation checklist

1. **Define strong-typed tag IDs.** Do not register raw `Guid`: .NET 10 added public `Variant` and `Version` properties, breaking JasperFx's one-public-property value-type validation.

   ```csharp
   public sealed record ListingStreamId(Guid Value);
   public sealed record BidderStreamId(Guid Value);
   ```

2. **Register tag types in the BC's Marten config.**

   ```csharp
   opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>();
   opts.Events.RegisterTagType<BidderStreamId>("bidder").ForAggregate<BidConsistencyState>();
   ```

3. **Register retry policies for both exception families.** `ConcurrencyException` and `DcbConcurrencyException` are siblings.

   ```csharp
   opts.OnException<ConcurrencyException>()
       .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
   opts.OnException<DcbConcurrencyException>()
       .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
   ```

   In Marten, `DcbConcurrencyException` is from Marten's DCB namespace; Polecat has a same-named type in its own namespace. If code is store-agnostic, be explicit about both.

4. **Tag every non-DCB write path explicitly.** `[BoundaryModel]` codegen can tag returned events. Test seeding, migrations, `[WriteAggregate]`, `IStartStream`, and raw append paths do not.

   ```csharp
   var wrapped = session.Events.BuildEvent(evt);
   wrapped.WithTag(new ListingStreamId(listingId), new BidderStreamId(bidderId));
   session.Events.Append(listingId, wrapped);
   ```

5. **Define the boundary state.** Plain class; `Apply(T)` or `Evolve(IEvent)`. Under CritterBids' Marten 8 test fixtures, add `public Guid Id { get; set; }` to avoid `CleanAllMartenDataAsync` teardown failures when the state is registered as a document.

6. **Write the handler.** Prefer `[BoundaryModel]`; use manual `FetchForWritingByTags` only when the canonical shape cannot preserve CritterBids contract boundaries.

## Tagging gotchas

- **`StartStream` drops pre-applied tags.** It re-wraps the raw object. For seeded tagged streams, either add tags through pending changes after `StartStream<T>` or use `BuildEvent` + `Append` for subsequent appends.
- **`AddTag` and `WithTag` are equivalent.** Both live on `IEvent` in `JasperFx.Events` and work across Marten/Polecat. `WithTag` is the fluent wrapper; use whichever reads better.
- **Tag tables are opt-in at write time.** If an event should participate in a DCB query, the append path must populate tags.
- **Mandatory stream type declaration changes seeding.** With `UseMandatoryStreamTypeDeclaration = true`, declare the stream type first, then tag the pending event or use typed append shapes that preserve the declaration.

## `[BoundaryModel]` pipeline gotchas

- Do not put `[BoundaryModel]` on `Before()` or `Validate()` state parameters. Wolverine codegen emits duplicate locals and fails with CS0128. Pipeline hooks receive state as a plain parameter.
- `ValidateAsync` + `[BoundaryModel]` did not compose cleanly in the first CritterBids handler; the handler failed discovery. Folding validation into `HandleAsync` was safer.
- Do not have two static methods whose names start with `Handle` on the same handler class. Helper methods for pure tests should be named `Decide`, `BuildDecision`, etc.
- `[BoundaryModel]` state can be null when the query matches no events. Use `state ??= new BidConsistencyState()` or handle the missing boundary explicitly.

## CritterBids Auctions decision — bid placement

Bid placement is the canonical DCB use case. A valid bid must simultaneously enforce:

- listing is open;
- amount exceeds current high bid;
- bidder is not the seller;
- bidder/session credit ceiling is not exceeded.

These are one immediate decision over multiple tagged event families, not a saga.

### `BidRejected` stream placement

Rule from M3-S1/M3-S4: `BidRejected` events go to a **dedicated Marten stream type per listing, tagged with `ListingStreamId`**. They do not go to the listing's primary bidding stream and not to one global audit stream.

Why:

- The primary stream feeds the acceptance boundary model. Mixing rejected bids into it would corrupt bid count/high-bid state or force fragile filters into every `Apply` method.
- Support/ops investigations start from `ListingId`; per-listing streams and tags match that access pattern.
- `BidRejected` is excluded from the DCB query by type list: the accepted-bid family is the single source of truth for `BidConsistencyState`.

## Common pitfalls

- **Treating DCB as a saga replacement.** DCB is for immediate consistency decisions, not long-running orchestration.
- **Registering `Guid` as a tag type.** Use strong-typed records (`ListingStreamId(Guid Value)`) to avoid .NET 10 `Guid` property validation failures.
- **Missing `AndEventsOfType`.** Fluent tag arms without event types do not query anything useful.
- **Expecting `StartStream` to preserve tags.** It does not; verify seed helpers.
- **Retrying only `ConcurrencyException`.** DCB has its own concurrency exception type.
- **Adding `BidRejected` to the acceptance model.** Keep rejected-bid audit events out of the decision boundary.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-advanced-dynamic-consistency-boundary` — DCB mechanics.
- `polecat-cross-stream-operations` — reference-only sibling-store comparison and cross-stream context.

**Prerequisites:**

- `marten-event-sourcing` — stream identity, Marten config, aggregate handler workflow.
- `wolverine-message-handlers` — handler shape, validation, retry policy registration.

**Downstream:**

- `marten-projections` — read-model projections that consume DCB-appended events.
- `critter-stack-testing-patterns` — tag seeding and fixture cleanup patterns.

**External:**

- ADR 011 (All-Marten Pivot) and ADR 007 (UUID v7 stream IDs) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions.
- JasperFx open question #2 in [[`docs/jasperfx-open-questions.md`](../../jasperfx-open-questions.md)](../../jasperfx-open-questions.md) — stale docs claiming DCB is Polecat-only.
