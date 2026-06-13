# CritterBids — AI Development Guidelines

CritterBids is an open-source auction platform built on the Critter Stack (Wolverine + Marten + PostgreSQL). It is structured as a **.NET modular monolith** — a single deployable unit with well-enforced bounded context (BC) boundaries. It is modeled after eBay's platform conventions and is designed as both a reference architecture and a live conference demo vehicle.

> **Companion project:** CritterSupply (e-commerce) demonstrates the same Critter Stack in a different domain. Patterns established there carry over here unless explicitly overridden.

---

## Quick Start (First 5 Minutes)

1. **Understand what you're looking at:**
   - Single deployable API host (`src/CritterBids.Api`) currently wiring 8 BC modules together
   - Each BC is a separate .NET class library — no BC references another BC's internals
   - BCs communicate exclusively through types in `src/CritterBids.Contracts`
   - All currently implemented BC modules use PostgreSQL via Marten (ADR 011 — All-Marten Pivot)

2. **Run the system locally:**
   ```bash
   dotnet run --project src/CritterBids.AppHost --launch-profile http
   ```
   .NET Aspire (`CritterBids.AppHost`) is the single local-orchestration path — it provisions Postgres, RabbitMQ, and any other infrastructure dependencies, and launches the API host. Dashboard at `http://localhost:15237`. To use the `https` profile instead, first run `dotnet dev-certs https --trust` once. Infrastructure containers are labelled `com.docker.compose.project=critterbids` and appear grouped under **critterbids** in Docker Desktop.

