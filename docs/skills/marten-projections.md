# Marten Projections Reference

Complete reference for projection patterns available on Marten — both **native Marten projections** (JSONB documents in Postgres, projection daemon processes events from `mt_events` into `mt_doc_*` tables) and **EF Core projections** (relational tables via `Marten.EntityFrameworkCore`, for BI tooling and complex relational queries).

> **Scope note.** The native and EF Core halves of this file both live under the same `marten-projections.md` name today. That's deliberate — both are Marten projection mechanisms, just with different storage backends. If the file grows past ~35 KB or either half starts to dominate maintenance churn, a split into `marten-native-projections.md` + `marten-efcore-projections.md` is a reasonable future refactor. Not today.

> **CritterBids status:** EF Core projections are not yet implemented; native Marten projections (inline snapshots, multi-stream) are in active use. Update the corresponding section with concrete findings when the first EF Core projection lands.

---

## Table of Contents

### Native Marten Projections (JSONB documents)

1. [Single-Stream Projections](#single-stream-projections)
2. [Multi-Stream Projection Routing Patterns](#multi-stream-projection-routing-patterns)
3. [Composite Projections](#composite-projections)
4. [Event Enrichment](#event-enrichment)
5. [Flat Table Projections](#flat-table-projections)
6. [Handler-Driven Projections — Tolerant Upsert](#handler-driven-projections--tolerant-upsert)
7. [View Extension Across Milestones](#view-extension-across-milestones)
8. [Decision Guide — Which Projection Type?](#decision-guide--which-projection-type)

### EF Core Projections (relational tables)

9. [When to Use EF Core Projections](#when-to-use-ef-core-projections)
10. [The Three EF Core Projection Types](#the-three-ef-core-projection-types)
11. [Setup](#setup)
12. [DbContext Configuration](#dbcontext-configuration)
13. [Single-Stream (EF Core)](#single-stream-ef-core)
14. [Multi-Stream (EF Core)](#multi-stream-ef-core)
15. [Event Projections (EF Core)](#event-projections-ef-core)
16. [EF Core Registration](#ef-core-registration)
17. [Testing](#testing)
18. [Pitfalls](#pitfalls)
19. [Lessons Learned](#lessons-learned)
20. [How It Works Under the Hood](#how-it-works-under-the-hood)

---

# Native Marten Projections

Native Marten projections store projected documents as JSONB in PostgreSQL. The event-sourcing core (`marten-event-sourcing.md` §5) covers single-stream snapshots, inline vs async lifecycles, the `Apply` / `Evolve` / `DetermineAction` convention methods, and performance knobs (`IncludeType`, `CacheLimitPerTenant`, `BatchSize`). This file picks up from there with the **routing and composition** patterns: how to shape projections when events come from many streams, how to chain projections in stages, and how to batch reference-data lookups without N+1 queries.

---

## Single-Stream Projections

Covered in full in `marten-event-sourcing.md` §5 (inline snapshots via `opts.Projections.Snapshot<T>`, separate `SingleStreamProjection<T, TId>` classes, `Evolve`, `DetermineAction`, `RebuildSingleStreamAsync`). This section is the pointer — go there for single-stream content.

Key callouts for cross-referencing:

- `opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline)` is the CritterBids default for aggregates that need LINQ-queryable snapshots
- `session.Events.FetchLatest<T>(streamId)` is the preferred read path; `AggregateStreamAsync` is for time travel only
- Missing `Apply` methods for events in the stream return `null` silently — see Anti-Pattern #9 in `marten-event-sourcing.md`

---

## Multi-Stream Projection Routing Patterns

Multi-stream projections produce read models that aggregate events from multiple source streams. The defining feature is a routing rule — `Identity<TEvent>(x => x.SomeId)` — that tells Marten which document each event belongs to.

**Default lifecycle is async.** Multi-stream projections default to the async daemon because inline multi-stream can create contention under heavy load (multiple streams racing to update the same projection document in the same transaction).

### Basic routing: one event, one document

```csharp
public sealed class SellerActivityProjection : MultiStreamProjection<SellerActivity, Guid>
{
    public SellerActivityProjection()
    {
        // Route events to the document keyed by SellerId
        Identity<ListingPublished>(x => x.SellerId);
        Identity<ListingSold>(x => x.SellerId);
        Identity<ListingWithdrawn>(x => x.SellerId);
    }

    public SellerActivity Create(ListingPublished e)
        => new SellerActivity { Id = e.SellerId, ListingCount = 1 };

    public void Apply(ListingPublished e, SellerActivity view) => view.ListingCount++;
    public void Apply(ListingSold e, SellerActivity view) => view.SoldCount++;
    public void Apply(ListingWithdrawn e, SellerActivity view) => view.WithdrawnCount++;
}
```

### Common-interface routing

When many event types share a routing key, route by interface instead of enumerating each event:

```csharp
public interface IListingEvent { Guid ListingId { get; } }

public sealed record ListingPublished(Guid ListingId, Guid SellerId, ...) : IListingEvent;
public sealed record BidPlaced(Guid ListingId, Guid BidderId, decimal Amount) : IListingEvent;
public sealed record BiddingClosed(Guid ListingId, ...) : IListingEvent;

public sealed class ListingAuditProjection : MultiStreamProjection<ListingAudit, Guid>
{
    public ListingAuditProjection()
    {
        // Applies to every event implementing IListingEvent
        Identity<IListingEvent>(x => x.ListingId);
    }
}
```

### Fan-out: one event, many documents

`Identities<T>` (plural) routes a single event to multiple documents. Useful when an event carries a collection of entity IDs and each needs its own projection update:

```csharp
public sealed record FeaturedListingsPublished(IReadOnlyList<Guid> ListingIds, DateTimeOffset FeaturedAt);

public sealed class ListingFeatureProjection : MultiStreamProjection<ListingFeatureStatus, Guid>
{
    public ListingFeatureProjection()
    {
        // Each ListingId in the collection gets its own document updated
        Identities<IEvent<FeaturedListingsPublished>>(x => x.Data.ListingIds);
    }

    public void Apply(FeaturedListingsPublished e, ListingFeatureStatus view)
    {
        view.IsFeatured = true;
        view.FeaturedAt = e.FeaturedAt;
    }
}
```

### Time-based segmentation

Multi-stream identity rules can use event timestamps (via `IEvent<T>`) to segment a single stream into time-bucketed documents. Good fit for daily/monthly operations dashboards:

```csharp
public sealed class DailyBidActivity
{
    public string Id { get; set; } = "";  // composite: "{listingId}:{yyyy-MM-dd}"
    public Guid ListingId { get; set; }
    public DateOnly Day { get; set; }
    public int BidCount { get; set; }
    public decimal TotalBidVolume { get; set; }
    public decimal HighestBid { get; set; }
}

public sealed class DailyBidActivityProjection : MultiStreamProjection<DailyBidActivity, string>
{
    public DailyBidActivityProjection()
    {
        // Compose document ID from stream ID + calendar date
        Identity<IEvent<BidPlaced>>(e =>
            $"{e.StreamId}:{e.Timestamp:yyyy-MM-dd}");
    }

    public DailyBidActivity Create(IEvent<BidPlaced> e) => new()
    {
        Id = $"{e.StreamId}:{e.Timestamp:yyyy-MM-dd}",
        ListingId = e.StreamId,
        Day = DateOnly.FromDateTime(e.Timestamp.Date),
        BidCount = 1,
        TotalBidVolume = e.Data.Amount,
        HighestBid = e.Data.Amount
    };

    public void Apply(IEvent<BidPlaced> e, DailyBidActivity view)
    {
        view.BidCount++;
        view.TotalBidVolume += e.Data.Amount;
        if (e.Data.Amount > view.HighestBid)
            view.HighestBid = e.Data.Amount;
    }
}
```

The same listing stream now produces a separate document per day of bidding activity — queryable by listing, by day, or both.

### Fan-out with `FanOut<TParent, TChild>()`

When an event carries a collection that should be processed as individual child events:

```csharp
public sealed class DailyOpsBoardProjection : MultiStreamProjection<DailyOpsBoard, DateOnly>
{
    public DailyOpsBoardProjection()
    {
        Identity<IEvent<BatchSettlementProcessed>>(e =>
            DateOnly.FromDateTime(e.Timestamp.Date));

        // Each SettlementLine in the event is processed as its own event
        FanOut<BatchSettlementProcessed, SettlementLine>(x => x.Lines);
    }

    public void Apply(DailyOpsBoard view, SettlementLine line)
    {
        view.SettlementCount++;
        view.SettlementTotal += line.Amount;
    }
}
```

### Custom groupers (DB lookups for routing)

When the aggregate ID for routing isn't on the event itself and must be looked up, implement `IAggregateGrouper<TId>`:

```csharp
public class ListingToSellerGrouper : IAggregateGrouper<Guid>
{
    public async Task Group(
        IQuerySession session,
        IEnumerable<IEvent> events,
        IEventGrouping<Guid> grouping)
    {
        var bidEvents = events.OfType<IEvent<BidPlaced>>().ToList();
        if (!bidEvents.Any()) return;

        // Look up seller IDs for the listings in this event batch
        var listingIds = bidEvents.Select(e => e.Data.ListingId).Distinct().ToList();
        var sellerLookup = await session.Query<Listing>()
            .Where(x => listingIds.Contains(x.Id))
            .Select(x => new { x.Id, x.SellerId })
            .ToListAsync();

        var streamIds = sellerLookup
            .GroupBy(x => x.Id, x => x.SellerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        grouping.AddEvents<BidPlaced>(e => streamIds[e.ListingId], bidEvents);
    }
}
```

**Important constraint:** custom groupers **cannot read the projected document itself** — grouping and document building run in parallel, so reads are stale. If a grouper needs the current projection state for routing, it's a sign the architecture is wrong — consider an auxiliary inline projection the grouper can safely read, or restructure the event model.

### `RollUpByTenant()` — per-tenant summary

For projections that aggregate all events within a tenant into one summary document:

```csharp
public class TenantSummaryProjection : MultiStreamProjection<TenantSummary, string>
{
    public TenantSummaryProjection()
    {
        RollUpByTenant();
    }

    public void Apply(TenantSummary view, ListingPublished e) => view.TotalListings++;
    public void Apply(TenantSummary view, ListingSold e) => view.TotalSold++;
}
```

Requires `opts.Events.EnableGlobalProjectionsForConjoinedTenancy = true`. CritterBids is single-tenant so this recipe isn't currently in use, but it's the first pattern to reach for if a multi-tenant variant is ever introduced.

---

## Composite Projections

Composite projections group multiple projections into ordered stages. Stage 1 projections complete before stage 2 begins, letting downstream projections consume the **outputs** of upstream projections via **synthetic events**.

**Composite projections always run async** — they require the projection daemon. The benefit is single-read efficiency: the event batch is read once and shared across all stages.

### Configuration

```csharp
opts.Projections.CompositeProjectionFor("AuctionDashboard", projection =>
{
    // Stage 1 — base projections
    projection.Add<ListingStatusProjection>();
    projection.Add<BidActivityProjection>();
    projection.Snapshot<Listing>();

    // Stage 2 — depends on stage 1 outputs
    projection.Add<AuctionBoardSummaryProjection>(2);
    projection.Add<SellerDashboardProjection>(2);
});
```

The `(2)` second argument declares the stage. Anything without a stage number defaults to stage 1.

### Synthetic events

When a stage N projection creates, updates, or deletes a document, the composite emits synthetic events that stage N+1 projections can handle:

| Synthetic event | When emitted | Payload |
|---|---|---|
| `Updated<T>` | Upstream projection created or updated a document | Full document snapshot |
| `ProjectionDeleted<TDoc, TId>` | Upstream projection deleted a document | Document type and ID |
| `References<T>` | A projection attached a reference via `slice.Reference()` | The reference document |

Downstream projections handle these synthetic events with the same `Apply` / `Identity` convention as any other event:

```csharp
public class AuctionBoardSummaryProjection
    : MultiStreamProjection<AuctionBoardSummary, DateOnly>
{
    public AuctionBoardSummaryProjection()
    {
        // Route based on the data inside the upstream projection's snapshot
        Identity<Updated<Listing>>(x =>
            DateOnly.FromDateTime(x.Document.PublishedAt.Date));
        Identity<Updated<BidActivity>>(x =>
            DateOnly.FromDateTime(x.Document.LastBidAt.Date));
    }

    public void Apply(Updated<Listing> e, AuctionBoardSummary view)
    {
        // e.Document is the full Listing snapshot from the upstream projection
        if (e.Document.Status == ListingStatus.Open)
            view.OpenListings++;
    }

    public void Apply(Updated<BidActivity> e, AuctionBoardSummary view)
    {
        view.TotalBidVolume += e.Document.TotalVolume;
    }
}
```

The downstream projection doesn't need to re-query `Listing` or `BidActivity` — the composite hands it the snapshots directly.

### Rebuilding

Composites rebuild as a unit — you cannot rebuild one constituent projection within a composite independently:

```csharp
await daemon.RebuildProjectionAsync("AuctionDashboard", CancellationToken.None);
```

### When to reach for composite

Composite projections are appropriate when **a projection genuinely needs the output of another projection**. Signs you want one:

- A summary view that combines two or more read models (dashboard over listings + bids + sellers)
- A derived projection that would otherwise ad-hoc query upstream projections during Apply
- Multiple related projections for the same domain where single-read efficiency matters

If a projection just needs reference data (user names, product lookups), composite is overkill — use **event enrichment** instead.

---

## Event Enrichment

Event enrichment batches reference-data lookups before projection `Apply` methods run. It replaces the N+1 query pattern of resolving reference data inside `Apply` with a single batched load per daemon batch.

### The N+1 problem

```csharp
// ❌ WRONG — one LoadAsync per event in every batch
public async Task Apply(BidPlaced e, IQuerySession session, BidSummary view)
{
    var bidder = await session.LoadAsync<Bidder>(e.BidderId);  // N+1 query
    view.TopBidderName = bidder.DisplayName;
}
```

When a daemon batch contains hundreds of `BidPlaced` events, this generates hundreds of individual queries — each adding latency and load.

### `EnrichEventsAsync` — batch-loading pattern

Override `EnrichEventsAsync` on the projection. Marten calls it once per slice group before `Apply` methods run, giving you a single place to batch-load everything the slice needs:

```csharp
public class BidSummaryProjection : SingleStreamProjection<BidSummary, Guid>
{
    public override async Task EnrichEventsAsync(
        SliceGroup<BidSummary, Guid> group,
        IQuerySession querySession,
        CancellationToken cancellation)
    {
        // 1. Collect every BidderId across all events in the batch
        var bidEvents = group.Slices
            .SelectMany(s => s.Events().OfType<IEvent<BidPlaced>>())
            .ToArray();

        if (!bidEvents.Any()) return;

        // 2. Single batch query for all of them
        var bidderIds = bidEvents.Select(e => e.Data.BidderId).Distinct().ToArray();
        var bidders = await querySession.LoadManyAsync<Bidder>(cancellation, bidderIds);
        var lookup = bidders.ToDictionary(x => x.Id);

        // 3. Attach resolved data to events
        foreach (var e in bidEvents)
        {
            if (lookup.TryGetValue(e.Data.BidderId, out var bidder))
                e.Data.ResolvedBidder = bidder;  // requires a settable property on the event
        }
    }

    public void Apply(BidPlaced e, BidSummary view)
    {
        // No async query needed — enrichment already happened
        view.TopBidderName = e.ResolvedBidder?.DisplayName ?? "Unknown";
    }
}
```

**One query for the whole batch**, regardless of how many events it contains.

### Declarative enrichment API (Marten 8.18+)

The fluent API is cleaner when you want to replace event data or attach references:

```csharp
public override async Task EnrichEventsAsync(
    SliceGroup<BidSummary, Guid> group,
    IQuerySession querySession,
    CancellationToken cancellation)
{
    await group
        .EnrichWith<Bidder>()
        .ForEvent<BidPlaced>()
        .ForEntityId(e => e.BidderId)
        .EnrichAsync((slice, e, bidder) =>
        {
            // Replace the raw event with an enriched wrapper
            slice.ReplaceEvent(e, new EnhancedBidPlaced(e.Data.Amount, bidder));
        });
}
```

Key methods:

- `EnrichWith<TReference>()` — declare the reference type
- `ForEvent<TEvent>()` — which event triggers enrichment
- `ForEntityId(e => e.PropertyId)` — extract the lookup ID
- `EnrichAsync((slice, event, reference) => ...)` — what to do with the resolved data
- `ReplaceEvent(original, replacement)` — swap an event for an enriched version
- `Reference(doc)` — attach a reference to the slice for downstream consumers

### Important limitation: `FetchLatest` does not run enrichment

`EnrichEventsAsync` runs **only in the async daemon's projection pipeline**. It does **not** fire during `FetchForWriting` or `FetchLatest` live aggregations. Aggregate write models must resolve reference data in the command handler, not lean on projection enrichment.

```csharp
// ❌ WRONG — enriched data is absent in live aggregation path
var bidSummary = await session.Events.FetchLatest<BidSummary>(listingId);
// bidSummary.TopBidderName is empty if it was only set via enrichment

// ✅ CORRECT — resolve reference data in the handler, not in the aggregate
```

### Business-key resolution (Marten 8.22+)

When the lookup key isn't a document ID but a natural business key, `EnrichUsingEntityQuery` lets you run a custom query per slice:

```csharp
await group
    .EnrichWith<Marketplace>()
    .ForEvent<ListingPublished>()
    .EnrichUsingEntityQuery<string>(async (slices, events, cache, ct) =>
    {
        var marketplaceCodes = events.Select(e => e.Data.MarketplaceCode).Distinct().ToArray();
        var marketplaces = await querySession.Query<Marketplace>()
            .Where(m => marketplaceCodes.Contains(m.Code))
            .ToListAsync(ct);
        var lookup = marketplaces.ToDictionary(m => m.Code);

        foreach (var slice in slices)
        foreach (var e in slice.Events().OfType<IEvent<ListingPublished>>())
            if (lookup.TryGetValue(e.Data.MarketplaceCode, out var m))
                slice.Reference(m);
    }, cancellation);
```

---

## Flat Table Projections

For read models that should land in **SQL-friendly relational tables** rather than JSONB documents. Two shapes: declarative (`FlatTableProjection`) and raw-SQL (`EventProjection`).

### `FlatTableProjection` — declarative upsert-style mapping

Marten generates PostgreSQL upsert statements from a fluent mapping DSL:

```csharp
public class DailyBidVolumeProjection : FlatTableProjection
{
    public DailyBidVolumeProjection() : base("daily_bid_volume", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();

        Options.TeardownDataOnRebuild = true;

        Project<BidPlaced>(map =>
        {
            map.Map(x => x.ListingId);              // → listing_id column
            map.Map(x => x.Amount, "bid_amount");    // → bid_amount column
            map.Increment("bid_count");              // bid_count += 1
            map.Increment(x => x.Amount, "total_volume");  // total_volume += Amount
            map.SetValue("last_event_type", "BidPlaced");
        });

        Project<BiddingClosed>(map =>
        {
            map.Map(x => x.HammerPrice, "final_price");
            map.SetValue("status", "closed");
        });

        Delete<ListingWithdrawn>();  // deletes the row
    }
}
```

**Key capabilities:**

- **Marten manages the schema** (creates, migrates via Weasel)
- **Upsert semantics** — robust to out-of-order events
- **`Increment()`** for counter columns
- **`SetValue()`** for literal values
- **`Delete<T>()`** for row deletion
- **`TeardownDataOnRebuild`** clears the table before rebuild

**Limitation:** cannot access event metadata (timestamp, sequence, stream ID) from within the mapping. If you need those, use `EventProjection` with raw SQL (below).

### `EventProjection` with raw SQL — full metadata access

For cases where `FlatTableProjection`'s declarative mapping isn't enough (joins, subqueries, metadata access, complex conditionals):

```csharp
public class BidAuditProjection : EventProjection
{
    public BidAuditProjection()
    {
        var table = new Table("bid_audit");
        table.AddColumn<Guid>("id").AsPrimaryKey();
        table.AddColumn<Guid>("listing_id").NotNull();
        table.AddColumn<Guid>("bidder_id").NotNull();
        table.AddColumn<decimal>("amount").NotNull();
        table.AddColumn<long>("event_sequence").NotNull();
        table.AddColumn<DateTimeOffset>("bid_at").NotNull();
        SchemaObjects.Add(table);

        Options.DeleteDataInTableOnTeardown(table.Identifier);
    }

    // IEvent<T> gives access to stream id, sequence, timestamps
    public void Project(IEvent<BidPlaced> e, IDocumentOperations ops)
    {
        ops.QueueSqlCommand(
            "INSERT INTO bid_audit (id, listing_id, bidder_id, amount, event_sequence, bid_at) " +
            "VALUES (?, ?, ?, ?, ?, ?)",
            Guid.NewGuid(), e.StreamId, e.Data.BidderId, e.Data.Amount, e.Sequence, e.Timestamp);
    }

    public void Project(IEvent<BidRejected> e, IDocumentOperations ops)
    {
        // More complex SQL with a lookup
        ops.QueueSqlCommand(
            "DELETE FROM bid_audit WHERE listing_id = ? AND bidder_id = ? AND amount = ?",
            e.Data.ListingId, e.Data.BidderId, e.Data.Amount);
    }
}
```

`QueueSqlCommand` batches SQL into the session's unit of work — commands are sent in one round-trip with the event append.

### When to reach for flat-table projections

| Need | Use |
|---|---|
| Simple column-per-property mapping, upsert semantics | `FlatTableProjection` |
| Counter/aggregate columns with increment semantics | `FlatTableProjection` |
| Event metadata (sequence, timestamp, stream ID) in SQL | `EventProjection` with raw SQL |
| Complex SQL (joins, CTEs, subqueries) | `EventProjection` with raw SQL |
| Read model feeds Power BI / BI tooling | Either, or EF Core projections (see below) |

CritterBids' future Operations BC dashboards are the most likely first adopter — daily bid volume, settlement totals, seller performance rollups — where the consumer is reporting tooling and flat tables are a better fit than JSONB.

---

## Handler-Driven Projections — Tolerant Upsert

Native `MultiStreamProjection<T, TId>` is the right tool when the projection's **routing rule is a property on the event**. When the projection is driven by cross-BC integration events handled by a Wolverine handler — where the handler also needs to call domain services, schedule messages, or emit further events — the projection is updated from inside the handler instead. The store-side pattern for that handler is a **tolerant upsert**.

```csharp
public static async Task Handle(
    BiddingOpened integration,
    IDocumentSession session,
    CancellationToken cancellationToken)
{
    var view = await session.LoadAsync<CatalogListingView>(integration.ListingId, cancellationToken)
               ?? new CatalogListingView { Id = integration.ListingId };

    view.AuctionStatus = CatalogAuctionStatus.Live;
    view.ScheduledCloseAt = integration.ScheduledCloseAt;

    session.Store(view);
    // CommitAsync is driven by Wolverine auto-transactions — do not call it here.
}
```

**The primitive:** `LoadAsync ?? new T { Id = ... }`. Marten's `IQuerySession.LoadAsync<T>(Guid id)` returns `Task<T?>` — `null` when the document doesn't yet exist. Defaulting to `new T` gives you a single code path that covers first-touch (insert) and subsequent-touch (update) without a separate `StoreAsync` vs `UpdateAsync` split.

**Why tolerant.** Cross-BC integration events can arrive in any order depending on queue scheduling, retry policy, and deployment order. A handler in BC Y consuming an event from BC X cannot assume that BC X's inline snapshot has already flowed out to BC Y's view. If `Listing` inline snapshots fail to reach `CatalogListingView` first, a `BiddingOpened` handler that assumed `view != null` would throw `NullReferenceException` under the first real queue-order race. Tolerant upsert makes arrival order irrelevant.

**When to prefer this over a native `MultiStreamProjection`:**

| Situation | Reach for |
|---|---|
| Routing purely by `event.Id` property, no external calls | **Native `MultiStreamProjection<T, TId>`** |
| Handler also schedules messages / calls domain services / emits further events | **Handler-driven tolerant upsert** |
| Events originate from another BC over the message bus | **Handler-driven tolerant upsert** |
| Read model combines events from multiple BCs' contracts | **Handler-driven tolerant upsert** (one handler class per source BC) |

**Citation:** Marten source `src/Marten/IQuerySession.cs:169` — `Task<T?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : notnull;`. The nullable return type is the whole API contract you lean on.

**In-repo ground:** `src/CritterBids.Listings/ProjectionHandlers/AuctionStatusHandler.cs` (authored M3-S6) and its `ListingSnapshotHandler` sibling — both handle `CatalogListingView` upserts from foreign-BC cascades. See retrospective `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` §"LoadAsync ?? new" for the arrival-order failure mode that motivated the pattern.

---

## View Extension Across Milestones

Read models accumulate fields across milestones as new BCs start contributing. The naïve structural response — a new view per source BC (`CatalogListingCore`, `CatalogListingAuctionStatus`, `CatalogListingSettlement`) — fragments the read model and forces every UI query into a multi-document join.

**The shape rule (M3-D2 Path A):** **one view per logical entity; one sibling handler class per event-source BC; fields grow additively on the shared view across milestones.**

```
src/CritterBids.Listings/
├── CatalogListingView.cs                      ← single view, all fields
└── ProjectionHandlers/
    ├── ListingSnapshotHandler.cs              ← M2-S7: consumes ListingPublished,
    │                                              ListingUpdated, ListingWithdrawn
    ├── AuctionStatusHandler.cs                ← M3-S6: consumes BiddingOpened,
    │                                              BiddingClosed, ListingSold,
    │                                              ListingPassed
    ├── SettlementStatusHandler.cs             ← M4 (planned): PaymentCaptured,
    │                                              PayoutReleased fields
    └── ObligationsStatusHandler.cs            ← M5 (planned): ShippedOn,
                                                   TrackingNumber, etc.
```

Each handler class owns a disjoint field set on the same `CatalogListingView`. No handler reads fields owned by another handler. Additions are purely additive — existing handlers never need to change when a new milestone lands.

```csharp
// CatalogListingView.cs — grows additively; no fields removed across milestones
public sealed class CatalogListingView
{
    // M2-S7 — ListingSnapshotHandler
    public required Guid Id { get; set; }
    public required Guid SellerId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required decimal StartPrice { get; set; }
    public string[] Categories { get; set; } = [];

    // M3-S6 — AuctionStatusHandler
    public CatalogAuctionStatus AuctionStatus { get; set; }
    public DateTimeOffset? ScheduledCloseAt { get; set; }
    public decimal? HammerPrice { get; set; }
    public Guid? WinnerId { get; set; }
    public int BidCount { get; set; }
    public string? ClosedReason { get; set; }

    // M4 planned — SettlementStatusHandler
    // public SettlementStatus SettlementStatus { get; set; }
    // public DateTimeOffset? PaidAt { get; set; }
}
```

**Why not a native `MultiStreamProjection`?** Because the source events are **integration contracts from multiple BCs**, not streams in the projecting BC. Native projections route by streams visible to the projecting store; integration events flow through the message bus and land in handlers. See the previous section for the handler-driven tolerant upsert primitive these handler classes build on.

**Rejected alternatives (M3-D2 review):**
- **Path B — one view per source BC with a UI-side join.** Rejected: pushes the join into every read path, and the set of source BCs grows every milestone.
- **Path C — inline snapshot of `CatalogListingView` via native composition.** Rejected: the view aggregates cross-BC events that the Listings store does not own; Marten cannot route events it does not see.

**Decision boundary — when this shape applies.** Use the one-view/sibling-handlers shape when: (a) the read model is a per-entity rollup; (b) fields originate from two or more BCs; (c) every UI query against the view wants all fields in one round trip. When the read model is genuinely per-BC (e.g. `SellerActivity` summarizing a seller's own listings), a native `MultiStreamProjection` owned by that BC is still the right tool.

**In-repo ground:** `CatalogListingView` established at M2-S7 by `ListingSnapshotHandler`, extended at M3-S6 by `AuctionStatusHandler` with the six auction-status fields listed above. See retrospective `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` §"M3-D2 resolution" for the Path A decision record.

---

## Decision Guide — Which Projection Type?

| Situation | Projection type |
|---|---|
| One stream → one document, JSONB storage | **Single-stream snapshot** (inline or async) — see `marten-event-sourcing.md` §5 |
| Events from multiple streams → one document per entity, JSONB | **`MultiStreamProjection<T, TId>`** with `Identity<TEvent>(x => ...)` |
| One event updates many documents, JSONB | **`MultiStreamProjection` with `Identities<T>`** |
| Routing requires DB lookup, JSONB | **`MultiStreamProjection` with custom `IAggregateGrouper<TId>`** |
| Per-tenant rollup, JSONB | **`MultiStreamProjection` with `RollUpByTenant()`** |
| Events carry collections that project individually | **`FanOut<TParent, TChild>()`** |
| Reference data needed in projections | **Event enrichment** via `EnrichEventsAsync` |
| Stage 2 projection consumes stage 1 output | **Composite projection** with `Updated<T>` synthetic events |
| Read model for reporting tools (Power BI, SSRS) in flat tables | **`FlatTableProjection`** (simple mapping) or **`EventProjection`** (raw SQL) |
| Read model for complex relational queries via EF Core LINQ | **EF Core projection** (see second half of this file) |
| Dual-store output (Postgres JSONB + EF Core table) | **`EfCoreEventProjection`** (EF Core section) |
| Read model driven by cross-BC integration events (handler also schedules / calls services) | **Handler-driven tolerant upsert** — `LoadAsync ?? new` pattern (§6) |
| Read model accumulates fields across milestones from two or more source BCs | **One view, sibling handler classes per source BC** (§7) |

---

# EF Core Projections from Marten Events

Patterns for using Entity Framework Core as a projection target for Marten's event store — bridging event sourcing with relational read models. Marten's event store remains the system of record; EF Core handles the read-side tables.

---

## When to Use EF Core Projections

EF Core projections let you write event-sourced aggregates to relational tables via Entity Framework Core, while Marten's event store remains the system of record.

✅ **Use EF Core projections when:**

- **Relational queries are complex** — joins, aggregations, and filtering are more ergonomic in EF Core LINQ than Marten's JSONB queries. Example: seller performance across all listings, joined with settlement totals and bid counts.
- **BI tooling requires SQL tables** — Power BI, SSRS, and SQL Server Management Studio query EF Core tables directly. This is the primary motivation for EF Core projections in CritterBids' Operations and Settlement BCs.
- **Cross-database compatibility** — the same projection code works with Marten (PostgreSQL) and Polecat (SQL Server) with only the `DbContext` provider changing.
- **Multi-stream aggregation with complex identity mapping** — `EfCoreMultiStreamProjection` is natural when many event streams contribute to one denormalized entity.

❌ **Do NOT use EF Core projections when:**

- Marten's native projections (`SingleStreamProjection<T>`, inline snapshots) are sufficient — fewer moving parts.
- Queries are simple document lookups by ID.
- Performance is critical on PostgreSQL — Marten's native JSONB projections have lower overhead than EF Core change tracking.

### CritterBids Use Cases

| BC | Use Case | Projection Type |
|---|---|---|
| Operations | Live lot board, seller activity summary, bid counts by listing | Multi-stream |
| Settlement | Financial summary by seller, fee totals by period | Multi-stream |
| Listings | Browse catalog with complex filters | Single-stream |

---

## The Three EF Core Projection Types

| Base Class | Use Case | Model |
|---|---|---|
| `EfCoreSingleStreamProjection<TDoc, TId, TDbContext>` | One event stream → one entity | Listing stream → `ListingBidSummary` |
| `EfCoreMultiStreamProjection<TDoc, TId, TDbContext>` | Many streams → one entity | All listing streams → `SellerPerformance` |
| `EfCoreEventProjection<TDbContext>` | React to events, write to EF Core + Marten atomically | `AuctionDashboardView` with dual-store |

---

## Setup

```bash
dotnet add package Marten.EntityFrameworkCore
# For Polecat BCs (SQL Server):
dotnet add package Polecat.EntityFrameworkCore
```

Add the EF Core provider:
- PostgreSQL (Marten BCs): `Npgsql.EntityFrameworkCore.PostgreSQL`
- SQL Server (Polecat BCs): `Microsoft.EntityFrameworkCore.SqlServer`

Weasel (Marten's schema migration engine) automatically migrates EF Core entity tables alongside Marten's own schema on startup — no `dotnet ef database update` required.

---

## DbContext Configuration

```csharp
namespace CritterBids.Operations.Projections;

public class OperationsProjectionDbContext : DbContext
{
    public OperationsProjectionDbContext(
        DbContextOptions<OperationsProjectionDbContext> options) : base(options) { }

    public DbSet<ListingBidSummary> ListingBidSummaries => Set<ListingBidSummary>();
    public DbSet<SellerPerformance> SellerPerformances => Set<SellerPerformance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ListingBidSummary>(entity =>
        {
            entity.ToTable("listing_bid_summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SellerId).HasColumnName("seller_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.CurrentHighBid).HasColumnName("current_high_bid");
            entity.Property(e => e.BidCount).HasColumnName("bid_count");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.ClosedAt).HasColumnName("closed_at");

            entity.HasIndex(e => e.SellerId);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<SellerPerformance>(entity =>
        {
            entity.ToTable("seller_performances");
            entity.HasKey(e => e.Id); // SellerId
            entity.Property(e => e.TotalListings).HasColumnName("total_listings");
            entity.Property(e => e.TotalSold).HasColumnName("total_sold");
            entity.Property(e => e.TotalRevenue).HasColumnName("total_revenue");
            entity.Property(e => e.AverageBidsPerListing).HasColumnName("avg_bids_per_listing");
        });
    }
}
```

**Conventions:** snake_case table and column names to match Marten. Define all mappings explicitly — no convention-based inference. Add indexes for queries you know will be common.

Register in the BC's `AddXyzModule()`:

```csharp
services.AddDbContext<OperationsProjectionDbContext>(options =>
    options.UseNpgsql(connectionString));
// or for Polecat BCs:
//    options.UseSqlServer(connectionString));
```

---

## Single-Stream (EF Core)

One listing's event stream → one `ListingBidSummary` entity.

```csharp
public sealed class ListingBidSummary
{
    public Guid Id { get; set; }           // ListingId
    public Guid SellerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal StartingBid { get; set; }
    public decimal CurrentHighBid { get; set; }
    public int BidCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal? HammerPrice { get; set; }
}

public sealed class ListingBidSummaryProjection
    : EfCoreSingleStreamProjection<ListingBidSummary, Guid, OperationsProjectionDbContext>
{
    public override ListingBidSummary? ApplyEvent(
        ListingBidSummary? snapshot,
        Guid identity,
        IEvent @event,
        OperationsProjectionDbContext dbContext,
        IQuerySession session)
    {
        switch (@event.Data)
        {
            case ListingPublished published:
                return new ListingBidSummary
                {
                    Id = published.ListingId,
                    SellerId = published.SellerId,
                    Title = published.Title,
                    StartingBid = published.StartingBid,
                    CurrentHighBid = 0m,
                    BidCount = 0,
                    Status = "Pending"
                };

            case BiddingOpened:
                if (snapshot != null) snapshot.Status = "Open";
                return snapshot;

            case BidPlaced placed:
                if (snapshot != null)
                {
                    snapshot.CurrentHighBid = placed.Amount;
                    snapshot.BidCount++;
                }
                return snapshot;

            case ListingSold sold:
                if (snapshot != null)
                {
                    snapshot.Status = "Sold";
                    snapshot.HammerPrice = sold.HammerPrice;
                    snapshot.ClosedAt = sold.SoldAt;
                }
                return snapshot;

            case ListingPassed passed:
                if (snapshot != null)
                {
                    snapshot.Status = "Passed";
                    snapshot.ClosedAt = passed.PassedAt;
                }
                return snapshot;

            case ListingWithdrawn:
                if (snapshot != null) snapshot.Status = "Withdrawn";
                return snapshot;
        }

        return snapshot;
    }
}
```

**Pattern notes:**
- First event (stream creation) returns a **new** entity
- Subsequent events mutate the snapshot and return it
- Returning `null` deletes the entity — rarely needed
- EF Core change tracking determines insert vs. update automatically

---

## Multi-Stream (EF Core)

Many listing streams → one `SellerPerformance` entity per seller.

```csharp
public sealed class SellerPerformance
{
    public Guid Id { get; set; }           // SellerId
    public int TotalListings { get; set; }
    public int TotalSold { get; set; }
    public int TotalPassed { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal AverageBidsPerListing { get; set; }
    private int _totalBids;

    public void RecalculateAverage() =>
        AverageBidsPerListing = TotalListings > 0
            ? (decimal)_totalBids / TotalListings
            : 0m;
}

public sealed class SellerPerformanceProjection
    : EfCoreMultiStreamProjection<SellerPerformance, Guid, OperationsProjectionDbContext>
{
    public SellerPerformanceProjection()
    {
        // Map events to SellerId (the aggregate identity for this projection)
        Identity<ListingPublished>(e => e.SellerId);
        Identity<ListingSold>(e => e.SellerId);
        Identity<ListingPassed>(e => e.SellerId);
        Identity<BidPlaced>(e => e.SellerId);
    }

    public override SellerPerformance? ApplyEvent(
        SellerPerformance? snapshot,
        Guid identity,  // SellerId
        IEvent @event,
        OperationsProjectionDbContext dbContext)
    {
        snapshot ??= new SellerPerformance { Id = identity };

        switch (@event.Data)
        {
            case ListingPublished: snapshot.TotalListings++; break;
            case BidPlaced: /* track bids, recalc average */ break;
            case ListingSold sold:
                snapshot.TotalSold++;
                snapshot.TotalRevenue += sold.HammerPrice;
                break;
            case ListingPassed: snapshot.TotalPassed++; break;
        }

        return snapshot;
    }
}
```

**Key differences from single-stream:**
- `Identity<TEvent>()` calls in the constructor map events to aggregate ID
- The `identity` parameter is the mapped ID (SellerId), not the stream ID (ListingId)
- `snapshot ??= new SellerPerformance { Id = identity }` initializes on first event

---

## Event Projections (EF Core)

React to individual events and write to **both** EF Core entities and Marten documents atomically. Use when you need dual-store — one relational (for BI/reporting) and one document (for internal queries).

```csharp
public sealed class AuctionDashboardProjection
    : EfCoreEventProjection<OperationsProjectionDbContext>
{
    protected override async Task ProjectAsync(
        IEvent @event,
        OperationsProjectionDbContext dbContext,
        IDocumentOperations operations,
        CancellationToken token)
    {
        if (@event.Data is ListingSold sold)
        {
            // Write to SQL Server table (for Power BI)
            dbContext.SalesRecords.Add(new SalesRecord
            {
                Id = Guid.CreateVersion7(),
                ListingId = sold.ListingId,
                SellerId = sold.SellerId,
                WinnerId = sold.WinnerId,
                HammerPrice = sold.HammerPrice,
                SoldAt = sold.SoldAt
            });

            // Also write to Marten document (for internal analytics)
            var analytics = await operations.LoadAsync<AuctionAnalytics>(
                DateOnly.FromDateTime(sold.SoldAt.DateTime), token);
            analytics ??= new AuctionAnalytics { Date = DateOnly.FromDateTime(sold.SoldAt.DateTime) };
            analytics.TodaySalesCount++;
            analytics.TodayRevenue += sold.HammerPrice;
            operations.Store(analytics);
        }
    }
}
```

**Note:** `EfCoreEventProjection` requires `opts.AddEntityTablesFromDbContext<TDbContext>()` during registration — see below.

---

## EF Core Registration

```csharp
// In the BC's AddXyzModule() Marten configuration:

// Single-stream and multi-stream projections
opts.Add(new ListingBidSummaryProjection(), ProjectionLifecycle.Inline);
opts.Add(new SellerPerformanceProjection(), ProjectionLifecycle.Async);

// Event projections — different registration + entity tables
opts.Projections.Add(new AuctionDashboardProjection(), ProjectionLifecycle.Inline);
opts.AddEntityTablesFromDbContext<OperationsProjectionDbContext>(); // Required for event projections
```

**Lifecycle choice:**

| Lifecycle | When It Runs | Use When |
|---|---|---|
| `Inline` | Same transaction as event append | Strong consistency required; simple fast projections |
| `Async` | Background daemon | Eventual consistency acceptable; complex or slow projections |

Inline projections block writes. If a projection takes > ~50ms, use Async.

---

## Testing

Separate scopes for appending events (Marten) vs. querying projections (DbContext):

```csharp
[Fact]
public async Task ListingSold_ProjectsToListingBidSummary()
{
    var listingId = Guid.CreateVersion7();

    // Append events via Marten
    await using (var scope = _fixture.Host.Services.CreateAsyncScope())
    {
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();
        session.Events.StartStream<Listing>(listingId,
            new ListingPublished(listingId, sellerId, "Test Item", 10m, ...),
            new BidPlaced(listingId, ..., 50m),
            new ListingSold(listingId, winnerId, sellerId, 50m, DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();
    }

    // Query via DbContext (inline projection already ran)
    await using (var scope = _fixture.Host.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<OperationsProjectionDbContext>();
        var summary = await db.ListingBidSummaries.FindAsync(listingId);

        summary.ShouldNotBeNull();
        summary.Status.ShouldBe("Sold");
        summary.HammerPrice.ShouldBe(50m);
        summary.BidCount.ShouldBe(1);
    }
}
```

**For async projections** — poll until the daemon catches up:

```csharp
ReturnSummary? result = null;
var sw = Stopwatch.StartNew();
while (sw.Elapsed < TimeSpan.FromSeconds(10))
{
    await using var scope = _fixture.Host.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<OperationsProjectionDbContext>();
    result = await db.ListingBidSummaries.FindAsync(listingId);
    if (result != null) break;
    await Task.Delay(100);
}
result.ShouldNotBeNull("Projection did not complete within timeout");
```

---

## Pitfalls

**1. Missing `AddEntityTablesFromDbContext` for event projections.**
`EfCoreEventProjection` requires `opts.AddEntityTablesFromDbContext<TDbContext>()` separately from projection registration. Missing it causes `InvalidOperationException` at startup.

**2. Inline projections block writes.**
If `ApplyEvent` is slow (complex joins, external calls), every event append waits. Profile before choosing Inline. Async projection handles the same load without blocking writes.

**3. Async projections need the daemon running in tests.**
Use polling (see Testing section) and verify `DaemonMode.Solo` is configured in your test fixture. Alternatively, use Inline lifecycle in tests for deterministic behavior.

**4. Mutating events in `ApplyEvent`.**
`ApplyEvent` receives the same event instance during replay. Mutating the event causes projections to see stale data on rebuild. Always read from events, never write to them.

```csharp
// ❌ WRONG
snapshot.Status = @event.Data.Status = "Completed"; // mutates the event

// ✅ CORRECT
snapshot.Status = "Completed"; // read-only
```

**5. Change tracking overhead at scale.**
EF Core's change tracking runs on every `SaveChangesAsync()`. For high-throughput projections, disable it for read operations and keep it only for write paths.

**6. Schema drift without `AutoCreate.All`.**
Weasel migrates tables on startup only when `AutoCreateSchemaObjects = AutoCreate.All`. In production, set to `AutoCreate.None` and run migrations explicitly to control timing.

---

## Lessons Learned

**Schema migrations are transparent but irreversible.** Weasel generates and runs migrations on startup. Test schema changes in a staging environment before production deploys. Version-control your `DbContext.OnModelCreating` — it is your schema definition.

**Inline projections block writes.** A 500ms `ApplyEvent` means every event append waits 500ms. Use Async projections for complex aggregations.

**Async projections require monitoring.** If the daemon falls behind, projections become stale — but writes continue to succeed. Monitor the async daemon high-water mark. Add health checks that fail if projections are more than N seconds behind.

**Composite projections are powerful but hard to debug.** If stage 1 fails, stage 2 silently doesn't run. Prefer independent projections when possible; use composite only when one genuinely depends on another.

---

## How It Works Under the Hood

**Transaction coordination (Inline):**
1. Marten opens a transaction
2. Events appended to `mt_events`
3. For each projection: `DbContext` created → `ApplyEvent` called → EF Core tracks changes → `dbContext.SaveChangesAsync()` within the same transaction
4. Transaction commits atomically — both events and entities, or neither

**Insert vs. Update:** EF Core change tracking handles this. First `ApplyEvent` call for a stream ID returns a new entity → `INSERT`. Subsequent calls receive the existing snapshot → `UPDATE`. Marten loads the snapshot before calling `ApplyEvent`.

**Weasel migration:** On startup, Marten inspects `DbContext.OnModelCreating`, compares desired schema to actual database, and runs `CREATE TABLE` / `ALTER TABLE` statements. No `dotnet ef migrations add` needed. `DbContext` configuration is the schema definition.

**Rebuilding after a bug fix:**

```csharp
// Truncate projection tables and replay from events
await dbContext.Database.ExecuteSqlRawAsync("TRUNCATE TABLE listing_bid_summaries");
var daemon = host.Services.GetRequiredService<IProjectionCoordinator>();
await daemon.RebuildAsync<ListingBidSummaryProjection>(ct);
```

---

## References

### Native Marten projections
- [Marten Projections](https://martendb.io/events/projections/)
- [Marten Single-Stream Projections](https://martendb.io/events/projections/single-stream-projections.html)
- [Marten Multi-Stream Projections](https://martendb.io/events/projections/multi-stream-projections.html)
- [Marten Composite Projections](https://martendb.io/events/projections/composite.html)
- [Marten Event Enrichment](https://martendb.io/events/projections/enrichment.html)
- [Marten Flat Table Projections](https://martendb.io/events/projections/flat.html)

### EF Core projections
- [Marten EF Core Projections](https://martendb.io/events/projections/efcore.html)
- [Polecat.EntityFrameworkCore NuGet](https://www.nuget.org/packages/Polecat.EntityFrameworkCore/)

### Related CritterBids skills
- `docs/skills/marten-event-sourcing.md` — single-stream projections, aggregate conventions, daemon configuration, `RebuildSingleStreamAsync`
- `docs/skills/dynamic-consistency-boundary.md` — DCB patterns for cross-stream consistency (separate mechanism from composite projections)
- `docs/skills/polecat-event-sourcing.md` — 📚 Reference only; SQL Server + Polecat specifics (not active under ADR 011)
- `docs/skills/critter-stack-testing-patterns.md` — TestFixture patterns for async-projection testing
