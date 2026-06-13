# M9-S4b: Seller SPA — Listing Management Write Operations — Retrospective

**Date:** 2026-06-13
**Milestone:** M9 — Seller Console
**Slice:** S4b — Listing management write operations (create-draft, edit-draft, submit-for-publication)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S4b-seller-listing-management.md`

## Baseline

- **.NET build:** 0 errors, 2 CS0108 warnings (saga Version hiding — baseline since M8-S7)
- **.NET tests:** 326 all pass (10 projects) — untouched this slice (zero `.cs` changes)
- **Frontend Vitest:** 92 (bidder 25, ops 47, seller 20)
- **Playwright e2e:** 1 (local-only, two-bidder bid-war)
- **npm workspace members:** 5 (bidder, ops, seller, shared, e2e)

## Items completed

| Item | Description |
|------|-------------|
| S4b-1 | `react-hook-form` + `@hookform/resolvers` installed in `@critterbids/seller` — first consumer in the workspace |
| S4b-2 | shadcn/ui components added — Input, Label, Select, Checkbox (copied from bidder/custom) |
| S4b-3 | Create-draft form at `/listings/new` — 10 fields, Zod validation, Flash/Timed conditional visibility, extended-bidding toggle |
| S4b-4 | Edit-draft dialog on Draft-status listings — 3 mutable fields (title, reservePrice, buyItNowPrice), modal overlay |
| S4b-5 | Submit-for-publication button on Draft-status listings — calls `POST /api/selling/listings/submit` |
| S4b-6 | Mutation hooks — `useCreateDraft`, `useEditDraft`, `useSubmitListing` with TanStack Query invalidation |
| S4b-7 | Tests — 35 new tests: 17 form schema, 7 create-listing page, 4 mutation payload, 7 listings page actions |
| S4b-8 | Retrospective — this file |

## S4b-1: react-hook-form + @hookform/resolvers

First form library in the CritterBids workspace. `react-hook-form` v7+ with `@hookform/resolvers/zod` for schema validation. Three packages added to `@critterbids/seller` only — the bidder and ops apps have no form surfaces that need it.

## S4b-3: Create-draft form — the form-to-wire-contract boundary

The most significant design decision in this slice was the form value typing strategy. The Zod schema validates **string-typed** form values (matching what HTML inputs produce), and the mutation hook's `toCreateDraftPayload` function converts to the backend's typed JSON format at the mutation boundary.

**Why not `z.coerce.number()`?** The `@hookform/resolvers/zod` resolver compares Zod schema input and output types against `useForm`'s generic parameter. `z.coerce.number()` creates an `unknown` input type that doesn't match the output `number` — a TypeScript error at `resolver` assignment. The string-typed approach avoids this entirely.

### Conditional field visibility

Two conditional groups driven by `useForm.watch()`:
1. **Duration field:** Required and visible for Timed format; hidden for Flash (matches narrative 004: keyboard is Flash with null duration, camera is Timed with 7-day duration)
2. **Extended-bidding fields:** Visible when `extendedBiddingEnabled` checkbox is checked; hidden otherwise

`shouldUnregister: true` ensures hidden fields don't carry stale values — when a user switches from Timed to Flash, the duration value is automatically unregistered.

### Duration presets

The create-draft form uses a **dropdown with presets** (1 hour, 3 hours, 12 hours, 1 day, 3 days, 7 days) rather than free-form TimeSpan input. This matches narrative 004's camera listing ("7 days") and avoids ISO 8601 duration parsing UX complexity. The presets use .NET's `TimeSpan` string format (e.g. `"7.00:00:00"` for 7 days).

## S4b-4: Edit-draft dialog

A modal dialog overlaying the listing card. Only Draft-status listings show the Edit button. Three mutable fields are pre-populated from the listing's current values — `title`, `reservePrice`, `buyItNowPrice`. Format, startingBid, duration, and extended-bidding fields are immutable after creation (per the `UpdateDraftListing` command shape which only accepts those three fields).

## S4b-5: Submit-for-publication

Direct action button — no confirmation dialog. The backend's auto-approval chain (ListingSubmitted → ListingApproved → ListingPublished) means the listing transitions to Published immediately. The listings query is invalidated on success, so the dashboard re-fetches and shows the updated status.

## Test results

| Phase | Seller Tests | Result |
|-------|-------------|--------|
| Baseline | 20 | Pass |
| After form schema tests | 37 | Pass |
| After create-listing page tests | 44 | Pass |
| After mutation payload tests | 48 | Pass |
| After listings page action tests | 55 | Pass |
| Final (all workspaces) | bidder 25 + ops 47 + seller 55 = 127 | Pass |

## Build state at session close

- **Build errors:** 0 (seller `tsc --noEmit` + `vite build` clean)
- **Build warnings:** 2 SignalR `INVALID_ANNOTATION` rolldown warnings (third-party, existing baseline)
- **Backend .NET build:** untouched (zero `.cs` changes) — 0 errors, 2 CS0108 warnings (baseline)
- **Backend .NET tests:** untouched (326 pass, 10 projects)
- **Seller Vitest:** 55 (was 20; +35 new)
- **Bidder/ops Vitest:** 25/47 (unchanged)

Negative-space assertions:
- `.cs` files modified: 0
- Backend test files modified: 0
- `@critterbids/shared` files modified: 0
- Bidder/ops source files modified: 0

## Key learnings

1. **`z.coerce.number()` + `@hookform/resolvers/zod` type mismatch is a known pattern.** Zod's coerce transforms change the input type to `unknown`, which doesn't match react-hook-form's form value type. The solution is string-typed schemas with manual conversion at the mutation boundary. This is actually a cleaner separation of concerns — the schema owns form validation, the mutation owns wire-contract conformance.

2. **`shouldUnregister: true` is the right call for conditional fields.** Without it, switching from Timed to Flash would leave a stale duration value in the form state that passes through to the mutation. With it, hidden fields are automatically cleared, and the mutation function nullifies Flash duration regardless (belt-and-suspenders).

3. **Duration presets vs free-form input.** Free-form TimeSpan parsing would require educating users on ISO 8601 duration format (`.NET's "d.HH:MM:SS"`, not ISO 8601's `"P7D"`). Presets eliminate this entirely and cover the realistic auction duration range. The narrative's "7 days" maps directly to the `"7.00:00:00"` preset value.

