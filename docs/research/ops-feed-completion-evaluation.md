# Relay Ops-Feed Completion — Staff-Architect Evaluation

**Date:** 2026-06-10
**Status:** Evaluation complete; **decision made — sanction the slice (pre-S7)**
**Decision owner:** Erik Shafer
**Origin:** M8-S6 prompt-authoring finding 2 (`docs/prompts/implementations/M8-S6-ops-dashboard-views.md` § Lived-backend findings); carried forward in `docs/retrospectives/M8-S6-ops-dashboard-views-retrospective.md` § What remains and narrative 008's v1.1 Document History row.

---

## The decision that needs to be made

**Should M8 sanction a small backend slice that completes the `OperationsHub` push feed, or close the milestone with the documented polling stopgap and defer the completion past M8?**

This is "Decision 1" of the two open decisions recorded at M8-S6 close (the other — dispute-resolution controls on the ops dashboard — is independent and not evaluated here).

### Background

At M8-S6 prompt authoring, reading the lived Relay handler topology (frontend-slice-discipline Rule 1, applied to the push surface) found that **the ops feed is a strict subset of what the six operator boards render**. These events reach `IHubContext<BiddingHub>` only — nothing publishes them as `OperationsFeedNotification` to the `OperationsHub`:

| Missing from the ops feed | Board consequence |
|---|---|
| `SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed` | **Settlement queue has zero live-push coverage** |
| `DeadlineEscalated` | Escalation *arrivals* are not pushed (departures are, via `DisputeOpened`) |
| `ListingPassed`, `ListingWithdrawn` (also `BiddingOpened`, `ExtendedBiddingTriggered`, `BuyItNow*`) | Lot board misses terminal transitions live |

The lived ops-feed vocabulary is 14 `eventType` values (enumerated in `client/ops/src/signalr/messages.ts`); the gap list above is what the boards' read models consume but the feed never announces.

Per frontend-slice-discipline Rule 2, M8-S6 rendered the lived subset and shipped a **documented stopgap**: a 20-second `refetchInterval` on exactly the two affected queries (`PUSH_GAP_REFETCH_INTERVAL_MS` in `client/ops/src/operations/queries.ts`), with this completion slice named as the work that retires it. M8's "renders the operator read models with live re-query refresh" exit criterion is satisfied *to the lived push vocabulary*, with the gap spec-visible — so closing M8 without this slice is process-legal.

### The two options

1. **Sanction the slice now (before M8-S7).** A third milestone-sanctioned backend exception (precedents: M8-S3a bid-placement endpoint, M8-S3c ADR 027 sticky bindings), its own prompt and PR: add `OperationsFeedNotification` publications to the Relay handlers for the missing events; delete the polling stopgap; add precise cache-bridge mappings.
2. **Defer past M8.** Close the milestone on the stopgap; the carry-forward stays recorded in the S6 retro and narrative 008 v1.1 for a future milestone to pick up.

---

## Evaluation

### 1. The gap is an accident, not a decision — that changes its category

Nobody decided "settlement events shouldn't reach the ops dashboard." The omission is a sequencing artifact: M6-S5/S6 built Relay handlers narrative-by-narrative, and the settlement events got `BiddingHub` handlers because narratives 001/002 (bidder journeys) needed them. The `OperationsHub` had zero consumers until M8-S5/S6, so the gap was invisible — the same mechanism as the M8-S5 `accessTokenFactory` discovery: a contract exercised by only one consumer shape looks complete until the second consumer arrives.

When a gap is an accident, completing it is **truth restoration, not scope expansion**. The repo has the exact precedent: M8-S3c (ADR 027) was sanctioned mid-milestone as "infrastructure truth-restoration only — no contract change, no new domain capability." This is the same shape, smaller.

### 2. The current state is the worst of both worlds, architecturally

The system currently pays for **both** mechanisms: a WebSocket connection with reconnect logic, staff auth, and a cache bridge — *and* interval polling on two boards. In production codebases that posture is the signature of an unfinished push migration. For a **reference architecture**, it is worse than inefficient — it is pedagogically wrong. The repo currently teaches: "push = re-query… except where we forgot to push, where it's polling." With the feed complete, every board teaches one clean rule (ADR 026).

Subtler operational cost: **polling masks pipeline failure.** With full push coverage, a silent ops board is a signal (the feed is down). With polling underneath, a dead push pipeline and a quiet auction are indistinguishable — observability traded for liveness that was never needed.

### 3. The cost is genuinely small, and the codebase proves it

