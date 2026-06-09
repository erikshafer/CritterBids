# Frontend Lessons Catalog (pre-skill ledger)

**Status:** Running catalog — append as we learn. Not yet a skill.
**Scope:** The CritterBids frontend build (M8) plus the **Critter Stack integration gotchas the frontend work surfaced**. This is the raw material that will later distill into the portable `docs/skills/frontend/SKILL.md` that ADR-013 earmarks ("`docs/skills/frontend/ (to be created)` — implementation conventions").
**Why this exists:** retros capture per-slice findings but are narrative-bound and scattered. This is the cross-cutting middle layer between "what we noticed in M8-S2" and "the reusable skill." Catalog first, distil later — so one project's accidents don't get baked into the skill prematurely.

Each entry is tagged by portability so the eventual distillation knows what travels:

- **[CS]** Critter Stack general — true for any Wolverine/Marten + SPA project (CritterBids, CritterSupply, future).
- **[CB]** CritterBids-specific — a fact about this codebase's endpoints/contracts.
- **[TC-2026]** Toolchain version-time — true as of the 2026 stack; re-verify against installed versions, will age.

> **How to use:** when starting a frontend slice, skim §A before wiring any API call and §D before writing tests. When you hit a new reusable lesson, add it here with a one-line source. When we build the skill, promote the **[CS]** entries (and the durable **[CB]** patterns) into `SKILL.md` and leave the **[TC-2026]** entries here as a dated changelog.

---

## A. Critter Stack API integration (read before wiring any call)

1. **[CS] Wolverine.HTTP command endpoints require a JSON request body — even for empty-record commands.** A handler like `Handle(StartParticipantSession cmd)` binds the command from the body; a body-less POST returns `400 "Invalid JSON format — the input does not contain any JSON tokens"`. Send `body: "{}"` + `Content-Type: application/json`. *Source: M8-S2 session bootstrap bug, caught by live smoke. Saved as memory `wolverine-http-command-body`.*

