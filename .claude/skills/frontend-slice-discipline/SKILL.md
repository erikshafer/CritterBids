---
name: frontend-slice-discipline
description: >-
  CritterBids frontend working discipline, distilled from the M8 retrospectives:
  read the lived backend surface before writing client code, render the lived
  subset and record gaps as carry-forwards (never invent shapes or silently widen
  into backend changes), verify the installed toolchain instead of remembered
  APIs, close every slice with a live smoke against the Aspire host (with the
  browser-fallback playbook), and the recurring test conventions (injectable
  seams, isolated test routers, tests in the production type-check, StrictMode
  double-effect handling). Use when starting, implementing, or closing any
  frontend slice or feature in client/.
---

# CritterBids Frontend Slice Discipline

> Process rules for working in `client/` — the lessons every M8 slice paid for once so the
> next slice doesn't. The *wire contract* itself lives in
> `docs/skills/wolverine-http-frontend-contract/SKILL.md` (HTTP) and
> `.claude/skills/signalr/SKILL.md` (push); this skill is about **how to work**, not what the
> bytes look like.

## When to apply

- Starting a new frontend slice or feature in `client/bidder/` or `client/ops/`.
- Mid-slice, when a spec (milestone table, narrative, prompt) names something you can't find
  in the backend.
- Closing a slice: deciding what "verified" means before the retro claims it.

## Rule 1 — Read the lived backend before writing client code

Milestone tables, narratives, and prompts describe **intent**; `src/` is **truth**. Before the
first client line, read the actual endpoint signatures, read-model records, and notification
types the slice consumes. Every M8 slice that skipped ahead would have shipped against fiction:

- The milestone named a `ListingDetailView`; the lived endpoint returns `CatalogListingView`
  for both list and detail (M8-S2). The client binds to the real shape — no invented schema.
- Narrative 001 names a targeted `Outbid` push; the lived Relay surface fans out `BidPlaced`
  only (M8-S3b). "Outbid" became a client-side derivation from view transitions — reading
  `Relay/Notifications/` first reshaped the whole design.
- The narrative names `remainingCredit`; the lived `SettlementCompletedNotification` omits it
  (M8-S4). The view renders the subset that exists.
- The prompt said `accessTokenFactory` puts the token on the negotiate; the installed client's
  source said Bearer header (M8-S5). Package source in `node_modules` counts as lived backend.

## Rule 2 — Render the lived subset; gaps become carry-forwards

When the spec names something the backend doesn't expose, there are exactly two moves, and
inventing a client-side workaround is neither:

