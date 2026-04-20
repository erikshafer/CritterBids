# Frontend Stack Research — CritterBids

**Status:** Draft — Research Phase (Milestone RF-1)
**Owner:** Erik Shafer
**Last Updated:** 2026-04-19
**Suggested repo location:** `docs/research/frontend-stack-research.md`

---

## 1. Purpose & Position

This document captures the initial research effort to select a frontend technology stack for CritterBids. It is explicitly a **research document**, not a commitment. Decisions that emerge from it will be promoted to Architecture Decision Records (ADRs) through the normal milestone process.

The CritterBids frontend exists to make the Critter Stack showcase feel real during conference demos and to give the system a usable face. The backend (Wolverine, Marten, and the rest of the Critter Stack) is the architectural centerpiece. The frontend complements it; it does not drive it.

This framing matters for every decision in this document. When a technology choice could plausibly seat the frontend as co-equal with the backend (for example, Next.js with API routes and server actions), we reject it on principle, not on merit alone. The backend owns contracts, events, and projections. The frontend renders them.

---

## 2. Project Context

CritterBids is a Critter Stack showcase project modeled on eBay-style auctions. Relevant details that shape frontend decisions:

- **Two auction formats.** Timed auctions run over days, eBay-style. Flash auctions run over a short session window and exist primarily as a live-demo vehicle for conferences and user groups.
- **Live bidding is the defining experience.** Flash sessions imply multiple bids per second from an audience scanning a QR code on their phones, with a projected ops dashboard visible to the room.
- **Mobile-first is not optional.** The audience at conferences and user groups primarily uses phones. Tablets and laptops follow.
- **Event-sourced backend.** Every state change is an event in Marten (PostgreSQL). Reconciliation on reconnect is naturally a projection replay, not a bespoke sync protocol.
- **Self-hosted deployment on Hetzner VPS.** No Azure, no Vercel, no managed SignalR service. Scaling is a single-box concern first and a two-or-three-instance concern later.
- **Eight bounded contexts on a single Marten store.** All BCs (Participants, Auctions, Listings, Selling, Settlement, Obligations, Relay, Operations) use PostgreSQL via Marten per ADR 011, which superseded ADR 003's earlier dual-store arrangement. The frontend is agnostic to bounded-context structure entirely; it talks to HTTP endpoints and SignalR hubs, not directly to stores.
- **LLM-assisted development.** Agents (Claude Code, Copilot, and others) will author a substantial portion of the frontend code. Stack choices that have strong LLM training support and idiomatic patterns matter.

---

## 3. Scope

### 3.1 In Scope

- Build tooling and project scaffolding.
- Framework posture (SPA vs meta-framework).
- Language, type safety, and linting baselines.
- Routing, data fetching, server-state management.
- UI component library and styling system.
- Forms and validation.
- Real-time client integration with ASP.NET Core SignalR.
- Mobile-first UX patterns and PWA posture.
- Testing strategy at the frontend layer.
- Deployment shape on Hetzner.
- Skill-file and documentation organization.

### 3.2 Out of Scope (for this document)

- Detailed UI/UX design, wireframes, or visual identity.
- Specific component implementations.
- Authentication flow diagrams (depends on Participants BC decisions, which are upstream).
- Analytics, observability, or error reporting tooling selection.
- i18n or accessibility audit process (deferred; both will be addressed, but not here).
- Visual design system tokens (colors, typography, spacing) beyond what the component library ships with.

---

## 4. Constraints & Guiding Principles

1. **Contracts flow from the backend.** Any contract the frontend prototype pins down must flow back through the ADR and milestone process for backend concurrence. The frontend never designs API shapes unilaterally.
2. **LLM-friendly by default.** Prefer mature, ubiquitous technologies with deep training coverage. Novelty is a cost, not a feature.
3. **Deploy-anywhere.** The build output should be static assets that any web server or CDN can serve. No vendor-specific runtime dependencies.
4. **Mobile experience is a first-class acceptance criterion.** If a pattern works well on desktop but poorly on a phone, it fails.
5. **Operational simplicity on a single VPS.** The frontend should not require its own runtime process. It is static files plus a reverse-proxy config.
6. **Type safety end-to-end.** TypeScript strict mode. Runtime validation at the wire boundary. The frontend should catch contract drift before it reaches production.
7. **No premature scaffolding.** If a tool does not earn its presence in the first milestone of real UI work, it is not in the stack.

