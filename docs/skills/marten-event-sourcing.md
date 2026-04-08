# Event Sourcing with Marten and Wolverine

Patterns for event-sourced systems using Marten (event store + document database) and Wolverine (command handling + message bus).

---

## Table of Contents

1. [When to Use Event Sourcing](#when-to-use-event-sourcing)
2. [Stream Identity Conventions](#stream-identity-conventions)
3. [Aggregate Design](#aggregate-design)
4. [Domain Event Structure](#domain-event-structure)
5. [Projections](#projections)
6. [Snapshot Strategies](#snapshot-strategies)
7. [Wolverine Integration Patterns](#wolverine-integration-patterns)
8. [Tagged Event Writes (DCB)](#tagged-event-writes-dcb)
9. [Testing Event-Sourced Systems](#testing-event-sourced-systems)
10. [Event Versioning](#event-versioning)
11. [Marten Configuration](#marten-configuration)
12. [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
13. [Lessons Learned](#lessons-learned)

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
| **UUID v7** (time-ordered, random) | Stream ID generated at entity creation — most aggregates |
| **UUID v5** (deterministic, SHA-1) | Stream ID derived from natural key — multiple handlers need same ID without lookup |

**UUID v7 Pattern:**

```csharp
[WolverinePost("/api/listings")]
public static (CreationResponse<Guid>, IStartStream) Handle(PublishListing cmd)
{
    var listingId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Listing>(listingId, new ListingPublished(listingId, ...));
    return (new CreationResponse<Guid>($"/api/listings/{listingId}", listingId), stream);
}
```

**UUID v5 Pattern** — when multiple handlers must resolve the same stream without a lookup:

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
    // Derived properties — computed from state, no side effects
    public bool IsOpen => Status == ListingStatus.Open;
    public bool HasReserve => ReservePrice.HasValue;

    // Factory method — creates aggregate from first event
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

    // Apply methods — pure functions, always use @event parameter name
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

Two flavors:

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
// Pure static functions — no infrastructure dependencies
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

Use a separate Decider class when: complex logic with multiple decision paths, many handlers on one aggregate, logic benefits from unit testing in isolation.

---

## Domain Event Structure

**Always include the aggregate ID as the first parameter:**

```csharp
// ✅ CORRECT — aggregate ID first
public sealed record BidPlaced(
    Guid ListingId,       // Always first — matches stream ID
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    bool IsProxyBid,
    DateTimeOffset PlacedAt);

// ❌ WRONG — missing aggregate ID
public sealed record BidPlaced(
    Guid BidId,           // Missing ListingId!
    decimal Amount);
```

**Why:** Marten inline projections expect the ID in event data. Events are self-documenting when viewed in isolation. Enables correlation in queries and diagnostics.

**Naming:** Past tense. `BidPlaced`, `BiddingOpened`, `ReserveMet` — not `PlaceBid`, `OpenBidding`.

---

## Projections

Marten supports four projection types:

### 1. Inline Snapshots

Aggregate state persisted as a JSON document in the same transaction as event append. Zero-lag read models for hot-path queries.

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

Read models derived from events across multiple streams, keyed differently than the source streams.

```csharp
// CatalogListingView: a discovery-optimized read model
// Combines events from Selling BC (ListingPublished) + Auctions BC (BiddingOpened, BidPlaced, BiddingClosed)
public sealed class CatalogListingViewProjection : MultiStreamProjection<CatalogListingView, Guid>
{
    public CatalogListingViewProjection()
    {
        Identity<ListingPublished>(x => x.ListingId);
        Identity<BiddingOpened>(x => x.ListingId);
        Identity<BidPlaced>(x => x.ListingId);
        Identity<BiddingClosed>(x => x.ListingId);
        Identity<ListingWithdrawn>(x => x.ListingId);
    }

    public CatalogListingView Create(ListingPublished evt) =>
        new()
        {
            Id = evt.ListingId,
            SellerId = evt.SellerId,
            Title = evt.Title,
            StartingBid = evt.StartingBid,
            Status = "Pending"
        };

    public static CatalogListingView Apply(CatalogListingView view, BiddingOpened evt) =>
        view with { Status = "Open", ClosesAt = evt.ScheduledCloseAt };

    public static CatalogListingView Apply(CatalogListingView view, BidPlaced evt) =>
        view with { CurrentHighBid = evt.Amount, BidCount = view.BidCount + 1 };

    public static CatalogListingView Apply(CatalogListingView view, BiddingClosed evt) =>
        view with { Status = "Closed", FinalPrice = evt.HammerPrice };
}
```

```csharp
opts.Projections.Add<CatalogListingViewProjection>(ProjectionLifecycle.Inline);
```

### 3. Async Projections

Processed by background daemon, not in the write transaction. For complex denormalized views, analytics, or high-write throughput.

```csharp
opts.Projections.Add<OperationsDashboardProjection>(ProjectionLifecycle.Async);
```

```csharp
.AddAsyncDaemon(DaemonMode.Solo)   // Single instance
// or
.AddAsyncDaemon(DaemonMode.HotCold) // Multi-instance with leader election
```

**Pros:** Low write latency, high throughput, complex cross-stream aggregation.
**Cons:** Eventual consistency lag, requires daemon monitoring.

### 4. Live Aggregation

Aggregate state computed by replaying events at query time. No persistence.

```csharp
var listing = await session.Events.AggregateStreamAsync<Listing>(listingId);
var listingAtTime = await session.Events.AggregateStreamAsync<Listing>(listingId,
    timestamp: DateTime.UtcNow.AddHours(-1));
```

Good for: testing, diagnostics, admin audit tools. Not for hot-path queries.

### Decision Matrix

| Requirement | Lifecycle |
|---|---|
| Zero-lag aggregate state queries | `Inline` snapshot |
| Read model keyed differently than source | `Inline` multi-stream |
| Complex denormalized / cross-BC view | `Async` |
| High write throughput | `Async` |
| Temporal "time travel" queries | `Live` |
| Testing | `Inline` or `Live` |

---

## Snapshot Strategies

Configure inline snapshots for any aggregate you need to query via LINQ. Without a snapshot, `session.Query<T>()` returns empty even when the event stream exists.

```csharp
// ✅ Makes aggregate queryable
opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);

// ✅ Async variant when write latency must be minimized
opts.Projections.Snapshot<HeavyAggregate>(SnapshotLifecycle.Async);
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

## Testing Event-Sourced Systems

### The Race Condition Problem

HTTP-based tests for event-sourced aggregates fail under load due to a timing issue: Wolverine's `AutoApplyTransactions()` commits the transaction asynchronously — the HTTP response returns *before* the commit completes. Subsequent reads see stale state.

```csharp
// ❌ Race condition — transaction may not be committed when GET fires
await _fixture.Host.Scenario(s => s.Post.Json(cmd).ToUrl(url));
await _fixture.Host.Scenario(s => s.Get.Url(url)); // Stale data!

// Task.Delay() is not a fix — timing-based solutions are inherently fragile
```

### ✅ The Fix: Direct Command Invocation

```csharp
[Fact]
public async Task Listing_WhenBidAccepted_UpdatesHighBid()
{
    var listingId = Guid.CreateVersion7();
    await _fixture.ExecuteAndWaitAsync(new PublishListing(listingId, ...));
    await _fixture.ExecuteAndWaitAsync(new OpenBidding(listingId, ...));

    // ✅ Direct invocation — waits for all Wolverine side effects
    await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, Guid.NewGuid(), bidderId, 50m));

    // ✅ Query event store directly — guaranteed to see committed state
    await using var session = _fixture.GetDocumentSession();
    var listing = await session.Events.AggregateStreamAsync<Listing>(listingId);

    listing.ShouldNotBeNull();
    listing.CurrentHighBid.ShouldBe(50m);
    listing.HighBidderId.ShouldBe(bidderId);
}
```

**TestFixture helper:**

```csharp
public async Task ExecuteAndWaitAsync(object command)
    => await _host.InvokeAsync(command);
// Wolverine ensures all transactions complete before returning

public IDocumentSession GetDocumentSession()
    => _host.Services.GetRequiredService<IDocumentStore>().LightweightSession();
```

**When to use each:**

| Test Type | Approach |
|---|---|
| Aggregate state transitions | Direct command invocation + query event store |
| HTTP endpoint routing / serialization | HTTP via Alba |
| Integration flows (events published to bus) | Direct command invocation |
| E2E tests | HTTP or Playwright |

---

## Event Versioning

### Default: Additive-Only Changes

New fields on events must be nullable or have defaults. Old events deserialize cleanly.

```csharp
// Version 1
public sealed record BidPlaced(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount);

// Version 2 — safe additive change
public sealed record BidPlaced(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount,
    bool IsProxyBid = false); // New optional field, defaults for old events
```

### Breaking Changes: Upcasting

For unavoidable breaking changes, register an upcaster in the BC's Marten configuration:

```csharp
opts.Events.Upcast<BidPlacedV1, BidPlaced>(v1 =>
    new BidPlaced(v1.ListingId, v1.BidId, v1.BidderId, v1.Amount, IsProxyBid: false));
```

Old events remain in the store unchanged; transformation happens at read time.

### Versioned Type Names (Last Resort)

`BidPlaced_V2` as a new type. Original retained for backward compatibility. Expensive — use additive changes or upcasting first.

**See also:** `docs/decisions/005-contract-versioning.md` for the CritterBids policy.

---

## Marten Configuration

### Standard BC Module Pattern

```csharp
// Inside AddXyzModule() extension method
services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.AutoCreateSchemaObjects = AutoCreate.All; // Development; use None in production
    opts.UseSystemTextJsonForSerialization(EnumStorage.AsString);

    // Schema isolation — one schema per BC
    opts.DatabaseSchemaName = "auctions"; // lowercase BC name

    opts.Events.StreamIdentity = StreamIdentity.AsGuid;

    // Inline snapshot projections
    opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);

    // Multi-stream projections
    opts.Projections.Add<CatalogListingViewProjection>(ProjectionLifecycle.Inline);

    // Document indexes for query performance
    opts.Schema.For<Listing>()
        .Identity(x => x.Id)
        .Index(x => x.SellerId);
})
.AddAsyncDaemon(DaemonMode.Solo)
.UseLightweightSessions()
.IntegrateWithWolverine();
```

### ⚠️ CRITICAL: `AutoApplyTransactions()` Is Non-Negotiable

Without `AutoApplyTransactions()` in the Wolverine configuration, handlers do not commit Marten changes. Silent failure — handlers return success, no data persists.

```csharp
// In the BC's module Wolverine configuration — REQUIRED IN EVERY BC
opts.Policies.AutoApplyTransactions();      // ← non-negotiable
opts.Policies.UseDurableLocalQueues();
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

**Diagnosis:** Handler returns HTTP 200, events table is empty, projections not updated, no exceptions thrown → check `AutoApplyTransactions()` is present.

### ⚠️ Do Not Call `SaveChangesAsync()` in Wolverine Handlers

`AutoApplyTransactions()` commits the session automatically after handler execution. Manual `SaveChangesAsync()` calls are redundant and misleading. Remove them.

### Session Types

```csharp
LightweightSession()    // Default — no change tracking, best performance
DirtyTrackedSession()   // Change tracking — rare, only when conditional saves needed
QuerySession()          // Read-only — HTTP GET endpoints
```

---

## Anti-Patterns to Avoid

### 1. ❌ Mutable State on Aggregates

```csharp
// ❌ WRONG
public sealed record Listing(Guid Id, List<Bid> Bids) // Mutable list!
{
    public void Apply(BidPlaced @event) { Bids.Add(...); } // Mutation!
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

```csharp
// ❌ WRONG
public Listing Apply(BidPlaced @event)
{
    if (@event.Amount <= 0) throw new InvalidOperationException(); // Validation in Apply!
    return this with { CurrentHighBid = @event.Amount };
}

// ✅ CORRECT — validation in Before(), Apply() only transforms state
public Listing Apply(BidPlaced @event) =>
    this with { CurrentHighBid = @event.Amount, HighBidderId = @event.BidderId };
```

### 3. ❌ Side Effects in Apply Methods

Apply methods are called during replay. Side effects (logging, HTTP calls, publishing) in Apply will fire on every projection rebuild.

### 4. ❌ Inconsistent `@event` Naming

Always use `@event`. Never `evt`, `e`, `domainEvent`, or any other variation.

### 5. ❌ Block Bodies When Expression Bodies Work

Use `=>` expression bodies for simple transforms. Block bodies only for complex logic with temporary variables.

### 6. ❌ Missing Aggregate ID in Events

Always include the aggregate ID as the first property. Events must be self-documenting and support projection correlation.

### 7. ❌ HTTP-Based Testing of Event-Sourced Aggregates

POST → immediate GET is a race condition. Use `ExecuteAndWaitAsync` + direct event store query. See [Testing](#testing-event-sourced-systems).

### 8. ❌ Missing Snapshot Projection for Queryable Aggregates

`session.Query<T>()` returns empty without a snapshot projection even when events exist. Always configure `opts.Projections.Snapshot<T>()` for any aggregate you query via LINQ.

### 9. ❌ Missing Apply Methods for Event Types in Stream ⚠️ CRITICAL

If an aggregate stream contains an event type for which no `Apply()` method exists, Marten returns `null` from `AggregateStreamAsync<T>()`. **Silent failure — no exception.** Every event type appended to a stream must have a corresponding `Apply()` method.

**Diagnosis:** `FetchStreamAsync()` shows events exist but `AggregateStreamAsync()` returns null → find the event type missing its `Apply()` method.

### 10. ❌ Missing `AutoApplyTransactions()`

Silent data loss. Handler returns success, nothing persisted. See [Marten Configuration](#marten-configuration).

### 11. ❌ DCB Boundary State Missing `Guid Id` Property

Marten registers boundary state classes as documents. Without `public Guid Id { get; set; }`, `DeleteAllDocumentsAsync()` throws during test cleanup causing cascading failures.

---

## Lessons Learned

**L1: Verify integration queue wiring end-to-end.** Unit tests and handler tests aren't enough. A BC can publish an event that never reaches the subscriber because the queue binding is missing. Add cross-BC smoke tests for all RabbitMQ queues.

**L2: Design integration events for all known consumers.** When an event carries too little data, every new consumer forces a contract expansion. Document all consumers when designing event payloads.

**L3: HTTP-based testing doesn't respect eventual consistency.** `POST → GET` race conditions appear intermittently on CI and under load. Use direct command invocation for state-changing operations. See [Testing](#testing-event-sourced-systems).

**L4: Sagas must handle all terminal states.** A saga that handles `ReturnCompleted` but not `ReturnRejected` leaves dangling state permanently. When adding integration events, verify ALL consumers handle ALL terminal states.

**L5: Document-based saga vs event-sourced aggregate.** Sagas are write-heavy, read-light. Event replay on every handler invocation degrades performance as message count grows. Use document store with numeric revisions for saga state. Use event sourcing for domain aggregates where history matters.

**L6: `AutoApplyTransactions()` is non-negotiable.** Its absence fails silently — handlers appear to work but persist nothing. Every Marten BC requires it. Added in every `AddXyzModule()` call without exception.

**L7: Don't call `SaveChangesAsync()` in Wolverine handlers.** `AutoApplyTransactions()` handles commits. Manual calls are misleading and redundant.

**L8: Incremental ES migration is safe; big-bang is not.** When migrating a document-store BC to event sourcing: define all events first, build the aggregate, build the projection, migrate handlers one at a time keeping tests green after each.

---

## References

- [Marten Event Sourcing](https://martendb.io/events/)
- [Marten Projections](https://martendb.io/events/projections/)
- [Wolverine Marten Integration](https://wolverinefx.net/guide/durability/marten/)
- [Decider Pattern](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)
- `docs/skills/dynamic-consistency-boundary.md`
- `docs/skills/wolverine-message-handlers.md`
- `docs/skills/wolverine-sagas.md`
