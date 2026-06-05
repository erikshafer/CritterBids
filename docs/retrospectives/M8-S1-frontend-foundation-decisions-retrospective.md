# M8-S1: Frontend Foundation Decisions — Retrospective

**Date:** 2026-06-04
**Milestone:** M8 — React Frontend SPAs
**Slice:** S1 — Frontend Foundation Decisions (ADR-013 acceptance + ADR-025 + BiddingHub connection proof)
**Agent:** @PSA (with @UXE consulted on the core-stack acceptance, @DOE on the SPA layout ADR)
**Prompt:** `docs/prompts/implementations/M8-S1-frontend-foundation-decisions.md`

## Baseline

- Build clean at session open: `dotnet build CritterBids.slnx` → **0 errors / 0 warnings**; **281 tests** across 10 backend test projects (M7 close).
- **No frontend code surface existed.** `client/` did not exist; this slice introduces the first frontend code in the repo.
- ADR-013 (Frontend Core Stack) status **Proposed** since 2026-04-19; next unreserved ADR number **025**.
- **Precondition gap (resolved as a precursor this session):** the prompt's Preconditions gate assumes `docs/milestones/M8-frontend-spas.md` exists; it did not (milestones stopped at M7). Per the gate, work **stopped and escalated** before any slice deliverable. The user directed authoring the M8 milestone doc first; it was authored as a milestone-scoping precursor, then this slice ran against it. (See "Findings" / "What remains".)

## Items completed

| Item | Description |
|------|-------------|
| (pre) | M8 milestone doc authored (`docs/milestones/M8-frontend-spas.md`) + index row — milestone-scoping precursor, unblocking the Preconditions gate |
| S1.1 | ADR-013 accepted (`Proposed` → `Accepted`, dated), verified against the 2026 ecosystem; Deferred Questions kept parked; index re-badged |
| S1.2 | ADR-025 authored (`Accepted`) — SPA monorepo layout + build-output integration + dev-server story; index row + next-number pointer → 026 |
| S1.3 | Minimal `BiddingHub` connection-proof app scaffolded at `client/bidder/`; `npm install` + `npm run build` clean; live connectivity demonstrated |
| S1.4 | `CLAUDE.md` frontend pointer added (the `client/` layout + two-SPA split) |
| S1.5 | This retrospective |

## S1.1: ADR-013 acceptance

**Accepted as-proposed — no sub-choice revised.** The prompt required verifying the composition against the 2026 ecosystem *at acceptance time* rather than trusting the April proposal. The version-pinned / highest-churn members were checked against current upstream docs:

- **Tailwind CSS v4** — current (`tailwindcss@4.3.0` on npm). Its idiomatic Vite integration is now the v4-native `@tailwindcss/vite` plugin + a single `@import "tailwindcss";`, **superseding v3's PostCSS-plugin + `@tailwind base/components/utilities` directives.** This is a *wiring* change the proof app follows, not a *choice* change — the "Tailwind v4" pin holds. (Had the proof been scaffolded from v3 muscle memory, the styling wiring would have been wrong; this is precisely why acceptance-time verification protects the scaffold, not just the ADR.)
- **`@microsoft/signalr`** — `HubConnectionBuilder` API (`.withUrl`, `.withAutomaticReconnect`, `.on`, `.start`) unchanged (`@microsoft/signalr@10.0.0`).

No Revisit Trigger (library abandonment, LLM-fluency inversion) fired. The five Deferred Questions (routing, UI state, auth client pattern, SignalR integration pattern → ADR-014, PWA offline scope) **remain deferred** and are resolved in the later M8 slices that need them. Recorded in the ADR's new acceptance note + `## Document History`.

## S1.2: ADR-025 — SPA monorepo layout

Three decisions, each with alternatives weighed:

- **Source layout = npm-workspaces monorepo rooted at `client/`** (`bidder/` + planned `ops/` + planned `shared/`). Rejected: two independent projects (would duplicate the wire-contract surface the two apps share — the precise drift `CritterBids.Contracts` prevents on the backend), and a single app with two route trees (couples the public bundle to staff code; contradicts the two-SPA framing and the anonymous-vs-`StaffOnly` split). `client/shared/` is named as the **frontend analogue of `CritterBids.Contracts`** — one shared wire surface, two consumers.
- **Build-output = host-served static files, single deployable** (bidder at `/`, ops at `/ops/`), consistent with ADR-001/012. Static-file middleware on the host is **deferred to a later slice** (there is no real shell to serve yet); S1 adds none. Rejected for MVP: separate static/CDN deploy (second deploy target + prod CORS).
- **Dev-server = Vite dev-server proxy** (`/api`, `/hub` with `ws: true`) to `http://localhost:5180`. This **resolves the prompt's riskiest open question with no API-host change**: the browser stays same-origin, so no CORS, and ADR-024's auth surface is untouched. Rejected: API-host CORS (would force `AddCors`/`UseCors` + SignalR credential CORS — the backend creep the slice forbids). The "trivial dev-only CORS allowance" the prompt pre-authorized turned out **unnecessary**.

## S1.3: BiddingHub connection-proof app

Scaffolded **at its final `client/bidder/` home** (not a throwaway path), so M8-S2 promotes it in place. Structure: Vite 8 + React 19 + TypeScript strict; Tailwind v4 via `@tailwindcss/vite`; `@microsoft/signalr` pinned. `src/useBiddingHub.ts` owns a single `HubConnection` to `/hub/bidding` with `.withAutomaticReconnect()`, registers the `ReceiveMessage` client method (ADR-023), and surfaces live `HubConnectionState`; `src/App.tsx` renders the state as a coloured pill. **No catalog, no bid, no TanStack Query, no shadcn catalog, no second SPA, no `OperationsHub`.**

**Build:** `npm install` (57 packages, 0 vulnerabilities) and `npm run build` (`tsc --noEmit && vite build`) both succeed — **exit code 0**, `dist/` emitted. `tsc --noEmit` was chosen over the Vite template's `tsc -b` to avoid `composite`/project-reference ceremony unjustified for a single-package proof while still type-checking under strict.

**Known cosmetic:** the Vite/Rolldown build prints two `[INVALID_ANNOTATION]` warnings about `/*#__PURE__*/` placement **inside the `@microsoft/signalr` vendor file** (`dist/esm/Utils.js`) — upstream, not our code, non-fatal (build exits 0). Left as-is rather than adding fragile `onLog` suppression; noted so a future reader does not mistake it for a failure.

**Live verification (manual, against a running API host):** Aspire AppHost started (`dotnet run --project src/CritterBids.AppHost --launch-profile http`), provisioning Postgres + RabbitMQ and the API at `:5180`. Probes: `GET /openapi/v1.json` → 200; `POST /hub/bidding/negotiate?negotiateVersion=1` → **200** (anonymous hub accepts the handshake). Chromium for the Playwright MCP was not installed, so instead of a browser the **actual `@microsoft/signalr` client** (same library/options as the proof app) was driven from Node against `http://localhost:5173/hub/bidding` — i.e. **through the Vite dev proxy** — and reached `STATE = Connected` with a real transport `connectionId`; the direct `:5180` path also reached `Connected`. This proves the negotiate + WebSocket-upgrade path (incl. `ws: true` proxying) end to end. The browser app shares the identical hook, so the rendered pill follows from the same path. Verification infra (AppHost, Vite, the temp Node harness, the Aspire containers) was torn down afterward, leaving a clean tree.

## S1.4: CLAUDE.md pointer

A short `## Frontend (M8 — client/)` section added after the BC Module Quick Reference: the two-SPA table, the `client/shared/`-as-`Contracts`-analogue note, the dev-proxy one-liner, and pointers to ADR-013 / ADR-025 / the milestone doc. A pointer, not a duplication.

## Test results