---

## 5. Research Findings

Each subsection summarizes what the current ecosystem looks like as of April 2026, then offers a provisional recommendation. Provisional recommendations are consolidated in Section 6.

### 5.1 Scaffolding and Build Tooling

Create React App (`create-react-app`) was officially deprecated in February 2025 and is no longer the recommended starting point. The React team now points developers at meta-frameworks (Next.js, Remix) or at build tools (Vite, Parcel, Rsbuild) for non-framework SPAs.

Vite is the de facto choice for SPA work in 2026. It provides native ES module serving in development (sub-second cold starts, near-instant HMR), uses Rollup (migrating to Rolldown, a Rust-based bundler) for production builds, and has a large plugin ecosystem that covers React, TypeScript, PWA, SVG handling, and legacy browser support. Project creation is a single command: `npm create vite@latest <n> -- --template react-ts`.

Parcel and Rsbuild are viable alternatives but have smaller ecosystems and less LLM coverage. They are not worth evaluating further for this project.

**Provisional recommendation:** Vite.

### 5.2 Framework Question: SPA vs Meta-Framework

This is the most consequential single decision. The research produced a clear answer.

Next.js is a full-stack React meta-framework. It offers server-side rendering, static site generation, incremental static regeneration, React Server Components, file-based routing, middleware, image optimization, and API routes. Its strength is building production-ready, SEO-friendly public applications where the framework owns both rendering and the server-side boundary.

CritterBids does not benefit from any of that. The backend is the Critter Stack. Introducing Next.js would mean either:

- Running a second web-tier service (the Next.js process) alongside the .NET application, doubling operational complexity.
- Using Next.js API routes to proxy the real backend, adding a weakly-typed pass-through layer that duplicates what Wolverine endpoints already provide idiomatically.
- Using React Server Components and server actions that talk to data sources, which implies either a direct database connection (bypassing the Critter Stack entirely) or HTTP proxy calls to the .NET backend (the same pass-through problem).

None of these improves the user experience during a live bidding demo. A Vite SPA, by contrast, is static HTML and JavaScript served by a reverse proxy, with all logic flowing through real Critter Stack endpoints and hubs.

The honest counterargument: if CritterBids ever needed listing pages to rank in Google search (eBay-style organic traffic), SSR becomes valuable. For a conference showcase and reference architecture, that is not a priority, and it is a decision that can be revisited if priorities shift.

**Provisional recommendation:** Vite-based SPA. No Next.js.

### 5.3 Language & Type Safety

TypeScript in strict mode. Non-negotiable for a project of this shape. Runtime validation at the wire boundary (Zod or Valibot) catches contract drift early.

ESLint with the TypeScript and React plugins. Prettier for formatting. These are all standard and well understood by every LLM that writes code.

**Provisional recommendation:** TypeScript strict, ESLint, Prettier, Zod for runtime validation.

### 5.4 Routing

Two serious options exist as of 2026: TanStack Router and React Router v7.

**TanStack Router** provides fully type-safe routing, generates types for all route parameters and search params, and treats search params as typed state with compile-time validation. It is the newer of the two and has strong TypeScript ergonomics. It also integrates well with TanStack Query.

**React Router v7** is the continuation of the Remix and React Router lineage. It is more broadly known, has more LLM training coverage, and offers a framework mode with its own loaders and actions. Its type safety has improved in v7 but does not match TanStack Router.

For an LLM-assisted codebase, React Router v7 has the advantage of familiarity; agents are more likely to write idiomatic code without specific guidance. For a type-safety-maximizing codebase, TanStack Router is the better fit.

**Provisional recommendation:** Park as an open question (see Section 9). Both are defensible. Lean toward React Router v7 unless type-safety-on-routes is a hard requirement.

### 5.5 Server State & Data Fetching