1. **Render what exists** and record the gap — in the retro's findings and
   "What remains", with the backend change the gap implies (e.g. "display name needs a
   `GET /api/participants/{id}` read path"). A recorded carry-forward survives; a silent
   omission evaporates.
2. **Escalate a sanctioned backend slice** when the journey cannot ship without it — the
   M8-S3a precedent (`POST /api/auctions/bids` had no HTTP surface at all). Its own slice,
   its own prompt, never a quiet `.cs` touch inside a frontend slice.

M8's frontend slices shipped with **zero** unsanctioned backend changes because every gap took
move 1 or move 2.

## Rule 3 — Verify the installed toolchain, not remembered APIs

The 2026 ecosystem moved under every slice that assumed muscle memory. Check the **installed**
package (its `node_modules` source or current docs) at scaffold time:

- Tailwind v4: the v3 PostCSS + `@tailwind` directives wiring is gone — v4 is
  `@tailwindcss/vite` + one `@import "tailwindcss";` (M8-S1).
- Zod 4 moved string-format validators off `z.string()` (M8-S2).
- TypeScript deprecated `baseUrl` — the `@/*` alias works without it under
  `moduleResolution: "bundler"` (M8-S2).
- SignalR 7+ changed the access-token transport (M8-S5) — found by grepping
  `node_modules/@microsoft/signalr/dist/esm/`, not by docs.

The pattern: **the library *choice* is usually stable; the *wiring* is what changed.** A
five-minute source check beats a debugging session against a green-looking scaffold.

## Rule 4 — Close every slice with a live smoke

Mocked-fetch tests verify **response handling**; only a live host verifies **request shape**
(body presence, `Content-Type`, header names, key casing) and **cross-client propagation**.
The two canonical failures:

- M8-S2: green build + green tests shipped a session POST that 400'd on its first live
  request (no body).
- M8-S3b: a fully green, correctly-built frontend — and the live cross-client loop didn't
  close, because two backend bugs sat between it and a working demo. Only the integrated
  manual run found them.

A skipped smoke is an **explicitly unchecked acceptance criterion** in the retro (the M8-S4
precedent), never a silent pass.

### The smoke playbook

1. **Host:** `dotnet run --project src/CritterBids.AppHost --launch-profile http`. Dev-only
   secrets (the ADR-024 staff token) go in the launching shell's environment —
   `$env:OperationsAuth__StaffToken = "..."` — child projects inherit it (no repo change; see
   `docs/skills/aspire/SKILL.md`).
2. **Probe the HTTP contract** with direct requests first (PowerShell `Invoke-WebRequest` /
   curl): expected status with and without credentials, body casing, `Location` headers.
3. **Drive the real client library from Node** through the Vite dev proxy when a browser is
   unavailable — same library, same options as the app, deterministic, and it exercises the
   proxy (`ws: true`) specifically. Node 22's built-in `WebSocket` is header-less like a
   browser, so it reproduces browser credential transport faithfully.
4. **Real browser pass** when feasible: the Playwright MCP needs Chrome; when it's missing
   (admin install), `playwright-core` driving system **Edge** (`channel: "msedge"`,
   `headless: true`) works without any browser download (M8-S5).
5. **Tear down** what you started (background hosts, dev servers, temp harnesses) and record
   the smoke's findings — including expected console noise — in the retro.

## Test conventions that recurred

- **Injectable seams over module mocks where a boundary exists:** the SignalR Providers take a
  `createConnection` prop; fetch wrappers take a `fetchImpl`. Tests drive pushes/responses
  through fakes without patching globals. (Module-mock only when asserting on the *production*
  factory itself — the M8-S5 `withUrl`-capture test.)
- **Isolated test router, not the real shell.** Render the page under a minimal router with
  stub routes (so typed `Link`s resolve) instead of mounting `AppShell` — keeps SignalR and
  the session POST out of jsdom (M8-S2).
- **Tests compile in the production type-check.** `tsconfig.json` `include: ["src"]` puts
  `*.test.tsx` inside `tsc --noEmit`, which runs in `npm run build` — a type error in a test
  breaks the build. Tests must satisfy `strict` + `noUnusedLocals`.
- **Reset browser storage between tests** (`sessionStorage.clear()` in the Vitest setup) —
  both apps key auth/session state off it.
- **StrictMode double-effects are a design constraint, not noise.** For a one-shot effect
  (the session POST), a `startedRef` guard ensures one real request — and deliberately **no**
  cancelled-flag on the result write: under StrictMode the surviving fetch belongs to the
  torn-down first effect instance, and a cancelled flag drops the only result on the floor
  (the M8 Bug #2 fix-up walkthrough found exactly this hang). For connection effects, the
  cleanup `stop()` makes dev log one benign
  `Failed to start the HttpConnection before stop() was called` — expected with the cleanup
  present, a bug without it.

## Common pitfalls

- **Coding against the milestone/narrative table** without opening the backend types → binds
  to views and pushes that don't exist (Rule 1).
- **"Fixing" a backend gap from the frontend** (inventing fields, fake data, client-side
  workarounds for missing reads) → Rule 2: render the subset + carry-forward, or escalate a
  slice.
- **Scaffolding from training-data muscle memory** → Rule 3; check the installed version.
- **Calling a slice done on green unit tests** → Rule 4; request contracts and cross-client
  loops are only verifiable live.
- **Mounting the full app shell in component tests** → jsdom fights SignalR and the session
  bootstrap; isolate with a test router.
- **Adding a cancelled-flag to every async effect reflexively** → for one-shot
  effects under StrictMode it can discard the only result; reason about which effect instance
  owns the in-flight work.

## See also

- `docs/skills/wolverine-http-frontend-contract/SKILL.md` — the HTTP wire contract this
  discipline verifies (body rules, `CreationResponse`, ProblemDetails, retry rules).
- `.claude/skills/signalr/SKILL.md` — the push-surface client conventions (ADR 026 pattern,
  hub auth, dedupe).
- `docs/skills/aspire/SKILL.md` — host startup + dev-secret environment inheritance for the
  smoke playbook.
- ADR-013 / ADR-025 / ADR-026 in `docs/decisions/` — the stack, layout, and SignalR pattern
  decisions slices point at rather than re-deciding.
- M8 retrospectives (`docs/retrospectives/M8-S1…S5-*.md`) — the evidence base; each rule cites
  its slice.
