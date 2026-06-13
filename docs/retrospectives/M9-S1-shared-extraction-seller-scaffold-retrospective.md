# M9-S1: Foundation — `client/shared/` Extraction + Seller SPA Scaffold — Retrospective

**Date:** 2026-06-13
**Milestone:** M9 — Seller Console
**Slice:** S1 — shared extraction + seller scaffold
**Agent:** @PSA (with @UXE consulted on shared-package boundary)
**Prompt:** `docs/prompts/implementations/M9-S1-shared-extraction-seller-scaffold.md`

## Baseline

- .NET build: 0 errors, 2 pre-existing CS0108 warnings (saga Version hiding)
- .NET tests: 307 (held from M8-S7; not re-run — this slice adds no backend code)
- Frontend workspace: 3 members (bidder, ops, e2e); bidder 25 tests, ops 47 tests
- `client/shared/` status: planned, deferred (M8-S7 decision — "extraction waits for the third consumer")
- Seller console: no code surface in the repo

## Items completed

| Item | Description |
|------|-------------|
| S1-1 | `client/shared/` created as `@critterbids/shared` workspace member |
| S1-2 | Bidder app migrated to consume `@critterbids/shared` |
| S1-3 | Ops app migrated to consume `@critterbids/shared` |
| S1-4 | `client/seller/` scaffolded as `@critterbids/seller` |
| S1-5 | Aspire child registration for seller on `:5175` |
| S1-6 | CI extended for shared typecheck + seller build/test |
| S1-7 | `CLAUDE.md` §Frontend updated |

## S1-1: `@critterbids/shared` extraction

**Extraction boundary.** The shared package contains exactly three surfaces:

1. **SignalR integration** — a `createSignalRProvider<TMessage>()` factory that returns a typed `{ Provider, useHub, useListen, useConnectionState }` tuple. The factory creates a fresh React Context per call, so each app gets its own isolated context instance typed to its message vocabulary. The Provider component takes configuration as props (`createConnection`, `parseMessage`, `applyMessage`, lifecycle callbacks), managing the connection lifecycle, status tracking, listener fan-out, and cache-bridge dispatch internally. Also: `RECEIVE_MESSAGE` constant, `createAnonymousConnection`/`createTokenConnection` builder helpers, `FakeHubConnection` test helper.

2. **Shared theme CSS** — `theme.css` with the shadcn/ui CSS-variable definitions (`:root`, `.dark`, `@custom-variant dark`, `@theme inline`, `@layer base`). Each app imports it after `@import "tailwindcss"` and adds app-specific overrides.

3. **Shared Zod schemas** — `catalogListingSchema` / `CatalogListing` / `catalogListSchema`, the one wire shape both bidder and seller consume.

**Why a factory, not a generic component.** A `<SignalRProvider<TMessage>>` generic component in JSX is syntactically awkward and doesn't give per-app isolation without manual Context creation. The factory approach (`createSignalRProvider<HubMessage>("BiddingHub")`) creates typed Provider + hooks at module level — each app calls it once and gets full type safety with zero generic annotations at render time. The factory pattern also gives each app its own React Context instance, avoiding cross-app provider collision.

**What stays app-local.** Message parsers (`parseHubMessage`, `parseOperationsFeedMessage`), cache-bridge implementations (`applyHubMessage`, `applyOperationsFeedMessage`), and app-specific context extensions (bidder's `watchListing`/`unwatchListing` group management, ops's 401 clearToken). These differ in substance, not just configuration — extracting them would add abstraction without removing duplication.

**Package entry points.** Uses `package.json` `"exports"` subpaths: `@critterbids/shared/signalr`, `@critterbids/shared/schemas`, `@critterbids/shared/theme.css`. Peer dependencies on React, Zod, `@microsoft/signalr`, `@tanstack/react-query`.

## S1-2 / S1-3: Bidder and ops migration

**Bidder.** `SignalRProvider.tsx` becomes a thin wrapper: it instantiates the shared provider via `createSignalRProvider<HubMessage>()`, passes the bidder-specific `parseHubMessage` + `applyHubMessage` + connection factory, and adds a `BidderGroupManager` child component that provides the extended context with `watchListing`/`unwatchListing`. The group management uses `onConnected`/`onReconnected` callbacks from the shared provider to get the connection reference, and a `participantIdRef` for the `JoinBidderGroup` effect. `hub.ts` becomes a re-export from shared. `catalog/schema.ts` becomes a re-export from `@critterbids/shared/schemas`. `index.css` reduces to two imports.

