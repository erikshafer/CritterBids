# M8-S3b: Bidder Live Bidding + SignalR Integration ADR — Retrospective

**Date:** 2026-06-08
**Milestone:** M8 — React Frontend SPAs
**Slice:** S3b — Bidder Live Bidding + ADR 026 (frontend half of the bid-placement journey)
**Agent:** @PSA with @UXE on the bidding UX
**Prompt:** `docs/prompts/implementations/M8-S3b-bidder-live-bidding.md`

## Baseline

- Frontend build clean at session start: `client/bidder` → `npm run build` exit 0; `npm test` → **7 tests green** across 2 files (catalog page + schema).
- M8-S3a merged: `POST /api/auctions/bids` live (`[AllowAnonymous]`, 200 `PlaceBidResponse` / ProblemDetails 4xx with `reason` + `currentHighBid`, 404 `UnknownBidder`, server-sourced credit ceiling).
- M8-S2 shell present: routed TanStack Router app, anonymous `ParticipantId` held in `sessionStorage`, a connected-but-data-less `useBiddingHub` opening one `/hub/bidding` connection inside the connection indicator.
- ADR number confirmed against the index: `026` was still the next unreserved number — used as-is; pointer advanced to `027`.

## Items completed

| Item | Description |
|------|-------------|
| S3b.1 | **ADR 026** authored (`SignalRProvider` + `useListen` + TanStack Query cache bridge), `Accepted`, alternatives weighed, Document-History row; ADR index row added + next-number pointer → 027 |
| S3b.2 | **ADR 013** amended — SignalR-integration Deferred Question flipped parked → resolved → ADR 026; other three remain parked; dated Document-History row |
| S3b.3 | **Milestone doc** stale `ADR 014` references corrected to `ADR 026` (number-only, verbatim) + v0.3 Document-History row; the same stale collision in **ADR 025**'s relationship table + prose corrected too (it pointed at "ADR 014 (forthcoming)" for the SignalR pattern) |
| S3b.4 | `SignalRProvider` (one app-wide connection) replaces M8-S2's per-component `useBiddingHub`; `useListen` / `useHubConnectionState` / `useWatchListing` hooks; Zod `parseHubMessage` wire-normalization |
| S3b.5 | Cache bridge: push → `invalidateQueries`; the listing-detail view re-queries on every relevant push (no payload rendered as truth) |
| S3b.6 | Bid placement (`usePlaceBid`) — optimistic update, reconcile against 200, rollback + reason surface on 4xx/5xx; `BidPanel` UI |
| S3b.7 | Live beats (Moments 5–7): outbid, extended-bidding banner, gavel/sold — all derived from re-queried view transitions; `LiveActivity` transient feed via `useListen` |
| S3b.8 | Vitest + RTL: wire-normalization, cache-bridge re-query on a simulated push, optimistic reconcile/rollback, UI reason surface — **+10 tests (7 → 17)** |

## The headline finding: the push wire is heterogeneous and carries no `Outbid`

Reading the lived Relay surface (`src/CritterBids.Relay/Notifications/`) before writing a line of client code changed the design:

- **No uniform `type` discriminator.** `BidPlacedNotification` and `ListingSoldNotification` carry no `eventType` (distinguished structurally by `bidId` vs `winnerId`); `ListingGroupNotification` / `BidderGroupNotification` carry an `eventType` string + a human `payload`. `parseHubMessage` normalizes all four into one discriminated `HubMessage` union, tried most-specific-first, returning `null` (logged-and-ignored) on an unrecognized shape so a future notification type can't tear down the connection.
- **No `Outbid` push exists.** Narrative 001 Moment 5 names a targeted "Outbid" push, but Relay fans out `BidPlaced` only. Per the prompt's open question this resolved to a **client-side derivation** (held participant drops from high bidder to not-high while still Open), not an escalated backend event. The "push = re-query, derive beats from view transitions" discipline makes this natural — outbid, extended bidding, and gavel are all derived from how the re-queried `CatalogListingView` changes, never from a push payload.

