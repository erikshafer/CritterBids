# M9-S4a: Seller SPA — Registration + My Listings Dashboard

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S4a of M9 (first frontend-heavy seller slice; S4 split per handoff guidance)
**Narrative:** `docs/narratives/004-seller-publishes-and-withdraws-listing.md` (Moment 1 — registration; My Listings read surface prerequisite for Moments 2–5)
**Agent:** @PSA
**Estimated scope:** one PR, ~20-25 files (pages, components, hooks, schemas, tests, retro)

---

## Preconditions

This prompt assumes **`docs/milestones/M9-seller-console.md` exists** (authored 2026-06-13, PR #103) and that M9-S1 through M9-S3 all shipped (`f800a9f`, PRs #104–#106). All five seller-facing endpoint gaps from the milestone audit (§2) are closed. The working branch starts from clean `main` at `f800a9f`.

## S4 split rationale

The milestone doc's S4 row covers registration, listing dashboard, create-draft form, edit-draft, submit, and withdraw — spanning narrative 004 Moments 1–5. The handoff anticipated this may be too broad for one slice. Assessment:

- The create-draft form alone requires 10 fields with conditional visibility (Flash vs Timed), `react-hook-form` + `@hookform/resolvers` installation (not present in the workspace), and Zod validation schemas against listing-time fields.
- The `WithdrawListing` endpoint is `[Authorize(Policy = "StaffOnly")]` (M7-S6, ADR-024) — the anonymous seller SPA **cannot trigger withdrawal**. The seller console renders withdrawal *status* on listings but cannot initiate it. This is an ADR-024 design decision, not a gap.

**Split:**
- **S4a (this slice):** Seller registration + My Listings dashboard — the read + register surface
- **S4b (next slice):** Create-draft form, edit-draft, submit-for-publication — the write operations

This matches the M8 precedent (S3a/S3b/S3c split) for slices whose surface area is too broad.

## Goal

Wire the seller SPA's two foundational surfaces: one-click seller registration (narrative 004 Moment 1) and the "my listings" dashboard consuming the M9-S2 query endpoint (`GET /api/selling/listings?sellerId=`). After this slice, the seller console is a working read surface over the seller's listing portfolio, with seller identity established. S4b adds the write operations that populate that portfolio from the UI.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | Authoritative for scope. §7 S4 row, §2 endpoint audit. |
| `CLAUDE.md` | Routing layer, global conventions, §Frontend. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` | **Required** — verification ladder, backend-exception discipline, live-smoke rules. |
| `docs/narratives/004-seller-publishes-and-withdraws-listing.md` | Moment 1 (registration). |
| `client/seller/src/` | The M9-S1 scaffold to build on. |
| `client/bidder/src/` | Precedent for TanStack Query, Zod schemas, session management, page patterns. |
| `client/shared/src/schemas/` | Shared Zod schemas; evaluate whether `SellerListingSummary` belongs here or in seller-local. |
| `src/CritterBids.Selling/SellerListingSummary.cs` | The backend read-model shape (10 fields). |
| `src/CritterBids.Selling/GetSellerListingsEndpoint.cs` | The query endpoint: `GET /api/selling/listings?sellerId=`. |
| `src/CritterBids.Participants/Features/RegisterAsSeller/RegisterAsSeller.cs` | The registration endpoint: `POST /api/participants/{id}/register-seller`. |

## In scope

### S4a-1: Seller registration flow

- **Registration state in `SessionContext`:** Extend the existing `SessionContext` to track `isRegisteredSeller: boolean`. After session establishment, the seller SPA does not auto-register — the user must opt in (narrative 004 Moment 1's one-click action).
- **Registration mutation hook:** `useRegisterAsSeller()` — calls `POST /api/participants/{id}/register-seller` with the session's `ParticipantId`. On 200, updates `SessionContext` to `isRegisteredSeller: true`. Handles 409 (already registered) as success (idempotent). On error, surfaces the failure.
- **Registration UI:** On `HomePage` (or a gated route), render a registration prompt when `isRegisteredSeller` is false + session is established. One button: "Register as Seller." On success, the UI transitions to the listings dashboard. Matches the narrative: "The seller dashboard refreshes; the seller-registration step shows complete."

### S4a-2: My Listings dashboard page

- **Zod schema:** `sellerListingSummarySchema` for the `SellerListingSummary` wire shape. Fields: `id` (Guid string), `sellerId`, `title`, `format` (enum: `"Flash" | "Timed"`), `status` (enum: `"Draft" | "Submitted" | "Published" | "Rejected" | "Withdrawn"`), `startingBid` (number), `reservePrice` (number | null), `buyItNowPrice` (number | null), `createdAt` (ISO string), `publishedAt` (ISO string | null). This schema is seller-specific (not the catalog shape bidders consume) — it lives in the seller app, not `@critterbids/shared`.
- **Query hook:** `useSellerListings(sellerId)` — calls `GET /api/selling/listings?sellerId={sellerId}`, Zod-parses the response, returns via TanStack Query. Same `fetchParsed` pattern as the bidder's `queries.ts`.
- **Listings page:** Route at `/listings`. Shows the seller's listing portfolio as a card grid. Each card shows: title, format badge, status badge, starting bid, created date. Empty state when no listings exist ("You haven't created any listings yet."). Loading skeleton. Error state with retry.
- **Status badge variants:** Map `ListingStatus` values to badge variants: Draft → outline, Submitted → outline, Published → secondary, Rejected → destructive, Withdrawn → destructive. (Distinct from the bidder's catalog status variants — these are seller-lifecycle statuses.)
- **Routing:** Add `/listings` route to the seller router. Update `AppShell` nav to include a "My Listings" link (visible only when registered).

### S4a-3: App shell + navigation updates

- **Conditional navigation:** The `AppShell` nav shows "My Listings" only after registration. Before registration, the shell renders the home page with the registration prompt.
- **Redirect after registration:** On successful registration, navigate to `/listings`.

### S4a-4: shadcn/ui components

- Add the shadcn/ui components the seller SPA needs: `Button`, `Card` (Card/CardHeader/CardTitle/CardContent), `Badge`, `Skeleton`. Copy from the bidder app's `components/ui/` — these are locally-owned per ADR 013 (no shared UI components in `@critterbids/shared`; each SPA owns its component instances).

### S4a-5: Shared presentational components

- `EmptyState` and `ErrorState` components — same pattern as the bidder's `States.tsx`.
- `formatUsd` and seller-specific `statusVariant` utilities in `lib/format.ts`.

### S4a-6: Tests

- **Zod schema test:** Validate `sellerListingSummarySchema` parses a representative payload; rejects missing required fields; handles nullable fields correctly.
- **Registration test:** Render the registration prompt; simulate click; mock the POST; verify state transition.
- **My Listings page test:** Mock the query response; verify cards render with correct data; verify empty state; verify error state with retry.
- **SessionContext test:** Verify the registration state tracks correctly (pre- and post-registration).
- All tests use the isolated test router pattern (no full AppShell mount in tests).

### S4a-7: Live smoke

- Run `dotnet run --project src/CritterBids.AppHost --launch-profile http`
- Open the seller SPA at `http://localhost:5175/seller/`
- Verify: session establishes, registration prompt appears, one-click registers, my-listings page loads (empty initially)
- If listings exist (from prior dev-seed runs), verify they render correctly
- Record smoke findings in the retro

### S4a-8: Retrospective

- `docs/retrospectives/M9-S4a-seller-registration-listings-dashboard-retrospective.md`

## Explicitly out of scope

- **Create-draft form, edit-draft, submit-for-publication.** These are S4b scope — the write operations that populate the listings dashboard this slice builds.
- **Withdraw action from the seller console.** `WithdrawListing` is `[Authorize(Policy = "StaffOnly")]` per M7-S6 / ADR-024. The seller sees Withdrawn *status* on listings but cannot trigger it. A seller-facing withdraw endpoint would require a new backend slice (sanctioned exception) — out of scope for M9-S4a and potentially for M9 entirely (the withdrawal action is a staff operation at MVP).
- **Live auction observation.** M9-S5 scope.
- **Obligation fulfillment.** M9-S6 scope.
- **`react-hook-form` installation.** Not needed until S4b (the create-draft form).
- **Backend changes.** Zero `.cs` touches. All endpoints exist. Frontend-slice-discipline Rule 2 applies.
- **`@critterbids/shared` changes.** The `SellerListingSummary` schema is seller-specific; the shared catalog schema already exists for the bidder surface.
- **`docs/STATUS.md` regeneration.** Deferred to M9-S7.

## Conventions to pin or follow

- **Frontend-slice-discipline** — all four rules apply. Rule 1: read the backend shapes before writing client code (done in prompt authoring). Rule 2: render the lived subset. Rule 3: verify installed toolchain. Rule 4: live smoke.
- **TanStack Query pattern:** Same `queryOptions` + `useQuery` shape as the bidder's `queries.ts`.
- **Zod at the wire boundary:** Parse once at fetch time; consumers use the inferred TypeScript type, never raw JSON.
- **shadcn/ui components are locally owned:** Each SPA copies the components it needs; no shared UI component library.
- **Session storage key:** `critterbids.seller.participantId` (already established in the scaffold's `SessionContext`).
- **No module mocks for seams that accept injection:** The session context and query hooks are testable via dependency injection patterns, not `vi.mock`.

## Spec delta

Per ADR 020: this slice has **limited spec consequence**. Narrative 004 Moment 1 (seller registration) gains a concrete frontend implementation — the one-click registration flow renders the narrative's intent. No narrative Document History amendment is needed (the narrative already covers Moment 1 fully). No workshop amendment. The milestone doc's exit criteria advance: the seller SPA begins rendering seller-perspective journeys.

## Acceptance criteria

- [ ] Seller registration one-click flow works: session establishes → registration prompt appears → click registers → 200/409 both result in `isRegisteredSeller: true`
- [ ] `GET /api/selling/listings?sellerId=` consumed via TanStack Query with Zod parsing
- [ ] My Listings page renders listing cards with title, format badge, status badge, starting bid, created date
- [ ] Empty state renders when seller has no listings
- [ ] Error state renders with retry on query failure
- [ ] Loading skeleton renders during fetch
- [ ] Navigation: "My Listings" visible only after registration; clicking navigates to `/listings`
- [ ] shadcn/ui components (Button, Card, Badge, Skeleton) present in `client/seller/src/components/ui/`
- [ ] `EmptyState` / `ErrorState` presentational components present
- [ ] Zod schema test covers parse + rejection + nullable fields
- [ ] Registration test covers the mutation flow
- [ ] My Listings page test covers rendering, empty, and error states
- [ ] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [ ] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests, seller ≥ 2 tests (grown)
- [ ] Live smoke against Aspire stack recorded in retro (or explicitly marked as unchecked per M8-S4 precedent)
- [ ] No backend changes (zero `.cs` files touched)
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [ ] `docs/retrospectives/M9-S4a-seller-registration-listings-dashboard-retrospective.md` written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph

## Open questions

- **Registration persistence across page reloads:** The scaffold's `SessionContext` persists `participantId` in `sessionStorage`. Should `isRegisteredSeller` also persist there, or should it re-derive from an API call on reload? Lean: persist in `sessionStorage` alongside the participant ID — the registration is durable on the backend (idempotent 409), and a stale `true` is harmless (the backend still validates on every command). Re-derivation on every page load would require a `GET /api/participants/{id}` endpoint that doesn't exist.
- **SellerListingSummary schema location:** The bidder's `CatalogListing` schema lives in `@critterbids/shared` because both bidder and seller consume it. `SellerListingSummary` is seller-specific. Lean: seller-local in `client/seller/src/listings/schema.ts`.