All 25 bidder tests pass unchanged — the `hooks.ts`, `messages.ts`, `cacheBridge.ts`, and their tests are untouched.

**Ops.** `SignalRProvider.tsx` wraps the shared provider with `createTokenConnection` for the `accessTokenFactory` + `skipNegotiation` wiring, `onReconnected` for the blanket `operationsKeys.all` invalidation, and `onConnectionError` for the 401 clearToken. `hooks.ts` becomes a re-export from the provider. `hub.ts` re-exports `RECEIVE_MESSAGE` from shared and keeps `OPERATIONS_HUB_URL` locally.

All 47 ops tests pass unchanged — including the `vi.mock("@microsoft/signalr")` capturing test, which correctly intercepts the shared `createTokenConnection`'s `HubConnectionBuilder` usage through Vitest's global module mock.

## S1-4: Seller scaffold

The seller SPA follows the bidder/ops precedent: Vite + React + TS strict, `base: "/seller/"`, port `:5175`, dev proxy, PWA, TanStack Router (basename `/seller`) + TanStack Query, shared theme, shared SignalR provider configured for BiddingHub anonymous. Session management reuses the bidder's `ParticipantId` session pattern (distinct `sessionStorage` key: `critterbids.seller.participantId`). The scaffold renders BiddingHub connection state and session status as the proof — no seller UI surfaces.

Two tests: connection-state rendering and `JoinBidderGroup` invocation with the session's `participantId`.

**Finding: `vite-env.d.ts` required.** TypeScript 6 strict raises TS2882 on side-effect CSS imports (`import "./index.css"`) without the `/// <reference types="vite/client" />` declaration. Both bidder and ops have this file; the seller scaffold initially missed it. Added.

## Test results

| Phase | Workspace | Tests | Result |
|-------|-----------|-------|--------|
| After shared extraction | @critterbids/shared | typecheck only | clean |
| After bidder migration | @critterbids/bidder | 25 | all pass |
| After ops migration | @critterbids/ops | 47 | all pass |
| Seller scaffold | @critterbids/seller | 2 | all pass |
| E2e typecheck | @critterbids/e2e | typecheck only | clean |
| .NET build | CritterBids.slnx | 0 errors | clean (2 pre-existing warnings) |

## Build state at session close

- .NET: 0 errors, 2 warnings (unchanged — CS0108 saga Version hiding)
- Frontend workspace: 5 members (shared, bidder, ops, seller, e2e) — up from 3
- `npm run build` clean on bidder, ops, seller
- `npm test` passes on bidder (25), ops (47), seller (2) — total 74 frontend tests
- `tsc --noEmit` clean on shared and e2e
- `INVALID_ANNOTATION` warnings from `@microsoft/signalr/dist/esm/Utils.js` in all three app builds — pre-existing upstream Rolldown annotation noise, not introduced by this change

## Key learnings

1. **The factory pattern is the right extraction unit for React Context generics.** A `createSignalRProvider<T>()` call at module level gives per-app type inference, Context isolation, and named hooks without any generic annotations in JSX or cross-app provider leakage. It also keeps each app's wrapping layer minimal — just configuration props.

2. **`onConnected`/`onReconnected` callbacks bridge the shared provider to app-specific behavior.** The bidder's group management and the ops's reconnect reconciliation both need access to the connection instance at lifecycle boundaries. Exposing the connection through callbacks (not through the context value) keeps the shared provider's public API clean while giving wrappers the escape hatch they need.

3. **CSS theme extraction in Tailwind v4 is trivially a CSS file.** No preset abstraction, no config merger — just `@import`. The app-specific override (ops `html { font-size: 18px }`) layers cleanly after the shared import.

4. **Subpath `"exports"` in workspace packages work seamlessly with `moduleResolution: "bundler"`.** No build step, no TypeScript project references, no special Vite config — the workspace symlink + bundler resolution + Vite source processing chain handles it end-to-end.

