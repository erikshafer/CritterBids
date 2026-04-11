# CritterBids

[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Marten-336791?logo=postgresql&logoColor=white)](https://martendb.io/)
[![SQL Server](https://img.shields.io/badge/SQL_Server-Polecat-CC2927?logo=microsoftsqlserver&logoColor=white)](https://wolverine.netlify.app/)
[![RabbitMQ](https://img.shields.io/badge/RabbitMQ-4.x-FF6600?logo=rabbitmq&logoColor=white)](https://www.rabbitmq.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Build](https://github.com/erikshafer/CritterSupply/actions/workflows/dotnet.yml/badge.svg?branch=main)](https://github.com/erikshafer/CritterBids/actions/workflows/ci.yml)

> An open-source auction platform built on the [Critter Stack](https://wolverine.netlify.app/) — a reference architecture and live conference demo vehicle for event-driven .NET systems.

---

## What Is CritterBids?

CritterBids is a working, demonstrable auction platform modeled after eBay's conventions and terminology. It is built on **Wolverine** (messaging and command handling), **Marten** (event sourcing over PostgreSQL), and **Polecat** (event sourcing over SQL Server) — JasperFx's suite of .NET libraries collectively known as the [Critter Stack](https://wolverine.netlify.app/).

CritterBids is one of several open-source reference architectures showcasing the Critter Stack across different domains. The auction domain was chosen because competitive real-time bidding surfaces patterns — contention, time pressure, multi-audience projections — that simpler domains don't.

CritterBids is not a toy. It is a realistic platform designed to be run, demonstrated, and learned from.

---

## Why an Auction Platform?

Auctions are a natural fit for event-driven architecture. The core mechanic — competitive bidding under time pressure — is inherently event-driven, and the domain surfaces patterns that simpler examples don't:

| Pattern | How CritterBids demonstrates it |
|---|---|
| **Dynamic Consistency Boundaries (DCB)** | Concurrent bidders contending over the same lot — the canonical DCB scenario, not a contrived one |
| **Sagas and process managers** | Auction closing, proxy bid manager, post-sale obligations, and anti-snipe extended bidding are four distinct saga shapes |
| **Projections by audience** | Sellers, participants, ops staff, and finance all need radically different views of the same event streams |
| **Real-time transport** | SignalR is load-bearing — the live bid feed is the participant experience, not a demo flourish |
| **Mixed storage engines** | PostgreSQL via Marten for auction-core BCs; SQL Server via Polecat for operations and finance |
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

### Bounded Contexts

| BC | Storage | Key Patterns |
|---|---|---|
| **Auctions** | PostgreSQL / Marten | DCB, Auction Closing saga, Proxy Bid saga |
| **Selling** | PostgreSQL / Marten | Event-sourced aggregate, listing state machine |
| **Listings** | PostgreSQL / Marten | Multi-stream projections, full-text search, watchlist |
| **Obligations** | PostgreSQL / Marten | Saga, cancellable scheduled messages, escalation chain |
| **Relay** | PostgreSQL / Marten | Wolverine handlers, SignalR push to participants |
| **Participants** | SQL Server / Polecat | Event-sourced aggregate, anonymous session management |
| **Settlement** | SQL Server / Polecat | Saga, financial event stream, reserve evaluation |
| **Operations** | SQL Server / Polecat | Cross-BC projections, SignalR ops dashboard |

The SQL Server BCs reflect a real organizational pattern: finance and ops data belongs where BI tooling and compliance teams can query it directly, without an ETL layer.

---

## Tech Stack

| Concern | Tool |
|---|---|
| Language | C# 14 / .NET 10 |
| Message handling | [Wolverine 5+](https://wolverine.netlify.app/) |
| Event sourcing (PostgreSQL) | [Marten 8+](https://martendb.io/) |
| Event sourcing (SQL Server) | Polecat 2+ |
| Async messaging | RabbitMQ (AMQP) |
| Real-time push | SignalR |
| Testing | xUnit + Shouldly + Testcontainers + Alba |
| Frontend | React + TypeScript |
| Local orchestration | .NET Aspire 13.2+ |
| Deployment | Hetzner VPS |

---

## Two Listing Formats

### Timed Listings

The standard eBay-style format. A seller creates a listing, configures a duration (1, 3, 5, 7, or 10 days), and publishes it. The listing runs independently. The highest bidder at the scheduled close wins, subject to reserve.

### Flash Listings (Session-Based)

An Operations staff member creates a Session, attaches listings, and starts it. All listings open simultaneously and close together — typically 5 to 10 minutes later. Extended bidding can fire on individual lots.

Flash Sessions exist for live conference and meetup demonstrations. They use the same Auctions BC mechanics as timed listings — the same saga, the same DCB enforcement, the same anti-snipe logic. The Session is an optional coordination container, not a separate system.

---

## The Demo Scenario

The ideal live demonstration:

1. Presenter shows a QR code or URL
2. Audience scans — receives an anonymous session with a generated display name and a hidden credit ceiling
3. A Flash Session starts — three to five lots, five to ten minutes, everything live on the projector
4. Audience bids. Extended bidding fires. The ops dashboard shows saga state, bid feed, and settlement activity in real time.
5. Lots close. Winners are declared. The audience watched it happen.

This scenario directly shapes architectural decisions. Anonymous frictionless onboarding, SignalR reliability under load, a projector-legible ops dashboard, and a single `docker compose up` deployment are all first-class constraints — not afterthoughts.

---

## Getting Started

> **Note:** CritterBids is in active early development. The `src/` projects are being built out milestone by milestone. The instructions below reflect the intended local development workflow.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or compatible Docker runtime)
- [Node.js 22+](https://nodejs.org/) (for React frontends)

### Run Locally

CritterBids uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/get-started/aspire-overview) for local orchestration. A single command starts PostgreSQL, SQL Server, RabbitMQ, and the API host — no separate `docker compose up` needed.

```bash
dotnet run --project src/CritterBids.AppHost --launch-profile http
```

The Aspire dashboard opens at `http://localhost:15237`. It shows live service health, structured logs, and distributed traces for all running resources. In Docker Desktop, the three infrastructure containers (PostgreSQL, SQL Server, RabbitMQ) appear grouped under **critterbids** in the Containers view.

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

The participant frontend (`critterbids-web`) and ops dashboard (`critterbids-ops`) are separate React apps in `src/frontend/`.

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
│   ├── CritterBids.Obligations/  # Obligations BC
│   ├── CritterBids.Relay/        # Relay BC (SignalR push)
│   ├── CritterBids.Participants/ # Participants BC
│   ├── CritterBids.Settlement/   # Settlement BC
│   ├── CritterBids.Operations/   # Operations BC
│   └── frontend/
│       ├── critterbids-web/      # Participant-facing SPA
│       └── critterbids-ops/      # Staff ops dashboard
├── tests/
│   └── [BC integration and unit test projects]
└── docs/
    ├── vision/       # Overview, BC map, domain event vocabulary
    ├── skills/       # Implementation pattern guides (load before implementing)
    ├── decisions/    # Architecture Decision Records (ADRs)
    ├── milestones/   # Scoped milestone definitions
    └── personas/     # Agent personas for Event Modeling workshops
```

---

## Documentation

| Document | Purpose |
|---|---|
| [`docs/vision/overview.md`](docs/vision/overview.md) | Full project vision, eBay model, architecture summary |
| [`docs/skills/README.md`](docs/skills/README.md) | Skills index — load before implementing any feature |
| [`docs/decisions/`](docs/decisions/) | Architecture Decision Records |
| [`docs/milestones/MVP.md`](docs/milestones/MVP.md) | MVP definition of done and demo scenario |
| [`CLAUDE.md`](CLAUDE.md) | AI development entry point and coding conventions |

If you are contributing or exploring the codebase with an AI assistant, start with `CLAUDE.md`.

---

## Roadmap

**MVP** — A working, demonstrable auction platform suitable for a live conference demo with audience participation. All 8 BCs, both listing formats, both React frontends, full `docker compose up` deployment.

**Post-MVP milestones (planned):**

- `M-transport-swap` — Live swap from RabbitMQ to Azure Service Bus (configuration-only change, demonstrable during a conference talk)
- `M-polecat-marten-swap` — Swap a Polecat BC to Marten, demonstrating JasperFx's storage-agnostic programming model
- Real payment processor integration (same saga shape, real Stripe wiring)
- Demo reset command cascade
- Feedback and reputation system

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
- **JasperFx:** [jasperfx.github.io](https://jasperfx.github.io/)
- **Tools:** [JetBrains Rider](https://www.jetbrains.com/rider/), [DataGrip](https://www.jetbrains.com/datagrip/)

---

## Maintainer

**Erik "Faelor" Shafer**

[LinkedIn](https://www.linkedin.com/in/erikshafer/) · [Blog](https://www.event-sourcing.dev) · [YouTube](https://www.youtube.com/@event-sourcing) · [Bluesky](https://bsky.app/profile/erikshafer.bsky.social)
