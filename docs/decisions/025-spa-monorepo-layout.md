# ADR 025 — SPA Monorepo Layout + Build Integration

**Status:** Accepted
**Date:** 2026-06-04 (M8-S1)

---

## Context

M8 introduces the first frontend code surface in CritterBids: two React single-page applications (ADR 004, ADR 012) — a public **bidder-facing app** and a staff-gated **operations dashboard** — both static Vite builds pointing at the same `CritterBids.Api` host (`bounded-contexts.md`: "the ops dashboard and participant-facing app are separate React SPAs pointing at the same API host"). ADR 013 (accepted in this same M8-S1 session) fixes the library composition. What ADR 013 deliberately does **not** decide is *where the SPA source lives, how its build output relates to the .NET host, and how the dev server reaches the API and the SignalR hubs.* Those three questions are this ADR's subject.

These are hard-to-reverse, cross-cutting decisions. Repo layout, the build pipeline, and the dev-server contract shape how every later M8 slice is structured; changing them after two SPAs and a shared contract layer exist is expensive. That is why this is an ADR rather than a milestone-doc note, authored as a foundation decision in the M8-S1 slice before any real SPA shell is built.

This ADR is constrained by its accepted parents:

- **ADR 012 (Vite SPA, not a meta-framework):** both SPAs ship as static Vite output; the backend owns all contracts. No Next.js / Remix framework mode / TanStack Start; no server-rendered route ownership.
- **ADR 001 (Modular Monolith):** the system is a single deployable unit with enforced internal boundaries. The frontend layout is given the chance to echo that ethos rather than fight it.
- **ADR 024 (Staff auth):** the ops SPA authenticates with the `StaffToken` posture (`X-Staff-Token` header for HTTP; `access_token` query string for the `OperationsHub` negotiate). Any dev-server choice must not widen that auth/CORS surface.

The M8-S1 slice that authors this ADR scaffolds **one** minimal `BiddingHub` connection-proof app at the layout decided here — it does not build the second SPA or the shared package. This ADR therefore *plans* the full layout while S1 *realizes* only the first member.

---

## Decision

### 1. Source layout — npm-workspaces monorepo rooted at `client/`

The SPA source lives under a single `client/` directory at the repository root, organized as an **npm-workspaces monorepo**:

```
client/
├── package.json              # workspaces manifest + shared dev tooling (TS, ESLint, Prettier)
├── tsconfig.base.json        # shared strict TS config; each app extends it
├── tailwind.preset.*         # shared Tailwind v4 preset (theme tokens) — added when the 2nd consumer lands
├── bidder/                   # public bidder-facing SPA  (workspace member)   ← scaffolded in M8-S1
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json         # extends ../tsconfig.base.json
│   └── src/
├── ops/                      # staff operations dashboard SPA (workspace member)   ← PLANNED (M8-S5), not built in S1
│   └── …
└── shared/                   # shared API client + Zod wire schemas + SignalR integration (workspace member)   ← PLANNED, not built in S1
    └── …
```

The two SPAs are **workspace members**, not independent projects and not one app with two route trees. A `client/shared/` member holds the surface both apps genuinely share: the typed API client, the Zod schemas that validate every HTTP response and SignalR payload at the wire boundary (ADR 013), and the SignalR integration pattern (ADR 026). `client/shared/` is the **frontend analogue of `CritterBids.Contracts`**: one shared contract surface, two consumers — the same shape the backend already uses to keep BCs from depending on each other's internals.

**M8-S1 footprint:** only the workspace root manifest (`client/package.json` + `client/tsconfig.base.json`) and `client/bidder/` are created. `client/ops/` and `client/shared/` are planned homes; they are authored in the slices that need them (ops shell at M8-S5; `shared/` when the second consumer or ADR 026 makes the duplication real). The bidder proof app is scaffolded **directly at its final `client/bidder/` home**, not a throwaway location — the layout is decided here, so the proof does not strand code at a path this ADR disowns.

### 2. Build-output integration — host-served static files, single deployable

Each SPA is built by Vite to a static `dist/`. The **production posture** is that `CritterBids.Api` serves each SPA's built output as static files, preserving the modular-monolith single-deployable ethos (ADR 001) and ADR 012's static-SPA posture:

- Bidder app served at base path **`/`** (`vite build` with `base: "/"`).
- Ops dashboard served at base path **`/ops/`** (`vite build` with `base: "/ops/"`).
- API at `/api`, SignalR hubs at `/hub` (unchanged — owned by the backend).

