# Wolverine Sagas

Patterns and conventions for building stateful orchestration sagas with Wolverine + Marten.

---

## Table of Contents

1. [When to Use a Saga](#when-to-use-a-saga)
2. [The Wolverine Saga API](#the-wolverine-saga-api)
3. [Starting a Saga](#starting-a-saga)
4. [Handler Discovery](#handler-discovery)
5. [Marten Document Configuration](#marten-document-configuration)
6. [Business Logic — The Decider Pattern](#business-logic--the-decider-pattern)
7. [Scheduled Messages and Timeouts](#scheduled-messages-and-timeouts)
8. [Idempotency](#idempotency)
9. [Saga Lifecycle Completion](#saga-lifecycle-completion)
10. [DOs and DO NOTs](#dos-and-do-nots)
11. [Testing Sagas](#testing-sagas)
12. [Quick Reference Checklist](#quick-reference-checklist)

---

## When to Use a Saga

A Wolverine saga is the right tool when business logic must **coordinate multiple steps over time**, maintaining mutable state that drives orchestration decisions. Unlike event-sourced aggregates (which append immutable events), a saga is a **living document** that mutates as the process progresses.

| Scenario | Best Pattern | Why |
|---|---|---|
| Coordinate 2+ steps or BCs over time | **Saga** | Mutable state, correlation, compensation |
| Record what happened to a single aggregate | **Event-sourced aggregate** | Immutable history, time-travel |
| Simple fire-and-forget message routing | **Message handler** | No state needed |
| Stateless message transformation | **Message handler** | Functional, no persistence |
| Complex conditional flow with rollback | **Saga** | Compensation chains, state guards |
| Long-running workflow with timeouts | **Saga** | Scheduled messages, durable state |
| Single BC, multi-stream immediate decision | **DCB** | See `dynamic-consistency-boundary.md` |

**Rule of thumb:** If you're asking "what state is this workflow in right now?" — reach for a saga.

### Event-Sourced Aggregate vs. Document-Based Saga

| Dimension | Event-Sourced Aggregate | Document-Based Saga |
|---|---|---|
| Persistence | Append-only event stream | Mutable JSON document |
| History | Full audit trail | Current state only |
| State access | Rebuild from events | Direct property read |
| Concurrency | Optimistic via stream version | Optimistic via numeric revision |
| Best for | Domain objects with history | Orchestration processes |

Use a document-based saga for orchestration. The saga's job is coordination — the individual BCs own their event streams with full history. The saga just needs to know "where are we now."

**CritterBids examples:** `AuctionClosingSaga` (closes listings, declares winners), `ProxyBidManager` (auto-bids per bidder per listing), `SettlementSaga` (charge winner, pay seller), `ObligationsSaga` (post-sale shipping coordination).

---

## The Wolverine Saga API

### The `Saga` Base Class

```csharp
public sealed class AuctionClosingSaga : Saga
{
    // REQUIRED: Wolverine uses this as the correlation key.
    // Must be named "Id" — Wolverine/Marten convention.
    public Guid Id { get; set; }

    public Guid ListingId { get; set; }
    public Guid? WinnerId { get; set; }
    public decimal HammerPrice { get; set; }
    public bool ExtendedBiddingEnabled { get; set; }
    public DateTimeOffset ScheduledCloseAt { get; set; }
    public AuctionClosingStatus Status { get; set; }

    public OutgoingMessages Handle(BidPlaced message) { ... }
    public OutgoingMessages Handle(CloseAuction message) { ... }
}
```

### Message Correlation

Wolverine correlates incoming messages to a saga instance by convention: it looks for a property named `{SagaTypeName}Id`. For `AuctionClosingSaga`, it looks for `AuctionClosingSagaId`.

```csharp
// Wolverine finds AuctionClosingSaga whose Id == message.AuctionClosingSagaId
public sealed record BidPlaced(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    Guid AuctionClosingSagaId);  // <-- correlation key
```

No attribute, no configuration — just the naming convention. If the property name doesn't match, the saga won't be found and Wolverine throws at startup.

```csharp
// ✅ CORRECT
public sealed record CloseAuction(Guid AuctionClosingSagaId, DateTimeOffset ScheduledAt);

// ❌ WRONG — "Id" alone won't correlate
public sealed record CloseAuction(Guid Id, DateTimeOffset ScheduledAt);

// ❌ WRONG — wrong name
public sealed record CloseAuction(Guid SagaId, DateTimeOffset ScheduledAt);
```

### `MarkCompleted()`

Call `MarkCompleted()` when the saga reaches a terminal state. Wolverine deletes the saga document from Marten after the current handler completes. **Every terminal state must call `MarkCompleted()`** — orphaned saga documents accumulate indefinitely otherwise.

```csharp
public void Handle(ListingWithdrawn message)
{
    Status = AuctionClosingStatus.Withdrawn;
    MarkCompleted();
}
```

---

## Starting a Saga

The cleanest approach is a **separate static handler class** returning a tuple with the saga type. Wolverine recognizes this as a saga start:

```csharp
// Separate handler class — NOT on the saga itself
public static class StartAuctionClosingSagaHandler
{
    // Return type (AuctionClosingSaga, ...) signals Wolverine to persist the saga
    public static (AuctionClosingSaga, CloseAuction) Handle(BiddingOpened message)
    {
        var sagaId = Guid.CreateVersion7();
        var saga = new AuctionClosingSaga
        {
            Id = sagaId,
            ListingId = message.ListingId,
            ExtendedBiddingEnabled = message.ExtendedBiddingEnabled,
            ScheduledCloseAt = message.ScheduledCloseAt,
            Status = AuctionClosingStatus.Open
        };

        // Schedule the close message
        var closeCmd = new CloseAuction(sagaId, message.ScheduledCloseAt);

        return (saga, closeCmd);
    }
}
```

The returned `CloseAuction` message is cascaded by Wolverine. To schedule it for future delivery, use `bus.ScheduleAsync()` instead of returning it as an immediate cascade — see [Scheduled Messages and Timeouts](#scheduled-messages-and-timeouts).

---

## Handler Discovery

**⚠️ CRITICAL:** Saga handlers must be discovered via `IncludeAssembly()`, not `IncludeType<T>()`.

```csharp
// In BC's AddXyzModule() method:
opts.UseWolverine(config =>
{
    config.Discovery.IncludeAssembly(typeof(AuctionClosingSaga).Assembly); // ✅ Correct
    // config.Discovery.IncludeType<AuctionClosingSaga>(); // ❌ Wrong — won't find all handlers
});
```

Also add `[assembly: WolverineModule]` to the BC's `AssemblyAttributes.cs`:

```csharp
// AssemblyAttributes.cs
using Wolverine.Attributes;
[assembly: WolverineModule]
```

---

## Marten Document Configuration

Configure the saga document with numeric revisions for optimistic concurrency:

```csharp
// In BC's Marten configuration:
opts.Schema.For<AuctionClosingSaga>()
    .Identity(x => x.Id)
    .UseNumericRevisions(true);
```

### `ConcurrencyException` Retry Policy

Concurrent saga message processing is expected. Configure retry with cooldown:

```csharp
opts.OnException<ConcurrencyException>()
    .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
```

---

## Business Logic — The Decider Pattern

Keep business logic in a static `Decider` class with pure functions. The saga's `Handle()` method delegates to it. This keeps the saga document as thin state-holder and makes business logic trivially unit-testable.

```csharp
// Pure decision record — carries state changes + outgoing messages
public sealed record AuctionClosingDecision
{
    public AuctionClosingStatus? Status { get; init; }
    public Guid? WinnerId { get; init; }
    public decimal? HammerPrice { get; init; }
    public DateTimeOffset? NewCloseAt { get; init; }
    public IReadOnlyList<object> Messages { get; init; } = [];
}

// Pure static decider — no infrastructure dependencies
public static class AuctionClosingDecider
{
    public static AuctionClosingDecision HandleBidPlaced(
        AuctionClosingSaga current,
        BidPlaced bid,
        DateTimeOffset now)
    {
        if (!current.ExtendedBiddingEnabled)
            return new AuctionClosingDecision(); // No-op

        var windowStart = current.ScheduledCloseAt.AddMinutes(-current.ExtensionWindowMinutes);
        if (now < windowStart)
            return new AuctionClosingDecision(); // Not in extension window

        var newCloseAt = now.AddMinutes(current.ExtensionMinutes);
        return new AuctionClosingDecision
        {
            NewCloseAt = newCloseAt,
            Messages = [new ExtendedBiddingTriggered(current.ListingId, current.ScheduledCloseAt, newCloseAt)]
        };
    }
}

// Saga delegates to decider, applies result
public sealed class AuctionClosingSaga : Saga
{
    public OutgoingMessages Handle(BidPlaced bid)
    {
        var decision = AuctionClosingDecider.HandleBidPlaced(this, bid, DateTimeOffset.UtcNow);
        Apply(decision);
        var outgoing = new OutgoingMessages();
        foreach (var msg in decision.Messages) outgoing.Add(msg);
        return outgoing;
    }

    private void Apply(AuctionClosingDecision decision)
    {
        if (decision.Status.HasValue) Status = decision.Status.Value;
        if (decision.WinnerId.HasValue) WinnerId = decision.WinnerId;
        if (decision.HammerPrice.HasValue) HammerPrice = decision.HammerPrice.Value;
        if (decision.NewCloseAt.HasValue) ScheduledCloseAt = decision.NewCloseAt.Value;
    }
}
```

### State Minimality — Re-Read Emission-Only Fields

Saga state is **orchestration state**, not a snapshot of the domain. If an outcome event needs a field that the saga doesn't drive decisions on, re-read it from the source of truth at emission time rather than expanding saga state to carry it through the lifetime of the process.

The decision boundary: **store a field if it gates a `Handle` branch; load it on demand if it only appears in a cascaded event payload.** Every field added to saga state becomes a concurrent-update hazard under numeric revisions, widens the start handler's capture surface, and forces a saga schema migration when the outcome contract changes.

```csharp
public async Task<OutgoingMessages> Handle(
    [SagaIdentityFrom(nameof(CloseAuction.ListingId))] CloseAuction message,
    IDocumentSession session,
    TimeProvider time,
    CancellationToken cancellationToken)
{
    if (Status == AuctionClosingStatus.Resolved) return new OutgoingMessages();

    var messages = new OutgoingMessages { new BiddingClosed(ListingId, time.GetUtcNow()) };

    if (BidCount > 0 && ReserveHasBeenMet)
    {
        // ListingSold carries SellerId, but the saga never decides on it —
        // re-read the live aggregate rather than widening saga state or
        // threading SellerId through the Start handler.
        var listing = await session.Events.AggregateStreamAsync<Listing>(
            ListingId, token: cancellationToken);
        messages.Add(new ListingSold(
            ListingId, listing!.SellerId, CurrentHighBidderId!.Value,
            CurrentHighBid, BidCount, time.GetUtcNow()));
    }
    /* ... */

    Status = AuctionClosingStatus.Resolved;
    MarkCompleted();
    return messages;
}
```

The read cost is paid once per terminal — cheap against a live aggregate (which the DCB already hits on every decision), negligible against an inline projection.

**In-repo ground:** CritterBids `AuctionClosingSaga.Handle(CloseAuction)` (authored M3-S5b) — `SellerId` lives on the `Listing` aggregate (populated via `Apply(BiddingOpened)`) and is re-read at close time instead of being captured by `StartAuctionClosingSagaHandler`. The frozen-start-handler invariant was preserved at zero cost. See `src/CritterBids.Auctions/AuctionClosingSaga.cs` and retrospective `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` §"S5b-1" for the full narrative.

---

## Scheduled Messages and Timeouts

Use `bus.ScheduleAsync()` for delayed delivery. This is the only justified `IMessageBus` use in handlers.

```csharp
// Schedule the auction close from the saga start handler
public static async Task<AuctionClosingSaga> Handle(
    BiddingOpened message,
    IMessageBus bus)
{
    var sagaId = Guid.CreateVersion7();
    var saga = new AuctionClosingSaga { Id = sagaId, ... };

    // Schedule close at the listing's configured close time
    await bus.ScheduleAsync(
        new CloseAuction(sagaId),
        message.ScheduledCloseAt);

    return saga;
}
```

### Canceling and Rescheduling (Anti-Snipe Extension)

When extended bidding fires, cancel the existing scheduled close and reschedule:

```csharp
public async Task Handle(BidPlaced bid, IMessageBus bus)
{
    var decision = AuctionClosingDecider.HandleBidPlaced(this, bid, DateTimeOffset.UtcNow);
    Apply(decision);

    if (decision.NewCloseAt.HasValue)
    {
        // Cancel the existing scheduled close
        await bus.CancelScheduledAsync(ScheduledCloseMessageId);

        // Reschedule at the extended time
        var token = await bus.ScheduleAsync(
            new CloseAuction(Id),
            decision.NewCloseAt.Value);

        ScheduledCloseMessageId = token.Id;
    }
}
```

### Obligation Timeout Chain

The Obligations saga drives its timeout chain with a sequence of scheduled messages — each cancelable if the obligation is fulfilled before the deadline fires:

```csharp
public async Task Handle(PostSaleStarted message, IMessageBus bus)
{
    ShipByDeadline = DateTimeOffset.UtcNow.AddDays(2);

    // Schedule reminder at day 1
    var reminder = await bus.ScheduleAsync(
        new ShippingReminderDue(Id),
        DateTimeOffset.UtcNow.AddDays(1));
    ReminderMessageId = reminder.Id;

    // Schedule escalation at day 3
    var escalation = await bus.ScheduleAsync(
        new ShippingDeadlineMissed(Id),
        DateTimeOffset.UtcNow.AddDays(3));
    EscalationMessageId = escalation.Id;
}

public async Task Handle(TrackingInfoProvided message, IMessageBus bus)
{
    // Cancel both scheduled messages — obligation met early
    await bus.CancelScheduledAsync(ReminderMessageId);
    await bus.CancelScheduledAsync(EscalationMessageId);

    TrackingNumber = message.TrackingNumber;
    Status = ObligationStatus.Shipped;
}
```

---

## Idempotency

Sagas receive at-least-once delivery. Every handler must guard against duplicate messages.

```csharp
public sealed class AuctionClosingSaga : Saga
{
    // Track processed bid IDs to guard against duplicates
    public HashSet<Guid> ProcessedBidIds { get; set; } = new();

    public OutgoingMessages Handle(BidPlaced bid)
    {
        // Idempotency guard — ignore duplicate delivery
        if (ProcessedBidIds.Contains(bid.BidId))
            return new OutgoingMessages();

        ProcessedBidIds.Add(bid.BidId);
        // ... process bid
    }
}
```

**Also guard terminal state:** Check for terminal states at the top of handlers that issue compensation messages. A message arriving after the saga has closed should be silently ignored.

```csharp
public OutgoingMessages Handle(BidPlaced bid)
{
    // Terminal state guard — saga may have already closed
    if (Status is AuctionClosingStatus.Closed or AuctionClosingStatus.Withdrawn)
        return new OutgoingMessages();

    if (ProcessedBidIds.Contains(bid.BidId))
        return new OutgoingMessages();

    // ... process
}
```

---

## Saga Lifecycle Completion

**Every terminal path must call `MarkCompleted()`.**

Common CritterBids terminal paths:

```csharp
// AuctionClosingSaga
public OutgoingMessages Handle(CloseAuction message)
{
    if (Status is AuctionClosingStatus.Withdrawn) return new OutgoingMessages();

    Status = HighBidderId.HasValue && ReserveMet
        ? AuctionClosingStatus.Sold
        : AuctionClosingStatus.Passed;

    MarkCompleted(); // ← Terminal
    // ... return outgoing messages
}

public void Handle(ListingWithdrawn message)
{
    Status = AuctionClosingStatus.Withdrawn;
    MarkCompleted(); // ← Terminal
}

// ProxyBidManager
public OutgoingMessages Handle(BidPlaced competing)
{
    if (competing.Amount >= MaxAmount)
    {
        Status = ProxyBidStatus.Exhausted;
        MarkCompleted(); // ← Terminal — proxy max exceeded
        return new OutgoingMessages { new ProxyBidExhausted(ListingId, BidderId, MaxAmount) };
    }
    // ... auto-bid up to max
}

public void Handle(BiddingClosed message)
{
    MarkCompleted(); // ← Terminal — listing closed
}

// ObligationsSaga
public void Handle(ObligationFulfilled message)
{
    Status = ObligationStatus.Complete;
    MarkCompleted(); // ← Terminal — happy path
}

public void Handle(DisputeResolved message)
{
    Status = ObligationStatus.Resolved;
    MarkCompleted(); // ← Terminal — dispute path
}
```

**⚠️ Premature closure is worse than late closure.** If a saga closes before a message it should have handled arrives, that message finds no saga — compensation, refunds, or notifications silently fail. Always check whether active sub-workflows (e.g., open disputes) are complete before calling `MarkCompleted()`.

```csharp
public void Handle(ObligationsWindowExpired message)
{
    WindowExpired = true;
    if (HasActiveDispute) return; // Stay open — dispute still running
    Status = ObligationStatus.Complete;
    MarkCompleted();
}

public void Handle(DisputeResolved message)
{
    HasActiveDispute = false;
    if (!WindowExpired) return; // Stay open — window not yet expired
    Status = ObligationStatus.Resolved;
    MarkCompleted();
}
```

### Handling Post-`MarkCompleted()` Deliveries — the `NotFound` Named-Method Convention

`MarkCompleted()` deletes the saga document. Any message that arrives afterward correlated to the same saga id — a retry, a late cascade, an at-least-once redelivery — finds no saga. By default Wolverine throws. If the message is one you want to **silently absorb** (the saga's job is already done), add a static method literally named `NotFound` on the saga class that accepts that message type.

```csharp
public sealed class AuctionClosingSaga : Saga
{
    // Normal handler path — runs when the saga document still exists
    public OutgoingMessages Handle(BiddingClosed message) { /* ... */ }

    // Escape hatch — runs when the saga was already completed & deleted
    public static void NotFound(BiddingClosed message)
    {
        // Intentional no-op: terminal arrived after saga closed out normally.
    }
}
```

The method must be `static` (the saga instance by definition does not exist at this point), and its parameter list is resolved exactly like a normal Wolverine handler — `IDocumentSession`, `ILogger<T>`, etc. can be injected. Use it to log, surface a metric, or quietly drop — but do **not** use it as a back-door for "resurrect the saga": if the flow isn't really terminal, the saga shouldn't have called `MarkCompleted()` in the first place.

**Citation:** Wolverine source `src/Wolverine/Persistence/Sagas/SagaChain.cs:24` (`public const string NotFound = "NotFound";`), `:235` (`NotFoundCalls = findByNames(NotFound);` — the codegen field), `:354-366` (the dispatch branch that invokes `NotFoundCalls` when the saga load returns null).

**In-repo ground:** `AuctionClosingSaga.NotFound(CloseAuction)` and `AuctionClosingSaga.NotFound(ListingWithdrawn)` — both authored M3-S5b to absorb late `CloseAuction` timer fires that race the withdrawn/completed terminal. See retrospective `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` §"Surprise 1" for the failure mode that prompted the discovery.

---

## DOs and DO NOTs

**DO:**
- Inherit from `Saga` with `public Guid Id { get; set; }`
- Name correlation properties `{SagaName}Id` on integration messages
- Start sagas via separate handler returning `(SagaType, ...)`
- Use `IncludeAssembly()` not `IncludeType<T>()`
- Configure `.UseNumericRevisions(true)` in Marten
- Add `ConcurrencyException` retry policy
- Keep business logic in static Decider classes
- Guard every handler against duplicate message delivery
- Guard terminal-state handlers against closed sagas
- Call `MarkCompleted()` on every terminal path
- Use `IReadOnlyList<T>` with immutable updates for collection properties
- Store derived counts from authoritative collections, not as separate counters

**DO NOT:**
- Put business logic in Wolverine message handlers on the saga class
- Use mutable `List<T>` on saga properties
- Store counts that can be derived from authoritative collections
- Close a saga while sub-workflows (disputes, active returns) are still open
- Trust that handlers fire in order — always guard against out-of-order delivery
- Use `bus.PublishAsync()` in saga handlers — use `OutgoingMessages` instead

---

## Testing Sagas

### Unit Tests (no infrastructure)

Test Decider pure functions directly:

```csharp
[Fact]
public void HandleBidPlaced_WhenInExtensionWindow_ExtendsCloseTime()
{
    var saga = new AuctionClosingSaga
    {
        Id = Guid.NewGuid(),
        ListingId = Guid.NewGuid(),
        ExtendedBiddingEnabled = true,
        ExtensionWindowMinutes = 2,
        ExtensionMinutes = 2,
        ScheduledCloseAt = DateTimeOffset.UtcNow.AddSeconds(90) // Within 2-minute window
    };

    var bid = new BidPlaced(saga.ListingId, Guid.NewGuid(), Guid.NewGuid(), 50m, saga.Id);
    var decision = AuctionClosingDecider.HandleBidPlaced(saga, bid, DateTimeOffset.UtcNow);

    decision.NewCloseAt.ShouldNotBeNull();
    decision.Messages.ShouldContain(m => m is ExtendedBiddingTriggered);
}
```

### Integration Tests (Alba + Wolverine + Marten)

Test the full saga lifecycle via message dispatch:

```csharp
[Fact]
public async Task AuctionClosingSaga_WhenBidInExtensionWindow_ReschedulesClose()
{
    // 1. Start the saga
    await _fixture.ExecuteAndWaitAsync(new BiddingOpened(listingId, ...));

    // 2. Place a bid in the extension window
    var saga = await _fixture.LoadSagaByListingId(listingId);
    await _fixture.ExecuteAndWaitAsync(new BidPlaced(listingId, ..., saga.Id));

    // 3. Assert saga rescheduled the close
    var updated = await _fixture.LoadSaga<AuctionClosingSaga>(saga.Id);
    updated.ScheduledCloseAt.ShouldBeGreaterThan(saga.ScheduledCloseAt);
}
```

Use `ExecuteAndWaitAsync` from the TestFixture to dispatch messages and wait for all Wolverine side effects to complete before asserting.

### Resolving `IMessageBus` in a Test Harness — It Is Scoped

Wolverine registers `IMessageBus` as a **scoped** service: `services.AddScoped<IMessageBus, MessageContext>()`. Attempting `host.Services.GetRequiredService<IMessageBus>()` against the root container throws `InvalidOperationException: Cannot resolve scoped service ... from root provider`. In production this is invisible because every message invocation already runs inside a per-message scope. In tests you must create the scope yourself.

```csharp
// ❌ WRONG — throws: IMessageBus is scoped, root container cannot satisfy it
var bus = _fixture.Host.Services.GetRequiredService<IMessageBus>();
await bus.ScheduleAsync(new CloseAuction(sagaId), DateTimeOffset.UtcNow.AddSeconds(30));

// ✅ CORRECT — create a scope; dispose releases bus + any scoped deps
await using var scope = _fixture.Host.Services.CreateAsyncScope();
var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
await bus.ScheduleAsync(new CloseAuction(sagaId), DateTimeOffset.UtcNow.AddSeconds(30));
```

**Citation:** Wolverine source `src/Wolverine/HostBuilderExtensions.cs:190` (`services.AddScoped<IMessageBus, MessageContext>();`).

**In-repo ground:** The `ExecuteAndWaitAsync` family on `AuctionsTestFixture` wraps the scope-and-dispose dance so saga tests don't have to spell it out per call. Reach for the raw `IMessageBus` (scoped) only when you need a capability the fixture helper does not expose — e.g. `ScheduleAsync` with a specific trigger time for a `CloseAuction` cancellation test. See retrospective `docs/retrospectives/M3-S5-auction-closing-saga-skeleton-retrospective.md` §"Surprise 4" for the discovery.

---

## Quick Reference Checklist

**Infrastructure:**
- [ ] Inherits from `Saga` with `public Guid Id { get; set; }`
- [ ] Integration messages have `{SagaName}Id` correlation property
- [ ] Saga started via separate handler returning `(SagaType, ...)`
- [ ] `IncludeAssembly(typeof(MySaga).Assembly)` in module registration
- [ ] `[assembly: WolverineModule]` in domain assembly
- [ ] Marten configured with `.Identity(x => x.Id).UseNumericRevisions(true)`
- [ ] `ConcurrencyException` retry policy configured

**Business Logic:**
- [ ] Business logic in static Decider class (pure functions)
- [ ] Decision record carries nullable state changes + messages
- [ ] Derived counts computed from authoritative collections

**Idempotency & Safety:**
- [ ] Idempotency guard on handlers (HashSet of processed IDs)
- [ ] Terminal-state guard at top of handlers that issue compensation
- [ ] Active sub-workflow check before `MarkCompleted()` where applicable

**Lifecycle Completion:**
- [ ] Every terminal path calls `MarkCompleted()`
- [ ] No premature closure while sub-workflows are open

---

## References

- [Wolverine Saga Documentation](https://wolverinefx.net/guide/durability/marten/sagas.html)
- [Process Manager Pattern — EIP](https://www.enterpriseintegrationpatterns.com/patterns/messaging/ProcessManager.html)
- [Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)
- `docs/skills/dynamic-consistency-boundary.md` — for single-BC, single-decision patterns
- `docs/skills/wolverine-message-handlers.md` — for handler patterns and return types
