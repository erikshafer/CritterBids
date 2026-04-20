# ADR 013 — Frontend Core Stack

**Status:** Proposed
**Date:** 2026-04-19

---

## Context

ADR 012 established that both CritterBids frontend applications ship as Vite-based single-page applications. A Vite SPA on its own is a build tool plus a language runtime; the *shape* of the application is defined by the libraries composed on top. This ADR captures the core set of libraries that together define what a CritterBids frontend looks like.

The research behind these choices is recorded in `docs/research/frontend-stack-research.md`. This ADR is the formal commitment to the subset of that research that has high confidence. Decisions with open tradeoffs (routing library, UI state management beyond server state, authentication client pattern, SignalR integration pattern) are explicitly deferred to later ADRs or milestone-level decisions and are listed under "Deferred Questions" below.

### Why one ADR rather than many

Each library choice is nominally separable, but they form a coherent composition. TanStack Query's cache becomes the bridge between SignalR events and the UI. shadcn/ui's `Form` component is designed around `react-hook-form` + Zod. The testing strategy depends on Vitest being Vite-native. Splitting these into eight or ten ADRs would lose the coherence without adding decision-making value; each sub-decision has a clear answer and the ADRs would be near-identical in rationale (modern, LLM-friendly, deeply trained in the 2026 ecosystem, composable with the other choices).

This ADR therefore captures the sub-decisions together, each with a brief rationale. Where a sub-decision is genuinely contested (routing, UI state), it is deferred rather than forced.

### The philosophy: focused-library composition

The CritterBids frontend adopts the composition approach that has become the 2026 React mainstream: a small set of focused libraries, each solving one problem well, assembled into a coherent whole. Two alternatives were considered and rejected:

- **Heavyweight integrated component libraries** (MUI, Chakra, Mantine). These ship a large catalog of components styled by a single design system, plus hooks and utilities. They accelerate early work but create upgrade friction, ship larger bundles, and are less amenable to copy-into-project LLM workflows.
- **Ultra-minimal roll-your-own with headless primitives only**. Using Radix UI directly and building every visual component from scratch offers maximum control but pays for it with significant authoring cost. shadcn/ui already provides this foundation plus sensible defaults, so there is no reason to redo the work.

The focused-composition approach also aligns with the CritterBids-as-reference-architecture framing. Readers can substitute individual libraries (swap TanStack Query for SWR, swap Tailwind for CSS Modules) without replacing the entire stack.

---

## Scope

### In Scope

- Language and baseline tooling (TypeScript, linting, formatting).
- Runtime validation at the wire boundary.
- Server state management.
- Styling system.
- Component library.
- Forms and validation.
- Real-time client library (the *integration pattern* is deferred to ADR 014).
- Testing strategy (unit, integration, end-to-end).
- Progressive Web App posture (day-one adoption vs retrofit).

### Out of Scope (deferred)

- Routing library (TanStack Router vs React Router v7). Revisited when routing requirements are concrete.
- UI state management beyond server state (Zustand, TanStack Store, or just React Context). No preemptive choice; adopted when a concrete need surfaces.
- Authentication client pattern (JWT vs cookie, token refresh strategy). Waits on Participants BC.
- SignalR integration pattern (Provider, hook, TanStack Query cache bridge). ADR 014, after the first real hub exists on the backend.
- Visual design tokens, branding, accessibility audit process.

---

## Decision

### Language and tooling

**TypeScript in strict mode.** Non-negotiable for a reference architecture. Strict mode catches contract drift at compile time and pairs with Zod to catch it at runtime.

**ESLint with the TypeScript and React plugins; Prettier for formatting.** Standard, broadly understood, and LLM-idiomatic. No bespoke alternatives warranted.

**Zod for runtime validation** at the wire boundary. Every incoming HTTP response body and SignalR payload is parsed through a Zod schema before the rest of the application trusts it. This catches backend-frontend contract drift the moment it happens, rather than letting it propagate into the cache or UI. Alternatives (Valibot, io-ts) considered; Zod chosen for ecosystem depth and shadcn/ui `Form` component alignment.

### Server state management

**TanStack Query.** The 2026 standard for managing server state in React. Handles caching, background refetching, loading and error states, stale-while-revalidate, optimistic updates, mutations, pagination, and infinite scroll. For CritterBids, TanStack Query's cache is the frontend mirror of Marten projections; optimistic updates are central to the bidding UX (user clicks Bid, UI updates immediately, rolls back on server rejection). Alternatives (SWR, RTK Query, Apollo Client) do not improve on TanStack Query for this project.