Not speculative: `BidPlacedHandler` and `ListingSoldHandler` already demonstrate the dual-push shape (take `IHubContext<OperationsHub>` alongside the bidder hub; one extra `SendAsync` with an `OperationsFeedNotification`). The slice replicates that edit across the settlement, obligations, and Auctions-lifecycle handlers. No contract change, no migration, no new infrastructure; the M6/M7-S6 hub-assertion test patterns already exist. Runtime cost per event is one extra `Clients.All.SendAsync` in a single-process monolith: negligible.

Frontend reaction cost is **zero before any mapping is added** — the S6 cache bridge's unknown-`eventType` branch blanket-invalidates the `["operations"]` family, so the new pushes work the moment they ship. Precise bridge mappings are then a ~10-line diff in the same PR (frontend changes are unrestricted in M8 anyway).

### 4. "Polling is fine at this scale" — and why that's the wrong test

It is fine: two staff dashboards at 20 s is nothing. But the product-ready question is *what the pattern costs when copied*. Interval polling scales O(clients × interval) regardless of activity; push scales O(events). And the visible version of the cost is concrete: the conference-demo settlement completes in **milliseconds** and renders on a **0–20 second** timer, on the projector, during the gavel→charged beat the demo arc is built around.

The one thing interval polling was *accidentally* providing — recovery from missed pushes during disconnection windows — has a cheaper, event-driven replacement: a one-shot `["operations"]` family invalidation in the provider's `onreconnected` callback (plus TanStack Query's existing `refetchOnWindowFocus`). One refetch per reconnect instead of three per minute forever. The completion slice should include it; that is the correct reconciliation story for any push system, independent of this gap.

> The deeper principle: in an event-sourced system, **polling a read model is almost always a confession** — the events that change the model already exist, so polling means the notification topology has a hole. The legitimate uses of timers are reconciliation after known message-loss windows (reconnect) and genuinely event-less data (third-party state). Neither applies here, which is why the stopgap was correctly labeled a stopgap.

### 5. Sequencing: before S7, for two concrete reasons

- **S7's two-bidder e2e generates exactly the traffic that verifies this slice** — bids, a gavel, a settlement. Run the completion first and the milestone's flagship test exercises the finished feed for free. Run it after and the e2e validates the stopgap, then goes stale.
- **S7 is the doc-refresh slice** (STATUS, `bounded-contexts.md`, CLAUDE.md, milestone close-out). Close the milestone documenting the finished posture once, rather than documenting the stopgap and amending it a week later.

### 6. What "done" must mean — pin the invariant, not the event list

The mistake to avoid is completing *some* of the feed and recreating this bug class at M9. The acceptance criterion should be an invariant:

> **Every integration event that mutates an Operations BC read model has a corresponding `OperationsFeedNotification` publication.**

That is checkable (the Operations BC handlers enumerate their consumed events; diff against Relay's ops publications) and is a candidate for an assertion-style topology test. Two design details the slice prompt must settle:

- **`eventType` naming:** the new strings follow the dominant `nameof(Event)` convention. The `*Operations` suffix exists only for the two bidder-name collisions (`BidPlacedOperations`, `ListingSoldOperations`) and should not spread.
- **Stopgap retirement is in-scope:** delete `PUSH_GAP_REFETCH_INTERVAL_MS` outright; add the `onreconnected` invalidation; add the precise bridge mappings.

---

## Recommendation

**Sanction the slice. Run it before M8-S7, as one PR** (working name M8-S6b or S7-precursor): Relay handler completions + the invariant-shaped check + cache-bridge mappings + stopgap deletion + `onreconnected` reconciliation. Estimated effort: roughly half a session. The milestone then closes with the architecture it was always supposed to demonstrate, and S7's e2e and doc refresh both land against the finished posture.

The defer option is legitimate (exit criterion already satisfied, carry-forward recorded) but leaves the flagship demo's money-shot board on a timer and the reference architecture teaching a muddled rule through at least one more milestone.

## References

- `docs/prompts/implementations/M8-S6-ops-dashboard-views.md` — § Lived-backend findings (the gap's discovery and evidence)
- `docs/retrospectives/M8-S6-ops-dashboard-views-retrospective.md` — § S6.4 (the finding), § What remains (the carry-forward as recorded)
- `docs/narratives/008-operator-resolves-dispute-with-extension.md` — Document History v1.1 (the `DeadlineEscalated` gap named in spec)
- `client/ops/src/operations/queries.ts` — `PUSH_GAP_REFETCH_INTERVAL_MS` (the stopgap this slice retires)
- `client/ops/src/signalr/messages.ts` — the lived 14-value `eventType` vocabulary
- `src/CritterBids.Relay/Handlers/` — `BidPlacedHandler.cs` / `ListingSoldHandler.cs` (the dual-push template); the settlement/obligations/lifecycle handlers the slice edits
- ADR 023 (broadcast architecture), ADR 026 (push = re-query pattern), ADR 027 / M8-S3c (the truth-restoration sanctioning precedent)
