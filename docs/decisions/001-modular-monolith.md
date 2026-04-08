# ADR 001 — Modular Monolith Architecture

**Status:** Accepted  
**Date:** 2026-04

---

## Context

CritterBids needs a deployment architecture that demonstrates the Critter Stack's bounded context discipline while remaining simple enough to run as a conference demo on a single Hetzner VPS with `docker compose up`.

Three options were considered:

**Option A — Microservices:** Each BC as a separate deployable service. Provides physical boundary enforcement and independent scaling. High operational overhead — service discovery, network latency, distributed tracing, container orchestration. Adds significant complexity with no benefit for a reference architecture or demo scenario.

**Option B — Traditional Monolith:** Single project, shared codebase, no internal boundary enforcement. Simple to run, but defeats the purpose of demonstrating BC discipline. Boundaries erode over time.

**Option C — Modular Monolith:** Single deployable unit internally organized into well-enforced, loosely-coupled BC modules. One process, one database per storage engine, no network hops between modules. Boundaries enforced by project structure and messaging conventions rather than network separation.

---

## Decision

CritterBids is structured as a **modular monolith** (Option C).

Each bounded context is a separate .NET class library project. Modules communicate exclusively through integration events defined in `CritterBids.Contracts`. No BC project references another BC project directly. The `CritterBids.Api` host project wires all modules together at startup via `AddXyzModule()` extension methods.

---

## Consequences

**Positive:**
- Single deployment unit — `docker compose up` works for the full stack
- BC boundaries are structurally enforced — compiler prevents direct BC-to-BC references
- Transport-agnostic by design — RabbitMQ configuration lives in one place in `Program.cs`; swapping transports is a config change, not a BC-level refactor
- Extraction path to microservices is clear — the Wolverine message-based communication means extraction is primarily a deployment concern, not a code refactor
- Operationally simple — no service discovery, no distributed tracing overhead for a demo

**Negative:**
- All BCs scale together — cannot scale the Auctions BC independently under high bid load
- A bug in one module can take down the whole process — mitigated by testing discipline

**Neutral:**
- Explicitly different from CritterSupply's structure — worth calling out in documentation as a deliberate architectural choice, not an oversight

---

## References

- `docs/vision/overview.md` — CritterBids vision and demo-first philosophy
- `src/CritterBids.Api/Program.cs` — module registration at startup
- `src/CritterBids.Contracts/` — shared integration event types
