# M9-S4b: Seller SPA — Listing Management Write Operations

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S4b of M9 (second frontend-heavy seller slice; the write half of the S4 split)
**Narrative:** `docs/narratives/004-seller-publishes-and-withdraws-listing.md` (Moments 2–4 — draft, publish, camera fast-path)
**Agent:** @PSA
**Estimated scope:** one PR, ~25-30 files (form components, mutation hooks, schemas, shadcn/ui additions, tests, retro)

---

## Preconditions

This prompt assumes M9-S4a shipped (PR #107, `fa1e319`). The seller SPA has a working read surface: registration flow, My Listings dashboard, session context with `isRegisteredSeller`, Zod schema for `SellerListingSummary`, TanStack Query hook. S4b adds the write operations — create-draft form, edit-draft, and submit-for-publication — that populate the dashboard S4a built.

## Goal

Wire the three listing write operations into the seller SPA: a 10-field create-draft form at `/listings/new` with `react-hook-form` + Zod validation and Flash/Timed conditional visibility; an edit-draft dialog on Draft-status listings; and a submit-for-publication action button. After this slice, a seller can create, edit, and publish listings entirely through the UI — the complete write surface for narrative 004 Moments 2–4.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | Authoritative for scope. §7 S4 row. |
| `CLAUDE.md` | Routing layer, global conventions, §Frontend. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` | **Required** — verification ladder, backend-exception discipline, live-smoke rules. |
| `.claude/skills/react-hook-form/SKILL.md` | **Required** — form patterns, Zod resolver integration. |
| `docs/narratives/004-seller-publishes-and-withdraws-listing.md` | Moments 2–4 are the story this slice renders. |
| `client/seller/src/` | The S4a foundation to build on. |
| `client/bidder/src/bidding/BidPanel.tsx` | Form + mutation precedent. |
| `src/CritterBids.Selling/CreateDraftListing.cs` | Backend handler (10 fields, ValidateAsync). |
| `src/CritterBids.Selling/UpdateDraftListing.cs` + `UpdateDraftListingEndpoint.cs` | Partial update shape: listingId, title?, reservePrice?, buyItNowPrice?. |
| `src/CritterBids.Selling/SubmitListing.cs` + `SubmitListingEndpoint.cs` | Submit command shape: listingId, sellerId. |

## In scope

### S4b-1: Install `react-hook-form` + `@hookform/resolvers`

- `npm install react-hook-form @hookform/resolvers --workspace @critterbids/seller`
- These packages are not present anywhere in the workspace; this is the first consumer.

### S4b-2: shadcn/ui component additions

- **Input** — copy from `client/bidder/src/components/ui/input.tsx` (the bid-amount field precedent)
- **Label** — standard shadcn/ui label primitive for accessible form fields
- **Select** — shadcn/ui select for the format dropdown (Flash / Timed)
- **Checkbox** — shadcn/ui checkbox for the extended-bidding toggle

### S4b-3: Create-draft form at `/listings/new`

- **Route:** Add `/listings/new` to the seller router.
- **Zod validation schema:** `createDraftSchema` for the command shape:
  - `title` — required string, non-empty
  - `format` — enum `"Flash" | "Timed"`
  - `startingBid` — required positive number
  - `reservePrice` — optional positive number
  - `buyItNowPrice` — optional positive number (must be >= reservePrice when both set)
  - `duration` — required for Timed (HH:MM:SS string), hidden/null for Flash
  - `extendedBiddingEnabled` — boolean, default false
  - `extendedBiddingTriggerWindow` — required when extendedBiddingEnabled (HH:MM:SS string)
  - `extendedBiddingExtension` — required when extendedBiddingEnabled (HH:MM:SS string)
- **Conditional field visibility:**
  - Duration field: visible and required when format is "Timed"; hidden when "Flash"
  - Extended-bidding fields (triggerWindow, extension): visible when `extendedBiddingEnabled` is checked; hidden otherwise
- **Form wiring:** `react-hook-form` with `@hookform/resolvers/zod`, `useForm()`, controlled components.
- **Mutation hook:** `useCreateDraft(sellerId)` — wraps `POST /api/listings/draft` with the session's sellerId injected. On success: invalidate `sellerListings` query and navigate to `/listings`.
- **Navigation:** "Create Listing" button on the ListingsPage (visible alongside the listings grid or empty state).

### S4b-4: Edit-draft dialog

- **Trigger:** "Edit" button on Draft-status listing cards on the ListingsPage.
- **Dialog/inline form:** Show the three mutable fields (title, reservePrice, buyItNowPrice). Pre-populated from the listing's current values.
- **Zod schema:** `editDraftSchema` — title (optional string), reservePrice (optional number), buyItNowPrice (optional number). BuyItNowPrice must be >= ReservePrice when both are provided.
- **Mutation hook:** `useEditDraft()` — wraps `PUT /api/selling/listings/draft`. On success: invalidate `sellerListings` query and close the dialog.
- **State guard:** Only Draft-status listings show the Edit button.

### S4b-5: Submit-for-publication action

- **Trigger:** "Submit for Publication" button on Draft-status listing cards on the ListingsPage.
- **Mutation hook:** `useSubmitListing()` — wraps `POST /api/selling/listings/submit` with `{ listingId, sellerId }`. On success: invalidate `sellerListings` query (status changes to Published via auto-approval chain).
- **Confirmation:** Inline "Are you sure?" or direct action (lean: direct, with pending state shown).
- **State guard:** Only Draft-status listings show the Submit button.

### S4b-6: Tests

- **Create-draft form schema tests:** Validate the Zod schema for all validation rules — required fields, conditional duration, extended-bidding conditional required, BIN >= reserve.
- **Create-draft form rendering test:** Render the form, verify all 10 fields present for Timed + extended bidding; verify conditional visibility toggles.
- **Edit-draft tests:** Render edit form, verify pre-populated values, verify mutation on submit.
- **Submit mutation test:** Verify the submit button triggers the mutation, verify listing query invalidation.
- **ListingsPage action buttons test:** Verify Draft listings show Edit + Submit buttons; non-Draft listings do not.

### S4b-7: Live smoke

- Run `dotnet run --project src/CritterBids.AppHost --launch-profile http`
- Open the seller SPA at `http://localhost:5175/seller/`
- Verify: register → create draft → verify in listings → edit draft → submit → verify Published status
- Record smoke findings in the retro

### S4b-8: Retrospective

- `docs/retrospectives/M9-S4b-seller-listing-management-retrospective.md`

## Explicitly out of scope

- **Withdraw action from the seller console.** `WithdrawListing` is `[Authorize(Policy = "StaffOnly")]` per M7-S6 / ADR-024. The seller sees Withdrawn *status* but cannot trigger it.
- **Listing detail page / individual listing view.** If warranted, a future slice. S4b is the write surface, not a detail read view.
- **Live auction observation.** M9-S5 scope.
- **Obligation fulfillment.** M9-S6 scope.
- **Backend changes.** Zero `.cs` touches. All three endpoints exist and are tested (M9-S2, PR #105).
- **`@critterbids/shared` changes.** The create-draft schema is seller-specific.
- **`docs/STATUS.md` regeneration.** Deferred to M9-S7.

## Conventions to pin or follow

- **Frontend-slice-discipline** — all four rules apply. Rule 1: read backend shapes before writing client code (done above). Rule 2: render the lived subset. Rule 3: verify installed toolchain. Rule 4: live smoke.
- **react-hook-form skill** — Zod resolver, controlled components, conditional field registration.
- **TanStack Query mutation pattern:** `useMutation` with `onSuccess` that invalidates the listings query via `queryClient.invalidateQueries`.
- **Zod at the wire boundary:** Parse/validate at form submission; the mutation sends the validated data.
- **shadcn/ui components are locally owned:** Each SPA copies the components it needs.
- **TimeSpan serialization:** The backend accepts ISO 8601 duration format (e.g. `"00:30:00"` for 30 minutes). The form collects human-readable input and converts to this format before submission.

## Spec delta

Per ADR 020: this slice has **moderate spec consequence**. Narrative 004 Moments 2–4 (GreyOwl12 drafts the keyboard, the keyboard is published, the camera is published) gain concrete frontend implementations. The seller can now perform the full listing-creation-through-publication flow through the UI. No narrative Document History amendment needed (the narrative already covers these Moments fully). The milestone doc's exit criteria advance: listing management write surface is complete.

## Acceptance criteria

- [ ] `react-hook-form` and `@hookform/resolvers` installed in `@critterbids/seller`
- [ ] shadcn/ui Input, Label, Select (or equivalent), and Checkbox components present
- [ ] Create-draft form at `/listings/new` with all 10 fields
- [ ] Duration field: required + visible for Timed, hidden for Flash
- [ ] Extended-bidding fields: visible when checkbox checked, hidden otherwise
- [ ] Zod validation: title required, startingBid positive, BIN >= reserve, conditional duration/extended-bidding
- [ ] Create-draft mutation calls `POST /api/listings/draft` and navigates to `/listings` on success
- [ ] Edit-draft on Draft-status listings: shows the 3 mutable fields, calls `PUT /api/selling/listings/draft`
- [ ] Submit-for-publication on Draft-status listings: calls `POST /api/selling/listings/submit`
- [ ] Action buttons (Edit, Submit) appear only on Draft-status listings
- [ ] Listings query invalidated after each successful mutation (create, edit, submit)
- [ ] "Create Listing" navigation from the listings page
- [ ] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [ ] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests
- [ ] Seller Vitest count grows from 20
- [ ] Live smoke against Aspire stack recorded in retro (or explicitly marked unchecked per M8-S4 precedent)
- [ ] No backend changes (zero `.cs` files touched)
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [ ] `docs/retrospectives/M9-S4b-seller-listing-management-retrospective.md` written

## Open questions

- **Duration input UX:** Should the form collect duration as separate hours/minutes fields, a single "HH:MM:SS" text input, or a dropdown with common presets (1 hour, 3 hours, 1 day, 3 days, 7 days)? Lean: dropdown with presets — simpler UX, matches the narrative's "7 days" duration for the camera listing, avoids free-form TimeSpan parsing.
- **Edit-draft dialog vs inline editing:** Should editing use a modal dialog overlaying the listing card, or inline expansion on the card itself? Lean: dialog (modal) — cleaner separation, pre-population is straightforward, consistent with form patterns.
