# CritterBids

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Marten-336791?logo=postgresql&logoColor=white)](https://martendb.io/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-4.x-FF6600?logo=rabbitmq&logoColor=white)](https://www.rabbitmq.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/erikshafer/CritterBids/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/erikshafer/CritterBids/actions/workflows/ci.yml)

> An open-source auction platform built on the [Critter Stack](https://wolverine.netlify.app/) — a reference architecture and live conference demo vehicle for event-driven .NET systems.

---

## What Is CritterBids?

CritterBids is a working, demonstrable auction platform built on the [Critter Stack](https://wolverine.netlify.app/) — JasperFx's suite of .NET libraries including **Wolverine** (messaging and command handling), **Marten** (event sourcing + document storage over PostgreSQL), and **Polecat** (used for SQL Server storage-swap demonstrations). The current branch runs on PostgreSQL via Marten (ADR 011); the Polecat path is preserved as a post-MVP storage-agnostic demo.

CritterBids is one of several open-source reference architectures showcasing the Critter Stack across different domains. The auction domain was chosen because competitive real-time bidding surfaces patterns — contention, time pressure, multi-audience projections — that simpler domains don't.

CritterBids is intended to be run, demonstrated, and learned from and not for actual live auctions.

---

## Why an Auction Platform?

Auctions are a natural fit for event-driven architecture. The core mechanic — competitive bidding under time pressure — is inherently event-driven, and the domain surfaces patterns that simpler examples don't:

| Pattern | How CritterBids demonstrates it |
|---|---|
| **Dynamic Consistency Boundaries (DCB)** | Concurrent bidders contending over the same listing — the canonical DCB scenario, not a contrived one |
| **Sagas and process managers** | Auction closing, proxy bid manager, post-sale obligations, and anti-snipe extended bidding are four distinct saga shapes |
| **Projections by audience** | Sellers, participants, ops staff, and finance all need radically different views of the same event streams |
| **Real-time transport** | SignalR is load-bearing — the live bid feed is the participant experience, not a demo flourish |
| **Storage agnosticism** | Current implementation runs on PostgreSQL via Marten; the same patterns are preserved for a post-MVP Polecat/SQL Server swap demo |
| **Transport agnosticism** | RabbitMQ for MVP, then a live swap to Azure Service Bus — one config change, no BC-level refactor |

Any audience can follow a live bidding session, whether or not they know what an event store is. That makes it an unusually effective teaching vehicle.

---

## Architecture

CritterBids is a **modular monolith** — a single deployable unit organized into well-enforced, loosely-coupled bounded context modules.

- Each bounded context (BC) is a separate .NET class library project
- BCs communicate exclusively through types in `CritterBids.Contracts` — no BC references another BC's internals
- The `CritterBids.Api` host wires all modules together at startup via `AddXyzModule()` extension methods
- All cross-BC messaging is via Wolverine — no direct handler-to-handler calls

This gives the boundary enforcement of microservices without the distributed systems overhead. The full stack runs on a single VPS.

### Bounded Contexts (MVP target)

| BC | Storage | Key Patterns |
|---|---|---|
| **Auctions** | PostgreSQL / Marten | DCB, Auction Closing saga, Proxy Bid saga |
| **Selling** | PostgreSQL / Marten | Event-sourced aggregate, listing state machine |
| **Listings** | PostgreSQL / Marten | Multi-stream projections, full-text search, watchlist |
| **Obligations** | PostgreSQL / Marten | Saga, cancellable scheduled messages, escalation chain |
| **Relay** | PostgreSQL / Marten | Wolverine handlers, SignalR push to participants |
| **Participants** | PostgreSQL / Marten | Event-sourced aggregate, anonymous session management |
| **Settlement** | PostgreSQL / Marten | Saga, financial event stream, reserve evaluation |
| **Operations** | PostgreSQL / Marten | Cross-BC read-model projections, SignalR ops dashboard |

All eight BC modules are active: Auctions, Selling, Listings, Participants, Settlement, Obligations, Relay, and Operations. All run on PostgreSQL via Marten (ADR 011 — All-Marten Pivot). The Polecat rationale is preserved as a post-MVP stretch goal where the swap itself becomes a live demonstration of the Critter Stack's storage-agnostic programming model.

---

## Tech Stack

| Concern | Tool |
|---|---|
| Language | C# 14 / .NET 10 |
| Message handling | [Wolverine 6+](https://wolverine.netlify.app/) |
| Event sourcing (PostgreSQL) | [Marten 9+](https://martendb.io/) |
| Async messaging | RabbitMQ (AMQP) |
| Real-time push | SignalR |
| Testing | xUnit + Shouldly + Testcontainers + Alba (backend) · Vitest + Playwright (frontend) |
| Frontend | React + TypeScript — two static Vite SPAs (bidder + ops dashboard) |
| Local orchestration | .NET Aspire 13.4+ |
| Deployment | Hetzner VPS |

---

## Two Listing Formats

### Timed Listings

The standard eBay-style format. A seller creates a listing, configures a duration (1, 3, 5, 7, or 10 days), and publishes it. The listing runs independently. The highest bidder at the scheduled close wins, subject to reserve.

### Flash Listings (Session-Based)

An Operations staff member creates a Session, attaches listings, and starts it. All listings open simultaneously and close together — typically 5 to 10 minutes later. Extended bidding can fire on individual listings.

Flash Sessions exist for live conference and meetup demonstrations. They use the same Auctions BC mechanics as timed listings — the same saga, the same DCB enforcement, the same anti-snipe logic. The Session is an optional coordination container, not a separate system.

---

## The Demo Scenario

The ideal live demonstration:

1. Presenter shows a QR code or URL
2. Audience scans — receives an anonymous session with a generated display name and a hidden credit ceiling
3. A Flash Session starts — three to five listings, five to ten minutes, everything live on the projector
4. Audience bids. Extended bidding fires. The ops dashboard shows saga state, bid feed, and settlement activity in real time.
5. Listings close. Winners are declared. The audience watched it happen.

This scenario directly shapes architectural decisions. Anonymous frictionless onboarding, SignalR reliability under load, a projector-legible ops dashboard, and a single Aspire-driven local run command are all first-class constraints — not afterthoughts.

---

## Getting Started

> **Note:** CritterBids is in active development, built out milestone by milestone. The MVP arc — all eight backend BCs plus both frontend SPAs — is complete; see [`docs/STATUS.md`](docs/STATUS.md) for the current snapshot.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or compatible Docker runtime)
- [Node.js 22+](https://nodejs.org/) (for React frontends)

### Run Locally

CritterBids uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) for local orchestration. A single command starts PostgreSQL, RabbitMQ, the API host, and both SPA dev servers — no separate `docker compose up`, no separate `npm run dev`. (Run `npm install` once in `client/` first.)

```bash
dotnet run --project src/CritterBids.AppHost --launch-profile http
```

The Aspire dashboard opens at `http://localhost:15237`. It shows live service health, structured logs, and distributed traces for all running resources. The bidder-facing app serves at `http://localhost:5173` and the staff ops dashboard at `http://localhost:5174` (both proxy `/api` and `/hub` to the API host — no CORS, see ADR 025). In Docker Desktop, the two infrastructure containers (PostgreSQL, RabbitMQ) appear grouped under **critterbids** in the Containers view.

#### HTTPS dashboard (optional)

To use the HTTPS profile instead, first trust the .NET development certificate if you haven't already:

```bash
dotnet dev-certs https --trust
```

Then run with the https profile:

```bash
dotnet run --project src/CritterBids.AppHost --launch-profile https
```

The dashboard will be at `https://localhost:17019`.

Both React SPAs shipped in M8 and live in `client/` (an npm-workspaces monorepo, ADR 025): the public **bidder-facing app** (`client/bidder/`, anonymous, live bidding over `BiddingHub`) and the staff **operations dashboard** (`client/ops/`, `StaffToken`-gated, live operator boards over `OperationsHub`). A third workspace member, `client/e2e/`, holds the Playwright end-to-end tests — including the two-bidder bid-war test — run locally against the live Aspire stack (`npm run e2e` from `client/`; see [`client/e2e/README.md`](client/e2e/README.md)).

---

## Project Structure

```
CritterBids/
├── src/
│   ├── CritterBids.AppHost/      # .NET Aspire orchestration — local dev entry point
│   ├── CritterBids.Api/          # Host — wires all BC modules together
│   ├── CritterBids.Contracts/    # Shared integration event types (the BC public API)
│   ├── CritterBids.Auctions/     # Auctions BC
│   ├── CritterBids.Selling/      # Selling BC
│   ├── CritterBids.Listings/     # Listings BC
│   ├── CritterBids.Participants/ # Participants BC
│   ├── CritterBids.Settlement/   # Settlement BC
│   ├── CritterBids.Obligations/  # Obligations BC
│   ├── CritterBids.Relay/        # Relay BC (SignalR push + reactive consumers)
│   └── CritterBids.Operations/  # Operations BC (cross-BC read-model projections, ops dashboard)
├── tests/
│   └── [BC integration and unit test projects]
├── client/                       # Frontend npm-workspaces monorepo (ADR 025)
│   ├── bidder/                   # Public bidder-facing SPA (Vite + React + TS)
│   ├── ops/                      # Staff operations dashboard SPA
│   └── e2e/                      # Playwright end-to-end tests (run against the live stack)
└── docs/
    ├── vision/       # Overview, BC map, domain event vocabulary
    ├── skills/       # Implementation pattern guides (load before implementing)
    ├── decisions/    # Architecture Decision Records (ADRs)
    ├── milestones/   # Scoped milestone definitions
    ├── narratives/   # Journey narratives (the specs slices anchor to)
    └── personas/     # Agent personas for Event Modeling workshops
```

---

## Documentation

| Document | Purpose |
|---|---|
| [`docs/STATUS.md`](docs/STATUS.md) | Derived project-status snapshot — where we are, what's next, deferred items, risks |
| [`docs/vision/README.md`](docs/vision/README.md) | Vision index — project overview, BC map, domain event vocabulary, reactive architecture notes |
| [`docs/skills/README.md`](docs/skills/README.md) | Skills index — load before implementing any feature |
| [`docs/decisions/`](docs/decisions/) | Architecture Decision Records |
| [`docs/milestones/MVP.md`](docs/milestones/MVP.md) | MVP definition of done and demo scenario |
| [`CLAUDE.md`](CLAUDE.md) | AI development entry point and coding conventions |

If you are contributing or exploring the codebase with an AI assistant, start with `CLAUDE.md`.

---

## Roadmap

**MVP** — A working, demonstrable auction platform suitable for a live conference demo with audience participation. Eight BCs and both listing formats on Marten/PostgreSQL, with Aspire-based local orchestration.

**Post-MVP milestones (planned):**

- Seller console (working name M9) — the seller-perspective SPA surfaces narratives 004/005/006 describe: publish, watch-close, fulfill-obligation
- `M-transport-swap` — Live swap from RabbitMQ to Azure Service Bus (configuration-only change, demonstrable during a conference talk)
- `M-storage-swap` — Migrate a BC's event store between Marten/PostgreSQL and Polecat/SQL Server, demonstrating the Critter Stack's storage-agnostic programming model
- Real payment processor integration (same saga shape, real Stripe wiring)
- Demo reset command cascade
- Feedback and reputation system

---

## CI Pipeline (Current)

GitHub Actions CI lives in [`.github/workflows/ci.yml`](.github/workflows/ci.yml) and runs on pushes/PRs targeting `main`.

- A `changes` job (via `dorny/paths-filter`) skips build/test execution for doc-only changes.
- The branch-protection-friendly required check is the final `CI` aggregator job.
- Code-path changes run restore/build, publish the API artifact, targeted unit tests (Contracts + selected Selling/Participants suites), and an integration matrix (Api, Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations).
- `client/**` changes run a `frontend` job: build + Vitest for both SPAs, plus a type-check of the e2e workspace member. The Playwright e2e itself runs locally, not in CI (it needs the full Aspire stack — a recorded M8-S7 deferral).

---

## Contributing

Contributions welcome. Before submitting a PR:

1. Read [`CLAUDE.md`](CLAUDE.md) for coding conventions and the non-negotiable modular monolith rules
2. Load the relevant skill file from [`docs/skills/`](docs/skills/) before implementing
3. Run `dotnet build` and `dotnet test` before committing
4. Do not commit directly to `main` — branch and PR

---

## Resources

- **Blog:** [event-sourcing.dev](https://www.event-sourcing.dev)
- **Wolverine:** [wolverine.netlify.app](https://wolverine.netlify.app/)
- **Marten:** [martendb.io](https://martendb.io/)
- **Polecat:** [polecat.netlify.app](https://polecat.netlify.app/)
- **JasperFx:** [jasperfx.github.io](https://jasperfx.github.io/)
- **Tools:** [JetBrains Rider](https://www.jetbrains.com/rider/), [DataGrip](https://www.jetbrains.com/datagrip/)

---

## Maintainer

**Erik "Faelor" Shafer**

[LinkedIn](https://www.linkedin.com/in/erikshafer/) · [Blog](https://www.event-sourcing.dev) · [YouTube](https://www.youtube.com/@event-sourcing) · [Bluesky](https://bsky.app/profile/erikshafer.bsky.social)
