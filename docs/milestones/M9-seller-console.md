# M9 — Seller Console

**Status:** ✅ Complete (M9-S8 close, 2026-06-17)
**Scope:** The third React SPA — a seller-facing console that renders the seller-perspective journeys narratives 004–007 describe: listing management, live auction observation, and post-sale obligation fulfillment. M9 also triggers the `client/shared/` extraction that ADR 025 deferred to the third consumer, and carries the first housekeeping backend slices from M8's deferred ledger.
**Companion docs:** [`../narratives/004-seller-publishes-and-withdraws-listing.md`](../narratives/004-seller-publishes-and-withdraws-listing.md) · [`../narratives/005-seller-watches-flash-auction-close.md`](../narratives/005-seller-watches-flash-auction-close.md) · [`../narratives/006-seller-fulfills-post-sale-obligation.md`](../narratives/006-seller-fulfills-post-sale-obligation.md) · [`../narratives/007-seller-recovers-missed-shipping-deadline.md`](../narratives/007-seller-recovers-missed-shipping-deadline.md) · [`../decisions/025-spa-monorepo-layout.md`](../decisions/025-spa-monorepo-layout.md) (§`client/shared/`) · [`../decisions/013-frontend-core-stack.md`](../decisions/013-frontend-core-stack.md) · [`../decisions/026-signalr-integration-pattern.md`](../decisions/026-signalr-integration-pattern.md) · `CLAUDE.md` §Frontend · [`../retrospectives/M8-retrospective.md`](../retrospectives/M8-retrospective.md) (§"What M9 Should Know") · [`../skills/frontend-slice-discipline/SKILL.md`](../skills/frontend-slice-discipline/SKILL.md)

---

## 1. Goal & Exit Criteria

### Goal

Deliver the **seller console** — a third React SPA at `client/seller/` that gives GreyOwl12 (and any registered seller) a working surface for the journeys the four seller narratives dramatise: drafting and publishing listings, observing live auctions on those listings, and fulfilling post-sale obligations. The seller console is the **seller's window** on the same engine the bidder app and ops dashboard already render — the third perspective on the same auction.

M9 is also the `client/shared/` extraction milestone. ADR 025 planned the shared workspace member as the frontend analogue of `CritterBids.Contracts`; M8-S7 evaluated and deferred it, finding that the bidder and ops apps "duplicate the pattern, not the bytes" (different hubs, auth, message vocabularies). The seller console is the third consumer that reveals the real shared subset — Zod wire schemas, the SignalR provider/hook pattern, and any shared UI infrastructure that emerges from three consumers.

The backend is largely shipped — the Selling, Auctions, Obligations, and Relay BCs are feature-complete. But several seller-side commands lack HTTP endpoints (they exist only as bus commands driven by the dev-seed endpoint in-process). M8's retro explicitly flagged this: *"expect S3a-style sanctioned-exception slices to expose whatever the console needs; budget them up front."* M9 budgets those backend precursor slices as first-class scope, not afterthoughts.

### Exit criteria

