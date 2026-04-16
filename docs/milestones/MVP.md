# Milestone: MVP

**Goal:** A working, demonstrable auction platform suitable for a live conference demo with audience participation.

---

## Definition of Done

MVP is complete when:

- A participant can scan a QR code or hit a URL, receive an anonymous session with a generated display name and hidden credit ceiling, and place bids
- A seller can create a listing with a starting bid, optional reserve, optional Buy It Now price, optional extended bidding config, and publish it
- The Auctions BC processes bids, enforces the DCB under concurrent load, runs the proxy bid manager saga, and drives the auction closing saga
- The Flash Session format works — Operations staff can create a Session, attach listings, start the Session, and all listings open simultaneously
- Extended bidding fires correctly when a bid arrives within the configured window — the scheduled close message is canceled and rescheduled
- `ListingSold` triggers the Settlement saga — winner's credit ceiling is charged, seller payout is calculated
- `SettlementCompleted` triggers the Obligations saga — seller receives shipping reminders, timeout chain runs
- The Listings BC projects a browsable catalog with search and watchlist
- The Relay BC pushes real-time notifications via SignalR — outbid alerts, extended bidding, won/lost
- The Operations dashboard shows live lot activity, bid feed, saga states, and settlement queue via SignalR
- Both React frontends (`critterbids-web` and `critterbids-ops`) are functional and demo-ready
- The full stack runs with `docker compose up` on a Hetzner VPS
- A QR code points to a stable URL

---

## In Scope for MVP

- All 8 BCs with their core event models and sagas
- Timed listings (eBay-style, seller-configured duration)
- Flash listings (Session-based, short duration, Operations-controlled)
- Anonymous participant sessions — generated display name, hidden credit ceiling, no email/password
- Seller self-service listing creation and management
- Proxy bid manager (auto-bid up to credit ceiling)
- Auction closing saga with anti-snipe extended bidding (seller-configurable)
- Buy It Now mechanic (removes after first bid)
- Reserve price (confidential, Settlement-evaluated)
- Settlement saga (virtual credit, no real payment processor)
- Obligations saga with scheduled reminder and escalation chain
- Dispute workflow (basic — open, resolve, close)
- Relay BC — SignalR push for in-session notifications
- Operations dashboard — live board, demo controls
- PostgreSQL (Marten) for all eight BCs — All-Marten pivot per ADR 011; the original Polecat/SQL
  Server rationale for Participants, Settlement, and Operations is preserved as a post-MVP stretch
  goal (see ADR 003 and ADR 011)
- RabbitMQ for inter-BC messaging
- React frontends for both participant and ops surfaces
- Docker Compose deployment
- Seed data for demo — pre-approved listings, demo Session ready to start

---

## Explicitly Out of Scope for MVP

These are deferred, not discarded. Each is a documented post-MVP milestone or backlog item.

| Item | Notes |
|---|---|
| Real payment processor integration | Credit ceiling is virtual — same saga shape, no Stripe wiring |
| Carrier tracking webhook | Obligations BC has the seam stubbed |
| Full feedback / reputation system | Seam designed post-`ObligationFulfilled`; data-seeded for demo |
| Multi-tenancy | Banked — worth discussing in event modeling but not implementing in MVP |
| Demo reset command cascade | Docker volume removal is sufficient for MVP |
| Transport swap (RabbitMQ → Azure Service Bus) | Milestone M-transport-swap, post-MVP |
| Polecat ↔ Marten swap | Stretch goal, post-MVP |
| Kafka transport | Post-MVP if pursued |
| Email / SMS notifications | Relay BC has the seam; SignalR push is sufficient for demo |
| Advanced seller tooling | Relist, bulk listing, promoted listings |

---

## Demo Scenario (MVP Success Criteria)

The following scenario must work end-to-end for MVP to be considered complete:

1. Presenter opens `critterbids-ops`, creates a Flash Session with 3 listings attached
2. Presenter shows QR code — audience scans, receives anonymous sessions
3. Presenter starts the Session — all 3 listings open simultaneously
4. Audience places bids — proxy bids, outbid notifications, extended bidding fires on at least one lot
5. Listings close — winners declared, ops dashboard shows settlement saga completing
6. Obligations saga starts — seller prompted to ship (mocked/seeded in demo)
7. Presenter shows ops dashboard throughout — saga state, bid feed, message activity visible

Total runtime: 10–15 minutes.
