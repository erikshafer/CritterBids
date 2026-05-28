# Relay BC

**Maturity:** Planned-only.

**Evidence for the call:** No `src/CritterBids.Relay` project exists. No SignalR hub, notification handler, or routing type corresponding to this BC is present anywhere in `src/`. The BC is declared in `docs/vision/bounded-contexts.md` lines 165–186 and is referenced as the consuming endpoint for many integration events in the topology (lines 219, 225, 233, 239, 243, 246, 249). It is also named in `CLAUDE.md` lines 138.

This dossier records only what the vision doc declares.

---

## Business purpose (per vision doc)

> "All outbound communication and real-time push. Routes integration events from every other BC to the right participant via the right channel — SignalR for in-session alerts, email/SMS seams for production."
> — `docs/vision/bounded-contexts.md` line 167.

## What the vision doc attributes to it

From `docs/vision/bounded-contexts.md` lines 169–179:

- SignalR hub connections — manages participant connections and delivers real-time push.
- Notification routing — maps integration events to the correct participant(s) and channel.
- Notification history projection — participants can view their notification feed.
- Two SignalR hubs declared: `BiddingHub` (participant-facing) and `OperationsHub` (staff-facing). Line 179.

## Events attributed to it

From `docs/vision/domain-events.md` lines 110–112: Relay originates no domain events. It is a pure consumer.

## Integration topology (per vision)

- **In:** events from every other BC (`docs/vision/bounded-contexts.md` line 183). The topology lists explicit Relay routes for `ParticipantSessionStarted`, `SellerRegistrationCompleted`, `ListingPublished`, `ListingRevised`, `SessionCreated`, `SessionStarted`, `BidPlaced`, all significant Auctions events, `SellerPayoutIssued`, `PaymentFailed`, `ObligationFulfilled`, `DisputeOpened`, `LotWatchAdded`, `LotWatchRemoved`. Lines 219, 225, 233, 239, 243, 246, 249.
- **Out:** none in the integration-event sense; outbound is to external channels (SignalR, email seam, SMS seam) per line 185.

A single Settlement → Relay publish route exists in `src/CritterBids.Api/Program.cs` lines 145–156: `PublishMessage<SellerPayoutIssued>().ToRabbitQueue("relay-settlement-events")`. No `ListenToRabbitQueue` for that queue exists in `Program.cs`. The comment on lines 150–154 explicitly states the route is wired structurally with no consumer because Relay has not shipped; the publish rule is required for Wolverine.Tracking's `tracked.Sent` assertions.

## Storage (per vision)

PostgreSQL via Marten — notification history only. `docs/vision/bounded-contexts.md` line 181.

## Tests

None — there is no Relay project and no Relay test project. The publish-route test referenced above (`SellerPayoutIssuedPublishRouteTests`) lives under `tests/CritterBids.Settlement.Tests`, not a Relay test project.

## Open questions

None at the BC level beyond the BC's complete absence from code.
