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
7. [Multi-Phase Sagas with Self-Sent Continuation Commands](#multi-phase-sagas-with-self-sent-continuation-commands)
8. [Scheduled Messages and Timeouts](#scheduled-messages-and-timeouts)
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

## Multi-Phase Sagas with Self-Sent Continuation Commands

The Auction Closing saga is a **two-phase shape**: it starts on `BiddingOpened`, accumulates bid state through the auction's lifetime, and closes on `CloseAuction` (a single scheduled-message handler). One open phase, one close phase. The Settlement saga (M5-S4 / W003 Phase 1 Part 2 Approach A) is structurally distinct: a **seven-phase progression** through `Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed`, where each phase is its own `Handle` method invoked by a self-sent continuation command. The mechanics differ in three concrete ways worth pinning here, since both shapes are likely to recur in CritterBids and future BCs.

### When to reach for a multi-phase saga

A multi-phase saga is the right tool when:

| Property | Two-phase (Auction Closing) | Multi-phase (Settlement) |
|---|---|---|
| Trigger sources | One open + many bidders + one close timer | One inbound integration event |
| Decisions per phase | Multiple per phase (every bid, every reserve check) | One per phase (charge → calculate → payout → complete) |
| Time scale | Hours / days | Seconds (in-process queue) |
| Failure paths | Closure outcomes (Sold / Passed / Withdrawn) | One linear path with one early exit (`Failed`) |
| Continuation source | External events (BIDS, scheduled close) | Self-sent commands within the same saga |
| Shape | Accumulator | Pipeline |

If the workflow is a *pipeline* — each step depends on the previous step's terminal state, and there's no incoming external traffic between steps — multi-phase with self-sends is correct. If the workflow is an *accumulator* — multiple external messages arrive over time and the saga reacts to each — two-phase with start + close is correct.

### Self-sent continuation commands

Each phase emits a self-send command in its `OutgoingMessages` return. Wolverine routes the command back to the saga via the saga's own `Handle` method. The command is a plain `sealed record (Guid SettlementId)` — Wolverine's `[SagaIdentityFrom(nameof(X.SettlementId))]` decoration on the receiving Handle parameter overrides the default `{SagaName}Id` correlation convention.

```csharp
public OutgoingMessages Handle(
    [SagaIdentityFrom(nameof(ChargeWinner.SettlementId))] ChargeWinner message,
    IDocumentSession session)
{
    if (Status != SettlementStatus.ReserveChecked)
    {
        throw new InvalidSettlementTransitionException(Id, Status, nameof(ChargeWinner));
    }

    Status = SettlementStatus.WinnerCharged;
    session.Events.Append(Id, new WinnerCharged(Id, WinnerId, HammerPrice, DateTimeOffset.UtcNow));

    return new OutgoingMessages { new CalculateFee(Id) };
}
```

### State guards on every phase entry

Every Handle method begins with a `if (Status != ExpectedPhase) throw InvalidSettlementTransitionException(...)` guard. The guard is the multi-phase saga's idempotency contract: re-delivery of a continuation command after the saga has advanced past the corresponding phase throws (Wolverine inbox dedup should prevent this in practice; the guard is the correctness backstop). The seven invalid-transition scenarios in W003 §1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 each correspond to exactly one of these guards firing.

The guards are also where the `SettlementStatus` enum earns its keep — the enum is the cross-Handle contract that lets each phase declare its precondition explicitly. Without the enum, the precondition would be implicit in nullable-field combinations (`FeeAmount is not null && SellerPayout is not null` → "we must be at FeeCalculated"), which is harder to read and easier to drift.

### The financial event stream — `session.Events.Append` alongside `OutgoingMessages`

The Auction Closing saga emits its outcome events (BiddingClosed / ListingSold / ListingPassed) only on the bus via `OutgoingMessages` — there is no Auctions-side event stream that stores them; downstream BCs consume them via RabbitMQ. The Settlement saga is different: per W003 §"Financial Event Stream", every event in the settlement's lifecycle is appended to a dedicated audit stream keyed by `SettlementId`. This adds one line per phase:

```csharp
session.Events.Append(Id, new ReserveCheckCompleted(...));
return new OutgoingMessages { new ChargeWinner(Id) };
```

The first event in the stream uses `session.Events.StartStream<FinancialEventStream>(sagaId, settlementInitiated)` because `opts.Events.UseMandatoryStreamTypeDeclaration = true` requires every new stream to declare its type. `FinancialEventStream` is a marker class (mirrors `BidRejectionAudit`'s shape — see `marten-event-sourcing.md`) whose sole purpose is satisfying the stream-type-declaration rule.

Integration events that cross BC boundaries are dual-role: they're both appended to the financial event stream (audit) AND emitted via `OutgoingMessages` (bus delivery to local + cross-BC consumers). The Settlement saga emits `SellerPayoutIssued` and `SettlementCompleted` this way; the local M5-S3 `PendingSettlementHandler` fires on the OutgoingMessages dispatch (under `MultipleHandlerBehavior.Separated`) and the cross-BC publish route (S6) routes the same emission to RabbitMQ.

### Retry-on-not-found at the Start handler

The Settlement saga's Start handler reads `PendingSettlement` (a projection seeded by `ListingPublished`, possibly days earlier in a different BC). Per W003 Phase 1 Part 1 Option A, if the projection has not caught up at start time, the handler throws `PendingSettlementNotFoundException` and a Wolverine retry policy (`OnException<...>().RetryWithCooldown(100ms, 250ms, 500ms)`) re-queues the inbound message. The triggering event stays in the queue until the projection catches up. This pattern generalizes: a multi-phase saga that depends on a projection at start time should declare a custom retryable exception and a corresponding `IWolverineExtension` retry policy, both BC-scoped.

### `MarkCompleted()` at the terminal phase

The terminal phase's Handle calls `MarkCompleted()` after appending the terminal event and emitting the integration event. Wolverine removes the saga document at the next persistence boundary; the audit stream persists per W003 §"Financial Event Stream" — the saga's mutable orchestration state goes away, but the immutable history of what happened stays.

### In-repo ground

| Aspect | Two-phase (M3-S5) | Multi-phase (M5-S4) |
|---|---|---|
| Saga document | `src/CritterBids.Auctions/AuctionClosingSaga.cs` | `src/CritterBids.Settlement/SettlementSaga.cs` |
| Start handler | `StartAuctionClosingSagaHandler.cs` | `StartSettlementSagaHandler.cs` |
| Continuation pattern | Scheduled `CloseAuction` via `bus.ScheduleAsync` | Self-sent `CheckReserve` / `ChargeWinner` / `CalculateFee` / `IssueSellerPayout` / `CompleteSettlement` via `OutgoingMessages` |
| Audit stream | None (events flow to other BCs only) | `FinancialEventStream` keyed by `SettlementId` |
| Retry policy | `AuctionsConcurrencyRetryPolicies` (`ConcurrencyException` / `DcbConcurrencyException`) | `SettlementsConcurrencyRetryPolicies` (`PendingSettlementNotFoundException`) |
| Integration tests | `AuctionClosingSagaTests` — multi-message scenarios | `SettlementSagaTests.Full_BiddingSource_HappyPath_ProducesSixEventStream` — single inbound message, six-event stream assertion |

The two shapes coexist in the same project because the underlying business processes have different shapes. Don't force a single saga primitive on both — match the saga shape to the workflow's natural rhythm.

---

## Composite-Key Correlation — the Dispatcher Pattern

Wolverine's `[SagaIdentityFrom]` resolves the saga id by **reading a single named property** off the inbound message. Verified against the codegen frame at `Wolverine.Persistence.Sagas.PullSagaIdFromMessageFrame`:

```csharp
// PullSagaIdFromMessageFrame.GenerateCode — pseudocode
writer.Write($"Guid sagaId = {message}.{sagaIdMember.Name};");
writer.Write($"if (sagaId == default && !Guid.TryParse(envelope.SagaId, out sagaId)) sagaId = {message}.{sagaIdMember.Name};");
```

There is **no expression resolver, no method-based identity, no delegate hook.** If your saga's id is a derived composite that no inbound contract carries, you cannot use `[SagaIdentityFrom]` directly against the inbound contract.

### When this matters

`AuctionClosingSaga.Id = ListingId` and `SettlementSaga.Id = UuidV5(ns, $"settlement:{ListingId}")` both work the standard way because the inbound contract carries the lookup key — `ListingId` for AuctionClosingSaga, `SettlementId` (computed before dispatch by the start handler) for SettlementSaga.

The Proxy Bid Manager saga (M4-S3) is structurally different. Its id is `UuidV5(ns, $"{ListingId}:{BidderId}")` — a derived composite that no inbound `BidPlaced` carries. Worse, a single `BidPlaced` may target N proxy sagas (one per registered bidder on the listing); no single field on `BidPlaced` could address them all.

### The dispatcher pattern (Path C)

Introduce an Auctions-internal command and a small non-saga handler that bridges the gap:

```csharp
// 1. Internal command carrying the resolved SagaId + the original payload.
public sealed record ProxyBidObserved(
    Guid SagaId,
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    bool IsProxy,
    DateTimeOffset PlacedAt);

// 2. Non-saga handler that queries active sagas on this listing and fans out.
public static class ProxyBidDispatchHandler
{
    public static async Task<OutgoingMessages> Handle(
        BidPlaced message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var sagas = await session.Query<ProxyBidManagerSaga>()
            .Where(s => s.ListingId == message.ListingId
                        && s.Status == ProxyBidManagerStatus.Active)
            .ToListAsync(cancellationToken);

        var outgoing = new OutgoingMessages();
        foreach (var saga in sagas)
        {
            outgoing.Add(new ProxyBidObserved(saga.Id, message.ListingId, /* ... */));
        }
        return outgoing;
    }
}

// 3. Saga correlates via the standard property-pull path against the wrapped command.
public sealed class ProxyBidManagerSaga : Saga
{
    public Guid Id { get; set; }
    /* ... */

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))] ProxyBidObserved message)
    {
        /* react to the wrapped bid */
    }
}
```

### Why not a field on the original contract

For sagas with a one-to-one relationship between an inbound event and a saga instance, you could add a `XxxSagaId` field to the contract and have the producer compute the v5 hash before emission. The Proxy Bid Manager case is **one-to-many** — a single `BidPlaced` targets every active proxy on the listing — so the field-on-contract path is not available.

### Where the cost lands

- The dispatcher runs one Marten query per inbound event (cheap key-range scan in production; the saga document doesn't exist before `RegisterProxyBid`).
- The fan-out emits N internal commands, each dispatched in its own scope.
- The saga's reactive Handle is a normal `[SagaIdentityFrom]` handler — no new Wolverine mechanism.
- Each saga's own-bid vs competing-bid branching is local; the dispatcher is correlation infrastructure, not business logic.

### In-repo ground

`src/CritterBids.Auctions/ProxyBidDispatchHandler.cs` (M4-S3) bridges `BidPlaced` to N `ProxyBidObserved` deliveries. The full path is the first lived composite-key saga correlation in CritterBids. M4-S3 retrospective §"OQ1 resolution" carries the original three-path decision (A: resolver-based `[SagaIdentityFrom]` — unavailable; B: field on contract — unavailable for one-to-many; C: dispatcher — selected).

---

## Multiple Handlers + `MultipleHandlerBehavior.Separated` — Send, Don't Invoke

When two handlers in the same BC subscribe to the same message type under `MultipleHandlerBehavior.Separated`, the bus dispatch path matters:

- `IMessageBus.InvokeAsync` is **single-handler-targeted** — it does not fan out. Under Separated mode with multiple handlers, it falls through to the default `local://{type}/` endpoint, which has no handler attached. `Wolverine.Runtime.Handlers.NoHandlerForEndpointException` surfaces from `HandlerGraph.cs:178–205` (the sticky-resolution branch).
- `IMessageBus.SendAsync` / `PublishAsync` (the publish path) **fans out** to each handler's auto-assigned sticky queue per the convention at `HandlerChain.cs:351–353` (queue name = handler type's lowercased full name).

In integration tests with `Wolverine.Tracking`:

```csharp
// ❌ WRONG — InvokeMessageAndWaitAsync(msg) calls c.InvokeAsync(msg).
//   With multiple handlers under Separated, throws NoHandlerForEndpointException
//   at `local://critterbids.contracts.auctions.bidplaced/` (the default endpoint
//   that has no sticky chain bound).
var tracked = await host.TrackActivity()
    .InvokeMessageAndWaitAsync(new BidPlaced(/* ... */));

// ✅ CORRECT — SendMessageAndWaitAsync(msg) calls c.SendAsync(msg).
//   Fans out to both handler sticky queues; each runs in its own scope.
var tracked = await host.TrackActivity()
    .DoNotAssertOnExceptionsDetected()
    .SendMessageAndWaitAsync(new BidPlaced(/* ... */));
```

`UseFastEventForwarding` itself uses `Context.PublishAsync` (see `PublishIncomingEventsBeforeCommit.cs` in Wolverine.Marten), so forwarded events fan out correctly in production. The trap is test code using the invoke path against a message that gained a second handler.

### Symptom shape

```
Wolverine.Runtime.Handlers.NoHandlerForEndpointException :
  No handlers for message type {Type} at endpoint local://{type}/.
  This is usually because of 'sticky' handler to endpoint configuration.
```

The endpoint reported is the *default* type-named endpoint, **not** a handler's auto-assigned queue. If you see this on a message that worked before adding a second handler in the same BC, switch the test dispatch from invoke to send.

### When `InvokeAsync` is still correct

`InvokeAsync` is correct for messages with exactly one handler — it preserves the call-and-wait-for-handler semantics that the publish path does not. For dispatch tests of a command with a single handler (the M2.5 / M3-S4 / M4-S2 pattern), keep using `InvokeMessageAndWaitAsync`. The switch to `SendMessageAndWaitAsync` is targeted: only when the dispatched message type has > 1 handler under Separated mode.

### In-repo ground

`tests/CritterBids.Auctions.Tests/ProxyBidManagerSagaTests.cs` (M4-S3) uses `SendMessageAndWaitAsync` for the three scenarios that dispatch `BidPlaced` (4.2 / 4.4 / 4.5) because `BidPlaced` gained `ProxyBidDispatchHandler` alongside the existing `AuctionClosingSaga.Handle(BidPlaced)`. The 4.1 scenario (`RegisterProxyBid` — single handler) and `PlaceBidDispatchTests` (M3-S4, dispatches `PlaceBid` — single handler) continue to use `InvokeMessageAndWaitAsync`.

---

## Saga-to-Saga Cascades — Eager / Single-Cycle Under `SendMessageAndWaitAsync`

A saga whose `OutgoingMessages` emit a command that another saga reacts to (and so on) forms a recursive cascade. `Wolverine.Tracking.SendMessageAndWaitAsync` is observed to wait for the **full cascade**, not just the first dispatch — every envelope (including recursively emitted ones) completes before the awaited tracked session returns. In CritterBids the M4-S4 §4.10 two-proxy bidding war runs ~10 alternating `PlaceBid` ↔ `BidPlaced` ↔ `ProxyBidObserved` hops between two `ProxyBidManagerSaga` instances and completes inside a single ~1-second tracked invocation.

### Implication for test shape

Assertions on **final state** work cleanly with one `SendMessageAndWaitAsync` call. No polling, no `Task.Delay`, no second dispatch needed.

```csharp
var tracked = await host.TrackActivity()
    .DoNotAssertOnExceptionsDetected()
    .Timeout(TimeSpan.FromSeconds(30))   // generous upper bound — cascade is fast in practice
    .SendMessageAndWaitAsync(new BidPlaced(/* trigger */));

// Assert end-state after the entire cascade settles:
(await fixture.LoadSaga<WeakerSaga>(id)).ShouldBeNull();              // exhausted + deleted
(await fixture.LoadSaga<StrongerSaga>(id)).Status.ShouldBe(Active);   // winning side
tracked.NoRoutes.MessagesOf<TerminationEvent>().ShouldHaveSingleItem();
```

### What the cascade requires

For the cascade to actually run multiple steps, every cascaded command must complete its handler successfully. DCB validators (M3-S4 `PlaceBidHandler`) reject commands when the listing stream lacks the events they expect, which silently halts the cascade at step one. **Seed the upstream Marten state** (`SeedListingStreamAsync` + `SeedAuctionClosingSagaAsync` in the CritterBids fixture) before dispatching the trigger.

### Assertion-bucket cross-cut when adding a handler

Adding a new BC-local handler for a cascade-produced event flips that event's `tracked.*` bucket assignment. CritterBids M4-S4 example: the four `AuctionClosingSagaTests.Close_*` tests previously asserted `tracked.NoRoutes.MessagesOf<ListingSold/ListingPassed>()` because no Auctions handler existed for those outcome events (Listings + Settlement handlers were fixture-excluded). After `ProxyBidDispatchHandler` gained `Handle(ListingSold)` / `Handle(ListingPassed)`, the cascade messages flipped to `tracked.Sent`. The cause is unavoidable — the dispatcher always runs even with empty fan-out, so the cascade outcome event is now a routed message. Pre-emptive search of test fixtures for `tracked.NoRoutes.MessagesOf<X>()` is the cheapest way to discover affected tests before they fail.

### In-repo ground

`tests/CritterBids.Auctions.Tests/ProxyBidManagerSagaTests.cs` (M4-S4) §`TwoProxies_WeakerExhausts_StrongerWins` exercises the bidding-war cascade end-to-end in a single tracked invocation. `tests/CritterBids.Auctions.Tests/AuctionClosingSagaTests.cs` carries the bucket-flip examples (Close_ReserveMet_ProducesListingSold and three siblings) updated at M4-S4 close.

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
