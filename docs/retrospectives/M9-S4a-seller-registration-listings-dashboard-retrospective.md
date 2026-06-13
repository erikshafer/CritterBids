# M9-S4a: Seller Registration + My Listings Dashboard — Retrospective

**Date:** 2026-06-13
**Milestone:** M9 — Seller Console
**Slice:** S4a — Seller registration + my listings dashboard
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S4a-seller-registration-listings-dashboard.md`

## Baseline

- **.NET build:** 0 errors, 2 CS0108 warnings (saga Version hiding — baseline since M8-S7)
- **.NET tests:** 326 all pass (10 projects) — untouched this slice (zero `.cs` changes)
- **Frontend Vitest:** 74 (bidder 25, ops 47, seller 2)
- **Playwright e2e:** 1 (local-only, two-bidder bid-war)
- **npm workspace members:** 5 (bidder, ops, seller, shared, e2e)

## Items completed

| Item | Description |
|------|-------------|
| S4a-1 | Seller registration flow — SessionContext extended with `isRegisteredSeller`, `registerAsSeller()`, `isRegistering`, `registrationError` |
| S4a-2 | My Listings dashboard — Zod schema, TanStack Query hook, ListingsPage with cards |
| S4a-3 | AppShell + navigation updates — conditional "My Listings" nav, home→listings redirect after registration |
| S4a-4 | shadcn/ui components — Button, Card, Badge, Skeleton (copied from bidder) |
| S4a-5 | Shared presentational components — EmptyState, ErrorState, formatUsd, sellerStatusVariant |
| S4a-6 | Tests — 18 new tests across schema, listings page, session context |
| S4a-7 | Live smoke — full HTTP journey verified (session → register → listings query → create draft → verify listing) |
| S4a-8 | Retrospective — this file |

## S4a-1: Seller registration flow

**Why this approach.** The registration state lives in `SessionContext` rather than a separate context because the seller's identity (ParticipantId + IsRegisteredSeller) is one logical unit — the `registerAsSeller` mutation consumes the participantId from the same context it writes `isRegisteredSeller` into. A separate context would require cross-context coordination.

Registration state is persisted to `sessionStorage` alongside the participantId (key: `critterbids.seller.isRegisteredSeller`). The alternative — re-deriving from the backend on every page load — would require a `GET /api/participants/{id}` read endpoint that does not exist. The persisted `true` is safe: the backend validates seller status on every command anyway (e.g. `CreateDraftListing.ValidateAsync` checks `IsRegisteredAsync`), and a stale `true` causes a graceful 403, not silent corruption.

**Structural metrics:**

| Metric | Before | After |
|--------|--------|-------|
| SessionState fields | 2 (participantId, status) | 6 (+isRegisteredSeller, registerAsSeller, isRegistering, registrationError) |
| sessionStorage keys | 1 (participantId) | 2 (+isRegisteredSeller) |

### Registration body finding (live smoke catch)

The initial implementation sent an empty body `{}` to `POST /api/participants/{id}/register-seller`. The live smoke caught a 500: `ArgumentOutOfRangeException: Cannot use an empty Guid as the stream id`. Root cause: Wolverine's `[WriteAggregate]` identity resolution tries `participantId` (camelCase of aggregate + "Id") in the body first, then falls back to the route `{id}` — but the empty body yielded `Guid.Empty` before the fallback could activate.

**Fix:** Send `{ "participantId": "<id>" }` in the request body. The test was updated to assert the body includes the participantId.

**Lesson:** Same pattern as M8-S2's empty-body 400 — Wolverine HTTP POST endpoints bind command properties from the body, and an empty body leaves Guid fields as `Guid.Empty`. The route parameter fallback path doesn't reliably override body-bound values when `[WriteAggregate]` identity resolution runs. Always include the identity field in the body.

## S4a-2: My Listings dashboard

Follows the bidder's `CatalogPage` pattern exactly: TanStack Query with `queryOptions`, Zod-parsed at the fetch boundary, card grid with loading skeleton and empty/error states.

The `SellerListingSummary` Zod schema lives in `client/seller/src/listings/schema.ts` — seller-specific, not shared. The bidder's `CatalogListing` (from `@critterbids/shared/schemas`) is the public catalog view; `SellerListingSummary` is the seller's private portfolio view with different fields (includes `reservePrice`, which is confidential on the catalog side).

**Seller status badge variants** differ from the bidder's catalog variants:

| Status | Seller badge | Bidder badge |
|--------|-------------|--------------|
| Draft | outline | n/a |
| Submitted | outline | n/a |
| Published | secondary | n/a |
| Open | n/a | secondary |
| Rejected | destructive | n/a |
| Withdrawn | destructive | destructive |
| Sold | n/a | default |

The seller sees lifecycle states (Draft→Published→Withdrawn); the bidder sees auction states (Open→Sold→Settled). Different perspectives, same architecture.

## S4a-4: shadcn/ui components

Button, Card, Badge, and Skeleton copied from the bidder's `components/ui/`. Per ADR 013, each SPA owns its component instances — no shared UI component library in `@critterbids/shared`. This is the third time these components are copied (bidder → ops → seller); if a fourth consumer emerges, the pattern library question resurfaces.

## Test results

| Phase | Seller Tests | Result |
|-------|-------------|--------|
| Baseline | 2 | Pass |
| After schema tests | 10 | Pass |
| After listings page tests | 15 | Pass |
| After session context tests | 20 | Pass |
| Final (all workspaces) | bidder 25 + ops 47 + seller 20 = 92 | Pass |

## Build state at session close

- **Build errors:** 0 (seller `tsc --noEmit` + `vite build` clean)
- **Build warnings:** 2 SignalR `INVALID_ANNOTATION` rolldown warnings (third-party, existing baseline)
- **Backend .NET build:** untouched (zero `.cs` changes)
- **Backend .NET tests:** untouched (326 pass, 10 projects)
- **Seller Vitest:** 20 (was 2; +18 new)
- **Bidder/ops Vitest:** 25/47 (unchanged)

Negative-space assertions:
- `.cs` files modified: 0
- Backend test files modified: 0
- `@critterbids/shared` files modified: 0

## Key learnings

1. **Wolverine `[WriteAggregate]` identity binding from request body vs route parameter is order-dependent.** The body's camelCase property name is tried first; the route `{id}` fallback only activates if the body field is absent, not if it's `Guid.Empty`. Always include the identity field in the body for POST endpoints with `[WriteAggregate]`. This extends the M8-S2 lesson (empty-body 400) into the `[WriteAggregate]` identity-resolution path.

2. **Seller status badge variants are semantically distinct from bidder variants.** Don't reuse the bidder's `statusVariant` — the two apps render different status vocabularies from different backend projections. The `sellerStatusVariant` function maps the `ListingStatus` enum; the bidder's `statusVariant` maps the `CatalogListingView.Status` string.

3. **Registration persistence in `sessionStorage` is the right call when no read endpoint exists.** The alternative (re-derive from backend) would require a new endpoint that doesn't exist. The persisted boolean is safe because the backend validates independently on every command.

4. **HTTP-only smoke is sufficient when Chrome is unavailable.** The HTTP journey (session → register → create draft → query listings) exercises the full request-contract surface. The browser smoke adds UI rendering verification but the HTTP probe caught the one real bug (registration body). Mark the browser smoke as explicitly unchecked per M8-S4 precedent.

## Findings against narrative

Narrative 004 Moment 1 (seller registration) implemented faithfully. The one-click registration flow matches the narrative's description: "The seller dashboard POSTs `RegisterAsSeller { ParticipantId }` to `/api/participants/{id}/register-seller`" — with the finding that the body must include `participantId` (the narrative says `RegisterAsSeller { ParticipantId }` which implies the field is present).

No `narrative-update`, `workshop-update`, `code-update`, or `document-as-intentional` findings.

### WithdrawListing endpoint is StaffOnly

The `WithdrawListing` endpoint at `POST /api/selling/listings/withdraw` is `[Authorize(Policy = "StaffOnly")]` per M7-S6 / ADR-024. The seller console (anonymous) cannot trigger withdrawal. The seller sees Withdrawn *status* on listings (via the `SellerListingSummary` projection) but cannot initiate it. This is by design — withdrawal is a staff action at MVP.

Narrative 004 Moment 5 describes GreyOwl12 withdrawing from a "seller dashboard" — that narrative was forward-spec (M4-S2 era) before the M7-S6 decision to gate withdrawal behind `StaffOnly`. The narrative's UI framing ("The seller dashboard sends a `WithdrawListing` command") is aspirational for a post-MVP seller-initiated withdrawal path. No narrative update needed — the narrative's `UX-or-UI-detail` deferral explicitly notes "M6 frontend MVP territory" for the seller-dashboard rendering.

## Spec delta — landed?

Prompt declared limited spec consequence: narrative 004 Moment 1 gains a frontend implementation. No narrative Document History amendment needed (the narrative already covers Moment 1 fully as a lived Moment). No workshop amendment. The milestone doc's exit criteria advance: the seller SPA begins rendering seller-perspective journeys via the registration flow and the listings dashboard.

## Verification checklist

- [x] Seller registration one-click flow works: session establishes → registration prompt appears → click registers → 200/409 both result in `isRegisteredSeller: true`
- [x] `GET /api/selling/listings?sellerId=` consumed via TanStack Query with Zod parsing
- [x] My Listings page renders listing cards with title, format badge, status badge, starting bid, created date
- [x] Empty state renders when seller has no listings
- [x] Error state renders with retry on query failure
- [x] Loading skeleton renders during fetch
- [x] Navigation: "My Listings" visible only after registration; clicking navigates to `/listings`
- [x] shadcn/ui components (Button, Card, Badge, Skeleton) present in `client/seller/src/components/ui/`
- [x] `EmptyState` / `ErrorState` presentational components present
- [x] Zod schema test covers parse + rejection + nullable fields
- [x] Registration test covers the mutation flow
- [x] My Listings page test covers rendering, empty, and error states
- [x] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [x] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests, seller ≥ 2 tests (grown to 20)
- [ ] Live smoke against Aspire stack — **HTTP smoke complete** (caught registration body bug); browser smoke unchecked (Chrome not installed, admin-install required; per M8-S4 precedent)
- [x] No backend changes (zero `.cs` files touched)
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [x] This retrospective written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph

## What remains / next session should verify

### In scope for M9, deferred to S4b

- **Create-draft form:** `react-hook-form` + `@hookform/resolvers` installation, 10-field form with conditional visibility (Flash vs Timed), Zod validation schema for `CreateDraftListing`
- **Edit-draft:** Partial-update form driving `PUT /api/selling/listings/draft`
- **Submit-for-publication:** Action button on draft listings driving `POST /api/selling/listings/submit`
- **Listing detail view:** Per-listing detail page (if warranted by S4b scope assessment)

### In scope for M9, deferred to later slices

- **WithdrawListing from seller console:** Requires a seller-facing (anonymous) withdraw endpoint — potential backend exception for M9-S5 or later, or explicitly out of M9 scope (withdrawal is a staff action at MVP per ADR-024)
- **Live auction observation:** M9-S5
- **Obligation fulfillment:** M9-S6
- **Browser smoke:** Blocked by Chrome admin-install; verify when available (M9-S7 e2e slice is the natural home)
- **STATUS.md regeneration:** M9-S7

### Out of scope, tracked elsewhere

- Cache-bridge burst-final hardening (deferred from M9-S3 to M9-S5/S7)
- Extended-bidding e2e banner assert (M9-S7)
