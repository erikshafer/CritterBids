# ADR 003 — Polecat (SQL Server) for Operations, Settlement, and Participants BCs

**Status:** Accepted  
**Date:** 2026-04

---

## Context

CritterBids uses both PostgreSQL (via Marten) and SQL Server (via Polecat) as event stores. This reflects a real-world organizational pattern where SQL Server is the corporate database — used by BI tooling, finance teams, and compliance — while PostgreSQL is the developer-chosen database for new services.

Polecat is JasperFx's SQL Server-targeting sibling to Marten, sharing the same programming model. As of Polecat 2.0, it aims for near-feature parity with Marten on PostgreSQL.

---

## Decision

The following BCs use **SQL Server via Polecat**:

- **Operations** — staff-facing, reporting-adjacent. Projections should be queryable by Power BI or SQL Server BI tooling without an ETL layer.
- **Settlement** — financial records belong in SQL Server for audit trail, compliance, and finance team access.
- **Participants** — staff-managed participant standing, seller verification, and activity history are appropriate for SQL Server alongside operations data.

All other BCs use **PostgreSQL via Marten**.

---

## Rationale

| BC | Reason for SQL Server |
|---|---|
| Operations | Power BI / BI tooling, staff reporting, projections as the reporting layer |
| Settlement | Financial audit trail, compliance access, finance team SQL queries |
| Participants | Staff-managed data (flagged sessions, seller verification) co-located with ops |

The Auctions BC specifically stays on Marten because the DCB APIs (`EventTagQuery`, `[BoundaryModel]`, `IEventBoundary<T>`) are Marten-specific. Listings BC stays on Marten for PostgreSQL full-text search.

---

## Consequences

**Positive:**
- Demonstrates the JasperFx ecosystem's breadth — same programming model, two storage engines
- Operations projections are directly queryable by BI tools without ETL
- Financial records in SQL Server satisfies real enterprise compliance patterns
- The "Polecat ↔ Marten swap" stretch goal becomes a natural extension of this decision

**Negative:**
- Docker Compose includes a SQL Server image — slower to pull and start than PostgreSQL
- Two storage engines increase operational surface area slightly

---

## Stretch Goal

A post-MVP milestone (**M-storage-swap**) will demonstrate swapping a BC's event store between Marten and Polecat — showing that the JasperFx abstraction makes storage engine swaps a registration-level concern, not a business logic refactor.

---

## References

- `docs/vision/bounded-contexts.md` — BC storage assignments
- `docs/milestones/MVP.md` — MVP scope
