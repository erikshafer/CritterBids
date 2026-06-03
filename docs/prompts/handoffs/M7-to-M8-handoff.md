# Hand-off Prompt — CritterBids M7 → M8 Milestone Boundary

| Field | Value |
|---|---|
| **Status** | Active |
| **Authored** | 2026-06-03 |
| **Author of record** | Erik Shafer (with Windsurf/Cascade session collaborator) |
| **Target project** | CritterBids — `~/Code/CritterBids` (macOS) or `C:\Code\CritterBids` (Windows) |
| **Workflow position** | M7 complete (all 7 slices merged to `main` via PR #76). M8 not started. |

---

## 0. Read this section first

You are picking up **CritterBids** at the boundary between **M7 (Operations BC)** and **M8 (React Frontend SPAs)**. M7 was the eighth and final MVP backend bounded context. All backend BCs are implemented, all integration routes are active, all staff surfaces are auth-gated, and the test suite is green at **281 tests**.

**M8 is the frontend milestone.** No backend BC work remains. The backend API is the surface M8 consumes.

Three things to orient yourself:

1. **Canonical knowledge lives in `docs/`, not in any tool's conversation history.** M7 spanned three AI agents (GitHub Copilot, Claude Code, Windsurf). The retro-as-canonical-state convention means you start from docs, not from a prior chat.
2. **Read the newest retrospective first.** The M7 milestone retro (`docs/retrospectives/M7-retrospective.md`) has the "What M8 Should Know" section written specifically for this handoff.
3. **`CLAUDE.md` at repo root is the conventions file.** Despite the name, it is tool-agnostic. Read it before implementing anything.

---

## 1. Orientation — files to read (in order)

| Priority | File | Why |
|---|---|---|
| 1 | `CLAUDE.md` | Hard rules, conventions, do-not list, BC module quick reference |
| 2 | `docs/STATUS.md` | Derived project-status snapshot (regenerated at M7 close) |
| 3 | `docs/retrospectives/M7-retrospective.md` | Milestone retro with "What M8 Should Know" section |
| 4 | `docs/retrospectives/M7-S7-end-to-end-integration-housekeeping-retrospective.md` | Latest session retro |
| 5 | `docs/decisions/README.md` | ADR index (24 authored; next unreserved: 025) |
| 6 | `docs/vision/bounded-contexts.md` | BC map — all 8 active |

---

## 2. Where we are

- **Build:** 0 errors / 0 warnings (`dotnet build CritterBids.slnx`)
- **Tests:** 281 passing (`dotnet test CritterBids.slnx`)
- **Engine:** Wolverine 6.2.2 / Marten 9 / JasperFx 2 / .NET 10
- **Auth:** ADR-024 implemented — `StaffToken` scheme + `StaffOnly` policy. Staff surfaces gated; participant-facing endpoints remain `[AllowAnonymous]`.
- **All 8 BCs active:** Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations
- **All 6 operator views queryable:** settlement queue, lot board, bid-activity feed, obligations (escalations + disputes), session activity, participant activity
- **All 5 operations-\* consumer queues wired**
- **12 staff-gated surfaces:** 7 query + 4 mutation + 1 hub (OperationsHub)

---

## 3. What's next — M8

**M8 — React Frontend SPAs.** Two SPAs consuming the same API host:

- **Bidder-facing app** — public catalog, live bidding via `BiddingHub` WebSocket
- **Staff ops dashboard** — operator views, live feed via `OperationsHub` (StaffOnly-gated)

### Known M8 concerns (from M7 retro)

1. **ADR-013 (frontend core stack) is still Proposed.** M8-S1 should accept/revise it — settle Vite, React, TypeScript, TailwindCSS, `@microsoft/signalr` client wiring.
2. **Render-time Title join.** Operations API returns `ListingId` only for lot board / obligations views. The frontend resolves display titles from `/api/listings/{id}` (catalog endpoint, `[AllowAnonymous]`).
3. **Relay push = re-query signal.** `OperationsHub` pushes are notifications to refresh, not authoritative data. The SPA should re-fetch from the query endpoint on push. See M7 milestone doc §5.
4. **Staff auth from the SPA.** `X-Staff-Token` header for HTTP requests; `access_token` query string for OperationsHub WebSocket connections (SignalR clients cannot set custom headers on the negotiate POST).
5. **CI matrix gap.** Settlement, Obligations, Relay, and Operations tests run locally only (~46% of tests). Extending the CI matrix is a natural M8-S1 housekeeping item.

### Suggested M8-S1 scope

Following the established M{n}-S1 foundation-decisions pattern:

- Accept/revise ADR-013 (frontend core stack)
- Decide monorepo layout for SPAs (e.g., `client/bidder/`, `client/ops/`)
- Scaffold Vite + React + TypeScript project(s)
- Wire `@microsoft/signalr` client to `BiddingHub` (anonymous) as proof of connection
- Optionally extend CI matrix

---

## 4. Deferred items to carry forward

| Item | Target |
|---|---|
| `OperationsHub` staff-group targeting (`Clients.All` → per-group) | Post-MVP |
| Render-time `Title` join | M8 (frontend) |
| `marten-projections` skill: non-monotone state-machine guard | Future skill session |
| `wolverine-http-auth` skill | Future skill session |
| Marten event-type alias / upgrade pass | First-real-deploy retro |
| CI matrix extension | M8-S1 or standalone PR |

---

## 5. Session workflow reminder

The prompt → execute → retro loop applies to every session:

1. **Read** the newest retro + `CLAUDE.md` + relevant skills before starting
2. **Author** a work-order prompt (`docs/prompts/implementations/M8-S{n}-*.md`) if one doesn't exist
3. **Implement** on a branch off `main` — never commit to `main` directly
4. **Test** — `dotnet build CritterBids.slnx` (0 errors / 0 warnings) + `dotnet test CritterBids.slnx` (all green) before PR
5. **Write** a session retrospective (`docs/retrospectives/M8-S{n}-*-retrospective.md`)
6. **PR** off `main` — no `Co-Authored-By` trailer

---

## 6. Quick-start commands

```bash
# Verify clean state after pulling main
dotnet build CritterBids.slnx
dotnet test CritterBids.slnx

# Start infrastructure for local dev
docker compose up -d   # PostgreSQL + RabbitMQ

# Run the API
dotnet run --project src/CritterBids.Api
```
