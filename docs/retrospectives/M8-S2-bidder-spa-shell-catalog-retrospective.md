# M8-S2: Bidder SPA Shell + Catalog — Retrospective

**Date:** 2026-06-08
**Milestone:** M8 — React Frontend SPAs
**Slice:** S2 — Bidder SPA Shell + Catalog (promote the connection proof to a routed shell; render the public catalog + listing detail read-only; establish the anonymous session)
**Agent:** Claude Code (Opus 4.8, interactive mode) as @PSA (with @UXE-vantage shell/layout decisions)
**Prompt:** `docs/prompts/implementations/M8-S2-bidder-spa-shell-catalog.md`
**Branch:** `docs/m8-s2-prompt` (prompt + implementation + ADR amendment + this retro in one PR off `main`)

## Baseline

- M8-S1 merged: ADR-013 `Accepted`, ADR-025 `Accepted`, the `BiddingHub` connection proof at `client/bidder/` building clean (Vite 8 + React 19 + TS strict, Tailwind v4 via `@tailwindcss/vite`, `@microsoft/signalr` pinned, `useBiddingHub` + a connection-state pill).
- Backend feature-complete at M7 close (281 tests). M8-S2 originates **zero** backend work.
- The routing library was pinned to **TanStack Router** by the user at prompt-authoring time (the earlier "session decides" framing was overridden before the session ran), so this slice wired it rather than evaluating candidates.

## Items completed