TanStack Query (formerly React Query) is the standard for managing server state in React applications in 2026. It handles caching, background refetching, loading and error states, stale-while-revalidate patterns, optimistic updates, mutation lifecycles, pagination, and infinite scroll. It effectively removes the need for Redux-style global state for anything server-derived.

For CritterBids, server state is the state that matters: current listings, bid history, auction status, user profile. All of this lives in Marten projections on the backend. TanStack Query's cache is the frontend mirror of those projections. Optimistic updates are particularly important for bidding UX; the user clicks Bid, the UI updates immediately, and if the server rejects the bid the cache rolls back.

Alternatives (SWR, RTK Query, Apollo Client) exist but do not improve on TanStack Query for this project.

**Provisional recommendation:** TanStack Query.

### 5.6 UI Components & Styling

**Styling: Tailwind CSS v4.** Mature, ubiquitous, heavily represented in LLM training data, and aligned with how nearly every modern React component library is built. Tailwind v4 uses a Rust-based engine with native CSS variable configuration and dramatically improved build times. No serious competitor in this space.

**Component library: shadcn/ui.** This is not a traditional component library. Its model is: a CLI copies component source files into your repository, and from that point forward those files are yours to modify. There is no npm dependency, no runtime stylesheet injection, no upgrade treadmill. Components are built on Radix UI primitives (which handle accessibility, keyboard navigation, focus management, and ARIA attributes) and styled with Tailwind.

The shadcn model is unusually well-suited to LLM-assisted development. Agents can read the actual component source in the repo, understand the pattern, and extend it without guessing at an opaque API. shadcn also has a large companion ecosystem: shadcnblocks, Origin UI, Magic UI, and others all follow the same copy-into-your-project convention.

Alternatives considered and rejected for this project:

- **Material UI, Chakra, Mantine.** Traditional npm-dependency component libraries. Higher upgrade friction, less LLM-friendly, heavier bundles.
- **Ignite UI for React, Infragistics.** Enterprise-focused, commercial. Overkill and not aligned with the open-source showcase nature of CritterBids.
- **Headless UI only.** Workable but forces us to build every component from scratch. shadcn already provides this plus sensible defaults.

**Provisional recommendation:** Tailwind CSS v4 plus shadcn/ui.

### 5.7 Forms & Validation

`react-hook-form` plus Zod is the de facto standard. shadcn/ui's `Form` component is built around this combination. Zod doubles as the runtime validator for incoming SignalR payloads and HTTP responses, which unifies the validation story at both directions of the wire.

**Provisional recommendation:** react-hook-form plus Zod.

### 5.8 Real-Time: SignalR Client Integration

The official client is `@microsoft/signalr`. It abstracts WebSockets, Server-Sent Events, and long polling behind a unified API, negotiates the best available transport per connection, and handles reconnection automatically when configured with `.withAutomaticReconnect()`.

The canonical React integration pattern:

1. A `SignalRProvider` React Context that owns the `HubConnection` lifecycle.
2. A `useHub` or `useListen(eventName, handler)` hook that subscribes component-scoped handlers and unsubscribes on unmount.
3. Integration with TanStack Query's cache via `queryClient.setQueryData` (to merge incoming events into cached queries) or `queryClient.invalidateQueries` (to trigger a refetch when a cache patch is too complex to compute).

The TkDodo blog post "Using WebSockets with React Query" is the canonical reference for this pattern and should be required reading before prototype work begins.

A newer option worth tracking but not adopting in the first milestone is **TanStack DB's query collections**, which provide `writeInsert`, `writeUpdate`, `writeDelete`, and `writeBatch` primitives specifically designed for reconciling WebSocket events into local collections without triggering full refetches. This maps well onto an event-sourced backend: each domain event becomes a collection mutation. It is newer and less battle-tested; revisit for a later milestone.

For bid velocity (multiple bids per second), three patterns matter:

- **Debouncing high-frequency display updates.** A 100-200 ms debounce on incoming bid updates prevents UI thrashing without feeling laggy.
- **React 19's `useTransition` / `startTransition`.** Marks bid-stream updates as non-urgent so typing in the bid input field does not block.
- **Exponential backoff with jitter on reconnection.** Start at 1 second, double up to 30 seconds, randomize by ±20 percent to avoid synchronization spikes if many clients reconnect at once.