The API host gains static-file + SPA-fallback middleware (`UseStaticFiles` + `MapFallbackToFile` per base path) **in a later M8 slice, when there is a real SPA shell to serve** — explicitly **not in M8-S1**. S1 adds no static-file middleware and makes no API-host change. The build-time wiring (copying each `dist/` into the host's served output during publish) is a later-slice concern recorded here as the target, not implemented now.

This is a deployment *posture*, not a runtime coupling: the SPAs remain pure static assets that could be lifted to a CDN unchanged. Serving them from the host is the MVP/demo default, chosen for single-deployable simplicity, not a dependency the SPA code takes on.

### 3. Local dev-server story — Vite dev-server proxy, no CORS, no API-host change

In development each SPA runs its own Vite dev server (separate ports). Each app's `vite.config.ts` **proxies** the backend paths to the API host:

```ts
server: {
  proxy: {
    "/api": { target: "http://localhost:5180", changeOrigin: true },
    "/hub": { target: "http://localhost:5180", changeOrigin: true, ws: true },
  },
}
```

`http://localhost:5180` is the API host's `http` launch-profile URL (`src/CritterBids.Api/Properties/launchSettings.json`); it is the proxy target whether the host is run directly (`dotnet run --project src/CritterBids.Api --launch-profile http`) or under Aspire (which discovers the same launch-profile endpoint). The target is the one value a contributor may need to adjust; everything else is fixed.

Because the browser talks **only to the Vite dev-server origin**, every `/api` and `/hub` request is same-origin from the browser's perspective. Consequences:

- **No CORS configuration anywhere**, and **no change to the API host** — the `StaffToken` auth surface (ADR 024) is untouched.
- The SignalR **negotiate POST and the WebSocket upgrade** both traverse the proxy (`ws: true` enables the upgrade). This is the path the M8-S1 connection proof relies on, and it works without the "trivial dev-only CORS allowance" the M8-S1 prompt pre-authorized as a possible necessity — the allowance is not needed.

The dev-server proxy is the decision for MVP. If a future deployment genuinely requires the SPA to call the API cross-origin (separate static deploy, see Alternatives), CORS becomes a real decision at that point — it is out of scope here and would be its own change, not a silent widening of the API surface.

---

## Alternatives Considered

### Source layout

- **Two fully independent Vite projects** (`client/bidder/`, `client/ops/`, no workspace, no shared package). Simplest to start — zero workspace tooling. **Rejected** because the two apps share a real contract surface: the same Zod schemas validate the same API/hub payloads in both apps, and the SignalR integration pattern (ADR 026) is identical for both hubs. Independent projects would duplicate that surface and let it drift — the precise failure the backend avoids with `CritterBids.Contracts`. The workspace cost (a root `package.json` with a `workspaces` array) is small; the DRY-on-the-wire-contract benefit is large and pedagogically on-message for a reference architecture.
- **Single Vite app, two route trees** (one bundle, `/` for bidder and `/ops` for staff). **Rejected:** it couples the public bidder bundle to staff-only code (a bundle-size and information-exposure smell), contradicts the `bounded-contexts.md` two-window / two-SPA projector framing, and blurs the anonymous-vs-`StaffOnly` auth split that ADR 024 draws cleanly between the two apps.

### Build-output integration

- **Separate static deploy / CDN per SPA** (each SPA deployed independently, calling the API cross-origin). A legitimate scale posture and the SPAs remain CDN-liftable by design. **Rejected for MVP** because it introduces a second (and third) deploy target and forces production CORS on the API host — complexity unjustified at MVP/demo, where single-deployable is simpler and matches ADR 001. Recorded as the post-MVP scale door.
- **Subdomain split** (`app.` and `ops.` hosts). Same trade-off as separate deploy; post-MVP.

### Dev-server reach

- **API-host CORS** (API allows the dev-server origin; SPA calls the API directly cross-origin). **Rejected:** it requires `AddCors`/`UseCors` on the API host plus SignalR credentialed-CORS configuration, widening exactly the API auth/CORS surface the M8-S1 slice is forbidden to grow. The proxy achieves the same dev outcome with no backend change and no CORS at all.

---

## Consequences

**Positive:**

- **One shared contract surface, two consumers.** `client/shared/` mirrors `CritterBids.Contracts`; the wire schemas and SignalR integration live once. The reference-architecture story is coherent front to back.
- **No backend change for the frontend's dev loop.** The Vite proxy keeps the API host (and ADR 024's auth surface) untouched; the connection proof and all later dev work reach `/api` and `/hub` same-origin.
- **Single deployable preserved.** Host-served static output keeps CritterBids one deployable unit for MVP/demo, consistent with ADR 001 and ADR 012.
- **The proof app is not stranded.** It is scaffolded at its final `client/bidder/` home under the decided layout, so M8-S2 promotes it in place rather than relocating it.

