---
name: marten-projections
description: "Marten projections in CritterBids: native read models, EF Core projection posture, handler-driven tolerant upserts, and milestone view extension. Use when designing projection-backed read models."
cluster: marten
tags: [marten, projections, read-models, ef-core, integration-events]
---

# Marten Projections

> CritterBids projection conventions for native Marten JSONB documents and EF Core relational targets.
> Generic projection mechanics live in ai-skills `marten-projections-*`; **this skill documents only the CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- Designing a CritterBids read model fed by Marten events or cross-BC integration messages.
- Choosing between native Marten projections, EF Core projections from Marten events, and handler-driven tolerant upserts.
- Extending a read model across milestones as new BCs contribute fields.
- Building Settlement's single-source-seeded cache shape or Listings' cross-BC catalog view shape.

Do NOT use this skill for: aggregate mutation mechanics (see `marten-event-sourcing`), DCB tag-query writes (see `dynamic-consistency-boundary`), or projection side effects for SignalR broadcasts (see `projection-side-effects-for-broadcast-live-views`).

## Read upstream first

Generic Marten projection mechanics are fully covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of projection authoring:

1. `marten-projections-single-stream` — snapshots, `Create`/`Apply`/`Evolve`, lifecycles, rebuilds.
2. `marten-projections-multi-stream` — identity routing, fan-out, custom groupers, view projections.
3. `marten-projections-composite` — staged projections and `Updated<T>` synthetic events.
4. `marten-projections-event-enrichment` — `EnrichEventsAsync` and batch reference lookups.
5. `marten-projections-flat-table` — SQL-friendly `FlatTableProjection` and raw-SQL `EventProjection`.

Those cover ~80% of the topic. This skill picks up at the CritterBids-specific decisions.

## Scope note — native + EF Core stay together

This skill intentionally combines two halves:

- **Part 1 — Native Marten projections**: JSONB read models in PostgreSQL.
- **Part 2 — EF Core projections from Marten events**: relational read tables fed by the Marten event store.

Both are Marten projection mechanisms with different target backends. Split only if growth or maintenance churn demands it; do not split preemptively. CritterBids is all-Marten/PostgreSQL per ADR 011. Any old Marten-vs-Polecat BC framing is stale.

## CritterBids projection posture

| Need | CritterBids default |
|---|---|
| One stream → one queryable document | Native single-stream snapshot; usually inline for aggregate snapshots. |
| Events from multiple streams in one BC | Native `MultiStreamProjection<TDoc, TId>`. |
| One event updates many documents | Native multi-stream fan-out (`Identities<T>` / `FanOut<TParent,TChild>`). |
| Reference data in projection logic | Event enrichment; never N+1 loads in `Apply`. |
| Stage 2 consumes stage 1 output | Composite projection, but only when there is a real dependency. |
| Reporting/BI-friendly tables | Flat-table projection first; EF Core projection when EF LINQ/table modeling is the actual consumer need. |
| Cross-BC integration events feed a view | Handler-driven tolerant upsert; native Marten projections do not see foreign BC events. |
| Read model grows by milestone/source BC | One shared view, sibling handler class per source BC. |
| Workflow-start cache for another BC | Single-source-seeded cache with absorbing terminal statuses. |

## Part 1 — Native Marten projections

### Use native multi-stream only for events owned by the projecting store

Native `MultiStreamProjection<TDoc, TId>` is the right tool when routing is a property on events in the same Marten store:

```csharp
public sealed class SellerActivityProjection : MultiStreamProjection<SellerActivity, Guid>
{
    public SellerActivityProjection()
    {
        Identity<ListingPublished>(x => x.SellerId);
        Identity<ListingSold>(x => x.SellerId);
        Identity<ListingWithdrawn>(x => x.SellerId);
    }
}
```

For dashboard and reporting rollups, prefer async unless the query path truly requires same-transaction consistency.

### `RollUpByTenant()` is not a current CritterBids pattern

CritterBids is single-tenant. Keep tenant rollups out of BC prompts unless a real multi-tenant variant exists. If that changes, load upstream multi-tenancy guidance before using `RollUpByTenant()`.

### Flat tables are likely Operations-first

Flat-table projections are the likely first fit for Operations dashboards and reporting surfaces: daily bid volume, settlement totals, seller performance, and similar SQL-shaped outputs. Use them when the consumer wants columns, counters, or BI tooling rather than JSONB documents.

## Handler-driven projections — tolerant upsert

Native projections are wrong when the read model is driven by cross-BC integration events handled by Wolverine. Those events arrive through queues and handlers, not through the projecting BC's event store.

