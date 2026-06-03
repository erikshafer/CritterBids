# M7 — Operations BC — Milestone Retrospective

**Date:** 2026-06-03
**Milestone:** M7 — Operations BC
**Sessions:** S1 → S7 (7 sessions; no mid-flight splits)
**Agents:** @PSA (S1–S4 via GitHub Copilot; S5–S6 via Claude Code; S7 via Windsurf/Cascade)

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M7-operations-bc.md` §1:

| Exit criterion | Status |
|---|---|
| Solution builds clean with `dotnet build` — 0 errors, 0 warnings (engine baseline: Wolverine 6 / Marten 9 / JasperFx 2) | ✅ 0 errors / 0 warnings at M7 close |
| Operations BC implemented: `CritterBids.Operations` and `CritterBids.Operations.Tests` projects, `AddOperationsModule()`, Marten config per BC-module conventions and ADR 011 | ✅ S2 (`AddOperationsModule()`, `operations` schema, six view types registered) |
| `AddOperationsModule()` called in `Program.cs`; Operations discovery added to `opts.Discovery.IncludeAssembly(...)` | ✅ S2 |
| Authentication-posture resumption decided in M7-S1 and recorded as an ADR | ✅ S1 — [ADR-024](../decisions/024-staff-token-authentication.md) (StaffToken scheme + StaffOnly policy) |
| OpenSpec adoption decision for Operations recorded | ✅ S1 — declined (Operations is a pure consumer BC with no domain commands or sagas) |
| Cross-BC consumer routes wired in `Program.cs`: all `operations-*` consumer queues and publish-route additions | ✅ S2 (settlement), S3 (auctions, selling), S4 (obligations), S5 (participants + session events on auctions queue) |
| Operator read models implemented in the `operations` Marten schema | ✅ S2–S5 — all six views: `SettlementQueueView`, `LotBoardView`, `BidActivityEntry`, `OperationsObligationsView`, `SessionActivityView`, `ParticipantActivityView` |
| Staff query endpoints implemented for the read models, gated by the `StaffOnly` authorization policy | ✅ S6 — seven `[WolverineGet]` endpoints under `/api/operations/*` |
| Existing staff-mutation endpoints gated with `StaffOnly` policy | ✅ S6 — `WithdrawListingEndpoint`, `CreateSession`/`StartSession` (new thin owning-BC endpoints), `ResolveDisputeEndpoint` (gated in place) |
| At least one authorized-vs-unauthorized test per staff surface | ✅ S6 — 401 + success for every HTTP endpoint and hub; no 403 (structurally unreachable under single shared secret) |
| At least one read-model projection test per operator view | ✅ S2–S5 — full lifecycle + idempotency + pure-consumer assertion per view |
| Full solution layout updated; `bounded-contexts.md` Operations status flipped | ✅ S7 — "Planned" → "Active (M7)"; heading, storage table, and integration topology note updated |
| M7-S1 through final-session retrospective docs written | ✅ All seven slice retros land with their slices |
| M7 retrospective doc written | ✅ This document |

All exit criteria honored. No deferrals beyond those explicitly scoped as post-M7 / post-MVP.

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 (PR #64) | Foundation decisions: ADR-024 (staff auth), OpenSpec decline for Operations, W006 field freeze | ✅ | Three-fork ADR-024 exploration; first milestone to decline OpenSpec |
| S2 (PR #66) | Operations BC scaffold — project, `AddOperationsModule()`, `SettlementQueueView` consumer | ✅ | Eighth and final BC project created; first Operations consumer queue (`operations-settlement-events`) activates the pre-wired M5-S6 publish route |
| S3 (PR #68) | Lot board upsert view (`LotBoardView`) + bid-activity append feed (`BidActivityEntry`) | ✅ | Two-queue S3 scope (auctions + selling); status monotone advancement pattern established |
| S4 (PR #70) | `OperationsObligationsView` — escalation queue + open-dispute queue | ✅ | Tolerant-seed decision: terminal rows can exist for obligations that never entered a queue; S6 query endpoints filter by QueueState |
| S5 | Session & participant activity board (`SessionActivityView`, `ParticipantActivityView`) | ✅ | Fifth and final operations-* queue (`operations-participants-events`); all six views complete |
| S6 | Staff auth gating (ADR-024 code) + seven query endpoints + staff mutation endpoints wired/gated | ✅ | Highest-risk slice — default-scheme trap, wire-then-gate for three staff mutations, OperationsHub `[Authorize]`, access_token query-string for SignalR negotiate |
| S7 | Cross-BC journey test, route audit, attribute audit, doc updates, milestone close | ✅ | Multi-agent session (Windsurf); BidPlaced sticky-queue finding rediscovered |

No mid-flight splits — every slice closed at its planned scope.

---

## Cross-BC Integration Map

All Operations consumer flows wired and verified through Testcontainers Postgres:

```
Operations-INBOUND (pure consumer — all routes inbound-only):
  Settlement (M5) ──► operations-settlement-events ──► SettlementQueueView        ✅ S2
                      (PaymentFailed, SettlementCompleted, SellerPayoutIssued)
  Selling (M2)    ──► operations-selling-events    ──► LotBoardView (seed)        ✅ S3
                      (ListingPublished)
  Auctions (M3/4) ──► operations-auctions-events   ──► LotBoardView + BidActivity ✅ S3
                      (BiddingOpened, BidPlaced, ListingSold, ListingPassed,
                       ListingWithdrawn, SessionCreated, SessionStarted,
                       ListingAttachedToSession)
  Obligations (M6)──► operations-obligations-events ──► OperationsObligationsView  ✅ S4
                      (DeadlineEscalated, DisputeOpened, DisputeResolved,
                       ObligationFulfilled)
  Participants (M1)──► operations-participants-events──► ParticipantActivityView   ✅ S5
                       (ParticipantSessionStarted)

Operations-OUTBOUND:  None (pure consumer — ADR-014 Path A)
```

Five new RabbitMQ consumer queues wired in M7: `operations-settlement-events` (S2; publish-only since M5-S6), `operations-auctions-events` (S3), `operations-selling-events` (S3), `operations-obligations-events` (S4), `operations-participants-events` (S5).

The S7 journey test dispatches a representative event from each source BC family and queries every view through staff-gated endpoints, proving the full consume → project → query path end-to-end.

---

## Test Count at M7 Close

| Project | Count | Δ from pre-M7 | M7 contributions |
|---|---|---|---|
| `CritterBids.Auctions.Tests` | 65 | — | — |
| `CritterBids.Api.Tests` | 41 | +39 | S6: staff auth gate tests (38); S7: journey test (1) |
| `CritterBids.Operations.Tests` | 38 | +38 | S2 scaffold/settlement (8) + S3 lot board/bid activity (12) + S4 obligations (8) + S5 session/participant (10) |
| `CritterBids.Selling.Tests` | 36 | — | — |
| `CritterBids.Relay.Tests` | 36 | +1 | — (count adjustment from M6 close) |
| `CritterBids.Settlement.Tests` | 25 | — | — |
| `CritterBids.Listings.Tests` | 20 | — | — |
| `CritterBids.Obligations.Tests` | 13 | — | — |
| `CritterBids.Participants.Tests` | 6 | — | — |
| `CritterBids.Contracts.Tests` | 1 | — | — |
| **Total** | **281** | **+78** | |

Test arc across the milestone: 203 (M6 close) → 211 (S2) → 223 (S3) → 230 (S4) → 240 (S5) → 280 (S6) → **281 (S7)**.

---

## Key Decisions Made in M7

| Identifier | Decision |
|---|---|
| [ADR-024](../decisions/024-staff-token-authentication.md) | **StaffToken authentication scheme + StaffOnly authorization policy.** Config-driven staff passphrase (`OperationsAuth:StaffToken`) as the default authenticate and challenge scheme. `X-Staff-Token` header for HTTP; `access_token` query string for OperationsHub WebSocket negotiate. Empty-token startup guard in Production. 403 structurally unreachable (single shared secret). Post-MVP revisit: per-user identity, roles, external IdP (swap behind unchanged `StaffOnly` policy). |
| M7-D1 | **OpenSpec declined for Operations.** Operations is a pure consumer BC with no domain commands or sagas — the OpenSpec propose → implement → archive loop adds friction without value for a projection-only surface. |
| M7-D2 | **One view per source-BC entity, not one view per source-BC.** The lot board (`ListingId`-keyed) and bid-activity feed (`BidId`-keyed) are separate views even though both consume Auctions events. Per ADR-014, the keying entity determines the view boundary. |
| M7-D3 | **S4 tolerant-seed rows.** `OperationsObligationsView` tolerantly seeds from any Obligations event, not just `DeadlineEscalated`/`DisputeOpened`. Terminal rows can exist for obligations that were fulfilled without entering a queue. S6 query endpoints filter by `QueueState`. |
| M7-D4 | **Wire-then-gate for staff mutations.** `WithdrawListing`, `CreateSession`, and `StartSession` were command-only handlers with no HTTP endpoint. S6 created thin owning-BC endpoints first, then gated them — the command surface was wired structurally before auth was applied. |

---

## Key Learnings — Cross-Session Patterns

### 1. Pure-consumer BCs have a distinct testing shape

Operations handlers produce no outgoing messages — no cascades, no `IMessageBus` usage except the forbidden list. This means `tracked.Sent.AllMessages().ShouldBeEmpty()` is a first-class assertion on every dispatch, proving ADR-014 Path A structurally. The assertion pattern is unique to pure-consumer BCs; producer BCs (Settlement, Obligations) assert the *presence* of outgoing messages.

### 2. Separated-handler sticky queues and InvokeAsync are mutually exclusive for multi-handler message types

When `MultipleHandlerBehavior.Separated` is active, a message type with multiple handler chains cannot be dispatched via `InvokeAsync` — Wolverine cannot resolve which sticky endpoint to target. Use `SendMessageAndWaitAsync` for multi-handler types (e.g., `BidPlaced` → LotBoardAuctionsHandler + BidActivityHandler). This was discovered in S3 (`BidActivityHandlerTests`) and rediscovered in S7 (journey test).

### 3. AlbaHost.For<Program>() preserves full routing topology

A hand-built `WebApplication` that registers the same services but skips `Program.cs`'s RabbitMQ route configuration loses the endpoint-to-handler binding. Booting from `Program.cs` via `AlbaHost.For<Program>()` preserves the full topology even with `DisableAllExternalWolverineTransports()`. This matters for Separated-handler testing.

### 4. Discovery exclusion via IWolverineExtension composes cleanly with Alba

Registering `IWolverineExtension` implementations that call `CustomizeHandlerDiscovery` from `ConfigureServices` integrates cleanly with `AlbaHost.For<Program>()` — the extensions fire after `Program.cs`'s `UseWolverine` block, adding exclusions without disturbing the main routing. This pattern (established in M5, refined through M6/M7) is now the canonical cross-BC test isolation technique.

### 5. Multi-agent sessions work when canonical knowledge lives in docs

M7 is the first CritterBids milestone to span three different AI agents (GitHub Copilot S1–S4, Claude Code S5–S6, Windsurf S7). The prompt → execute → retro loop, the retro-as-canonical-state convention, and the skills/milestones/decisions documentation meant no agent needed the previous agent's conversation history to pick up work. The checkpoint/summary mechanism carries session state; the docs carry project state.

---

## ADR Candidate Review

| Finding | ADR warranted? | Rationale |
|---|---|---|
| Staff auth scheme and policy | **Yes — ADR-024 at S1** | Has alternatives (JWT, cookie, API key); project-specific reasoning for config-passphrase; carry-forward implications for M8 and post-MVP |
| OpenSpec declined for Operations | **No** | Per-BC evaluation gate (ADR-021); decision recorded in the S1 retro and openspec/README.md ledger |
| Tolerant-seed obligation rows | **No** | View-design judgment call; recorded in S4 retro and propagated to S6 query endpoint filtering |
| Wire-then-gate staff mutation pattern | **No** | Implementation sequencing; no rejected alternatives |

**ADR-024 is M7's single ADR.** Next unreserved: **025**.

---

## Technical Debt and Deferred Items

| Item | Deferred in | Target |
|---|---|---|
| `OperationsHub` staff-group targeting (`Clients.All` → per-staff-group broadcast) | M7-S1 fork #4 / ADR-024 item 6 | Post-MVP Relay edit |
| Render-time `Title` join (lot board / obligations view show `ListingId` only) | M7-S3/S4 | M8 (frontend render concern) |
| `marten-projections` skill: non-monotone state-machine guard section | M7-S4 | Future skill-update session |
| Marten event-type alias / upgrade pass for prior namespace promotions | Carried from M5 | First-real-deploy retrospective |
| CI matrix extension (Settlement, Obligations, Relay, Operations tests run locally only) | Carried from M6 | Standalone CI PR or M8-S1 |
| `wolverine-http-auth` skill codifying the default-scheme trap / hub-path credential patterns | M7-S6 gap | Future skill-authoring session |

---

## What's Next — M8

M7 closes the **eighth and final MVP backend bounded context**. All backend BCs are implemented, all consumer routes are wired, all staff surfaces are auth-gated, and the test suite is green at 281.

The recommended next milestone is **M8 — React Frontend SPAs**, for concrete reasons:

- **The backend is complete.** Every operator view is queryable, every integration route is active, and the staff auth surface is in place. The data exists; it needs a renderer.
- **Two React SPAs** are planned: the bidder-facing app (public catalog + live bidding via BiddingHub) and the staff ops dashboard (operator views + live feed via OperationsHub). Both consume the same API host.
- **ADR-013 (frontend core stack) is still Proposed.** M8-S1 should accept/revise it, settling Vite, React, TypeScript, TailwindCSS, and the `@microsoft/signalr` client wiring.
- **The render-time `Title` join** (lot board and obligations view store `ListingId` only) is an M8 concern — the API returns the ID, and the frontend resolves the display title from the catalog endpoint.
- **The Relay-push vs Operations-read-model eventual-consistency contract** (M7 milestone §5) must be carried into the frontend design: pushes are "re-query" signals, not read-your-own-write.

---

## Key Numbers at M7 Close

- **Tests:** 281 passing (up from 203 at M6 close; +78 in M7 — Operations 38, Api 39, Relay 1)
- **Sessions:** 7 (S1 through S7; no mid-flight splits)
- **New ADRs:** 1 (ADR-024 — staff authentication)
- **New RabbitMQ consumer queues:** 5 (`operations-settlement-events`, `operations-auctions-events`, `operations-selling-events`, `operations-obligations-events`, `operations-participants-events`)
- **New Marten document types:** 6 (`SettlementQueueView`, `LotBoardView`, `BidActivityEntry`, `OperationsObligationsView`, `SessionActivityView`, `ParticipantActivityView` — all in `operations` schema)
- **New HTTP endpoints:** 7 query + 3 staff mutation (wired in owning BCs)
- **BCs in `src/`:** 8 of 8 MVP backend BCs — all active
- **Build:** 0 errors, 0 warnings
- **Agents used:** 3 (GitHub Copilot S1–S4, Claude Code S5–S6, Windsurf S7) — first multi-agent milestone

---

## What M8 Should Know

**At M7 close the solution has 281 tests passing across 10 test projects, covering the full Participants → Selling → Auctions → Listings → Settlement → Obligations → Relay → Operations pipeline, verified end-to-end through real Postgres + (test-stubbed) RabbitMQ + real-Kestrel SignalR + staff-gated HTTP.** All eight MVP backend BCs are implemented: Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations.

**For M8 (React Frontend SPAs):**
- ADR-013 (frontend core stack) is **Proposed** — accept/revise at M8-S1.
- Two SPAs: bidder-facing (public) and staff ops dashboard (StaffOnly gated).
- Staff auth: `X-Staff-Token` header from the SPA; `access_token` query string for OperationsHub WebSocket connections. The React app must read the token from config, never hardcode it.
- Render-time `Title` join: Operations API returns `ListingId` only for lot board / obligations views. The frontend resolves display titles from `/api/listings/{id}`.
- Relay push = re-query signal: `OperationsHub` pushes are notifications to refresh, not authoritative data. The SPA should re-fetch from the query endpoint on push, not render the push payload directly.
- Engine baseline: Wolverine 6.2.2 / Marten 9 / JasperFx 2; .NET 10; 0-warning build.
- CI runs ~44% of tests. Extending the CI matrix to all test projects is a natural M8-S1 housekeeping item.
