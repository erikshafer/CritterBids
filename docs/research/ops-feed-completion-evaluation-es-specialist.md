# Relay Ops-Feed Completion — Event-Sourcing Specialist Evaluation

**Date:** 2026-06-10
**Status:** Evaluation complete; **decision made — sanction the slice (pre-S7)**
**Decision owner:** Erik Shafer
**Evaluator lens:** Staff-level software architect — event sourcing, distributed systems, .NET / Critter Stack (Wolverine + Marten)
**Origin:** Same decision as `ops-feed-completion-evaluation.md`; independent second opinion requested for compare-and-contrast.

---

## 1. Architectural Assessment: The Notification Gap Through Event-Sourcing Principles

The core invariant of the push-re-query pattern (ADR 026) is: **every event that mutates a read model must emit a notification that triggers re-query of that read model.** This is not a nice-to-have — it is the contract that makes the pattern coherent. Without it, the notification topology and the projection topology are inconsistent, and the frontend silently degrades from reactive to polling without any structural signal that this has happened.

In event-sourcing terms, the projections are correct — they fold every relevant event. The gap is purely in the notification layer: the Relay BC's handler coverage is a partial function over the Operations BC's event vocabulary. This is not a data-integrity problem (the read models converge on the next poll or page load), but it is a **liveness problem**: the ops dashboard's contract promises real-time visibility, and three entire read-model surfaces (settlement queue, escalation arrivals, lot-board terminal states) violate that promise.

The architectural smell is that the gap is not random — it tracks a clear pattern. Events that were introduced when only the BiddingHub existed never got a second publication when the OperationsHub was added. This is a completeness debt, not a design disagreement.

## 2. Engineering Trade-Offs: Cost, Risk, Timing

**Cost is low.** The template is established (`BidPlacedHandler` dual-push pattern). Each missing handler needs: inject `IHubContext<OperationsHub>`, add one `SendAsync` call with an `OperationsFeedNotification`, add the cache-bridge mapping. This is mechanical — 10 events, each a 5–10 line delta to an existing handler (or a small new handler for `DeadlineEscalated`). No new abstractions, no schema changes, no migration.

**Risk is low.** The Relay BC's sticky-handler pattern is proven. The `OperationsFeedNotification` wire type is homogeneous — no new record types. The cache-bridge mapping is additive. The frontend change is subtractive (delete `refetchInterval`, delete the `PUSH_GAP_REFETCH_INTERVAL_MS` constant). Integration test coverage for dual-push handlers already exists as precedent.

**Timing is tight but workable.** M8-S7 is scoped as e2e test + doc refresh + close-out. Adding this slice either requires a dedicated S6.5/S7a or folding it into S7. Given the mechanical nature, folding it into S7's scope is defensible — the e2e test benefits from complete push coverage anyway (the Playwright test can assert on real-time updates rather than waiting for a 20-second poll).

## 3. Reference-Architecture Implications

A reference architecture that ships with a documented polling workaround for an incomplete notification topology teaches the wrong lesson. The entire point of ADR 026's push-re-query pattern is that it is **uniform**: every mutation triggers a push, the frontend never polls. The stopgap introduces a second data-freshness strategy (interval polling) that exists solely because the first strategy was incompletely applied.

For someone studying this codebase to learn event-sourced real-time patterns, the stopgap is confusing. It raises the question "when should I poll vs. push?" — and the answer is "never, we just didn't finish." That is not a pedagogically useful ambiguity.

Completing the feed turns the Relay BC into a clean example of exhaustive notification coverage, which is the pattern worth teaching.

## 4. Conference Demo Implications

This is the decisive practical factor. The demo's money shot — gavel, settlement, charged — flows through exactly the read models that have zero push coverage. The settlement queue board will sit inert for up to 20 seconds after each settlement event before the poll fires. In a live demo with an audience, 20 seconds of "nothing happened" after the gavel drops is a presentation-killing dead spot.

The lot board's missing terminal transitions (ListingPassed, ListingWithdrawn) are less dramatic but still visible: lots that should visually resolve on-screen will hang in their pre-terminal state until the next navigation or poll.

## 5. Recommendation

**Sanction the slice. Complete it before M8 closes.**

The work is mechanical (10 handler deltas following an established template), low-risk (additive publications on a proven wire type), and directly enables the milestone's stated exit criterion without asterisks. It eliminates a polling workaround that contradicts the architecture's own ADR, and it unblocks the conference demo's critical path.

Fold it into M8-S7 rather than creating a separate slice. The e2e Playwright test in S7 is a natural integration point — it can validate that the push notifications arrive, which is a stronger assertion than testing against a polling fallback. The deliverable is:

1. Add `IHubContext<OperationsHub>` + `OperationsFeedNotification` publication to each of the 10 gap handlers in Relay
2. Add cache-bridge mappings for the new notification event types
3. Delete `PUSH_GAP_REFETCH_INTERVAL_MS` and `refetchInterval` from the two affected query options
4. Cover at least the settlement-queue push path in the e2e test

The two prior M8 backend exceptions (bid-placement endpoint, sticky bindings) were both sanctioned because they unblocked frontend work that could not proceed without them. This exception is the same category: the frontend's real-time contract cannot be fulfilled without backend completion. The pattern of sanctioning small, well-scoped backend slices when they directly serve the frontend milestone's goals is consistent and defensible.

Deferring saves no meaningful effort (the work does not get smaller later) and ships a milestone whose showcase feature — live ops visibility — visibly does not work for its most important board.
