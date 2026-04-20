# ADR 012 — Frontend: Vite SPA, Not a Meta-Framework

**Status:** Proposed
**Date:** 2026-04-19

---

## Context

CritterBids needs a frontend shape for its two React applications (`critterbids-web` for participants, `critterbids-ops` for staff). ADR 004 established React with TypeScript as the frontend language and library; it did not establish the framework posture. The remaining question is whether the React code runs as a client-side single-page application, as a server-rendered meta-framework application, or as some hybrid.

The decision matters because it determines:

- Whether CritterBids runs one deployable process (the .NET API host) or two (the .NET host plus a Node.js runtime for the frontend).
- Whether contracts are owned exclusively by the Critter Stack BCs or shared with frontend-defined route loaders and server actions.
- Whether the frontend can be served as static assets by the existing reverse proxy on the Hetzner VPS or requires its own runtime and deploy pipeline.
- The operational surface area for conference demos, where every additional moving part is a risk.

### The Critter Stack is the centerpiece

CritterBids exists to showcase the Critter Stack. Backend BCs, Wolverine message handling, Marten event-sourcing, and Aspire orchestration are the point of the project. The frontend exists to make this feel real during live demos and to give the system a usable face. It complements the backend; it does not compete with it.

Any framework choice that positions the frontend as an independent application tier with its own data-fetching layer, its own API routes, or its own server runtime creates a second backend. That second backend cannot use the Critter Stack idiomatically — it is JavaScript/TypeScript on Node, not .NET on Wolverine. It either duplicates contracts that the real backend already owns or introduces a pass-through layer that adds no value.

### Deployment model

ADR 001 (Modular Monolith) established that CritterBids runs as a single deployable unit on a single Hetzner VPS with minimal operational overhead. A frontend that requires its own Node.js runtime violates the spirit of that decision. A static Vite build, by contrast, is a pile of HTML/CSS/JS files that the existing Caddy or Nginx reverse proxy serves alongside the .NET endpoints — no new runtime, no new deploy target, no new health check.

### SEO is not a driver

The CritterBids audience is conference attendees who scan a QR code and log in. There is no public search-engine discoverability requirement. The ops dashboard is internal by definition. A public marketing page, if one ever exists, is a separate concern and can be a static HTML page or a separately-deployed landing site.

### LLM-assisted authorship

A substantial portion of the frontend will be written by coding agents. Ubiquitous, well-trodden patterns with deep training coverage reduce the friction of agentic development. A Vite + React + TypeScript SPA is the most common React shape in the ecosystem and has the broadest LLM fluency. Meta-framework patterns (server components, server actions, file-based route loaders) are also well-represented but introduce semantic complexity that is harder to get right on the first try.

---

## Options Considered

### Option A — Next.js (meta-framework)

Deploy Next.js as the frontend. Use React Server Components for data-heavy pages, client components for interactive UI, and server actions or API routes to broker calls to the Critter Stack backend.

**Evaluation:** Next.js is the strongest meta-framework in the React ecosystem and excels at SEO-driven public applications and full-stack SaaS where the framework owns both rendering and the data boundary. For CritterBids it introduces structural problems:

- A second runtime process (Node.js) sits alongside the .NET API host, doubling the deployment surface. Sticky-session requirements for SignalR become more complex to reason about when there is an intermediate tier.
- Server actions and API routes become a second, weaker backend. Either they proxy the Critter Stack endpoints (pure overhead) or they talk to storage directly (bypassing the Critter Stack, which defeats the purpose of the showcase).
- Many Next.js optimizations (image CDN, edge middleware, ISR) are coupled to Vercel as a hosting platform. Self-hosting Next.js on Hetzner works but surrenders the ergonomics that make Next.js attractive.
- Next.js's opinions (file-based routing, server components, the app router) are additional surface area that future agents and contributors must learn alongside the Critter Stack. CritterBids's value as a reference architecture is clearest when the frontend is minimal and the backend is the focal point.

**Decision: rejected.**

### Option B — Remix framework mode (React Router v7) or TanStack Start

Use a React Server Components-era meta-framework that is less Vercel-coupled than Next.js. React Router v7 in framework mode is the continuation of Remix's loader/action model. TanStack Start is built on Vite and markets itself as a less-opinionated alternative to Next.js.

**Evaluation:** Both reduce some of the Next.js concerns (both are Vite-based; TanStack Start in particular is less coupled to any hosting platform), but they do not eliminate the fundamental issue: they still introduce a Node.js runtime and a server-side data layer that competes with the Critter Stack rather than complementing it. Server-rendered loaders and actions invite frontend agents to define contracts unilaterally, which violates the contract-ownership rule established in the frontend research document.

TanStack Start specifically is a release-candidate stage product as of April 2026. Less battle-tested than Next.js, with a smaller ecosystem. Adopting it for a reference architecture carries additional stability risk.

**Decision: rejected.**

### Option C — Vite SPA

