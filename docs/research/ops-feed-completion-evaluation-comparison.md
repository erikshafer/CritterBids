# Relay Ops-Feed Completion — Evaluation Comparison

**Date:** 2026-06-10
**Decision owner:** Erik Shafer
**Purpose:** Compare and contrast two independent architectural evaluations to inform the sanction/defer decision.

| | Evaluation A (Staff Architect) | Evaluation B (Event-Sourcing Specialist) |
|---|---|---|
| **File** | `ops-feed-completion-evaluation.md` | `ops-feed-completion-evaluation-es-specialist.md` |
| **Lens** | Modular monolith guardian, Critter Stack conventions, reference-architecture pedagogy | Event sourcing first principles, projection/notification topology consistency |
| **Model** | Claude Fable 5 | Claude Opus 4.6 |

---

## Where They Agree (Strong Signal)

Both evaluations independently arrive at the same recommendation — **sanction the slice** — but they reach it through different reasoning chains, which makes the convergence more meaningful than if they were restating each other.

### 1. The gap is accidental, not a design choice

- **A** frames it as a "sequencing artifact" — handlers were written when only BiddingHub had consumers, and the OperationsHub gap was invisible until M8-S5/S6 provided the second consumer.
- **B** frames the same observation in event-sourcing language: "the gap tracks a clear pattern — events introduced when only BiddingHub existed never got a second publication." Both call it completeness debt, not design disagreement.

### 2. The cost is genuinely small and mechanical

- **A** points to the existing dual-push template (`BidPlacedHandler`, `ListingSoldHandler`) and estimates "roughly half a session."
- **B** sizes it as "10 events, each a 5–10 line delta to an existing handler" — mechanical, no abstractions, no schema changes. Both agree the risk is low because the wire type (`OperationsFeedNotification`) and the handler pattern (`[StickyHandler]` + `IHubContext<OperationsHub>`) are already proven.

### 3. The reference-architecture argument is decisive for a project of this kind

- **A** says the current state "is pedagogically wrong — the repo currently teaches: push = re-query… except where we forgot to push, where it's polling."
- **B** says "the stopgap introduces a second data-freshness strategy that exists solely because the first strategy was incompletely applied" and calls it a "pedagogically useful ambiguity" — that it isn't one.

### 4. The conference demo's settlement beat is broken

- **A** specifically names the "gavel→charged beat the demo arc is built around" happening on a 0–20 second timer.
- **B** calls this "the decisive practical factor" and labels 20 seconds of "nothing happened" a "presentation-killing dead spot."

### 5. Deferring saves nothing

Both note the work does not shrink by waiting — the same handlers need the same edits regardless of when they're done.

---

## Where They Diverge (The Interesting Differences)

### Framing of the core problem

| Dimension | Evaluation A | Evaluation B |
|---|---|---|
| **Primary frame** | "Truth restoration, not scope expansion" — categorizing the work by CritterBids' own sanctioning precedent (M8-S3c) | "The notification topology and the projection topology are inconsistent" — a formal invariant violation in event-sourcing terms |
| **What breaks** | Emphasizes the *observable* cost: polling masks pipeline failure, the demo's money-shot board is on a timer | Emphasizes the *structural* cost: the push-re-query contract is a partial function, liveness guarantee is violated for three surfaces |

**Significance:** A is pragmatic (what does the user/operator see?); B is structural (what invariant is broken?). Both are valid — A is more persuasive to a product stakeholder, B to a systems architect.

### Depth on polling as a reconciliation strategy

- **A** goes deeper here. It explicitly argues that polling was *accidentally* providing reconnection recovery, proposes the correct replacement (`onreconnected` → one-shot `["operations"]` invalidation), and names that replacement as in-scope for the completion slice. It also articulates the O(clients × interval) vs O(events) scaling distinction.
- **B** mentions the polling/push dichotomy but doesn't prescribe the reconnection story. It treats the stopgap removal as purely subtractive (delete `refetchInterval`) without addressing what replaces the accidental reconnection benefit.