**Negative:**

- **Workspace ceremony on day one.** The monorepo root exists with a single member at M8-S1; the shared-tooling payoff is not visible until `client/ops/` and `client/shared/` land. Mitigation: the S1 root manifest is minimal (a `workspaces` array and shared dev-dependencies), and the layout it establishes is the one every later slice expects.
- **Base-path discipline.** Serving the ops app at `/ops/` requires its Vite `base` and its router basename to agree; a mismatch surfaces as broken asset URLs. Mitigation: pinned here and re-stated in the M8-S5 ops-shell slice.
- **Dev proxy target is environment-coupled.** `http://localhost:5180` is correct for the standard launch profile; a contributor who runs the API on a different port must adjust the single proxy target. Mitigation: it is one well-documented value, and it is the only one.

**Neutral:**

- The static-file serving middleware is deferred to a later slice. Until then, the SPAs are exercised only against the dev proxy. This is intentional — M8-S1 proves the connection, not the production serving path.

---

## Relationship to Other ADRs

| ADR | Effect |
|---|---|
| ADR 001 — Modular Monolith | **Echoes.** Host-served static output keeps the single-deployable ethos; `client/shared/` mirrors `CritterBids.Contracts`'s shared-contract boundary. |
| ADR 004 — React for Frontend Applications | **Depends on.** The two React SPAs this ADR lays out. |
| ADR 012 — Frontend: Vite SPA, Not a Meta-Framework | **Depends on / constrained by.** Static Vite output, backend owns contracts — this ADR's build-output posture is the concrete encoding of ADR 012's stance. |
| ADR 013 — Frontend Core Stack | **Peer (same slice).** ADR 013 fixes *which libraries*; ADR 025 fixes *where the code lives and how it builds/serves*. `client/shared/` is the home of ADR 013's Zod wire schemas and (via ADR 026) its SignalR integration. |
| ADR 026 — SignalR Integration Pattern | **Provides a home for.** ADR 026 was authored at M8-S3b (first hub wired from a client); its Provider + hook + cache-bridge code lands in `client/bidder/src/signalr/` now and moves to `client/shared/` when the ops app becomes the second consumer. |
| ADR 024 — Staff Auth Posture | **Preserved.** The dev-server proxy choice is specifically the one that does not touch ADR 024's auth/CORS surface. |

---

## References

- ADR 001 — Modular Monolith Architecture
- ADR 004 — React for Frontend Applications
- ADR 012 — Frontend: Vite SPA, Not a Meta-Framework
- ADR 013 — Frontend Core Stack
- ADR 024 — Staff Token Authentication
- `docs/milestones/M8-frontend-spas.md` — the milestone this layout serves (§4 Solution Layout, §5 Infrastructure/Build/Dev)
- `docs/vision/bounded-contexts.md` — "separate React SPAs pointing at the same API host"
- `src/CritterBids.Api/Properties/launchSettings.json` — the `http` profile (`http://localhost:5180`) the dev proxy targets
- Vite dev-server proxy docs: https://vite.dev/config/server-options#server-proxy

---

## Document History

- **2026-06-04** — `M8-S1-frontend-foundation-decisions`: Authored and accepted as the second M8-S1 foundation decision (peer to ADR 013's acceptance). Settles (1) source layout = npm-workspaces monorepo rooted at `client/` with `bidder/` + planned `ops/` + planned `shared/` members; (2) build-output integration = host-served static files, single deployable, bidder at `/` and ops at `/ops/`, static-file middleware deferred to a later slice; (3) dev-server reach = Vite dev-server proxy to `http://localhost:5180` with `ws: true`, resolving the prompt's open question with no CORS and no API-host change. M8-S1 realizes only the `client/` root + `client/bidder/` proof app; `ops/` and `shared/` are planned homes.
