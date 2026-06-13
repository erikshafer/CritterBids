# M9-S2: Backend Precursor — Seller Listing Endpoints - Retrospective

**Date:** 2026-06-13
**Milestone:** M9 - Seller Console
**Slice:** S2 - Backend precursor (seller listing endpoints)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S2-seller-listing-endpoints.md`
**Duration:** ~1h

## Baseline

- .NET build: 0 errors, 2 CS0108 warnings (saga Version hiding — baseline)
- Selling BC tests: 36 passing
- Full solution tests: 307 passing
- Selling BC: 2 HTTP endpoints (`POST /api/listings/draft`, `POST /api/selling/listings/withdraw`), no query endpoint, no projection
- `SubmitListing` and `UpdateDraftListing` handlers bus-only (no HTTP surface)

## Items completed

| Item | Description |
|------|-------------|
| S2a | `SellerListingSummary` inline snapshot projection |
| S2b | `POST /api/selling/listings/submit` thin-gateway endpoint |
| S2c | `PUT /api/selling/listings/draft` thin-gateway endpoint |
| S2d | `GET /api/selling/listings?sellerId=` query endpoint |
| S2e | Integration tests: 9 new (2 submit, 2 update, 5 query) |
| S2f | Retrospective |

## S2a: SellerListingSummary inline projection

**Why this approach:** The `SellerListing` aggregate is event-sourced — state is rebuilt from events per stream via `AggregateStreamAsync`. You cannot query across streams by `SellerId`. A `SellerListingSummary` Marten document projection stores a queryable document per listing, filtered by `SellerId` for the "my listings" dashboard.

**Why inline, not async:** Inline means the projection updates in the same transaction as event appends. The seller sees their listing immediately after creating/submitting — no eventual-consistency lag for the demo scenario. Same trade-off as `CatalogListingView`, but simpler: this is a single-stream projection in the same BC, not a handler-driven cross-BC view.

**Registration:** `opts.Projections.Snapshot<SellerListingSummary>(SnapshotLifecycle.Inline)` in `SellingModule.ConfigureMarten()`. Schema: `selling` (matches `RegisteredSeller`).

**Discovery:** `SnapshotLifecycle` moved from `Marten.Events.Projections` to `JasperFx.Events.Projections` in Marten 9 (JasperFx extraction). The ctx7 docs still show the old namespace.

| Metric | Before | After |
|--------|--------|-------|
| Selling BC document types | 1 (`RegisteredSeller`) | 2 (`RegisteredSeller`, `SellerListingSummary`) |
| Inline projections | 0 | 1 |
| Apply methods on projection | — | 7 (all SellerListing event types) |

## S2b: POST /api/selling/listings/submit

**Why thin gateway:** Attaching `[WolverinePost]` to the existing `SubmitListingHandler.Handle` would deregister it as a message handler — the `WithdrawListingEndpoint` comment documents this exact footgun. The thin-gateway cascade pattern returns 202 Accepted and cascades the command for async handler execution.

**Handler shape after:**

```csharp
public static class SubmitListingEndpoint
{
    [WolverinePost("/api/selling/listings/submit")]
    [AllowAnonymous]
    public static (IResult, SubmitListing) Post(SubmitListing command)
        => (Results.Accepted($"/api/selling/listings/{command.ListingId}"), command);
}
```

## S2c: PUT /api/selling/listings/draft

Same shape as S2b — thin gateway, 202 Accepted, cascades `UpdateDraftListing`. Uses `[WolverinePut]` instead of `[WolverinePost]`.

## S2d: GET /api/selling/listings?sellerId=

**Why convention binding, not `[FromQuery]`:** The Selling BC project references `WolverineFx.Http.Marten` but not `Microsoft.AspNetCore.Mvc`. `[FromQuery]` is an MVC attribute not available in this project. Wolverine HTTP binds non-routed, non-service parameters from the query string by convention for GET endpoints — `Guid sellerId` is bound automatically.

**Handler shape after:**

```csharp
public static class GetSellerListingsEndpoint
{
    [WolverineGet("/api/selling/listings")]
    [AllowAnonymous]
    public static async Task<IReadOnlyList<SellerListingSummary>> Get(
        Guid sellerId, IQuerySession session, CancellationToken ct)
    {
        return await session.Query<SellerListingSummary>()
            .Where(x => x.SellerId == sellerId)
            .ToListAsync(ct);
    }
}
```

## S2e: Integration tests

| Test class | Tests | What it covers |
|------------|-------|---------------|
| `SubmitListingApiTests` | 2 | Happy path (202 + listing transitions to Published), guard (already-published listing) |
| `UpdateDraftListingApiTests` | 2 | Happy path (202 + fields updated), guard (published listing) |
| `GetSellerListingsApiTests` | 5 | Happy path, multiple listings, empty result, seller isolation, status reflects submit |

The guard tests for submit and update verify 202 from the thin gateway; the handler throws asynchronously. This is the correct behavior — the thin gateway returns before the handler runs.

## Test results

| Phase | Selling Tests | Full Solution | Result |
|-------|--------------|---------------|--------|
| Baseline | 36 | 307 | Pass |
| After S2a-S2d (build only) | — | — | 0 errors, 2 warnings (baseline) |
| After S2e (full run) | 45 | 316 | Pass |

## Build state at session close

- **Errors:** 0
- **Warnings:** 2 CS0108 (baseline — saga Version hiding in Auctions)
- **Selling BC HTTP endpoints:** 4 (was 2)
  - `POST /api/listings/draft` (M2 — CreateDraftListing)
  - `POST /api/selling/listings/withdraw` (M7 — WithdrawListing, StaffOnly)
  - `POST /api/selling/listings/submit` (M9-S2 — SubmitListing, AllowAnonymous)
  - `PUT /api/selling/listings/draft` (M9-S2 — UpdateDraftListing, AllowAnonymous)
- **Selling BC query endpoints:** 1 (was 0)
  - `GET /api/selling/listings?sellerId=` (M9-S2 — SellerListingSummary)
- **Selling BC inline projections:** 1 (was 0)
- **Selling BC tests:** 45 (was 36, +9)
- **Full solution tests:** 316 (was 307, +9)
- **`IDocumentSession` usage in new code:** 0 (query uses `IQuerySession`)
- **`IMessageBus` usage in new code:** 0 (cascade via tuple return)

## Key learnings

1. **`SnapshotLifecycle` lives in `JasperFx.Events.Projections`, not `Marten.Events.Projections`.** Marten 9's JasperFx extraction moved projection lifecycle enums to the base library. The ctx7 docs (which index markdown source, not compiled API) still reference the old namespace. Check the NuGet package XML docs (`Marten.xml`) when a namespace resolution fails.

2. **Wolverine HTTP binds query parameters by convention for GET endpoints.** The `[FromQuery]` attribute from `Microsoft.AspNetCore.Mvc` is not available in BC projects that only reference `WolverineFx.Http.Marten`. Non-routed, non-service primitive parameters are automatically bound from the query string. The convention makes the BC self-contained without an MVC dependency.

3. **The thin-gateway cascade pattern is the right default for exposing bus-only handlers over HTTP** when the existing handler uses `[WriteAggregate]`. Adding `[WolverinePost]` to an existing handler deregisters it as a message handler. Three of four Selling BC HTTP endpoints now use the thin-gateway shape (all except `CreateDraftListingHandler`, which was authored as an HTTP handler from the start).

## Findings against narrative

This slice does not implement a narrative Moment — it exposes existing bus-only commands over HTTP as backend precursors for M9-S4 (seller console listing management). No narrative drift; no findings to route.

## Spec delta - landed?

**No spec consequence.** This session is a backend-precursor infrastructure slice that exposes existing commands over HTTP and adds a Selling-side read model. No narrative Moments are implemented; no domain events are added; no narrative, workshop, or ADR was amended.

## Verification checklist

- [x] `SellerListingSummary` exists as an inline snapshot projection in the Selling BC, registered in `SellingModule.ConfigureMarten()`
- [x] `POST /api/selling/listings/submit` returns 202 Accepted and cascades `SubmitListing` to the existing handler
- [x] `PUT /api/selling/listings/draft` returns 202 Accepted and cascades `UpdateDraftListing` to the existing handler
- [x] `GET /api/selling/listings?sellerId={sellerId}` returns the seller's listings from the `SellerListingSummary` projection
- [x] Integration tests cover happy path and guard conditions for all three endpoints (9 tests)
- [x] Existing .NET build succeeds: 0 errors, 2 CS0108 warnings (baseline held)
- [x] Existing .NET tests pass: 307 baseline preserved; grown to 316
- [x] No new domain events, no new integration events, no new BC modules
- [x] No frontend changes — `client/` untouched
- [x] Retrospective written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M9-S3 (backend precursor continued):** Seller-facing query endpoints for obligation status and settlement summary. Listings `ExtendedBiddingTriggered` handler (M8-S7 carry-forward). Cache-bridge burst-final hardening evaluation.
- **M9-S4 (seller SPA listing management):** Consumes the three endpoints shipped in this slice. The `SellerListingSummary` projection shape may need extension if the seller dashboard requires fields not currently projected (e.g., `Duration`, `ExtendedBiddingEnabled`).
- **Pagination on the query endpoint:** Not needed for the demo scenario (single-digit listing count per seller). If the projection grows, the endpoint may need `skip/take` parameters — evaluate in M9-S4.
- **The thin-gateway 202 pattern means the seller console cannot get synchronous error feedback from submit/update.** The seller console will need to handle this via the push + re-query pattern (ADR 026) — a submit followed by a re-query against the projection to see the resulting status. This is consistent with the bidder app's existing pattern.
