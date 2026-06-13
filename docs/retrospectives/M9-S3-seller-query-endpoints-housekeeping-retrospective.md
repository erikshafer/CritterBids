# M9-S3: Backend Precursor — Seller Query Endpoints + Housekeeping - Retrospective

**Date:** 2026-06-13
**Milestone:** M9 - Seller Console
**Slice:** S3 - Backend precursor continued (seller query endpoints + housekeeping)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S3-seller-query-endpoints-housekeeping.md`

## Baseline

- Clean `main` at `7c498d1` (post-M9-S2: seller listing endpoints + `SellerListingSummary` projection).
- .NET build: 0 errors, 2 CS0108 warnings (saga Version hiding — baseline held from M8-S7).
- .NET tests: 316 all pass (10 projects).
- Frontend: unchanged (`@critterbids/shared` typecheck clean, bidder 25, ops 47, seller 2).
- Endpoint surface audit from M9 milestone doc §2: 3 of 5 seller gaps closed by S2; 2 remaining (obligation status, settlement summary).
- M8-S7 carry-forwards: Listings `ExtendedBiddingTriggered` handler (Finding 1) and cache-bridge burst-final hardening (Finding 2) both open.

## Items completed

| Item | Description |
|------|-------------|
| S3a | Obligations BC seller query endpoint — `GET /api/obligations/status?sellerId={sellerId}` |
| S3b | Settlement BC `SellerSettlementSummary` projection + handler + `GET /api/settlement/summaries?sellerId={sellerId}` |
| S3c | Listings `ExtendedBiddingTriggered` handler + `listings-auctions-events` publish route |
| S3d | Cache-bridge burst-final hardening — **evaluated, deferred** (see below) |
| S3e | Integration tests — 10 new tests across 3 BCs |
| S3f | This retrospective |

## S3a: Obligations seller query endpoint

**Why this approach.** The `ObligationStatusView` already carries `SellerId` (seeded from `PostSaleCoordinationStarted`) and is an inline single-stream projection — strongly consistent, immediately queryable. The endpoint is a pure `IQuerySession` read with no handler pipeline, matching the `GetSellerListingsEndpoint` precedent from M9-S2 exactly.

**Endpoint after:**

```csharp
[WolverineGet("/api/obligations/status")]
[AllowAnonymous]
public static async Task<IReadOnlyList<ObligationStatusView>> Get(
    Guid sellerId, IQuerySession session, CancellationToken ct)
```

No module registration changes — `ObligationStatusView` is already registered in `ObligationsModule.ConfigureMarten()` with schema `obligations`.

## S3b: Settlement seller summary projection + query endpoint

**Why a new projection, not PendingSettlement.** The `PendingSettlement` document caches pre-sale data (reserve, BIN, fee%) and lifecycle status (Pending/Consumed/Expired/Failed), but it does NOT carry the settlement financial outcome fields (HammerPrice, FeeAmount, SellerPayout). The `SettlementSaga` is deleted at `MarkCompleted()`. The `SettlementCompleted` integration event is the durable record of the financial outcome — a handler-driven tolerant-upsert document seeded from it is the natural approach.

**Why handler-driven, not a native Marten projection.** `SettlementCompleted` is an integration event emitted via `OutgoingMessages` from the saga — it arrives through Wolverine dispatch, not through the Marten event store's projection pipeline. The handler-driven tolerant-upsert pattern (per `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert") is the established shape for cross-BC integration event consumption. The handler rides `settlement-settlement-events` — the Settlement BC's self-consumption queue, matching `PendingSettlementHandler.Handle(SettlementCompleted)`.

**Two handlers for SettlementCompleted on the same queue.** Under `MultipleHandlerBehavior.Separated` (ADR 027), each handler gets its own chain. The existing `PendingSettlementHandler.Handle(SettlementCompleted)` transitions the `PendingSettlement` row to `Consumed`; the new `SellerSettlementSummaryHandler.Handle(SettlementCompleted)` creates the `SellerSettlementSummary` document. Both bind to `settlement-settlement-events` via `[StickyHandler]`. Verified working in the test fixture (5/5 tests green).

**Document shape:**