- [x] **Seller SPA** exists at `client/seller/` as an npm-workspace member, rendering the seller-perspective journeys: listing management (narratives 004 Moments 1–5, M9-S4a/S4b), live auction observation (narrative 005 Moments 1–4, M9-S5), and obligation fulfillment (narrative 006 Moments 1–4, M9-S6)
- [x] **`client/shared/` extracted** as a fifth npm-workspace member per ADR 025 (M9-S1), consumed by all three SPAs (bidder, ops, seller); the shared surface includes the SignalR provider/hook/cache-bridge pattern, the shared Zod wire schemas, and the Tailwind theme — **annotation:** ADR 025 counted it as the "fourth" member; with the `e2e` member already landed in M8, `shared` is the fifth, leaving a five-member workspace
- [x] **Seller-side HTTP surface complete** — every seller action the console drives has a public HTTP endpoint (M9-S2/S3 backend precursor slices, sanctioned and recorded per M8's precedent); no bus-only commands remain for seller-facing flows
- [x] **Live auction observation** — the seller console connects to `BiddingHub` (anonymous, same as the bidder app, M9-S5) and shows real-time bid activity, reserve crossing, extended-bidding status, and gavel-fall for the seller's own listings
- [x] **Obligation management** — the seller console surfaces the `ObligationStatusView` for the seller's own listings and drives the `ProvideTracking` command (M9-S6); the full lifecycle is now e2e-verified (M9-S8)
- [x] **Listings `ExtendedBiddingTriggered` handler shipped** (M9-S3) — `CatalogListingView.ScheduledCloseAt` advances on extension, re-arming the extended-bidding banner the M8-S7 e2e found unreachable (first M8 housekeeping carry-forward)
- [x] **Cache-bridge burst-final hardening evaluated** (M9-S3) — see the M9-S3 retro for the disposition
- [x] Clean-checkout `npm install` + `npm run build` on all workspace members; TypeScript strict; no .NET breakage
- [x] Existing .NET baseline unchanged beyond sanctioned backend exceptions — 0 errors / 0 net-new warnings held; **annotation:** baseline grew 307 → 328 backend tests across M9's sanctioned backend slices (S2/S3/S7), not broken
- [x] Playwright e2e extended with a seller-perspective test (M9-S8 — `client/e2e/tests/seller-obligation.spec.ts`, two consecutive green runs against the live Aspire stack; reuses the M8-S7 harness conventions)
- [x] CI `frontend` job covers the seller app (build + Vitest) — **annotation:** landed earlier than this slice (PR #111 restructured the `frontend` matrix to cover `seller` build-test + `shared`/`e2e` type-check); M9-S8 verified it rather than adding it
- [x] `CLAUDE.md` §Frontend updated (seller app, `client/shared/`, workspace member count) — kept current across M9 slices; verified at M9-S8 close
- [x] All slice retros + M9 retrospective doc written (M9-S8)

---

## 2. In Scope

### The seller SPA

| App | Directory home | Audience | Auth | Live channel | Renders |
|---|---|---|---|---|---|
| Seller console | `client/seller/` | Registered sellers | Anonymous (same ParticipantId session as bidder; seller registration is the gate) | `BiddingHub` (`/hub/bidding`) | Narratives 004 (listing lifecycle), 005 (auction observation), 006 (obligation fulfillment), 007 (escalation recovery) |

The seller console is the third static Vite SPA in the `client/` workspace. It follows the same stack (ADR 013) and layout conventions (ADR 025) as the bidder and ops apps. Auth is anonymous — the seller's identity is the `ParticipantId` from the session, with `IsRegisteredSeller` as the application-level gate (the same posture the Selling BC endpoints use today; per-user seller identity is post-MVP, ADR 024 revisit trigger territory).

### Seller SPA surfaces

| Surface | What it renders | Source |
|---|---|---|
| Seller registration | One-click seller registration (`POST /api/participants/{id}/register-seller`) from the session | Narrative 004 Moment 1 |
| My listings (dashboard) | Seller's own listings with status (Draft / Published / Sold / Withdrawn) | New query endpoint — **backend precursor** |
| Create listing | Draft form with all listing-time fields (title, format, starting bid, reserve, BIN, duration, extended bidding) | Narrative 004 Moment 2; `POST /api/listings/draft` (exists) |
| Edit draft | Update draft listing before submission | `UpdateDraftListing` — **backend precursor** (bus-only today) |
| Submit listing | Submit draft for publication (auto-approval) | `SubmitListing` — **backend precursor** (bus-only today; narrative 004 F002) |
| Withdraw listing | Withdraw a published listing | Narrative 004 Moment 5; `POST /api/selling/listings/withdraw` (exists) |
| Live auction view | Real-time bid feed, reserve-crossing indicator (confidential to seller), extended-bidding status, gavel-fall | Narrative 005 Moments 1–4; `BiddingHub` |
| Obligation tracker | Post-sale obligation status, ship-by deadline, reminder banner, escalation state | Narrative 006 Moments 1–4; narrative 007 Moments 1–3 |
| Provide tracking | Carrier + tracking number entry form | Narrative 006 Moment 3; `POST /api/obligations/tracking` (exists) |
| Settlement summary | Payout confirmation (hammer − fee) | Narrative 002 Moment 4 (cross-reference); query endpoint TBD |

### `client/shared/` extraction

The fourth npm-workspace member. ADR 025 described it as the "frontend analogue of `CritterBids.Contracts`." M8-S7 found the bidder and ops apps duplicate the *pattern* (SignalR provider, `useListen`, cache bridge, Zod parse surface) but not the *bytes* (different hubs, different auth, different message vocabularies). With three consumers, the real shared subset becomes visible:

**Candidates for extraction** (confirmed by M8 lived experience):
- The `SignalRProvider` + `useListen` + cache-bridge pattern (ADR 026) — identical shape, parameterised by hub URL + auth
- Zod wire schemas for shared contract types (`CatalogListingView`, other Listings query responses used by both bidder and seller)
- TanStack Query wrapper utilities (if any emerge from three consumers)
- Shared Tailwind preset / shadcn component subset (evaluate at extraction time)

**Not extracted prematurely:**
- Hub-specific message vocabularies (bidder's `BidPlacedNotification` vs ops's `OperationsFeedNotification`)
- Hub-specific auth configuration (`skipNegotiation` + token factory for ops; anonymous for bidder + seller)
- App-specific routing, layouts, pages

The extraction is evaluated against three real consumers, not speculated. M9-S1 performs the extraction; later slices consume the result.

### Backend precursor slices

The M8 retro's "What M9 Should Know" section warned: *"Seller-side backend surfaces are bus-only today. The seller submit flow and operator attach/start commands have no public HTTP endpoints — the dev seed endpoint orchestrates them in-process precisely because of that."*

M9 follows M8's precedent: backend precursors are **sanctioned, recorded, and run as their own slices** before the frontend slices that need them.

#### Endpoint surface audit

| Command | Endpoint today | Gap |
|---|---|---|
| `StartParticipantSession` | `POST /api/participants/session` | ✅ Exists |
| `RegisterAsSeller` | `POST /api/participants/{id}/register-seller` | ✅ Exists |
| `CreateDraftListing` | `POST /api/listings/draft` | ✅ Exists |
| `UpdateDraftListing` | *(bus-only)* | **Needs endpoint** |
| `SubmitListing` | *(bus-only)* | **Needs endpoint** (narrative 004 F002) |
| `WithdrawListing` | `POST /api/selling/listings/withdraw` | ✅ Exists |
| `ProvideTracking` | `POST /api/obligations/tracking` | ✅ Exists |
| Seller's own listings query | *(none)* | **Needs endpoint** |
| Seller's obligation status query | *(none)* | **Needs endpoint** (may query `ObligationStatusView` by seller) |
| Seller's settlement summary query | *(none)* | **Needs endpoint** (may query `PendingSettlement` or a seller projection) |

Three of the ten seller-facing flows need new endpoints; three more need new query surfaces. Six total backend touches — budget for two backend precursor slices.

### Housekeeping carry-forwards from M8

| Item | Source | Disposition in M9 |
|---|---|---|
| **Listings `ExtendedBiddingTriggered` handler** — `CatalogListingView.ScheduledCloseAt` never advances; the extended-bidding banner and "Extended" catalog status are unreachable | M8-S7 e2e finding | **In scope** — first M9-adjacent housekeeping candidate per the M8 retro |
| **Cache-bridge burst-final hardening** (push-refetch race for the last event of a burst; the re-query can lose the race to a sibling-queue projection) | M8-S7 e2e finding | **In scope** as an evaluation; if the fix is localised (delayed re-invalidate in the cache bridge), ship it; if it requires infrastructure, record rationale and defer |

---

## 3. Explicit Non-Goals

- **New backend domain capability.** M9 is, like M8, primarily a frontend milestone that renders existing backend surfaces. The backend precursor slices expose *existing* commands/queries over HTTP — no new domain events, no new saga transitions, no new BC modules. Any finding that requires new domain capability is escalated, not absorbed.
- **Seller authentication beyond the existing anonymous-session posture.** The seller is identified by `ParticipantId` with `IsRegisteredSeller` as the application gate. Per-user seller identity, persistent seller accounts, and external IdP integration are post-MVP (ADR 024 revisit trigger).
- **Operator-facing session management in the seller console.** CreateSession, AttachListingToSession, and StartSession are staff commands (ADR 024's `StaffOnly` policy). The seller console does not surface session management — the operator does that from the ops dashboard. The seller sees their listing transition to "Open" when the operator starts the session.
- **Dispute-opening from the seller console.** The M8-S6b dispute-resolution control is an ops-dashboard feature. The buyer's `OpenDispute` endpoint exists but is narrated only from the buyer/operator vantage (narrative 008). Seller-side dispute UI (viewing an open dispute, responding) is a future extension, not M9 core.
- **Timed-listing lifecycle.** Narratives 004/005 dramatise Flash listings (session-attached, operator-started). The Timed-listing path (bidding opens immediately on publication, no session) is structurally complete in the backend (M3 lived) but the seller console's scope is the Flash demo story, not a full listing-format matrix. Timed listings work by construction — they are not the demo scenario.
- **Email / SMS / push notifications to the seller.** Relay is SignalR-only at MVP; non-SignalR delivery channels are post-MVP (M6 non-goals). The seller console relies on the same `BiddingHub` push + re-query pattern the bidder app uses.
- **Native wrappers.** Same as M8: Capacitor / Tauri / native shells are not in scope.
- **Production deployment.** The seller console is served locally via Aspire (matching the bidder/ops apps as Aspire children); production static-file serving middleware remains deferred from M8.

---

## 4. Solution Layout

### New surfaces added in M9

```
client/
├── package.json              # workspaces: bidder, ops, seller, shared, e2e
├── tsconfig.base.json
├── shared/                   ← NEW IN M9 (extracted from bidder + ops)
│   ├── package.json          # @critterbids/shared
│   └── src/
│       ├── signalr/          # SignalRProvider, useListen, cache bridge (parameterised)
│       ├── schemas/          # Shared Zod wire schemas (CatalogListingView, etc.)
│       └── ...
├── seller/                   ← NEW IN M9
│   ├── package.json          # @critterbids/seller
│   ├── vite.config.ts        # proxy to localhost:5180, base: "/seller/"
│   ├── tsconfig.json         # extends ../tsconfig.base.json
│   └── src/
├── bidder/                   # MODIFIED: consumes @critterbids/shared
├── ops/                      # MODIFIED: consumes @critterbids/shared
└── e2e/                      # EXTENDED: seller-perspective tests
```

The workspace grows from three members (bidder, ops, e2e) to five (+ shared, seller). The seller app's `base` path (`/seller/`) follows the ops precedent (`/ops/`) per ADR 025's production-serve posture.

### Backend layout — unchanged

`src/` and `tests/` are structurally unchanged. The backend precursor slices add HTTP endpoints to existing BC projects (Selling, possibly Listings/Obligations) — no new `.csproj`, no new BC. The Listings `ExtendedBiddingTriggered` handler is a handler addition to the existing Listings BC.

### Aspire integration

The seller SPA dev server becomes the fourth Aspire child process (seller `:5175`), matching the bidder (`:5173`) and ops (`:5174`) pattern from M8. `src/CritterBids.AppHost/` gains the child-process registration; `ASPIRE_URLS` or the equivalent env-var binding follows the M8 convention.

---

## 5. Infrastructure, Build & Dev

### Build integration

Same as M8: each SPA is a static Vite build. The seller app follows the bidder/ops precedent — `npm run build` in `client/seller/` produces a `dist/` directory. No meta-framework, no SSR.

### Local dev-server story

The seller app runs its own Vite dev server with the same proxy configuration as bidder/ops:

```ts
server: {
  port: 5175,
  proxy: {
    "/api": { target: "http://localhost:5180", changeOrigin: true },
    "/hub": { target: "http://localhost:5180", changeOrigin: true, ws: true },
  },
}
```

### Real-time client wiring

The seller console connects to `BiddingHub` (`/hub/bidding`) — the same anonymous hub the bidder app uses. The seller is a participant who happens to also be registered as a seller; the hub does not distinguish. The seller sees bid-placed, reserve-met, extended-bidding, and gavel-fall notifications on their own listings via the same push + re-query pattern (ADR 026).

**Open question for M9-S1:** whether seller-specific notifications (e.g. `SellerPayoutIssued` from Settlement via Relay) currently route to a `seller:{sellerId}` group on the `BiddingHub`, or whether that routing needs to be added. The answer is in `src/CritterBids.Relay/` — if the routing exists, the seller console consumes it; if not, a backend precursor wires it.

### `client/shared/` as a workspace dependency

`@critterbids/shared` is an internal workspace package. Consumer apps declare it as a dependency in their `package.json`; npm workspaces resolves it via the symlink. No publish to npm; no build-time copy. TypeScript project references (`references` in `tsconfig.json`) wire the type system.

### Testing infrastructure

Per ADR 013: Vitest for unit/component tests in the seller app, Playwright for e2e. The M8-S7 Playwright harness (`client/e2e/`) is extended with seller-perspective tests. The verification ladder from M8 applies: lived backend first, live smoke per slice, extend the e2e.

### CI extension

The `frontend` CI job is extended to include `@critterbids/seller` (build + Vitest) and `@critterbids/shared` (build + any shared tests). The backend matrix is unchanged.

---

## 6. Conventions Pinned

### Frontend stack — ADR 013

The seller console uses the same accepted stack as the bidder and ops apps. No new libraries without an ADR.

### SPA layout — ADR 025

The seller app follows the decided layout: workspace member at `client/seller/`, Vite dev proxy, host-served static output at `/seller/`. `client/shared/` is the fourth member per ADR 025's original plan, now realized.

### SignalR integration — ADR 026

The seller console uses the same Provider + `useListen` + cache-bridge pattern, extracted to `client/shared/` as the shared integration surface. A push is a re-query signal, never authoritative data.

### Frontend slice discipline

The `frontend-slice-discipline` skill (authored in M8) governs M9 slices. Its rules apply verbatim:
1. Read `src/` before writing client code — bind to reality, not narrative tables.
2. Backend exceptions are sanctioned, recorded, and run as their own slices.
3. Live smoke per slice against the running Aspire stack.

### Backend convention carry-through

All backend conventions from `CLAUDE.md` apply to the precursor slices: `sealed record` commands, `[WriteAggregate]` patterns, `OutgoingMessages` for integration events, UUID v7 stream IDs, no "Event" suffix.

---

## 7. Slice Breakdown

M9 ran as eight slices. The first two are backend and infrastructure precursors; the next four build the seller SPA; a backend race-fix slice (S7) landed before the close; and S8 closes the milestone. Like M8, this was a scope ceiling refined in per-slice prompts — open questions resolved in the slices that needed them, not pre-decided here. (The slice plan originally numbered the close "S7"; the `CatalogListingView` cross-queue race fix took S7 mid-milestone, so the close renumbered to S8.)

| Slice | Title | Scope |
|---|---|---|
| M9-S1 | Foundation — `client/shared/` extraction + seller SPA scaffold | Extract the shared SignalR + Zod + utility surface into `client/shared/` by evaluating what three consumers (bidder, ops, seller) actually share. Migrate bidder and ops to consume `@critterbids/shared` — both apps must remain green after extraction. Scaffold `client/seller/` as the fifth workspace member: Vite + React + TS strict, Tailwind v4, dev proxy, Aspire child registration (`:5175`). Resolve the seller-hub routing open question. No seller UI beyond the scaffold proof. |
| M9-S2 | Backend precursor — Seller listing endpoints | **Sanctioned backend exception (budgeted).** Wire the missing seller-facing HTTP endpoints over existing bus commands: (1) `POST /api/selling/listings/submit` over `SubmitListing` (narrative 004 F002 follow-up); (2) `PUT /api/selling/listings/draft` or equivalent over `UpdateDraftListing`; (3) `GET /api/selling/listings?sellerId=` — a seller-scoped listing query (new query endpoint against `SellerListing` or a Selling-side projection). Integration tests per endpoint. No frontend. |
| M9-S3 | Backend precursor — Seller query endpoints + housekeeping | **Sanctioned backend exception (continued).** Wire seller-facing query endpoints for obligation status and settlement summary. Ship the Listings `ExtendedBiddingTriggered` handler (the M8-S7 carry-forward — `CatalogListingView.ScheduledCloseAt` advances on extension). Evaluate and ship or defer the cache-bridge burst-final hardening. Integration tests. No frontend. |
| M9-S4 | Seller SPA — registration + listing management | Seller app shell + layout; seller registration flow (the one-click `RegisterAsSeller` from the session); listing dashboard ("my listings" view consuming the S2 query endpoint); create-draft form (`react-hook-form` + Zod validation against listing-time fields); edit-draft; submit-for-publication; withdraw. Narratives 004 Moments 1–5. |
| M9-S5 | Seller SPA — live auction observation | Live auction view from the seller's vantage: `BiddingHub` connection (via `@critterbids/shared` SignalR provider); real-time bid feed on the seller's own listings; reserve-crossing indicator (the seller's confidential reserve is known client-side from the draft fields); extended-bidding status; gavel-fall. Narrative 005 Moments 1–4. |
| M9-S6 | Seller SPA — obligation fulfillment | Obligation tracker: post-sale status view (`ObligationStatusView` for the seller's listings); ship-by deadline countdown; reminder banner; provide-tracking form (driving the existing `POST /api/obligations/tracking`); delivery-confirmed / fulfilled terminal state. Narratives 006 Moments 1–4 + narrative 007's escalation-recovery UX (the "Overdue — under review" → "Shipped" recovery). |
| M9-S7 | Listings cross-queue race fix | Backend bug fix landed mid-milestone (PR #112): the `CatalogListingView` last-writer-wins create race between the `listings-selling-events` and `listings-auctions-events` queue handlers, resolved with `Insert`-on-create across all twelve write methods + a `DocumentAlreadyExistsException` retry policy. No frontend; not in the original slice plan. See `docs/retrospectives/M9-S7-listings-cross-queue-race-fix-retrospective.md`. |
| M9-S8 | End-to-end + housekeeping (close) | Playwright seller-perspective e2e (extend the M8-S7 harness — a seller's listing sells, the seller console provides tracking, the obligation auto-confirms to Fulfilled; `client/e2e/tests/seller-obligation.spec.ts`); CI `frontend` coverage verified (already complete per PR #111); `bounded-contexts.md` / `STATUS.md` / milestone-doc refresh; pre-M10 skills audit; M9 milestone retrospective. Sanctioned dev-host config: `Obligations__DemoMode=true` in the AppHost so the post-sale lifecycle runs live (the conference-demo posture). |

### Open questions for per-slice resolution

| # | Question | Resolving slice | Lean |
|---|---|---|---|
| OQ-1 | Does the `BiddingHub` currently route seller-specific notifications (e.g. `SellerPayoutIssued`, `ObligationFulfilled` addressed to the seller) to a seller group, or does the seller need to subscribe to listing-level groups? | M9-S1 | Lean: listing-level groups (`listing:{listingId}`) are already used for bid notifications; the seller joins the same group and receives the same pushes the bidder does. Seller-specific notifications (payout) may need a `seller:{sellerId}` group addition in Relay. |
| OQ-2 | What is the right query shape for "my listings"? A Selling-side `SellerListingSummary` projection, or a query endpoint over the existing `SellerListing` aggregate stream? | M9-S2 | Lean: a lightweight Marten document projection in the Selling BC is the path of least resistance (the existing `RegisteredSeller` document is the precedent for Selling-side projections). |
| OQ-3 | Does the seller console need a distinct base path (`/seller/`) and how does the API host's SPA-fallback routing work with three apps? | M9-S1 | Lean: yes, `/seller/` base path following the `/ops/` precedent; one `MapFallbackToFile` per base path prefix. |
| OQ-4 | Should the seller console share a Tailwind preset with the bidder app (they're both public-facing, unlike the ops high-contrast layout)? | M9-S1 | Lean: if bidder and seller share a design language, a Tailwind preset in `client/shared/` serves both; evaluate at extraction time. |

---

## 8. Risk & Dependency Notes

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| 1 | **`client/shared/` extraction scope creep.** The extraction could pull in too much or too little; getting the boundary wrong costs rework in all three consumers. | Medium | Evaluate against three real consumers in M9-S1; extract only what is duplicated today; the shared package can grow in later slices. |
| 2 | **Seller-hub routing may need a Relay change.** If seller-specific notifications are not currently routed to seller-addressable groups, M9 needs a Relay backend touch beyond the budgeted Selling/Listings endpoints. | Low-Medium | Audit `src/CritterBids.Relay/` in M9-S1; if needed, the change is a handler addition (adding a `seller:{sellerId}` group send), not a new domain event. |
| 3 | **Listing-level push deduplication on the seller side.** The seller and bidder both connect to `BiddingHub` and may receive the same notifications if the seller also has the bidder app open. This is by design (same hub, same participant), but the seller console's UI must handle it gracefully. | Low | The cache-bridge pattern (invalidate + re-query) is inherently idempotent; duplicate pushes cause duplicate re-queries but not duplicate UI state. |
| 4 | **Endpoint contract design for seller queries.** The "my listings" and "my obligations" query endpoints need careful scoping — the seller should see only their own data, not other sellers'. | Low | Standard per-seller filtering by `SellerId` from the session; same anonymous-session identity the Selling BC already validates in `CreateDraftListingHandler.ValidateAsync`. |
| 5 | **Two sagas still have unenforced optimistic concurrency** (`SettlementSaga`, `PostSaleCoordinationSaga`). | Medium | Carry-forward from M8; not M9 scope unless the seller console surfaces a race. The `IRevisioned` + retry policy follow-up is a backend chore, not a seller-console dependency. |

---

## 9. Inputs and Carry-Forwards

### From M8 retro "What M9 Should Know"

> **Scope the milestone first.** ✅ This document.
>
> **The seller console is the `client/shared/` trigger.** ✅ Scoped as M9-S1.
>
> **Seller-side backend surfaces are bus-only today.** ✅ Budgeted as M9-S2/S3 backend precursor slices; endpoint surface audit in §2.
>
> **Carry the verification ladder.** ✅ Pinned in §6 (frontend slice discipline): lived backend first, live smoke per slice, extend the e2e.
>
> **First housekeeping candidates.** ✅ Both scoped: Listings `ExtendedBiddingTriggered` handler in M9-S3; cache-bridge burst-final hardening evaluated in M9-S3.

### From M8 deferred ledger (items this milestone addresses)

| Item | M9 disposition |
|---|---|
| `client/shared/` extraction | M9-S1 |
| Listings `ExtendedBiddingTriggered` handler | M9-S3 (backend precursor) |
| Cache-bridge burst-final hardening | M9-S3 (evaluate and ship or defer) |

### From M8 deferred ledger (items NOT addressed by M9)

| Item | Why not M9 |
|---|---|
| Playwright e2e in CI | Still needs full Aspire stack in Actions; re-evaluate with CI infra work |
| `IRevisioned` + retry policies for `SettlementSaga`/`PostSaleCoordinationSaga` | Backend chore, not seller-console scope |
| Settlement double-publish (saga appends AND returns via `OutgoingMessages`) | Backend chore |
| `remainingCredit` on bidder settlement outcome view | Bidder app, not seller console |
| Bidder display-name header | Bidder app, not seller console |
| Wolverine upstream handoff | Satellite JasperFx work |

---

## Document History

- **v0.1** (2026-06-13): Authored as the M9 milestone-scoping artifact, clearing the milestone-doc precondition gate for M9-S1 implementation prompts. Scope derived from: M8 milestone retrospective §"What M9 Should Know" and §"Technical Debt and Deferred Items"; narratives 004 (seller publishes/withdraws), 005 (seller watches auction close), 006 (seller fulfills obligation), 007 (seller recovers missed deadline); ADR 025 §`client/shared/` extraction plan and the M8-S7 evaluation (pattern-vs-bytes); the endpoint surface audit against `src/CritterBids.Selling/`, `src/CritterBids.Obligations/`, and `src/CritterBids.Api/Dev/DemoSeedEndpoint.cs`; and the `frontend-slice-discipline` skill. Seven slices planned: two backend precursors (endpoint exposure + housekeeping carry-forwards), one infrastructure foundation (`client/shared/` extraction + seller scaffold), three seller SPA slices (listing management, auction observation, obligation fulfillment), and an e2e + housekeeping close. Status `Planned`; M9-S1 not yet started.
