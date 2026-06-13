# M9-S2: Backend Precursor — Seller Listing Endpoints

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S2 of M9 (first backend precursor slice)
**Narrative:** `docs/narratives/004-seller-publishes-and-withdraws-listing.md` (seller-vantage listing lifecycle; S2 exposes the backend commands the seller console will drive in M9-S4)
**Agent:** @PSA
**Estimated scope:** one PR, ~8-10 files (3 endpoint classes, 1 projection, 1 query endpoint, integration tests, module registration, retro)

---

## Preconditions

This prompt assumes **`docs/milestones/M9-seller-console.md` exists** (authored 2026-06-13, PR #103). Per AUTHORING.md rule 3 the milestone doc is authoritative for scope. M9-S1 shipped (`2edc0da`, PR #104) — the shared extraction and seller SPA scaffold are on `main`.

## Goal

Wire the three missing seller-facing HTTP endpoints scoped in the M9 milestone doc (S2 row) so the seller console (M9-S4+) has a complete backend surface for listing management:

1. **`POST /api/selling/listings/submit`** — thin gateway over the existing `SubmitListing` command (cascade pattern, matching `WithdrawListingEndpoint` precedent)
2. **`PUT /api/selling/listings/draft`** — thin gateway over the existing `UpdateDraftListing` command (same cascade pattern)
3. **`GET /api/selling/listings?sellerId={sellerId}`** — new query endpoint against a `SellerListingSummary` inline projection (resolving OQ-2)

Each endpoint gets integration tests via the existing `SellingTestFixture` (Alba + Testcontainers). No frontend changes.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | Authoritative for scope. §7 S2 row and OQ-2. |
| `CLAUDE.md` | Routing layer and global conventions. |
| `docs/skills/wolverine-message-handlers/SKILL.md` | Handler and HTTP endpoint patterns. |
| `docs/skills/critter-stack-testing-patterns/SKILL.md` | Integration test patterns, cross-BC handler isolation. |
| `docs/skills/marten-projections/SKILL.md` | Projection registration and inline lifecycle. |
| `src/CritterBids.Selling/WithdrawListingEndpoint.cs` | The thin-gateway cascade precedent in this BC. |
| `src/CritterBids.Selling/SubmitListing.cs` | The existing handler (bus-only, `[WriteAggregate]`). |
| `src/CritterBids.Selling/UpdateDraftListing.cs` | The existing handler (bus-only, `[WriteAggregate]`). |
| `src/CritterBids.Selling/SellerListing.cs` | The aggregate shape — projection mirrors this. |
| `src/CritterBids.Selling/SellingModule.cs` | Module registration — projection registers here. |
| `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs` | Test fixture with cross-BC exclusions. |
| `tests/CritterBids.Selling.Tests/CreateDraftListingApiTests.cs` | HTTP endpoint test pattern (Alba + TrackedHttpCall). |

## OQ-2 resolution: SellerListingSummary inline projection

**Decision:** A `SellerListingSummary` Marten inline snapshot projection in the Selling BC.

**Why a projection, not an aggregate query:** The `SellerListing` aggregate is event-sourced — it's rebuilt from events via `AggregateStreamAsync`, which works per-stream (per listing). There is no way to query across streams by `SellerId` without a stored document. A `SellerListingSummary` document is stored in PostgreSQL and queryable via `session.Query<SellerListingSummary>().Where(x => x.SellerId == sellerId)`.

**Why inline, not async:** Inline projections update in the same transaction as the event append. The seller creates a draft and immediately sees it in "my listings" — no eventual-consistency lag. This matches the demo scenario's expectations and avoids a "where's my listing?" UX problem in M9-S4.

**Projection shape:** The summary carries the fields the "my listings" dashboard needs — Id, SellerId, Title, Format, Status, StartingBid, ReservePrice, BuyItNowPrice, PublishedAt, CreatedAt. The projection has `Apply` methods for DraftListingCreated, DraftListingUpdated, ListingSubmitted, ListingApproved, ListingPublished, ListingRejected, and ListingWithdrawn — mirroring the aggregate's Apply methods but writing a queryable document.

**Registration:** `Projections.Snapshot<SellerListingSummary>(SnapshotLifecycle.Inline)` in `SellingModule.ConfigureMarten()`. The snapshot lifecycle stores the document inline with event appends; no async daemon needed.

## In scope

### S2a: `SellerListingSummary` inline projection

- New file: `src/CritterBids.Selling/SellerListingSummary.cs`
- A `sealed class` (Marten projections need public setters) with `Apply` methods for all seven SellerListing event types
- Fields: `Id` (Guid, stream ID), `SellerId`, `Title`, `Format`, `Status`, `StartingBid`, `ReservePrice`, `BuyItNowPrice`, `PublishedAt`, `CreatedAt`
- Registered in `SellingModule.ConfigureMarten()` as `opts.Projections.Snapshot<SellerListingSummary>(SnapshotLifecycle.Inline)`
- Schema: `selling` (matches `RegisteredSeller`)

### S2b: `POST /api/selling/listings/submit` endpoint

- New file: `src/CritterBids.Selling/SubmitListingEndpoint.cs`
- Thin gateway pattern (matches `WithdrawListingEndpoint`): takes `SubmitListing` as JSON body, cascades the command, returns 202 Accepted
- `[AllowAnonymous]` — seller-facing, not staff-gated
- The existing `SubmitListingHandler` stays untouched as a message handler

### S2c: `PUT /api/selling/listings/draft` endpoint

- New file: `src/CritterBids.Selling/UpdateDraftListingEndpoint.cs`
- Thin gateway pattern: takes `UpdateDraftListing` as JSON body, cascades the command, returns 202 Accepted
- `[AllowAnonymous]`
- The existing `UpdateDraftListingHandler` stays untouched as a message handler

### S2d: `GET /api/selling/listings?sellerId={sellerId}` query endpoint

- New file: `src/CritterBids.Selling/GetSellerListingsEndpoint.cs`
- Wolverine HTTP GET endpoint using `IQuerySession` to query the `SellerListingSummary` projection
- Filters by `sellerId` query parameter; returns `IReadOnlyList<SellerListingSummary>`
- `[AllowAnonymous]`

### S2e: Integration tests

- New file: `tests/CritterBids.Selling.Tests/SubmitListingApiTests.cs` — HTTP-level tests for the submit endpoint:
  - Happy path: draft listing submitted, 202 returned, listing transitions to Published
  - Guard: non-draft listing returns error (the handler throws; the test verifies the dispatch)
- New file: `tests/CritterBids.Selling.Tests/UpdateDraftListingApiTests.cs` — HTTP-level tests for the update endpoint:
  - Happy path: draft listing updated, 202 returned, update event appended
  - Guard: non-draft listing returns error
- New file: `tests/CritterBids.Selling.Tests/GetSellerListingsApiTests.cs` — HTTP-level tests for the query endpoint:
  - Happy path: create listings for a seller, query by sellerId, verify the projection returns them
  - Empty: query for unknown sellerId returns empty list
  - Filtering: listings from a different seller are not returned

### S2f: Retrospective

- `docs/retrospectives/M9-S2-seller-listing-endpoints-retrospective.md` — written last; carries the `**Prompt:**` header line and the `## Spec delta -- landed?` paragraph.

## Explicitly out of scope

- **Frontend changes.** No seller UI, no bidder/ops changes, no `client/` touches. M9-S4+ consumes these endpoints.
- **New domain events.** The endpoints expose existing commands over HTTP. No new event types.
- **Seller registration validation on submit/update endpoints.** The `SubmitListingHandler` does not validate seller registration (it validates listing state). The `CreateDraftListingHandler` already gates on seller registration. This is consistent — only creation requires the gate.
- **The query endpoint's obligation or settlement status.** Those are M9-S3 (backend precursor continued). This slice covers only listing-management endpoints.
- **Listings `ExtendedBiddingTriggered` handler.** That's M9-S3 housekeeping.
- **Cache-bridge burst-final hardening.** That's M9-S3 evaluation.
- **Changing the existing `POST /api/listings/draft` route.** The create-draft endpoint stays at its M2-era route. The new endpoints use the `/api/selling/listings/` prefix.
- **`docs/STATUS.md` regeneration.** Deferred to M9-S7.

## Conventions to pin or follow

- **Thin-gateway cascade pattern:** `WithdrawListingEndpoint` is the canonical precedent in this BC. Submit and update endpoints follow the exact same shape: `(IResult, Command) Post/Put(Command command)`. The tuple return type tells Wolverine to send the HTTP response AND cascade the command.
- **Inline snapshot projection:** `Projections.Snapshot<T>(SnapshotLifecycle.Inline)` — the Marten pattern for a single-stream read model that updates in the same transaction as event appends. The projection class has `Apply` methods, same as an aggregate.
- **`[AllowAnonymous]` on seller-facing endpoints:** per CLAUDE.md and ADR-024, public endpoints stay `[AllowAnonymous]`. Only staff-facing endpoints use `[Authorize(Policy = "StaffOnly")]`.
- **`sealed record` for commands, events:** existing. No change.
- **`IReadOnlyList<T>` for collections:** the query endpoint returns `IReadOnlyList<SellerListingSummary>`.
- **Cross-BC handler isolation in tests:** the existing `SellingTestFixture` has exclusions for all foreign BCs. No new exclusions needed (the new endpoints don't introduce new event types that foreign BCs consume).
- **Schema namespace:** `selling` (matches `RegisteredSeller`).

## Spec delta

Per ADR 020: this slice has **no spec consequence** on narratives or workshops. It exposes existing bus-only commands over HTTP and adds a Selling-side read model. No new Moments are implemented; no new domain events; no narrative Document History rows. The spec consequence is limited to the endpoint surface audit in the milestone doc (§2): three of six gaps are closed by this slice. The retro's `## Spec delta -- landed?` paragraph confirms: no spec consequence; this is a backend-precursor infrastructure slice.

## Acceptance criteria

- [ ] `SellerListingSummary` exists as an inline snapshot projection in the Selling BC, registered in `SellingModule.ConfigureMarten()`
- [ ] `POST /api/selling/listings/submit` returns 202 Accepted and cascades `SubmitListing` to the existing handler
- [ ] `PUT /api/selling/listings/draft` returns 202 Accepted and cascades `UpdateDraftListing` to the existing handler
- [ ] `GET /api/selling/listings?sellerId={sellerId}` returns the seller's listings from the `SellerListingSummary` projection
- [ ] Integration tests cover happy path and guard conditions for all three endpoints
- [ ] Existing .NET build succeeds: 0 errors, 2 CS0108 warnings (baseline held)
- [ ] Existing .NET tests pass: 307 baseline preserved or grown
- [ ] No new domain events, no new integration events, no new BC modules
- [ ] No frontend changes — `client/` untouched
- [ ] `docs/retrospectives/M9-S2-seller-listing-endpoints-retrospective.md` written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

- **Snapshot lifecycle vs explicit SingleStreamProjection:** `Projections.Snapshot<T>(SnapshotLifecycle.Inline)` is the simplest registration but may not support schema namespace assignment. If the Marten API requires a `SingleStreamProjection<T>` subclass for schema control, fall back to that pattern. Resolve by trying the simpler path first.
- **Query endpoint return shape:** If the seller console needs pagination in M9-S4, the query endpoint may need to evolve. For now, return the full list — the expected listing count per seller in the demo scenario is single-digit. Flag if the projection grows large.
