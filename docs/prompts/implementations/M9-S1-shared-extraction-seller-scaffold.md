# M9-S1: Foundation — `client/shared/` Extraction + Seller SPA Scaffold

**Milestone:** M9 ([Seller Console](../../milestones/M9-seller-console.md))
**Slice:** S1 of M9 (foundation slice; the shared extraction + third SPA scaffold)
**Narrative:** `docs/narratives/004-seller-publishes-and-withdraws-listing.md` (the seller-vantage listing lifecycle the seller console eventually renders; this slice scaffolds the SPA shell, not a rendered journey)
**Agent:** @PSA (with @UXE consulted on shared-package boundary and seller SPA scaffold)
**Estimated scope:** one PR; `client/shared/` extraction, bidder + ops migration to consume it, `client/seller/` scaffold with BiddingHub connection proof, Aspire child registration, CI extension. No seller UI beyond the scaffold proof; no backend changes.

---

## Preconditions

This prompt assumes **`docs/milestones/M9-seller-console.md` exists** (authored 2026-06-13, PR #103). Per AUTHORING.md rule 3 the milestone doc is authoritative for scope; per the milestone-doc precondition gate, M9's definition-of-done and deliverables are scoped there. If that doc does not exist when this prompt is picked up, **stop and escalate**.

This prompt also assumes the M9 scoping PR (#103) has merged to `main`. If it hasn't, either wait for the merge or confirm with the user that this branch should base off the scoping branch.

## Goal

Land the infrastructure foundation that every later M9 slice depends on, in one reviewable PR:

1. **Extract `client/shared/`** as the fourth npm-workspace member — the frontend analogue of `CritterBids.Contracts`. The extraction is evaluated against three real consumers (bidder, ops, seller) and contains only what is actually duplicated today: the SignalR provider/hook/cache-bridge pattern (parameterised by hub URL + auth configuration), the shared Tailwind v4 CSS-variable theme, and the `CatalogListingView` Zod schema (the one wire shape both the bidder and seller apps consume). Zod schemas that are app-specific (`PlaceBidResponse`, `OperationsFeedNotification`, ops board schemas) stay in their owning app.
2. **Migrate bidder and ops apps** to consume `@critterbids/shared` — remove the duplicated SignalR infrastructure and shared theme CSS from each app; import from `@critterbids/shared` instead. Both apps must remain fully functional after the migration (build, test, live smoke against Aspire).
3. **Scaffold `client/seller/`** as the fifth workspace member — the seller console's Vite + React + TypeScript (strict) shell. Follows the bidder/ops precedent: Tailwind v4 (consuming the shared theme), `@microsoft/signalr`, TanStack Router + Query, PWA from day one. Connects to `BiddingHub` (anonymous, same as bidder) using the extracted `@critterbids/shared` SignalR provider. Renders connection state as the scaffold proof — no seller UI surfaces (those are M9-S4 through M9-S6).
4. **Register the seller SPA as an Aspire child** (`:5175`) following the bidder/ops pattern.
5. **Extend CI** to cover `@critterbids/shared` and `@critterbids/seller` (build + Vitest).

This slice is the M9 analogue of M8-S1 (frontend foundation), with one critical difference: M8-S1 authored the governing ADRs; M9-S1 operates under them (ADR 013, 025, 026 all accepted). The decision density is in the extraction boundary, not in new ADRs.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M9-seller-console.md` | **Authoritative for scope.** M9 definition-of-done, the seven-slice plan, and the S1 row. §2 "client/shared/ extraction" and §7 OQ-1/OQ-3/OQ-4 are the governing constraints. |
| `CLAUDE.md` §Frontend | The current two-SPA layout; this slice updates it to three SPAs + shared. |
| `docs/decisions/025-spa-monorepo-layout.md` | ADR 025 — the workspace layout; `client/shared/` was planned here, now realized. |
| `docs/decisions/026-signalr-integration-pattern.md` | ADR 026 — the Provider + `useListen` + cache-bridge pattern being extracted. |
| `docs/decisions/013-frontend-core-stack.md` | ADR 013 — the accepted library composition the seller scaffold follows. |
| `docs/skills/frontend-slice-discipline/SKILL.md` | Governs all M9 frontend slices: read `src/` before writing, live smoke per slice. |
| `client/bidder/src/signalr/` | The bidder's SignalR integration — the extraction source. |
| `client/ops/src/signalr/` | The ops SignalR integration — the second extraction source (structurally identical pattern, different hub/auth). |
| `client/bidder/src/index.css` + `client/ops/src/index.css` | The CSS variable themes — byte-identical except for the ops projector override; extraction candidates. |
| `client/bidder/src/catalog/schema.ts` | The `catalogListingSchema` — shared between bidder (catalog) and seller (my listings); extraction candidate. |
| `src/CritterBids.AppHost/Program.cs` | Aspire child-process registration pattern to replicate for the seller SPA. |

## In scope

### 1. `client/shared/` workspace member

Create `client/shared/` as `@critterbids/shared`, the fourth workspace member (fifth counting e2e). Contents:

**SignalR integration (extracted from bidder + ops):**
- A parameterised `SignalRProvider` that takes hub URL, auth configuration (anonymous vs token-factory), and a message parser as props/generics — the bidder and ops providers share identical lifecycle management (connect, reconnect, status tracking, group management, listener fan-out) but differ in hub URL, auth, and message vocabulary
- The `useListen`, `useHubConnectionState` hooks (generic over message type)
- The cache-bridge *pattern* (the `applyHubMessage` dispatch shape) — but the actual cache-bridge mapping is app-specific (bidder invalidates `["listing", id]` and `["catalog"]`; ops invalidates `operationsKeys.all`), so each app retains its own bridge implementation that consumes the shared dispatch hook
- The `RECEIVE_MESSAGE` constant and the hub-connection builder utilities
- Test helpers: the fake `HubConnection` factory and any shared test utilities

**Shared theme CSS:**
- `client/shared/src/theme.css` containing the common shadcn/ui CSS variable definitions (`:root`, `.dark`, `@custom-variant dark`, `@theme inline`, `@layer base` with `border-color` / `outline-color` / `body` rules)
- Each app's `index.css` reduces to `@import "tailwindcss"; @import "@critterbids/shared/src/theme.css";` plus app-specific overrides (ops: `html { font-size: 18px }`)

**Shared Zod schemas:**
- `catalogListingSchema` + `CatalogListing` type — the one wire shape both bidder and seller consume (the bidder's catalog and the seller's "my listings" share the `CatalogListingView` response shape)
- App-specific schemas stay in their owning app: `placeBidResponseSchema` (bidder), `problemDetailsSchema` (bidder), ops board schemas (ops), seller-specific schemas (seller, later slices)

**Package shape:**
- `client/shared/package.json` (`@critterbids/shared`, `"type": "module"`, peer dependencies on React + Zod + `@microsoft/signalr` + `@tanstack/react-query`)
- `client/shared/tsconfig.json` extending `../tsconfig.base.json`
- Exports via `package.json` `"exports"` field so consumers import `@critterbids/shared/signalr`, `@critterbids/shared/schemas`, `@critterbids/shared/theme.css`

### 2. Bidder and ops migration

- Replace `client/bidder/src/signalr/SignalRProvider.tsx` with a thin wrapper that configures the shared provider for the BiddingHub (anonymous, `/hub/bidding`, bidder message parser, bidder cache bridge)
- Replace `client/ops/src/signalr/SignalRProvider.tsx` with a thin wrapper that configures the shared provider for the OperationsHub (token-factory auth, `/hub/operations`, `skipNegotiation`, ops message parser, ops cache bridge)
- Both apps add `"@critterbids/shared": "*"` to their `dependencies`
- Both apps' `index.css` import the shared theme and retain only app-specific overrides
- The bidder's `catalogListingSchema` import moves to `@critterbids/shared/schemas`
- All existing tests must pass after migration; the apps must build and run correctly against Aspire

### 3. `client/seller/` scaffold

- `client/seller/package.json` (`@critterbids/seller`, dependencies: `@critterbids/shared`, React, Zod, `@microsoft/signalr`, `@tanstack/react-query`, `@tanstack/react-router`, shadcn utilities)
- `client/seller/vite.config.ts`: `base: "/seller/"`, port `:5175`, proxy to API host, PWA manifest (name: "CritterBids Seller Console", `start_url: "/seller/"`)
- `client/seller/tsconfig.json` extending `../tsconfig.base.json`, path alias `@` → `./src`
- `client/seller/src/index.css`: `@import "tailwindcss"; @import "@critterbids/shared/src/theme.css";` (same dark theme as bidder; no projector override)
- `client/seller/index.html`: `class="dark"` on root, script entry
- Minimal app shell: TanStack Router with basename `/seller`, QueryClientProvider, the shared SignalRProvider configured for BiddingHub (anonymous, same as bidder), a connection-state indicator as the scaffold proof
- Session management: reuse the same `ParticipantId` session pattern as the bidder app (`POST /api/participants/session`)
- No seller-specific UI surfaces (listing management, auction observation, obligation tracking are M9-S4 through M9-S6)

### 4. Aspire child registration

- `src/CritterBids.AppHost/Program.cs`: add `builder.AddViteApp("seller", "../../client/seller")` with `.WithHttpEndpoint(port: 5175, name: "http")`, `.WithEnvironment("CRITTERBIDS_API_URL", ...)`, `.WaitFor(api)` — following the bidder/ops pattern exactly

### 5. Workspace + CI updates

- `client/package.json`: add `"shared"` and `"seller"` to the `workspaces` array
- CI `.github/workflows/ci.yml`: extend the `frontend` job to include `@critterbids/shared` (build) and `@critterbids/seller` (build + Vitest)
- `CLAUDE.md` §Frontend: update to reflect three SPAs + `client/shared/` as a realized member (no longer "planned, deferred")

### 6. Retrospective

`docs/retrospectives/M9-S1-shared-extraction-seller-scaffold-retrospective.md` — written last; carries the `**Prompt:**` header line and the `## Spec delta — landed?` paragraph.

## Explicitly out of scope

- **Seller UI surfaces.** No listing management, auction observation, obligation tracking, or any seller-specific views. Those are M9-S4 through M9-S6. The scaffold renders connection state only.
- **Backend changes.** No new endpoints, no handler changes, no Relay modifications. M9-S2/S3 are the backend precursor slices. OQ-1 is resolved (no Relay change needed), so S1 has zero backend touches.
- **New ADRs.** The governing ADRs (013, 025, 026) are accepted; S1 operates under them.
- **Seller-specific Zod schemas.** The seller app's domain-specific schemas (if any) land with the UI slices that need them.
- **The `client/shared/` package published to npm.** It is an internal workspace dependency resolved via npm workspaces symlinks. No npm publish.
- **Production static-file serving middleware** for the seller app (same deferral as M8).
- **The ops projector font-size override moving to shared.** It stays in the ops `index.css` as an app-specific override — the seller app does not need it.
- **Resolving OQ-2** (my-listings query shape). That resolves in M9-S2 when the endpoint is built.
- **`docs/STATUS.md` regeneration.** This can happen in M9-S7 or as a housekeeping chore.

## Conventions to pin or follow

- **Frontend stack:** ADR 013 (accepted) — TypeScript strict, Zod 4, TanStack Query + Router, Tailwind v4 + shadcn/ui, `@microsoft/signalr`, Vitest, PWA from day one. The seller scaffold follows the exact same library composition as bidder and ops.
- **SPA layout:** ADR 025 — workspace member at `client/seller/`, Vite dev proxy, base path `/seller/`. `client/shared/` is the fourth member per ADR 025's original plan, now realized.
- **SignalR integration:** ADR 026 — the Provider + `useListen` + cache-bridge pattern, now extracted to `client/shared/` as the shared integration surface.
- **Frontend slice discipline:** `docs/skills/frontend-slice-discipline/SKILL.md` — read `src/` before writing, live smoke per slice against the running Aspire stack.
- **The shared package is the frontend `CritterBids.Contracts`:** extract only what is genuinely shared today; it grows in later slices as real need emerges. Don't speculatively extract.

## OQ resolutions (from M9 milestone §7)

These four open questions are resolved here, by source-code inspection, before the session runs:

### OQ-1: Seller-hub routing — RESOLVED, no changes needed

The `BiddingHub` already routes seller-specific notifications. Source-code audit of `src/CritterBids.Relay/Handlers/`:

| Notification | Target group | Handler |
|---|---|---|
| `SellerPayoutIssued` | `bidder:{message.SellerId}` | `SellerPayoutIssuedHandler.cs:33` |
| `ObligationFulfilled` | `bidder:{message.SellerId}` (+ `bidder:{message.WinnerId}`) | `ObligationsRelayHandlers.cs:92` |
| `TrackingInfoProvided` | `bidder:{message.WinnerId ?? message.SellerId}` | `ObligationsRelayHandlers.cs:43` |
| `SettlementCompleted` | `bidder:{message.WinnerId}` (buyer, not seller — but `SellerPayoutIssued` covers the seller) | `SettlementCompletedHandler.cs:40` |
| Bid/listing-level activity | `listing:{listingId}` groups | `BidPlacedHandler.cs`, `ListingSoldHandler.cs`, `AuctionsBiddingHandlers.cs` |

The seller connects to `BiddingHub` as a participant (their `ParticipantId` IS their `SellerId`), joins `bidder:{participantId}` for personal pushes and `listing:{listingId}` for listing-level activity. No new groups, no Relay handler changes, no backend touch for S1.

### OQ-3: Base path — RESOLVED, `/seller/` following the `/ops/` precedent

The seller app uses `base: "/seller/"` in `vite.config.ts`, matching the ops app's `base: "/ops/"` (ADR 025). TanStack Router basename is `/seller`. The PWA manifest's `start_url` is `"/seller/"`. The Aspire child runs on `:5175` (ops is `:5174`, bidder is `:5173`). Production multi-SPA fallback routing remains deferred (same as M8).

### OQ-4: Tailwind preset sharing — RESOLVED, extract shared CSS-variable theme

The bidder and ops `index.css` files are **byte-identical** in their CSS variable definitions (`:root`, `.dark`, `@custom-variant dark`, `@theme inline`, `@layer base`). The only difference is the ops app's `html { font-size: 18px }` projector-legibility override. In Tailwind v4 (CSS-native, no `tailwind.config.js`), the "preset" is a CSS file. Extract the common block to `client/shared/src/theme.css`; each app imports it. The seller app shares the bidder's design language (dark theme, standard font-size). The ops projector override stays in `client/ops/src/index.css` as an app-specific layer.

### OQ-2: My-listings query shape — deferred to M9-S2

This resolves in the backend precursor slice that builds the seller-scoped listing query endpoint. The milestone doc's lean (a lightweight Marten document projection in the Selling BC) stands.

## Spec delta

Per ADR 020: this slice's spec consequence is the **realization of ADR 025's `client/shared/` member** — moving it from "planned, deferred to the seller-console milestone" to a realized, consumed package. ADR 026's SignalR integration pattern moves from app-local code to a shared, parameterised surface. A secondary consequence is the first seller-console code surface in the repo — a scaffold proof connecting to the `BiddingHub`, not a rendered journey. No Moment of narrative 004 is implemented; no domain data is rendered. The retro's `## Spec delta — landed?` paragraph confirms: `client/shared/` exists and is consumed by all three SPAs; the bidder and ops apps build and test green after migration; the seller scaffold connects to the BiddingHub and renders connection state.

## Acceptance criteria

- [ ] `client/shared/` exists as `@critterbids/shared` workspace member with the extracted SignalR provider/hooks, shared theme CSS, and `catalogListingSchema`
- [ ] `client/shared/package.json` declares peer dependencies (React, Zod, `@microsoft/signalr`, `@tanstack/react-query`); `tsconfig.json` extends `../tsconfig.base.json`
- [ ] `client/bidder/` consumes `@critterbids/shared` — the duplicated SignalR infrastructure is removed; `catalogListingSchema` imports from shared; `index.css` imports the shared theme
- [ ] `client/ops/` consumes `@critterbids/shared` — the duplicated SignalR infrastructure is removed; `index.css` imports the shared theme; the projector font-size override remains app-local
- [ ] `client/bidder/` builds (`npm run build`), tests pass (`npm run test`), and runs correctly against Aspire (live smoke: catalog loads, BiddingHub connects, bids can be placed)
- [ ] `client/ops/` builds, tests pass, and runs correctly against Aspire (live smoke: auth gate works, OperationsHub connects, board views populate)
- [ ] `client/seller/` exists as `@critterbids/seller` workspace member at the ADR-025 layout
- [ ] `client/seller/vite.config.ts` has `base: "/seller/"`, port `:5175`, dev proxy to API host, PWA manifest
- [ ] `client/seller/` has TypeScript strict, Tailwind v4 (shared theme), TanStack Router (basename `/seller`), TanStack Query, and the shared SignalR provider configured for BiddingHub (anonymous)
- [ ] The seller scaffold renders BiddingHub connection state as the proof (connects, shows status, reconnects)
- [ ] `src/CritterBids.AppHost/Program.cs` registers the seller SPA as an Aspire child on `:5175`
- [ ] `client/package.json` workspaces array includes `"shared"` and `"seller"`
- [ ] Clean-checkout `npm install` + `npm run build` succeeds on all workspace members (bidder, ops, seller, shared)
- [ ] CI `.github/workflows/ci.yml` frontend job covers `@critterbids/shared` (build) and `@critterbids/seller` (build + Vitest)
- [ ] `CLAUDE.md` §Frontend updated: three SPAs + `client/shared/` realized (no longer "planned, deferred")
- [ ] Existing .NET build + test baseline unchanged — this slice adds no backend code
- [ ] No new ADRs; no backend changes; no seller UI surfaces beyond the scaffold proof
- [ ] `docs/retrospectives/M9-S1-shared-extraction-seller-scaffold-retrospective.md` written with the `**Prompt:**` header and `## Spec delta — landed?` paragraph
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

- **Shared SignalR provider parameterisation shape.** The bidder provider is anonymous with group management (`watchListing`, `unwatchListing`, `JoinBidderGroup`); the ops provider is token-authenticated with `skipNegotiation` and no group management. The shared provider must accommodate both — likely via a configuration object or generic type parameter. The right abstraction should emerge from the extraction; if it doesn't feel clean, flag and discuss whether a shared factory function (rather than a shared component) is the better extraction unit.
- **Shared package entry points.** Whether `@critterbids/shared` uses `package.json` `"exports"` for sub-path imports (`@critterbids/shared/signalr`, `@critterbids/shared/schemas`, `@critterbids/shared/theme.css`) or a barrel `index.ts`. Lean toward `"exports"` for tree-shaking and clear boundaries, but confirm against the TypeScript project-references setup.
- **Test migration scope.** The bidder and ops `SignalRProvider.test.tsx` files test the app-specific provider behavior (bidder groups, ops token auth). When the core provider moves to shared, decide: (a) shared gets the lifecycle tests, apps keep only the configuration-specific tests; or (b) shared has no tests and the app tests cover the full stack. Lean toward (a).
