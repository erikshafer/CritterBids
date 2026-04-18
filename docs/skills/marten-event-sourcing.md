# Event Sourcing with Marten and Wolverine

Patterns for event-sourced systems using Marten (event store + document database) and Wolverine (command handling + message bus).

---

## Table of Contents

1. [When to Use Event Sourcing](#when-to-use-event-sourcing)
2. [Stream Identity Conventions](#stream-identity-conventions)
3. [Aggregate Design](#aggregate-design)
4. [Domain Event Structure](#domain-event-structure)
5. [Projections](#projections)
6. [ProjectLatest — Pending Event State](#projectlatest--pending-event-state)
7. [Snapshot Strategies](#snapshot-strategies)
8. [Wolverine Integration Patterns](#wolverine-integration-patterns)
9. [Cross-Stream Aggregate Handlers](#cross-stream-aggregate-handlers)
10. [Tagged Event Writes (DCB)](#tagged-event-writes-dcb)
11. [Async Daemon Configuration](#async-daemon-configuration)
12. [Testing Event-Sourced Systems](#testing-event-sourced-systems)
13. [Event Versioning](#event-versioning)
14. [Marten Configuration](#marten-configuration)
15. [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
16. [Lessons Learned](#lessons-learned)

---

## When to Use Event Sourcing

### ✅ Use Event Sourcing For:

| Use Case | CritterBids Examples |
|---|---|
| Transactional data with frequent state changes | Listings, ParticipantSessions, SettlementSagas |
| Audit trail is valuable | Bid history, settlement records, obligation lifecycle |
| Complex business logic | Auction closing saga, proxy bid manager |
| Temporal queries needed | "What was the listing state at time T?" |
| Event-driven integrations | Listing events → Relay, Settlement, Obligations |
| Replay/rebuild capability | Rebuild projections from events after schema changes |

### ❌ Use Document Store Instead For:

| Use Case |
|---|
| Master data with infrequent changes |
| Read-heavy workloads where current state is all that matters |
| Simple CRUD operations |

---

## Stream Identity Conventions

### UUID v7 vs UUID v5

| Pattern | When to Use |
|---|---|
| **UUID v7** (time-ordered, random) | Stream ID generated at entity creation — Marten BCs (Selling, Listings, Auctions, Obligations, Relay) |
| **UUID v5** (deterministic, SHA-1) | Stream ID derived from natural key — Polecat BCs where multiple handlers must coordinate on same ID without lookup |

**CritterBids convention:** Marten BCs use UUID v7. No namespace constant required — stream IDs are generated at creation time with no cross-handler coordination requirement. Polecat BCs (Participants, Settlement, Operations) use UUID v5 with BC-specific namespace constants where determinism is load-bearing.

**UUID v7 Pattern (Marten BCs):**

```csharp
[WolverinePost("/api/listings")]
public static (CreationResponse<Guid>, IStartStream) Handle(PublishListing cmd)
{
    var listingId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Listing>(listingId, new ListingPublished(listingId, ...));
    return (new CreationResponse<Guid>($"/api/listings/{listingId}", listingId), stream);
}
```

**UUID v5 Pattern (Polecat BCs)** — when multiple handlers must resolve the same stream without a lookup:

```csharp
public sealed record SomeNaturalKeyAggregate
{
    public static Guid StreamId(string naturalKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(naturalKey, nameof(naturalKey));

        var namespaceBytes = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
        var nameBytes = Encoding.UTF8.GetBytes($"bc-prefix:{naturalKey.ToUpperInvariant()}");
        var hash = SHA1.HashData([.. namespaceBytes, .. nameBytes]);

        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(hash[..16]);
    }
}
```

**Convention:** Each BC gets its own PostgreSQL schema — `opts.DatabaseSchemaName = "auctions"`, `opts.DatabaseSchemaName = "listings"`, etc. One event stream per aggregate instance. Stream ID = Aggregate ID.

---

## Aggregate Design

### Core Principles

1. **Aggregates are immutable records** — use `with` expressions, never mutate
2. **Apply methods are pure functions** — transform state without side effects
3. **No behavior in aggregates** — only data + Apply methods; logic lives in handlers
4. **No base classes** — no `Aggregate` base class or `IEntity` interface
5. **Status enum over boolean flags** — single source of truth, impossible states are impossible

### Anatomy of an Event-Sourced Aggregate

```csharp
public sealed record Listing(
    Guid Id,
    Guid SellerId,
    string Title,
    decimal StartingBid,
    decimal? ReservePrice,
    decimal CurrentHighBid,
    Guid? HighBidderId,
    bool ReserveMet,
    ListingStatus Status,
    DateTimeOffset PublishedAt)
{
    public bool IsOpen => Status == ListingStatus.Open;
    public bool HasReserve => ReservePrice.HasValue;

    public static Listing Create(IEvent<ListingPublished> @event) =>
        new(@event.StreamId,
            @event.Data.SellerId,
            @event.Data.Title,
            @event.Data.StartingBid,
            @event.Data.ReservePrice,
            CurrentHighBid: 0m,
            HighBidderId: null,
            ReserveMet: false,
            ListingStatus.Pending,
            @event.Data.PublishedAt);

    public Listing Apply(BiddingOpened @event) =>
        this with { Status = ListingStatus.Open };

    public Listing Apply(BidPlaced @event) =>
        this with
        {
            CurrentHighBid = @event.Amount,
            HighBidderId = @event.BidderId
        };

    public Listing Apply(ReserveMet @event) =>
        this with { ReserveMet = true };

    public Listing Apply(BiddingClosed @event) =>
        this with { Status = ListingStatus.Closed };

    public Listing Apply(ListingWithdrawn @event) =>
        this with { Status = ListingStatus.Withdrawn };
}

public enum ListingStatus { Pending, Open, Closed, Withdrawn }
```

### Conventions

- **Parameter naming:** Always `@event` — never `evt`, `e`, or other abbreviations
- **Expression bodies:** Use `=>` for simple Apply methods; block bodies only for complex logic
- **Instance methods:** Prefer instance `Apply()` over static `Apply(State, Event)` for consistency
- Be consistent within a BC — do not mix instance and static Apply methods in the same aggregate

### Decider Pattern

**Inline handler logic** — for simple aggregates with 1–3 handlers:

```csharp
public static class OpenBiddingHandler
{
    public static ProblemDetails Before(OpenBidding cmd, Listing? listing)
    {
        if (listing is null) return new ProblemDetails { Detail = "Not found", Status = 404 };
        if (listing.Status != ListingStatus.Pending)
            return new ProblemDetails { Detail = "Already open", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        OpenBidding cmd,
        [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new BiddingOpened(listing.Id, cmd.ScheduledCloseAt));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.BiddingOpened(listing.Id, cmd.ScheduledCloseAt));

        return (events, outgoing);
    }
}
```

**Separate Decider class** — for complex aggregates with many handlers:

```csharp
public static class ListingDecider
{
    public static ListingDecision HandleBidPlaced(Listing listing, BidPlaced bid, DateTimeOffset now)
    {
        if (!listing.IsOpen) return ListingDecision.NoChange;
        if (bid.Amount <= listing.CurrentHighBid) return ListingDecision.NoChange;

        return new ListingDecision
        {
            NewHighBid = bid.Amount,
            NewHighBidderId = bid.BidderId,
            ReserveMetNow = !listing.ReserveMet && listing.ReservePrice.HasValue && bid.Amount >= listing.ReservePrice
        };
    }
}
```

---

## Domain Event Structure

**Always include the aggregate ID as the first parameter:**

```csharp
// ✅ CORRECT — aggregate ID first
public sealed record BidPlaced(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    bool IsProxyBid,
    DateTimeOffset PlacedAt);

// ❌ WRONG — missing aggregate ID
public sealed record BidPlaced(
    Guid BidId,
    decimal Amount);
```

**Naming:** Past tense. `BidPlaced`, `BiddingOpened`, `ReserveMet` — not `PlaceBid`, `OpenBidding`.

---

## Projections

Marten supports four projection types:

### 1. Inline Snapshots

```csharp
opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
opts.Projections.Snapshot<ParticipantSession>(SnapshotLifecycle.Inline);
```

Query the snapshot document:

```csharp
var listing = await session.LoadAsync<Listing>(listingId);
var openListings = await session.Query<Listing>()
    .Where(l => l.Status == ListingStatus.Open)
    .ToListAsync();
```

**Pros:** Zero lag, strong consistency, no daemon required.
**Cons:** Write latency increases slightly; not suitable for complex cross-stream read models.

### 2. Multi-Stream Projections

```csharp
public sealed class CatalogListingViewProjection : MultiStreamProjection<CatalogListingView, Guid>
{
    public CatalogListingViewProjection()
    {
        Identity<ListingPublished>(x => x.ListingId);
        Identity<BiddingOpened>(x => x.ListingId);
        Identity<BidPlaced>(x => x.ListingId);
        Identity<BiddingClosed>(x => x.ListingId);
    }

    public CatalogListingView Create(ListingPublished evt) =>
        new() { Id = evt.ListingId, SellerId = evt.SellerId, Title = evt.Title, Status = "Pending" };

    public static CatalogListingView Apply(CatalogListingView view, BiddingOpened evt) =>
        view with { Status = "Open", ClosesAt = evt.ScheduledCloseAt };

    public static CatalogListingView Apply(CatalogListingView view, BidPlaced evt) =>
        view with { CurrentHighBid = evt.Amount, BidCount = view.BidCount + 1 };

    public static CatalogListingView Apply(CatalogListingView view, BiddingClosed evt) =>
        view with { Status = "Closed", FinalPrice = evt.HammerPrice };
}
```

### 3. Async Projections

Processed by background daemon — for complex denormalized views, analytics, or high-write throughput.

```csharp
opts.Projections.Add<OperationsDashboardProjection>(ProjectionLifecycle.Async);
```

When using Wolverine-managed event subscription distribution, async daemon distribution is managed by Wolverine — do not call `AddAsyncDaemon()` directly. Instead, configure `UseWolverineManagedEventSubscriptionDistribution = true` in the `IntegrateWithWolverine()` call:

```csharp
.IntegrateWithWolverine(m =>
{
    m.UseWolverineManagedEventSubscriptionDistribution = true;
})
```

### 4. Live Aggregation

**Prefer `FetchLatest<T>()` for current-state lookups.** It consults snapshot caches and replays only events since the last snapshot — the hot-path default.

```csharp
// Preferred: fast, snapshot-aware
var listing = await session.Events.FetchLatest<Listing>(listingId);
```

**Reserve `AggregateStreamAsync<T>()` for time travel.** It always replays from event 1 (or from a given version/timestamp), bypassing any cached snapshot. Useful for diagnostics, audit tooling, and historical replay.

```csharp
// Time travel: state at a specific version
var listingAtV5 = await session.Events.AggregateStreamAsync<Listing>(listingId, 5);

// Time travel: state one hour ago
var listingThen = await session.Events.AggregateStreamAsync<Listing>(listingId,
    timestamp: DateTime.UtcNow.AddHours(-1));
```

Good for testing, admin audit tools, and diagnostic replay — not for hot-path queries.

### Decision Matrix

| Requirement | Lifecycle |
|---|---|
| Zero-lag aggregate state queries | `Inline` snapshot |
| Read model keyed differently than source | `Inline` multi-stream |
| Complex denormalized / cross-BC view | `Async` |
| High write throughput | `Async` |
| Temporal "time travel" queries | `Live` |
| Testing | `Inline` or `Live` |

### `Evolve` as an alternative to per-event `Apply` methods

The canonical CritterBids pattern is one `Apply(T)` method per event type. For aggregates or projection states with many event types, an alternative shape is a single `Evolve(IEvent)` method with a `switch` on `e.Data`:

```csharp
public sealed record ListingState(Guid Id, ListingStatus Status, decimal CurrentHighBid)
{
    public static ListingState Evolve(ListingState snapshot, IEvent e) => e.Data switch
    {
        ListingPublished p => new ListingState(p.ListingId, ListingStatus.Pending, 0m),
        BiddingOpened     => snapshot with { Status = ListingStatus.Open },
        BidPlaced b       => snapshot with { CurrentHighBid = b.Amount },
        BiddingClosed     => snapshot with { Status = ListingStatus.Closed },
        _                 => snapshot
    };
}
```

`Evolve(IEvent)` receives the wrapped event with metadata (timestamp, sequence, stream ID). Return `null` to delete the document. Useful when Apply methods all share common logic or when per-event metadata is needed across the switch.

**Pick one style per aggregate.** Don't mix `Apply(T)` methods and `Evolve(IEvent)` on the same type. CritterBids defaults to per-event `Apply` methods for their narrow, well-named call sites.

### `DetermineAction` — soft-delete / un-delete lifecycles

For projections that need to distinguish "store", "soft-delete", and "undelete" at write time, override `DetermineAction` on a `SingleStreamProjection<T, TId>`:

```csharp
public class ListingProjection : SingleStreamProjection<Listing, Guid>
{
    public override (Listing?, ActionType) DetermineAction(
        Listing? snapshot, Guid identity, IReadOnlyList<IEvent> events)
    {
        var actionType = ActionType.Store;

        foreach (var data in events.ToQueueOfEventData())
        {
            switch (data)
            {
                case ListingPublished p:
                    snapshot = new Listing(identity, p.SellerId, p.Title, ...);
                    break;
                case ListingWithdrawn when snapshot is { Withdrawn: false }:
                    snapshot = snapshot with { Withdrawn = true };
                    actionType = ActionType.StoreThenSoftDelete;
                    break;
                case ListingRepublished when snapshot?.Withdrawn == true:
                    snapshot = snapshot with { Withdrawn = false };
                    actionType = ActionType.UnDeleteAndStore;
                    break;
            }
        }
        return (snapshot, actionType);
    }
}
```

CritterBids' current Listings workflow uses status-enum transitions rather than soft-delete, so `DetermineAction` is not currently wired. If a future withdrawal-and-relist workflow demands that a listing's read model be hidden from catalog queries without losing history, `DetermineAction` is the right tool — it preserves the snapshot while flagging it as soft-deleted.

### Rebuilding a single stream's projection

For healing one aggregate's read model without rebuilding the entire projection (e.g., after fixing a bug that corrupted state for a specific listing), Marten 7.28+ exposes `RebuildSingleStreamAsync`:

```csharp
await store.Advanced.RebuildSingleStreamAsync<Listing>(listingId);
```

Replays all events for that stream and rebuilds its projected document. Targeted healing — much cheaper than `daemon.RebuildProjectionAsync<ListingProjection>()`, which replays every listing in the system.

### Projection performance knobs

Three tunables matter in practice when a projection's throughput comes under pressure:

```csharp
public class CatalogListingViewProjection : MultiStreamProjection<CatalogListingView, Guid>
{
    public CatalogListingViewProjection()
    {
        Identity<ListingPublished>(x => x.ListingId);
        // ... routing rules ...

        // Only load these event types from the DB — needed when Apply methods
        // use interfaces or base types that Marten can't auto-infer from.
        IncludeType<ListingPublished>();
        IncludeType<BiddingOpened>();
        IncludeType<BidPlaced>();
        IncludeType<BiddingClosed>();

        // Cache projected documents in memory per tenant. Keeps hot tenants
        // responsive under heavy load; memory cost grows with fan-in.
        Options.CacheLimitPerTenant = 1000;

        // Process more events per daemon batch. Default is 1000.
        Options.BatchSize = 5000;
    }
}
```

**When to reach for `IncludeType<T>`:** only when `Apply` methods take a base type or interface (e.g., `Apply(IListingEvent e, ...)`) — Marten can't infer the event filter from polymorphic signatures and has to fall back to loading everything. Concrete-typed `Apply` methods get the filter for free.

**CritterBids posture:** defaults are fine until a projection's daemon shard visibly falls behind. `CacheLimitPerTenant` and `BatchSize` are the first levers to pull before considering architectural changes. `IncludeType<T>` is a latent correctness fix (unnecessary I/O) rather than a tuning knob.

---

## ProjectLatest — Pending Event State

Marten 8.29+ ships a `ProjectLatest` API that projects an aggregate against pending (uncommitted)
events, returning the would-be state before `SaveChangesAsync()` is called. Two patterns are
relevant to CritterBids:

### Pattern 1 — Validate against the would-be state

```csharp
public static class PlaceBidHandler
{
    public static async Task<ProblemDetails> ValidateAsync(
        PlaceBid cmd, [ReadAggregate] Listing? listing, IDocumentSession session)
    {
        if (listing is null)
            return new ProblemDetails { Detail = "Listing not found", Status = 404 };
        if (!listing.IsOpen)
            return new ProblemDetails { Detail = "Listing closed", Status = 400 };
        if (cmd.Amount <= listing.CurrentHighBid)
            return new ProblemDetails { Detail = "Bid does not exceed current high", Status = 400 };

        var pendingEvent = new BidPlaced(listing.Id, cmd.BidId, cmd.BidderId, cmd.Amount);
        var wouldBe = await session.Events.ProjectLatest<Listing>(listing.Id, pendingEvent);
        // Inspect wouldBe for further business rules...

        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(PlaceBid cmd, [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new BidPlaced(listing.Id, cmd.BidId, cmd.BidderId, cmd.Amount));
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.BidPlaced(listing.Id, cmd.BidderId, cmd.Amount));
        return (events, outgoing);
    }
}
```

### Pattern 2 — Return the updated state from an HTTP command handler

```csharp
[WolverinePost("/api/listings/{listingId}/bids")]
public static async Task<(IResult, Events, OutgoingMessages)> Handle(
    PlaceBid cmd, [WriteAggregate] Listing listing, IDocumentSession session)
{
    var evt = new BidPlaced(listing.Id, cmd.BidId, cmd.BidderId, cmd.Amount);
    var updated = await session.Events.ProjectLatest<Listing>(listing.Id, evt);

    var outgoing = new OutgoingMessages();
    outgoing.Add(new Contracts.BidPlaced(listing.Id, cmd.BidderId, cmd.Amount));

    return (Results.Ok(updated), new Events(evt), outgoing);
}
```

**`ProjectLatest` is read-only** — it computes the projected state in memory but does not append events. The return value of `Handle()` (the `Events` object) is what Wolverine persists.

---

## Snapshot Strategies

Configure inline snapshots for any aggregate you need to query via LINQ. Without a snapshot, `session.Query<T>()` returns empty even when the event stream exists.

```csharp
opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
opts.Projections.Snapshot<HeavyAggregate>(SnapshotLifecycle.Async);
opts.Projections.UseIdentityMapForAggregates = true;
```

**Diagnosis:** If `session.Events.AggregateStreamAsync<T>(id)` returns a result but `session.Query<T>().ToListAsync()` returns empty — you're missing a snapshot registration.

---

## Wolverine Integration Patterns

See `wolverine-message-handlers.md` for the full return type reference. Key patterns for event sourcing:

**Append to existing stream:**
```csharp
public static (Events, OutgoingMessages) Handle(SomeCmd cmd, [WriteAggregate] Listing listing) { ... }
```

**Start new stream — MUST use `MartenOps.StartStream()`:**
```csharp
// ✅ Correct — IStartStream returned to Wolverine
var stream = MartenOps.StartStream<Listing>(id, new ListingPublished(...));
return (new CreationResponse(...), stream);

// ❌ Wrong — session.Events.StartStream() is silently discarded
session.Events.StartStream<Listing>(id, new ListingPublished(...)); // NOT persisted
```

**Multiple events from one handler:**
```csharp
public static IEnumerable<object> Handle(CompleteCheckout cmd, [WriteAggregate] Checkout checkout)
{
    yield return new PaymentRecorded(checkout.Id, cmd.Amount);
    yield return new CheckoutCompleted(checkout.Id, DateTimeOffset.UtcNow);
}
```

**`[MartenStore]` attribute (named/ancillary stores only):**

> **ADR 009:** CritterBids uses a single primary `IDocumentStore`. `[MartenStore]` is **not** required
> on any handler. `IDocumentSession` is injected directly by Wolverine's `SessionVariableSource`.

If named or ancillary stores are ever introduced (e.g. multi-server deployments), every handler
targeting that store must carry `[MartenStore(typeof(IBcDocumentStore))]`. Without it, Wolverine
routes the injected session to the wrong store. This does not apply to current CritterBids handlers.

---

## Cross-Stream Aggregate Handlers

Most handlers mutate exactly one stream. When a single command legitimately needs to mutate **two or more known streams atomically**, add one `[WriteAggregate]` parameter per stream. Wolverine loads each stream via `FetchForWriting` and commits all appended events in a single `SaveChangesAsync()` call — the writes are atomic.

This is the "known stream IDs on the command" pattern. If the set of affected streams is dynamic (selected by tag query rather than by ID), you want DCB instead — see [Tagged Event Writes (DCB)](#tagged-event-writes-dcb) below.

### Multiple `[WriteAggregate]` parameters

Identify each stream by `nameof(Command.SomeId)`. Each parameter gets its own optimistic-concurrency check against the loaded version.

```csharp
public sealed record TransferCredit(Guid FromBidderId, Guid ToBidderId, decimal Amount);

public static class TransferCreditHandler
{
    public static void Handle(
        TransferCredit command,
        [WriteAggregate(nameof(TransferCredit.FromBidderId))] IEventStream<BidderCredit> from,
        [WriteAggregate(nameof(TransferCredit.ToBidderId))]   IEventStream<BidderCredit> to)
    {
        if (from.Aggregate.AvailableCredit < command.Amount)
            throw new InvalidOperationException("Insufficient credit");

        from.AppendOne(new CreditDebited(command.FromBidderId, command.Amount));
        to.AppendOne(new CreditCredited(command.ToBidderId, command.Amount));
    }
}
```

### Per-stream optimistic concurrency with `VersionSource`

Wolverine auto-discovers a `Version` property only for the **first** `[WriteAggregate]` parameter. Additional parameters need an explicit `VersionSource` pointing to a per-stream expected-version property on the command:

```csharp
public sealed record TransferCredit(
    Guid FromBidderId,
    Guid ToBidderId,
    decimal Amount,
    long FromVersion,
    long ToVersion);

public static void Handle(
    TransferCredit command,
    [WriteAggregate(nameof(TransferCredit.FromBidderId),
        VersionSource = nameof(TransferCredit.FromVersion))] IEventStream<BidderCredit> from,
    [WriteAggregate(nameof(TransferCredit.ToBidderId),
        VersionSource = nameof(TransferCredit.ToVersion))] IEventStream<BidderCredit> to)
{
    from.AppendOne(new CreditDebited(command.FromBidderId, command.Amount));
    to.AppendOne(new CreditCredited(command.ToBidderId, command.Amount));
}
```

### `[ConsistentAggregate]` — read-then-decide-not-to-write

`[WriteAggregate]` only enforces the version check when events are actually appended. If a handler loads a stream, inspects its state, and decides to append nothing, the version check is skipped — which means a concurrent write on that stream can slip through.

When the correctness of the "append nothing" decision depends on the loaded state being current, use `[ConsistentAggregate]`. It forces the version check **even when no events are appended**.

| Need | Attribute |
|---|---|
| Mutate a stream, always append events | `[WriteAggregate]` (default) |
| Mutate a stream, sometimes append nothing, always need version guarantee | `[ConsistentAggregate]` |
| Read-only access, no concurrency requirement | `[ReadAggregate]` |

**Parameter-level** — apply to a specific parameter when mixing strict and loose consistency:

```csharp
public static class ApproveAuctionChangeHandler
{
    public static IEnumerable<object> Handle(
        ApproveAuctionChange command,
        [WriteAggregate(nameof(ApproveAuctionChange.ListingId))] IEventStream<Listing> listing,
        [ConsistentAggregate(nameof(ApproveAuctionChange.SellerId))] IEventStream<Seller> seller)
    {
        if (!seller.Aggregate.IsInGoodStanding)
            yield break; // No event appended — but seller version is STILL checked

        yield return new ListingChangeApproved(command.ListingId, command.ChangeId);
    }
}
```

If a concurrent "suspend seller" write lands between the seller load and the handler's decision, the save fails with `ConcurrencyException` — exactly what we want when the seller's good-standing status is load-bearing for the approval.

**Class-level** — apply `[ConsistentAggregateHandler]` when every aggregate parameter on the class should enforce consistency regardless of whether events are appended:

```csharp
[ConsistentAggregateHandler]
public static class CheckListingAvailabilityHandler
{
    public static IEnumerable<object> Handle(
        CheckListingAvailability command,
        IEventStream<Listing> listing)
    {
        if (listing.Aggregate.Status == ListingStatus.Open)
            yield return new ListingAvailabilityConfirmed(command.ListingId);
        // When closed: no event, but version check still happens
    }
}
```

### When to reach for cross-stream handlers vs DCB

| Feature | Multiple `[WriteAggregate]` | DCB `[BoundaryModel]` |
|---|---|---|
| Stream identity | Known IDs on the command | Event tags, flexible query |
| Number of streams | Fixed at compile time | Dynamic at runtime |
| Typical use | Transfer between two known entities | Capacity / uniqueness rules across many streams |
| Concurrency | Per-stream optimistic | Tag-scoped optimistic |

Prefer cross-stream `[WriteAggregate]` when both IDs are on the command. Reach for DCB when the set of affected streams depends on a query (e.g., "all active bidder sessions for this listing") rather than on known IDs.

---

## Tagged Event Writes (DCB)

When using DCB, events must be tagged at write time via `BuildEvent()` + `AddTag()` + `Append()`. Standard paths (`[WriteAggregate]`, `MartenOps.StartStream()`, raw `session.Events.Append(id, obj)`) do NOT populate tag tables.

```csharp
var wrapped = session.Events.BuildEvent(evt);
wrapped.AddTag(new ListingStreamId(listingId));
session.Events.Append(listingId, wrapped);
```

`StartStream` re-wraps `IEvent` objects and drops tags — use `Append` instead; streams are created implicitly on first append.

See `docs/skills/dynamic-consistency-boundary.md` for the complete DCB implementation checklist.

---

## Async Daemon Configuration

The async daemon is what processes `ProjectionLifecycle.Async` projections and Wolverine event subscriptions. Getting its registration right matters more than any other projection tuning knob — a misconfigured daemon either doesn't run, runs on the wrong node, or fights another daemon for shard assignments.

### Daemon modes

Three modes, each with a specific fit:

| Mode | Registration | Best for |
|---|---|---|
| **Solo** | `.AddAsyncDaemon(DaemonMode.Solo)` | Dev, tests, single-node deployments. No leader election. |
| **HotCold** | `.AddAsyncDaemon(DaemonMode.HotCold)` | Multi-node with Marten-managed leader election. One "hot" node runs all shards; others stand by. |
| **Wolverine-managed distribution** | `UseWolverineManagedEventSubscriptionDistribution = true` | Multi-node production. Distributes shards evenly across all active nodes instead of concentrating on one. |

**CritterBids uses Wolverine-managed distribution** when `IntegrateWithWolverine()` is wired — it's the production-grade default for the deployment target (Hetzner single-VPS today, with a clean path to multi-node if load demands it).

```csharp
builder.Services.AddMarten(opts => { /* ... */ })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine(m =>
    {
        m.UseWolverineManagedEventSubscriptionDistribution = true;
    });

// Durability must be Balanced, not Solo, for Wolverine-managed distribution:
builder.Host.UseWolverine(opts =>
{
    opts.Durability.Mode = DurabilityMode.Balanced;
});
```

### Critical rule: do NOT combine `AddAsyncDaemon(HotCold)` with Wolverine-managed distribution

These are two different shard-assignment schemes. Wiring both creates a fight: Marten's HotCold election picks one node as hot, while Wolverine tries to distribute shards across all nodes. The observable result is non-deterministic shard ownership, duplicate projection work, and drifting `wolverine_node_assignments`.

```csharp
// ❌ WRONG — two competing daemon managers
services.AddMarten(opts => { /* ... */ })
    .AddAsyncDaemon(DaemonMode.HotCold)
    .IntegrateWithWolverine(m =>
    {
        m.UseWolverineManagedEventSubscriptionDistribution = true;
    });

// ✅ CORRECT — pick one
services.AddMarten(opts => { /* ... */ })
    .IntegrateWithWolverine(m =>
    {
        m.UseWolverineManagedEventSubscriptionDistribution = true;
    });
```

### Error handling defaults

Marten's default daemon error handling skips poison events in normal processing but is strict during rebuilds. CritterBids inherits these defaults:

```csharp
services.AddMarten(opts =>
{
    // Continuous processing — tolerate bad events so the daemon doesn't halt
    opts.Projections.Errors.SkipApplyErrors = true;         // default: true
    opts.Projections.Errors.SkipSerializationErrors = true; // default: true
    opts.Projections.Errors.SkipUnknownEvents = true;       // default: true

    // Rebuilds — fail fast so we can fix the bug before overwriting good state
    opts.Projections.RebuildErrors.SkipApplyErrors = false;        // default: false
    opts.Projections.RebuildErrors.SkipSerializationErrors = false; // default: false
    opts.Projections.RebuildErrors.SkipUnknownEvents = false;       // default: false
});
```

**CritterBids posture:** accept defaults. The asymmetry ("loose during steady-state, strict during rebuild") is the right call for a reference-architecture project where rebuilds are deliberate and inspectable, while continuous processing must not halt on a single malformed event.

When `SkipApplyErrors = true` with Wolverine event subscriptions using `ProcessEventsWithWolverineHandlersInStrictOrder`, the order of fallback is: (1) Wolverine's inline retries fire, (2) if retries are exhausted, the event goes to Wolverine's dead letter queue, (3) the subscription continues. Flip `SkipApplyErrors = false` and the subscription pauses on exhaustion instead.

### `WaitForNonStaleProjectionDataAsync` in tests

Async projections don't run inline — tests that append events and immediately query projected documents will see empty results unless they wait for the daemon to catch up.

```csharp
// Append events
await using var session = _fixture.Host.DocumentStore().LightweightSession();
session.Events.Append(listingId, new ListingPublished(...), new BiddingOpened(...));
await session.SaveChangesAsync();

// Wait for async daemon to catch up before querying projected data
await _fixture.Host.DocumentStore().WaitForNonStaleProjectionDataAsync(5.Seconds());

var projection = await session.Query<CatalogListingView>()
    .Where(x => x.Id == listingId).FirstOrDefaultAsync();
projection.ShouldNotBeNull();
```

**For test fixtures**, use `DaemonMode.Solo` to avoid the leader-election cost:

```csharp
builder.ConfigureServices(services =>
{
    services.MartenDaemonModeIsSolo();   // faster test startup
    services.RunWolverineInSoloMode();   // no leader election
});
```

### Anti-pattern recap

- `DurabilityMode.Solo` in production — loses leader election, shard distribution, and crash recovery. Always `Balanced` for production; `Solo` for dev/tests only.
- Forgetting `WaitForNonStaleProjectionDataAsync` in async-projection tests — silent intermittent failures that look like test flakiness.
- Calling `AddAsyncDaemon()` alongside Wolverine-managed distribution (see above).

---

## Testing Event-Sourced Systems

### The Race Condition Problem

HTTP-based tests for event-sourced aggregates fail under load: Wolverine's `AutoApplyTransactions()` commits asynchronously — the HTTP response returns before the commit completes.

```csharp
// ❌ Race condition
await _fixture.Host.Scenario(s => s.Post.Json(cmd).ToUrl(url));
await _fixture.Host.Scenario(s => s.Get.Url(url)); // Stale data!
```

### ✅ The Fix: Direct Command Invocation

```csharp
await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, Guid.NewGuid(), bidderId, 50m));

await using var session = _fixture.GetDocumentSession();
var listing = await session.Events.AggregateStreamAsync<Listing>(listingId);

listing.ShouldNotBeNull();
listing.CurrentHighBid.ShouldBe(50m);
```

| Test Type | Approach |
|---|---|
| Aggregate state transitions | Direct command invocation + query event store |
| HTTP endpoint routing / serialization | HTTP via Alba |
| Integration flows | Direct command invocation |
| E2E tests | HTTP or Playwright |

---

## Event Versioning

### Default: Additive-Only Changes

New fields must be nullable or have defaults:

```csharp
// Version 2 — safe additive change
public sealed record BidPlaced(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount,
    bool IsProxyBid = false);
```

### Breaking Changes: Upcasting

```csharp
opts.Events.Upcast<BidPlacedV1, BidPlaced>(v1 =>
    new BidPlaced(v1.ListingId, v1.BidId, v1.BidderId, v1.Amount, IsProxyBid: false));
```

---

## Marten Configuration

### Standard BC Module Pattern (ADR 009, extended by ADR 011)

*Confirmed by CritterStackSamples north star analysis (§9 — Modular Monolith Architecture, §14 — Cross-Cutting Similarities)*

CritterBids uses a **single primary `IDocumentStore`** registered in `Program.cs`. Every one of the
eight BCs contributes its types via `services.ConfigureMarten()` inside its `AddXyzModule()`. Never
call `AddMarten()` or `AddMartenStore<T>()` from inside a BC module.

`opts.Schema.For<T>().DatabaseSchemaName("bc-name")` is how each BC claims its schema namespace
within the shared store. Document tables for BC-owned types are isolated in the BC's schema; the
shared `mt_events` and `mt_streams` tables live in the root schema defined by `Program.cs`.

`opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` is registered here for any aggregate that
needs fast read access via LINQ queries. Inline lifecycle means the snapshot updates in the same
transaction as the event append — always consistent, no daemon required.

```csharp
// Inside AddAuctionsModule() — no connection string, no store provisioning
public static IServiceCollection AddAuctionsModule(this IServiceCollection services)
{
    services.ConfigureMarten(opts =>
    {
        // Schema isolation: document tables in "auctions" schema, mt_events shared
        opts.Schema.For<Listing>().DatabaseSchemaName("auctions");
        opts.Schema.For<Listing>().Identity(x => x.Id).Index(x => x.SellerId);

        opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
        opts.Projections.Add<CatalogListingViewProjection>(ProjectionLifecycle.Inline);
        opts.Projections.UseIdentityMapForAggregates = true;
    });

    services.AddTransient<IAuctionPricingService, AuctionPricingService>();
    return services;
}
```

### Primary Store Registration (`Program.cs`)

```csharp
var postgresConnectionString = builder.Configuration.GetConnectionString("postgres");
if (!string.IsNullOrEmpty(postgresConnectionString))
{
    builder.Services.AddMarten(opts =>
    {
        opts.Connection(postgresConnectionString);
        opts.DatabaseSchemaName = "public";
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Events.UseMandatoryStreamTypeDeclaration = true;
        opts.DisableNpgsqlLogging = true;
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();

    // All Marten BC modules registered here — inside the postgres null guard
    builder.Services.AddSellingModule();
    // builder.Services.AddAuctionsModule();
    // builder.Services.AddListingsModule();
}
```

### Host-Level Wolverine Settings (`Program.cs` — not in BC modules)

*Confirmed by CritterStackSamples north star analysis: `AutoApplyTransactions()` is always in `UseWolverine()` globally, never inside a BC's `ConfigureMarten()` call.*

```csharp
builder.UseWolverine(opts =>
{
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
    opts.Durability.MessageStorageSchemaName = "wolverine";
    opts.Policies.AutoApplyTransactions();   // ← global policy, applies to every BC's handlers
});
```

`AutoApplyTransactions()` is a global Wolverine policy registered once in `UseWolverine()`. It
applies to every handler in the process — there is no need to, and you must not, include it inside
a BC's `services.ConfigureMarten()` call. This matches every CritterStackSamples reference project
without exception.

Note: earlier CritterBids documents (M2 milestone §6, pre-ADR 011) showed `AutoApplyTransactions()`
inside the `AddMarten()` lambda in each BC module. That placement is incorrect. The correct placement
is `opts.Policies.AutoApplyTransactions()` inside `UseWolverine()` in `Program.cs`. The existing
`SellingTestFixture.cs` does not include it in the fixture's `AddMarten()` call precisely because
the host's `UseWolverine()` block covers it.

### ⚠️ CRITICAL: `AutoApplyTransactions()` Is Non-Negotiable

Without `AutoApplyTransactions()`, handlers do not commit Marten changes — silent failure.
**Diagnosis:** Handler returns HTTP 200, events table is empty, no exceptions → verify
`AutoApplyTransactions()` is in `Program.cs`'s `UseWolverine()` block.

### ⚠️ Do Not Call `SaveChangesAsync()` in Wolverine Handlers

`AutoApplyTransactions()` commits the session after handler execution. Manual `SaveChangesAsync()` is redundant — remove it.

### Session Types

```csharp
LightweightSession()    // Default via UseLightweightSessions() — best performance
DirtyTrackedSession()   // Change tracking — rare, only when conditional saves needed
QuerySession()          // Read-only — HTTP GET endpoints
```

### Projection Configuration Reference

```csharp
opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline);               // Zero-lag snapshot
opts.Projections.Snapshot<T>(SnapshotLifecycle.Async);                // Low write latency
opts.Projections.Add<TProjection>(ProjectionLifecycle.Inline);        // Multi-stream inline
opts.Projections.Add<TProjection>(ProjectionLifecycle.Async);         // Async daemon
opts.Projections.UseIdentityMapForAggregates = true;                  // Cache aggregates
opts.Projections.EnableAdvancedAsyncTracking = true;                  // Better daemon tracking
opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;   // Poison event handling
```

### Event Append Mode — Why `Quick`

Marten offers two event-append modes, and the choice matters for high-write scenarios like live auction bidding:

```csharp
opts.Events.AppendMode = EventAppendMode.Rich;   // Default
opts.Events.AppendMode = EventAppendMode.Quick;  // CritterBids default
```

| Mode | Behavior | Trade-off |
|---|---|---|
| **Rich** | Two-step: insert events, then round-trip to populate `IEvent.Version` and `IEvent.Sequence` at append time | Inline projections receive populated `IEvent` metadata during the same transaction |
| **Quick** | Single-step: insert events; `Version`/`Sequence` are assigned server-side and re-synced after | 40–50% faster appends; eliminates sequence gaps under load |

**CritterBids uses `Quick` mode** (see `Program.cs`). Quick eliminates the "event skipping" problem where slow clients create gaps in the event sequence that stall the high water mark, and the performance win compounds at bid-time.

**The trade-off:** inline projections don't receive `IEvent.Version` or `IEvent.Sequence` during projection time under Quick mode — those values are populated after the DB round-trip. If an aggregate's own logic needs its current version inside an inline projection's `Apply` method, implement `IRevisioned` on the aggregate so Marten re-syncs the version after commit. CritterBids aggregates don't currently consume `IEvent.Version` in `Apply`, so `IRevisioned` is not wired today.

### `UseIdentityMapForAggregates` — snapshot caching inside the session

```csharp
opts.Events.UseIdentityMapForAggregates = true;   // CritterBids enables this per BC module
```

Caches aggregate snapshots in the session's identity map so repeated `FetchForWriting` or `FetchLatest` calls within the same session don't re-query the database. Particularly valuable for HTTP endpoints that return `UpdatedAggregate` — the post-commit state is already in the map, no extra round-trip needed.

> **⚠️ Warning: do not mutate aggregates loaded via the identity map outside Marten's own projection or aggregate-handler workflow.** With identity-mapping on, mutations to a cached aggregate are persisted on the next `SaveChangesAsync()` — which means code that loads, mutates, and doesn't intend to save can still corrupt the aggregate's stored state. Treat identity-mapped aggregates as **read-only** outside the projection pipeline and `[WriteAggregate]` handler chain. (This is Anti-Pattern #14 below.)

### Streaming JSON endpoints with `Marten.AspNetCore`

The `Marten.AspNetCore` NuGet provides extension methods that stream raw JSONB from PostgreSQL directly to the HTTP response, bypassing C# deserialization and re-serialization. Significant performance win for read-heavy endpoints — especially Listings BC's catalog browse, which is dominated by large result sets.

```csharp
using Marten;
using Marten.AspNetCore;

// Stream a list of documents as a JSON array
[WolverineGet("/api/listings")]
[ProducesResponseType<Listing[]>(200, "application/json")]
public static Task GetOpenListings(IQuerySession session, HttpContext context)
    => session.Query<Listing>()
        .Where(l => l.Status == ListingStatus.Open)
        .OrderByDescending(l => l.PublishedAt)
        .WriteArray(context);

// Stream a single document by ID (404 if not found)
[WolverineGet("/api/listings/{id}")]
[ProducesResponseType<Listing>(200, "application/json")]
[ProducesResponseType(404)]
public static Task GetListing(Guid id, IQuerySession session, HttpContext context)
    => session.Json.WriteById<Listing>(id, context);
```

**OpenAPI caveat:** because these endpoints return `Task` (void from the framework's perspective), Wolverine can't infer the response type for OpenAPI/Swagger. Add `[ProducesResponseType<T>]` explicitly — this is the trade-off for the performance win.

**Serialization caveat:** the JSON streamed is Marten's stored JSONB, which uses `AddMarten()`'s configured serializer settings (typically camelCase with `EnumStorage.AsString`). If the HTTP API contract requires a different JSON shape than Marten stores internally, either align the Marten serializer to the API contract or use a conventional endpoint that materializes through a DTO.

CritterBids' Listings BC catalog browse is the obvious first adopter once the Listings BC lands; until then, standard `ToListAsync()` endpoints are fine.

---

## Anti-Patterns to Avoid

### 1. ❌ Mutable State on Aggregates

```csharp
// ❌ WRONG
public sealed record Listing(Guid Id, List<Bid> Bids)
{
    public void Apply(BidPlaced @event) { Bids.Add(...); }
}

// ✅ CORRECT
public sealed record Listing(Guid Id, IReadOnlyList<Bid> Bids)
{
    public Listing Apply(BidPlaced @event) =>
        this with { Bids = Bids.Append(new Bid(...)).ToList() };
}
```

### 2. ❌ Business Logic in Apply Methods

`Apply()` is for state transformation only. Validation and business decisions belong in handlers.

### 3. ❌ Side Effects in Apply Methods

Apply methods are called during replay. Side effects (logging, HTTP calls, publishing) in Apply will fire on every projection rebuild.

### 4. ❌ Inconsistent `@event` Naming

Always use `@event`. Never `evt`, `e`, `domainEvent`, or any other variation.

### 5. ❌ Block Bodies When Expression Bodies Work

Use `=>` expression bodies for simple transforms. Block bodies only for complex logic with temporary variables.

### 6. ❌ Missing Aggregate ID in Events

Always include the aggregate ID as the first property.

### 7. ❌ HTTP-Based Testing of Event-Sourced Aggregates

POST → immediate GET is a race condition. Use `ExecuteAndWaitAsync` + direct event store query.

### 8. ❌ Missing Snapshot Projection for Queryable Aggregates

`session.Query<T>()` returns empty without a snapshot projection even when events exist.

### 9. ❌ Missing Apply Methods for Event Types in Stream ⚠️ CRITICAL

If an aggregate stream contains an event type for which no `Apply()` method exists, Marten returns `null` from `AggregateStreamAsync<T>()`. **Silent failure — no exception.** Every event type appended to a stream must have a corresponding `Apply()` method.

**Diagnosis:** `FetchStreamAsync()` shows events exist but `AggregateStreamAsync()` returns null → find the event type missing its `Apply()` method.

### 10. ❌ Missing `AutoApplyTransactions()`

Silent data loss. Handler returns success, nothing persisted.

### 11. ❌ Missing `[MartenStore]` on Handlers

> **ADR 009 update:** CritterBids uses a shared primary store — `[MartenStore]` attributes are not
> required on handlers. This anti-pattern applied under ADR 008 (superseded). Retained here as
> reference if named/ancillary stores are introduced for multi-server deployments.

Named store handlers without `[MartenStore(typeof(IBcDocumentStore))]` do not route to the correct store.
Wolverine does not infer the store from the parameter type. Silent misconfiguration — wrong store is used
or no session is injected.

### 12. ❌ Calling `AddMarten()` in Multiple BC Modules or `AddMartenStore<T>()` Per BC

Two `AddMarten()` calls in the same process register competing `IDocumentStore` singletons — the second
call silently discards the first BC's configuration. The fix is not `AddMartenStore<T>()` (which
loses `IDocumentSession` injection and `AutoApplyTransactions`) but a single `AddMarten()` in `Program.cs`
with each BC contributing via `services.ConfigureMarten()` inside its `AddXyzModule()`. See ADR 009.

### 13. ❌ DCB Boundary State Missing `Guid Id` Property

Marten registers boundary state classes as documents. Without `public Guid Id { get; set; }`, `CleanAllMartenDataAsync()` throws during test cleanup causing cascading failures.

### 14. ❌ Mutating Aggregates Cached Under `UseIdentityMapForAggregates`

When `UseIdentityMapForAggregates = true`, Marten caches aggregate snapshots in the session's identity map. Any mutation to a cached aggregate is persisted on the next `SaveChangesAsync()` — even if the mutating code never intended to save.

```csharp
// ❌ WRONG — mutation persisted silently on next SaveChangesAsync
var listing = await session.Events.FetchLatest<Listing>(listingId);
listing.Title = listing.Title.ToUpperInvariant();  // code path that "just logs" the title
// Later in the same session:
await session.SaveChangesAsync();  // title is now uppercase in the DB — no one asked for that

// ✅ CORRECT — project to a DTO for ad-hoc reads; leave the aggregate untouched
var listing = await session.Events.FetchLatest<Listing>(listingId);
var display = new ListingDisplay(Title: listing.Title.ToUpperInvariant(), ...);
```

**Diagnosis:** a listing's fields mysteriously change after unrelated handler executions. Check whether `UseIdentityMapForAggregates` is on and whether any code path mutates aggregate properties outside an `[WriteAggregate]` handler or projection `Apply` method.

Treat identity-mapped aggregates as **read-only** outside the projection pipeline and aggregate-handler chain. If the same session needs a mutable view, project into a DTO.

---

## Lessons Learned

**L1: Verify integration queue wiring end-to-end.** Unit tests and handler tests aren't enough. A BC can publish an event that never reaches the subscriber because the queue binding is missing. Add cross-BC smoke tests for all RabbitMQ queues.

**L2: Design integration events for all known consumers.** When an event carries too little data, every new consumer forces a contract expansion. Document all consumers when designing event payloads.

**L3: HTTP-based testing doesn't respect eventual consistency.** `POST → GET` race conditions appear intermittently on CI and under load. Use direct command invocation for state-changing operations.

**L4: Sagas must handle all terminal states.** A saga that handles `ReturnCompleted` but not `ReturnRejected` leaves dangling state permanently.

**L5: Document-based saga vs event-sourced aggregate.** Sagas are write-heavy, read-light. Use document store with numeric revisions for saga state. Use event sourcing for domain aggregates where history matters.

**L6: `AutoApplyTransactions()` is non-negotiable.** Its absence fails silently — handlers appear to work but persist nothing.

**L7: Don't call `SaveChangesAsync()` in Wolverine handlers.** `AutoApplyTransactions()` handles commits.

**L8: Under ADR 009 (shared primary store), `[MartenStore]` is not required on handlers.** `IDocumentSession` is injected by `SessionVariableSource` from the primary store registered in `Program.cs` via `AddMarten().IntegrateWithWolverine()`. The named-store constraint (ADR 008, superseded) required the attribute for inbox routing.

**L9: `MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination` must both be set.** Without `Separated`, multiple BC handlers for the same message type combine into one queue — BC isolation is broken. Without `IdAndDestination`, fanout deduplication silently prevents some BC handlers from firing.

---

## References

- [Marten Event Sourcing](https://martendb.io/events/)
- [Marten Projections](https://martendb.io/events/projections/)
- [Wolverine Marten Integration](https://wolverinefx.net/guide/durability/marten/)
- [Decider Pattern](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)
- `docs/decisions/009-shared-marten-store.md` — shared primary store, ConfigureMarten() pattern (current)
- `docs/decisions/008-marten-bc-isolation.md` — named store ADR (superseded)
- `docs/skills/adding-bc-module.md` — canonical BC module registration pattern
- `docs/skills/dynamic-consistency-boundary.md`
- `docs/skills/wolverine-message-handlers.md`
- `docs/skills/wolverine-sagas.md`