**Provisional recommendation:** `@microsoft/signalr` with a custom `SignalRProvider` plus `useListen` hook, bridged into TanStack Query via cache updates.

### 5.9 Real-Time: Backend Scaling Considerations

This is backend concern, but it shapes frontend assumptions.

Redis acts as a pub/sub backplane between SignalR server instances. When CritterBids scales beyond one .NET instance, Redis synchronizes broadcasts so a bid placed against Server A reaches clients connected to Server B. Setup is trivial: add the `Microsoft.AspNetCore.SignalR.StackExchangeRedis` package and chain `.AddStackExchangeRedis()` onto `AddSignalR()`.

Two non-obvious facts that affect the frontend:

1. **Sticky sessions are still required with a Redis backplane.** SignalR negotiation is two steps (POST to `/hub/negotiate`, then WebSocket upgrade), and both must land on the same server. Caddy or Nginx in front of the .NET instances will need session affinity configured. The exception is when all clients are forced to WebSockets only and the client sets `SkipNegotiation: true`. For a conference audience on modern phones, forcing WebSockets is viable.
2. **Messages sent while Redis is down are lost.** SignalR does not buffer. This matters for bidding correctness. The mitigation is client-side reconciliation on reconnect: the frontend requests a snapshot of the current auction state (top bid, remaining time, recent bid history) from the Marten projection via HTTP, then resubscribes to the live stream. This pattern maps naturally onto the event-sourced backend and should be specified as part of the SignalR integration ADR.

Azure SignalR Service is not applicable (we are self-hosting on Hetzner).

### 5.10 Mobile-First & PWA

Mobile-first is a CritterBids requirement because the demo audience primarily uses phones. Three layers of concern:

**Responsive layout.** Tailwind's responsive prefixes (`sm:`, `md:`, `lg:`, `xl:`) make mobile-first the default. Design at `sm` and up, with phone being the base case.

**Touch-friendly UI patterns.** Tap targets at least 44x44 pixels. A persistent bottom bar for bid input so thumb reach is minimal. Sticky countdown timer. Careful handling of the mobile keyboard (it should not cover the bid input). shadcn/ui's `Drawer` component (based on Vaul) is specifically designed for mobile bottom sheets.

**PWA capabilities.** `vite-plugin-pwa` turns the SPA into an installable Progressive Web App with a service worker and a web app manifest. The conference demo flow becomes: audience scans QR, lands on CritterBids, gets prompted to "Add to Home Screen," and from then on the app launches full-screen with no browser chrome. Service workers also enable basic offline caching for static assets, which helps on weak conference Wi-Fi.

PWA-from-day-one vs PWA-retrofit is a real choice. From-day-one adds a small amount of setup and testing overhead (icons, manifest, service-worker caching strategy) but removes a later migration task. Retrofit is always possible but tends to get deferred indefinitely.

**Provisional recommendation:** Tailwind responsive breakpoints for layout, shadcn/ui Drawer for bottom-sheet patterns, PWA-from-day-one via `vite-plugin-pwa`.

### 5.11 Testing

**Unit and integration:** Vitest. Shares configuration with Vite; fast; idiomatic for the ecosystem.

**Component testing:** React Testing Library on top of Vitest. Standard.

**End-to-end:** Playwright. Particularly valuable for CritterBids because Playwright can drive multiple browser contexts in parallel, which lets us simulate a live bid war between two or more bidders in a single test. This is the kind of test that catches race conditions and reconciliation bugs that unit tests cannot.

**Provisional recommendation:** Vitest, React Testing Library, Playwright.

### 5.12 Deployment

Vite produces static HTML, CSS, and JavaScript. Any static file server or CDN serves them. The Hetzner architecture is straightforward:

- Caddy or Nginx in front, terminating TLS.
- Static Vite build served at `/`.
- .NET API reverse-proxied at `/api`.
- SignalR hubs reverse-proxied at `/hubs/*` with WebSocket upgrade headers properly forwarded.
- Redis and PostgreSQL colocated on the same VPS or on a sibling instance.

