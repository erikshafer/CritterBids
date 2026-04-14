# CritterBids — AI Development Guidelines

CritterBids is an open-source auction platform built on the Critter Stack (Wolverine + Marten + Polecat). It is structured as a **.NET modular monolith** — a single deployable unit with well-enforced bounded context (BC) boundaries. It is modeled after eBay's platform conventions and is designed as both a reference architecture and a live conference demo vehicle.

> **Companion project:** CritterSupply (e-commerce) demonstrates the same Critter Stack in a different domain. Patterns established there carry over here unless explicitly overridden.

---

## Quick Start (First 5 Minutes)

1. **Understand what you're looking at:**
   - Single deployable API host (`src/CritterBids.Api`) wiring 8 BC modules together
   - Each BC is a separate .NET class library — no BC references another BC's internals
   - BCs communicate exclusively through types in `src/CritterBids.Contracts`
   - PostgreSQL (Marten) for auction-core BCs; SQL Server (Polecat) for Operations, Settlement, Participants

2. **Run the system locally:**
   ```bash
   dotnet run --project src/CritterBids.AppHost --launch-profile http
   ```
   .NET Aspire (`CritterBids.AppHost`) is the single local-orchestration path — it provisions Postgres, SQL Server, RabbitMQ, and any other infrastructure dependencies, and launches the API host. Dashboard at `http://localhost:15237`. To use the `https` profile instead, first run `dotnet dev-certs https --trust` once. All three infrastructure containers are labelled `com.docker.compose.project=critterbids` and appear grouped under **critterbids** in Docker Desktop.

3. **Key files to orient yourself:**
   - **[CLAUDE.md](./CLAUDE.md)** — this file, AI development entry point
   - **[docs/vision/bounded-contexts.md](./docs/vision/bounded-contexts.md)** — BC map, ownership, integration topology
   - **[docs/vision/domain-events.md](./docs/vision/domain-events.md)** — canonical event vocabulary
   - **[docs/skills/README.md](./docs/skills/README.md)** — skill index, load before implementing

4. **Before implementing anything:**
   - Check `docs/vision/bounded-contexts.md` for BC boundaries
   - Load the relevant skill file(s) from `docs/skills/`
   - Code is the source of truth once it exists — docs describe intent, code shows reality

---

## Documentation Hierarchy

```
Code (src/, tests/)                      ← Source of truth once written
    ↑
docs/vision/bounded-contexts.md          ← BC ownership, integration topology
    ↑
CLAUDE.md (this file)                    ← Entry point, conventions, routing
    ↑
docs/skills/*.md                         ← Implementation patterns (load on demand)
    ↑
docs/personas/*.md                       ← Agent personas for workshops
    ↑
docs/decisions/*.md (ADRs)               ← Architectural decisions with rationale
    ↑
docs/milestones/*.md                     ← Scope per milestone
```

---

## Session Workflow

CritterBids implementation work runs through a **prompt → execute → retro** loop. Every session is bounded by a written prompt and closes with a written retrospective; both live in version control alongside the code they govern.

- **[docs/prompts/README.md](./docs/prompts/README.md)** — Session prompt template, naming convention, and the ten rules every Copilot session prompt obeys. Read before authoring a new prompt.
- **[docs/retrospectives/README.md](./docs/retrospectives/README.md)** — Retrospective template, section order, and the ten rules every session retro obeys. Read before writing a retro at session close.

A session prompt and its retro share a slug (e.g. `M1-S2-participants-bc-scaffold.md` ↔ `M1-S2-participants-bc-scaffold-retrospective.md`) so they sort together. The retro is part of the session's deliverable PR — not a follow-up.

---

## Modular Monolith Rules — Non-Negotiable

These are the structural rules that define CritterBids as a modular monolith. Violations break the architecture.

- **No BC project references another BC project.** The only shared dependency is `CritterBids.Contracts`.
- **Integration events cross BC boundaries via `CritterBids.Contracts`.** Types defined there are the public API between modules.
- **Each BC registers itself via `AddXyzModule()`.** `CritterBids.Api/Program.cs` calls these — nothing else.
- **BCs never call each other's handlers directly.** All cross-BC communication is via Wolverine messages.

---

## Preferred Tools & Stack

| Concern | Tool |
|---|---|
| Language | C# 14+ / .NET 10+ |
| Message handling | Wolverine 5+ |
| Event sourcing (PostgreSQL) | Marten 8+ |
| Event sourcing (SQL Server) | Polecat 2+ |
| Async messaging | RabbitMQ (AMQP) |
| Real-time push | SignalR |
| Testing | xUnit + Shouldly + Testcontainers + Alba |
| Frontend | React + TypeScript |
| Local orchestration | .NET Aspire 13.2+ (primary) |