2. **[CS] API JSON is camelCase** (ASP.NET / System.Text.Json **web** defaults). Zod schemas at the wire boundary mirror camelCase keys. Nulls are written (web defaults don't ignore null), but model fields `.nullish()` to tolerate either. *Source: M8-S2, confirmed live (`{"value":…,"url":…}`).*

3. **[CS] `CreationResponse<T>` → `201 Created` + a `Location` header** (`/api/{resource}/{id}`); the JSON body carries `value` (the created id) and `url`. **Read the id from the `Location` header** — it's robust to body-serialization details — with the body `value` as a fallback. *Source: M8-S2 `extractParticipantId`; memory `feedback_wolverine_http_testing` also notes the Location header.*

4. **[CB] Read-endpoint conventions to mirror in the client:** list endpoints return `[]` (an empty array, **never 404**) when empty → render an *empty state*, not an error; detail endpoints **404 on a missing id** → render a *not-found state* distinct from a generic error. *Source: `CatalogEndpoints.cs`; M8-S2 catalog/detail.*

5. **[CB] The listing detail endpoint returns the same read model as the list** (`CatalogListingView`), not a separate `ListingDetailView`. The milestone surface table named a `ListingDetailView` that does not exist in the lived backend — **bind the client to the lived read model; don't invent a shape.** *Source: M8-S2 finding.*

6. **[CB] The anonymous participant's generated display name is not surfaced over HTTP.** `POST /api/participants/session` returns only the id; there is no `GET /api/participants/{id}`. Rendering the minted name needs a new backend read path. *Source: M8-S2 — deferred the display-name header. Backend-reality-vs-milestone-surface gap.*

7. **[CB] Seeding a published listing is a multi-step domain flow** (register seller → create draft → submit), and **there is no HTTP publish endpoint** (`SubmitListing` isn't exposed over HTTP). A fresh DB's catalog is empty; don't expect to seed one with a single call for a smoke test. *Source: M8-S2 smoke check.*

---

## B. Frontend stack wiring (the CritterBids M8 shape — reusable starting point)

8. **[CS] Vite dev-server proxy beats CORS for a static SPA + same-origin API.** Proxy `/api` and `/hub` (with **`ws: true`** for SignalR) to the API host; the browser stays same-origin, so **no CORS and no API-host change**, and the SignalR negotiate POST *and* the WebSocket upgrade ride the proxy. *Source: ADR-025; proven live in M8-S1 and M8-S2.*

9. **[TC-2026] Tailwind v4 native wiring** = the `@tailwindcss/vite` plugin + a single `@import "tailwindcss";`. **No** PostCSS, **no** `tailwind.config.js`, **no** `@tailwind base/components/utilities` directives (that's v3 muscle memory and will produce wrong styling). *Source: ADR-013 acceptance note; M8-S1.*

10. **[TC-2026] shadcn/ui on Tailwind v4** = a CSS-variable theme in `index.css` with `:root`/`.dark` vars + an `@theme inline { --color-*: var(--*) }` mapping, plus the `cn()` util. **Authoring the baseline by hand is viable in non-interactive/CI environments** (write `components.json` + `cn` + copy-in primitives) — a later `npx shadcn add <component>` still resolves against the committed `components.json`. Copy components in only as surfaces need them. *Source: M8-S2.*

11. **[TC-2026] TanStack Router: prefer code-based routing for small route sets.** `createRootRoute`/`createRoute`/`createRouter` need **no** router plugin and emit **no** generated `routeTree.gen.ts` — a cleaner, codegen-free diff. Use `getRouteApi("/path/$id")` to read typed params **without** importing the route object (avoids a router⇄page import cycle). Migrate to file-based routing when route count grows. *Source: ADR-013 routing resolution; M8-S2.*

12. **[CS] TanStack Query + Zod-at-the-boundary.** The Zod `schema.parse(...)` inside the `queryFn` is the **single** parse point; components consume the inferred type, never raw JSON. Keep queries **fetch-authoritative** — do not architect them to render a hub-push body as truth — so the future SignalR→cache bridge (ADR-014) slots in as a re-query signal without a rewrite. *Source: ADR-013; M8-S2; M7 §5 "push = re-query" contract.*

13. **[TC-2026] PWA "day one" with `vite-plugin-pwa`** = `registerType: "autoUpdate"` + `injectRegister: "auto"` (no manual SW-registration code) + `workbox.globPatterns` for app-shell precache. "Day one" attaches to the first *real* shell, not a throwaway proof. Offline *data* scope (caching listings, queueing bids) is a separate, deferred question. *Source: ADR-013; M8-S2.*

---

## C. 2026 toolchain pins & gotchas (re-verify against installed versions)

14. **[TC-2026] Zod resolved to v4** — string-format validators moved off `z.string()` (`.uuid()`/`.email()` are now top-level or removed from the method chain). For server-issued ids, `z.string()` is enough and is cross-version safe. *Source: M8-S2.*

15. **[TC-2026] TypeScript errors on deprecated `baseUrl`.** Use `paths` **without** `baseUrl` under `moduleResolution: "bundler"` (paths resolve relative to the tsconfig). *Source: M8-S2 build failure.*

16. **[CS] `tsconfig` `include: ["src"]` puts `*.test.tsx` in the production type-check.** `tsc --noEmit` in the build script checks test files too, so a type error in a test breaks `npm run build`, not just Vitest — and tests must satisfy `strict` + `noUnusedLocals` (use the `void unusedVar` idiom for intentionally-unused destructures). *Source: M8-S2.*

17. **[TC-2026] `@microsoft/signalr` emits cosmetic `[INVALID_ANNOTATION]` `/*#__PURE__*/` warnings** under the Rolldown-based Vite build — upstream vendor file, **non-fatal**, build exits 0. Don't mistake it for a failure; don't add fragile `onLog` suppression. *Source: M8-S1 and M8-S2.*

---

## D. Testing & CI discipline (read before writing frontend tests)

18. **[CS] Mocked-`fetch` unit tests verify response handling, not request shape — they cannot catch request-contract bugs.** A green build + green unit tests still shipped the session-POST body bug (#1). **Run a live smoke against the Aspire host** (`dotnet run --project src/<App>.AppHost`, Docker up) and curl/drive the real endpoints through the Vite proxy. *Source: M8-S2. Saved as memory `frontend-live-smoke-catches-request-contract`.*

19. **[CS] For focused component tests, mount under an isolated memory-history router — not the real app shell.** A test router whose root is a bare `Outlet` (plus a stub route so typed `Link`s resolve at runtime) keeps SignalR connections and the session POST out of jsdom, making the test deterministic. *Source: M8-S2 `CatalogPage.test.tsx`.*

20. **[CS] Frontend CI on a backend-gated pipeline needs its own path filter + an aggregator that treats `skipped` as passing.** A frontend-only PR skips the backend `code`-filtered jobs; if the single required aggregator demanded every job be `success`, adding the frontend job to `needs` would make *backend-only* PRs fail on a skipped frontend job. The fix: per-job, fail only on `failure`/`cancelled`; `success` **or** `skipped` passes. *Source: PR #81 (`ci/frontend-job`).*

21. **[CB] Chromium is not installed for the Playwright MCP** in this environment (as of M8-S1/S2). Real-browser smokes fall back to HTTP/proxy checks + jsdom tests; a true browser e2e waits on the M8-S7 Playwright stage (which would `npx playwright install` its own browser). *Source: M8-S1, M8-S2.*

---

## Distillation status

Nothing promoted to a skill yet. When `docs/skills/frontend/SKILL.md` is authored, promote the **[CS]** entries and the durable **[CB]** patterns into it, keep the **[TC-2026]** entries here as a dated version-changelog, and cross-reference this catalog from ADR-013 (closing its "to be created" pointer).

## Document history

- **2026-06-08** — Created after M8-S1/S2 + the frontend-CI PR (#81). Seeded with 21 entries across API integration, stack wiring, toolchain pins, and testing/CI. Scope: frontend + the Critter Stack gotchas the frontend work surfaced (per Erik's "catalog now, portable skill later").
