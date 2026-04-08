# ADR 002 — RabbitMQ as Initial Message Transport

**Status:** Accepted  
**Date:** 2026-04

---

## Context

CritterBids requires an async message transport for inter-BC integration events. Wolverine supports multiple transports — RabbitMQ, Azure Service Bus, Amazon SQS, Kafka, and others — with a transport-agnostic programming model. The choice of transport should not affect BC-level code.

A secondary goal is to demonstrate Wolverine's transport-agnostic design by swapping transports as a visible milestone — ideally live during a conference talk.

---

## Decision

CritterBids uses **RabbitMQ** as its initial message transport for MVP.

The transport configuration lives exclusively in `CritterBids.Api/Program.cs`. No BC module contains transport-specific code. Integration events are published via `OutgoingMessages` — the Wolverine outbox pattern — which is transport-agnostic.

A future milestone (**M-transport-swap**) will demonstrate swapping from RabbitMQ to **Azure Service Bus** with a configuration-only change. This swap is designed to be performable live during a conference demo.

---

## Rationale

- RabbitMQ is well-understood from CritterSupply development
- Local dev story is simple — a single Docker Compose service
- The transport swap to Azure Service Bus is a more compelling conference story than staying on RabbitMQ or starting on ASB
- Kafka was considered but deferred — it is a different architectural pattern (log-based, consumer offsets) that warrants its own dedicated milestone, not an MVP concern

---

## Consequences

**Positive:**
- Simple local development — `docker compose up` includes RabbitMQ
- Familiar from CritterSupply — no new operational knowledge required for MVP
- Transport swap milestone is a meaningful showcase of Wolverine's design

**Negative:**
- RabbitMQ is not the most interesting transport story for enterprise audiences — mitigated by the swap milestone

---

## Future Milestones

- **M-transport-swap:** Swap RabbitMQ → Azure Service Bus live during a demo
- **Post-MVP (if pursued):** Kafka as a third transport option for the high-volume `BidPlaced` stream

---

## References

- `docs/milestones/MVP.md` — MVP scope
- `docs/milestones/M-transport-swap.md` — transport swap milestone definition (pending)
