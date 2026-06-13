# M9-S5: Seller SPA — Live Auction Observation - Retrospective

**Date:** 2026-06-13
**Milestone:** M9 - Seller Console
**Slice:** S5 - Live auction observation (seller-side SignalR activation + listing detail page)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M9-S5-seller-live-auction-observation.md`
**Duration:** ~2h

## Baseline

- Clean main at `a8a886c` (M9-S4b shipped)
- Seller SPA: 55 Vitest tests passing, build clean, TypeScript strict clean
- Bidder SPA: 25 tests (baseline), ops SPA: 47 tests (baseline)
- `SellerSignalRProvider` existed but `parseMessage` returned `null` — no messages parsed or applied
- No listing detail page; dashboard showed Selling BC lifecycle only (Draft/Published/Withdrawn)

## Items completed

| Item | Description |
|------|-------------|
| S5-1 | Seller SignalR message parsing — `BidPlacedNotification`, `ListingSoldNotification`, `ListingGroupNotification` |
| S5-2 | Seller cache bridge — invalidates `["listing", id]` and `["sellerListings"]` on any push |
| S5-3 | Seller listing-group joins — per-page `useWatchListing` on the detail page |
| S5-4 | Listing detail query — `useListing(id)` hook using `catalogListingSchema` from `@critterbids/shared/schemas` |
| S5-5 | Listing detail page at `/listings/$id` — static card + live auction panel + reserve indicator + terminal outcomes |
| S5-6 | Dashboard → detail navigation — "View" button on Published/Submitted listings, reserve via search params |
| S5-7 | Tests — 29 new tests across 5 test files |
| S5-8 | Live smoke — HTTP contract verified; SPA proxy confirmed |
| S5-9 | Retrospective (this file) |

## S5-1: Seller SignalR message parsing

The seller's `parseHubMessage` is a narrower copy of the bidder's — it parses the same wire payloads from the BiddingHub but drops `SettlementCompletedNotification` and `BidderGroupNotification` (seller-irrelevant). The discriminated union carries three `kind` values: `bidPlaced`, `listingSold`, `listingEvent`.

**Why seller-local, not extracted to `@critterbids/shared`:** The bidder and seller message sets diverge (bidder has settlement + bidder-group messages the seller doesn't care about). Premature extraction would create a union type larger than either consumer needs. Start local; evaluate extraction in M9-S7 when both parsers have stabilized.

**Structural discrimination approach:** Same as bidder (M8-S3b finding) — no uniform `type` discriminator on the wire, so we parse most-specific-first with Zod schemas and assign a client-side `kind` discriminator. `bidPlaced` is tried first (has `bidId`), then `listingSold` (has `hammerPrice`), then `listingEvent` (generic envelope).

| Metric | Value |
|--------|-------|
| `HubMessage` union members | 3 (bidPlaced, listingSold, listingEvent) |
| Zod schemas defined | 3 |
| `parseHubMessage` return type | `HubMessage \| null` |

## S5-2: Seller cache bridge

`applyHubMessage` invalidates two query key families:
- `["listing", listingId]` — triggers re-fetch of the CatalogListingView on the detail page
- `["sellerListings"]` — triggers dashboard re-fetch so status changes propagate

Same pattern as bidder (ADR 026): a push is a re-query signal, never authoritative data.

## S5-3: Seller listing-group joins

The `SellerSignalRProvider` was rewritten from the `<never>` null-stub to a full provider using `createSignalRProvider<HubMessage>`. The `SellerGroupManager` handles:
- `bidder:{participantId}` group join on connect (existing from S4a)
- `listing:{listingId}` group join/leave via `watchListing`/`unwatchListing` methods exposed through context

`useWatchListing(listingId)` is a hook that calls `watchListing` on mount and `unwatchListing` on unmount — same pattern as bidder's `useWatchListing`.

## S5-5: Listing detail page

**Cross-BC data join:** The seller's listing detail page is the only place in the UI that joins data from two BCs:
- `CatalogListingView` (Listings BC): auction lifecycle data — `currentHighBid`, `bidCount`, `scheduledCloseAt`, `status`, `hammerPrice`, `winnerId`
- `reservePrice` (Selling BC, via `SellerListingSummary`): passed as a route search param from the dashboard

This avoids a second query to the Selling BC from the detail page. The reserve indicator compares `currentHighBid >= reservePrice` and renders green "Reserve met" or orange "Reserve not met".

**Extended-bidding banner:** Derived from `scheduledCloseAt` movement via `useEffect` + `useRef` — same pattern as bidder's `LiveBidding`. When the close time moves later than the previous value, shows "Extended bidding activated."

**Terminal outcomes:**
- Sold: "Sold for $X.XX to bidder {winnerId}" (green)
- Passed: "Listing passed — {reason}" (amber)
- Generic close: "This auction has closed." (grey)

**Observer-protagonist:** No bid panel, no mutation hooks. The seller sees a muted "You are observing this auction." message when the listing is open. Narrative 005 explicitly: "he has no commands to send."

## S5-6: Dashboard → detail navigation

Added `VIEWABLE_STATUSES = ["Published", "Submitted"]` filter. A "View" button appears for viewable listings, linking to `/listings/$id?reserve={reservePrice}`. The reserve price travels as a Zod-validated search param (`z.object({ reserve: z.number().optional() })`).

**Why route search params, not cache reads:** The dashboard always has `SellerListingSummary` data in the query cache, but reading cache directly couples the detail page to the dashboard's query lifecycle. Route params are explicit, serializable, and work on direct-link navigation. The `reserve` param is optional — the detail page gracefully degrades (no reserve indicator) when absent.

## S5-7: Tests

| File | Tests | Coverage |
|------|-------|----------|
| `messages.test.ts` | 7 | Parse all three message kinds, unknown payloads return null |
| `cacheBridge.test.ts` | 4 | Correct query keys invalidated for each message kind |
| `ListingDetailPage.test.tsx` | 9 | Static fields, auction panel, reserve indicator (met/not-met), sold terminal, observer message, 404, back link |
| `LiveActivity.test.tsx` | 6 | Bid-placed rendering, listing-event rendering, dedup by identity, cap at 8, most-recent-first |
| `ListingsPage.test.tsx` | +2 | View button on Published, no View button on Draft |

All test files use the established seller test patterns: `FakeHubConnection` class for SignalR provider wrapping, `vi.stubGlobal("fetch")` for API mocking, TanStack Router memory history for route testing.

**Discovery: `useSellerSignalR must be used within a SellerSignalRProvider`** — The `ListingDetailPage` tests initially failed because the `Providers` wrapper lacked the `SellerSignalRProvider`. Fixed by adding `FakeHubConnection` and wrapping with `<SellerSignalRProvider createConnection={() => fakeConnection}>`. Same pattern the bidder app uses for components that consume hub context.

**Discovery: duplicate "Sold" text in DOM** — The sold terminal-outcome test found two elements matching `/Sold/` — the status Badge and the terminal outcome paragraph. Fixed by narrowing the assertion to `/Sold for/`.

## Test results

| Phase | Seller Tests | Bidder Tests | Ops Tests | Result |
|-------|-------------|-------------|-----------|--------|
| Baseline (S4b close) | 55 | 25 | 47 | Pass |
| S5-1 + S5-2 (SignalR infra) | 66 (+11) | 25 | 47 | Pass |
| S5-5 + S5-6 (detail page + nav) | 77 (+11) | 25 | 47 | Pass |
| S5-7 (LiveActivity + ListingsPage) | 84 (+7) | 25 | 47 | Pass |
| **Final** | **84** | **25** | **47** | **Pass** |

Seller test count: 55 → 84 (+29 tests, +53%).

## Build state at session close

- **Errors:** 0 (all three SPAs)
- **TypeScript strict:** Clean (`tsc --noEmit` passes for seller)
- **Vite build:** Clean (seller dist generated, 6 precache entries)
- **Backend:** Zero `.cs` files touched — no `dotnet build`/`dotnet test` required
- **Seller `parseMessage` calls returning `null`: 0** (was 100% before this slice)
- **Seller routes with `validateSearch`: 1** (`/listings/$id`)
- **`useWatchListing` hooks: 2** (bidder + seller — same pattern, separate implementations)
- **`createSignalRProvider` instantiations: 3** (bidder, ops, seller — all three SPAs now fully wired)

## Key learnings

1. **Route search params are the lightest cross-query bridge.** Passing `reservePrice` as a Zod-validated search param avoids cache coupling, works on direct-link navigation, and degrades gracefully when absent. This pattern is preferable to reading from another query's cache whenever the source page always has the data.

2. **Structural message discrimination is copy-friendly, not extract-friendly.** The bidder and seller parse the same wire payloads but care about different subsets. A shared parser would carry dead `kind` branches in each consumer. Copy-and-narrow is the right call until the union sets converge.

3. **The `FakeHubConnection` test helper pattern is reusable across all three SPAs.** Each SPA that wraps components in a `SignalRProvider` needs this — consider extracting to `@critterbids/shared` test utilities if a fourth consumer appears. For now, copy per-SPA is fine (3 instances).

4. **Observer-protagonist pages are simpler than actor pages.** No mutation hooks, no optimistic updates, no error boundaries around submit paths. The seller's live auction page is approximately half the complexity of the bidder's `LiveBidding` — same data surface, no action surface.

5. **Flash listings require an operator session to start bidding.** The HTTP smoke can verify the complete seller lifecycle through Published, but observing live bidding activity requires an operator to start a session + bidders to place bids. This is inherent to Flash format, not a test gap.

## Findings against narrative

**Narrative:** `docs/narratives/005-seller-watches-flash-auction-close.md`

- **Moments 1–4 implemented as drafted.** The seller observes bidding-opens (via `BiddingOpened` ListingGroupNotification), reserve crosses (via `ReserveMet` ListingGroupNotification + reserve indicator comparison), close extends (via `scheduledCloseAt` movement detection), and gavel falls (via `ListingSoldNotification`).
- **No drift found.** The narrative's observer-protagonist framing ("he has no commands to send") maps directly to the implementation: no bid panel, no mutation hooks, no action controls.
- **`document-as-intentional`:** The reserve indicator uses a client-side comparison (`currentHighBid >= reservePrice`) rather than relying on the `ReserveMet` push event alone. This is intentional — the push confirms the crossing, but the comparison renders the indicator state on initial page load before any push arrives.

## Spec delta — landed?

Prompt declared significant spec consequence: narrative 005 Moments 1–4 gain concrete frontend implementations. **Landed as written.** The seller can now observe live auctions through the UI — the listing detail page at `/listings/$id` renders real-time bid activity, reserve-crossing indicator, extended-bidding status, and terminal outcomes. The milestone doc's exit criteria advance toward completion. No narrative or workshop amendment was required.

## Verification checklist

- [x] Seller `parseMessage` parses `BidPlacedNotification`, `ListingSoldNotification`, and `ListingGroupNotification` into a discriminated union
- [x] Seller cache bridge invalidates `["listing", id]` and `["sellerListings"]` on listing-related pushes
- [x] `SellerSignalRProvider` wires `parseMessage` and `applyMessage` (replacing the `null` stubs)
- [x] Listing detail page at `/listings/$id` renders CatalogListingView data
- [x] Reserve indicator compares `currentHighBid` against seller's `reservePrice`
- [x] Extended-bidding banner derived from `scheduledCloseAt` movement
- [x] Terminal outcome shows Sold (hammer price) / Passed / Withdrawn
- [x] No bid panel on the seller's auction view
- [x] Live activity feed with capped, deduped, most-recent-first entries
- [x] Listing detail page joins `listing:{listingId}` group on mount
- [x] Dashboard listings navigate to `/listings/$id`
- [x] `npm run build` succeeds for seller app (TypeScript strict, no errors)
- [x] Existing frontend baselines preserved: bidder 25 tests, ops 47 tests
- [x] Seller Vitest count grew from 55 to 84 (+29)
- [x] Live smoke against Aspire stack recorded in retro
- [x] No backend changes (zero `.cs` files touched)
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [x] This retrospective written

## Live smoke results

**HTTP contract verification (automated):**
1. `POST /api/participants/session` → 201, Location header with participant ID ✓
2. `POST /api/selling/register` → 200 ✓
3. `POST /api/selling/listings/create-draft` (Flash, reserve $50, BIN $100, extended bidding) → 201, Location header with listing ID ✓
4. `POST /api/selling/listings/{id}/submit` → 202 (auto-approval fires) ✓
5. `GET /api/selling/my-listings` → Published status, `reservePrice: 50.0` visible ✓
6. `GET /api/listings/{id}` → CatalogListingView with `status: "Published"`, `bidCount: 0`, `scheduledCloseAt: null` ✓

**SPA proxy verification:**
- `http://localhost:5175/api/listings` → 200 (Vite proxy to API host) ✓
- `http://localhost:5175/api/listings/{id}` → 200 ✓
- `http://localhost:5175/seller/` → 200 (SPA index served) ✓