**Significance:** If you sanction the slice, A's `onreconnected` reconciliation point is a real deliverable that B misses.

### Sequencing recommendation

- **A** recommends a **dedicated pre-S7 slice** (working name M8-S6b or S7-precursor) — run the completion first, then S7's e2e test validates the finished feed.
- **B** recommends **folding into S7 itself** — the Playwright test becomes both the integration point and the validation.

**Significance:** This is a genuine tactical disagreement:
- A's approach (separate slice) gives the completion its own PR and retro, keeping S7's scope clean and the doc-refresh slice documenting the finished posture on the first pass.
- B's approach (fold into S7) reduces ceremony but risks S7 scope creep — the "e2e + doc refresh + close-out" slice gains ~10 handler edits + cache-bridge changes + stopgap deletion.

### The invariant proposal

- **A** proposes a specific **testable invariant**: "Every integration event that mutates an Operations BC read model has a corresponding `OperationsFeedNotification` publication" — and suggests an assertion-style topology test to enforce it going forward.
- **B** states the invariant informally but doesn't propose making it mechanically enforced.

**Significance:** A's topology-test idea is a valuable carry-forward that prevents this class of gap from recurring. Worth including in the slice prompt regardless of which sequencing approach is chosen.

### Scope of the gap count

- **A** names 6 missing event types explicitly (SettlementCompleted, SellerPayoutIssued, PaymentFailed, DeadlineEscalated, ListingPassed, ListingWithdrawn) plus a parenthetical mention of BiddingOpened/ExtendedBiddingTriggered/BuyItNow*.
- **B** counts 10 missing events more precisely (adds TrackingInfoProvided, ObligationFulfilled, BiddingOpened, ExtendedBiddingTriggered to the explicit list).

**Significance:** The broader count from B is more accurate for scoping the actual work. Some of those events (BiddingOpened, ExtendedBiddingTriggered) may not need ops push if they don't mutate a board the operator watches in real time — but the invariant says they should have it if the Operations BC handler consumes them.

---

## Summary for the Decision

| Question | Answer from both evaluations |
|---|---|
| Should the slice be sanctioned? | **Yes** — unanimous |
| Is the cost justified? | **Yes** — mechanical, low-risk, half a session |
| Is deferring defensible? | **Yes, but costly** — exit criterion is technically met; the cost is the demo beat, the pedagogical muddiness, and documenting/amending the stopgap |
| When should it run? | **A says pre-S7 (separate PR); B says folded into S7** — a genuine open question for Erik |
| What should the acceptance criterion be? | **A's invariant** ("every Operations-consumed event has an ops push") — stronger than B's implicit coverage list |
| Anything only one evaluation caught? | **A:** `onreconnected` reconciliation story, topology-test proposal. **B:** More precise gap count (10 events vs. 6+parenthetical) |

The evaluations reinforce each other on the **what** (sanction it) and **why** (accidental gap, mechanical fix, demo-critical, reference-architecture integrity). The remaining decision for Erik is the **when** (separate pre-S7 slice vs. folded into S7) and whether to carry A's topology-test and reconnection-reconciliation ideas into the slice prompt.

---

## Decision (Erik, 2026-06-10)

**Sanction the slice as a dedicated pre-S7 slice** (working name M8-S6b). Both evaluations' recommendations are accepted:

- **Pre-S7 sequencing** (Evaluation A's recommendation) — keeps S7 focused on its existing scope (e2e test + doc refresh + milestone close-out) and lets the completion slice have its own PR and retro.
- **Evaluation A's unique contributions are in-scope for the slice prompt:** the `onreconnected` reconciliation story (one-shot `["operations"]` family invalidation replacing the accidental polling recovery) and the topology-test invariant ("every Operations-consumed event has a corresponding ops push" — enforced, not just documented).
- **Evaluation B's precise 10-event gap count** informs the slice's scope inventory.

The slice prompt should be authored next session. S7 then lands against the finished posture — its e2e test validates the complete feed, and its doc refresh documents the final state once.