### Styling and components

**Tailwind CSS v4** for styling. Mature, ubiquitous, deeply trained in LLM weights, and the foundation of nearly every modern React component library. Tailwind v4's Rust-based engine offers fast builds and native CSS-variable configuration. No serious competitor for this project's shape. Alternatives considered and rejected: CSS Modules (more verbose, less consistent), styled-components or Emotion (runtime CSS-in-JS overhead, declining ecosystem).

**shadcn/ui** as the component library. The copy-into-project model (CLI copies component source into `components/ui/`, after which the files belong to the repo) solves both the upgrade-treadmill problem and the LLM-fluency problem: agents can read the actual component source, understand the pattern, and extend it without guessing at an opaque API. Components are built on Radix UI primitives (accessibility, keyboard navigation, focus management, ARIA) and styled with Tailwind. Ecosystem companions (shadcnblocks, Origin UI, Magic UI) follow the same convention and are available as needed. Alternatives considered and rejected: MUI, Chakra, and Mantine (traditional npm-dependency libraries with upgrade friction and heavier bundles); headless-only (higher authoring cost with no benefit over shadcn/ui's starting point).

### Forms and validation

**`react-hook-form` + Zod.** The de facto standard pairing. shadcn/ui's `Form` component is designed around this combination, so adopting it is essentially free. Zod schemas used for form validation also serve as runtime validators for HTTP request bodies and SignalR invocation payloads; the validation logic is written once and applied at both ends of the wire.

### Real-time client library

**`@microsoft/signalr`.** The official client. Abstracts WebSockets, Server-Sent Events, and long polling behind a unified API; negotiates the best available transport per connection; handles reconnection automatically when configured with `.withAutomaticReconnect()`. No credible alternative for integrating with ASP.NET Core SignalR hubs.

**Important scope note:** this ADR chooses the *library*. The integration *pattern* (a `SignalRProvider` React Context, a `useListen(event, handler)` hook, and a bridge into the TanStack Query cache) is deferred to ADR 014, which requires backend concurrence on hub method signatures and event shapes. The frontend will not pin the integration pattern in advance of the backend's first real hub.

### Testing

**Vitest + React Testing Library** for unit and component tests. Vitest shares configuration with Vite and runs fast; React Testing Library is the standard for component testing in 2026. Alternatives (Jest) are viable but introduce a second configuration surface.

**Playwright** for end-to-end tests. Playwright's ability to drive multiple browser contexts in parallel within a single test is particularly valuable for CritterBids: a test can simulate a live bid war between two or more bidders against a real backend, catching race conditions and reconciliation bugs that unit tests cannot. Alternatives (Cypress) considered; Playwright chosen for multi-context support and better TypeScript story.

### Progressive Web App

**`vite-plugin-pwa` from day one.** Both frontend applications ship with a service worker and web app manifest from their first commit. For `critterbids-web`, this enables the conference demo flow: audience scans a QR code, lands on the app, is prompted to "Add to Home Screen," and from that point launches the app full-screen with no browser chrome. For `critterbids-ops`, the same capability provides offline-shell caching on weak conference Wi-Fi.

The *scope* of offline behavior (app shell only vs. caching listings data vs. queueing offline bids) is deliberately not decided here; it will be captured in a frontend skill. What this ADR commits to is the PWA posture — the manifest, the service worker, the installable behavior — which is cheaper to adopt on day one than to retrofit later.

---

## Consequences

**Positive:**

- **Coherent composition.** The library choices are mutually reinforcing: Tailwind styles shadcn components, TanStack Query caches data that SignalR events update, `react-hook-form` + Zod handles both form validation and wire validation, Vitest and Playwright share Vite's config. Agents writing code that composes these libraries will produce idiomatic results.
- **LLM-friendliness is maximized across the board.** Every choice has deep training-data coverage. Agents rarely need specific prompting to "use library X idiomatically" because X is already their default.
- **Ownership of UI code.** shadcn/ui's copy-into-project model means component source lives in the CritterBids repository. No hidden abstractions, no upgrade surprises, no wondering why a Tailwind class does not win over a library's internal styles.
- **Mobile-first UX is natural.** Tailwind responsive prefixes, shadcn `Drawer` for bottom sheets, and PWA installability together give CritterBids a demo-ready mobile experience from the first sprint.
- **Testing depth matches the real-time nature of the app.** Playwright multi-context tests are the right tool for validating a bid war under realistic conditions; Vitest keeps the unit-test inner loop fast.

**Negative:**

- **More moving parts than a heavyweight integrated library would have.** Eight or nine library names instead of "just MUI." Mitigation: each library is focused, well-documented, and ubiquitous in 2026; onboarding cost is minimal for any developer familiar with the React ecosystem.
- **Component source is owned by the repo, so security and bug fixes must be pulled in manually.** If a Radix primitive has a CVE or shadcn fixes a component bug upstream, the fix is a manual re-copy of the affected component. Mitigation: in practice, shadcn components are stable and this is a rare event; the ownership tradeoff is net-positive.
- **PWA-from-day-one adds setup friction on the first commit.** Icons, manifest, service worker registration, offline strategy decisions. Mitigation: the configuration is small and covered by `vite-plugin-pwa`; retrofitting later is more expensive.
- **No single vendor to blame when something breaks across the stack.** A bug that spans TanStack Query + SignalR + Zod requires debugging across three library boundaries. Mitigation: each library has excellent diagnostics; the integration surface between them is explicit rather than hidden behind framework magic.

**Neutral:**

- **This stack is aggressively modern.** It assumes developers are current on 2026 React idioms. For a reference architecture, that is appropriate. For legacy-team modernization, it would not be.
- **The composition model invites readers to swap individual libraries.** This is a *feature* for a reference architecture (the stack is legible and substitutable) but means the implementations are not locked together behind abstractions.

---

## Revisit Triggers

This decision should be reconsidered if any of the following become true:

- A library in this stack is effectively abandoned (no releases or security fixes for 12+ months) and its community coalesces around a successor.
- The LLM-fluency premise inverts: a new library emerges with substantially better agentic authorship characteristics and gains broad training-data coverage.
- PWA adoption in conference-demo contexts reveals a platform limitation (iOS Safari behavior, service-worker restrictions) significant enough to warrant native wrappers (Capacitor, Tauri, etc.).
- The frontend discovers a pattern that cannot be expressed idiomatically in this stack and requires a framework-level feature.

---

## Deferred Questions

Explicitly parked rather than resolved by this ADR. Each will be addressed in its own milestone or a subsequent ADR.

- **Routing library.** TanStack Router (fully type-safe, newer) vs React Router v7 (broader ecosystem familiarity, larger LLM training footprint). Both are defensible. Revisit when concrete routing requirements exist (which routes, what search-param-as-state is needed, how much IDE type-safety matters vs. LLM fluency).
- **UI state management beyond server state.** For modal open/closed, theme, ephemeral UI state: Zustand is the current lightweight default. TanStack Store is a credible alternative. React Context may be sufficient. Do not scaffold state management preemptively; adopt when a concrete need arises.
- **Authentication client pattern.** JWT with refresh tokens vs cookie-based auth. Affects CORS, sticky-session requirements, and SignalR `accessTokenFactory` behavior. Waits on Participants BC decisions.
- **SignalR integration pattern.** ADR 014. Provider + hook + TanStack Query cache bridge. Requires backend concurrence on at least one hub's contracts.
- **PWA offline scope.** App shell only vs. caching listings data vs. queueing offline bid submissions. Captured in a frontend skill, not an ADR.

---

## Relationship to Other ADRs

| ADR | Effect |
|---|---|
| ADR 001 — Modular Monolith | **Unaffected.** Library choices are internal to the frontend build output. |
| ADR 004 — React for Frontend Applications | **Extended.** ADR 004 picked React + TypeScript; this ADR picks the ecosystem around them. |
| ADR 012 — Frontend: Vite SPA, Not a Meta-Framework | **Depends on.** SPA posture from ADR 012 enables the focused-library composition approach adopted here. |
| ADR 014 — SignalR Integration Pattern (forthcoming) | **Depends on this.** Client library choice (`@microsoft/signalr`) established here; integration pattern is ADR 014's subject. |

---

## References

- ADR 001 — Modular Monolith Architecture
- ADR 004 — React for Frontend Applications
- ADR 012 — Frontend: Vite SPA, Not a Meta-Framework
- `docs/research/frontend-stack-research.md` — underlying research (§5.3 through §5.11)
- TanStack Query docs: https://tanstack.com/query/latest
- shadcn/ui docs: https://ui.shadcn.com
- Tailwind CSS v4 docs: https://tailwindcss.com
- `@microsoft/signalr` on npm: https://www.npmjs.com/package/@microsoft/signalr
- `vite-plugin-pwa` docs: https://vite-pwa-org.netlify.app
- `docs/skills/frontend/` (to be created) — implementation conventions
