# M8-S6b: Relay Ops-Feed Completion + Dispute-Resolution Control

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — slice plan §7, row M8-S6b (added v0.5)
**Slice:** S6b of M8, inserted before S7 — the third sanctioned backend exception (precedent S3a/S3c) plus the sanctioned narrative-008 Moment 2 scope addition
**Narrative:** `docs/narratives/008-operator-resolves-dispute-with-extension.md` (Moment 2 — the one place the operator *acts*). The feed-completion half serves the same operator vantage plus the demo spine's settlement beat (narratives 001/002 at the projector).
**Agent:** Claude Code
**Estimated scope:** one PR; Relay handler deltas + backend tests + `client/ops/` changes (~15 files). **No `CritterBids.Contracts` change.**

---

## Decision record this slice executes

Both halves were decided after S6 close (the two open decisions the S6 retro recorded):

- **Decision 1** (2026-06-10): sanction the ops-feed completion as a dedicated pre-S7 slice. Two independent evaluations converged — see `docs/research/ops-feed-completion-evaluation-comparison.md`, whose §Decision also fixes the in-scope extras (the `onreconnected` reconciliation and the enforced topology invariant) and adopts the 10-event gap inventory as the starting count.
- **Decision 2** (2026-06-11, the same doc's Addendum): sanction a frontend-only "Resolve with extension" control on the dispute card, folded into this slice rather than running as its own S6c.

## Goal

Make the `OperationsHub` feed complete — every integration event that mutates an Operations BC read model publishes a corresponding `OperationsFeedNotification` — so the polling stopgap (`PUSH_GAP_REFETCH_INTERVAL_MS`) is deleted, reconnection recovery becomes an explicit `onreconnected` one-shot invalidation, and the invariant is enforced by a topology test that fails whenever a future Operations-consumed event ships without an ops push. Then give Morgan the narrative-008 Moment 2 action: resolving a dispute with an extension from the dispute card, through the existing `StaffOnly` resolve endpoint. After this slice, S7's e2e validates the finished feed and its doc refresh documents the final posture once.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope.** §7 row M8-S6b; §3 non-goals. |
| `docs/research/ops-feed-completion-evaluation-comparison.md` (+ the two evaluations it compares) | The decision record: the gap inventory, the `onreconnected` requirement, the topology-test requirement, the Decision 2 Addendum. |
| `docs/narratives/008-operator-resolves-dispute-with-extension.md` | Moment 2 — what the control does and what Morgan sees when the card clears. |
| `src/CritterBids.Operations/` (handlers) + `src/CritterBids.Relay/Handlers/` | The two sides of the invariant: the consumed-event vocabulary vs. the published ops-feed vocabulary. The dual-push template lives in `BidPlacedHandler` / `ListingSoldHandler` / `ObligationsRelayHandlers`. |
| `.claude/skills/frontend-slice-discipline/SKILL.md` + `.claude/skills/signalr/SKILL.md` | The working rules: lived-surface-first, live-smoke close, ADR 026 bridge pattern, per-hub auth. |
| `docs/skills/wolverine-http-frontend-contract/SKILL.md` | The wire rules the mutation must obey (JSON body even for thin commands, camelCase STJ, ProblemDetails, no-body 202 handling). |
| `client/ops/src/` (`operations/queries.ts`, `signalr/cacheBridge.ts`, `signalr/SignalRProvider.tsx`, `operations/ObligationsQueues.tsx`, `auth/staffApi.ts`) | The S6 baseline this slice finishes: the stopgap constant, the bridge map, the provider, the read-only dispute card, the 401-funnelled fetch. |

## In scope

**Backend (Relay only):**

1. **Inventory first.** Enumerate every integration event type handled in `src/CritterBids.Operations/` and diff it against the lived 14-value ops-feed vocabulary. The evaluations' 10-event count (`SettlementCompleted`, `SellerPayoutIssued`, `PaymentFailed`, `DeadlineEscalated`, `TrackingInfoProvided`, `ObligationFulfilled`, `ListingPassed`, `ListingWithdrawn`, `BiddingOpened`, `ExtendedBiddingTriggered`) is the starting list; the invariant decides final membership — an event Operations does not consume needs no ops push, and an Operations-consumed event missing from that list still gets one.
2. **Dual-push deltas** to the existing Relay handlers — and a new handler method where Relay does not yet consume the event at all (`DeadlineEscalated` is the known case) — following the established template: inject `IHubContext<OperationsHub>`, one `Clients.All` send of an `OperationsFeedNotification`. No new notification record types; no payload-shape change.
3. **Topology test** enforcing the invariant mechanically ("every integration event with an Operations BC handler has a Relay `OperationsFeedNotification` publication"), plus integration coverage for at least the settlement-path publications (dual-push handler test precedent exists).

**Frontend (`client/ops/`):**

4. **Cache-bridge mappings** for the new `eventType`s (settlement events → settlement queue; `DeadlineEscalated` → escalations; `ListingPassed`/`ListingWithdrawn` and the other lifecycle events → lot board; obligations fulfilment events → the queue families they affect). Update the vocabulary comments in `messages.ts`/`cacheBridge.ts`; retire the `ListingSoldOperations` "cheap proxy signal" workaround note if the direct settlement pushes supersede it.
5. **Stopgap deletion.** `PUSH_GAP_REFETCH_INTERVAL_MS` and both `refetchInterval` usages are removed; the stale gap-comments in `queries.ts` and `ObligationsQueues.tsx` go with them.
6. **`onreconnected` reconciliation.** On hub reconnect, one one-shot invalidation of the `["operations"]` key family — events missed while disconnected are reconciled by re-query, the same authority rule as a push. Unit-tested.
7. **The dispute-resolution control.** On each dispute card (only when `disputeId` is non-null): a "Resolve with extension" action posting `{ obligationId, disputeId, resolutionType: "Extension" }` to `POST /api/obligations/disputes/resolve` through `staffFetch` (the 401 funnel rides along), expecting **202 Accepted** with no body. No optimistic cache write and no manual row removal — the `DisputeResolved` push re-queries both queues, and the card leaving the data **is** the success signal; the control shows a pending state until then. Failure (non-2xx) surfaces visibly on the card (ProblemDetails-aware), never silently. Vitest coverage for the mutation seam and the pending/error states.

**Close:** live smoke against the Aspire host per frontend-slice-discipline (resolve a dispute from the dashboard end-to-end; watch a settlement event move the settlement queue with no poll), the retro, and a narrative 008 Document History row.

## Explicitly out of scope

- **`Refund` / `Closed` resolution controls.** Refund compensation mechanics are a recorded M6/M7 non-goal; the narrative beat is the Extension path. The endpoint accepts all three values; the UI offers one.
- **The buyer's "report a problem" form / `OpenDispute` from any UI** — stays deferred (narrative 008's other deferred surface).
- **Any `CritterBids.Contracts` change** — no new integration events, no payload changes. If the inventory turns up a gap that cannot be closed without one, stop and escalate (Open question 1).
- **`OperationsFeedNotification` shape changes** — the wire stays the homogeneous four-field record (`listingId?`, `eventType`, `payload`, `occurredAt`).
- **Bidder app (`client/bidder/`) and the `BiddingHub` surface** — untouched.
- **Notification-history (`INotificationHistoryWriter`) expansion** — ops-feed publications do not gain history entries unless the existing template for that event already writes them.
- **S7 scope** — the Playwright e2e, the CI frontend job, and the doc refresh stay in S7.