| Field | Source |
|---|---|
| Id (ListingId) | `SettlementCompleted.ListingId` — natural key, one listing = one settlement |
| SettlementId | `SettlementCompleted.SettlementId` |
| SellerId | `SettlementCompleted.SellerId` |
| WinnerId | `SettlementCompleted.WinnerId` |
| HammerPrice | `SettlementCompleted.HammerPrice` |
| FeeAmount | `SettlementCompleted.FeeAmount` |
| SellerPayout | `SettlementCompleted.SellerPayout` |
| CompletedAt | `SettlementCompleted.CompletedAt` |

## S3c: Listings ExtendedBiddingTriggered handler

**What was missing.** `AuctionStatusHandler` consumed 6 of the Auctions BC's integration events but NOT `ExtendedBiddingTriggered`. `CatalogListingView.ScheduledCloseAt` was written once at `BiddingOpened` and never advanced. The extended-bidding banner in the bidder app (M8-S3b) derived from `ScheduledCloseAt` shifting later between re-queries — structurally unreachable without this handler.

**Two changes required:**
1. **Handler method** — `Handle(ExtendedBiddingTriggered)` added to `AuctionStatusHandler`. Same tolerant-upsert shape as the other 6 methods. Withdrawn-preservation guard matches `Handle(BiddingOpened)` — symmetric: if the listing is Withdrawn, no-op.
2. **Publish route** — `ExtendedBiddingTriggered` → `listings-auctions-events` added in `Program.cs`. The event was already routed to `relay-auctions-events` and `auctions-auctions-events` but NOT to the Listings consumer queue. Without this route, the handler would never fire in production.

**AuctionStatusHandler consumer count:** 6 → 7 events. The class-level `[StickyHandler("listings-auctions-events")]` binding covers the new method automatically.

## S3d: Cache-bridge burst-final hardening — deferred

**Evaluation.** The fix is localised: a delayed re-invalidation (`setTimeout(() => invalidateQueries(...), 500)`) after each immediate invalidation in the cache bridge. The `queryClient.invalidateQueries()` call is idempotent — a duplicate invalidation causes at most one redundant re-fetch, and TanStack Query deduplicates in-flight requests.

**Why defer.** Three reasons:
1. The seller cache bridge doesn't exist yet — it's created in M9-S5 (live auction observation). Shipping the fix now in bidder + ops means the seller would still lack it until S5, and then S5 would need to retrofit it.
2. Touching `client/bidder/` and `client/ops/` in a backend-only slice violates the prompt's "no frontend changes" constraint without strong justification — the race is harmless in the demo scenario (the next manual interaction heals it).
3. The natural landing point is the `@critterbids/shared` cache-bridge extraction. If the cache-bridge pattern is extracted to the shared workspace member (a candidate for M9-S5 or S7), the delayed re-invalidation can be baked into the shared implementation once, covering all three consumers by construction.

**Disposition:** deferred to M9-S5 or M9-S7 with this rationale recorded. The fix remains a ~5-line change per cache bridge; no infrastructure dependency.

## Test results

| Phase | Suite | Result |
|-------|-------|--------|
| S3a | Obligations query endpoint (3 tests) | **3/3 pass** |
| S3b | Settlement summary handler + query (5 tests) | **5/5 pass** |
| S3c | Listings ExtendedBiddingTriggered handler (2 tests) | **2/2 pass** |
| Regression | Full .NET suite (10 projects) | **326/326 pass** (316 baseline + 10 new) |

## Build state at session close

- `.cs` files changed: 7 (3 new endpoint files, 2 new Settlement projection files, 1 handler extension, 1 publish route)
- `.cs` files in `CritterBids.Contracts`: **0** — no new contract types
- `client/` changes: **0** — backend-only slice
- Errors: **0**; Warnings: **2** (CS0108 baseline held)
- Query endpoints: 2 new (`/api/obligations/status`, `/api/settlement/summaries`)
- Handler methods on `AuctionStatusHandler`: 6 → **7** (`ExtendedBiddingTriggered` added)
- `ExtendedBiddingTriggered` publish routes: 2 → **3** (`listings-auctions-events` added)
- `SellerSettlementSummary` documents in `settlement` schema: **1** new
- Tests: 316 → **326** (+10)

### Endpoint surface after S3

| Endpoint | Method | BC | Auth | Added |
|----------|--------|----|------|-------|
| `/api/obligations/status?sellerId=` | GET | Obligations | AllowAnonymous | M9-S3 |
| `/api/settlement/summaries?sellerId=` | GET | Settlement | AllowAnonymous | M9-S3 |

