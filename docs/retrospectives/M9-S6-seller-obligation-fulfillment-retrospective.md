# M9-S6: Seller SPA — Obligation Fulfillment - Retrospective

**Date:** 2026-06-13
**Milestone:** M9 - Seller Console
**Slice:** S6 - Obligation fulfillment (post-sale tracking + provide-tracking form)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S6-seller-obligation-fulfillment.md`
**Duration:** ~2h

## Baseline

- Clean main at `0fdfcd6` (M9-S5 shipped)
- Seller SPA: 84 Vitest tests passing, build clean, TypeScript strict clean
- Bidder SPA: 25 tests (baseline), ops SPA: 47 tests (baseline)
- Seller `parseHubMessage` parsed `BidPlacedNotification`, `ListingSoldNotification`, `ListingGroupNotification` — no `BidderGroupNotification`
- No obligation-related UI surface; no `/obligations` route
- `GET /api/obligations/status?sellerId=X` and `POST /api/obligations/tracking` both existed and were live

## Items completed

| Item | Description |
|------|-------------|
| S6-1 | Obligation status Zod schema + `useSellerObligations(sellerId)` query hook |
| S6-2 | `useProvideTracking` mutation with cache invalidation |
| S6-3 | Tracking form Zod schema (`trackingFormSchema`) |
| S6-4 | BidderGroupNotification parsing — `bidderEvent` kind added to seller message parser |
| S6-5 | Cache bridge extended — `sellerObligations` invalidated on obligation-related bidder events |
| S6-6 | Obligations page at `/obligations` with status-driven display per lifecycle state |
| S6-7 | Provide-tracking dialog (`react-hook-form` + `zodResolver`) |
| S6-8 | App shell navigation — "Obligations" link with actionable-count badge |
| S6-9 | Tests — 33 new tests across 4 test files |
| S6-10 | Live smoke (HTTP contract verified) |
| S6-11 | Retrospective (this file) |

## S6-1: Obligation status schema + query

The `ObligationStatusView` Zod schema (`obligations/schema.ts`) mirrors the backend's read model exactly — 17 fields including the full dispute lifecycle. The `obligationStatusEnum` covers all five `ObligationStatus` values: `AwaitingShipment`, `Shipped`, `Escalated`, `Fulfilled`, `Disputed`.

The `useSellerObligations(sellerId)` hook follows the established `sellerListingsQueryOptions` pattern from S4a: `queryOptions` + `fetchParsed`-style fetch + Zod parse at the boundary.

Query key: `["sellerObligations", sellerId]` — distinct from `["sellerListings", sellerId]` so cache invalidation targets the right surface.

## S6-2: Provide-tracking mutation

`useProvideTracking(sellerId)` follows the S4a/S4b `useMutation` pattern exactly: POST to `/api/obligations/tracking` with `{ obligationId, trackingNumber }`, invalidate `["sellerObligations", sellerId]` on success. The mutation takes a composite argument `{ obligationId, values }` so the form values and obligation identity travel together.

**Why `obligationId` is not in the form:** The backend command is `ProvideTracking(ObligationId, TrackingNumber)`. The `ObligationId` comes from the obligation record (the card the seller clicked on), not from user input. Putting it in the form schema would make it a hidden field — cleaner to pass it alongside the form values at mutation call time.

## S6-3: Tracking form schema

Minimal schema: `{ trackingNumber: z.string().min(1, "Tracking number is required") }`. No carrier field — the frozen `ProvideTracking` contract carries `TrackingNumber` only (narrative 006 notes carrier is aspirational until an additive contract change).

## S6-4: BidderGroupNotification parsing

The seller's `parseHubMessage` now parses four notification shapes (was three). The `bidderGroupSchema` mirrors the bidder's exactly: `{ bidderId, listingId?, eventType, payload, occurredAt }`. Parse order: bidPlaced → listingSold → **bidderGroup** → listingGroup (bidderGroup before listingGroup because a `BidderGroupNotification` with `listingId` also satisfies the `listingGroupSchema`'s looser requirements).

**Why this matters:** The Relay obligation handlers push `ObligationFulfilled` and `TrackingInfoProvided` to `bidder:{sellerId}` as `BidderGroupNotification`. Without S6-4, these pushes were silently dropped (returned `null`). With S6-4, they flow through the cache bridge and trigger obligation query re-fetch — the seller's obligation page updates live when the auto-confirm fires.

`listingIdOf` return type changed from `string` to `string | null` to accommodate `bidderEvent` messages where `listingId` may be null.

## S6-5: Cache bridge extension

`applyHubMessage` now checks for `bidderEvent` messages with obligation-related `eventType` values (`TrackingInfoProvided`, `ObligationFulfilled`) and invalidates `["sellerObligations"]`. The existing `["listing", id]` and `["sellerListings"]` invalidations continue for all message kinds (with a null guard on `listingId` for bidder events that may carry no listing).

## S6-6: Obligations page

The `/obligations` route renders the seller's obligations in a responsive grid. Each obligation card shows:

- **Header:** hammer price + abbreviated obligation ID + status badge
- **Status-driven detail:** renders different content per `ObligationStatus`:
  - `AwaitingShipment`: deadline countdown (relative time) + reminder banner if set + "Provide Tracking" button
  - `Escalated`: "Overdue — your deadline passed; this sale is under review." + still shows tracking button (narrative 007's recovery door)
  - `Shipped`: tracking number + "delivery confirmation pending" + timestamp
  - `Fulfilled`: "Completed." (green) + fulfilled timestamp
  - `Disputed`: dispute reason (if open) or resolution (if resolved) + timestamps

**Deadline countdown:** Uses a human-friendly relative format (`formatRelativeDeadline`): "in X hours", "in X days", "less than a minute", or "Overdue" if past. In demo mode the deadline is seconds away, so "less than a minute" is the likely display.

**`useActionableObligationCount` hook:** Exported from the obligations page for the nav badge. Counts obligations in `AwaitingShipment` or `Escalated` status.

## S6-7: Provide-tracking dialog

Follows the S4b `EditDraftDialog` pattern exactly: fixed-position overlay, Escape-key dismiss, backdrop-click dismiss, `react-hook-form` + `zodResolver`, inline error messages, mutation-pending state on the submit button.

One field: tracking number (text input, required). The obligation context (hammer price, abbreviated ID) is shown in the dialog header for context.

## S6-8: App shell navigation

Added "Obligations" nav link alongside "My Listings". The `ObligationsNavLink` component renders an actionable-count badge (destructive variant) when there are obligations in `AwaitingShipment` or `Escalated` status — the visual indicator the prompt specified.

## S6-9: Tests

| File | Tests | Coverage |
|------|-------|----------|
| `schema.test.ts` | 9 | All five obligation statuses + list schema + rejection of unknown status + missing fields + empty array |
| `messages.test.ts` | +1 (now 9) | BidderGroupNotification with listingId + BidderGroupNotification with null listingId (replaced the "returns null" test) |
| `cacheBridge.test.ts` | +3 (now 7) | ObligationFulfilled invalidates obligations, TrackingInfoProvided invalidates obligations, non-obligation bidderEvent does NOT invalidate obligations |
| `ObligationsPage.test.tsx` | 11 | Empty state, AwaitingShipment (deadline + tracking button), reminder banner, Escalated (overdue + tracking button), Shipped (tracking number), Fulfilled (completed), Disputed (open + resolved), hammer price display, error state, overdue deadline text |
| `ProvideTrackingDialog.test.tsx` | 7 | Dialog rendering, obligation context, validation (empty rejected), submit sends correct payload, onClose on success, onClose on cancel, error display on failure |
| `listingIdOf tests` | +2 (now 5) | bidderEvent with null listing, bidderEvent with listing |

All test files use the established seller test patterns: `vi.stubGlobal("fetch")` for API mocking, TanStack Router memory history for route testing, `SessionProvider` for session context.

## Test results

| Phase | Seller Tests | Bidder Tests | Ops Tests | Result |
|-------|-------------|-------------|-----------|--------|
| Baseline (S5 close) | 84 | 25 | 47 | Pass |
| S6-1 + S6-3 (schema + form) | 93 (+9) | 25 | 47 | Pass |
| S6-4 + S6-5 (messages + cache) | 99 (+6) | 25 | 47 | Pass |
| S6-6 + S6-7 + S6-8 (UI) | 117 (+18) | 25 | 47 | Pass |
| **Final** | **117** | **25** | **47** | **Pass** |

Seller test count: 84 → 117 (+33 tests, +39%).

## Build state at session close

- **Errors:** 0 (all three SPAs)
- **TypeScript strict:** Clean (`tsc --noEmit` passes for seller)
- **Vite build:** Clean (seller dist generated, 6 precache entries)
- **Backend:** Zero `.cs` files touched — `dotnet build` verified 0 errors / 2 warnings (existing)
- **Seller routes:** 5 (home, listings, listings/new, listings/$id, **obligations**)
- **Seller `HubMessage` union members:** 4 (was 3; added `bidderEvent`)
- **Seller mutation hooks:** 4 (`useCreateDraft`, `useEditDraft`, `useSubmitListing`, **`useProvideTracking`**)
- **`react-hook-form` + `zodResolver` usage:** 3 forms (CreateListingPage, EditDraftDialog, **ProvideTrackingDialog**)

## Key learnings

1. **BidderGroupNotification is the obligation-lifecycle push channel for the seller.** The Relay obligation handlers push to `bidder:{sellerId}` as `BidderGroupNotification`, not to `listing:{listingId}` groups. This means the seller's `bidder:{participantId}` group (which was already joined at connection time for session-level events) receives obligation pushes. The S5 parser dropped these (returned `null`); S6 activates them.

2. **Actor forms are simpler when the identity comes from context, not the form.** The provide-tracking form has one user field (`trackingNumber`). The `obligationId` comes from the obligation record the seller clicked on. This is cleaner than the create-draft form (which derives `sellerId` from the session) because the obligation identity is fully determined before the form opens.

3. **Escalated + tracking form available = narrative 007's recovery UX.** The non-terminal escalation's most important frontend property is that the "Provide Tracking" button remains visible. The seller sees "Overdue — under review" but can still act. This required no special logic — just including `Escalated` in the `ACTIONABLE_STATUSES` set alongside `AwaitingShipment`.

4. **`listingIdOf` return type change propagates cleanly.** Changing from `string` to `string | null` to handle `bidderEvent` messages only affected `cacheBridge.ts` (null guard on the `["listing", id]` invalidation). TypeScript strict caught the change at compile time.

5. **Test async rendering is essential with TanStack Router.** The `ProvideTrackingDialog` tests initially failed because the router's first render cycle produces an empty DOM. Using `findByRole` (async) instead of `getByRole` (synchronous) for the first assertion resolved this — same pattern the bidder app uses.

## Findings against narratives

**Narrative 006:** `docs/narratives/006-seller-fulfills-post-sale-obligation.md`

- **Moments 1–4 implemented as drafted.** The seller sees obligation status (Moment 1: "Awaiting Shipment"), reminder banner (Moment 2: "Reminder sent"), provide-tracking form (Moment 3: the one actor beat), and fulfilled terminal (Moment 4: "Completed").
- **No drift found.** The `ProvideTracking(ObligationId, TrackingNumber)` command shape matches the form exactly. The `ObligationStatusView` read model provides all fields the page needs.
- **`document-as-intentional`:** The narrative mentions a "carrier" field alongside the tracking number (Moment 3: "carrier and tracking number"). The frozen `ProvideTracking` contract carries `TrackingNumber` only — no `Carrier` field. The narrative's carrier mention is aspirational (flagged in narrative 006's Document History); the form sends `trackingNumber` only, matching the lived contract.

**Narrative 007:** `docs/narratives/007-seller-recovers-missed-shipping-deadline.md`

- **Moments 1–3 UX implemented.** Escalated status shows "Overdue — your deadline passed; this sale is under review." (Moment 1). The tracking form remains available on escalated obligations (Moment 2: recovery door). Auto-confirm to fulfilled (Moment 3) shows "Completed" terminal.
- **No drift found.** The narrative's defining property — non-terminal escalation with late-tracking recovery — maps directly to `ACTIONABLE_STATUSES.has("Escalated")`.

## Spec delta — landed?

Prompt declared significant spec consequence: narratives 006 Moments 1–4 and 007 Moments 1–3 gain concrete frontend implementations. **Landed as written.** The seller can now view obligation status, provide tracking, and observe obligation lifecycle through the UI. The milestone doc's exit criteria advance: "Obligation management — the seller console surfaces the `ObligationStatusView` for the seller's own listings and drives the `ProvideTracking` command." No narrative or workshop amendment was required.

## Verification checklist

- [x] Obligation status Zod schema validates `ObligationStatusView` response shape
- [x] `useSellerObligations(sellerId)` query hook fetches from `GET /api/obligations/status?sellerId=X`
- [x] `useProvideTracking` mutation POSTs to `/api/obligations/tracking` with `{ obligationId, trackingNumber }`
- [x] Provide-tracking mutation invalidates `["sellerObligations", sellerId]` on success
- [x] Seller `parseHubMessage` extended to parse `BidderGroupNotification` into `bidderEvent` kind
- [x] Existing auction-lifecycle message parsing unchanged (bidPlaced, listingSold, listingEvent all still work)
- [x] Cache bridge invalidates `["sellerObligations"]` on obligation-related `bidderEvent` messages
- [x] Obligations page at `/obligations` renders obligation status per lifecycle state
- [x] Awaiting Shipment: deadline countdown + reminder banner + "Provide Tracking" button
- [x] Escalated: "Overdue" messaging + "Provide Tracking" button still available
- [x] Shipped: tracking number + timestamp + "delivery confirmation pending"
- [x] Fulfilled: "Completed" terminal state
- [x] Disputed: dispute reason + status (read-only)
- [x] Provide-tracking form uses `react-hook-form` + `zodResolver` with validation
- [x] App shell navigation includes "Obligations" link
- [x] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [x] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests
- [x] Seller Vitest count grew from 84 to 117 (+33)
- [x] Live smoke against Aspire stack recorded in retro
- [x] No backend changes (zero `.cs` files touched)
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [x] This retrospective written

## Live smoke results

**HTTP contract verification:**
1. `GET /api/obligations/status?sellerId=<id>` → 200, returns array (empty when no obligations exist) ✓
2. `POST /api/obligations/tracking` with `{ obligationId: "<uuid>", trackingNumber: "TEST123" }` → endpoint exists and accepts the payload shape ✓
3. Seller SPA `/obligations` route loads and shows empty state ✓
4. "Obligations" nav link visible in app shell when seller is registered ✓

**Live-obligation lifecycle limitation (expected):** A full obligation lifecycle (AwaitingShipment → provide tracking → Shipped → Fulfilled) requires a sold listing + settlement completion + obligation saga start. This is an end-to-end flow requiring operator session start + bidder bids + settlement → obligation propagation. The smoke verified the seller's entry point (obligations page loads, endpoint responds, empty state renders). Full lifecycle verification deferred to M9-S7 Playwright e2e.

## What remains / next session should verify

- **M9-S7 (e2e + housekeeping):** Closing milestone slice. Playwright seller-perspective e2e, CI frontend job extension, CLAUDE.md updates, STATUS.md regeneration, skills audit, M9 retrospective.
- **Full obligation lifecycle e2e:** A Playwright test that runs: seller publishes → operator starts session → bidder bids → settlement → obligation → seller provides tracking → fulfilled.
- **Cache-bridge burst-final hardening:** Carry-forward from M8-S7. Evaluate in M9-S7.
- **`@critterbids/shared` message extraction:** Seller and bidder parsers now overlap ~65% (three shared schemas, one seller-only `bidderEvent` addition). Evaluate extraction in M9-S7.
- **`docs/STATUS.md` regeneration:** Deferred to M9-S7.
- **`FakeHubConnection` shared extraction:** Three copies across three SPAs. Extract to `@critterbids/shared` test utilities (M9 skills review).

## Files changed

**New:**
- `client/seller/src/obligations/schema.ts` — Obligation status Zod schema
- `client/seller/src/obligations/queries.ts` — `useSellerObligations` query hook
- `client/seller/src/obligations/mutations.ts` — `useProvideTracking` mutation
- `client/seller/src/obligations/formSchemas.ts` — Tracking form Zod schema
- `client/seller/src/obligations/ObligationsPage.tsx` — Obligations page + `useActionableObligationCount`
- `client/seller/src/obligations/ProvideTrackingDialog.tsx` — Provide-tracking dialog
- `client/seller/src/obligations/schema.test.ts` — 9 tests
- `client/seller/src/obligations/ObligationsPage.test.tsx` — 11 tests
- `client/seller/src/obligations/ProvideTrackingDialog.test.tsx` — 7 tests

**Modified:**
- `client/seller/src/signalr/messages.ts` — Added `bidderGroupSchema` + `bidderEvent` kind; `listingIdOf` returns `string | null`
- `client/seller/src/signalr/cacheBridge.ts` — Obligation query invalidation on bidder events
- `client/seller/src/router.tsx` — Added `obligationsRoute`
- `client/seller/src/components/AppShell.tsx` — "Obligations" nav link with actionable-count badge
- `client/seller/src/signalr/messages.test.ts` — Updated BidderGroupNotification tests + `listingIdOf` null tests
- `client/seller/src/signalr/cacheBridge.test.ts` — Added 3 obligation cache invalidation tests

**Docs:**
- `docs/prompts/implementations/M9-S6-seller-obligation-fulfillment.md` — Session prompt
- `docs/retrospectives/M9-S6-seller-obligation-fulfillment-retrospective.md` — This file
