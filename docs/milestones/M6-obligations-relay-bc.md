# M6 — Obligations BC + Relay BC

**Status:** Planning
**Scope:** Obligations BC (post-sale coordination saga with cancellable scheduled messages) and Relay BC (SignalR-based real-time push and notification routing for participant and staff UIs). Combined milestone because Relay's first real integration is the Obligations-produced events, and the two BCs are both small enough to ship together without scope risk.
**Companion docs:** [`../workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md) · [`../vision/bounded-contexts.md`](../vision/bounded-contexts.md) · [`../vision/domain-events.md`](../vision/domain-events.md) · [`../skills/wolverine-sagas.md`](../skills/wolverine-sagas.md) · [`../skills/wolverine-signalr.md`](../skills/wolverine-signalr.md) · [`../skills/marten-projections.md`](../skills/marten-projections.md) · [`../skills/critter-stack-testing-patterns.md`](../skills/critter-stack-testing-patterns.md) · [`../decisions/README.md`](../decisions/README.md)

---

## 1. Goal & Exit Criteria

### Goal

Deliver two BCs that together complete the post-sale participant experience and introduce the real-time push layer.

**Obligations BC** is the post-sale coordination saga triggered by `SettlementCompleted`. It schedules a chain of seller-side shipping reminders, cancels them when the seller provides tracking information, escalates missed deadlines to Operations staff, and manages the dispute sub-workflow if something goes wrong. Cancellable scheduled messages are the defining pattern — this is the canonical CritterBids lived example of `bus.ScheduleAsync()` combined with saga-state-driven cancellation.

**Relay BC** is the outbound hub for everything real-time. It consumes integration events from every other BC and routes them to the correct participant(s) via SignalR. Two hubs: `BiddingHub` (participant-facing real-time bid feed) and `OperationsHub` (staff-facing live dashboard feed). Relay also owns a notification history projection in Marten so participants can see what happened while they were disconnected. Relay never originates domain events — it is a pure consumer.

At M6 close, the Flash auction demo journey runs end-to-end from QR-scan through real-time bid updates, settlement, and obligation kick-off, with live SignalR push visible in two browser windows on the same projector.

### Exit criteria

- [ ] Solution builds clean with `dotnet build` — 0 errors, 0 warnings
- [ ] Obligations BC implemented: `CritterBids.Obligations` and `CritterBids.Obligations.Tests` projects, `AddObligationsModule()`, Marten config per BC-module conventions and ADR 011
- [ ] Relay BC implemented: `CritterBids.Relay` and `CritterBids.Relay.Tests` projects, `AddRelayModule()`, Marten config per BC-module conventions and ADR 011
- [ ] Obligations saga hosting decision made in M6-S1, recorded as an ADR (next unreserved: `020-<slug>.md`)
- [ ] Demo-mode timeout config for Obligations saga decided in M6-S1 (W001-6 closed: PO decision on whether to use a configurable cap, a separate `DemoMode` appsettings flag, or hardcoded short durations); decision recorded inline in the M6-S1 retrospective if not ADR-worthy
- [ ] Obligations saga happy path green: `PostSaleCoordinationStarted` → shipping-reminder chain → `ObligationFulfilled`; all cancellation paths (tracking provided cancels reminders) green
- [ ] Obligations saga escalation path green: missed deadline → `DeadlineEscalated`; dispute sub-workflow (`DisputeOpened` → `DisputeResolved`) green
- [ ] `CritterBids.Contracts.Obligations.*` integration events authored: `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`
- [ ] At least one dispatch test per Obligations command exercising the Wolverine routing path
- [ ] Relay BC `BiddingHub` wired: real-time push for `BidPlaced`, `BiddingOpened`, `ListingSold`, `ListingPassed`, `ReserveMet`, `ExtendedBiddingTriggered`, `ProxyBidExhausted`, `SettlementCompleted`
- [ ] Relay BC `OperationsHub` wired: real-time push for `SessionCreated`, `SessionStarted`, `BidPlaced`, `ListingSold`, `ListingPassed`, `DisputeOpened`, `DisputeResolved`, `DeadlineEscalated`
- [ ] Relay notification history projection implemented: Marten document projection of delivered notifications; queryable by participant ID
- [ ] All new RabbitMQ routes wired in `Program.cs`; `relay-settlement-events` gains its `ListenToRabbitQueue()` call (pre-wired publish-only since M5-S6)
- [ ] Relay and Obligations BC discovery added to `Program.cs`'s `opts.Discovery.IncludeAssembly(...)` calls
- [ ] `AddObligationsModule()` and `AddRelayModule()` called in `Program.cs`
- [ ] M6-S1 through final-session retrospective docs written
- [ ] M6 retrospective doc written

---

## 2. In Scope

### Obligations BC — core components

| Component | What it owns | Design source |
|---|---|---|
| `PostSaleCoordination` saga | Post-sale lifecycle from `SettlementCompleted` to `ObligationFulfilled`; schedules reminder messages with `bus.ScheduleAsync()`, cancels them when tracking is provided, escalates missed deadlines, manages dispute sub-workflow | `bounded-contexts.md` Obligations section; `domain-events.md` Obligations events |
| Reminder chain | Scheduled `SendShippingReminder` message(s); configurable timing (demo-mode config decision in S1 per W001-6); produces `ShippingReminderSent` (internal) | W001-6 open question — closed in M6-S1 |
| Escalation path | `SendDeadlineEscalation` message fires if `TrackingInfoProvided` has not arrived before deadline; produces `DeadlineEscalated` (internal); notifies Operations BC via `DeadlineEscalated` on `relay-obligations-events` | `bounded-contexts.md` |
| Dispute sub-workflow | `DisputeOpened` (integration) on report; `DisputeResolved` (integration) on ops resolution; `MarkCompleted()` after resolution | MVP scope only: open / ops-resolve / close; no appeals, no multi-round negotiation |
| Tracking seam | HTTP carrier-tracking endpoint stubbed in MVP (in-process handler); real webhook receiver is post-MVP | `bounded-contexts.md` Obligations key decisions |

### Relay BC — core components

| Component | What it owns | Design source |
|---|---|---|
| `BiddingHub` | Participant-facing SignalR hub; delivers real-time bid feed, reserve-met badge, extended-bidding countdown, BIN removal, listing outcomes, settlement confirmation | `bounded-contexts.md` Relay section |
| `OperationsHub` | Staff-facing SignalR hub; delivers live lot board updates, session activity, settlement queue, obligation pipeline status, dispute alerts | `bounded-contexts.md` Relay section |
| Notification handlers | One Wolverine handler per consumed event type; each handler pushes to the relevant hub group (listing-specific group, participant-specific group, or broadcast) | Pattern per `docs/skills/wolverine-signalr.md` |
| Notification history projection | Marten document projection; one row per delivered notification per participant; queryable for the participant notification feed | `bounded-contexts.md` "Notification history projection" |

### Cross-BC wiring

#### Obligations integrations

| From | Event | To | Purpose |
|---|---|---|---|
| Settlement (M5) | `SettlementCompleted` | Obligations (M6) | Trigger `PostSaleCoordination` saga; carries `ListingId`, `WinnerId`, `SellerId` for reminder routing |
| Obligations (M6) | `TrackingInfoProvided` | Relay (M6) | Push tracking confirmation to winner's notification feed |
| Obligations (M6) | `ObligationFulfilled` | Relay (M6) | Push completion notification; consumed by Operations in M7 |
| Obligations (M6) | `DisputeOpened` | Relay (M6) | Alert Operations staff; consumed by Operations in M7 |
| Obligations (M6) | `DisputeResolved` | Relay (M6) | Notify relevant participants; consumed by Operations in M7 |

New RabbitMQ routes for Obligations:

- `obligations-settlement-events` — Obligations listens; consumes `SettlementCompleted` from Settlement's existing outbound path (Settlement also already publishes `SettlementCompleted` on `listings-settlement-events` — a second publish route is added in M6)
- `relay-obligations-events` — Obligations publishes `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`; Relay listens in the same milestone. Operations will add `ListenToRabbitQueue("operations-obligations-events")` in M7.

#### Relay integrations — events consumed per hub

Relay is a pure consumer; all routes below are **inbound-only** for Relay.

| Queue | Events | Hub target |
|---|---|---|
| `relay-participants-events` | `ParticipantSessionStarted`, `SellerRegistrationCompleted` | `OperationsHub` (session board) |
| `relay-selling-events` | `ListingPublished`, `ListingRevised`, `ListingEndedEarly` | `OperationsHub` (listing board) |
| `relay-auctions-events` | `BiddingOpened`, `BidPlaced`, `BidRejected`, `ReserveMet`, `ExtendedBiddingTriggered`, `ListingSold`, `ListingPassed`, `ListingWithdrawn`, `ProxyBidExhausted`, `BuyItNowPurchased`, `BuyItNowOptionRemoved`, `SessionCreated`, `SessionStarted`, `ListingAttachedToSession` | `BiddingHub` (participant); `OperationsHub` (staff) |
| `relay-settlement-events` | `SellerPayoutIssued` | `BiddingHub` (seller notification) |
| `relay-obligations-events` | `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` | `BiddingHub` (participant alert); `OperationsHub` (dispute / escalation alerts) |
| `relay-listings-events` | `LotWatchAdded`, `LotWatchRemoved` | `OperationsHub` (watch count) |

`relay-settlement-events` is **already pre-wired** with a `PublishMessage<SellerPayoutIssued>()` route from M5-S6. M6 adds the missing `ListenToRabbitQueue("relay-settlement-events")` call and (if needed) the `PaymentFailed` publish route.

New Rabbit routes added by M6 for Relay: `relay-participants-events`, `relay-selling-events`, `relay-auctions-events`, `relay-obligations-events`, `relay-listings-events` (plus the `ListenTo` for the pre-existing `relay-settlement-events`).

Publish-side additions for already-published events going to new Relay queues (e.g. `BidPlaced` → `relay-auctions-events`) are additions to existing message types' routing, not new types. `AutoProvision()` handles queue declaration.

#### Integration contracts authored in M6

All go in `src/CritterBids.Contracts/Obligations/`:

- `TrackingInfoProvided` — seller provided tracking; carries `ObligationId`, `ListingId`, `SellerId`, `TrackingNumber`, `ProvidedAt`
- `ObligationFulfilled` — both parties complete; carries `ObligationId`, `ListingId`, `WinnerId`, `SellerId`, `FulfilledAt`
- `DisputeOpened` — dispute raised; carries `ObligationId`, `ListingId`, `DisputeId`, `RaisedBy`, `Reason`, `OpenedAt`
- `DisputeResolved` — ops staff resolved; carries `ObligationId`, `ListingId`, `DisputeId`, `ResolutionType` (`Refund | Extension | Closed`), `ResolvedAt`

Relay publishes no integration events (pure consumer). No new `CritterBids.Contracts.Relay` namespace is needed.

### Open questions from PARKED-QUESTIONS.md resolved or carried in M6

| ID | Question | Disposition in M6 |
|---|---|---|
| W001-6 | Demo-mode timeout config for Obligations sagas | **Closed in M6-S1.** Decide: (A) `DemoMode` appsettings flag that overrides deadline durations to seconds; (B) configurable `ObligationsOptions` section with a `DemoTimeoutSeconds` key; (C) hardcoded short durations with a `#if DEBUG` guard. PO decision captures the cap constraint from W001 Phase 2. ADR if the decision is cross-cutting; retrospective note if it is BC-internal. |
| W001-13 | How does seller provide tracking? Dedicated screen or inline? | Carries to M8 (frontend). Obligations saga's backend seam accepts a `ProvideTracking` command regardless of UX surface. |
| W004-P2-8 | Publish notification via Relay or HTTP 200 sufficient? | Partially resolved: Relay is the answer for SignalR push. The HTTP 200 question is about whether the seller's submit action also returns a success in the API response — that is a frontend concern deferred to M8. Relay's handler fires regardless of the HTTP path. |
| W003-1 | What happens if `SellerPayoutIssued` fails (infrastructure issue)? | Relay BC's handler failure retries per Wolverine policy. Permanent failure surfaces in Wolverine dead-letter queue. Operations tooling for staff intervention deferred to M7. |
| W003-5 | Does Settlement need a manual retry mechanism for ops staff? | Post-MVP; Operations BC (M7). |

---

## 3. Explicit Non-Goals

- **Real carrier-tracking webhook integration.** The `TrackingInfoProvided` seam is stubbed in MVP. The Obligations saga treats `ProvideTracking` as a first-class command regardless of how tracking info arrives (seller-submitted form or carrier webhook). The real webhook receiver is post-MVP.
- **Dispute appeals and multi-round negotiation.** MVP dispute workflow is: open / ops-resolve / close. No appeals, no partial-resolution states.
- **Compensation or reversal logic on dispute resolution.** `DisputeResolved` carries a `ResolutionType` field (`Refund | Extension | Closed`) but settlement reversal is post-MVP.
- **Real-time bidder credit balance push during an auction.** Credit is checked at bid time (DCB, M3) and updated at settlement (M5). No mid-auction balance-change push via Relay.
- **Email, SMS, or push notification delivery.** Relay delivers to SignalR only in MVP. The email and SMS seams exist as code stubs (methods that log but do not call an external service). Replacing the stubs with real delivery is post-MVP.
- **Relay endpoints.** Relay is handler-only in MVP. No Wolverine HTTP endpoints are registered for Relay. Hub connections are managed via `@microsoft/signalr` in the React SPAs (M8).
- **Operations BC.** Relay's `OperationsHub` is wired and pushes to the correct group, but the Operations BC projections that would give the staff dashboard its full read-model (lot board, saga state panel, dispute queue) ship in M7.
- **Frontend SPAs.** Both React frontends are M8 scope. At M6 close the SignalR endpoints are functional and testable via integration tests and `dotnet-signalr-client` tooling, but no browser UI exists yet.
- **Auctions BC changes.** The M3/M4 Auctions implementation is unchanged. Any additional events Relay needs that Auctions does not currently publish are either already in Contracts or added as publish-route additions in `Program.cs` only (no Auctions BC code changes).

---

## 4. Solution Layout

### New projects added in M6

- `src/CritterBids.Obligations/` — Obligations BC implementation (saga, commands, internal events, module wiring)
- `src/CritterBids.Relay/` — Relay BC implementation (SignalR hubs, notification handlers, notification history projection, module wiring)
- `tests/CritterBids.Obligations.Tests/` — Obligations BC test project; xUnit + Shouldly + Testcontainers + Alba
- `tests/CritterBids.Relay.Tests/` — Relay BC test project; xUnit + Shouldly + Testcontainers + Alba

### New files added in M6 (representative, not exhaustive)

Obligations BC:

- `PostSaleCoordinationSaga.cs` — the Wolverine Saga (or equivalent per S1 decision); manages the full post-sale lifecycle
- `PostSaleCoordinationId.cs` — strongly-typed identifier (UUID v5 derived from `SettlementId` or UUID v7 per S1 decision)
- `PostSaleCoordinationStarted.cs`, `ShippingReminderSent.cs`, `DeadlineEscalated.cs`, `DeliveryConfirmed.cs` — Obligations-internal domain events
- `StartPostSaleCoordination.cs`, `SendShippingReminder.cs`, `SendDeadlineEscalation.cs`, `ProvideTracking.cs`, `OpenDispute.cs`, `ResolveDispute.cs` — saga command records
- `ObligationsModule.cs` — DI wiring; `AddObligationsModule()` extension
- `ObligationsIdentityNamespaces.cs` — UUID v5 namespace constant (if v5 path chosen in S1)
- `SettlementCompletedHandler.cs` — cross-BC consumer; starts the saga on `SettlementCompleted`
- `ObligationsOptions.cs` — options record for demo-mode timeout config (shape per S1 decision)

Relay BC:

- `BiddingHub.cs` — participant-facing SignalR hub; participant connections keyed by `BidderId`
- `OperationsHub.cs` — staff-facing SignalR hub; broadcast to all staff connections
- `RelayModule.cs` — DI wiring; `AddRelayModule()` extension; hub registration
- `NotificationHistoryView.cs` — Marten document projection (notification history per participant)
- Per-event handler files (one per consumed event type, grouped by source BC): e.g., `BidPlacedHandler.cs`, `ListingSoldHandler.cs`, `SettlementCompletedHandler.cs`, `ObligationFulfilledHandler.cs`, etc.

Contracts:

- `src/CritterBids.Contracts/Obligations/TrackingInfoProvided.cs`
- `src/CritterBids.Contracts/Obligations/ObligationFulfilled.cs`
- `src/CritterBids.Contracts/Obligations/DisputeOpened.cs`
- `src/CritterBids.Contracts/Obligations/DisputeResolved.cs`

API host wiring:

- `src/CritterBids.Api/Program.cs` — `builder.Services.AddObligationsModule()` and `builder.Services.AddRelayModule()`; all new RabbitMQ routes; `opts.Discovery.IncludeAssembly()` for both new BCs; `app.MapHub<BiddingHub>()` and `app.MapHub<OperationsHub>()` for SignalR

### Full solution layout at M6 close

```
src/
├── CritterBids.Api/
├── CritterBids.AppHost/
├── CritterBids.Contracts/
│   ├── Auctions/   (M3 / M4)
│   ├── Obligations/  ← NEW IN M6
│   ├── Participants/  (M5-S5 promotion)
│   ├── Selling/    (M2 / M4-S2)
│   └── Settlement/  (M5)
├── CritterBids.Auctions/    (M3 / M4)
├── CritterBids.Listings/    (M2 / M3-S6)
├── CritterBids.Obligations/  ← NEW IN M6
├── CritterBids.Participants/  (M1)
├── CritterBids.Relay/        ← NEW IN M6
├── CritterBids.Selling/     (M2 / M4-S2)
└── CritterBids.Settlement/  (M5)

tests/
├── CritterBids.Api.Tests/
├── CritterBids.Auctions.Tests/
├── CritterBids.Listings.Tests/
├── CritterBids.Obligations.Tests/  ← NEW IN M6
├── CritterBids.Participants.Tests/
├── CritterBids.Relay.Tests/        ← NEW IN M6
├── CritterBids.Selling.Tests/
└── CritterBids.Settlement.Tests/
```

---

## 5. Infrastructure

### Marten configuration

Both Obligations and Relay use Marten on PostgreSQL per ADR 011's All-Marten Pivot. Neither BC calls `AddMarten()` directly — they contribute their types via `services.ConfigureMarten()` inside `AddObligationsModule()` / `AddRelayModule()`.

**Obligations** registers:
- `PostSaleCoordinationSaga` as a Wolverine saga-store-managed document (or equivalent per S1 decision)
- Obligations-internal event types: `PostSaleCoordinationStarted`, `ShippingReminderSent`, `DeadlineEscalated`, `DeliveryConfirmed`

**Relay** registers:
- `NotificationHistoryView` as a Marten document projection (multi-stream, keyed by `BidderId`)

### RabbitMQ routing summary

New routes added in M6 (`Program.cs` additions):

| Queue | Direction | Events | Notes |
|---|---|---|---|
| `obligations-settlement-events` | In (Obligations listens) | `SettlementCompleted` | Settlement gains a third publish route for `SettlementCompleted` alongside `listings-settlement-events` |
| `relay-obligations-events` | In (Relay listens); Out (Obligations publishes) | `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` | Operations will add a ListenTo in M7 via `operations-obligations-events` |
| `relay-participants-events` | In (Relay listens) | `ParticipantSessionStarted`, `SellerRegistrationCompleted` | Participants gains new publish routes |
| `relay-selling-events` | In (Relay listens) | `ListingPublished`, `ListingRevised`, `ListingEndedEarly` | Selling gains new publish routes |
| `relay-auctions-events` | In (Relay listens) | All significant Auctions events | Auctions gains new publish routes (no Auctions BC code changes — only `Program.cs` routing) |
| `relay-settlement-events` | In (Relay listens) | `SellerPayoutIssued` | **Already pre-wired** publish-side from M5-S6; M6 adds the missing `ListenToRabbitQueue()` call |
| `relay-listings-events` | In (Relay listens) | `LotWatchAdded`, `LotWatchRemoved` | Listings gains new publish routes |

Existing queues (`listings-settlement-events`, `settlement-auctions-events`, `settlement-selling-events`, `listings-auctions-events`, etc.) stay unchanged.

`operations-settlement-events` (pre-wired publish-only from M5-S6) still has no consumer in M6. Operations BC (M7) adds the `ListenToRabbitQueue()`.

### Scheduled messages

The Obligations saga is CritterBids' **first BC to use `bus.ScheduleAsync()`** in production handlers (Auctions uses it for the auction close timer; Obligations uses it for the reminder chain). The pattern:

1. On saga start: `await bus.ScheduleAsync(new SendShippingReminder(saga.Id), reminderDelay)` — saves the scheduled envelope `Id` onto saga state
2. On `TrackingInfoProvided`: retrieve the saved envelope `Id`; call the Wolverine message store's cancellation API to remove the pending message
3. If not cancelled before `reminderDelay`: `SendShippingReminder` fires; saga records `ShippingReminderSent`; schedules the escalation message

Demo-mode timeout configuration (W001-6, closed in M6-S1) determines what values `reminderDelay` and `escalationDelay` take. The `opts.Policies.UseDurableLocalQueues()` policy already in `Program.cs` ensures scheduled messages survive a restart.

### No new stores

Both BCs use the same Marten-on-PostgreSQL store as all other BCs. No new database, schema, or container.

---

## 6. Conventions Pinned

### Obligations saga hosting (S1 decision)

The settlement workflow hosting decision (ADR-019: Wolverine Saga) is the established precedent. The same reasoning applies to Obligations: phased state (Started → Reminded → Escalated → Fulfilled/Disputed) fits the Wolverine Saga primitive's persisted-document model. Unless M6-S1 finds a compelling reason to diverge, the Obligations saga follows the AuctionClosingSaga and SettlementSaga pattern.

If the S1 decision diverges, it is recorded as `020-obligations-saga-hosting.md`. If it confirms the pattern without novel options, it is recorded in the M6-S1 retrospective rather than a full ADR.

### UUID strategy for ObligationId

Per ADR 007 and the established M5 precedent for Settlement: a UUID v5 namespace-derived `ObligationId` is natural here because a natural business key exists (`SettlementId` → one obligation per settlement). The namespace constant lives in `ObligationsIdentityNamespaces.cs`, analogous to `SettlementsIdentityNamespaces.cs`.

If the S1 decision prefers UUID v7 for simplicity (e.g., the per-settlement uniqueness is enforced via saga state rather than deterministic ID), that is valid — Settlement's `SettlementId` is deterministic but Obligations could choose UUID v7 for simpler saga initiation. S1 closes this.

### `[AllowAnonymous]` posture

Per `CLAUDE.md`: all endpoints through M6 carry `[AllowAnonymous]`. Relay has no HTTP endpoints in MVP. Obligations has no HTTP endpoints in MVP. If any Wolverine HTTP endpoints are added in M6 for commands (e.g. a `ProvideTracking` endpoint for the seller form), they carry `[AllowAnonymous]`. Real authentication deferred to M6 close / M7.

Wait — per the current `CLAUDE.md` note, `[AllowAnonymous]` applies "through M6." This means M6 is the last milestone under the `[AllowAnonymous]` posture. If real authentication is intended to begin at M7, that should be noted in M7's planning. This milestone does not introduce any auth — the posture is `[AllowAnonymous]` for any endpoints added here, and the auth milestone is post-M6.

### `OutgoingMessages` for Obligations integration events

Obligations' four integration events (`TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`) are published via `OutgoingMessages` return tuples — never `IMessageBus.PublishAsync()` in handler bodies. Per `wolverine-message-handlers.md` anti-pattern discipline and the consistent pattern from M1 through M5.

### Relay never publishes integration events

Relay handlers return `void` (or `Task`). No `OutgoingMessages` returns, no `IMessageBus` calls, no domain event publications. Relay is a pure consumer; its output is SignalR pushes, not integration events. This is enforced structurally — if a Relay handler method signature returns anything except `void` / `Task`, it is a bug.

### BC discovery isolation in test fixtures

Both Obligations and Relay consume events that other BCs also handle. The `*BcDiscoveryExclusion` pattern (from `critter-stack-testing-patterns.md`) applies wherever a Wolverine test fixture needs to isolate a single BC's handler for a shared event type (e.g. testing that Relay's `BidPlacedHandler` fires without also triggering Listings' projection handler). Obligations.Tests and Relay.Tests must apply `DisableAllExternalWolverineTransports()` in their test fixtures per M5's Key Learning on `tracked.Sent` vs `tracked.NoRoutes`.

---

## 7. Slice Breakdown

M6 ships in seven slices. The first two are foundation slices; the remaining five alternate between Obligations and Relay work with a final cross-BC integration slice.

| Slice | Title | Scope |
|---|---|---|
| M6-S1 | Foundation Decisions — Obligations Saga Shape + Demo-Mode Config + Relay Hub Design | Obligations saga hosting decision; W001-6 timeout config decision; `BiddingHub` and `OperationsHub` SignalR group naming conventions; contract stubs in `src/CritterBids.Contracts/Obligations/`; ADR authoring if warranted |
| M6-S2 | Obligations BC Scaffold + `SettlementCompleted` Consumer | `CritterBids.Obligations` project; `AddObligationsModule()`; Marten config; module wiring in `Program.cs`; `obligations-settlement-events` RabbitMQ route; `SettlementCompletedHandler` starts the saga |
| M6-S3 | Obligations Saga — Happy Path + Cancellable Reminder Chain | `PostSaleCoordinationSaga`; `bus.ScheduleAsync()` reminder chain; `TrackingInfoProvided` command cancels scheduled messages; `ObligationFulfilled` emitted; saga `MarkCompleted()`; all happy-path scenarios green |
| M6-S4 | Obligations Saga — Escalation + Dispute Sub-Workflow | Missed-deadline escalation path (`DeadlineEscalated`); dispute sub-workflow (`DisputeOpened`, `DisputeResolved`); `MarkCompleted()` on dispute resolution; all failure-path scenarios green |
| M6-S5 | Relay BC Scaffold + `BiddingHub` Core Routes | `CritterBids.Relay` project; `AddRelayModule()`; `BiddingHub` and `OperationsHub` setup; `relay-auctions-events` and `relay-settlement-events` consumers; `BidPlaced`, `ListingSold`, `SettlementCompleted` handlers pushing to participant groups |
| M6-S6 | Relay BC — Remaining Routes + Notification History Projection | `relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, `relay-listings-events` consumers; `OperationsHub` push handlers; `NotificationHistoryView` Marten projection |
| M6-S7 | End-to-End Integration + Housekeeping | Full journey test from `SettlementCompleted` through Obligations start and Relay push; `relay-settlement-events` `ListenTo` confirmation; all `Program.cs` route additions verified; test count baseline updated |
