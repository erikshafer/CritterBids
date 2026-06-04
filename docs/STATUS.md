# CritterBids — Project Status Snapshot

**As of:** 2026-06-03 · `m7-s7-end-to-end-integration-housekeeping` branch (M7-S7 in progress, pending PR)
**Derived from:** [`retrospectives/M7-S7-end-to-end-integration-housekeeping-retrospective.md`](./retrospectives/M7-S7-end-to-end-integration-housekeeping-retrospective.md) (latest session retro), [`retrospectives/M7-retrospective.md`](./retrospectives/M7-retrospective.md) (latest milestone retro), [`milestones/M7-operations-bc.md`](./milestones/M7-operations-bc.md), [`decisions/README.md`](./decisions/README.md), [`decisions/PARKED.md`](./decisions/PARKED.md), `.github/workflows/ci.yml`

> **This document is a derived snapshot, not a source of truth.** The canonical session-close
> state is always the **most recent retrospective** in `docs/retrospectives/`. If this snapshot
> and a newer retrospective disagree, the retrospective wins. Any agent (Claude Code, GitHub
> Copilot, a local LLM) answering "where are we?" should read this file first, then verify
> against the newest retro before acting.
>
> **Regeneration rule:** refresh this file at session close (or whenever it is found stale) by
> re-deriving every section from the newest retrospective, the active milestone doc, the ADR
> index, and the deferred-items tables. Update the "As of" line with the date and `main` commit.

---

## 1. Where Are We?

CritterBids has **completed M7 (Operations BC)** — the eighth and final MVP backend bounded context.
**All seven M7 slices are complete** (S1–S7).

- **All 8 production BC projects exist in `src/`**: Participants, Selling, Auctions, Listings,
  Settlement, Obligations, Relay, Operations — plus Api, AppHost, Contracts.
- **Build state at last session close:** 0 errors / 0 warnings; **281 tests, all green**
  (full local `dotnet test CritterBids.slnx`).