3. **Key files to orient yourself:**
   - **[CLAUDE.md](./CLAUDE.md)** — this file, AI development entry point
   - **[docs/STATUS.md](./docs/STATUS.md)** — derived project-status snapshot (where we are, what's next, deferred items, risks); the newest retrospective remains canonical if they disagree
   - **[docs/vision/bounded-contexts.md](./docs/vision/bounded-contexts.md)** — BC map, ownership, integration topology
   - **[docs/vision/domain-events.md](./docs/vision/domain-events.md)** — canonical event vocabulary
   - **[docs/skills/README.md](./docs/skills/README.md)** — skill index, load before implementing
   - **[docs/decisions/README.md](./docs/decisions/README.md)** — ADR index, naming convention, next available number
   - **[openspec/README.md](./openspec/README.md)** — OpenSpec workspace (M6-adopting BCs only, per ADR 021); ignore if you are working an M1–M5 BC

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
docs/decisions/README.md                 ← ADR index, naming convention, next available number
docs/decisions/*.md (ADRs)               ← Architectural decisions with rationale
    ↑
docs/milestones/*.md                     ← Scope per milestone
```

### OpenSpec workspace (M6-adopting BCs only)

```
openspec/                                ← OpenSpec CLI workspace (peer to docs/, CLI-hardcoded)
├── README.md                            ← workspace orientation, capability + adoption ledgers
├── specs/<capability>/spec.md           ← accumulated capability spec, one per adopting BC
└── changes/<slug>/                      ← active change proposals (proposal/design/tasks/delta-spec)
    └── archive/YYYY-MM-DD-<slug>/       ← completed changes, preserved verbatim

.github/prompts/opsx-*.prompt.md         ← Copilot slash commands (do not edit; OpenSpec-managed)
.github/skills/openspec-*/SKILL.md       ← OpenSpec workflow skills (do not edit; OpenSpec-managed)
```

The OpenSpec workspace is in scope only for M6 Obligations (adopting) and for any M6 BC that opts in at its own opening session (Relay, Operations). M1–M5 BCs do not use OpenSpec and the workspace is irrelevant to their slices. See `openspec/README.md` and ADR 021 for the full adoption ledger.

`docs/skills/` is CritterBids' Critter-Stack-pattern library; `.github/skills/` is OpenSpec's workflow-mechanic library. The two are differently scoped and do not duplicate content. Edit only `docs/skills/`.

---

## Session Workflow

CritterBids implementation work runs through a **prompt → execute → retro** loop. Every session is bounded by a written prompt and closes with a written retrospective; both live in version control alongside the code they govern.

- **[docs/prompts/README.md](./docs/prompts/README.md)** — Session prompt template, naming convention, and the ten rules every Copilot session prompt obeys. Read before authoring a new prompt.
- **[docs/retrospectives/README.md](./docs/retrospectives/README.md)** — Retrospective template, section order, and the ten rules every session retro obeys. Read before writing a retro at session close.

A session prompt and its retro share a slug (e.g. `M1-S2-participants-bc-scaffold.md` ↔ `M1-S2-participants-bc-scaffold-retrospective.md`) so they sort together. The retro is part of the session's deliverable PR — not a follow-up.

---

## CI Pipeline Reality (Current)

- Workflow file: `.github/workflows/ci.yml`
- Trigger: `push`/`pull_request` on `main` (+ manual dispatch)
- Path-filter gate: doc-only changes skip build/test jobs
- Required branch-protection check: final `CI` aggregator job
- Current integration matrix coverage: Contracts, Api, Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations (all 8 BCs + Contracts + Api)

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
| Message handling | Wolverine 6+ |
| Event sourcing | Marten 9+ (PostgreSQL — all BCs) |
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
- `opts.Policies.AutoApplyTransactions()` in `UseWolverine()` in `Program.cs` — not inside BC `ConfigureMarten()` calls
- Staff-facing endpoints and the `OperationsHub` are gated by the `StaffOnly` authorization policy
  (M7-S6, ADR-024); the `StaffToken` scheme is the default authenticate + challenge scheme. Endpoints
  that are intentionally public stay `[AllowAnonymous]` (e.g. the read catalog, the `BiddingHub`).
  This supersedes the former "`[AllowAnonymous]` on all endpoints through M6" stance: real
  authentication resumed at M7 as the single-shared-secret staff posture in ADR-024. Per-user / role
  auth remains post-MVP.
- Integration events published via `OutgoingMessages` — never `IMessageBus` directly
- `bus.ScheduleAsync()` is the only justified use of `IMessageBus` in handlers
- UUID v7 stream IDs (`Guid.CreateVersion7()`) for all Marten BCs — no natural business key exists
  in most contexts; UUID v7 provides insert locality via its Unix-ms prefix (ADR 007). UUID v5 with
  a BC-specific namespace constant remains available when a natural business key enables deterministic
  stream creation.
- No "Event" suffix on domain event type names — ever
- No direct references to "paddle" — participants are identified by `BidderId`

### Canonical Bootstrap Sequence

The uniform bootstrap for all BCs — confirmed by analysis of all 12 CritterStackSamples reference
projects (`C:\Code\JasperFx\critter-stack-samples-analysis.md`, §1 and §14):

1. `AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` — primary store, outbox wiring, session optimization
2. `UseWolverine(opts => { opts.Policies.AutoApplyTransactions(); ... })` — message bus with automatic transaction policy
3. `AddWolverineHttp()` + `app.MapWolverineEndpoints()` — HTTP endpoint discovery

Every BC contributes its document types, projections, and aggregate registrations via
`services.ConfigureMarten()` inside its own `AddXyzModule()` extension method. `Program.cs` contains
exactly one `AddMarten()` call.

---

## BC Module Quick Reference

| BC | Project | Storage | Key Patterns |
|---|---|---|---|
| Participants | `CritterBids.Participants` | PostgreSQL / Marten | Event-sourced aggregate |
| Selling | `CritterBids.Selling` | PostgreSQL / Marten | Event-sourced aggregate, state machine |
| Auctions | `CritterBids.Auctions` | PostgreSQL / Marten | DCB, Auction Closing saga, Proxy Bid saga |
| Listings | `CritterBids.Listings` | PostgreSQL / Marten | Multi-stream projections, full-text search |
| Settlement | `CritterBids.Settlement` | PostgreSQL / Marten | Saga, financial event stream |
| Obligations | `CritterBids.Obligations` | PostgreSQL / Marten | Saga, cancellable scheduled messages |
| Relay | `CritterBids.Relay` | PostgreSQL / Marten | Wolverine handlers, SignalR hub |
| Operations | `CritterBids.Operations` | PostgreSQL / Marten | Cross-BC read-model projections, SignalR ops hub |

---

## Frontend (`client/`)

The frontend lives in `client/`, an npm-workspaces monorepo (ADR 025), separate from the .NET
solution (`CritterBids.slnx` does not reference it). Three static Vite + React + TypeScript SPAs
point at the same API host:

| App | Path | Audience | Auth | Live channel |
|---|---|---|---|---|
| Bidder-facing | `client/bidder/` | Public | Anonymous | `BiddingHub` (`/hub/bidding`) |
| Operations dashboard | `client/ops/` | Staff | `StaffToken` (ADR 024) | `OperationsHub` (`/hub/operations`) |
| Seller console | `client/seller/` | Public | Anonymous | `BiddingHub` (`/hub/bidding`) |

A `client/shared/` workspace member (`@critterbids/shared`) provides the wire-contract surface all
three apps share — the frontend analogue of `CritterBids.Contracts`. It contains:
- **SignalR integration**: a parameterised `createSignalRProvider<TMessage>()` factory (ADR 026
  pattern), connection builders, `RECEIVE_MESSAGE` constant, `FakeHubConnection` test helper
- **Shared theme**: the Tailwind v4 CSS-variable theme (`theme.css`) consumed by all three SPAs
- **Shared Zod schemas**: `catalogListingSchema` / `CatalogListing` (the one wire shape both
  bidder and seller consume)

Imports use `package.json` `"exports"` subpaths: `@critterbids/shared/signalr`,
`@critterbids/shared/schemas`, `@critterbids/shared/theme.css`.

A `client/e2e/` workspace member holds the Playwright end-to-end tests (M8-S7) — run locally
against the live Aspire stack, not in CI; see `client/e2e/README.md`. Dev uses a Vite dev-server
proxy to the API host (`http://localhost:5180`, `ws:true`) — no CORS, no API-host change; under
Aspire all dev servers launch as children (bidder `:5173`, ops `:5174`, seller `:5175`). The
library composition is **ADR 013** (TypeScript strict, Zod, TanStack Query, Tailwind v4 +
shadcn/ui, `@microsoft/signalr`, Vitest + Playwright, PWA); the layout, build-output, and
dev-server story are **ADR 025**.
See `docs/milestones/M8-frontend-spas.md` for the M8 (closed) milestone and
`docs/milestones/M9-seller-console.md` for the M9 seller console milestone.

---

## Skill Invocation Guide

Load the relevant skill before implementing. Skills encode hard-won patterns.

| Task | Skill to load |
|---|---|
| Adding a Wolverine handler | `docs/skills/wolverine-message-handlers/SKILL.md` |
| Creating a saga | `docs/skills/wolverine-sagas/SKILL.md` |
| Event-sourced aggregate (Marten) | `docs/skills/marten-event-sourcing/SKILL.md` |
| Marten projection | `docs/skills/marten-projections/SKILL.md` |
| DCB / Dynamic Consistency Boundary | `docs/skills/dynamic-consistency-boundary/SKILL.md` |
| Integration messaging | `docs/skills/integration-messaging/SKILL.md` |
| SignalR real-time | `docs/skills/wolverine-signalr/SKILL.md` |
| Writing tests | `docs/skills/critter-stack-testing-patterns/SKILL.md` |
| Cross-BC handler isolation in test fixtures | `docs/skills/critter-stack-testing-patterns/SKILL.md` |
| Running an Event Modeling workshop | `docs/skills/event-modeling/SKILL.md` |

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
