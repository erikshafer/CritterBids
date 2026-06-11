# TanStack DB Research — Client-Side Projections over the Critter Stack

**Status:** 📚 Parked — spike candidate (post-M8) · strong blog/talk material
**Date:** 2026-06-11
**Audience:** Future spike session; anyone reconsidering the ADR 026 SignalR integration pattern
**Reads with:** ADR 026 (SignalR cache-bridge, push = re-query), ADR 013 (frontend stack), ADR 027 (sticky bindings), `docs/research/frontend-stack-research.md`
**Sibling note:** MMO Reconnect evaluated and declined TanStack DB for its v1
(`C:\Code\mmo-reconnect\docs\research\tanstack-db-evaluation.md`) — no real-time requirement
there. CritterBids is where the use case actually lives.

---

## 0. Summary

TanStack DB is an embedded, reactive client-side database built on differential dataflow. Its
live queries are **incremental view maintenance — client-side projections** — which makes it the
most conceptually Critter-Stack-aligned thing in the React data ecosystem: Marten folds events
into projections server-side; TanStack DB folds collection deltas into query results
client-side. Projections all the way down.

For CritterBids specifically, it is best understood as a **candidate evolution of ADR 026**.
Today's pattern is *push = re-query signal*: a SignalR notification invalidates the TanStack
Query cache and the SPA refetches. TanStack DB offers a spectrum from "keep that exact
semantic, gain client-side live queries/joins" to "apply SignalR deltas directly into
collections and skip the refetch round-trip entirely."

**Verdict for now: do not adopt mid-M8.** It is beta (0.6.x) with breaking changes between
minors, M8-S5/S6/S7 have their own finish line, and ADR 026 is *proven* in the bidder app. But
a **post-M8 spike on one page** is cheap, and the write-up is a genuinely novel piece of .NET
content. Spike shape in §6.

---

## 1. Current state of the library (June 2026)

- `@tanstack/db` **0.6.x** (0.6.7 on npm late May 2026). **Beta**, explicitly.
- **0.5** introduced *query-driven sync* — the client query drives what syncs, eliminating
  per-view endpoint glue.
- **0.6** added SQLite-backed persistence across runtimes, hierarchical `includes` (projecting
  normalized collections into UI-shaped trees), reactive effects, offline transactions, and
  extended incremental sync to PowerSync and Trailbase (joining ElectricSQL and Query
  Collections).
- Team recruiting **SSR design partners on the road to v1** — read: v1 is quarters away, not
  weeks. Migration notes shipped for 0.5 → 0.6; expect more before 1.0.
- Adapters: React, Vue, Solid, Svelte, vanilla. Incremental adoption via `QueryCollection`
  wrapping existing TanStack Query usage.

## 2. The three primitives

| Primitive | What it is | Critter Stack analog |
|---|---|---|
| **Collection** | Typed client-side set of documents; populated by Query fetches, a sync engine, or custom sync | A client replica of a Marten projection (a set of view documents) |
| **Live query** | Incrementally maintained query over collections — joins, aggregates, sub-ms updates, fine-grained re-rendering (differential dataflow, same research lineage as Materialize) | A projection over projections; client-side `ViewProjection` |
| **Transactional optimistic mutation** | Write applies instantly as a local overlay; mutation fn fires the real request; overlay commits on accept, rolls back on reject | Command dispatch with optimistic UI; rollback maps to a 409 from `ConcurrencyConflictMiddleware` |

## 3. Fit against ADR 026 (the real question)

ADR 026's contract: **Relay pushes are signals, not data** — the SPA re-queries the
authoritative projection endpoint on notification. Proven across the full bidder journey
(bid → outbid → extension → gavel → settlement confirmation).

TanStack DB admits three postures, in ascending ambition:

1. **Conservative (drop-in):** wrap existing endpoints in `QueryCollection`s; keep push =
   re-query semantics untouched. Gain: client-side live queries and joins (e.g., the
   render-time `Title` join the ops views need — lot board rows joining
   `ListingId → CatalogListingView.Title` becomes a live two-collection join instead of
   per-row fetch glue), plus principled optimistic mutations for `PlaceBid`. ADR 026 survives
   unchanged.
2. **Delta-fed collections:** a custom collection sync applies SignalR pushes *as data*
   (upsert/delete of view documents keyed by id) instead of as invalidation signals. Skips the
   refetch round-trip; the event store → Relay → collection pipeline becomes a continuous
   replication stream of view-models. This **supersedes part of ADR 026** and would need its
   own ADR.