This setup requires no additional runtime for the frontend. Build output is pushed to the VPS via CI (GitHub Actions into `rsync` or `scp`) or built in place on the VPS.

**Provisional recommendation:** Static Vite build behind Caddy or Nginx. Decision on Caddy vs Nginx is a backend/ops concern and out of scope here.

---

## 6. Recommended Stack (Provisional)

Consolidated from Section 5. All recommendations are subject to the ADR process.

| Layer                      | Choice                                                | Confidence |
|----------------------------|-------------------------------------------------------|------------|
| Build tool                 | Vite                                                  | High       |
| Framework posture          | SPA (no Next.js)                                      | High       |
| Language                   | TypeScript strict                                     | High       |
| Linting / formatting       | ESLint, Prettier                                      | High       |
| Runtime validation         | Zod                                                   | High       |
| Routing                    | React Router v7 **or** TanStack Router                | Open       |
| Server state               | TanStack Query                                        | High       |
| Styling                    | Tailwind CSS v4                                       | High       |
| Components                 | shadcn/ui (plus ecosystem blocks as needed)           | High       |
| Forms                      | react-hook-form + Zod                                 | High       |
| Real-time client           | `@microsoft/signalr` with custom Provider + hooks     | High       |
| Real-time integration      | SignalR events bridged into TanStack Query cache      | High       |
| Mobile patterns            | Tailwind responsive, shadcn Drawer                    | High       |
| PWA                        | `vite-plugin-pwa`, from day one                       | Medium     |
| Unit / integration tests   | Vitest + React Testing Library                        | High       |
| E2E tests                  | Playwright (with multi-context bid-war tests)         | High       |
| Deployment                 | Static build behind Caddy or Nginx reverse proxy      | High       |

"High" means the research produced a clear answer and the choice is defensible without further investigation. "Medium" means the direction is clear but the specifics (for PWA: caching strategy, offline scope, install-prompt UX) warrant a dedicated deep-dive. "Open" means the research surfaced two defensible options and we want to defer the choice.

---

## 7. Architectural Concerns

Concerns that cut across the stack and should shape early decisions.

### 7.1 Contract Ownership Rule

The frontend consumes contracts; it does not define them. Concretely:

- Any HTTP endpoint contract (request shape, response shape, error model) is owned by the responsible .NET endpoint and its BC.
- Any SignalR hub method signature or event payload is owned by the hub's BC.
- Any integration event shape is owned by the publishing BC.

If a frontend prototype needs a contract to exist in order to make progress, the prototype **mocks** that contract. If the mock reveals useful shape information, the mock is brought to the backend milestone process as an input, not a decision. The backend may accept the suggested shape, counter with a different shape, or reject the premise. Either outcome is fine; what is not fine is the frontend pinning a contract and the backend inheriting it silently.

This rule should be captured in a short ADR or in the CritterBids convention docs.

### 7.2 Reconnection & Reconciliation

SignalR's reconnect is a connection-level primitive. It restores the socket. It does not restore application-level state. If a client was disconnected for ten seconds during a flash auction, it may have missed ten bids.

The reconciliation strategy should be specified explicitly and lives at the intersection of frontend and backend:

1. On reconnect, the client issues an HTTP request for a snapshot of the auction state (top bid, remaining time, last N bids, current phase).
2. The snapshot comes from a Marten projection and carries a version or sequence number.
3. The client resubscribes to the SignalR stream and filters incoming events against the snapshot's version to avoid double-counting.
4. If the snapshot version is far behind the stream, the client may need to poll again or degrade gracefully.

This is a concrete vertical slice that exercises the event-sourced backend, the SignalR transport, and the frontend cache reconciliation all together. It is a strong candidate for the first prototype spike once backend contracts exist.

### 7.3 Optimistic Updates & Bid Velocity

Optimistic updates are central to a responsive bidding UX. The pattern:

1. User submits a bid.
2. The frontend immediately updates the local TanStack Query cache with the bid in a "pending" visual state.
3. The frontend sends the bid via SignalR or an HTTP endpoint.
4. On acknowledgment, the pending state is promoted to confirmed.
5. On rejection (outbid, insufficient funds, auction ended), the cache rolls back and the user sees an error.

