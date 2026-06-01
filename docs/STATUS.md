# CritterBids — Project Status Snapshot

**As of:** 2026-05-31 · `main` @ `669998e` (PR #70, M7-S4 merged)
**Derived from:** [`retrospectives/M7-S4-obligations-view-escalation-dispute-queues-retrospective.md`](./retrospectives/M7-S4-obligations-view-escalation-dispute-queues-retrospective.md) (latest session retro), [`retrospectives/M6-retrospective.md`](./retrospectives/M6-retrospective.md) (latest milestone retro), [`milestones/M7-operations-bc.md`](./milestones/M7-operations-bc.md), [`decisions/README.md`](./decisions/README.md), [`decisions/PARKED.md`](./decisions/PARKED.md), `.github/workflows/ci.yml`

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

CritterBids is **mid-M7 (Operations BC)** — the eighth and final MVP backend bounded context.
**Four of M7's seven slices are complete** (S1–S4, all merged to `main` via PRs #63–#70).

- **All 8 production BC projects exist in `src/`**: Participants, Selling, Auctions, Listings,
  Settlement, Obligations, Relay, Operations — plus Api, AppHost, Contracts.
- **Build state at last session close:** 0 errors / 0 warnings; **230 tests, all green**
  (full local `dotnet test CritterBids.slnx`).
- **Engine baseline:** Wolverine 6.2.2 / Marten 9 / JasperFx 2 (upgraded mid-M6, PR #55).
- **Auth posture:** still `[AllowAnonymous]` everywhere in code. ADR-024 (staff `StaffToken`
  scheme + `StaffOnly` policy) is **decided and accepted** but its code lands in **M7-S6** —
  not yet implemented.
- **Development tooling note:** sessions through M7-S4 were driven via GitHub Copilot; work
  from 2026-05-31 onward is driven via Claude Code (Copilot usage limits). The
  prompt → execute → retro loop and all conventions are tool-agnostic and unchanged.

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
| **M7** | **Operations BC (operator read models + staff auth)** | 🔶 **In progress — 4/7 slices** |
| M8 | React frontend SPAs (bidder + ops dashboard) | ⏳ Not started (depends on M7) |

### M7 slice progress

| Slice | Scope | Status |
|---|---|---|
| M7-S1 | Foundation decisions: ADR-024 (staff auth), OpenSpec **decline**, W006 read-model field freeze | ✅ Done (PR #64) |
| M7-S2 | Operations BC scaffold + settlement-queue consumer (`SettlementQueueView`) | ✅ Done (PR #66) |
| M7-S3 | Lot board upsert view + bid-activity append feed | ✅ Done (PR #68) |
| M7-S4 | `OperationsObligationsView` — escalation + dispute queues | ✅ Done (PR #70) |
| **M7-S5** | **Session & participant activity board** (`operations-participants-events` + session events) | ⏭ **Next up** |
| M7-S6 | Staff auth gating (`StaffToken`/`StaffOnly` per ADR-024) + query endpoints over all views | ⏳ Pending |
| M7-S7 | End-to-end cross-BC journey test, route audit, `bounded-contexts.md` status flip, M7 retro | ⏳ Pending |

---

## 2. What's Up Next?

### Immediate: M7-S5 — Session & Participant Activity Board

Per the M7 milestone doc §7 and W006 (`docs/workshops/006-operations-source-audit.md`):

- Wire `operations-participants-events` consumer (+ session events from `operations-auctions-events`)
- Two upsert views per ADR 014 one-view-per-entity rule: a `SessionId`-keyed session lineup view
  and a `ParticipantId`-keyed participant activity view (W006 §5a/5b field freeze)
- Fed by `SessionCreated`, `SessionStarted`, `ListingAttachedToSession`, `ParticipantSessionStarted`
- The session workflow expects a **work-order prompt authored first**
  (`docs/prompts/implementations/M7-S5-*.md`), then implementation, then a retro sharing the slug

### Then: M7-S6 — Staff Auth Gating + Query Endpoints (highest-risk remaining slice)

- Implement ADR-024: `StaffToken` default scheme (header `X-Staff-Token`; `access_token` query
  string for `/hub/operations`), `StaffOnly` policy
- **Wire-then-gate**: `WithdrawListing`, `CreateSession`, `StartSession` are **command-only
  handlers with no HTTP endpoints** — S6 must create the owning-BC HTTP endpoints first, then
  gate them (the M7-S1 endpoint-existence finding). Only `ResolveDisputeEndpoint` already exists.
- Read-only staff query endpoints over all five operator views, authored gated
- S6's readers **must filter to queue states** (`Escalated`/`Disputed`) — the S4 tolerant-seed
  decision means terminal rows can exist for obligations that never entered a queue
- Authorized-vs-unauthorized tests (401/403/200) on real Kestrel + Testcontainers
- Update CLAUDE.md's `[AllowAnonymous]`-through-M6 text (ADR-024 supersedes it; edit owed at S6)

### Then: M7-S7 — Close-out

Cross-BC journey test producing real activity in every operator view; `Program.cs` route audit;
`bounded-contexts.md` Operations status flip; test-count baseline update; M7 milestone retro.

### After M7: M8 — Frontend

React + TypeScript SPAs (Vite per ADR 012; stack per ADR 013, still **Proposed**). Includes the
render-time `Title` join to the lot board (Operations views store `ListingId` only), Relay HTTP
endpoints + `@microsoft/signalr` client wiring, and the dashboard's tolerance for
Relay-push-vs-Operations-read-model eventual consistency (M7 milestone §5).

---

## 3. Deferred / Delayed Ledger

### In-scope for M7, not yet done

| Item | Deferred at | Lands in |
|---|---|---|
| Session & participant activity board | M7 plan | M7-S5 |
| Staff auth implementation (ADR-024 code), `StaffOnly` on staff mutations, query endpoints | M7-S1 (decision made, code deferred) | M7-S6 |
| CLAUDE.md `[AllowAnonymous]` text update | M7-S1 | M7-S6 |
| Cross-BC journey test, route audit, `bounded-contexts.md` Operations status flip | M7 plan | M7-S7 |

### Deferred past M7 (tracked, with target)

| Item | Deferred at | Target |
|---|---|---|
| `OperationsHub` staff-group targeting (currently `Clients.All`) | M7-S1 fork #4 | Post-MVP Relay edit |
| Render-time `Title` join (lot board / obligations view show `ListingId` only) | M7-S3/S4 | M8 (frontend render concern) |
| `wolverine-signalr` skill lived-Relay update (plain `Hub` + `IHubContext` per ADR-023) | Owed since M6-S5 | Next skills-maintenance pass or M7-S7 |
| `marten-projections` skill: non-monotone state-machine guard section (terminal-absorbing + open backward edge) | M7-S4 | Future skill-update session |
| Marten event-type alias / upgrade pass for prior namespace promotions | M5 | First-real-deploy retrospective |

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
| 1 | **CI does not run Settlement, Obligations, Relay, or Operations tests.** The integration matrix in `.github/workflows/ci.yml` covers only Api, Participants, Selling, Auctions, Listings — 101 of 230 tests (~44%) run only on developer machines. | **High** | Extend the CI matrix (Settlement, Obligations, Relay, Operations) — natural candidate for M7-S7 housekeeping or a standalone docs/CI PR. Until then, full `dotnet test CritterBids.slnx` locally before every PR is the only full-suite gate. |
| 2 | **M7-S6 auth slice is the highest-risk remaining MVP backend work.** Bare `AddAuthentication()` with no default scheme means a naive `[Authorize]` flip 500s instead of 401s; three of four staff mutations need HTTP endpoints wired before they can be gated. | Medium-High | ADR-024 pre-resolved the design; S6 prompt must carry the wire-then-gate sequence and the empty-token startup-failure rule. |
| 3 | **Tooling transition (Copilot → Claude Code) mid-milestone.** Session continuity previously lived in Copilot conversation history. | Medium | This document + the retro convention mitigate. The prompt → execute → retro loop is tool-agnostic; next session should start by reading the newest retro. |
| 4 | **ADR 013 (frontend core stack) is still Proposed**, and M8 depends on it. Routing and auth-client patterns are explicitly deferred inside it. | Medium | Accept/revise ADR 013 at M8 opening (M8-S1 foundation decisions). |
| 5 | **Eventual-consistency contract between Relay push and Operations read models** is documented but not yet exercised by any client. The M8 dashboard must treat pushes as "re-query" signals, not read-your-own-write. | Low-Medium | M7 milestone §5 documents the contract; M8-S1 must carry it into the frontend design. |
| 6 | **S4 tolerant-seed rows**: terminal `OperationsObligationsView` rows can exist for obligations that never entered a queue. A naive S6 reader that assumes every row was queued will over-report. | Low | Recorded as a flagged judgment call in the S4 retro; S6 prompt must repeat it. |
| 7 | **Doc nit:** the M7-S4 retro header says `**Date:** 2025-02-14` — wrong year/date (should be ~2026-05-31). | Trivial | Fix in the next docs PR. |

---

## 5. Key Numbers (at M7-S4 close)

- **Tests:** 230 passing — Auctions 65, Selling 36, Relay 36, Operations 27, Settlement 25, Listings 20, Obligations 13, Participants 6, Api 1, Contracts 1
- **ADRs:** 24 authored (next unreserved: **025**); 013 Proposed; 003/008/010 superseded
- **BCs in `src/`:** 8 of 8 MVP backend BCs (Operations newest, M7-S2)
- **Operator read models shipped:** 3 of 5 (settlement queue, lot board + bid-activity feed, obligations view); session + participant boards remain
- **RabbitMQ `operations-*` queues wired:** settlement, auctions, selling, obligations (participants remains)
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

- **v0.1** (2026-05-31): Authored at the Copilot → Claude Code tooling transition, immediately
  after M7-S4 merged (PR #70). Derived from the M7-S4 retro, M6 milestone retro, M7 milestone
  doc, ADR index, PARKED.md, and the CI workflow. First snapshot of its kind; regeneration rule
  recorded in the header.