3. **ElectricSQL wildcard (note, don't do):** Marten projections *are* Postgres tables;
   Electric syncs Postgres to collections via logical replication, so zero-glue sync over
   projection tables is technically possible. Rejected as a direction: couples the client to
   Marten storage layout, and per-row authorization/visibility becomes the sync layer's
   problem. The contract boundary belongs at the API, where DDD put it.

### Design rules that hold in every posture

- **Ship view-model deltas, never domain events.** Relay already embodies this (notifications
  are shaped, not raw `IEvent` payloads). A delta-fed collection receives already-authorized,
  already-shaped documents. The client holds a replica of a projection, not of the log.
- **Optimistic confirm needs a synchronous verdict.** `PlaceBid` over the DCB endpoint with
  inline projection updates returns a definitive accept/409 in one round trip — a clean
  commit/rollback signal for the transaction overlay. Async projections would blur this;
  CritterBids' inline-heavy read side is unusually well-matched.
- **Mash bidding** (one click = one real bid, working-as-intended per the 2026-06-09
  disposition) is the stress test: N rapid optimistic transactions with interleaved 409s.
  If the overlay model handles that gracefully, it handles anything in this app.

## 4. Where it would shine in CritterBids

- **Live auction page:** bids × listing × auction-status joins with high-bid/bid-count
  aggregates, updating per-push with fine-grained re-renders.
- **Ops dashboard (M8-S6 surfaces):** lot board, bid activity, settlement queue, obligations —
  many concurrent live views over overlapping data is exactly the collections + live queries
  sweet spot, including the deferred render-time `Title` join. (S6 should still ship on ADR
  026 + plain Query: the milestone is not the place to absorb beta churn.)
- **`LiveActivity` ticker:** a transient collection with a live query replaces the manual
  dedupe-by-`kind+occurredAt+text` identity hack with keyed upserts.

## 5. Risks / costs

| Risk | Notes |
|---|---|
| Beta churn | 0.5 → 0.6 shipped migration notes; pre-1.0 by their own framing. Pin hard if spiking. |
| Skill investment | Six TanStack-Query-centric frontend skills are encoded (user-level). Adoption means new patterns to learn and (if adopted) a skill to author. Upstream ships SKILL.md files in-repo (v0.6 updated them) — evaluate those before writing our own. |
| Two data layers during migration | Query cache + collections coexist by design, but mixed-mode invalidation reasoning is a real (temporary) tax. |
| SSR | Not applicable — CritterBids SPAs are client-rendered. |

## 6. Proposed spike (post-M8 satellite, one session)

- **Scope:** one page — the live auction view in `client/bidder/` — on a branch. Posture 1
  first (QueryCollections + one live query + optimistic `PlaceBid`), posture 2
  (delta-fed bids collection from `BiddingHub`) only if posture 1 lands fast.
- **Baseline:** the existing ADR 026 implementation of the same page.
- **Evaluate:** glue-code delta (lines of cache-bridge code removed), re-render behavior under
  bid storms (mash bidding), 409-rollback ergonomics, DX against the encoded skills.
- **Exit:** a §Decision in this doc → either an ADR proposal (posture + migration shape) or a
  dated "stay on ADR 026, revisit at TanStack DB v1.0."

## 7. Content angle

This is a blog post / talk approximately nobody in .NET-land has written:
**"Projections All the Way Down: Marten, SignalR, and TanStack DB"** — server-side incremental
view maintenance (Marten projections) meeting client-side incremental view maintenance
(differential dataflow live queries), with the event store as the backbone and Relay as the
replication channel. Differential dataflow's research lineage (Materialize et al.) gives it
depth beyond library news. Natural home: event-sourcing.dev; natural demo vehicle: the spike
branch. Pairs with the existing backlog framing of CritterBids as the live-demo vehicle.

## 8. References

- TanStack DB repo / npm — `@tanstack/db` 0.6.x (beta), collections / live queries /
  transactional mutations
- TanStack blog: *TanStack DB 0.5 — Query-Driven Sync* and *TanStack DB 0.6 — persistence,
  includes, offline transactions* (Mar 2026; includes migration notes and the SSR
  design-partner call)
- InfoQ: *TanStack DB Enters Beta* (Aug 2025) — positioning vs. raw TanStack Query optimistic
  updates
- ElectricSQL / PowerSync / Trailbase collection adapters (sync-engine postures)
- ADR 026 (this repo) — the incumbent pattern any adoption must beat