4. **Mutation payload tests verify the wire contract independently of jsdom.** The `toCreateDraftPayload` and `toEditDraftPayload` functions are pure transformations from form values to API payloads — testable without DOM rendering. These tests verify the contract the live smoke will exercise: string-to-number conversion, null vs empty string handling, conditional field nullification.

## Findings against narrative

Narrative 004 Moments 2–4 implemented faithfully:
- **Moment 2** (GreyOwl12 drafts the keyboard): the create-draft form covers all 10 listing-time fields, including Flash format (null duration), extended bidding enabled (30s trigger, 15s extension)
- **Moment 3** (keyboard published): the Submit for Publication button triggers the auto-approval chain
- **Moment 4** (camera published): the Timed format with 7-day duration is supported via the duration presets dropdown

No `narrative-update`, `workshop-update`, `code-update`, or `document-as-intentional` findings.

## Spec delta — landed?

Prompt declared moderate spec consequence: narrative 004 Moments 2–4 gain frontend implementations. The seller can now perform the full listing-creation-through-publication flow through the UI — create a Flash or Timed draft with all listing-time fields, edit the mutable fields, and submit for auto-approval publication. The milestone doc's exit criteria advance: the listing management write surface is complete.

## Verification checklist

- [x] `react-hook-form` and `@hookform/resolvers` installed in `@critterbids/seller`
- [x] shadcn/ui Input, Label, Select, and Checkbox components present in `client/seller/src/components/ui/`
- [x] Create-draft form at `/listings/new` with all 10 fields
- [x] Duration field: required + visible for Timed, hidden for Flash
- [x] Extended-bidding fields: visible when checkbox checked, hidden otherwise
- [x] Zod validation: title required, startingBid positive, BIN >= reserve, conditional duration/extended-bidding
- [x] Create-draft mutation calls `POST /api/listings/draft` and navigates to `/listings` on success
- [x] Edit-draft on Draft-status listings: shows 3 mutable fields, calls `PUT /api/selling/listings/draft`
- [x] Submit-for-publication on Draft-status listings: calls `POST /api/selling/listings/submit`
- [x] Action buttons (Edit, Submit) appear only on Draft-status listings
- [x] Listings query invalidated after each successful mutation (create, edit, submit)
- [x] "Create Listing" navigation from the listings page
- [x] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [x] Existing frontend baselines preserved: bidder 25, ops 47
- [x] Seller Vitest count grown from 20 to 55
- [x] Live smoke against Aspire stack — **HTTP smoke complete**: session → register → create Flash draft (keyboard) → query (1 Draft) → edit (title+reserve) → verify edit → submit → verify Published → create Timed draft (camera, 7-day duration) → submit camera → verify (2 Published). All three write endpoints exercised with both Flash and Timed formats. Browser smoke unchecked (Chrome not installed, admin-install required; per M8-S4 precedent)
- [x] No backend changes (zero `.cs` files touched)
- [x] No commit to `main`; feature branch `m9-s4b-seller-listing-management`; no `Co-Authored-By` trailer
- [x] This retrospective written with `**Prompt:**` header and `## Spec delta -- landed?` paragraph

## What remains / next session should verify

### In scope for M9, deferred to later slices

- **Live auction observation:** M9-S5 — BiddingHub connection, real-time bid feed on seller's listings
- **Obligation fulfillment:** M9-S6 — obligation tracker, provide-tracking form
- **Browser smoke:** Blocked by Chrome admin-install; verify when available (M9-S7 e2e slice)
- **STATUS.md regeneration:** M9-S7

### Out of scope, tracked elsewhere

- WithdrawListing from seller console: requires seller-facing (anonymous) withdraw endpoint — out of M9 scope (staff action at MVP per ADR-024)
- Cache-bridge burst-final hardening (deferred from M9-S3 to M9-S5/S7)
- Extended-bidding e2e banner assert (M9-S7)

---

## Document History

- **v0.1** (2026-06-13): Initial authoring. Create-draft form (10 fields, conditional visibility, Zod validation, react-hook-form), edit-draft dialog (3 mutable fields, modal), submit-for-publication button. 35 new tests. Zero backend changes. react-hook-form first consumer in workspace. String-typed schema approach with mutation-boundary conversion established as pattern for future forms.
