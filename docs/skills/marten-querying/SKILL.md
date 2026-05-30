---
name: marten-querying
description: "Marten querying in CritterBids: schema-per-BC table resolution, compiled-query posture, and streaming JSON endpoints. Use when writing hot read paths or HTTP queries."
cluster: marten
tags: [marten, querying, indexes, compiled-queries, http]
---

# Querying with Marten

> CritterBids query conventions for Marten-backed read paths.
> Generic Marten indexing, query optimization, and compiled-query mechanics live in ai-skills `marten-advanced-indexes-and-query-optimization` and `wolverine-refactor-to-compiled-query`; **this skill documents only the CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- Writing a read-side query against `IQuerySession` or `IDocumentSession` in a CritterBids BC.
- Deciding whether a frequently called endpoint should use a compiled query.
- Streaming Marten JSON directly from a Wolverine HTTP endpoint.
- Dropping to raw SQL and needing schema-per-BC table names safely.

Do NOT use this skill for: projection authoring (see `marten-projections`), aggregate command handling (see `marten-event-sourcing`), or generic Marten indexing mechanics (read upstream first).

## Read upstream first

Generic Marten query mechanics are fully covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of querying:

1. `marten-advanced-indexes-and-query-optimization` — JSONB indexes, duplicated fields, compiled query tradeoffs, LINQ tuning.
2. `wolverine-refactor-to-compiled-query` — when and how to promote a handler query to a compiled query/query plan.

Those cover ~80% of the topic. This skill picks up at the CritterBids-specific decisions.

## CritterBids query posture

| Scenario | CritterBids default |
|---|---|
| Simple document lookup by id | `session.LoadAsync<T>(id, ct)` or Wolverine/Marten endpoint return types. |
| Ordinary filtered read | LINQ on `IQuerySession`; add indexes only for real query paths. |
| Stable high-traffic read | Compiled query or query plan; benchmark before and after. |
| Several independent reads in one request | Batched query or a purpose-built projection; do not hide N+1 loops. |
| HTTP response is the persisted JSON shape | Marten.AspNetCore streaming return types. |
| Response shape differs from persisted document | Project to DTO/read model first; do not stream internal JSON blindly. |
| Raw SQL needed | Resolve table names from Marten schema metadata; no hardcoded schema/table strings. |

All eight BCs use Marten/PostgreSQL (ADR 011). Keep queries inside the owning BC's document model; cross-BC reads use contracts, projections, or caches, not direct table queries into another BC's schema.

## Schema-per-BC raw SQL rule

If raw SQL is unavoidable, resolve document table names through Marten instead of hardcoding `mt_doc_*` names or schema prefixes:

```csharp
var schema = session.DocumentStore.Options.Schema;
var tableName = schema.For<CatalogListingView>();

var rows = await session.AdvancedSql.QueryAsync<CatalogListingView>(
    $"select id, data from {tableName} where data ->> 'Status' = $1",
    cancellationToken,
    "Open");
```

Why this matters in CritterBids:

- Each BC claims its own PostgreSQL schema for documents.
- Table names can move when a document is re-owned or renamed.
- Tests often isolate schemas; hardcoded `public.mt_doc_*` breaks those fixtures.

Raw SQL must still respect BC boundaries. A Listings query should not join directly to Auctions-owned documents just because both are in the same database.

## Streaming JSON endpoints

For new Wolverine HTTP endpoints that simply return a persisted Marten document/array/aggregate shape, prefer Marten.AspNetCore return types:

```csharp
[WolverineGet("/api/listings/{id:guid}")]
[AllowAnonymous]
public static StreamOne<CatalogListingView> Get(Guid id, IQuerySession session) =>
    new(session.Query<CatalogListingView>().Where(x => x.Id == id));

[WolverineGet("/api/listings/open")]
[AllowAnonymous]
public static StreamMany<CatalogListingView> Open(IQuerySession session) =>
    new(session.Query<CatalogListingView>().Where(x => x.Status == "Open"));

[WolverineGet("/api/auctions/{id:guid}/state")]
[AllowAnonymous]
public static StreamAggregate<Listing> State(Guid id, IDocumentSession session) =>
    new(session, id);
```

CritterBids endpoints are `[AllowAnonymous]` through M6; real `[Authorize]` resumes at M6 per `CLAUDE.md`.

Use streaming only when the public API shape is intentionally the persisted read model. If client shape, naming, privacy, or anti-corruption mapping differs, return a DTO or purpose-built projection instead.

## Compiled-query posture

Promote to compiled queries when a query is stable, repeated, and measurable on a hot path. Avoid cargo-cult compiled queries for one-off admin/dashboard reads.

CritterBids-specific reminders:

- Use `sealed` classes for compiled query types where practical, matching project immutability posture.
- Keep query parameters as settable properties; avoid primary constructors on compiled query classes.
- Do not use async LINQ operators or `ToList()`/`ToArray()` inside `QueryIs()`.
- Prefer an explicit query object name (`OpenCatalogListingsQuery`) over anonymous ad-hoc LINQ in a busy endpoint.
- If the query is also useful inside a batched request, consider a query-plan style abstraction per upstream guidance.

```csharp
public sealed class OpenCatalogListingsQuery : ICompiledListQuery<CatalogListingView>
{
    public Expression<Func<IMartenQueryable<CatalogListingView>, IEnumerable<CatalogListingView>>> QueryIs() =>
        q => q.Where(x => x.Status == "Open")
              .OrderByDescending(x => x.PublishedAt);
}
```

## Common pitfalls

- **Hardcoding Marten table names.** Use `session.DocumentStore.Options.Schema.For<T>()` so schema-per-BC and test schemas keep working.
- **Cross-BC SQL joins.** A shared PostgreSQL database is not permission to bypass the modular-monolith boundary.
- **Streaming internal JSON as public API by accident.** Streaming is only safe when the persisted read model is intentionally public.
- **Compiled-query overuse.** They are for stable hot paths; generic LINQ is clearer for exploratory/admin queries.
- **Injecting `IDocumentSession` just to read.** Use `IQuerySession` for reads unless an API (for example `StreamAggregate<T>`) specifically requires `IDocumentSession`.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-advanced-indexes-and-query-optimization` — indexes, duplicated fields, raw/advanced SQL, LINQ tuning.
- `wolverine-refactor-to-compiled-query` — compiled query refactoring workflow.

**Prerequisites:**

- `marten-event-sourcing` — schema-per-BC and single-store Marten setup.
- `marten-projections` — read-model design before querying.

**Downstream:**

- `critter-stack-testing-patterns` — testing read paths and avoiding async projection races.
- `wolverine-message-handlers` — Wolverine HTTP endpoint conventions.

**External:**

- ADR 011 (All-Marten Pivot) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions and § Canonical Bootstrap Sequence.