This is the load-bearing rule ADR 026 encodes (milestone §6 / M7 §5): the cache bridge invalidates and re-queries the authoritative read endpoint; the push body is never rendered as state.

## The two M8-S3a flags — resolved frontend-only (no backend change)

- **(a) Idempotency.** `usePlaceBid` sets `retry: false`. A dropped/lost response is **never** auto-retried (the server generates the `BidId`, so a blind retry could double-bid) — it rolls the optimistic update back and the bidder re-submits deliberately. No client idempotency key added (that would be a backend slice to escalate).
- **(b) Concurrent-conflict.** `placeBid` treats **any** non-2xx — including a 5xx from a `DcbConcurrencyException` — as a rollback-and-retry-prompt, the same path as a 4xx rejection (`friendlyMessage` maps an unrecognized/5xx case to "Something changed while placing your bid. Please try again."). No graceful 409 mapping added on the backend.

Neither required a `.cs` change. The committed resolution is exactly In-scope #5; if the live two-bidder demo proves either genuinely needed, it escalates as its own backend slice.

## Architecture notes

- **One connection, not many.** M8-S2's `useBiddingHub` opened its own connection inside the indicator. `SignalRProvider` (mounted above the router, inside `SessionProvider` so it can enrol the held `ParticipantId` into its `bidder:{id}` group) now owns the single connection; the indicator reads its state via `useHubConnectionState`. `useBiddingHub.ts` was deleted.
- **Testable live channel.** The Provider's `createConnection` prop injects a fake `HubConnection` in tests — the cache-bridge test fires a `ReceiveMessage` with `amount: 999` and asserts the rendered value comes from the re-queried view (32), never the push (999 is asserted absent). Same dependency-injection seam the backend uses with `TimeProvider`.
- **Reconciliation ordering.** Floor held at "200 reconciles immediately, the push re-query confirms": `onSuccess` writes the authoritative `PlaceBidResponse` into the cache (including any `extendedBidding.newCloseAt`), `onSettled` invalidates, and the BiddingHub push for the same bid lands its own invalidation — all converging on the one view, so no flicker or double-count.

## Test results

| Phase | Files | Tests | Result |
|-------|-------|-------|--------|
| Baseline | 2 | 7 | green |
| After S3b | 6 | 17 | green (+10, no prior test changed) |

New tests: `signalr/messages.test.ts` (wire normalization, 5), `signalr/SignalRProvider.test.tsx` (cache-bridge re-query on a simulated push, 1), `bidding/usePlaceBid.test.tsx` (200 reconcile + 4xx rollback, 2), `bidding/BidPanel.test.tsx` (UI reason surface + acceptance, 2).

## Build state at session close

- `client/bidder` → `npm run build` (tsc --noEmit + vite build) → exit 0, TypeScript strict clean.
- `npm test` → 17/17 green.
- `.cs` / `.csproj` / `.slnx` / `Program.cs` changes: **0**. No client idempotency key, no 409 mapping, no `BiddingHub` / endpoint change. No `client/ops/`, no `OperationsHub`, no settlement view, no BuyNow/proxy UI, no display-name backend workaround.
- In-set libraries only (no new dependency; `button`/`input` copied in per the shadcn model).

## Findings against narrative

Narrative 001 Moments 3–7 (`docs/narratives/001-bidder-wins-flash-auction.md`).

- **`document-as-intentional`.** Moment 5's "targeted Outbid push" does not exist in the lived Relay contract (Relay fans out `BidPlaced` only). The narrative already files the Relay push surface under `defer` / forward-spec, and Moment 4's note now points the PlaceBidSheet UI at M8-S3b. The bidder-facing outbid affordance is a client derivation off `BidPlaced` + the view transition — the same observable beat, derived rather than pushed. No narrative edit required; the Moment text describes the designed behavior, and the derivation reproduces the bidder's vantage faithfully. (If a server `Outbid` push is later wanted for fidelity, it is a backend slice to escalate.)