| Phase | Backend tests | Frontend build | Result |
|-------|---------------|----------------|--------|
| Session open (M7 close) | 281 passing | — | baseline |
| After all S1 items | 281 (unchanged; not re-run) | `npm run build` exit 0, `dist/` emitted | ✅ |

Backend tests were **not re-run**: this slice touches **no `.cs` or build files** — the `client/` tree is entirely outside `CritterBids.slnx` (no `.csproj`), so the suite is definitionally unaffected, and the test suite requires Docker infra. The .NET solution **build** was confirmed still **0 errors / 0 warnings**. No backend test count change is possible from this diff.

## Build state at session close

- `dotnet build CritterBids.slnx` → **0 errors / 0 warnings** (unchanged from baseline).
- `npm run build --workspace @critterbids/bidder` → **exit 0**; `dist/index.html` + hashed JS/CSS assets emitted.
- TypeScript **strict**: on (`client/tsconfig.base.json`, `strict: true` + `noUnusedLocals`/`noUnusedParameters`); `tsc --noEmit` passes.
- Backend `.cs` / `.csproj` / `.slnx` files changed by this slice: **0**.
- API-host (`Program.cs`) changes: **0** (the dev proxy made a CORS allowance unnecessary).
- New `OperationsHub` / `client/ops/` / catalog / bid / TanStack code: **0** (out of scope, confirmed absent).
- `node_modules/` and `dist/` tracked by git: **0** (`client/.gitignore`).

## Key learnings