Bid velocity (multiple bids per second) compounds this. The frontend should coalesce rapid incoming updates using debouncing or React 19 transitions so the UI does not re-render thirty times per second. The server's authoritative ordering (Marten's append-only stream) is the source of truth; the frontend reconciles its optimistic state against that ordering.

### 7.4 Authentication on the Wire

The authentication story depends on the Participants BC and is upstream of this document. Two relevant facts for frontend planning:

- SignalR supports both JWT (via `accessTokenFactory`) and cookie-based auth. The choice affects CORS policy, sticky-session behavior, and how tokens refresh during a long-lived connection.
- Long-lived connections complicate token refresh. If a JWT expires mid-connection, SignalR does not automatically re-authenticate; the client needs to handle this explicitly (typically by catching a disconnect-with-auth-error and reconnecting with a fresh token).

This concern is flagged here and parked as an open question. A frontend ADR on auth client behavior will follow the Participants BC's own decisions.

### 7.5 Conference-Demo Reliability

Conference Wi-Fi is adversarial. Common failure modes:

- Captive portals that interfere with WebSocket upgrades.
- Proxies that silently kill idle connections after thirty seconds.
- Venues that block WebSocket traffic outright, forcing fallback to SSE or long polling.
- DNS latency on first load.

Mitigations that should be baked in from the start:

- Force WebSockets where possible, but test the SSE fallback path explicitly.
- Aggressive service-worker caching of the app shell so the app loads even if the backend is briefly unreachable.
- Visible connection-state indicator in the UI so users understand when they are offline vs connected vs reconnecting.
- Reconciliation on reconnect (see 7.2) so transient drops do not cause stale state.

---

## 8. Skills & Documentation Organization

A decision that emerged during research discussion: CritterBids should reorganize its `docs/skills/` tree into subject subtrees, starting with `docs/skills/frontend/`.

The status quo: all skills live in a flat `docs/skills/` directory. As the project grows, this will not scale cleanly, and it creates ambiguity when a frontend agent loads skills (it may pick up irrelevant backend skills, and vice versa).

The proposal:

1. Create `docs/skills/frontend/` now, and place all frontend-related skills there from the start (no retroactive migration required since no frontend skills exist yet).
2. Incrementally migrate existing skills into subject subtrees as they are touched: `docs/skills/backend/`, `docs/skills/database/`, `docs/skills/messaging/`, `docs/skills/testing/`, `docs/skills/devops/`. Category boundaries can be debated but should be few and obvious.
3. Update the `CLAUDE.md` routing rules so agents load skills from the appropriate subtree based on the milestone's scope.
4. Skills that are cross-cutting (for example, a general commit-message convention) stay at the top level or move to a `docs/skills/shared/` subtree.

This reorganization is low-risk because CritterBids has not yet accumulated the volume of skills that CritterSupply has. The cost of doing it now is small; the cost of deferring grows linearly with each new skill.

A short ADR capturing this decision is a reasonable companion to the frontend stack ADR.

---

## 9. Open Questions (Parked)

These questions have defensible answers in either direction and should not be resolved in this document. They will be addressed in their own milestones.

1. **TanStack Router vs React Router v7.** Type-safety maximalism vs ecosystem familiarity. Both are fine. Revisit when routing requirements are concrete (which routes exist, do we need search-param-as-state, does LLM fluency with the routing API matter more than IDE red squiggles).
2. **Single SignalR hub vs per-BC hubs.** Listings, Auctions, and Operations could share a hub or live on separate hubs. Separate hubs match BC boundaries and allow different auth per hub. Single hub is simpler to operate. Backend input required.
3. **JWT vs cookie authentication on the wire.** Depends on Participants BC decisions.
4. **Auction ops dashboard: separate app or same app?** The ops dashboard is internal and has a different audience than the bidder UI. It could be a separate Vite app at a different subdomain, a separate route tree in the same app gated by role, or a separate app entirely. Revisit once Operations BC has more shape.
5. **State management beyond TanStack Query.** For UI state (modals, theme, drawer open/closed), Zustand is the current lightweight default. TanStack Store is another option. React Context for truly small-scope state may also be sufficient. Defer until a concrete need arises; do not scaffold state management preemptively.
6. **PWA offline scope.** Static shell offline is easy. What about placing a bid offline and queuing it for sync? Probably not (bids have time semantics), but the boundary should be explicit.
7. **Visual identity and design tokens.** shadcn/ui ships with sensible defaults. CritterBids may or may not want custom branding. Out of scope for this document.
8. **Will the frontend also be used in the "Swapping the Bus" talk?** If so, the deploy story may need to demonstrate frontend-backend independence more explicitly (same build artifact deployed against a Wolverine-RabbitMQ backend and a Wolverine-ASB backend). Tangentially relevant.

