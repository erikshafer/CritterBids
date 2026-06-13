# M9-S5: Seller SPA — Live Auction Observation

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S5 of M9 (third frontend-heavy seller slice — real-time auction observation)
**Narrative:** `docs/narratives/005-seller-watches-flash-auction-close.md` (Moments 1–4 — bidding opens, reserve crosses, close extends, gavel falls)
**Agent:** @PSA
**Estimated scope:** one PR, ~20–25 files (SignalR wiring, listing detail page, live auction components, tests, retro)

---

## Preconditions

This prompt assumes M9-S4b shipped (PR #108, `a8a886c`). The seller SPA has the complete listing management surface: registration, dashboard, create-draft, edit-draft, submit-for-publication. The seller's `SellerSignalRProvider` connects to the BiddingHub and joins the `bidder:{participantId}` group, but its `parseMessage` returns `null` — no messages are parsed or applied. M9-S5 activates the SignalR channel: message parsing, cache bridge, listing-group joins, and the live auction observation UI.

## Goal

Wire the seller's BiddingHub SignalR connection to parse auction-lifecycle messages and build a listing detail page at `/listings/:id` that renders live auction observation from the seller's vantage. The seller sees real-time bid activity, a confidential reserve-crossing indicator (comparing `currentHighBid` from the `CatalogListingView` against `reservePrice` from the Selling BC's `SellerListingSummary`), extended-bidding status, close time, and gavel-fall. This is the observer-protagonist experience narrative 005 dramatises — GreyOwl12 watches but does not act.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | Authoritative for scope. §7 S5 row. |
| `CLAUDE.md` | Routing layer, global conventions, §Frontend. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` | **Required** — verification ladder, live-smoke rules. |
| `.claude/skills/signalr/SKILL.md` | **Required** — push + re-query pattern, cache bridge, provider wiring. |
| `docs/narratives/005-seller-watches-flash-auction-close.md` | The story this slice renders (observer-protagonist). |
| `client/seller/src/` | The S4a+S4b foundation to build on. |
| `client/bidder/src/signalr/` | Bidder's SignalR integration as precedent (messages, cacheBridge, hooks, SignalRProvider). |
| `client/bidder/src/bidding/LiveBidding.tsx` | Live-bidding component precedent (derived affordances: outbid, extended, gavel). |
| `client/bidder/src/bidding/LiveActivity.tsx` | Live activity feed precedent (transient `useListen` ticker). |
| `client/bidder/src/catalog/ListingDetailPage.tsx` | Detail page + `useWatchListing` precedent. |
| `client/shared/src/signalr/provider.tsx` | The `createSignalRProvider<TMessage>()` factory. |
| `client/shared/src/schemas/catalog.ts` | `CatalogListingView` Zod schema (live auction data shape). |
| `src/CritterBids.Relay/Handlers/AuctionsBiddingHandlers.cs` | Relay push handlers — what messages reach `listing:{listingId}` groups. |
| `src/CritterBids.Relay/Handlers/BidPlacedHandler.cs` | `BidPlacedNotification` shape. |
| `src/CritterBids.Relay/Handlers/ListingSoldHandler.cs` | `ListingSoldNotification` shape. |
| `src/CritterBids.Listings/Features/Catalog/CatalogEndpoints.cs` | `GET /api/listings/{id}` endpoint. |

## In scope

### S5-1: Seller SignalR message parsing

Wire the seller's `parseMessage` to actually parse BiddingHub payloads. The seller receives the same message types as the bidder (both connect to the BiddingHub, both join `listing:{listingId}` groups). Reuse the bidder's message discrimination approach: Zod schemas for each notification shape, structural discrimination (no uniform `type` discriminator on the wire — M8-S3b finding), normalized into a discriminated `HubMessage` union via a `kind` field assigned at parse time.

The seller cares about:
- `BidPlacedNotification` (bid feed)
- `ListingSoldNotification` (gavel fall)
- `ListingGroupNotification` with `eventType`: `BiddingOpened`, `ReserveMet`, `ExtendedBiddingTriggered`, `BiddingClosed`, `ListingPassed`, `ListingWithdrawn`, `BuyItNowPurchased`

The seller does NOT care about `SettlementCompletedNotification` or `BidderGroupNotification` (those are bidder-side). The parse function can be a copy-and-narrow of the bidder's `parseHubMessage`, or a shared utility extracted to `@critterbids/shared` if the overlap is clean enough (lean: start seller-local, evaluate extraction).

### S5-2: Seller cache bridge

A `applyHubMessage` function that translates parsed hub messages into TanStack Query cache invalidations. On any listing-related push:
- Invalidate `["listing", listingId]` — the detail view re-fetches the `CatalogListingView`
- Invalidate `["sellerListings", sellerId]` — the dashboard re-fetches so status changes propagate

This follows the bidder's cache bridge pattern (ADR 026): a push is a re-query signal, never authoritative data.

### S5-3: Seller listing-group joins

Update `SellerSignalRProvider` to join `listing:{listingId}` groups for the seller's published/active listings. Two approaches:
- **Per-page join:** The listing detail page joins the specific listing's group on mount (like the bidder's `useWatchListing`)
- **Dashboard-level join:** The dashboard joins all the seller's active listings' groups so the dashboard cards update live

Lean: per-page join for the detail page (primary live surface); the dashboard gets updates via the cache bridge invalidating `sellerListings`. Dashboard-level multi-listing joins are a potential enhancement but not required for narrative 005's single-listing observation story.

### S5-4: Listing detail query

Add a `useListing(id)` query hook to the seller app that fetches `GET /api/listings/{id}` and parses the response with the shared `catalogListingSchema` from `@critterbids/shared/schemas`. This gives the seller the live auction data: `currentHighBid`, `bidCount`, `scheduledCloseAt`, `status`, `hammerPrice`, `winnerId`, etc.

### S5-5: Listing detail page at `/listings/:id`

A new route and page component. The seller navigates here from the dashboard by clicking on a listing card. The page shows:

**Static section** (from `CatalogListingView`):
- Title, format, starting bid, buy-it-now (if set)

**Live auction panel** (seller-perspective, from `CatalogListingView` — re-queried on hub pushes):
- Current high bid + bid count
- Close time (from `scheduledCloseAt`)
- **Reserve indicator** — the seller's confidential view: compare `currentHighBid` against `reservePrice` (from the `SellerListingSummary` in the seller listings query cache, or passed via route state). Show "Reserve not met" / "Reserve met" with appropriate visual treatment. This is the cross-BC join the seller sees that no other consumer can.
- **Extended bidding banner** — derived from `scheduledCloseAt` moving later (same pattern as bidder's `LiveBidding`)
- **Terminal outcome** — Sold (hammer price, winner), Passed (reason), Withdrawn
- **No bid panel** — the seller is an observer, not an actor. Narrative 005 explicitly notes "he has no commands to send."

**Live activity feed** (transient `useListen` ticker, same pattern as bidder's `LiveActivity`):
- New bid notifications, reserve-met, extended-bidding-triggered, gavel-fall
- Capped, deduped, most-recent-first (same as bidder)

### S5-6: Dashboard → detail navigation

Add a "View" or card-click action on the listings dashboard that navigates to `/listings/:id` for listings that are Published or in an auction lifecycle state. Pass the `reservePrice` from the `SellerListingSummary` as route search params so the detail page can render the reserve indicator without a separate query to the Selling BC.

### S5-7: Tests

- **Message parsing tests:** Validate the seller's `parseHubMessage` against known payload shapes.
- **Cache bridge tests:** Validate `applyHubMessage` invalidates the correct query keys.
- **Listing detail page tests:** Render with mock listing data; verify static fields, live auction panel, reserve indicator, terminal outcomes.
- **Live activity tests:** Verify transient feed renders bid-placed, listing-event messages.
- **Dashboard navigation tests:** Verify "View" action navigates to `/listings/:id`.

### S5-8: Live smoke

- Run `dotnet run --project src/CritterBids.AppHost --launch-profile http`
- Open the seller SPA at `http://localhost:5175/seller/`
- Verify: register → create Flash draft → submit → navigate to listing detail → verify static fields + "Bidding hasn't opened" state
- (Full live-observation smoke requires an operator to start a session and bidders to bid — record what's observable)
- Record smoke findings in the retro

### S5-9: Retrospective

- `docs/retrospectives/M9-S5-seller-live-auction-observation-retrospective.md`

## Explicitly out of scope

- **Backend changes.** Zero `.cs` touches. All endpoints and SignalR handlers exist. The CatalogListingView is fully projected; the BiddingHub broadcasts to listing groups.
- **Obligation tracking.** M9-S6 scope (post-sale status, provide-tracking form).
- **Dashboard auction-status enrichment.** The seller dashboard shows Selling BC status (Draft/Published/Withdrawn). Showing "Open"/"Sold" on the dashboard would require either cross-BC projection or a dual query — out of scope for this observation slice. The detail page is the live surface.
- **Withdraw action from seller console.** Staff-only per ADR-024.
- **Cache-bridge burst-final hardening.** Carry-forward from M8-S7; evaluate in M9-S7.
- **`@critterbids/shared` message extraction.** The bidder and seller message parsers may overlap, but extracting a shared parse surface is premature until we confirm the overlap is stable. Start seller-local; evaluate extraction in M9-S7.
- **`docs/STATUS.md` regeneration.** Deferred to M9-S7.

## Conventions to pin or follow

- **Frontend-slice-discipline** — all rules apply. Rule 1: read backend shapes before writing client code (done). Rule 3: live smoke.
- **ADR 026 (SignalR integration pattern)** — push is a re-query signal; cache bridge invalidates queries; `useListen` for transient affordances only.
- **Shared schemas** — use `catalogListingSchema` from `@critterbids/shared/schemas` for the listing detail query (the seller and bidder consume the same read model).
- **Observer-protagonist** — the seller has no action controls on the live auction page. No bid panel, no mutation hooks in the live observation surface.
- **shadcn/ui components are locally owned** — copy new components as needed.

## Spec delta

Per ADR 020: this slice has **significant spec consequence**. Narrative 005 Moments 1–4 (keyboard goes live, reserve crosses, close extends, gavel falls) gain concrete frontend implementations from the seller's vantage. The seller can now observe live auctions on their own listings through the UI — the observer-protagonist experience the narrative dramatises. The milestone doc's exit criteria advance: "Live auction observation — the seller console connects to BiddingHub and shows real-time bid activity, reserve crossing, extended-bidding status, and gavel-fall for the seller's own listings."

## Acceptance criteria

- [ ] Seller `parseMessage` parses `BidPlacedNotification`, `ListingSoldNotification`, and `ListingGroupNotification` into a discriminated union
- [ ] Seller cache bridge invalidates `["listing", id]` and `["sellerListings", sellerId]` on listing-related pushes
- [ ] `SellerSignalRProvider` wires `parseMessage` and `applyMessage` (replacing the `null` stubs)
- [ ] Listing detail page at `/listings/:id` renders CatalogListingView data
- [ ] Reserve indicator compares `currentHighBid` against seller's `reservePrice`
- [ ] Extended-bidding banner derived from `scheduledCloseAt` movement
- [ ] Terminal outcome shows Sold (hammer price) / Passed / Withdrawn
- [ ] No bid panel on the seller's auction view
- [ ] Live activity feed with capped, deduped, most-recent-first entries
- [ ] Listing detail page joins `listing:{listingId}` group on mount
- [ ] Dashboard listings navigate to `/listings/:id`
- [ ] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [ ] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests
- [ ] Seller Vitest count grows from 55
- [ ] Live smoke against Aspire stack recorded in retro
- [ ] No backend changes (zero `.cs` files touched)
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [ ] `docs/retrospectives/M9-S5-seller-live-auction-observation-retrospective.md` written

## Open questions

- **Reserve price source on the detail page.** The `CatalogListingView` does not carry `reservePrice` (it's confidential). The `SellerListingSummary` has it. Options: (a) pass via route search param from the dashboard; (b) read from the TanStack Query cache for `["sellerListings", sellerId]`; (c) add a dedicated seller-listing-detail query. Lean: (a) route search param — simplest, no extra query, the dashboard always has the data.
- **Message parser duplication vs shared extraction.** The seller's `parseHubMessage` will overlap significantly with the bidder's. Extract to `@critterbids/shared` now, or start local and evaluate? Lean: start local — the seller may not care about all bidder message types (no `SettlementCompleted`, no `BidderGroupNotification`); the overlap should stabilize before extraction.
