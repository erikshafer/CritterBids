# CritterBids M9-S4 Handoff

**Written:** 2026-06-13
**From:** M9-S3 session (seller query endpoints + housekeeping)
**`main` at:** `f800a9f` — post-M9-S3 merge (PR #106)

---

## What shipped in M9 so far

| Slice | PR | What landed |
|---|---|---|
| M9-S1 | #104 | `client/shared/` extraction (SignalR provider, Zod schemas, theme CSS), seller SPA scaffold (`client/seller/`), Aspire child registration (`:5175`), CI frontend job extended |
| M9-S2 | #105 | `POST /api/selling/listings/submit`, `PUT /api/selling/listings/{id}/draft`, `GET /api/selling/listings?sellerId=`; `SellerListingSummary` inline snapshot projection in Selling BC; 19 new tests |
| M9-S3 | #106 | `GET /api/obligations/status?sellerId=`, `GET /api/settlement/summaries?sellerId=` + `SellerSettlementSummary` handler-driven projection; Listings `ExtendedBiddingTriggered` handler + publish route (M8-S7 carry-forward); cache-bridge burst-final hardening evaluated → deferred; 10 new tests |

**All five seller-facing endpoint gaps from the M9 milestone audit (§2) are closed.** Backend precursor slices complete.

---

## Current baselines

- **.NET build:** 0 errors, 2 CS0108 warnings (saga Version hiding — baseline since M8-S7)
- **.NET tests:** 326 all pass (10 projects)
- **Frontend Vitest:** 74 (bidder 25, ops 47, seller 2)
- **Playwright e2e:** 1 (local-only, two-bidder bid-war)
- **npm workspace members:** 5 (bidder, ops, seller, shared, e2e)
- **Engine:** Wolverine 6.8.0 / .NET 10 / Aspire 13.4.3

---

## What M9-S4 is

Per `docs/milestones/M9-seller-console.md` §7, slice table row:

> **M9-S4: Seller SPA — registration + listing management**
> Seller app shell + layout; seller registration flow (the one-click `RegisterAsSeller` from the session); listing dashboard ("my listings" view consuming the S2 query endpoint); create-draft form (`react-hook-form` + Zod validation against listing-time fields); edit-draft; submit-for-publication; withdraw. Narratives 004 Moments 1–5.

This is the first **frontend-heavy** M9 slice. The backend is fully shipped — every endpoint the seller console needs exists. S4 wires the seller SPA to the backend surfaces.

### Endpoints the seller SPA will consume in S4

| Endpoint | Method | BC | Purpose |
|---|---|---|---|
| `POST /api/participants/session` | POST | Participants | Start anonymous session |
| `POST /api/participants/{id}/register-seller` | POST | Participants | One-click seller registration |
| `GET /api/selling/listings?sellerId=` | GET | Selling | "My listings" dashboard |
| `POST /api/listings/draft` | POST | Selling | Create draft listing |
| `PUT /api/selling/listings/{id}/draft` | PUT | Selling | Update draft listing |
| `POST /api/selling/listings/submit` | POST | Selling | Submit draft for publication |
| `POST /api/selling/listings/withdraw` | POST | Selling | Withdraw published listing |

### What already exists in `client/seller/`

The M9-S1 scaffold provides:
- Vite + React + TS strict setup with dev proxy to `:5180`
- `SessionContext` — anonymous session management (same pattern as bidder)
- `SignalRProvider` — BiddingHub connection via `@critterbids/shared`
- `AppShell` with nav, `ConnectionIndicator`, basic routing
- `HomePage` placeholder
- Tailwind v4 consuming `@critterbids/shared/theme.css`

### Narratives to render

Read `docs/narratives/004-seller-publishes-and-withdraws-listing.md` for the story:
- **Moment 1:** Seller registers (one-click from session)
- **Moment 2:** Seller creates a draft listing (title, format, starting bid, reserve, BIN, duration, extended bidding)
- **Moment 3:** Seller reviews and submits the draft
- **Moment 4:** Seller sees the listing published (auto-approval)
- **Moment 5:** Seller withdraws a published listing

---

## Carry-forwards still open

| Item | Source | Target |
|---|---|---|
| Cache-bridge burst-final hardening (delayed re-invalidation) | M8-S7 → M9-S3 deferred | M9-S5 or M9-S7 — bake into `@critterbids/shared` |
| Extended-bidding e2e banner assert (code comment in `bid-war.spec.ts`) | M8-S7 e2e | M9-S7 (backend handler now exists) |
| `STATUS.md` regeneration (stale at M8 close, doesn't reflect M9 work yet) | Noticed this session | M9-S7 or any housekeeping slice |

---

## Next-slice candidates and decision context

**The clear next is M9-S4** (seller registration + listing management). There is no ambiguity — the milestone doc's slice table is sequential and S1–S3 are done. However, if the session determines S4 is too large for one slice, it could be split:

- **S4a:** Seller app shell, registration, "my listings" dashboard (read-only view)
- **S4b:** Create-draft form, edit-draft, submit, withdraw (write operations)

The M8 precedent (S3a/S3b/S3c split for the backend precursor + bidding + ADR 027) validates splitting when a slice's surface area is too broad. The decision should be made after reading the narrative and the existing seller scaffold — if the draft-creation form alone needs Zod schemas, react-hook-form wiring, and five field groups, it may warrant its own slice.

After S4, the remaining slices are:
- **M9-S5:** Live auction observation (BiddingHub, real-time bid feed, reserve indicator)
- **M9-S6:** Obligation fulfillment (obligation tracker, provide-tracking form, deadline countdown)
- **M9-S7:** End-to-end + housekeeping (Playwright seller e2e, CI extension, doc refresh, retros)

---

## Suggested skills

| Skill | Why |
|---|---|
| `docs/skills/frontend-slice-discipline/SKILL.md` | **Required** — governs all M9 frontend slices; the verification ladder, backend-exception discipline, live-smoke rules |
| `docs/skills/critter-stack-testing-patterns/SKILL.md` | If any backend test additions are needed (unlikely for S4 but possible if endpoint contract issues surface) |

Also read:
- `docs/narratives/004-seller-publishes-and-withdraws-listing.md` — the story S4 renders
- `client/seller/src/` — the existing scaffold to build on
- `client/bidder/src/` — the precedent seller should follow for session management, TanStack Query, and form patterns
- `client/shared/` — the shared SignalR/schema surface S4 consumes

---

## Session workflow reminder

- Author the M9-S4 implementation prompt per `docs/prompts/README.md`
- Execute on a new branch (`m9-s4-seller-registration-listing-management` or similar)
- Live-smoke the seller SPA against the running Aspire stack (`dotnet run --project src/CritterBids.AppHost`)
- Retrospective included, one PR