### M9 milestone endpoint gap status (from §2 audit)

| Gap | Status |
|-----|--------|
| `SubmitListing` endpoint | Closed (M9-S2) |
| `UpdateDraftListing` endpoint | Closed (M9-S2) |
| Seller's own listings query | Closed (M9-S2) |
| Seller's obligation status query | **Closed** (M9-S3) |
| Seller's settlement summary query | **Closed** (M9-S3) |

All five seller-facing endpoint gaps are now closed. The backend precursor slices (S2 + S3) are complete.

## Key learnings

1. **Handler-driven projections from self-published integration events are a natural fit.** The `SettlementSaga` self-publishes `SettlementCompleted` via `OutgoingMessages`; a sibling handler on the same BC's self-consumption queue can materialize the view without any new routing. Two handlers for the same message type on the same sticky queue coexist under Separated dispatch.
2. **Publish routes are as important as handler code.** The `ExtendedBiddingTriggered` handler was a 15-line addition, but without the `listings-auctions-events` publish route in Program.cs the handler would never fire. The RabbitMQ topology is the other half of the handler contract.
3. **Query endpoints over existing projections are near-zero risk.** The Obligations endpoint is a pure `IQuerySession` read over an already-registered, already-populated inline projection — no new types, no new modules, no handler pipeline. Three tests, three passes, zero surprises.

## Findings against narrative

The Listings `ExtendedBiddingTriggered` handler (S3c) is the carry-forward from M8-S7 Finding 1: narrative 005's Moment 3 specified the seller seeing the close shift, which requires `CatalogListingView.ScheduledCloseAt` to advance — forward-spec that never landed until now. With the handler in place, the extended-bidding banner in the bidder app and the future seller console's close-time display are now reachable from the lived read model. The handler is code, not narrative — no narrative Document History row is required (the e2e's banner assert re-addition is a future M9-S7 item).

## Spec delta - landed?

**No spec consequence.** This session is a backend-precursor infrastructure slice. It exposes existing read models over HTTP, adds a Settlement-side projection from an existing integration event, and fixes a handler gap. No new narrative Moments are implemented; no new domain events; no narrative or workshop Document History rows. The spec consequence is limited to the M9 milestone doc's endpoint surface audit (§2): all five seller-facing gaps are now closed.

## Verification checklist

- [x] `GET /api/obligations/status?sellerId={sellerId}` returns the seller's obligation status views
- [x] `SellerSettlementSummary` exists as a handler-driven document in the Settlement BC, registered in `SettlementModule.ConfigureMarten()`
- [x] `GET /api/settlement/summaries?sellerId={sellerId}` returns the seller's settlement summaries
- [x] `AuctionStatusHandler.Handle(ExtendedBiddingTriggered)` advances `CatalogListingView.ScheduledCloseAt`
- [x] `ExtendedBiddingTriggered` publish route added to `listings-auctions-events` in Program.cs
- [x] Cache-bridge burst-final hardening evaluated and **deferred** with rationale (§S3d)
- [x] Integration tests cover happy path, empty, and filtering for both query endpoints (3 + 3)
- [x] Integration tests cover ExtendedBiddingTriggered handler (happy path + Withdrawn guard) (2)
- [x] Existing .NET build succeeds: 0 errors, 2 CS0108 warnings (baseline held)
- [x] Existing .NET tests pass: 316 baseline preserved; grown to 326 (+10)
- [x] No new domain events, no new integration events, no new BC modules
- [x] No frontend changes — `client/` untouched
- [x] This retrospective written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **Cache-bridge burst-final hardening:** deferred to M9-S5 or M9-S7 per §S3d. The fix is a ~5-line change per cache bridge; bake it into `@critterbids/shared` when that surface is extracted.
- **Extended-bidding e2e banner assert:** the M8-S7 e2e marks the spot (a code comment in `bid-war.spec.ts`) where the "Extended bidding" banner assert should be re-added. Now that the backend handler exists, the banner is reachable. Land in M9-S7.
- **Seller console frontend slices (M9-S4+):** all backend precursor work is complete. The seller console has a full HTTP surface: listing management (S2), obligation status query (S3a), settlement summary query (S3b), and the existing ProvideTracking / Withdraw endpoints.