## Conventions to pin or follow

- New Relay consumption (e.g. `DeadlineEscalated`) binds sticky to `relay-obligations-events` per ADR 027 — extend the existing `[StickyHandler]` class; never add a second sticky class for the same (message type, endpoint) pair.
- The push is a signal, never a payload (ADR 026): no push field is written into the TanStack cache as truth, including for the new event types and the `onreconnected` path.
- The mutation obeys `wolverine-http-frontend-contract`: JSON body with `Content-Type: application/json` (even for thin commands), camelCase keys, 202-no-body handled without a parse attempt.

## Spec delta

- `docs/narratives/008-operator-resolves-dispute-with-extension.md` gains a Document History row: Moment 2 (the operator acts — Extension resolution from the dashboard) is implemented; the dispute card clears live via the existing `DisputeResolved` push.
- ADR 026's push-equals-re-query contract becomes total for the ops app: the documented polling stopgap is retired and the topology invariant ("every Operations-consumed event has an ops push") is mechanically enforced going forward.
- The M8 exit criterion's "lived push vocabulary" qualifier recorded at S6 is removed: the settlement queue, escalation arrivals, and lot-board terminal transitions update live.

## Acceptance criteria

- [ ] The topology test exists, fails when an Operations-consumed event lacks an ops-feed publication (demonstrated once during development), and passes on the final tree.
- [ ] A search for `PUSH_GAP_REFETCH_INTERVAL_MS` and `refetchInterval` over `client/ops/` returns nothing.
- [ ] Every event in the final gap inventory appears in `cacheBridge.ts` with board-key targets; the vocabulary comment in `messages.ts` matches the lived handler surface.
- [ ] `onreconnected` triggers exactly one `["operations"]`-family invalidation per reconnect; unit-tested.
- [ ] The dispute card renders the Extension control only when `disputeId` is non-null; a successful resolve returns 202 and the card leaves the queue via push-driven re-query (no manual cache write in the mutation path).
- [ ] A failed resolve surfaces a visible error on the card; a 401 still funnels to the staff re-gate.
- [ ] `dotnet build` + `dotnet test` green; `npm test` green in `client/ops` (and `client/bidder`, untouched, still green); the ops tests remain inside the production type-check.
- [ ] Live smoke performed against the Aspire host and recorded in the retro: one dispute resolved from the dashboard end-to-end; one settlement event observed moving the settlement queue live with the stopgap gone.
- [ ] Retro authored (`docs/retrospectives/M8-S6b-ops-feed-completion-dispute-control-retrospective.md`); narrative 008 Document History row added.

## Open questions

1. **Contract-shaped gaps.** If completing coverage for any inventoried event would require a new `CritterBids.Contracts` type, a payload change, or a Relay queue binding beyond ADR 027's existing set — flag and stop; this slice's mandate is publication-only deltas.
2. **`BuyItNow*` events.** The S6 finding mentioned them parenthetically; they are in scope only if the inventory shows an Operations BC handler consumes them. If their consumption status is ambiguous in code, escalate rather than guess.
3. **Live `disputeId` nulls.** If the live smoke surfaces dispute rows with `disputeId: null` (a projection gap), do not synthesize an id — escalate; the control stays hidden for those rows.
