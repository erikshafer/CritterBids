# M6-S6: Relay BC — Remaining Routes + Notification History Projection

**Milestone:** M6 ([Obligations BC + Relay BC](../../milestones/M6-obligations-relay-bc.md))
**Slice:** S6 of 7 (complete Relay inbound surface + staff-feed handlers + notification history)
**Narrative:** [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) and [`docs/narratives/006-seller-fulfills-post-sale-obligation.md`](../../narratives/006-seller-fulfills-post-sale-obligation.md)
**Agent:** @PSA
**Estimated scope:** one PR; `Program.cs` route completion for remaining Relay queues, remaining Relay handlers, `NotificationHistoryView` projection, Relay-focused tests, and slice retrospective

---

## Goal

Complete Relay's remaining M6 scope after S5 by wiring all remaining inbound queues, adding the missing event handlers for `BiddingHub`/`OperationsHub`, and introducing a Marten-backed `NotificationHistoryView` so participant/staff notifications are queryable after disconnects.

S6 is still a **Relay-only slice**: no new BCs, no new saga behavior, no new cross-BC workflow. Relay remains a pure consumer and does not publish integration events.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M6-obligations-relay-bc.md` | S6 scope and route table (`relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events`) |
| `docs/retrospectives/M6-S5-relay-bc-scaffold-bidding-hub-retrospective.md` | S5 residue list and ADR 023 direction |
| `docs/decisions/023-relay-reactive-broadcast-architecture.md` | Authoritative Relay hub push architecture (plain `Hub` + `IHubContext`) |
| `docs/skills/wolverine-signalr/SKILL.md` | CritterBids SignalR routing/group conventions and testing posture |
| `src/CritterBids.Api/Program.cs` | RabbitMQ `ListenToRabbitQueue()` and publish-route topology |
| `src/CritterBids.Relay/` | Existing Relay hubs, handlers, and module shape from S5 |

## In scope

1. Add remaining Relay inbound `ListenToRabbitQueue(...)` wiring in `Program.cs`:
   - `relay-participants-events`
   - `relay-selling-events`
   - `relay-obligations-events`
   - `relay-listings-events`
2. Expand relevant publish routes in `Program.cs` so source events land on those queues.
3. Add missing Relay handlers for the remaining M6 queue/event set, including:
   - participant/session notifications
   - selling/listing lifecycle notifications
   - obligations notifications (`TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`)
   - listings watch notifications
   - settlement-side residual handler (`SellerPayoutIssued`) as part of full inbound completion
4. Add `OperationsHub` push handlers for the S6 staff-feed set (session/listing/bid/dispute/watch/seller-registration surfaces).
5. Add `NotificationHistoryView` (`relay` schema) and Marten registration via `services.ConfigureMarten(...)` in Relay module.
6. Add/extend Relay tests for:
   - remaining hub push handlers
   - notification history accumulation/query behavior
7. Write the S6 retrospective with baseline deltas and `## Spec delta — landed?`.

## Explicitly out of scope

- End-to-end cross-BC fan-out proof (`SettlementCompleted` driving both Obligations and Relay sibling consumers) — S7
- Full M6 milestone closeout retrospective and skill-extraction pass — dedicated closeout session
- New integration contract design unless a concrete missing contract blocks the queue/handler surface (if so, add minimal additive contract types and document in retro)
- React SPA SignalR client work, Relay HTTP endpoints, email/SMS/push provider integrations
- OpenSpec capability change authoring for Relay

## Conventions to pin or follow

- Relay handlers stay `Task`/`void`; no `OutgoingMessages`, no `IMessageBus` fanout.
- ADR 023 is authoritative: plain `Hub` + direct `IHubContext<THub>` push in handlers.
- Preserve existing group-key conventions:
  - `listing:{ListingId}`
  - `bidder:{BidderId}`
  - `ops:staff`
- Keep BC isolation rules intact in sibling test fixtures (`RelayBcDiscoveryExclusion` pattern).
- Add Relay Marten types via `services.ConfigureMarten()` only; do not introduce `AddMarten()` in Relay module.

## Spec delta

Per ADR 020. S6's spec impact is narrative-anchored and operational: the Relay notification surface expands from S5's partial `BiddingHub` subset to the full remaining inbound route set, plus persisted notification history. The retrospective confirms landed queue coverage, handlers, and `NotificationHistoryView` behavior, and records any additive contract adjustments that were required to complete S6.

## Acceptance criteria

- [ ] `Program.cs` includes `ListenToRabbitQueue(...)` for `relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, and `relay-listings-events`.
- [ ] Source publish routing exists for events feeding those queues.
- [ ] Remaining Relay handlers for S6 queue/event surfaces are implemented and green.
- [ ] `OperationsHub` receives the planned S6 handler set (session/listing/bid/dispute/watch/seller-registration coverage).
- [ ] `NotificationHistoryView` is implemented and registered through Relay `ConfigureMarten(...)`.
- [ ] Relay tests cover remaining pushes and notification history accumulation.
- [ ] `dotnet build` passes.
- [ ] S6 retrospective is written with `## Spec delta — landed?`, baseline/test notes, and any carried-forward follow-ups.