Use the tolerant-upsert primitive:

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
    // Wolverine AutoApplyTransactions commits. Do not call SaveChangesAsync here.
}
```

Use handler-driven tolerant upsert when:

| Situation | Reach for |
|---|---|
| Routing purely by an event property in the same store | Native `MultiStreamProjection<TDoc,TId>`. |
| Handler also schedules messages, calls domain services, or emits further events | Handler-driven tolerant upsert. |
| Event originates from another BC over RabbitMQ/local queue | Handler-driven tolerant upsert. |
| One view combines events from multiple BC contracts | Handler-driven tolerant upsert, one handler class per source BC. |

In-repo ground: `CatalogListingView` handlers in Listings and `PendingSettlementHandler` in Settlement.

## Single-source-seeded caches

Settlement's `PendingSettlement` is not a normal UI read model. It is a cross-BC boundary cache: Settlement needs values from Selling at workflow start (`ListingSold`) without querying Selling directly.

Shape:

| Aspect | Multi-source view | Single-source-seeded cache |
|---|---|---|
| Seed event | Many sources contribute fields | One source event creates the row |
| Later events | Add fields and status | Mostly transition status |
| Consumer | UI/query surface | Downstream workflow or saga at start time |
| Purpose | Denormalized read performance | Avoid cross-BC query boundary violation |

Status must be absorbing. Re-delivered seed events must not reset terminal rows:

```csharp
var existing = await session.LoadAsync<PendingSettlement>(message.ListingId, cancellationToken);
var status = existing?.Status ?? PendingSettlementStatus.Pending;

session.Store(new PendingSettlement
{
    Id = message.ListingId,
    SellerId = message.SellerId,
    Status = status,
});
```

Terminal handlers use the same guard shape:

```csharp
if (existing.Status != PendingSettlementStatus.Pending) return;
session.Store(existing with { Status = PendingSettlementStatus.Expired });
```

Set `Id = ListingId` when the natural correlation key is the Marten document key. Do not add `UseNumericRevisions` to these projection documents unless real concurrent-writer contention appears.

## View extension across milestones

Listings' catalog view established the CritterBids rule:

> One view per logical entity; one sibling handler class per event-source BC; fields grow additively across milestones.

```text
CatalogListingView.cs                 # one shared view
ListingPublishedHandler.cs            # Selling source fields
AuctionStatusHandler.cs               # Auctions status fields
SettlementStatusHandler.cs            # Settlement terminal transition
AuctionsSessionHandler.cs             # Auctions session membership fields
SellingListingWithdrawnHandler.cs     # Selling withdrawn transition
```

Each handler owns a disjoint field set. Do not make every UI read path join `CatalogListingCore` + `CatalogListingAuctionStatus` + `CatalogListingSettlement`. Do not use a native multi-stream projection for foreign-BC integration contracts Marten cannot see.

### Status-preservation guards

When sibling handlers share a `Status` field, each handler owns transitions, not the full vocabulary. Guard before storing:

```csharp
// SettlementStatusHandler: only Sold -> Settled is legal
if (existing.Status != "Sold") return;

// AuctionStatusHandler: Withdrawn is absorbing
if (view.Status == "Withdrawn") return;

// SellingListingWithdrawnHandler: only Published/Open -> Withdrawn is legal
if (existing.Status is not ("Published" or "Open")) return;
```

Put the guard after `LoadAsync` and before any `session.Store` or cascade emission. A no-op transition must emit no messages.

## Part 2 — EF Core projections from Marten events

EF Core projections are not yet implemented in CritterBids. Keep examples Postgres/Marten-aligned: `Marten.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL`. Do not revive the stale Marten-vs-Polecat BC split.

Use EF Core projections only when relational tables are the goal:

| BC | Candidate use case | Projection type |
|---|---|---|
| Operations | Seller performance, bid counts by listing, reporting tables | Multi-stream / event projection |
| Settlement | Fee totals by seller or period | Multi-stream / flat table first, EF Core if EF queries are needed |
| Listings | Browse catalog with complex relational filters | Single-stream if native JSONB querying is insufficient |

Conventions if introduced:

- Table and column names are snake_case to match Marten-managed schema.
- `DbContext.OnModelCreating` is the schema definition; map explicitly.
- Register EF projection tables through Marten/Weasel; do not run separate `dotnet ef database update` in normal app startup.
- For hot paths, prefer async projections; inline EF change tracking blocks event appends.

## Common pitfalls

- **Using native projections for foreign BC messages.** Native Marten projections only process events in the local store. Cross-BC contracts belong in Wolverine handlers with tolerant upsert.
- **Regressing terminal statuses.** Any sibling handler writing a shared status must guard expected pre-states before storing.
- **Letting seed redelivery reset a workflow cache.** Preserve existing terminal state on seed-event redelivery.
- **Adding per-BC projection fragments.** A UI view that always needs all fields should stay one document with source-specific sibling handlers.
- **Choosing EF Core because it is familiar.** EF Core projections add change tracking and another model surface. Use them only for relational consumers that native Marten/flat-table projections cannot serve cleanly.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-projections-single-stream`, `marten-projections-multi-stream`, `marten-projections-composite`, `marten-projections-event-enrichment`, `marten-projections-flat-table`.

**Prerequisites:**

- `marten-event-sourcing` — stream identity, single-store Marten setup, snapshot posture.
- `wolverine-message-handlers` — handler shape and `OutgoingMessages` routing footguns.

**Downstream:**

- `projection-side-effects-for-broadcast-live-views` — projection side effects for SignalR live views.
- `dynamic-consistency-boundary` — tag-query consistency boundaries, separate from composite projections.
- `critter-stack-testing-patterns` — testing async projections and cross-BC handler isolation.

**External:**

- ADR 011 (All-Marten Pivot), ADR 014 (cross-BC read-model extension shape) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions and § BC Module Quick Reference.