## Spec delta — landed?

Landed. Per ADR 020: (1) **Narrative 001 Moments 3–7 render** in the bidder app — bid placement (Moment 4's PlaceBidSheet, deferred from the narrative to here) over `POST /api/auctions/bids` with optimistic update + rollback; the bid echo + outbid (Moment 5) as a re-query-driven feed + a derived outbid banner; the extended-bidding reclaim (Moment 6) as a banner reading the re-queried new close time; and the gavel-fall win (Moment 7) as a terminal "You won" state — all over `BiddingHub`. (2) **ADR 026 is authored** (`Accepted`): the SignalR integration pattern (`SignalRProvider` + `useListen` + TanStack Query cache bridge, Zod-normalized heterogeneous wire, push = re-query); the ADR index gained its row and the next-unreserved-number pointer advanced 026 → 027. (3) **ADR 013's SignalR-integration Deferred Question is resolved** → ADR 026 (amended in place; open-question count dropped 4 → 3). (4) **The milestone doc's stale `ADR 014` references are corrected to `ADR 026`** (number-only, plus a v0.3 Document-History row). Both M8-S3a flags were handled frontend-only (no-auto-retry idempotency; any non-2xx is a rollback) with no backend change. Both SPAs' future hub work now points at ADR 026.

## Verification checklist

- [x] ADR 026 exists (SignalR integration pattern — Provider + `useListen` + cache bridge), `Accepted`, alternatives weighed, Document-History row; index row added + pointer → 027
- [x] ADR 013 amended: SignalR-integration Deferred Question resolved → ADR 026 with a dated Document-History row; other three remain parked
- [x] Milestone doc `ADR 014` references corrected to `ADR 026` with a Document-History row
- [x] Listing-detail view joins the `BiddingHub` group and re-queries the read model on push; no push payload rendered as authoritative state (the query is the render source)
- [x] Bid placed via `POST /api/auctions/bids` with a JSON body; 200 reconciles the optimistic update; 4xx/5xx rolls back + surfaces the ProblemDetails `reason`
- [x] Outbid indication, extended-bidding banner (new close time), gavel-fall/sold state — driven by hub push → re-query (derived from view transitions)
- [x] Two M8-S3a flags handled frontend-only: no auto-retry of a lost-response bid; any non-2xx rolls back. No backend change (no `.cs`/`.csproj`/`.slnx`/`Program.cs`; no idempotency key; no 409 mapping)
- [x] Vitest + RTL: optimistic-update rollback on rejection (reason surfaced), cache-bridge re-query on a simulated push, 200 reconciliation; no Playwright e2e added
- [x] `client/bidder` builds (exit 0) + type-checks under strict from a clean checkout; in-set libraries only
- [x] No `client/ops/`, no `OperationsHub`, no settlement view, no BuyNow/proxy UI, no display-name backend workaround
- [x] This retrospective written with the `**Prompt:**` header, the `## Spec delta — landed?` paragraph, and how each flag was resolved frontend-side
- [x] No commit to `main`; work on branch `M8-S3b-bidder-live-bidding`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **M8-S4 (Bidder Settlement Outcome)** renders Moment 8 (won/charged confirmation). The terminal "You won" state here is the handoff point; the settlement-result view binds the `SettlementCompleted` surface.
- **M8-S5 ops app** reuses the ADR 026 Provider/hook/bridge shape against `OperationsHub`, adding the `access_token` negotiate credential (ADR 024). When `client/shared/` is created, the `signalr/` module is the natural first tenant.
- **M8-S7 Playwright e2e** is the true multi-context two-bidder bid-war test (mocked-fetch unit tests verify response handling, not request shape — LESSONS §D #18). A live-smoke run against an Aspire host is the cheapest next check that the bid POST's body/casing match the endpoint.
- **Server `Outbid` push** stays a candidate backend slice only if the demo wants targeted-push fidelity over the current client derivation.