| Item | Description |
|------|-------------|
| S2.1 | Promoted `client/bidder/` in place: routed app shell (header + session indicator + live-connection indicator + `<Outlet/>`), TanStack Router **code-based** route tree (`/`, `/listing/$id`, not-found). `useBiddingHub` retained, mounted in the persistent shell so the hub stays connected/observable across navigation — no bid data wired. |
| S2.2 | Resolved ADR-013's routing Deferred Question → **TanStack Router**; amended ADR-013 (inline resolution + dated Document-History row). Other four Deferred Questions stay parked. |
| S2.3 | Catalog + listing-detail data layer: a Zod schema mirroring `CatalogListingView` at the wire boundary; TanStack Query hooks over `GET /api/listings` (list, empty-state) and `GET /api/listings/{id}` (detail, distinct 404 not-found). Read-only, fetch-authoritative. |
| S2.4 | Anonymous session bootstrap: one `POST /api/participants/session` on load, id held in `sessionStorage`, rendered as established/**anonymous** — display-name header deferred. Failed POST does not block catalog reads. |
| S2.5 | Tailwind v4 + shadcn/ui baseline (`components.json`, `cn`, Card/Badge/Skeleton copied in as needed) + `vite-plugin-pwa` (manifest + service-worker registration, app-shell precache). |
| S2.6 | Vitest + RTL baseline; 7 tests (Zod accept/reject/array; catalog list + empty state). |
| S2.7 | This retrospective. |

## S2.2: Routing — TanStack Router, code-based

The user pinned TanStack Router. The remaining choice was **code-based vs file-based** routing. With the bidder app topping out around 3–4 routes across all of M8, code-based composition (`createRootRoute`/`createRoute`/`createRouter`) needs no `@tanstack/router-plugin` and emits **no generated `routeTree.gen.ts`** — a cleaner, more reviewable diff with no codegen artifact to commit or gitignore. The ADR-013 amendment records both the library resolution and this sub-choice, and notes a later slice may migrate to file-based routing if the route count grows. `getRouteApi("/listing/$id")` reads typed params in the detail page without importing the route object, avoiding a router⇄page import cycle.

## Findings (lived code vs the milestone surface table)

Two findings were surfaced at prompt-authoring time and confirmed in code during the slice:

1. **Display-name header deferred (user-confirmed).** `POST /api/participants/session` returns only the participant id (`CreationResponse<Guid>`); the generated display name lives in the `ParticipantSessionStarted` event and is exposed by **no** anonymous read endpoint (Participants BC has only `POST /session` and `POST /{id}/register-seller`). Surfacing it would require a backend change M8 forbids, so the session renders as "Anonymous bidder." The id is held (`sessionStorage`) as the token a later bidding slice will carry. **Carry-forward:** a future slice that renders the name needs a backend read path — either enriching the session response or a `GET /api/participants/{id}` — flagged here so it is not silently dropped.
2. **Detail binds to `CatalogListingView`, not a `ListingDetailView`.** `GET /api/listings/{id}` returns the **same** `CatalogListingView` the list returns (the milestone surface table named a `ListingDetailView` that does not exist in the lived backend). The Zod schema and both routes bind to `CatalogListingView`; no client-side shape was invented.

Two further findings emerged from the 2026 toolchain during implementation:

3. **Zod resolved to v4.** In Zod 4 the string-format validators (`.uuid()`, …) moved off `z.string()`. The format precision is not load-bearing for server-issued ids — the boundary's job is catching type/shape drift — so guid fields use `z.string()`, valid across Zod 3 and 4. (The schema still rejects wrong-typed and missing required fields, proven by tests.)
4. **`baseUrl` is deprecated** in the installed TypeScript (errors as TS6→7 forward-deprecation). The `@/*` path alias works under `moduleResolution: "bundler"` **without** `baseUrl` (resolved relative to the tsconfig), so `baseUrl` was removed.

## shadcn/ui baseline — authored, not CLI-generated

The shadcn baseline (`components.json`, the `cn` util, the Tailwind-v4 CSS-variable theme with `@theme inline`, and the Card/Badge/Skeleton primitives) was authored directly rather than via the interactive `shadcn init`/`add` CLI, which prompts and fetches from a registry — fragile in a non-interactive session. The produced artifacts are the standard "new-york" shadcn output, so a future `npx shadcn add <component>` will resolve against the committed `components.json` and slot new primitives in normally. Components were copied in **only** as the shell/catalog/detail needed them (no bulk catalog scaffold).

## Test results

| Phase | Frontend build | Frontend tests | Backend |
|-------|----------------|----------------|---------|
| Session open (M8-S1 close) | proof builds | none | 281 (unchanged) |
| After all S2 items | `npm run build` **exit 0**, `dist/` + `sw.js` + `manifest.webmanifest` (7 precache entries) | `vitest run` → **7 passed / 2 files** | 281 (definitionally unchanged) |

Backend tests were **not re-run**: this slice touches **zero** `.cs`/`.csproj`/`.slnx` files — `client/` is outside `CritterBids.slnx` — so the suite is definitionally unaffected and requires Docker infra. `tsc --noEmit` (strict, including `*.test.tsx`) passes. The two `[INVALID_ANNOTATION]` build warnings are the known cosmetic `@microsoft/signalr` vendor-file `/*#__PURE__*/` notices documented at S1 — upstream, non-fatal, build exits 0. The `Window.scrollTo` notices in the test run are jsdom not implementing a method TanStack Router calls on navigation — harmless.

## Build state at session close

- `npm run build --workspace @critterbids/bidder` → **exit 0**; `dist/index.html` + hashed JS/CSS + PWA `sw.js`/`manifest.webmanifest` emitted.
- TypeScript **strict** on; `tsc --noEmit` passes across app + test code.
- Backend `.cs`/`.csproj`/`.slnx` changed: **0**. API-host (`Program.cs`) changes: **0**.
- `node_modules/` / `dist/` tracked by git: **0** (`client/.gitignore`).
- New core-stack libraries outside the ADR-013 accepted set: **0** (added: TanStack Router, TanStack Query, Zod, shadcn deps, `vite-plugin-pwa`, Vitest/RTL — all in-set or the resolved routing pick).

## Key learnings

1. **Test files are in the production type-check.** `tsconfig.json` `include: ["src"]` means `*.test.tsx` is checked by `tsc --noEmit` in the build script — a type error in a test breaks `npm run build`, not just `vitest`. Good discipline, but the tests must compile under `strict` + `noUnusedLocals` (hence the `void unusedVar` destructure idiom).
2. **An isolated test router beats mounting the real shell.** Rendering `CatalogPage` under a memory-history router that is *not* the real `AppShell` root keeps SignalR (the connection indicator) and the session POST out of jsdom, making the catalog test focused and deterministic — while a stub `/listing/$id` route keeps the card's typed `Link` resolvable at runtime.
3. **Acceptance-time verification keeps paying out (S1's lesson, again).** Two silent breakers — Zod 4's moved string formats and TypeScript's `baseUrl` deprecation — would each have failed the build had they been assumed from training-data muscle memory. Checking the *installed* versions, not the remembered APIs, caught both.
4. **Findings discipline at prompt-authoring time prevented mid-slice scope creep.** The display-name gap and the `CatalogListingView`-as-detail-shape were surfaced and decided *before* the session ran, so the slice rendered anonymous and bound to the real shape without stopping to escalate or quietly widening into a backend change.

## Findings against narrative

Anchored to `docs/narratives/001-bidder-wins-flash-auction.md` (Moments 1–2) and `docs/narratives/003-bidder-starts-anonymous-session.md`. **Moment 2 lands in full** — the public catalog and a single listing's detail render read-only from the live `[AllowAnonymous]` endpoints. **Moment 1 lands partially** — the anonymous session is *established* and held, but its generated display name is **deferred** (no read path surfaces it; backend untouched), so narrative 003's "see your minted name" beat is not yet visible. No live-bidding Moment (3–7) is implemented — that is M8-S3 (which also authors ADR-014). This is `document-as-partial`: narratives 001/003 gain real rendered Moments but are not fully discharged; the display-name beat is a recorded carry-forward, not a silent omission.

## Spec delta — landed?

**Landed as written.** Per the prompt's `## Spec delta` (ADR 020): (1) **narrative 001 Moment 2 is rendered** in the bidder SPA (catalog + listing detail over the real endpoints) and **Moment 1 landed partially** (anonymous session established and held; display-name header explicitly deferred and recorded as carry-forward — narrative 003 anchored to the same partial state). (2) **ADR-013's routing Deferred Question is resolved → TanStack Router**, amended in place with rationale and a dated Document-History row; the stack ADR's parked-question count drops from five to four. No new ADR was authored (ADR-014 remains M8-S3). The code-level consequence — the bidder app's promotion from connection proof to a routed shell with a Zod-validated TanStack Query data layer and PWA wiring — exists, builds clean (`exit 0`), and is covered by 7 passing tests. The "PWA from day one" carry-forward from S1 was discharged on this first real shell.