---

## 10. Candidate ADRs

Ranked by confidence. We have discussed producing one, two, or three ADRs from this research. Recommended subset: ADR-FE-001 and ADR-FE-002. ADR-FE-003 is a strong candidate if backend concurrence on SignalR event patterns is ready.

> **Status update (2026-04-19):** The candidates below were realized as follows. ADR-FE-001 became **ADR 012 — Frontend: Vite SPA, Not a Meta-Framework** (Accepted). ADR-FE-002 became **ADR 013 — Frontend Core Stack** (Proposed). ADR-FE-003 remains a candidate and will become **ADR 014** once the Auctions BC has produced its first real hub. ADR-FE-004 is deferred as a separate convention ADR with number TBD.

### ADR-FE-001: CritterBids Frontend is a Vite SPA, not Next.js

**Confidence:** High. Clear answer from research. Sets the foundation for every other frontend decision.

**Core claim:** CritterBids ships a Vite-based React SPA. Next.js and other meta-frameworks are rejected because the Critter Stack is the backend and any meta-framework would either duplicate backend responsibilities or force an awkward pass-through layer. The SPA output is static assets served by the Hetzner reverse proxy alongside the .NET backend.

### ADR-FE-002: Core Frontend Technology Stack

**Confidence:** High for the stack as a whole; two items (routing, PWA timing) remain open.

**Core claim:** The CritterBids frontend uses React with TypeScript strict mode, TanStack Query for server state, Tailwind CSS v4 with shadcn/ui for styling and components, react-hook-form with Zod for forms and validation, `@microsoft/signalr` for real-time, Vitest plus React Testing Library for unit and integration tests, and Playwright for end-to-end tests. Routing and PWA-from-day-one are noted as open sub-decisions.

### ADR-FE-003: SignalR Client Integration Pattern

**Confidence:** Medium. The pattern is clear from research, but the concrete event shapes and hub structure require backend concurrence. This ADR may be better written after ADR-FE-001 and ADR-FE-002 and after the Auctions BC has produced its first hub.

**Core claim:** The frontend integrates SignalR via a `SignalRProvider` React Context owning the `HubConnection` lifecycle, a `useListen(event, handler)` hook for component-scoped subscriptions, and a bridge layer that translates incoming events into TanStack Query cache mutations (`setQueryData` for surgical updates, `invalidateQueries` for fuller refreshes). Reconnection uses `.withAutomaticReconnect()` with exponential backoff plus jitter. State reconciliation on reconnect is performed by fetching a projection snapshot over HTTP, then resubscribing to the stream.

### ADR-FE-004 (defer): Skills Organization into Subject Subtrees

**Confidence:** High but out of the frontend-stack lane.

**Core claim:** Reorganize `docs/skills/` into subject subtrees, starting with `docs/skills/frontend/` and migrating other skills incrementally as they are touched. Update `CLAUDE.md` routing to load skills from appropriate subtrees based on milestone scope.

This is better captured as a separate convention ADR rather than bundled into a frontend ADR.

---

## 11. Sequencing Plan

The frontend track runs as research and documentation only for Milestones RF-1 and RF-2. No prototype code. Prototype spike deferred to Milestone RF-3 at the earliest, and only after Auctions BC contracts are stable enough to inform it.

### Milestone RF-1: Frontend Stack Research (this milestone)

- Produce this research document.
- Draft ADR 012 (SPA vs meta-framework) and ADR 013 (core stack).
- Create `docs/skills/frontend/` directory in the CritterBids repo.

### Milestone RF-2: Real-Time and Skill Authoring

