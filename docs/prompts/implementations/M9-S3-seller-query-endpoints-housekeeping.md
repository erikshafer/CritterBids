# M9-S3: Backend Precursor — Seller Query Endpoints + Housekeeping

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S3 of M9 (second backend precursor slice)
**Narrative:** `docs/narratives/006-seller-fulfills-post-sale-obligation.md` (obligation fulfillment) · `docs/narratives/005-seller-watches-flash-auction-close.md` (extended bidding visibility)
**Agent:** @PSA
**Estimated scope:** one PR, ~12-15 files (2 query endpoints, 1 projection + handler, 1 handler addition, 1 publish route, integration tests, retro)

---

## Preconditions

This prompt assumes **`docs/milestones/M9-seller-console.md` exists** (authored 2026-06-13, PR #103) and that M9-S2 shipped (`7c498d1`, PR #105 — seller listing endpoints + `SellerListingSummary` projection). Per AUTHORING.md rule 3 the milestone doc is authoritative for scope. The working branch starts from clean `main` at `7c498d1`.

## Goal

Wire the two remaining seller-facing query endpoints from the M9 milestone doc endpoint surface audit (§2), ship the M8-S7 carry-forward Listings handler, and evaluate the cache-bridge burst-final hardening — completing all backend precursor work before the seller SPA slices (M9-S4+):

1. **Obligations BC: `GET /api/obligations/status?sellerId={sellerId}`** — query endpoint against the existing `ObligationStatusView` inline projection, filtered by seller
2. **Settlement BC: `SellerSettlementSummary` + `GET /api/settlement/summaries?sellerId={sellerId}`** — new handler-driven tolerant-upsert document seeded from `SettlementCompleted`, plus query endpoint
3. **Listings BC: `ExtendedBiddingTriggered` handler** — `CatalogListingView.ScheduledCloseAt` advances on extension (the M8-S7 carry-forward that made the extended-bidding banner unreachable)
4. **Cache-bridge burst-final hardening** — evaluate and ship or defer with rationale

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | Authoritative for scope. §7 S3 row, §2 endpoint audit, §9 carry-forwards. |
| `CLAUDE.md` | Routing layer and global conventions. |
| `docs/skills/critter-stack-testing-patterns/SKILL.md` | Integration test patterns, cross-BC handler isolation. |
| `docs/skills/wolverine-message-handlers/SKILL.md` | Handler and HTTP endpoint patterns. |
| `docs/skills/marten-projections/SKILL.md` | Handler-driven tolerant-upsert pattern (for SellerSettlementSummary). |
| `src/CritterBids.Obligations/ObligationStatusView.cs` | Existing view shape — queryable by SellerId. |
| `src/CritterBids.Obligations/ObligationsModule.cs` | Module registration. |
| `src/CritterBids.Selling/GetSellerListingsEndpoint.cs` | The M9-S2 query endpoint precedent (IQuerySession + seller filter). |
| `src/CritterBids.Settlement/PendingSettlementHandler.cs` | The tolerant-upsert handler precedent in Settlement BC. |
| `src/CritterBids.Settlement/SettlementModule.cs` | Module registration. |
| `src/CritterBids.Contracts/Settlement/SettlementCompleted.cs` | Integration event carrying settlement financial fields. |
| `src/CritterBids.Listings/AuctionStatusHandler.cs` | The class to extend with ExtendedBiddingTriggered handling. |
| `src/CritterBids.Contracts/Auctions/ExtendedBiddingTriggered.cs` | The contract event shape. |
| `src/CritterBids.Api/Program.cs` | RabbitMQ publish routing — ExtendedBiddingTriggered currently missing from `listings-auctions-events`. |
| `docs/retrospectives/M8-S7-end-to-end-housekeeping-retrospective.md` | Carry-forward findings (§Finding 1: handler gap, §Finding 2: cache-bridge race). |

## In scope

### S3a: Obligations seller query endpoint

- New file: `src/CritterBids.Obligations/GetSellerObligationsEndpoint.cs`
- `GET /api/obligations/status?sellerId={sellerId}` — Wolverine HTTP GET endpoint using `IQuerySession` to query `ObligationStatusView` by `SellerId`
- Returns `IReadOnlyList<ObligationStatusView>`
- `[AllowAnonymous]` — seller-facing, not staff-gated
- Mirrors the `GetSellerListingsEndpoint` pattern from M9-S2

### S3b: Settlement seller summary projection + query endpoint

- New file: `src/CritterBids.Settlement/SellerSettlementSummary.cs` — `sealed record` capturing the settlement financial outcome per listing for sellers
  - Fields: `Id` (Guid, ListingId — natural key), `SettlementId`, `SellerId`, `WinnerId`, `HammerPrice`, `FeeAmount`, `SellerPayout`, `CompletedAt`
- New file: `src/CritterBids.Settlement/SellerSettlementSummaryHandler.cs` — handler-driven tolerant-upsert consuming `SettlementCompleted`
  - Same tolerant-upsert shape as `PendingSettlementHandler`: LoadAsync by ListingId, construct if absent, store
  - Consumes `SettlementCompleted` from local saga dispatch (the saga self-publishes it via `OutgoingMessages`)
  - Sticky binding: `[StickyHandler("settlement-settlement-events")]` — rides the Settlement BC's self-consumption queue, matching `PendingSettlementHandler.Handle(SettlementCompleted)`
- Register `SellerSettlementSummary` in `SettlementModule.ConfigureMarten()` — `settlement` schema
- New file: `src/CritterBids.Settlement/GetSellerSettlementsEndpoint.cs`
  - `GET /api/settlement/summaries?sellerId={sellerId}` — queries `SellerSettlementSummary` by `SellerId`
  - Returns `IReadOnlyList<SellerSettlementSummary>`
  - `[AllowAnonymous]`

### S3c: Listings ExtendedBiddingTriggered handler

- Add `Handle(ExtendedBiddingTriggered)` method to the existing `AuctionStatusHandler` class
  - Tolerant-upsert: LoadAsync by ListingId, construct minimal view if absent
  - Updates `ScheduledCloseAt = message.NewCloseAt`
  - Same Withdrawn-preservation guard as `Handle(BiddingOpened)` — if the listing is already Withdrawn, no-op
- Add publish route in `Program.cs`: `ExtendedBiddingTriggered` → `listings-auctions-events`
  - The event is already routed to `relay-auctions-events` and `auctions-auctions-events`; this adds the Listings consumer route

### S3d: Cache-bridge burst-final hardening (evaluation)

- Evaluate the delayed re-invalidation approach documented in M8-S7 Finding 2
- The push-refetch race: a hub push arrives before the sibling-queue projection applies; the re-query reads stale data; the last event of a burst has no later push to reconcile
- If the fix is localised (a delayed re-invalidate in the cache bridge — `setTimeout(() => invalidateQueries(...), 500)` after the immediate invalidation), ship it in both bidder and seller cache bridges
- If it requires infrastructure changes or cross-cutting framework work, record rationale and defer

### S3e: Integration tests

- New file: `tests/CritterBids.Obligations.Tests/GetSellerObligationsApiTests.cs` — HTTP-level tests:
  - Happy path: seed obligation status views for a seller, query by sellerId, verify returned
  - Empty: query for unknown sellerId returns empty list
  - Filtering: obligations from a different seller are not returned
- New file: `tests/CritterBids.Settlement.Tests/SellerSettlementSummaryTests.cs` — tests for the projection and query:
  - Happy path: dispatch SettlementCompleted, verify SellerSettlementSummary document created with correct fields
  - Query: seed summaries for a seller, query by sellerId, verify returned
  - Filtering: summaries from a different seller are not returned
- Extend `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` with ExtendedBiddingTriggered test:
  - Seed a CatalogListingView at "Open" with ScheduledCloseAt, dispatch ExtendedBiddingTriggered, verify ScheduledCloseAt advanced to NewCloseAt
  - Withdrawn guard: seed at "Withdrawn", dispatch ExtendedBiddingTriggered, verify ScheduledCloseAt unchanged

### S3f: Retrospective

- `docs/retrospectives/M9-S3-seller-query-endpoints-housekeeping-retrospective.md`

## Explicitly out of scope

- **Frontend changes.** No seller UI, no bidder/ops changes, no `client/` touches (unless the cache-bridge fix is localised — that's the one sanctioned frontend touch). M9-S4+ consumes these endpoints.
- **New domain events.** The endpoints query existing projections. The `SellerSettlementSummary` consumes an existing integration event. No new contract types.
- **Obligations endpoint for winners.** The `ObligationStatusView` has `WinnerId` but the seller query filters by `SellerId` only. Winner-side obligation queries are not M9 scope.
- **Failed settlement details for sellers.** The `SellerSettlementSummary` captures completed settlements only (from `SettlementCompleted`). Failed settlement status is available via the listing's `PendingSettlement.Status == Failed`. A combined view is post-MVP.
- **`docs/STATUS.md` regeneration.** Deferred to M9-S7.
- **Changing existing RabbitMQ queue topology.** Only adding one new publish route for `ExtendedBiddingTriggered` → `listings-auctions-events`.
- **Extended-bidding e2e banner assert.** The M8-S7 e2e marks the spot for the banner assert to be re-added; that's an M9-S7 follow-up after the seller console's frontend renders it.

## Conventions to pin or follow

- **Query endpoint pattern:** `GetSellerListingsEndpoint` from M9-S2 is the precedent — `IQuerySession` + `.Where(x => x.SellerId == sellerId)` + `IReadOnlyList<T>` return.
- **Handler-driven tolerant-upsert:** The `PendingSettlementHandler` shape — LoadAsync, construct if absent, mutate via `with`, session.Store. Per `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert".
- **Sticky handler bindings:** Match the BC's existing queue topology. Obligations query endpoint has no handler (pure query). Settlement handler rides `settlement-settlement-events`.
- **Listings handler extension:** New method on `AuctionStatusHandler`, matching the existing 6-method pattern. Class-level `[StickyHandler("listings-auctions-events")]` covers the new method.
- **`[AllowAnonymous]` on seller-facing endpoints:** per CLAUDE.md and ADR-024.
- **`sealed record` for new types.**
- **Cross-BC handler isolation in tests:** each BC test fixture's exclusion set may need extension. Check whether new handlers or events cause discovery conflicts.

## Spec delta

Per ADR 020: this slice has **no spec consequence** on narratives or workshops. It exposes existing read models over HTTP and adds a Settlement-side projection from an existing integration event. The Listings handler addition is a carry-forward fix, not a new narrative Moment. The spec consequence is limited to the endpoint surface audit in the milestone doc (§2): the final two gaps (obligation status, settlement summary) are closed by this slice, and the M8-S7 carry-forward (Listings `ExtendedBiddingTriggered` handler) is resolved.

## Acceptance criteria

- [ ] `GET /api/obligations/status?sellerId={sellerId}` returns the seller's obligation status views
- [ ] `SellerSettlementSummary` exists as a handler-driven document in the Settlement BC, registered in `SettlementModule.ConfigureMarten()`
- [ ] `GET /api/settlement/summaries?sellerId={sellerId}` returns the seller's settlement summaries
- [ ] `AuctionStatusHandler.Handle(ExtendedBiddingTriggered)` advances `CatalogListingView.ScheduledCloseAt`
- [ ] `ExtendedBiddingTriggered` publish route added to `listings-auctions-events` in Program.cs
- [ ] Cache-bridge burst-final hardening evaluated and either shipped or deferred with rationale
- [ ] Integration tests cover happy path, empty, and filtering for both query endpoints
- [ ] Integration tests cover ExtendedBiddingTriggered handler (happy path + Withdrawn guard)
- [ ] Existing .NET build succeeds: 0 errors, 2 CS0108 warnings (baseline held)
- [ ] Existing .NET tests pass: 316 baseline preserved or grown
- [ ] No new domain events, no new integration events, no new BC modules
- [ ] No frontend changes (unless the cache-bridge fix is localised)
- [ ] `docs/retrospectives/M9-S3-seller-query-endpoints-housekeeping-retrospective.md` written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

- **SellerSettlementSummary sticky queue:** The handler consumes `SettlementCompleted` which `PendingSettlementHandler` already handles on `settlement-settlement-events`. With `MultipleHandlerBehavior.Separated`, two handlers for the same message type on the same queue should each get their own chain (per ADR 027). Verify this works in the test fixture.
- **Obligations query: Marten schema for IQuerySession:** The `ObligationStatusView` is registered in `ObligationsModule.ConfigureMarten()` with schema `obligations`. The query endpoint uses `IQuerySession` which reads from the same Marten store. No additional registration needed — but verify the query endpoint test works with the existing fixture.
