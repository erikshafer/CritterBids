# M1-S2: Infrastructure Orchestration ADR — Retrospective

**Date:** 2026-04-11
**Milestone:** M1 — Skeleton
**Slice:** S2 — Infrastructure orchestration decision
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M1-S2-infrastructure-orchestration-adr.md`

## Baseline

- Solution builds clean; 2 tests pass (both smoke tests from M1-S1).
- `docs/decisions/` contains ADRs 001–005; no `006-*` file existed.
- `docs/milestones/M1-skeleton.md` §5 described both Aspire and Docker Compose as required paths, with explicit "both paths must work at M1 exit" language.
- `docs/milestones/M1-skeleton.md` §9 listed S2 as *TBD* covering the combined decision + implementation.
- No `CritterBids.AppHost` project existed; no `docker-compose.yml` existed.

## Items completed

| Item | Description |
|------|-------------|
| S2a | Authored `docs/decisions/006-infrastructure-orchestration.md` as Accepted ADR |
| S2b | Rewrote `docs/milestones/M1-skeleton.md` §5 to commit to Aspire and link ADR 006 |
| S2c | Updated `docs/milestones/M1-skeleton.md` §9 session table: S2 = ADR, S3 = implementation, S4–S7 shift accordingly |
| S2d | Removed docker-compose.yml from §4 solution layout and removed docker-compose exit criterion from §1 |

## S2a: ADR 006 — decision rationale

**Decision:** .NET Aspire AppHost, single path. Docker Compose fallback dropped.

**Why Aspire over Compose.** Four CritterBids-specific factors drove this:

1. CLAUDE.md already names `.NET Aspire 13.2+` as the local orchestration tool and `dotnet run --project src/CritterBids.AppHost` as the primary run command — the project had a stated preference, not an open question.
2. Aspire injects connection strings from resource references automatically; maintaining a parallel `appsettings.Development.json` with matching credentials would be ongoing friction for a single-contributor project.
3. The developer dashboard (structured logs, traces, service health) is a conference demo asset — it makes infrastructure visible without extra tooling.
4. Maintaining both paths as first-class verified deliverables doubles infrastructure surface area with no contributors to serve that cost.

**Why the fallback was dropped entirely rather than kept as best-effort.** The prompt required a one-path decision and prohibited "we'll try Aspire and fall back if it doesn't work out" equivocation. A best-effort fallback is indistinguishable from a deferred second decision. Aspire is the path; contributors who cannot run Aspire are not an M1-scoped concern.

**CLAUDE.md staleness.** CLAUDE.md Quick Start describes a `docker compose up -d` fallback path. That description is now stale. Noted in the ADR Consequences section. Not fixed in this PR per scope constraints.

## S2b–S2d: Milestone doc coherence edits

Sections updated beyond the prompt's explicit §5 and §9 targets:

- **§1 exit criteria** — removed "docker compose up -d fallback path verified working" criterion. Without this, §1 would have referenced a path no longer required by §5.
- **§4 solution layout** — removed `docker-compose.yml` line. The solution layout is normative for M1-S3; leaving a file there that will not be created would mislead that session.

Both changes were required for the doc to "read coherently after the §5 rewrite" per the prompt's verification instruction.

## S2: ADR discovery gap (flagged for retro, not actioned)

The prompt explicitly flagged: no `docs/decisions/README.md` index exists, CLAUDE.md does not route to the decisions directory, and prompts README has no rule requiring architecture-adjacent prompts to list existing ADRs in context-load. This session had to infer the ADR numbering and existing content from the glob result and file reads. The gap is not fixed here — it is bundled for M1-S7 (retrospective skills + ADR session) where a `docs/decisions/README.md` index, a CLAUDE.md routing pointer, and a prompts-README rule can land as a coherent "make ADRs discoverable" change.

## Test results

| Phase | Tests | Result |
|-------|-------|--------|
| Session open (baseline) | 2 | Pass |
| Session close | 2 | Pass — no code changed |

## Build state at session close

- Errors: 0
- Warnings: 0 (no code changed)
- `.csproj` files created or modified: 0
- `Directory.Packages.props` pins: 4 (unchanged)
- Files outside `docs/`: 0

## Key learnings

1. **Documentation-only sessions still require coherence review across the full doc.** The prompt scoped §5 and §9, but §1 and §4 also referenced docker-compose. Limiting edits to only the named sections would have left the milestone doc internally contradictory.
2. **ADR discovery is a session cost, not a given.** Without a `docs/decisions/README.md` index or CLAUDE.md routing, the ADR numbering had to be confirmed by directory glob. This is a single-session annoyance today; it becomes a coordination hazard when the number of ADRs grows.

## Verification checklist

- [x] `docs/decisions/006-infrastructure-orchestration.md` exists
- [x] ADR status is **Accepted**
- [x] ADR Decision section names exactly one orchestration path in one sentence
- [x] ADR Context section references single-contributor, modular monolith, conference demo, Polecat + SQL Server (ADR 003), RabbitMQ (ADR 002)
- [x] ADR Consequences section names at least one M1-S3 follow-up and at least one item explicitly out of scope
- [x] `docs/milestones/M1-skeleton.md` §5 is rewritten to commit to Aspire and links ADR 006
- [x] §5 no longer contains "both paths must work" or any Docker Compose fallback description
- [x] `docs/milestones/M1-skeleton.md` §9 session table reflects S2 = ADR, S3 = infrastructure implementation
- [x] No files created or modified outside `docs/decisions/`, `docs/milestones/`, and this retrospective
- [x] No `.csproj`, `.slnx`, `.props`, or source files touched
- [x] `dotnet build` and `dotnet test` still succeed — no code changed, trivially true

## What remains / next session should verify

- **CLAUDE.md Quick Start is stale.** Describes a docker-compose fallback that no longer exists as a project requirement. Deferred; not assigned to a specific session.
- **M1-skeleton.md §1 exit criteria** still reference `docs/skills/aspire.md` as a deliverable — that skill file does not yet exist and is correctly deferred to the retrospective session (now S7).
- **M1-D4 (Polecat namespace GUID)** remains open. Assigned to S4 (Participants BC scaffold) where the constant is generated and pinned in code.
- **ADR discoverability gap** (no index, no CLAUDE.md routing) flagged for M1-S7.
