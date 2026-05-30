# M6-S6: Relay BC — Remaining Routes + Notification History Projection - Retrospective

**Date:** 2026-05-30  
**Milestone:** M6 - Obligations BC + Relay BC  
**Slice:** S6 - Relay remaining inbound consumers + OperationsHub pushes + Notification history  
**Agent:** @PSA  
**Prompt:** `docs/prompts/implementations/M6-S6-relay-remaining-routes-notification-history.md` (authored in companion worktree branch)

## Items completed

| Item | Description |
|---|---|
| S6a | Added Relay queue wiring in `Program.cs` for `relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events` and expanded `relay-auctions-events` publish routes |
| S6b | Added missing Relay handlers for Auctions/Selling/Participants/Obligations/Listings/Settlement (`SellerPayoutIssued`) |
| S6c | Added OperationsHub staff-feed handlers for session/bid/listing/dispute/watchlist/seller-registration notifications |
| S6d | Added `NotificationHistoryView` (`relay` schema) + `INotificationHistoryWriter` Marten-backed writer and Relay module `ConfigureMarten()` contribution |
| S6e | Added integration tests for new hub push handlers (`OperationsHubPushTests`, `BiddingHubRemainingPushTests`) and history accumulation test (`NotificationHistoryViewTests`) |
| S6f | Added narrative 001 Document History row v0.3 |

## Spec delta — landed?

**Landed.** Narrative 001 received a v0.3 row documenting the S6 Relay surface landing, including the additional inbound routes, full OperationsHub push set, and `NotificationHistoryView` becoming lived code.

## Open-question resolutions

1. **Missing contract types (`ListingRevised`, `ListingEndedEarly`, `LotWatchAdded`, `LotWatchRemoved`, `BidRejected`)**: authored as `sealed record` contracts with minimal payloads and wired routes/handlers.
2. **`SellerRegistrationCompleted` namespace mismatch**: retained existing root contract type (`CritterBids.Contracts.SellerRegistrationCompleted`) and wired Relay handler against it.
3. **Dispute/Tracking bidder-target payload gaps**: added optional additive fields (`TrackingInfoProvided.WinnerId`, `DisputeResolved.ParticipantId`) and implemented safe fallback behavior when IDs are absent.
4. **OperationsHub targeting**: standardized to `Clients.All` for S6 broadcast handlers; no new staff-group variant introduced in this slice.
5. **History model implementation**: implemented as handler-driven Marten document upsert (`NotificationHistoryView`) and registered via `services.ConfigureMarten()` in `AddRelayModule()`.

## Negative-space assertions (S6 close)

- Relay handler `IMessageBus` injections: **0**
- Relay handler `OutgoingMessages` returns: **0**
- Relay handler non-`Task`/`void` signatures: **0**
- Relay module `AddMarten()` calls: **0**
- Relay module `ConfigureMarten()` calls: **1** (`NotificationHistoryView`)

## Verification notes

- `dotnet build` passed (0 errors) after S6 edits.
- Relay hub push integration subset passed (`31/31`).
- Full `dotnet test CritterBids.slnx` run was blocked by environment Docker endpoint unavailability during Testcontainers-backed suites in this session.

## Owed documentation follow-up

Per S5 carry-forward, `docs/skills/wolverine-signalr/SKILL.md` still needs the lived CritterBids Relay guidance update reflecting ADR 023's plain `Hub` + `IHubContext` path.