- **Engine baseline:** Wolverine 6.2.2 / Marten 9 / JasperFx 2 (upgraded mid-M6, PR #55).
- **Auth posture:** ADR-024 implemented (M7-S6). Staff surfaces gated by `StaffToken` scheme +
  `StaffOnly` policy. Participant-facing endpoints remain `[AllowAnonymous]`.
- **Development tooling note:** M7 spanned three agents: GitHub Copilot (S1–S4), Claude Code
  (S5–S6), Windsurf/Cascade (S7). The prompt → execute → retro loop and all conventions are
  tool-agnostic and unchanged.

### Milestone ladder

| Milestone | Scope | Status |
|---|---|---|
| M1 | Skeleton + Participants BC | ✅ Complete |
| M2 / M2.5 | Selling + Listings pipeline | ✅ Complete |
| M3 | Auctions BC core (DCB, Auction Closing saga) | ✅ Complete |
| M4 | Auctions completion (Proxy Bid saga, Session aggregate, WithdrawListing) | ✅ Complete |
| Foundation refresh | Spec-anchored development retrofit (ADR 016–018), narratives, workshops | ✅ Complete |
| M5 | Settlement BC (payment saga, financial event stream) | ✅ Complete |
| M6 | Obligations BC (post-sale saga) + Relay BC (SignalR push) | ✅ Complete (203 tests at close) |
| **M7** | **Operations BC (operator read models + staff auth)** | ✅ Complete (281 tests at close) |
| M8 | React frontend SPAs (bidder + ops dashboard) | ⏳ Not started (depends on M7) |

### M7 slice progress

| Slice | Scope | Status |
|---|---|---|
| M7-S1 | Foundation decisions: ADR-024 (staff auth), OpenSpec **decline**, W006 read-model field freeze | ✅ Done (PR #64) |
| M7-S2 | Operations BC scaffold + settlement-queue consumer (`SettlementQueueView`) | ✅ Done (PR #66) |
| M7-S3 | Lot board upsert view + bid-activity append feed | ✅ Done (PR #68) |
| M7-S4 | `OperationsObligationsView` — escalation + dispute queues | ✅ Done (PR #70) |
| M7-S5 | Session & participant activity board (`operations-participants-events` + session events) | ✅ Done |
| M7-S6 | Staff auth gating (`StaffToken`/`StaffOnly` per ADR-024) + query endpoints over all views | ✅ Done |
| M7-S7 | End-to-end cross-BC journey test, route audit, `bounded-contexts.md` status flip, M7 retro | ✅ Done |

---

## 2. What's Up Next?

### Immediate: M8 — React Frontend SPAs

React + TypeScript SPAs (Vite per ADR 012; stack per ADR 013, still **Proposed**). M8-S1 should
accept/revise ADR-013, settling the frontend core stack.

Two SPAs planned:
- **Bidder-facing app** — public catalog + live bidding via `BiddingHub`
- **Staff ops dashboard** — operator views + live feed via `OperationsHub` (StaffOnly-gated)

Key M8 concerns:
- Render-time `Title` join: Operations API returns `ListingId` only; frontend resolves display
  titles from `/api/listings/{id}`
- Relay push = re-query signal: `OperationsHub` pushes are notifications to refresh, not
  authoritative data (M7 milestone §5)
- Staff auth: `X-Staff-Token` header for HTTP; `access_token` query string for OperationsHub
  WebSocket connections
- CI matrix extension: Settlement, Obligations, Relay, Operations tests run locally only

---

## 3. Deferred / Delayed Ledger

### Deferred past M7 (tracked, with target)

| Item | Deferred at | Target |
|---|---|---|
| `OperationsHub` staff-group targeting (currently `Clients.All`) | M7-S1 fork #4 / ADR-024 item 6 | Post-MVP Relay edit |
| Render-time `Title` join (lot board / obligations view show `ListingId` only) | M7-S3/S4 | M8 (frontend render concern) |
| `marten-projections` skill: non-monotone state-machine guard section (terminal-absorbing + open backward edge) | M7-S4 | Future skill-update session |
| `wolverine-http-auth` skill codifying default-scheme trap / hub-path credential patterns | M7-S6 gap | Future skill-authoring session |
| Marten event-type alias / upgrade pass for prior namespace promotions | M5 | First-real-deploy retrospective |
| CI matrix extension (Settlement, Obligations, Relay, Operations) | M6 Risk #1 | Standalone CI PR or M8-S1 |

### Post-MVP (explicitly out of scope)

| Item | Source |
|---|---|
| `Refund` settlement-reversal / compensation mechanics on `DisputeResolved` | M6/M7 non-goals |
| Real carrier-tracking webhook (`ProvideTracking` is an in-process stub) | M6 non-goals |
| Email / SMS / push delivery (Relay is SignalR-only; stubs log) | M6 non-goals |
| Per-user staff identity, roles, external IdP (swap behind unchanged `StaffOnly` policy) | ADR-024 revisit trigger |
| `DemoResetInitiated` cascade (MVP demo reset = Docker volume removal) | M7 non-goals |
| Long tail of "all significant events → Operations" beyond the M7-enumerated set | M7 §2 scope ceiling |
| Operations runbook / SRE docs (P-001 — triggers on first production-leaning deployment) | `decisions/PARKED.md` |
| Demo-script runbook (P-002 — triggers when a conference talk is scheduled) | `decisions/PARKED.md` |
| Transport swap demo (RabbitMQ → Azure Service Bus) | ADR 002 |

---

## 4. Current Risks

| # | Risk | Severity | Notes / Mitigation |
|---|---|---|---|
| 1 | ~~**CI does not run Settlement, Obligations, Relay, or Operations tests.**~~ **RESOLVED** — the `.github/workflows/ci.yml` integration matrix now covers all 8 BCs + Api (Settlement, Obligations, Relay, Operations added alongside the original Api, Participants, Selling, Auctions, Listings). The full 281-test suite now runs on every code-path PR. | ~~High~~ Resolved | Closed by the CI matrix extension. No further action. |
| 2 | **ADR 013 (frontend core stack) is still Proposed**, and M8 depends on it. Routing and auth-client patterns are explicitly deferred inside it. | Medium | Accept/revise ADR 013 at M8 opening (M8-S1 foundation decisions). |
| 3 | **Eventual-consistency contract between Relay push and Operations read models** is documented but not yet exercised by any client. The M8 dashboard must treat pushes as "re-query" signals, not read-your-own-write. | Low-Medium | M7 milestone §5 documents the contract; M8-S1 must carry it into the frontend design. |
| 4 | **Occasional Testcontainers flakes** in Auctions and Settlement test suites (timing-sensitive container startup). Both pass on rerun. | Low | Not caused by any code change; inherent to Testcontainers PostgreSQL startup variance. |
| 5 | **Doc nit:** the M7-S4 retro header says `**Date:** 2025-02-14` — wrong year/date (should be ~2026-05-31). | Trivial | Fix in the next docs PR. |

---

## 5. Key Numbers (at M7 close)

- **Tests:** 281 passing — Auctions 65, Api 41, Operations 38, Selling 36, Relay 36, Settlement 25, Listings 20, Obligations 13, Participants 6, Contracts 1
- **ADRs:** 24 authored (next unreserved: **025**); 013 Proposed; 003/008/010 superseded
- **BCs in `src/`:** 8 of 8 MVP backend BCs — all active
- **Operator read models shipped:** 6 of 6 (settlement queue, lot board, bid-activity feed, obligations view, session activity, participant activity)
- **RabbitMQ `operations-*` queues wired:** settlement, auctions, selling, obligations, participants — all 5 active
- **Staff-gated endpoints:** 7 query + 4 mutation + 1 hub (OperationsHub)
- **OpenSpec adoption:** Obligations ✅ adopted · Relay ❌ declined · Operations ❌ declined

---

## 6. Where to Look (reference map for agents)

| Question | Read |
|---|---|
| What are the conventions / hard rules? | `CLAUDE.md` (root) |
| What happened in the last session? | Newest file in `docs/retrospectives/` (canonical state) |
| What is the current milestone's scope? | `docs/milestones/M7-operations-bc.md` |
| What field set is frozen for Operations views? | `docs/workshops/006-operations-source-audit.md` (W006) |
| Why is the architecture this way? | `docs/decisions/README.md` (ADR index) |
| What was deliberately deferred? | `docs/decisions/PARKED.md` + §3 of this file |
| How do I implement pattern X? | `docs/skills/README.md` (skill index) |
| How do sessions run? | `docs/prompts/README.md` + `docs/retrospectives/README.md` |
| BC boundaries and integration topology? | `docs/vision/bounded-contexts.md` |
| Event vocabulary? | `docs/vision/domain-events.md` |

---

## Document History

- **v0.2** (2026-06-03): Regenerated at M7 close (all 7 slices complete, 281 tests). Derived
  from the M7-S7 retro, M7 milestone retro, M7 milestone doc, ADR index, PARKED.md, and the
  CI workflow. Milestone ladder, risks, key numbers, and deferred items fully updated.
- **v0.1** (2026-05-31): Authored at the Copilot → Claude Code tooling transition, immediately
  after M7-S4 merged (PR #70). Derived from the M7-S4 retro, M6 milestone retro, M7 milestone
  doc, ADR index, PARKED.md, and the CI workflow. First snapshot of its kind; regeneration rule
  recorded in the header.