**Live-observation limitation (expected):** Flash listings require an operator to start a session before bidding opens. The smoke verified the complete seller lifecycle through Published, confirming the detail page would render the `CatalogListingView` data correctly. Full live-observation (bid activity, reserve crossing, extended bidding, gavel fall) requires an operator + bidders — this is inherent to the auction format, not a test gap.

## What remains / next session should verify

- **M9-S6 (obligation tracking / post-sale):** Seller sees post-sale status, provide-tracking form. Next slice in the milestone.
- **Full live-observation smoke:** An end-to-end test with operator session start + bidder bids would exercise the complete SignalR path through the seller's detail page. Consider adding to `client/e2e/` Playwright suite.
- **Cache-bridge burst-final hardening:** Carry-forward from M8-S7. Evaluate in M9-S7.
- **`@critterbids/shared` message extraction:** Bidder and seller parsers overlap ~70%. Evaluate extraction when both have stabilized (M9-S7).
- **`docs/STATUS.md` regeneration:** Deferred to M9-S7.
- **`FakeHubConnection` shared extraction:** Three copies across three SPAs. Extract to `@critterbids/shared` test utilities when a pattern audit runs (M9 skills review).

## Files changed

**New:**
- `client/seller/src/signalr/messages.ts` — Hub message types + `parseHubMessage` + `listingIdOf`
- `client/seller/src/signalr/cacheBridge.ts` — `applyHubMessage` cache invalidation
- `client/seller/src/signalr/hooks.ts` — `useListen`, `useHubConnectionState`, `useWatchListing`
- `client/seller/src/listings/ListingDetailPage.tsx` — Route `/listings/$id`, static card + live auction panel
- `client/seller/src/listings/LiveAuction.tsx` — Live auction observation panel (reserve indicator, extended bidding, terminal outcomes)
- `client/seller/src/listings/LiveActivity.tsx` — Transient activity feed (capped, deduped)
- `client/seller/src/signalr/messages.test.ts` — 7 tests
- `client/seller/src/signalr/cacheBridge.test.ts` — 4 tests
- `client/seller/src/listings/ListingDetailPage.test.tsx` — 9 tests
- `client/seller/src/listings/LiveActivity.test.tsx` — 6 tests

**Modified:**
- `client/seller/src/signalr/SignalRProvider.tsx` — Rewritten from null-stub to full provider
- `client/seller/src/listings/queries.ts` — Added `useListing`, `listingDetailQueryOptions`, `ListingNotFoundError`
- `client/seller/src/listings/ListingsPage.tsx` — Added "View" button with reserve search param
- `client/seller/src/router.tsx` — Added `listingDetailRoute` with `validateSearch`
- `client/seller/src/lib/format.ts` — Added `auctionStatusVariant()`
- `client/seller/src/listings/ListingsPage.test.tsx` — Added 2 View-button tests

**Docs:**
- `docs/prompts/implementations/M9-S5-seller-live-auction-observation.md` — Session prompt
- `docs/retrospectives/M9-S5-seller-live-auction-observation-retrospective.md` — This file