Build both frontends as client-side single-page applications using Vite as the build tool. Static HTML/CSS/JS output. All data and real-time traffic flows through the existing .NET API endpoints and SignalR hubs. No Node.js runtime in production. Deploy by copying build output into a directory served by the Hetzner reverse proxy.

**Evaluation:** This is the shape that preserves ADR 001's modular-monolith deployment simplicity, respects the Critter-Stack-as-centerpiece framing, and keeps contract ownership unambiguously on the backend side. Initial page load is somewhat slower than a server-rendered alternative (a client has to fetch and execute JavaScript before rendering), but for an authenticated bidding application this is negligible — the user is going to authenticate and connect a WebSocket anyway, so an extra 200ms of startup time is not a UX issue.

Trade-offs:

- No SEO out of the box. Accepted, because there is no SEO requirement.
- No streaming SSR. Accepted, because CritterBids is a real-time WebSocket application where server streaming concerns are handled by SignalR rather than by HTML streaming.
- Slightly larger client bundle than equivalent Next.js projects that aggressively code-split. Mitigated by Vite's Rollup (and eventually Rolldown) production builds with automatic code-splitting per route.

**Decision: chosen.**

---

## Decision

Both CritterBids frontend applications (`critterbids-web` and `critterbids-ops`) are built as **client-side single-page applications using Vite as the build tool**. No Next.js, no Remix framework mode, no TanStack Start, no server-side rendering.

Each application builds to static HTML/CSS/JS artifacts. Those artifacts are served by the same reverse proxy that fronts the .NET API. No additional runtime process is introduced in production. All data and real-time traffic flows through Critter Stack endpoints and SignalR hubs.

---

## Consequences

**Positive:**

- **Single deployable unit preserved.** ADR 001's modular-monolith deployment model is unaffected. `docker compose up` continues to bring up the full CritterBids stack; the frontend build output is just one more static asset bundle served by the existing reverse proxy.
- **Contract ownership stays on the backend.** No server-side data layer on the frontend means no opportunity for the frontend to define contracts unilaterally. Any contract the frontend needs is a Wolverine endpoint or a SignalR hub method, which the responsible BC owns.
- **Operational simplicity for conference demos.** One process, one runtime, one log stream. Fewer things to explain to the audience when the demo inevitably surfaces an operational question.
- **LLM fluency is maximized.** Vite + React + TypeScript is the most common and broadly-trained React shape in the 2026 ecosystem.
- **Deploy-anywhere.** Static output runs on any web server, CDN, or S3-style object store. No vendor coupling. The "Swapping the Bus" conference demo story is reinforced: backend can be swapped, frontend can be redeployed independently, neither requires the other to be present.

**Negative:**

- **No server-side rendering means no SEO for any public pages.** If CritterBids ever needs organic discoverability for listings, this decision must be revisited. Low probability given the conference-demo scope.
- **Initial page load requires client-side JavaScript execution.** A few hundred milliseconds slower than SSR for the first render. Negligible for authenticated flows.
- **No framework-level conventions for routing, data fetching, or code-splitting.** The frontend must assemble its stack from focused libraries (TanStack Query, a routing library, etc.) rather than inheriting them from the framework. This is addressed by ADR 013.

**Neutral:**

- **Explicitly different from CritterSupply's frontend shape (Blazor WASM).** CritterBids already differs on language (React vs Blazor) per ADR 004; the SPA-vs-meta-framework choice is a further axis of differentiation and is consistent with the "demonstrate that .NET backends pair with non-Microsoft tooling" framing.
- **Meta-framework patterns (server components, server actions) are not part of the CritterBids vocabulary.** Contributors and future agents need to understand that the frontend does not have "server-side code" in the Next.js sense. All server-side logic is in the Critter Stack.

---

## Revisit Triggers

This decision should be reconsidered if any of the following become true:

- Public, unauthenticated listing pages are added to CritterBids with an SEO requirement (organic search traffic to individual listings).
- The conference demo story adds a "deploy the frontend on its own edge runtime" beat that depends on framework-level optimizations Vite cannot provide.
- A future Critter Stack project adopts a different frontend shape (Blazor, Next.js, or otherwise) and CritterBids is asked to demonstrate the alternative for consistency.

---

## Relationship to Other ADRs

| ADR | Effect |
|---|---|
| ADR 001 — Modular Monolith | **Reinforced.** Single deployable unit preserved; no additional runtime process. |
| ADR 004 — React for Frontend Applications | **Extended.** ADR 004 chose React; this ADR chooses the framework posture for that React code. |
| ADR 013 — Frontend Core Stack (forthcoming) | **Depends on this.** The SPA choice determines which routing, data-fetching, and UI-component libraries are relevant. |

---

## References

- ADR 001 — Modular Monolith Architecture
- ADR 004 — React for Frontend Applications
- `docs/research/frontend-stack-research.md` — underlying research (§5.2 Framework Question)
- React team guidance on build tools and meta-frameworks: https://react.dev/learn/build-a-react-app-from-scratch
- `docs/skills/frontend/` (to be created) — implementation conventions