1. **Acceptance-time verification protects the scaffold, not just the ADR.** Tailwind v4's *choice* was right in April, but its *setup* changed from v3 (PostCSS + 3 directives → one Vite plugin + one `@import`). Verifying at acceptance caught the wiring before the proof app was written.
2. **A Vite dev-server proxy resolves "SPA reaches the hub in dev" with zero backend change.** `ws: true` carries the SignalR negotiate POST *and* the WebSocket upgrade same-origin, so no CORS and no touch to ADR-024's auth surface. This is the reusable answer for every later M8 slice and supersedes the prompt's pre-authorized "trivial dev CORS allowance."
3. **Driving the real client library from Node is a stronger, more deterministic live check than a browser.** When Chromium was unavailable, running `@microsoft/signalr` through the Vite proxy verified the exact app path (same library, same options) without UI-timing flakiness — and confirmed `ws:true` proxying specifically.
4. **`client/shared/` mirrors `CritterBids.Contracts`.** The backend's shared-contract boundary has a clean frontend analogue; naming it now keeps the two SPAs from duplicating wire schemas later (the same reason BCs don't reference each other's internals).
5. **The Preconditions gate did its job.** The milestone doc was a genuine missing prerequisite; stopping and authoring it first (rather than inferring M8 scope from the slice prompt) surfaced two corrections — the seller console belongs to a future milestone, and the CI backend matrix is already complete (PR #77) — before either could become slice rework.

## Findings against narrative

Anchored to `docs/narratives/001-bidder-wins-flash-auction.md`. This slice implements **no Moment** of narrative 001 — by design. The proof connects *toward* the journey (it opens the `BiddingHub` the live-bidding Moments depend on) but renders **no domain data**: no catalog (Moment 2), no bid placement (Moment 4), no outbid/extended-bidding (Moments 5–6). This is **`document-as-intentional`**: the slice's spec consequence is the two ADRs, not a journey Moment; narrative 001 is unchanged and needs no row. The first narrative-001 Moment lands at M8-S2 (catalog) / M8-S3 (live bidding).

## Spec delta — landed?

**Landed as written.** Per the prompt's `## Spec delta` (ADR-020), this slice's spec consequence was the **acceptance of ADR-013** and the **authoring of ADR-025**, fixing the frontend's architectural surface for all later M8 slices. Both landed: `docs/decisions/013-frontend-core-stack.md` shows **`Accepted`** (dated 2026-06-04) with a `## Document History` row recording the as-proposed acceptance and the 2026-ecosystem verification, and the ADR index row is re-badged **✅ Accepted**; `docs/decisions/025-spa-monorepo-layout.md` exists, status **`Accepted`**, settling source layout + build-output integration + dev-server story with alternatives weighed, its `## Document History` row present, the index gained its row, and the next-unreserved pointer advanced to **026**. The secondary code-level consequence — the first frontend code surface as a single minimal proof app — exists at `client/bidder/`, builds clean, and **demonstrated live `/hub/bidding` connectivity** (manual verification above: `@microsoft/signalr` reached `Connected` through the Vite proxy and direct, against a running Aspire-hosted API). No Moment of narrative 001 was implemented; no domain data rendered, as the prompt specified.

## Verification checklist

- [x] `013-frontend-core-stack.md` status `Accepted` (dated), on-acceptance verification recorded (no revision needed); five Deferred Questions remain listed as deferred
- [x] `docs/decisions/README.md` ADR-013 row shows ✅ Accepted
- [x] `025-spa-monorepo-layout.md` exists, status `Accepted`, settling source layout + build-output integration + dev-server (proxy chosen over CORS) — each with alternatives weighed
- [x] `docs/decisions/README.md` gains the ADR-025 row; next-unreserved pointer advanced to **026**
- [x] Single Vite + React + TS app at the ADR-025 path (`client/bidder/`); TypeScript strict on; Tailwind v4 base config present; `@microsoft/signalr` pinned in `package.json`
- [x] `npm install` and `npm run build` succeed from a clean checkout (exit 0, `dist/` emitted)
- [x] `HubConnection` to `/hub/bidding` registers `ReceiveMessage` + `.withAutomaticReconnect()`, renders live connection state; manual verification recorded (live `Connected` against a running API host)
- [x] No second SPA / `client/ops/` code; no `OperationsHub` connection; no catalog/bid/TanStack wiring
- [x] No API-host behavior change (the dev proxy made even the pre-authorized trivial CORS allowance unnecessary)
- [x] .NET build baseline unchanged (0/0); the frontend build/test step does not break `dotnet build`/`dotnet test` (client/ is outside the solution)
- [x] `CLAUDE.md` gains a short frontend-layout pointer reflecting ADR-025
- [x] This retrospective written with the `**Prompt:**` header and `## Spec delta — landed?` paragraph
- [x] No commit to `main`; work on branch `m8-s1-frontend-foundation-decisions`; no `Co-Authored-By` trailer (PR pending)

## What remains / next session should verify

- **PWA-from-day-one (open question — resolved as defer-on-proof, carry forward).** ADR-013 keeps "PWA from day one" as the accepted posture, but the **minimal connection proof has no manifest/icons/offline story to make meaningful** — "day one" sensibly attaches to the first *real* SPA shell, not a throwaway connection harness. **No `vite-plugin-pwa` was wired in S1.** Carry-forward: **M8-S2** (the first real bidder shell) wires the manifest + service-worker registration, honoring the day-one stance. (In scope for the milestone, deferred to S2.)
- **Routing library** (ADR-013 Deferred Question) — resolved at M8-S2 when concrete routes exist.
- **SignalR integration pattern (ADR-014)** — authored at M8-S3 when the first hub is wired into the app (Provider + hook + TanStack Query cache bridge); its code lives in the planned `client/shared/`. (Out of scope here; the proof uses a raw hook deliberately.)
- **`client/ops/` + `client/shared/`** — planned homes only; built at M8-S5 (ops shell) and when the second consumer makes the shared surface real. (Out of scope, tracked in the milestone doc.)
- **Frontend CI job** — a Vitest + Vite-build (optionally Playwright) job; in-milestone housekeeping (likely M8-S7). The backend integration matrix already covers all eight BCs + Api (PR #77), so **no backend matrix work is owed** (corrects the stale M7-retro "~44% local only" note). (Out of scope here.)
- **The `[INVALID_ANNOTATION]` vendor warnings** — cosmetic, from `@microsoft/signalr`'s own dist. A future slice may silence them with a narrow documented `onLog` filter if the noise bothers CI; not worth fragile config now.
