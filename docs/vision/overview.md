# CritterBids — Vision Overview

## What Is CritterBids?

CritterBids is an open-source auction platform built on the [Critter Stack](https://wolverine.netlify.app/) — JasperFx's suite of .NET libraries including Wolverine (messaging and command handling), Marten (event sourcing and projections over PostgreSQL), and Polecat (event sourcing and projections over SQL Server).

It is a companion project to [CritterSupply](https://github.com/jasperfx/critter-supply), a Critter Stack e-commerce reference architecture. Where CritterSupply demonstrates the Critter Stack through an e-commerce domain, CritterBids demonstrates it through an auction domain — one with meaningfully different challenges, mechanics, and friction points.

CritterBids is not a toy. It is a realistic, working auction platform modeled after eBay's platform conventions, terminology, and user-facing design. It is built to be run, demonstrated, and learned from.

---

## Why an Auction Platform?

Auctions are a natural fit for showcasing event-driven architecture because the core mechanic — competitive bidding under time pressure — is inherently event-driven. Every bid is an event. Every clock tick matters. Every participant is reacting to what every other participant just did.

This domain produces genuine, legible drama that any audience can understand immediately, whether or not they know what an event store is.

More specifically, CritterBids was chosen because it creates meaningful opportunities to demonstrate:

- **Dynamic Consistency Boundaries (DCB)** — concurrent bidders contending over the same lot is the canonical DCB scenario. This is not a contrived example; it is the primary business mechanic.
- **Sagas and process managers** — the auction closing saga, the proxy bid manager, the post-sale obligations chain, and the extended bidding anti-snipe mechanic are all distinct saga shapes with different lifecycles.
- **Projections by audience** — a seller, a bidder, an ops staff member, and a finance team all need radically different views of the same underlying event streams.
- **Real-time transport** — SignalR is load-bearing in CritterBids, not a demo flourish. The live bid feed is the participant experience.
- **Storage agnosticism** — CritterBids runs all eight BCs on PostgreSQL via Marten for a uniform bootstrap across the solution. The Critter Stack's programming model is storage-agnostic by design — the same patterns work against Marten on PostgreSQL or Polecat on SQL Server — preserved as a future demo opportunity.
- **Transport agnosticism** — CritterBids starts with RabbitMQ and is designed to swap to Azure Service Bus as a milestone demonstration of Wolverine's transport-agnostic design.

---

## The eBay Model

CritterBids is deliberately modeled after eBay rather than a traditional auction house. This means:

- **Sellers are self-service.** There is no platform appraiser or intake coordinator. Sellers create their own listings, set their own parameters, and own their own reserve prices.
- **The platform is a coordinator, not an orchestrator.** CritterBids facilitates transactions between sellers and buyers. It takes a fee. It does not own the inventory.
- **Terminology follows eBay conventions.** Listings, starting bids, reserve prices, Buy It Now, final value fees — these are the words CritterBids uses because they are the words participants already know.
- **Buyers pay what they bid.** Fees are charged to the seller post-sale as a percentage of the final price, not added on top for the buyer.

Where CritterBids diverges from eBay, it does so deliberately. Extended bidding (anti-sniping) is seller-configurable — eBay uses hard deadlines, CritterBids makes this a choice. Flash auctions (short, session-based, for live demos) are a CritterBids addition with no eBay equivalent.

---

## Two Listing Formats

CritterBids supports two distinct auction formats that coexist within the same platform.

### Timed Listings

The standard eBay-style format. A seller creates a listing, configures a duration (1, 3, 5, 7, or 10 days), and publishes it. The listing runs independently — no container, no coordinated start. The highest bidder at the scheduled close time wins, subject to reserve.

This is the primary format for a production CritterBids deployment.

### Flash Listings (Session-Based)

A platform-created Session groups multiple listings together with a shared start time and a short configured duration — typically 5 to 10 minutes. When an Operations staff member starts a Session, all attached listings open for bidding simultaneously and close together.

This format exists primarily for live conference and meetup demonstrations. It gives a presenter control over pacing, a clear narrative arc, and a climax moment when everything closes and winners are declared.

Flash listings are a first-class feature of CritterBids, not a demo hack. They use the same Auctions BC mechanics as timed listings — the same saga, the same DCB enforcement, the same extended bidding logic. The Session is simply an optional coordination container inside the Auctions BC.

---

## The Demo-First Philosophy

CritterBids is designed with live audience participation in mind. The ideal conference demonstration looks like this:

1. Presenter shows a QR code or URL
2. Audience members scan and are assigned an anonymous session — a generated display name and a hidden credit ceiling
3. A Flash Session starts — three to five lots, five minutes each, everything live on the presenter's projector
4. Audience bids. Extended bidding fires. The ops dashboard shows saga state, bid feed, and settlement activity in real time.
5. Lots close. Winners are declared. The audience can see it happen.

This scenario imposes real constraints on the architecture:

- **Anonymous, frictionless onboarding.** No email. No password. Scan and bid.
- **SignalR must be reliable under load.** A room of 40 developers all bidding simultaneously is the test.
- **The ops dashboard must be legible from a projector.** Real-time, high-contrast, low-clutter.
- **Deployment must be simple.** A Hetzner VPS with a single `docker compose up`. The QR code points to a stable URL the presenter controls, not localhost.

Every architectural decision in CritterBids should be evaluated against this scenario. If a design choice makes the live demo harder, it needs a strong justification.

---

## Architecture Summary

CritterBids is structured as a **modular monolith** — a single deployable unit internally organized into well-enforced, loosely-coupled bounded context modules.

Each bounded context is a separate .NET class library project. Modules communicate exclusively through integration events defined in a shared `CritterBids.Contracts` project. No module references another module's internals directly. The `CritterBids.Api` host project wires all modules together at startup through each module's `AddXyzModule()` extension method.

This structure provides the boundary enforcement benefits of microservices without the distributed systems operational overhead. It also makes the transport-swap story legible — RabbitMQ is configured in one place in `Program.cs`, and swapping to Azure Service Bus is a configuration change, not a BC-level refactor.

**Storage:**
- PostgreSQL via Marten — Auctions, Listings, Selling, Obligations, Relay BCs
- SQL Server via Polecat — Operations, Settlement, Participants BCs

**Messaging:** RabbitMQ (MVP) → Azure Service Bus (milestone swap demonstration)

**Real-time:** SignalR — bid feed (participant-facing), ops live board (staff-facing)

**Frontend:** React — two SPAs sharing one backend API host. `critterbids-web` (participant-facing) and `critterbids-ops` (staff-facing).

**Deployment:** Hetzner VPS, Docker Compose.

---

## Relationship to CritterSupply

CritterBids and CritterSupply are sibling projects, not competing ones. They share the same underlying technology — the Critter Stack — and the same philosophy: real code, real patterns, no hand-waving.

| Concern | CritterSupply | CritterBids |
|---|---|---|
| Domain | E-commerce | Auction platform |
| Architecture | Vertical slice, multi-service oriented | Modular monolith |
| Primary saga | Order fulfillment | Auction closing + proxy bid manager |
| DCB usage | Promotions BC | Auctions BC — core mechanic |
| Identity | JWT, real users | Anonymous sessions, generated display names |
| Real-time | Minimal | SignalR load-bearing |
| Database | PostgreSQL only | PostgreSQL + SQL Server |
| Demo format | Browse and buy | Live audience participation bidding |

A developer who has worked through CritterSupply will find familiar patterns in CritterBids — the same Wolverine handler conventions, the same Marten projection patterns, the same BC boundary discipline. What they will also find are new patterns the e-commerce domain didn't surface: timer-driven saga initiation, proxy bid process managers, real-time contention at scale, and the Polecat SQL Server story.

---

## What CritterBids Is Not

- It is not a production-ready auction platform. It is a reference architecture and teaching vehicle.
- It is not a microservices showcase. The modular monolith is a deliberate architectural choice, not a stepping stone to service extraction.
- It is not a Blazor project. The React frontend is an intentional statement that .NET backends pair naturally with non-Microsoft frontend technology.
- It is not feature-complete. Milestones are scoped deliberately. MVP is a working demo platform, not a full eBay competitor.
