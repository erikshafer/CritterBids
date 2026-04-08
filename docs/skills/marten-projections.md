# EF Core Projections from Marten Events

Patterns for using Entity Framework Core as a projection target for Marten's event store — bridging event sourcing with relational read models.

> **CritterBids status:** Not yet implemented. This skill was authored ahead of first use. Update it with concrete findings when the first EF Core projection lands.

---

## Table of Contents

1. [When to Use EF Core Projections](#when-to-use-ef-core-projections)
2. [The Three Projection Types](#the-three-projection-types)
3. [Setup](#setup)
4. [DbContext Configuration](#dbcontext-configuration)
5. [Single Stream Projections](#single-stream-projections)
6. [Multi-Stream Projections](#multi-stream-projections)
7. [Event Projections](#event-projections)
8. [Registration](#registration)
9. [Testing](#testing)
10. [Pitfalls](#pitfalls)
11. [Lessons Learned](#lessons-learned)
12. [How It Works Under the Hood](#how-it-works-under-the-hood)

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

## The Three Projection Types

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

## Single Stream Projections

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

## Multi-Stream Projections

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

## Event Projections

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

## Registration

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

- [Marten EF Core Projections](https://martendb.io/events/projections/efcore.html)
- [Polecat.EntityFrameworkCore NuGet](https://www.nuget.org/packages/Polecat.EntityFrameworkCore/)
- `docs/skills/marten-event-sourcing.md` — native Marten projection patterns
- `docs/skills/polecat-event-sourcing.md` — SQL Server + Polecat specifics
- `docs/skills/critter-stack-testing-patterns.md` — TestFixture patterns
