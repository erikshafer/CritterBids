# CritterBids — GitHub Copilot Instructions

CritterBids is a .NET auction platform built on the Critter Stack (Wolverine + Marten + Polecat), structured as a modular monolith. It is modeled after eBay's platform conventions.

## Architecture

- **Modular monolith** — one deployable (`CritterBids.Api`), eight BC modules as separate projects
- **No BC references another BC** — shared types live only in `CritterBids.Contracts`
- **Wolverine** for message handling, sagas, and HTTP endpoints
- **Marten** (PostgreSQL) for Auctions, Selling, Listings, Obligations, Relay BCs
- **Polecat** (SQL Server) for Participants, Settlement, Operations BCs
- **RabbitMQ** for inter-BC integration events
- **SignalR** for real-time bid feed and ops dashboard
- **React + TypeScript** for both frontend SPAs

## Non-Negotiable Conventions

- `sealed record` for all commands, events, queries, and read models
- `IReadOnlyList<T>` not `List<T>` for collections
- Handlers return events/messages — never call `session.Store()` directly
- All saga terminal paths call `MarkCompleted()`
- `opts.Policies.AutoApplyTransactions()` in every BC's Marten/Polecat config
- `[Authorize]` on all non-auth endpoints
- Integration events via `OutgoingMessages` — never `IMessageBus` directly
- `bus.ScheduleAsync()` is the only justified `IMessageBus` use in handlers
- UUID v5 stream IDs with BC-specific namespace prefixes
- No "Event" suffix on domain event type names
- No "paddle" references — participants use `BidderId`

## Bounded Contexts

| BC | Project | Storage |
|---|---|---|
| Participants | `CritterBids.Participants` | SQL Server / Polecat |
| Selling | `CritterBids.Selling` | PostgreSQL / Marten |
| Auctions | `CritterBids.Auctions` | PostgreSQL / Marten |
| Listings | `CritterBids.Listings` | PostgreSQL / Marten |
| Settlement | `CritterBids.Settlement` | SQL Server / Polecat |
| Obligations | `CritterBids.Obligations` | PostgreSQL / Marten |
| Relay | `CritterBids.Relay` | PostgreSQL / Marten |
| Operations | `CritterBids.Operations` | SQL Server / Polecat |

## Key Domain Vocabulary

- **Listing** — the thing being auctioned (not "lot" in public-facing contexts)
- **Sale / Flash Session** — container for grouped listings (flash/demo format only)
- **Starting Bid** — minimum first bid (not "opening bid")
- **Reserve** — confidential minimum; never revealed to bidders
- **Hammer Price** — final bid at close, before fees
- **Final Value Fee** — platform fee charged to seller (not buyer)
- **Extended Bidding** — anti-snipe timer extension, seller-configurable
- **BidderId** — participant identifier (never "paddle")
- **ListingSold** — happy path close outcome
- **ListingPassed** — no bids or reserve not met

## Do Not

- Add a project reference from one BC to another BC
- Use `IMessageBus` in a handler (except `ScheduleAsync`)
- Use `List<T>` on records or aggregates
- Name a domain event with an "Event" suffix
- Reference "paddle" in any code
- Commit without `dotnet build` passing

## Read First

Before implementing, read `CLAUDE.md` and load the relevant skill file from `docs/skills/`.
