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
8. [Resiliency Policies](#resiliency-policies)
9. [Modular Monolith Routing Settings](#modular-monolith-routing-settings)
10. [Adding a New Integration — Checklist](#adding-a-new-integration--checklist)
11. [Critical Warnings](#critical-warnings)
12. [Lessons Learned](#lessons-learned)

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

### Delayed Delivery with `bus.ScheduleAsync`

The one exception to "always use `OutgoingMessages`" is delayed delivery. For fire-now send a return value; for send-later reach for `bus.ScheduleAsync` or the `DelayedFor`/`ScheduledAt` cascading helpers.

**Two cascading shapes** — preferred when the scheduled message originates from a handler:

```csharp
public static IEnumerable<object> Handle(OpenBidding cmd, [WriteAggregate] Listing listing)
{
    yield return new BiddingOpened(listing.Id, cmd.ScheduledCloseAt);

    // Delay from now
    yield return new AutoClose(listing.Id).DelayedFor(cmd.ScheduledCloseAt - DateTimeOffset.UtcNow);

    // Absolute time
    yield return new ReminderEmail(listing.SellerId).ScheduledAt(cmd.ScheduledCloseAt.AddMinutes(-15));
}
```

**Imperative `bus.ScheduleAsync`** — when the scheduled message originates outside a handler pipeline (background service, startup hook):

```csharp
await bus.ScheduleAsync(new AutoClose(listingId), 5.Minutes());
await bus.ScheduleAsync(new AutoClose(listingId), DateTimeOffset.UtcNow.AddHours(2));
```

Scheduling priority: transport-native scheduling (RabbitMQ deferred-messages plugin) → database-backed (outbox-persisted) → in-memory. CritterBids' default configuration uses the database-backed path, which survives process crashes and the RabbitMQ broker restart.

### Message Expiration with `DeliverWithin`

For transient messages that lose value past a deadline — flash-session countdown ticks, "seller is typing" indicators, stale ops dashboard refreshes — declare a TTL so the framework discards stale messages instead of processing them minutes after they're relevant.

**Per send:**

```csharp
await bus.PublishAsync(
    new FlashSessionTick(sessionId, tickNumber),
    new DeliveryOptions { DeliverWithin = 2.Seconds() });
```

**Per endpoint (applies to every message published to that queue):**

```csharp
opts.PublishMessage<FlashSessionTick>()
    .ToRabbitQueue("operations-auctions-live-ticks")
    .DeliverWithin(2.Seconds());
```

**Per message type via attribute:**

```csharp
[DeliverWithin(2)]   // seconds
public sealed record FlashSessionTick(Guid SessionId, int TickNumber);
```

Expired messages are discarded by Wolverine. On RabbitMQ and Azure Service Bus, `DeliverWithin` maps to the broker's native TTL so messages never even leave the queue once they've gone stale. This is the right tool for anything where "deliver this in 30 seconds" is worse than "drop this."

**CritterBids guidance:** reserve `DeliverWithin` for transient operational signals, not domain events. `ListingSold` must never expire; `FlashSessionTick` may.

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
// Settlement BC handler — reacts to ListingSold from Auctions BC.
// No [MartenStore] attribute needed — CritterBids uses a single primary IDocumentStore (ADR 009).
// IDocumentSession is injected by Wolverine's SessionVariableSource from the primary store.
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

### Per-Queue Durability and Parallelism Overrides

`UseDurableOutboxOnAllSendingEndpoints()` covers outbound publishing. For inbound listeners and finer-grained control, override per queue:

```csharp
// Durable inbox for critical BC queues (Settlement, Obligations)
opts.ListenToRabbitQueue("settlement-auctions-events")
    .UseDurableInbox(new BufferingLimits(maximum: 1000, threshold: 500))
    .ListenerCount(3)                     // parallel consumer connections to this queue
    .MaximumParallelMessages(20);         // concurrent handler invocations in-process

// Buffered for throughput-sensitive, loss-tolerant traffic
opts.ListenToRabbitQueue("operations-auctions-live-ticks")
    .BufferedInMemory(new BufferingLimits(1000, 200));

// Global inbox durability policy (if every BC wants it)
opts.Policies.UseDurableInboxOnAllListeners();
```

**BufferingLimits are back-pressure.** When the in-memory buffer fills to `maximum`, Wolverine pauses the listener; it resumes when the buffer drops to `threshold`. This prevents a slow handler from accumulating an unbounded prefetch queue during traffic spikes.

**CritterBids durability posture by BC queue (recommended):**

| Queue pattern | Endpoint type | Rationale |
|---|---|---|
| `settlement-*-events`, `obligations-*-events` | `UseDurableInbox()` | Financial / obligation paths — at-least-once delivery required |
| `relay-*-events` | `BufferedInMemory()` | Notifications are retryable from source events; loss is tolerable |
| `operations-*-events` (dashboard state) | `BufferedInMemory()` | Ops dashboard is a view; stale is bad, lost is fine |
| `operations-*-live-ticks` (flash-session ticks) | `BufferedInMemory()` + `DeliverWithin` | Transient, time-sensitive, expiration-bounded |
| `listings-*-events`, `auctions-*-events` | `UseDurableInbox()` | Catalog state transitions drive downstream decisions |

Set these per-queue in the consuming BC's module (where `opts.ListenToRabbitQueue(...)` is declared). Do not globally apply `UseDurableInboxOnAllListeners()` — it over-persists the loss-tolerant queues and adds unnecessary database pressure.

### Production Hardening — Quorum Queues

For any production deployment past a single broker node, use quorum queues. They replicate across RabbitMQ cluster members, survive single-node failures, and are the current RabbitMQ recommendation over classic durable queues.

```csharp
// Opt every queue into quorum type by default (set once in Program.cs)
opts.UseRabbitMq(...)
    .UseQuorumQueues();

// Or per-queue
opts.ListenToRabbitQueue("settlement-auctions-events")
    .QueueType(QueueType.quorum);
```

CritterBids' Hetzner VPS deployment runs a single RabbitMQ node for now, so quorum queues are optional. When the deployment becomes multi-node (or moves to a managed provider that offers cluster semantics), flipping this on is a one-line change.

### Production Hardening — Enhanced Dead Lettering

By default, messages that exhaust retry policies land in RabbitMQ's DLQ as the raw envelope — no exception metadata attached. Enhanced dead lettering adds exception headers to the failed message so the DLQ content is diagnostic without needing to cross-reference application logs.

```csharp
opts.UseRabbitMq(...)
    .EnableEnhancedDeadLettering();
// Adds headers: exception-type, exception-message, exception-stack, failed-at
```

**Recommended for CritterBids production.** The operational win is significant: when CritterWatch or the RabbitMQ management UI is browsing DLQ contents during an incident, the exception reason is right there with the message. Otherwise the investigator has to match by message ID across tool boundaries.

### `AutoProvision()` in Production

`AutoProvision()` creates missing queues, exchanges, and bindings at host startup. That's the right behaviour in development and in CritterBids' current conference-demo deployment, but is risky in shared or compliance-constrained production environments: any topology typo in code silently creates a new queue rather than failing loudly against the expected infrastructure.

**Guarded pattern for hardened production:**

```csharp
var rabbit = opts.UseRabbitMqUsingNamedConnection("rabbit");

if (builder.Environment.IsDevelopment() ||
    builder.Configuration.GetValue<bool>("RabbitMq:AutoProvision"))
{
    rabbit.AutoProvision();
}
```

Under this pattern, production deployments run without `AutoProvision()` and rely on a CI/CD job (or RabbitMQ definitions.json import) to apply topology changes separately. The opt-in configuration flag gives operators an escape hatch to re-enable provisioning during a recovery.

CritterBids' current deployment posture — single Hetzner VPS, single app process, Aspire-provisioned broker — is well inside "safe to auto-provision" territory. Revisit when the deployment topology changes.

---

## Resiliency Policies

Retry strategies, circuit breakers, and dead-letter handling for integration-message processing. Most of CritterBids' handlers through M2.5 are in-process against Marten/PostgreSQL — transient failures mean broker/database blips, and the default Wolverine retry posture is adequate. The Settlement BC (payment gateway) and Relay BC (outbound notifications, SignalR fan-out) are the first BCs where explicit resiliency policies will earn their keep; this section exists so the patterns are in reach when those BCs land.

### Error handling actions, at a glance

When a handler throws, Wolverine consults the configured policies and applies one of these actions:

| Action | Behaviour |
|---|---|
| `RetryNow` / `RetryTimes(n)` | Immediate inline retry, no pause |
| `RetryWithCooldown(...)` | Retry after each cooldown in a specified sequence (exponential backoff) |
| `Requeue` | Put the message at the back of the receiving queue |
| `ScheduleRetry(duration)` | Persist and redeliver at a future time |
| `Discard` | Log and drop — no DLQ record |
| `MoveToErrorQueue` | Send directly to the dead letter queue |
| `PauseProcessing(duration)` | Stop the listener temporarily (use with `Requeue()` for broker-level outages) |

Without any explicit policy and with retries exhausted, messages move to the dead letter queue automatically.

### Global retry policy with exponential backoff

```csharp
// In Program.cs's UseWolverine() block
opts.Policies.OnException<NpgsqlException>()
    .RetryWithCooldown(
        50.Milliseconds(),
        200.Milliseconds(),
        1.Seconds(),
        5.Seconds());   // after this: DLQ

opts.Policies.OnException<TimeoutException>()
    .RetryWithCooldown(100.Milliseconds(), 500.Milliseconds(), 2.Seconds());

// Catch-all for other exceptions: retry a couple of times then DLQ
opts.Policies.OnException<Exception>().RetryTimes(3);
```

**Predicate filtering** works on exception properties, not just type:

```csharp
opts.Policies.OnException<NpgsqlException>(ex => ex.IsTransient)
    .RetryWithCooldown(100.Milliseconds(), 500.Milliseconds(), 2.Seconds());

opts.Policies.OnException<NpgsqlException>(ex => !ex.IsTransient)
    .MoveToErrorQueue();
```

### Per-handler and per-message-type policies

For handler-specific policies, prefer per-handler configuration so the policy lives next to the code it protects:

**Attribute-based (simplest):**

```csharp
public static class ProcessPaymentHandler
{
    [RetryNow(typeof(SqlException), 50, 100, 250)]        // exponential ms
    [ScheduleRetry(typeof(PaymentGatewayTransientException), 5)]  // retry in 5s
    [MoveToErrorQueueOn(typeof(InvalidCardException))]
    [MaximumAttempts(5)]
    public static void Handle(ProcessPayment cmd) { /* ... */ }
}
```

**`Configure(HandlerChain)` for richer policy composition:**

```csharp
public static class ProcessPaymentHandler
{
    public static void Configure(HandlerChain chain)
    {
        chain.OnException<PaymentGatewayException>()
            .RetryWithCooldown(500.Milliseconds(), 2.Seconds(), 10.Seconds());

        chain.OnException<PaymentGatewayDownException>()
            .Requeue()
            .AndPauseProcessing(2.Minutes());   // external outage — stop hammering it
    }

    public static (Events, OutgoingMessages) Handle(ProcessPayment cmd, [WriteAggregate] SettlementSaga saga) { /* ... */ }
}
```

**Rule precedence** (most specific first): per-message-type attributes → `Configure(HandlerChain)` static method → `IHandlerPolicy` implementations → global `opts.Policies.OnException<T>()`.

### Circuit breakers on external-facing queues

Circuit breakers pause an endpoint when the failure rate exceeds a threshold within a rolling window. They are **per-endpoint**, not global — a failing payment queue should never pause the listing-catalog queue.

**Use them on any listener whose handler interacts synchronously with an external system:** payment gateways, shipping carriers, email / SMS providers, third-party APIs.

```csharp
opts.ListenToRabbitQueue("settlement-auctions-events")
    .CircuitBreaker(cb =>
    {
        cb.MinimumThreshold = 10;                // minimum messages before evaluation
        cb.TrackingPeriod = 5.Minutes();         // rolling window
        cb.FailurePercentageThreshold = 20;      // trip at >20% failure rate
        cb.PauseTime = 2.Minutes();              // how long the circuit stays open

        // Only count external-system errors toward the threshold
        cb.Include<PaymentGatewayException>();
        cb.Include<HttpRequestException>();
        cb.Include<TimeoutException>();

        // Ignore expected exceptions that indicate bad data, not a downed service
        cb.Exclude<InvalidCardException>();
    });
```

**Circuit breaker + retry policy together** is the canonical pair: retries absorb transient blips, the circuit breaker catches sustained outages and pauses the listener until the downstream recovers. Without the breaker, retries alone will thrash through messages against a downed service and generate a storm of DLQ entries and wasted load.

### Dead-letter queue handling

Messages that exhaust retries go to the DLQ automatically. Force immediate DLQ when a message can never succeed:

```csharp
opts.Policies.OnException<ContractViolationException>().MoveToErrorQueue();
```

Prefer `Discard` over `MoveToErrorQueue` when the message is truly fire-and-forget and cluttering the DLQ would make diagnostic investigation noisier:

```csharp
opts.Policies.OnException<StaleNotificationException>().Discard();   // no DLQ record
```

**Inspect DLQ contents** through CritterWatch's DLQ management UI (when deployed) or Wolverine's dead-letter administration API. With enhanced dead lettering enabled (see RabbitMQ Transport Configuration), exception metadata rides with the message.

### Inspecting configured policies via CLI

The `describe-resiliency` CLI reads the assembled policy graph and prints the active rules for any endpoint or message type. Use it to audit policy coverage before a production deployment:

```bash
# All endpoints
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-resiliency --all

# One endpoint
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-resiliency settlement-auctions-events

# One message type
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-resiliency --message "CritterBids.Contracts.Auctions.ListingSold"
```

The output shows circuit-breaker settings, per-message retry schedules, and the default-fallback policies that apply when no more-specific rule matches. This is also the fastest way to confirm that `[MaximumAttempts]` on a message type is being honoured alongside global `opts.Policies` rules.

### Anti-patterns

- **Swallowing exceptions inside handlers.** Letting exceptions bubble is how policies get a chance to work. A `catch` block that logs and swallows hides the failure from Wolverine entirely, denying the framework the chance to retry or DLQ.
- **Infinite retries without a circuit breaker.** If a handler calls an external service and the policy retries indefinitely, a sustained outage becomes an unbounded retry storm. Pair indefinite or aggressive retries with a circuit breaker on the endpoint.
- **Global circuit breakers.** Circuit breakers are intentionally per-endpoint. Apply them to the queues whose handlers touch external systems, not as a global policy.
- **Mixing attribute and fluent policies haphazardly.** Pick one dominant style per BC. The rule-precedence order makes mixing work, but the cognitive load of tracing which rule wins during a real incident is not worth the flexibility.

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

    // 3. Wolverine envelope tables live in the "wolverine" schema rather than the default
    //    Marten schema — keeps framework tables separate from application tables.
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
6. **Implement the handler** in the consumer BC — no `[MartenStore]` attribute required (ADR 009: single primary store; `IDocumentSession` injected via `SessionVariableSource`)
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
