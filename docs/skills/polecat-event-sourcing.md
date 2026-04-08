# Event Sourcing with Polecat and Wolverine

> **Status: Partially documented — first Polecat BC implementation will complete this file.**
>
> CritterBids is the first project in the Critter Stack ecosystem to use Polecat. This skill covers what is known before implementation. Update it with concrete findings when the first Polecat BC lands (likely **Participants**).

---

## What Is Polecat?

Polecat is JasperFx's SQL Server-targeting sibling to Marten. It provides event sourcing, projections, and document storage against SQL Server using the same programming model as Marten on PostgreSQL.

As of Polecat 2.0, the goal is near-feature parity with Marten. The aggregate design, `Apply()` method conventions, projection types, Wolverine integration patterns, and `AutoApplyTransactions()` requirement are all shared.

---

## Why SQL Server for Certain BCs?

CritterBids uses SQL Server via Polecat for **Operations**, **Settlement**, and **Participants** BCs. See `docs/decisions/003-polecat-bcs.md` for the full rationale. The short version:

- **Operations** — projections are directly queryable by Power BI and SQL Server BI tooling
- **Settlement** — financial records belong in SQL Server for audit trail and compliance
- **Participants** — staff-managed data co-located alongside operations

---

## What Transfers Directly from Marten

Treat `docs/skills/marten-event-sourcing.md` as the primary reference. These patterns are expected to transfer directly:

- Aggregate design — `sealed record`, `Apply()` methods, `with` expressions, status enums
- `@event` parameter naming convention
- Domain event structure — aggregate ID as first property, past-tense naming
- Decider pattern — inline handler logic vs. separate Decider class
- Projection types — inline snapshots, multi-stream projections, async daemon
- `AutoApplyTransactions()` — required in every Polecat BC, same as Marten
- Wolverine handler return patterns — `Events`, `OutgoingMessages`, `IStartStream`, tuples
- Testing strategy — `ExecuteAndWaitAsync`, direct aggregate queries, race condition avoidance
- Event versioning — additive-only changes as default, upcasting for breaking changes

---

## Polecat + SQL Server Specifics

### Connection String

Polecat uses a property-based connection string, not the method-based form Marten uses:

```csharp
// Marten (PostgreSQL)
opts.Connection(connectionString);

// Polecat (SQL Server) — property, not method
opts.ConnectionString = connectionString;
```

### Schema Isolation

Polecat defaults to `"dbo"` if `DatabaseSchemaName` is not set. Always set it explicitly:

```csharp
opts.DatabaseSchemaName = "participants"; // lowercase BC name
```

### EF Core Projections with Polecat

The same `EfCoreSingleStreamProjection<T>`, `EfCoreMultiStreamProjection<T>`, and `EfCoreEventProjection<T>` patterns from `docs/skills/marten-projections.md` work identically with Polecat. The only difference is the DbContext provider:

```csharp
// PostgreSQL (Marten BCs)
services.AddDbContext<OperationsProjectionDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// SQL Server (Polecat BCs)
services.AddDbContext<OperationsProjectionDbContext>(opts =>
    opts.UseSqlServer(connectionString));
```

The projection class itself is **identical**. The connection string provider is the only change.

Package reference for Polecat EF Core projections:

```bash
dotnet add package Polecat.EntityFrameworkCore
```

This is the primary reason EF Core projections are attractive for CritterBids' Polecat BCs — the Operations BC can feed SQL Server tables that Power BI connects to directly, with zero ETL pipeline.

### String Collation Gotchas

SQL Server's default collation (`SQL_Latin1_General_CP1_CI_AS`) is **case-insensitive**. PostgreSQL defaults to case-sensitive. This affects:

- Document ID lookups — `"DOG-001"` and `"dog-001"` are the same key on SQL Server
- LINQ `.Where()` string comparisons
- Unique indexes on string columns

**CritterBids mitigation:** Use GUIDs for all aggregate IDs in Polecat BCs. Normalize any string keys with `.ToUpperInvariant()` or `.ToLowerInvariant()` before storing or querying. Verify mixed-case behavior in Phase 0 of the first Polecat BC implementation.

### DCB Support

Wolverine's DCB APIs (`EventTagQuery`, `[BoundaryModel]`, `IEventBoundary<T>`) were developed for Marten on PostgreSQL. Polecat 2.0 aims for parity — verify DCB availability before designing any Polecat BC around it.

The Settlement, Operations, and Participants BCs do not currently require DCB, so this is not an immediate blocker for MVP.

---

## Implementation Checklist

Work through this during the first Polecat BC (likely **Participants**) and document findings here:

- [ ] Confirm `AddPolecat()` / `IntegrateWithWolverine()` API shape
- [ ] Confirm connection string format and property vs. method registration
- [ ] Confirm `DatabaseSchemaName` isolation behavior
- [ ] Confirm `AutoApplyTransactions()` behavior is identical to Marten
- [ ] Confirm inline snapshot registration syntax
- [ ] Confirm multi-stream projection registration syntax
- [ ] Confirm `UseNumericRevisions(true)` for saga documents
- [ ] Confirm async daemon setup and `DaemonMode` options
- [ ] Confirm `IStartStream` / `MartenOps.StartStream()` equivalent
- [ ] Confirm `AggregateStreamAsync<T>()` equivalent API
- [ ] Confirm `EfCoreSingleStreamProjection` + `EfCoreMultiStreamProjection` work with `Polecat.EntityFrameworkCore`
- [ ] Verify SQL Server collation behavior with string IDs
- [ ] Confirm `DeleteAllDocumentsAsync()` behavior for test cleanup
- [ ] Document any anti-patterns discovered
- [ ] Update this file with a "Known Gotchas" section and flip status to ✅

---

## References

- [Polecat on NuGet](https://www.nuget.org/packages/Polecat) — check current version
- [Polecat.EntityFrameworkCore NuGet](https://www.nuget.org/packages/Polecat.EntityFrameworkCore/)
- [JasperFx GitHub](https://github.com/jasperfx) — Polecat source and documentation
- `docs/skills/marten-event-sourcing.md` — primary reference until this file is complete
- `docs/skills/marten-projections.md` — EF Core projection patterns (Marten + Polecat)
- `docs/decisions/003-polecat-bcs.md` — which BCs use Polecat and why
