# M9-S6: Seller SPA — Obligation Fulfillment

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S6 of M9 (fourth frontend-heavy seller slice — post-sale obligation tracking + provide-tracking form)
**Narrative:** `docs/narratives/006-seller-fulfills-post-sale-obligation.md` (Moments 1–4 — obligation begins, reminder nudges, seller ships, auto-confirm fulfills) + `docs/narratives/007-seller-recovers-missed-shipping-deadline.md` (Moments 1–3 — deadline escalates, late tracking recovers, auto-confirm fulfills)
**Agent:** @PSA
**Estimated scope:** one PR, ~20–25 files (obligation queries, provide-tracking form, obligation status page, message parsing extension, tests, retro)

---

## Preconditions

This prompt assumes M9-S5 shipped (PR #109, `0fdfcd6`). The seller SPA has: registration, listing management (create/edit/submit), dashboard, listing detail page with live auction observation (SignalR-wired, reserve indicator, terminal outcomes). The seller's `parseHubMessage` parses `BidPlacedNotification`, `ListingSoldNotification`, and `ListingGroupNotification` — it does NOT parse `BidderGroupNotification`. The seller joins `listing:{listingId}` groups for live auction observation and `bidder:{participantId}` for session-level pushes, but obligation-lifecycle pushes (`ObligationFulfilled`, `TrackingInfoProvided`) arrive as `BidderGroupNotification` on `bidder:{sellerId}` and are currently dropped (return `null`).

## Goal

Build the seller's obligation fulfillment surface: a new `/obligations` route showing the seller's post-sale obligations (from `GET /api/obligations/status?sellerId=X`), a provide-tracking form (driving `POST /api/obligations/tracking`), ship-by deadline countdown, status-driven UI (awaiting shipment → shipped → fulfilled; escalated → shipped → fulfilled), and live updates via BidderGroupNotification parsing for obligation events. This is the seller's **first actor surface** since S4b — unlike S5's observer-protagonist, here GreyOwl12 submits the tracking form (narrative 006 Moment 3).

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | Authoritative for scope. §7 S6 row. |
| `CLAUDE.md` | Routing layer, global conventions, §Frontend. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` | **Required** — verification ladder, live-smoke rules. |
| `docs/narratives/006-seller-fulfills-post-sale-obligation.md` | Happy-path obligation lifecycle (Moments 1–4). |
| `docs/narratives/007-seller-recovers-missed-shipping-deadline.md` | Escalation → late-tracking recovery (Moments 1–3). |
| `client/seller/src/` | The S5 foundation to build on. |
| `client/seller/src/listings/CreateListingPage.tsx` | `react-hook-form` + Zod + `zodResolver` precedent for the provide-tracking form. |
| `client/seller/src/listings/mutations.ts` | TanStack Mutation + cache invalidation pattern. |
| `client/seller/src/listings/formSchemas.ts` | Zod form schema precedent. |
| `client/seller/src/signalr/messages.ts` | Current message parser (needs BidderGroupNotification extension). |
| `client/seller/src/signalr/cacheBridge.ts` | Current cache bridge (needs obligation query invalidation). |
| `client/bidder/src/signalr/messages.ts` | Bidder's `bidderGroupSchema` and `bidderEvent` kind as precedent. |
| `src/CritterBids.Obligations/GetSellerObligationsEndpoint.cs` | `GET /api/obligations/status?sellerId=X` — the query endpoint. |
| `src/CritterBids.Obligations/ProvideTrackingEndpoint.cs` | `POST /api/obligations/tracking` — the command endpoint. |
| `src/CritterBids.Obligations/ObligationStatusView.cs` | Read-model shape: `Id`, `ListingId`, `SellerId`, `WinnerId`, `HammerPrice`, `Status`, `ShipByDeadline`, `TrackingNumber`, `ReminderSentAt`, `TrackingProvidedAt`, `FulfilledAt`, `EscalatedAt`, `DisputeId`, `DisputeReason`, `DisputeOpenedAt`, `DisputeResolution`, `DisputeResolvedAt`. |
| `src/CritterBids.Obligations/ObligationStatus.cs` | Enum: `AwaitingShipment`, `Shipped`, `Escalated`, `Fulfilled`, `Disputed`. |
| `src/CritterBids.Obligations/ObligationCommands.cs` | `ProvideTracking(ObligationId, TrackingNumber)` — the command shape. |
| `src/CritterBids.Relay/Handlers/ObligationsRelayHandlers.cs` | Relay push routing: `TrackingInfoProvided` → `bidder:{WinnerId ?? SellerId}`; `ObligationFulfilled` → `bidder:{WinnerId}` + `bidder:{SellerId}`; both as `BidderGroupNotification`. |

## In scope

### S6-1: Obligation status Zod schema + query hook

Add a Zod schema for the `ObligationStatusView` response shape. The backend returns an array of obligation records, each with: `id`, `listingId`, `sellerId`, `winnerId`, `hammerPrice`, `status` (string enum), `shipByDeadline`, `trackingNumber`, `reminderSentAt`, `trackingProvidedAt`, `fulfilledAt`, `escalatedAt`, `disputeId`, `disputeReason`, `disputeOpenedAt`, `disputeResolution`, `disputeResolvedAt`. All timestamp fields are nullable ISO strings; `status` is one of `AwaitingShipment | Shipped | Escalated | Fulfilled | Disputed`.

Add a `useSellerObligations(sellerId)` query hook backed by `GET /api/obligations/status?sellerId=X`. Query key: `["sellerObligations", sellerId]`.

### S6-2: Provide-tracking mutation

Add a `useProvideTracking(sellerId)` mutation that POSTs to `/api/obligations/tracking` with `{ obligationId, trackingNumber }`. On success, invalidate `["sellerObligations", sellerId]`. Follow the S4a/S4b mutation pattern (`useMutation` + `useQueryClient` + `invalidateQueries` on success).

### S6-3: Provide-tracking form schema

Add a Zod form schema for the tracking entry form. Two fields: `trackingNumber` (required, non-empty string). The `obligationId` comes from the obligation record, not the form. Follow the `formSchemas.ts` precedent: `z.object()` with `.superRefine()` for validation messages.

### S6-4: BidderGroupNotification parsing

Extend the seller's `parseHubMessage` in `messages.ts` to parse `BidderGroupNotification`. The wire shape is `{ bidderId, listingId?, eventType, payload, occurredAt }` (same schema the bidder uses as `bidderGroupSchema`). Add a `bidderEvent` kind to the seller's `HubMessage` union. The seller cares about obligation-lifecycle eventTypes: `TrackingInfoProvided`, `ObligationFulfilled`. Other bidder-group events are accepted by the parser (unknown eventTypes are not fatal) but only the obligation-related ones drive cache invalidation.

Parse order: insert the bidderGroup parse before the listingGroup parse (same as bidder) so a `BidderGroupNotification` with `listingId` is not misidentified as a `ListingGroupNotification`.

### S6-5: Cache bridge extension

Extend `applyHubMessage` to invalidate `["sellerObligations"]` on `bidderEvent` messages whose `eventType` is `TrackingInfoProvided` or `ObligationFulfilled`. The existing listing/sellerListings invalidations continue unchanged for auction-lifecycle messages.

### S6-6: Obligations page at `/obligations`

A new route and page component. The seller navigates here from the app shell navigation. The page shows the seller's obligations in a list/grid. Each obligation card renders:

**Status-driven display:**
- **Awaiting Shipment** — "Ship your item by \<deadline\>." Ship-by deadline countdown (relative time: "in X hours" or "in X days"; past deadline: "Overdue"). Reminder banner if `reminderSentAt` is set: "Reminder sent — ship before your deadline." "Provide Tracking" button opening the tracking form.
- **Escalated** — "Overdue — your deadline passed; this sale is under review." Still shows the "Provide Tracking" button (narrative 007: the door is still open for late tracking recovery).
- **Shipped** — "Shipped — tracking #\<number\>; delivery confirmation pending." No action needed. Shows `trackingProvidedAt` timestamp.
- **Fulfilled** — "Completed." Terminal state with green treatment. Shows `fulfilledAt` timestamp.
- **Disputed** — "Dispute open (\<reason\>)." Shows `disputeOpenedAt`. If resolved: "Dispute resolved (\<resolution\>)." No action for the seller in MVP disputes.

**Common fields on every card:**
- Listing reference: hammer price (`formatUsd`), obligation ID (abbreviated)
- `ListingId` for cross-reference (link to `/listings/$id` if the listing detail page exists)

### S6-7: Provide-tracking dialog/form

A dialog or inline form rendered when the seller clicks "Provide Tracking" on an AwaitingShipment or Escalated obligation. Uses `react-hook-form` + `zodResolver` with the S6-3 form schema. Fields: tracking number (text input, required). On submit: call the `useProvideTracking` mutation with `{ obligationId, trackingNumber }`. On success: close the form, show success feedback, the cache bridge re-fetch shows the obligation as "Shipped". On error: show the error message inline.

Follow the S4a `CreateListingPage` pattern: `useForm` + `zodResolver`, `register()`, `handleSubmit()`, inline error messages via `formState.errors`.

### S6-8: App shell navigation

Add an "Obligations" nav link to the app shell so the seller can navigate between "My Listings" and "Obligations" (and Home). The obligation link should show a visual indicator (e.g. badge count) if there are obligations in actionable states (AwaitingShipment or Escalated).

### S6-9: Tests

- **Schema tests:** Validate the obligation status Zod schema against known payload shapes.
- **Mutation tests:** Validate `useProvideTracking` sends correct payload, invalidates correct query keys.
- **Message parsing tests:** Validate the extended `parseHubMessage` parses `BidderGroupNotification` into `bidderEvent` kind; verify existing auction-lifecycle parsing is unchanged.
- **Cache bridge tests:** Validate `applyHubMessage` invalidates `["sellerObligations"]` on obligation-related `bidderEvent` messages.
- **Obligations page tests:** Render with mock obligation data; verify status-driven display for each obligation status (AwaitingShipment, Escalated, Shipped, Fulfilled, Disputed); verify deadline countdown; verify reminder banner; verify "Provide Tracking" button visibility.
- **Provide-tracking form tests:** Render form; verify validation (empty tracking number rejected); verify submit calls mutation; verify success closes form.

### S6-10: Live smoke

- Run `dotnet run --project src/CritterBids.AppHost --launch-profile http`
- Open the seller SPA at `http://localhost:5175/seller/`
- Verify: obligations route loads; with no obligations, shows empty state
- Verify: the "Obligations" nav link appears in the app shell
- (Full obligation lifecycle requires a sold listing + settlement completion — record what's observable from the seller's entry point)
- Record smoke findings in the retro

### S6-11: Retrospective

- `docs/retrospectives/M9-S6-seller-obligation-fulfillment-retrospective.md`

## Explicitly out of scope

- **Backend changes.** Zero `.cs` touches. The obligation query endpoint (`GET /api/obligations/status?sellerId=X`), the provide-tracking endpoint (`POST /api/obligations/tracking`), and the Relay obligation push handlers all exist and are live. The `ObligationStatusView` projection is Inline and up-to-date.
- **Dispute actions from the seller console.** The seller sees dispute status (read-only) but cannot open or resolve disputes — that's operator-side (ops dashboard, M8-S6b).
- **Settlement summary view.** The milestone doc mentions settlement summary as a separate surface; the `ObligationStatusView` carries `HammerPrice` which is sufficient for the post-sale context. A dedicated settlement query is out of scope for S6.
- **Dashboard obligation-status enrichment.** The my-listings dashboard (S4a/S4b) shows Selling BC lifecycle only. Enriching it with obligation status (e.g. "Awaiting Shipment" on a sold listing card) would require cross-BC data joining on the dashboard — out of scope; the obligations page is the dedicated surface.
- **`@critterbids/shared` message extraction.** The seller's message parser now adds `bidderEvent`, increasing overlap with the bidder. Extraction remains deferred to M9-S7.
- **Cache-bridge burst-final hardening.** Carry-forward from M8-S7; deferred to M9-S7.
- **`docs/STATUS.md` regeneration.** Deferred to M9-S7.
- **Email/push notification for obligation reminders.** Relay is SignalR-only at MVP.

## Conventions to pin or follow

- **Frontend-slice-discipline** — all rules apply. Rule 1: read backend shapes before writing client code (done — `ObligationStatusView`, `ProvideTracking`, Relay handlers all audited). Rule 3: live smoke.
- **ADR 026 (SignalR integration pattern)** — push is a re-query signal; cache bridge invalidates queries; the obligation page re-queries on `ObligationFulfilled` push.
- **`react-hook-form` + `zodResolver` pattern** — established in S4a/S4b for create-draft and edit-draft forms; the provide-tracking form follows the same pattern (smaller surface: one field).
- **TanStack Mutation + cache invalidation** — `mutations.ts` pattern from S4a/S4b: `useMutation` + `useQueryClient` + `invalidateQueries` on success.
- **shadcn/ui components are locally owned** — copy new components as needed (e.g. dialog if used for the tracking form).

## Spec delta

Per ADR 020: this slice has **significant spec consequence**. Narrative 006 Moments 1–4 (obligation begins, reminder nudges, seller ships, auto-confirm fulfills) gain concrete frontend implementations from the seller's vantage. Narrative 007 Moments 1–3 (deadline escalates, late tracking recovers, auto-confirm fulfills) gain UX surface: the Escalated status shows "Overdue — under review" with the tracking form still available. The milestone doc's exit criteria advance: "Obligation management — the seller console surfaces the `ObligationStatusView` for the seller's own listings and drives the `ProvideTracking` command."

## Acceptance criteria

- [ ] Obligation status Zod schema validates `ObligationStatusView` response shape
- [ ] `useSellerObligations(sellerId)` query hook fetches from `GET /api/obligations/status?sellerId=X`
- [ ] `useProvideTracking` mutation POSTs to `/api/obligations/tracking` with `{ obligationId, trackingNumber }`
- [ ] Provide-tracking mutation invalidates `["sellerObligations", sellerId]` on success
- [ ] Seller `parseHubMessage` extended to parse `BidderGroupNotification` into `bidderEvent` kind
- [ ] Existing auction-lifecycle message parsing unchanged (bidPlaced, listingSold, listingEvent all still work)
- [ ] Cache bridge invalidates `["sellerObligations"]` on obligation-related `bidderEvent` messages
- [ ] Obligations page at `/obligations` renders obligation status per lifecycle state
- [ ] Awaiting Shipment: deadline countdown + reminder banner + "Provide Tracking" button
- [ ] Escalated: "Overdue" messaging + "Provide Tracking" button still available
- [ ] Shipped: tracking number + timestamp + "delivery confirmation pending"
- [ ] Fulfilled: "Completed" terminal state
- [ ] Disputed: dispute reason + status (read-only)
- [ ] Provide-tracking form uses `react-hook-form` + `zodResolver` with validation
- [ ] App shell navigation includes "Obligations" link
- [ ] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [ ] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests
- [ ] Seller Vitest count grows from 84
- [ ] Live smoke against Aspire stack recorded in retro
- [ ] No backend changes (zero `.cs` files touched)
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [ ] `docs/retrospectives/M9-S6-seller-obligation-fulfillment-retrospective.md` written

## Open questions

- **Obligations page layout.** Should obligations be a full-page list/grid at `/obligations`, or a section within an expanded listing detail page? Lean: dedicated `/obligations` route — the obligation lifecycle is distinct from the auction lifecycle; mixing them on the detail page overloads the seller's vantage. The dashboard links to `/listings/$id` for auctions and `/obligations` for post-sale tracking.
- **Provide-tracking form placement.** Inline expansion on the obligation card, or a dialog/modal? Lean: dialog — follows the S4b `EditDraftDialog` precedent; keeps the obligation list stable while the form is open. If the form is simple enough (one field), inline expansion is also acceptable.
- **Deadline countdown granularity.** Should the ship-by countdown show real-time seconds ticking, or a human-friendly relative format ("in 2 hours", "in 3 days")? Lean: human-friendly relative format with periodic refresh (the page already re-queries on cache invalidation; the countdown is informational, not real-time critical). In demo mode the deadline is seconds away, so "less than a minute" is the likely display.