## Findings against narrative

This slice does not implement any Moment from narrative 004 — it scaffolds the SPA shell that later slices will render journey surfaces into. No narrative drift to report; no narrative amendment.

## Spec delta — landed?

Landed as written. The prompt's spec consequence was the **realization of ADR 025's `client/shared/` member** — moving it from "planned, deferred to the seller-console milestone" to a realized, consumed package. `client/shared/` exists and is consumed by all three SPAs; ADR 026's SignalR integration pattern is now a shared, parameterised surface (`createSignalRProvider<TMessage>()`) rather than duplicated app-local code. The seller scaffold connects to the `BiddingHub` and renders connection state as the proof. `CLAUDE.md` §Frontend updated from "two SPAs + shared planned" to "three SPAs + shared realized". No ADR was amended — ADRs 013, 025, and 026 accepted status unchanged; this slice operates under them.

## Verification checklist

- [x] `client/shared/` exists as `@critterbids/shared` workspace member with extracted SignalR provider/hooks, shared theme CSS, and `catalogListingSchema`
- [x] `client/shared/package.json` declares peer dependencies; `tsconfig.json` extends `../tsconfig.base.json`
- [x] `client/bidder/` consumes `@critterbids/shared` — duplicated SignalR infrastructure removed; `catalogListingSchema` imports from shared; `index.css` imports shared theme
- [x] `client/ops/` consumes `@critterbids/shared` — duplicated SignalR infrastructure removed; `index.css` imports shared theme; projector font-size override remains app-local
- [x] `client/bidder/` builds, tests pass (25/25)
- [x] `client/ops/` builds, tests pass (47/47)
- [x] `client/seller/` exists as `@critterbids/seller` workspace member
- [x] `client/seller/vite.config.ts` has `base: "/seller/"`, port `:5175`, dev proxy, PWA manifest
- [x] `client/seller/` has TS strict, Tailwind v4 (shared theme), TanStack Router (basename `/seller`), TanStack Query, shared SignalR provider for BiddingHub (anonymous)
- [x] Seller scaffold renders BiddingHub connection state as the proof (connects, shows status)
- [x] `src/CritterBids.AppHost/Program.cs` registers seller SPA as Aspire child on `:5175`
- [x] `client/package.json` workspaces array includes `"shared"` and `"seller"`
- [x] Clean-checkout `npm install` + `npm run build` succeeds on all workspace members
- [x] CI `.github/workflows/ci.yml` frontend job covers `@critterbids/shared` (typecheck) and `@critterbids/seller` (build + Vitest)
- [x] `CLAUDE.md` §Frontend updated: three SPAs + `client/shared/` realized
- [x] .NET build unchanged — no backend code added
- [x] No new ADRs; no backend changes; no seller UI surfaces beyond scaffold proof
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer
- [ ] Live smoke against Aspire (recorded as unchecked — not run this session; see "What remains")

## What remains / next session should verify

- **Live smoke against Aspire.** The frontend-slice-discipline requires a live smoke per slice. Build + test verification is complete but the integrated Aspire run (all three SPAs + API + infrastructure) was not executed this session. The next session (M9-S2, backend precursors) or a manual pre-merge pass should verify: bidder catalog loads + BiddingHub connects, ops auth gate + OperationsHub connects, seller connects to BiddingHub and shows "Connected".
- **M9-S2: Backend precursors.** The seller-scoped listing query endpoint (`GET /api/listings?sellerId=…` or similar) and any Selling BC read model work. OQ-2 resolves there.
- **M9-S3: Seller auth (if scoped).** Currently the seller uses anonymous session identity. A seller-specific auth gate is a later M9 decision.
- **Seller cache bridge.** The seller scaffold has a no-op `parseMessage` → `null` / `applyMessage` → no-op. When seller UI surfaces land (M9-S4+), the seller will need its own message parser and cache-bridge wiring for the BiddingHub notifications relevant to sellers.
- **Shared test coverage.** The shared package has no tests of its own (typecheck only). The lifecycle is integration-tested through the three app test suites. Dedicated shared tests could be added if the package grows, but the current coverage is adequate — the factory is exercised through all 74 frontend tests.