## Verification checklist (prompt acceptance criteria)

- [x] `client/bidder/` is a routed app **using TanStack Router** (catalog `/`, listing-detail `/listing/$id`, not-found); builds (`npm run build` exit 0, `dist/` emitted) and type-checks under strict
- [x] ADR-013 amended: routing Deferred Question **resolved → TanStack Router** with reason + dated Document-History row; other four remain parked
- [x] Catalog route fetches `GET /api/listings` via TanStack Query, Zod-validates against `CatalogListingView`, renders listings, renders an **empty state on `[]`** (test-proven)
- [x] Listing-detail route fetches `GET /api/listings/{id}`, renders on 200, renders a **not-found state on 404** (distinct `ListingNotFoundError`); loading + error states on both
- [x] One `POST /api/participants/session` on load; participant id held (`sessionStorage`); rendered **established/anonymous, no display-name header**; failed POST does not block catalog
- [x] `useBiddingHub` retained + connection state visible in shell; **no bid feed / placement / hub-push-to-query bridge**
- [x] `vite-plugin-pwa` wired (manifest + SW registration); app-shell precache only; no offline data caching
- [x] Tailwind v4 utilities in use; shadcn/ui initialized with only the components used (no bulk scaffold)
- [x] Vitest + RTL configured; Zod schema + catalog list/empty-state covered; **no Playwright e2e**
- [x] **Zero** backend touch (`0` `.cs`/`.csproj`/`.slnx`/`Program.cs` changes); `dotnet build` baseline definitionally unchanged
- [x] No `client/ops/`, no `OperationsHub`, no ADR-014, no settlement view, no display-name backend workaround
- [x] This retrospective written with the `**Prompt:**` header + `## Spec delta — landed?` paragraph + recorded router pick + display-name carry-forward
- [x] No commit to `main`; PR off `main`; no `Co-Authored-By` trailer

## What remains / next session should verify

- **Live verification against a running API host was not performed this session** (no Aspire host started). The data layer is unit-tested with mocked `fetch`; a human/next-session smoke against `dotnet run --project src/CritterBids.AppHost --launch-profile http` should confirm the camelCase JSON keys, the `CreationResponse` `Location` header, and `/hub/bidding` connectivity through the Vite proxy end to end. (Casing is the one runtime assumption the build cannot prove.)
- **Display-name header (carry-forward).** Needs a backend read path (enrich the session response, or a `GET /api/participants/{id}`). Recommend deciding its owning slice when narrative 003's "minted name" beat is scheduled.
- **ADR-014 (SignalR integration pattern)** — M8-S3, when `BiddingHub` data is first bridged into the TanStack Query cache (Provider + `useListen` + cache bridge + message-type discriminator). The data layer was kept fetch-authoritative so that bridge slots in without a render-the-payload rewrite.
- **PNG PWA icons.** The manifest references a single SVG icon; Lighthouse installability prefers 192/512 PNGs. Cosmetic, deferrable to a polish/housekeeping slice.
- **Frontend CI job** — still unwired (`project_frontend_ci_not_wired`); `client/**` changes skip all jobs via the path filter. Earmarked M8-S7. This PR rides that gap.
- **STATUS.md** is stale (frozen at M7 close, says "M8 not started"); regenerate at an M8 housekeeping point.
