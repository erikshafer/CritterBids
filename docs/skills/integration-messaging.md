# Integration Messaging

Patterns and conventions for asynchronous message-based communication between bounded contexts in CritterBids.

---

## Table of Contents

1. [Domain Events vs Integration Messages](#domain-events-vs-integration-messages)
2. [Message Contracts](#message-contracts)
3. [Publishing Integration Messages](#publishing-integration-messages)
4. [Subscribing to Queues](#subscribing-to-queues)
5. [Integration Message Handlers](#integration-message-handlers)
6. [Queue Naming Conventions](#queue-naming-conventions)
7. [RabbitMQ Transport Configuration](#rabbitmq-transport-configuration)
8. [Adding a New Integration — Checklist](#adding-a-new-integration--checklist)
9. [Critical Warnings](#critical-warnings)
10. [Lessons Learned](#lessons-learned)

---

## Domain Events vs Integration Messages

**This distinction is foundational.**

| Aspect | Domain Events | Integration Messages |
|---|---|---|
| Scope | Inside a single BC | Cross BC boundaries |
| Namespace | BC-internal | `CritterBids.Contracts.<PublisherBcName>` |
| Persistence | Marten event streams | RabbitMQ durable queues + transactional outbox |
| Consumers | Handlers within the same BC | Handlers in downstream BCs |
| Purpose | Reconstruct aggregate state | Choreography and orchestration across BCs |
| Example | `BiddingClosed` (Auctions BC internal) | `ListingSold` (Auctions → Settlement, Obligations, Relay) |

An event can be **both** a domain event and the basis for an integration message. `ListingSold` is a domain event in the Auctions BC's event stream *and* the integration message published to downstream BCs. The handler in Auctions BC converts one to the other via `OutgoingMessages`.

---

## Message Contracts

All integration messages crossing BC boundaries are defined in `src/CritterBids.Contracts/`.

### Structure

```
src/CritterBids.Contracts/
├── CritterBids.Contracts.csproj   # No BC dependencies — pure contracts
├── Auctions/
│   ├── BidPlaced.cs
│   ├── ListingSold.cs
│   ├── ListingPassed.cs
│   └── ExtendedBiddingTriggered.cs
├── Selling/
│   ├── ListingPublished.cs
│   └── ListingEndedEarly.cs
├── Settlement/
│   ├── SettlementCompleted.cs
│   └── PaymentFailed.cs
├── Obligations/
│   ├── ObligationFulfilled.cs
│   └── DisputeOpened.cs
└── Participants/
    └── ParticipantSessionStarted.cs
```

**Namespace pattern:** `CritterBids.Contracts.<PublisherBcName>`

The namespace reflects the **publisher**, not the consumer. `ListingSold` is under `Auctions/` because Auctions BC publishes it, even though Settlement, Obligations, Relay, and Operations all consume it.

### Contract Structure

```csharp
namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Published by Auctions BC when a listing closes with a winning bidder above reserve.
///
/// Consumed by:
/// - Settlement BC: Initiate winner charge and seller payout
/// - Obligations BC: Begin post-sale coordination saga
/// - Relay BC: Notify winner and seller
/// - Operations BC: Update live dashboard
/// </summary>
public sealed record ListingSold(
    Guid ListingId,
    Guid SellerId,
    Guid WinnerId,
    decimal HammerPrice,
    DateTimeOffset SoldAt);
```

**Key characteristics:**
- `sealed record` — immutable, value equality
- XML doc comment documents **publisher** and **all known consumers**
- `DateTimeOffset` timestamps with `*At` suffix
- `IReadOnlyList<T>` for collections — never `List<T>` or arrays
- Rich payload — include all data consumers need to avoid follow-up queries

### Contract Design Rules

**Required, non-nullable fields only.** Optional-with-default invites "I'll fill it in later" patterns:

```csharp
// ❌ WRONG
public sealed record TrackingInfoProvided(Guid ObligationId, string? TrackingNumber = null);

// ✅ CORRECT
public sealed record TrackingInfoProvided(Guid ObligationId, string Carrier, string TrackingNumber);
```

**Rich payloads.** Document all consumers before finalizing contracts. A minimal contract will require expansion as consumers are added.

**Document all consumers** in XML comments. When adding a new consumer to an existing contract, update the comment.

---

## Publishing Integration Messages

### Publisher Configuration

Declare in the BC's `AddXyzModule()` Wolverine configuration:

```csharp
// Inside AddAuctionsModule():
opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
    .ToRabbitQueue("settlement-auctions-events");

opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
    .ToRabbitQueue("obligations-auctions-events");

opts.PublishMessage<CritterBids.Contracts.Auctions.ListingSold>()
    .ToRabbitQueue("relay-auctions-events");

opts.PublishMessage<CritterBids.Contracts.Auctions.BidPlaced>()
    .ToRabbitQueue("relay-auctions-events");
```

Each `PublishMessage<T>()` call creates a separate outbox entry. Wolverine guarantees at-least-once delivery to each queue independently. Only the owning BC publishes a given message type.

### Publishing from Handlers

**Always use `OutgoingMessages` as a return value — never `bus.PublishAsync()` in HTTP endpoints.** See Warning 7.

```csharp
public static (Events, OutgoingMessages) Handle(
    CloseAuction cmd,
    [WriteAggregate] Listing listing)
{
    var closed = new BiddingClosed(listing.Id, listing.CurrentHighBid, listing.HighBidderId);

    var outgoing = new OutgoingMessages();
    outgoing.Add(new CritterBids.Contracts.Auctions.ListingSold(
        listing.Id,
        listing.SellerId,
        listing.HighBidderId!.Value,
        listing.CurrentHighBid,
        DateTimeOffset.UtcNow));

    return (new Events(closed), outgoing);
}
```

`OutgoingMessages` is processed within the same Wolverine middleware pipeline that commits the Marten session — if the session commit fails, messages are not published. The transactional outbox guarantees at-least-once delivery.

**Exception:** `bus.ScheduleAsync()` remains valid — delayed delivery cannot be expressed via `OutgoingMessages`.

---

## Subscribing to Queues

Declare in the BC's `AddXyzModule()` Wolverine configuration:

```csharp
// Inside AddSettlementModule():
opts.ListenToRabbitQueue("settlement-auctions-events")
    .ProcessInline();
```

`.ProcessInline()` processes messages synchronously as they arrive — the default and recommended approach for most integrations. Wolverine automatically routes messages to the correct handler by message type.

---

## Integration Message Handlers

Integration handlers follow the same patterns as command handlers. See `wolverine-message-handlers.md`.

```csharp
// Settlement BC handler — reacts to ListingSold from Auctions BC
public static class ListingSoldHandler
{
    public static (SettlementSaga, OutgoingMessages) Handle(
        CritterBids.Contracts.Auctions.ListingSold message)
    {
        var sagaId = Guid.CreateVersion7();
        var saga = new SettlementSaga
        {
            Id = sagaId,
            ListingId = message.ListingId,
            WinnerId = message.WinnerId,
            SellerId = message.SellerId,
            HammerPrice = message.HammerPrice,
            Status = SettlementStatus.Initiated
        };

        return (saga, new OutgoingMessages());
    }
}
```

### Choreography vs Orchestration

**Choreography** — BC autonomously reacts to events without coordination:

```csharp
// Relay BC reacts to BidPlaced — no coordination with Auctions BC required
public static class BidPlacedHandler
{
    public static async Task Handle(
        CritterBids.Contracts.Auctions.BidPlaced message,
        IRelayService relay)
    {
        await relay.PushOutbidNotification(message.PreviousHighBidderId, message.ListingId, message.Amount);
    }
}
```

**Orchestration** — saga actively sends commands to coordinate BCs:

```csharp
public sealed class ObligationsSaga : Saga
{
    public OutgoingMessages Handle(CritterBids.Contracts.Settlement.SettlementCompleted message)
    {
        Status = ObligationStatus.AwaitingShipment;
        return new OutgoingMessages
        {
            new NotifySellerToShip(message.ListingId, message.SellerId)
        };
    }
}
```

See `wolverine-sagas.md` for comprehensive saga patterns.

### Idempotency

At-least-once delivery means handlers may receive the same message multiple times. Guard accordingly:

```csharp
// Stream-based idempotency — Marten's optimistic concurrency handles it
public static IStartStream Handle(ListingPublished message)
{
    // If stream already exists, Marten throws ConcurrencyException
    // Wolverine retries via configured retry policy
    return MartenOps.StartStream<CatalogListing>(message.ListingId, new CatalogListingCreated(...));
}
```

---

## Queue Naming Conventions

**Pattern:** `<consumer-bc>-<publisher-bc>-<category>`

| Queue | Consumer | Publisher | Category |
|---|---|---|---|
| `settlement-auctions-events` | Settlement | Auctions | all significant |
| `obligations-auctions-events` | Obligations | Settlement | payment confirmed |
| `relay-auctions-events` | Relay | Auctions | bid activity |
| `relay-settlement-events` | Relay | Settlement | payout issued |
| `listings-auctions-events` | Listings | Auctions | status changes |
| `operations-auctions-events` | Operations | Auctions | all significant |

This pattern documents **who consumes** and **who publishes** directly in the queue name. Easy to trace message flow in the RabbitMQ management UI. No collision risk across BCs.

---

## RabbitMQ Transport Configuration

Standard pattern in every BC's module registration:

```csharp
opts.UseRabbitMq(rabbit =>
{
    rabbit.HostName = config["RabbitMQ:hostname"] ?? "localhost";
    rabbit.VirtualHost = config["RabbitMQ:virtualhost"] ?? "/";
    rabbit.Port = config.GetValue<int?>("RabbitMQ:port") ?? 5672;
    rabbit.UserName = config["RabbitMQ:username"] ?? "guest";
    rabbit.Password = config["RabbitMQ:password"] ?? "guest";
})
.AutoProvision(); // Creates queues/exchanges automatically

opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

| Environment | Host | Notes |
|---|---|---|
| Local native | `localhost` | APIs on host, RabbitMQ in Docker |
| Local containerized | `rabbitmq` | Docker service name |

`AutoProvision()` creates durable queues and persistent messages automatically. All messages survive RabbitMQ restarts.

---

## Adding a New Integration — Checklist

1. **Define the contract** in `src/CritterBids.Contracts/<PublisherBc>/`
2. **Document all consumers** in XML doc comment — list every BC that will subscribe
3. **Declare the publisher** via `opts.PublishMessage<T>().ToRabbitQueue("...")` in publisher's `AddXyzModule()`
4. **Declare the subscriber** via `opts.ListenToRabbitQueue("...")` in consumer's `AddXyzModule()`
5. **Implement the handler** in the consumer BC
6. **Verify queue name** matches exactly on both sides
7. **Write a cross-BC smoke test** to verify the RabbitMQ pipeline end-to-end
8. **When retiring a contract:** search codebase for every reference. Classify as active, dead-needs-migration, or dead-no-publisher. Never close a milestone with unresolved dead handlers.
9. **Update `docs/vision/bounded-contexts.md`** only when adding a **new BC-to-BC integration direction**. Not for adding a new message to an existing direction.

---

## Critical Warnings

### ⚠️ Warning 1: Missing `PublishMessage<T>()` Causes Silent Message Loss

If you publish a message from a handler but forget to declare `opts.PublishMessage<T>()`, Wolverine **silently drops the message**. No exception, no log entry.

**Symptoms:** Downstream BC never receives the message. RabbitMQ shows zero messages in queue. Tests pass because they use in-memory tracking.

**Fix:** Always declare `PublishMessage<T>()` for every integration message. Verify with cross-BC smoke tests using real RabbitMQ.

---

### ⚠️ Warning 2: Queue Name Mismatch

Publisher sends to `settlement-auctions-events`, subscriber listens on `settlement-auction-events` (typo). Messages accumulate unread in the publisher's queue.

**Fix:** Verify names match exactly on both sides. Follow the `<consumer>-<publisher>-<category>` convention consistently.

---

### ⚠️ Warning 3: Contract Changes Without Coordinating Consumers

Expanding a required field and deploying the publisher before consumers are updated causes deserialization failures in consumers.

**Fix:** Add optional fields first, deploy consumers to handle them, then make fields required. Deploy consumers before publishers. Document all consumers before changing contracts.

---

### ⚠️ Warning 4: Saga Missing Terminal State Handlers

Saga handles `ObligationFulfilled` but not `DisputeResolved` or `PaymentFailed`. Saga is left in dangling state forever.

**Fix:** When adding integration events, verify every consuming saga handles every terminal state. Every terminal path calls `MarkCompleted()`.

---

### ⚠️ Warning 5: Returning Tuples from Integration Handlers Without `[WriteAggregate]`

Handler returns `(Aggregate, Event)` tuple. Wolverine doesn't persist the event. Use `IStartStream`, `Events` collection, or `session.Events.Append()` explicitly. See `marten-event-sourcing.md` Anti-Pattern #8.

---

### ⚠️ Warning 6: Building Messages from Entity State Instead of Command Values

Handler re-reads entity state to populate an outgoing message instead of passing the command value directly. The transient value the user submitted is silently lost.

```csharp
// ❌ WRONG — re-reads from entity, loses command value
outgoing.Add(new TrackingInfoProvided(obligation.Id, entity.CarrierName));

// ✅ CORRECT — uses command value directly
outgoing.Add(new TrackingInfoProvided(obligation.Id, cmd.Carrier, cmd.TrackingNumber));
```

---

### ⚠️ Warning 7: `bus.PublishAsync()` in HTTP Endpoints Bypasses the Outbox ⚠️ CRITICAL

`IMessageBus.PublishAsync()` in HTTP endpoints publishes immediately, outside Wolverine's transactional outbox. If the Marten session commit fails, the message has already been sent.

```csharp
// ❌ WRONG — published even if DB commit fails
await bus.PublishAsync(new ListingSold(...));

// ✅ CORRECT — processed within the same transaction
var outgoing = new OutgoingMessages();
outgoing.Add(new CritterBids.Contracts.Auctions.ListingSold(...));
return (Results.Ok(), outgoing);
```

`bus.ScheduleAsync()` remains valid for delayed delivery — it cannot be expressed via `OutgoingMessages`.

---

## Lessons Learned

These are distilled from real integration bugs. Each one represents a class of failure worth actively guarding against.

---

**L1: Verify queue wiring end-to-end, not just via in-memory tracking.**
Unit tests that use `fixture.Tracker.Sent` pass even when the RabbitMQ `PublishMessage<T>()` declaration is missing. Write cross-BC smoke tests with a real RabbitMQ container that verify publish → consume pipeline end-to-end.

---

**L2: Design contracts for all consumers before publishing the first message.**
A contract designed only for the immediate consumer requires expansion as new consumers are added. Expanding a contract is a coordinated deployment. Avoid it by building a consumer table before finalizing the contract:

| Event | Consumer A needs | Consumer B needs | Consumer C needs |
|---|---|---|---|
| `ListingSold` | `WinnerId`, `HammerPrice` | `SellerId`, `HammerPrice` | `ListingId` for UI |

---

**L3: Sagas must handle all terminal states from every BC they consume.**
A saga that handles the happy path but not `PaymentFailed` or `DisputeResolved` will accumulate dangling documents. For every integration relationship a saga has, enumerate every terminal event that relationship produces.

---

**L4: Integration event payloads must be rich enough for all consumers.**
Clients receiving a push notification with only an ID will make follow-up HTTP calls. Include all context needed to act on the event without querying back.

---

**L5: Required, non-nullable fields only on contracts.**
Optional-with-default fields on message records invite sloppy construction. If a field is always populated, mark it required. Enforcement at construction time catches missing data immediately.

---

**L6: Event tuple returns don't persist events without `[WriteAggregate]`.**
`(Aggregate, Event)` tuple returns only work when Wolverine controls the write path via `[WriteAggregate]`. Manual aggregate loading + tuple return silently discards events. Use `IStartStream`, `Events`, or `session.Events.Append()`. This has caused ~30 minutes of debugging more than once.

---

**L7: Publisher configuration must match subscriber expectations exactly.**
Queue names must be identical on both sides. The publisher declares `.ToRabbitQueue("name")`. The subscriber declares `.ListenToRabbitQueue("name")`. One character off and messages are silently undeliverable.

---

**L8: Build outgoing messages from command values, not entity state.**
When a handler modifies entity state and then reads it back to build an outgoing message, transient values from the command can be silently overwritten by stale entity state. Pass command values explicitly as parameters.

---

**L9: Fan-out pattern — parent returns `OutgoingMessages` with N child commands.**
One parent command can dispatch N child commands via `OutgoingMessages`. Wolverine processes each as a separate handler invocation in its own transaction. Optimistic concurrency on the parent stream prevents duplicate fan-outs.

```csharp
public static OutgoingMessages Handle(StartFlashSession cmd)
{
    var outgoing = new OutgoingMessages();
    foreach (var listingId in cmd.ListingIds)
        outgoing.Add(new OpenBidding(listingId, cmd.ScheduledCloseAt));
    return outgoing;
}
```

---

**L10: Fan-out tests need generous timing.**
N async messages + projection updates take longer than a single handler. If testing fan-out workflows that generate large numbers of child commands, allow adequate time for all side effects to complete before asserting.

---

**L11: Assert the full payload of outgoing integration messages, not just the type.**
A test that verifies `fixture.Tracker.Sent.Single<ListingSold>()` was published without checking `WinnerId`, `HammerPrice`, or `SellerId` will miss data loss bugs. Assert every field that matters to downstream consumers.

---

**L12: Integration handler → SignalR pattern requires `async Task<T>` with explicit `SaveChangesAsync()`.**
When a handler appends events to trigger inline projections and then immediately returns a real-time push message based on the updated projection, the handler must be `async Task<T>` and call `await session.SaveChangesAsync()` before querying the projection. Synchronous handlers cannot force the projection to update before the query.

---

**L13: `OutgoingMessages` is the only safe publishing mechanism from HTTP endpoints.**
`bus.PublishAsync()` bypasses the outbox in HTTP endpoints. Use `(IResult, OutgoingMessages)` or `(IResult, Events, OutgoingMessages)` return tuples. The outgoing messages are committed atomically with the Marten session. This is the most pervasive Critter Stack convention violation to watch for in code review.

---

**L14: Idempotent endpoints must not publish duplicate integration events.**
An idempotent HTTP endpoint that returns the existing resource on duplicate calls must also return an empty `OutgoingMessages`. If the state change didn't happen, no integration event should fire.

```csharp
var existing = await session.LoadAsync<Listing>(cmd.ListingId, ct);
if (existing is not null)
    return (Results.Ok(existing), new OutgoingMessages()); // No event on no-op
```

---

**L15: Document enrichment tradeoffs when embedding data from other BCs.**
When an integration message carries data that "belongs" to another BC (e.g., a seller's display name embedded in `ListingSold`), document the coupling explicitly. Enrichment is a shortcut with a maintenance cost. Record the tradeoff and the plan for remediation.

---

**L16: Dual-publish for safe contract retirement across multiple consumers.**
When retiring a contract name with multiple consumers, don't do a hard cutover. Temporarily publish both old and new contract types simultaneously. Migrate consumers one at a time. Retire the dual-publish only after all consumers are migrated and verified. Strict sequence: add new handlers → verify → remove old handlers → remove dual-publish.

```csharp
// MIGRATION: Dual-publish for backward compat — remove after Relay BC migrates
outgoing.Add(new LegacyContracts.BiddingClosed(listing.Id, ...));
outgoing.Add(new CritterBids.Contracts.Auctions.ListingSold(listing.Id, ...));
```

After removing dual-publish: search the codebase for every reference to the retired contract name. Classify each as active, dead-needs-migration, or dead-no-publisher. Dead handlers must be cleaned up before the milestone closes.

---

## References

- [Wolverine Transport Fundamentals](https://wolverine.netlify.app/guide/messaging/transports/)
- [RabbitMQ Transport](https://wolverine.netlify.app/guide/messaging/transports/rabbitmq/)
- [Transactional Inbox/Outbox](https://wolverine.netlify.app/guide/durability/)
- `docs/skills/wolverine-message-handlers.md` — handler patterns, return types
- `docs/skills/wolverine-sagas.md` — orchestration sagas
- `docs/skills/marten-event-sourcing.md` — domain events, aggregates
- `docs/vision/bounded-contexts.md` — CritterBids integration topology