- Deep-dive on SignalR client integration patterns, including reconnection and reconciliation.
- Draft ADR 014 (SignalR integration pattern) if backend is ready to concur.
- Author initial frontend skills in `docs/skills/frontend/`:
  - `vite-spa-setup.md`
  - `tanstack-query-patterns.md`
  - `signalr-client.md`
  - `tailwind-shadcn-conventions.md`
  - `mobile-first-patterns.md`
- Review Open Questions list; resolve items that have stabilized.

### Milestone RF-3: First Prototype Spike

- Precondition: The Auctions BC (or whichever BC owns bid events) has produced at least one real hub with stable event shapes.
- Scope: one vertical slice, most likely the bid-stream reconciliation scenario (subscribe, receive bids, reconcile on simulated disconnect, apply optimistic update on local bid).
- Explicit scope statement: this prototype is a throwaway. Its purpose is to validate the integration pattern, not to seed the production codebase.
- Retrospective mandatory, per the standard session-workflow rule.

### Milestone RF-4 and beyond

- Begin production frontend work against established contracts.
- At this point, the frontend track joins the normal milestone cadence and is indistinguishable from backend milestones in terms of process.

---

## 12. Glossary

- **SPA.** Single-Page Application. A web app that loads a single HTML document and updates content dynamically via JavaScript. No server-side page rendering for navigation.
- **SSR.** Server-Side Rendering. The server renders HTML and sends it to the client, which hydrates it into a live React app.
- **SSG.** Static Site Generation. Pages rendered at build time into static HTML files.
- **PWA.** Progressive Web App. A web app with a service worker, a web app manifest, and offline-capable shell, installable to a user's home screen.
- **HMR.** Hot Module Replacement. Development-time feature where code changes are applied to the running app without a full page reload.
- **Optimistic update.** UI pattern where the client applies a mutation to local state immediately on user action, then reconciles with the server's authoritative response.
- **Sticky session.** Load-balancer behavior that routes all requests from a given client to the same backend instance. Required for SignalR when the Redis backplane is used.
- **Backplane.** Shared pub/sub layer used to synchronize messages across multiple SignalR server instances. Redis is the self-hosted default.

---

## 13. References

### Ecosystem overviews

- React docs, "Build a React app from Scratch" (react.dev): official guidance endorsing Vite for non-framework SPA starts.
- TanStack ecosystem overview (byteiota, 2026): current state of Query, Router, Start, and related libraries.

### Framework comparisons

- "Vite vs Next.js: Complete Comparison for React Developers (2026)" (designrevision.com): positioning of Vite as build tool vs Next.js as framework.
- "Next.js vs React + Vite: Do You Actually Need a Framework?" (techsy.io): SPA-behind-login vs SSR-for-SEO framing.
- "Vite vs Next.js 2025" (strapi.io): architectural philosophy comparison with backend-separation implications.

### SignalR integration

- `@microsoft/signalr` npm package documentation.
- Microsoft Learn: "Redis backplane for ASP.NET Core SignalR scale-out."
- Microsoft Learn: "ASP.NET Core SignalR production hosting and scaling."
- Milan Jovanović, "Scaling SignalR With a Redis Backplane."
- TkDodo, "Using WebSockets with React Query" (tanstack.com blog archive): canonical React Query + WebSocket integration pattern.
- TanStack DB docs: Query Collection and real-time `writeBatch` patterns.

### Component libraries and styling

- shadcn/ui documentation: "Tailwind v4" migration guide and component inventory.
- "ShadCN UI in 2026" (dev.to): ownership-model rationale and production adoption patterns.

### Mobile and PWA

- `vite-plugin-pwa` documentation.
- "Level Up Your Web App: Converting a Vite + React Project to a PWA" (Medium, Feb 2026).
- "Building an Offline-First React App" (January 2026): service worker and caching strategy patterns.

### Real-time bidding prior art

- Artsy Engineering, "The Tech Behind Live Auction Integration": operator + bidder topology lessons for a real live-auction product.
- MoldStud, "WebSockets Integration for Real-Time Bidding in React Auction Systems": debounce window guidance, reconnection backoff patterns.
