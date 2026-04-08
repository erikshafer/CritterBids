# ADR 004 — React for Frontend Applications

**Status:** Accepted  
**Date:** 2026-04

---

## Context

CritterBids requires two frontend applications:

- `critterbids-web` — participant-facing SPA (bidding, listings, seller dashboard)
- `critterbids-ops` — staff-facing ops dashboard (live lot board, saga state, demo controls)

CritterSupply uses Blazor WASM. CritterBids deliberately uses a different technology to demonstrate that .NET backends pair naturally with non-Microsoft frontend tooling — a common misconception in the .NET community.

Four options were evaluated: React, Vue, Svelte, Angular.

---

## Decision

CritterBids uses **React with TypeScript** for both frontend applications.

---

## Rationale

- **React** has the widest recognition in a conference room — the "look, not Blazor" message lands with any developer audience
- Most SignalR community examples and client library documentation target React — less friction for the real-time bid feed implementation
- TypeScript is non-negotiable for a reference architecture — type safety for SignalR message shapes and API contracts
- Vue was considered — clean and familiar, but less of a statement
- Svelte was considered — would generate the most interest from frontend developers, but higher learning curve for .NET-focused contributors
- Angular was the last choice — viable but heavyweight for this use case

---

## Consequences

**Positive:**
- Broadest audience recognition — any developer will understand the "not Blazor" point immediately
- Rich SignalR client ecosystem
- Clear separation from CritterSupply's Blazor story

**Negative:**
- React ecosystem churn is a real maintenance concern for a reference architecture — mitigated by keeping dependencies minimal

---

## Conventions

See `docs/skills/react-frontend.md` for React + TypeScript + SignalR conventions specific to CritterBids.

Both SPAs point at the same `CritterBids.Api` host. The ops dashboard is protected by staff auth config. The participant-facing app uses anonymous sessions.

---

## References

- `docs/vision/overview.md` — demo-first philosophy, frontend scope
- `docs/skills/react-frontend.md` — implementation conventions (pending)