---

## Core Conventions

- `sealed record` for all commands, events, queries, and read models — no exceptions
- `IReadOnlyList<T>` not `List<T>` for collections
- Handlers return events/messages — never call `session.Store()` directly
- All saga terminal paths must call `MarkCompleted()`
- `opts.Policies.AutoApplyTransactions()` required in every BC's Marten configuration
- `[Authorize]` on all non-auth endpoints from first commit
- Integration events published via `OutgoingMessages` — never `IMessageBus` directly
- `bus.ScheduleAsync()` is the only justified use of `IMessageBus` in handlers
- UUID v5 stream IDs with BC-specific namespace prefixes
- No "Event" suffix on domain event type names — ever
- No direct references to "paddle" — participants are identified by `BidderId`

---

## BC Module Quick Reference

| BC | Project | Storage | Key Patterns |
|---|---|---|---|
| Participants | `CritterBids.Participants` | SQL Server / Polecat | Event-sourced aggregate |
| Selling | `CritterBids.Selling` | PostgreSQL / Marten | Event-sourced aggregate, state machine |
| Auctions | `CritterBids.Auctions` | PostgreSQL / Marten | DCB, Auction Closing saga, Proxy Bid saga |
| Listings | `CritterBids.Listings` | PostgreSQL / Marten | Multi-stream projections, full-text search |
| Settlement | `CritterBids.Settlement` | SQL Server / Polecat | Saga, financial event stream |
| Obligations | `CritterBids.Obligations` | PostgreSQL / Marten | Saga, cancellable scheduled messages |
| Relay | `CritterBids.Relay` | PostgreSQL / Marten | Wolverine handlers, SignalR hub |
| Operations | `CritterBids.Operations` | SQL Server / Polecat | Cross-BC projections, SignalR hub |

---

## Skill Invocation Guide

Load the relevant skill before implementing. Skills encode hard-won patterns.

| Task | Skill to load |
|---|---|
| Adding a Wolverine handler | `docs/skills/wolverine-message-handlers.md` |
| Handler in a Marten named-store BC (no default `IDocumentStore`) | `docs/skills/marten-named-stores.md` |
| Creating a saga | `docs/skills/wolverine-sagas.md` |
| Event-sourced aggregate (Marten) | `docs/skills/marten-event-sourcing.md` |
| Event-sourced aggregate (Polecat) | `docs/skills/polecat-event-sourcing.md` |
| Marten projection | `docs/skills/marten-projections.md` |
| Marten BC test fixture / named-store cleanup | `docs/skills/marten-named-stores.md` |
| DCB / Dynamic Consistency Boundary | `docs/skills/dynamic-consistency-boundary.md` |
| Integration messaging | `docs/skills/integration-messaging.md` |
| SignalR real-time | `docs/skills/wolverine-signalr.md` |
| Writing tests | `docs/skills/critter-stack-testing-patterns.md` |
| Cross-BC handler isolation in test fixtures | `docs/skills/critter-stack-testing-patterns.md` |
| Running an Event Modeling workshop | `docs/skills/event-modeling/SKILL.md` |

> **Named-store note:** CritterBids has **no default `IDocumentStore`** — only named stores via `AddMartenStore<T>()`.
> `IDocumentSession` is **not** injectable in handlers. `CleanAllMartenDataAsync()` (non-generic) will throw.
> Load `docs/skills/marten-named-stores.md` for the complete constraint reference before implementing
> any Marten-backed handler or test fixture.

> **Full skill status ledger:** `docs/skills/README.md`

---

## Event Modeling & Personas

For workshop sessions, load persona documents from `docs/personas/` alongside relevant skill files.

See `docs/personas/README.md` for the full roster and guidance on which personas to activate per session type.

---

## Context7

For Wolverine/Marten/Polecat capabilities beyond established skill file patterns:

- Library ID: Wolverine → `/jasperfx/wolverine`
- Library ID: Marten → `/jasperfx/marten`

---

## Do Not

- Commit directly to `main` — branch and PR
- Add a BC project reference to another BC project
- Use `IMessageBus` in a handler except for `ScheduleAsync()`
- Name a domain event type with an "Event" suffix
- Use `List<T>` on records or aggregates
- Reference "paddle" anywhere in domain or application code
- Commit without running `dotnet build` and `dotnet test`
- Include a `Co-Authored-By` trailer in commit messages
