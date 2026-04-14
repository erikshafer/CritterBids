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
8. [Modular Monolith Routing Settings](#modular-monolith-routing-settings)
9. [Adding a New Integration — Checklist](#adding-a-new-integration--checklist)
10. [Critical Warnings](#critical-warnings)
11. [Lessons Learned](#lessons-learned)

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
    └── SellerRegistrationCompleted.cs
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
[MartenStore(typeof(ISettlementDocumentStore))]
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

> ⚠️ **Named store handlers must carry `[MartenStore(typeof(IBcDocumentStore))]`.** Every Wolverine handler in a Marten BC requires this attribute. Without it, Wolverine does not route injected sessions to the correct named store.

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
| `selling-participants-events` | Selling | Participants | seller registration |
| `listings-selling-events` | Listings | Selling | listing published |
| `settlement-auctions-events` | Settlement | Auctions | all significant |
| `obligations-settlement-events` | Obligations | Settlement | payment confirmed |
| `relay-auctions-events` | Relay | Auctions | bid activity |
| `relay-settlement-events` | Relay | Settlement | payout issued |
| `listings-auctions-events` | Listings | Auctions | status changes |
| `operations-auctions-events` | Operations | Auctions | all significant |

This pattern documents **who consumes** and **who publishes** directly in the queue name. Easy to trace message flow in the RabbitMQ management UI. No collision risk across BCs.

---

## RabbitMQ Transport Configuration

### Standard Pattern (Aspire — recommended)

When running under .NET Aspire, use the named connection pattern. Aspire provides a URI, not a traditional connection string, and `UseRabbitMqUsingNamedConnection` handles both forms:

```csharp
// Inside Program.cs UseWolverine() — not inside any BC module
opts.UseRabbitMqUsingNamedConnection("rabbit")
    .AutoProvision(); // Creates queues/exchanges automatically at startup
```

The connection name `"rabbit"` must match the resource name declared in `CritterBids.AppHost/Program.cs`.

> **Do not configure RabbitMQ inside BC modules.** The transport is a host-level concern. BC modules declare which queues to publish to and listen on (`opts.PublishMessage<T>()`, `opts.ListenToRabbitQueue()`), but the transport itself is wired once in `Program.cs`.

### Manual Factory Pattern (non-Aspire environments)

For environments where Aspire connection string injection is not available:

```csharp
opts.UseRabbitMq(factory =>
{
    factory.HostName = config["RabbitMQ:hostname"] ?? "localhost";
    factory.VirtualHost = config["RabbitMQ:virtualhost"] ?? "/";
    factory.Port = config.GetValue<int?>("RabbitMQ:port") ?? 5672;
    factory.UserName = config["RabbitMQ:username"] ?? "guest";
    factory.Password = config["RabbitMQ:password"] ?? "guest";
})
.AutoProvision();
```

### Durability

```csharp
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

`AutoProvision()` creates durable queues and persistent messages automatically. All messages survive RabbitMQ restarts.

---

## Modular Monolith Routing Settings

These three settings are required in `Program.cs`'s `UseWolverine()` block when multiple BCs share the same process. They are **not** per-BC settings — configure them once at the host level.

```csharp
builder.Host.UseWolverine(opts =>
{
    // 1. Each BC handler for the same message type gets its own dedicated queue.
    //    Without this: multiple BC handlers for ListingPublished are combined into one
    //    logical handler on a single queue — BC isolation is broken.
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;

    // 2. Prevents durable inbox deduplication bug on fanout.
    //    Without this: the inbox deduplicates by message ID alone. When the same
    //    ListingPublished message fans out to selling-participants-events AND
    //    listings-selling-events, only the first BC handler fires.
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // 3. All named Marten stores write envelope rows to a shared "wolverine" schema.
    //    Without this: each named store creates its own duplicate envelope tables.
    opts.Durability.MessageStorageSchemaName = "wolverine";

    opts.UseRabbitMqUsingNamedConnection("rabbit").AutoProvision();
});
```

### What `Separated` Mode Does

In default (`ClassicCombineIntoOneLogicalHandler`) mode, multiple handlers for the same message type share one queue:

```
Default:  ListingPublished → single queue → [ListingsHandler, SettlementHandler, AuctionsHandler]

Separated: ListingPublished → listings queue    → ListingsHandler (own transaction, own retry policy)
           ListingPublished → settlement queue  → SettlementHandler (own transaction, own retry policy)
           ListingPublished → auctions queue    → AuctionsHandler (own transaction, own retry policy)
```

When a message arrives from RabbitMQ, Wolverine auto-fans it out to all separated handler queues. No special routing is needed.

### What `IdAndDestination` Does

With the default `MessageIdentity.Id`, the durable inbox key is the message ID alone. When one `ListingPublished` message fans out to three handler queues, all three share the same message ID. The inbox treats them as duplicates and only processes the first.

`MessageIdentity.IdAndDestination` uses `(messageId, destinationQueue)` as the inbox key. Each BC handler has a distinct destination queue, so all three are processed correctly.

---

## Adding a New Integration — Checklist

1. **Define the contract** in `src/CritterBids.Contracts/<PublisherBc>/`
2. **Document all consumers** in XML doc comment — list every BC that will subscribe
3. **Build the consumer table** before finalizing payload (see L2 in Lessons Learned)
4. **Declare the publisher** via `opts.PublishMessage<T>().ToRabbitQueue("...")` in publisher's `AddXyzModule()`
5. **Declare the subscriber** via `opts.ListenToRabbitQueue("...")` in consumer's `AddXyzModule()`
6. **Implement the handler** in the consumer BC — include `[MartenStore(typeof(IBcDocumentStore))]` on all Marten BC handlers
7. **Verify queue name** matches exactly on both sides
8. **Write a cross-BC smoke test** to verify the RabbitMQ pipeline end-to-end
9. **When retiring a contract:** search codebase for every reference. Classify as active, dead-needs-migration, or dead-no-publisher. Never close a milestone with unresolved dead handlers.
10. **Update `docs/vision/bounded-contexts.md`** only when adding a **new BC-to-BC integration direction**. Not for adding a new message to an existing direction.

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

Handler returns `(Aggregate, Event)` tuple. Wolverine doesn't persist the event. Use `IStartStream`, `Events` collection, or `session.Events.Append()` explicitly.

---

### ⚠️ Warning 6: Building Messages from Entity State Instead of Command Values

```csharp
// ❌ WRONG — re-reads from entity, loses command value
outgoing.Add(new TrackingInfoProvided(obligation.Id, entity.CarrierName));

// ✅ CORRECT — uses command value directly
outgoing.Add(new TrackingInfoProvided(obligation.Id, cmd.Carrier, cmd.TrackingNumber));
```

---

### ⚠️ Warning 7: `bus.PublishAsync()` in HTTP Endpoints Bypasses the Outbox ⚠️ CRITICAL

```csharp
// ❌ WRONG — published even if DB commit fails
await bus.PublishAsync(new ListingSold(...));

// ✅ CORRECT — processed within the same transaction
var outgoing = new OutgoingMessages();
outgoing.Add(new CritterBids.Contracts.Auctions.ListingSold(...));
return (Results.Ok(), outgoing);
```

---

### ⚠️ Warning 8: Missing `MultipleHandlerBehavior.Separated` in Modular Monolith

Without `Separated`, multiple BC handlers for the same message type share one queue and one transaction. If one BC's handler fails, it blocks all other BCs from processing the same message. This silently breaks BC isolation.

**Fix:** Set `opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated` in `Program.cs`'s `UseWolverine()` block. Also set `opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination` to prevent fanout deduplication bugs.

---

## Lessons Learned

**L1: Verify queue wiring end-to-end, not just via in-memory tracking.**
Write cross-BC smoke tests with a real RabbitMQ container that verify publish → consume pipeline end-to-end.

---

**L2: Design contracts for all consumers before publishing the first message.**
Build a consumer table before finalizing the contract:

| Event | Consumer A needs | Consumer B needs | Consumer C needs |
|---|---|---|---|
| `ListingSold` | `WinnerId`, `HammerPrice` | `SellerId`, `HammerPrice` | `ListingId` for UI |

---

**L3: Sagas must handle all terminal states from every BC they consume.**
A saga that handles the happy path but not `PaymentFailed` will accumulate dangling documents.

---

**L4: Integration event payloads must be rich enough for all consumers.**
Include all context needed to act on the event without querying back.

---

**L5: Required, non-nullable fields only on contracts.**
Optional-with-default fields invite sloppy construction. If a field is always populated, mark it required.

---

**L6: Event tuple returns don't persist events without `[WriteAggregate]`.**
Manual aggregate loading + tuple return silently discards events. Use `IStartStream`, `Events`, or `session.Events.Append()`.

---

**L7: Publisher configuration must match subscriber expectations exactly.**
Queue names must be identical on both sides. One character off and messages are silently undeliverable.

---

**L8: Build outgoing messages from command values, not entity state.**
When a handler modifies entity state and then reads it back, transient command values can be silently overwritten.

---

**L9: Fan-out pattern — parent returns `OutgoingMessages` with N child commands.**

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
N async messages + projection updates take longer than a single handler.

---

**L11: Assert the full payload of outgoing integration messages, not just the type.**
A test that only checks the type was published will miss data loss bugs. Assert every field that matters to downstream consumers.

---

**L12: Integration handler → SignalR pattern requires `async Task<T>` with explicit `SaveChangesAsync()`.**
When a handler appends events to trigger inline projections and then immediately queries the projection for a push notification, the handler must be `async Task<T>` and call `await session.SaveChangesAsync()` before querying the projection.

---

**L13: `OutgoingMessages` is the only safe publishing mechanism from HTTP endpoints.**
Use `(IResult, OutgoingMessages)` or `(IResult, Events, OutgoingMessages)` return tuples.

---

**L14: Idempotent endpoints must not publish duplicate integration events.**

```csharp
var existing = await session.LoadAsync<Listing>(cmd.ListingId, ct);
if (existing is not null)
    return (Results.Ok(existing), new OutgoingMessages()); // No event on no-op
```

---

**L15: Document enrichment tradeoffs when embedding data from other BCs.**
Enrichment is a shortcut with a maintenance cost. Record the tradeoff explicitly.

---

**L16: Dual-publish for safe contract retirement across multiple consumers.**
Temporarily publish both old and new contract types simultaneously. Migrate consumers one at a time. Retire the dual-publish only after all consumers are migrated and verified.

---

## References

- [Wolverine Transport Fundamentals](https://wolverine.netlify.app/guide/messaging/transports/)
- [RabbitMQ Transport](https://wolverine.netlify.app/guide/messaging/transports/rabbitmq/)
- [Transactional Inbox/Outbox](https://wolverine.netlify.app/guide/durability/)
- [Modular Monolith Tutorial](https://wolverine.netlify.app/tutorials/modular-monolith.html)
- `docs/skills/wolverine-message-handlers.md` — handler patterns, return types
- `docs/skills/wolverine-sagas.md` — orchestration sagas
- `docs/skills/marten-event-sourcing.md` — domain events, aggregates
- `docs/skills/adding-bc-module.md` — BC module registration, host-level Wolverine settings
- `docs/vision/bounded-contexts.md` — CritterBids integration topology
