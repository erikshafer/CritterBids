# ADR 006 — Infrastructure Orchestration

**Status:** Accepted
**Date:** 2026-04

---

## Context

CritterBids requires local provisioning of three infrastructure services:

- **PostgreSQL** — for Marten BCs arriving in M2+
- **SQL Server** — for Polecat BCs (Participants, Settlement, Operations per ADR 003)
- **RabbitMQ** — the settled async transport (ADR 002)

CritterBids is a single-contributor project and a conference demo vehicle. Developer experience and demo reliability are first-class constraints. The orchestration path must provision all three services, wire their connection strings and endpoints into the API host, and be reproducible on a fresh clone.

Two options were considered:

**Option A — .NET Aspire AppHost:** A dedicated `CritterBids.AppHost` project defines infrastructure resources in C#. Aspire injects connection strings and service endpoints into the API host automatically via resource references. A single `dotnet run --project src/CritterBids.AppHost` command starts all infrastructure and the API. The built-in developer dashboard shows service health, structured logs, and distributed traces — useful for live demos. Requires .NET Aspire 13.2+ and the Aspire workload installed alongside the .NET 10 SDK.

**Option B — Docker Compose:** A static `docker-compose.yml` at the repo root provisions PostgreSQL, SQL Server, and RabbitMQ. Contributors run `docker compose up -d` then `dotnet run --project src/CritterBids.Api`. Connection strings are maintained manually in `appsettings.Development.json`. Broader toolchain compatibility — no Aspire workload required.

Maintaining both paths as first-class requirements doubles the infrastructure surface area (two sets of connection string configuration, two verified boot paths, two sets of documentation) with no upstream contributors to serve that cost.

---

## Decision

CritterBids uses **.NET Aspire AppHost** as its single local-dev orchestration path.

---

## Consequences

**What changes as a result:**

- M1-S3 creates `src/CritterBids.AppHost` and registers PostgreSQL, SQL Server, and RabbitMQ resources with connection string references wired to the API host.
- `docker-compose.yml` is not authored as a deliverable in M1 or any subsequent milestone. Contributors must have the Aspire workload installed.
- `docs/milestones/M1-skeleton.md` §5 is updated in this session to reflect the single-path decision.

**Explicitly out of scope for this ADR:**

- Container image choices (e.g., SQL Server tag, RabbitMQ management vs. standard image) — decided in M1-S3.
- Aspire package version pinning — decided in M1-S3 when `Directory.Packages.props` is extended.
- Production deployment — not an M1 concern.
- CI/CD infrastructure provisioning — not an M1 concern.

**Staleness note:**

`CLAUDE.md`'s Quick Start section describes a `docker compose up -d && dotnet run --project src/CritterBids.Api` fallback path. That description is now stale. It is not updated in this PR — `CLAUDE.md` edits are out of scope for this ADR session and should be addressed in a future cleanup session.

---

## References

- ADR 002 — RabbitMQ as Initial Message Transport
- ADR 003 — Polecat (SQL Server) for Operations, Settlement, and Participants BCs
- `docs/milestones/M1-skeleton.md` §5 — updated in this session
