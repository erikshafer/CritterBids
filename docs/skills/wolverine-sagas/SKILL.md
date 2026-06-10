---
name: wolverine-sagas
description: "Wolverine sagas in CritterBids: orchestration state, terminal MarkCompleted paths, scheduling, correlation, and test traps. Use when building long-running BC workflows."
cluster: wolverine
tags: [wolverine, sagas, scheduling, orchestration, marten]
---

# Wolverine Sagas

> CritterBids conventions for stateful Wolverine workflows.
> Generic handler, messaging, scheduling, and resiliency mechanics live in ai-skills `wolverine-handlers-*` and `wolverine-messaging-*`; **this skill documents only the CritterBids-specific saga decisions.**

## When to apply this skill

Use this skill when:

- Building or changing a long-running workflow such as `AuctionClosingSaga`, `ProxyBidManagerSaga`, `SettlementSaga`, or `ObligationsSaga`.
- Deciding between a document-based saga, an event-sourced aggregate, a DCB decision, or a stateless handler.
- Adding terminal states, delayed delivery, timeout chains, or self-sent continuation commands.
- A saga test starts failing after adding another handler for the same message type.

Do NOT use this skill for: generic handler signatures (see `wolverine-message-handlers`), Marten aggregate mutation (see `marten-event-sourcing`), integration contracts (see `integration-messaging`), or single-decision cross-stream rules (see `dynamic-consistency-boundary`).

## Read upstream first

No dedicated saga ai-skill exists in the verified `C:\Code\JasperFx\ai-skills\skills\` directory. Read these closest upstream skills first (license required; install via `npx skills add`) — they cover ~80% of the mechanics this skill assumes:

1. `wolverine-handlers-fundamentals` — handler discovery, cascading return values, `OutgoingMessages`, and the “don’t inject `IMessageBus` to publish” rule.
2. `wolverine-handlers-declarative-persistence` — Wolverine-managed persistence and transaction boundaries.
3. `wolverine-messaging-message-routing` — local dispatch, send vs publish routing, delayed delivery options.
4. `wolverine-messaging-resiliency-policies` — retries, cooldowns, circuit breakers, DLQ policy placement.
5. `marten-aggregate-handler-workflow` — event-sourced aggregate workflow, for the “aggregate vs saga” decision.
6. `wolverine-testing-integration-marten` — tracked sessions and Wolverine + Marten integration-test shape.

This skill picks up at the CritterBids decisions and the saga-specific traps this codebase hit.

## Saga vs aggregate vs handler — CritterBids decision rule

A saga is orchestration state: mutable, write-heavy, read-light, and disposable after the workflow ends. A domain aggregate is business history: event-sourced, replayable, and retained.

| Situation | Use | CritterBids examples |
|---|---|---|
| Coordinate steps or BCs over time | Document-based saga | Auction closing, settlement, obligations |
| Preserve domain history for one entity | Event-sourced aggregate | Listing, participant, financial audit stream |
| One immediate cross-stream rule | DCB | Auction bid validation / capacity-like rules |
| Stateless transformation or notification | Message handler | Relay notification handlers |

Document-based sagas use Marten documents with numeric revisions — and the saga must implement
`JasperFx.IRevisioned`, or the revisions are bumped but never enforced:

```csharp
public sealed class AuctionClosingSaga : Wolverine.Saga, JasperFx.IRevisioned
{
    public int Version { get; set; }   // load-bearing, not decoration
    ...
}

opts.Schema.For<AuctionClosingSaga>()
    .Identity(x => x.Id)
    .UseNumericRevisions(true);
```

Wolverine's Marten saga persistence (6.5.1, `MartenPersistenceFrameProvider.DetermineUpdateFrame`)
emits the revision-checked `UpdateSagaRevisionFrame` only when the saga type implements
`IRevisioned`; otherwise it generates a plain `IDocumentSession.Update` and concurrent saga
writes last-writer-win silently. CritterBids shipped three milestones with the schema half only —
the M8-S3c live verification caught two concurrent `ClosingBidObserved` handlers closing an
auction with the WRONG winner (both loaded `BidCount = 0`; the stale write committed last). The
fan-out era's duplicate deliveries had been masking the race by re-applying the lost update.
Pair the interface with an `OnException<ConcurrencyException>().RetryWithCooldown(...)` policy so
the losing write reloads and re-applies through the saga's idempotency guards.

Keep saga state minimal. Store a field only if it gates a `Handle` branch; load emission-only payload data from the source of truth when emitting terminal messages. This avoided widening `AuctionClosingSaga` just to carry `SellerId` for `ListingSold`.

## CritterBids saga shapes

### Two-phase accumulator: Auction Closing

`AuctionClosingSaga` starts on `BiddingOpened`, accumulates bid/reserve state while the auction runs, and closes on a scheduled `CloseAuction`.

| Property | Auction Closing shape |
|---|---|
| Trigger sources | one open event, many bid events, one close timer |
| Time scale | hours / days |
| Continuation | scheduled `CloseAuction` via `bus.ScheduleAsync` |
| Terminal outcomes | sold, passed, withdrawn |
| Audit stream | none; outcome messages flow to other BCs |

Use this shape when external messages arrive throughout the workflow lifetime.

### Multi-phase pipeline: Settlement

`SettlementSaga` is a pipeline: `Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed`. Each phase emits a self-sent continuation command through `OutgoingMessages`.

```csharp
public OutgoingMessages Handle(
    [SagaIdentityFrom(nameof(ChargeWinner.SettlementId))] ChargeWinner message,
    IDocumentSession session)
{
    if (Status != SettlementStatus.ReserveChecked)
        throw new InvalidSettlementTransitionException(Id, Status, nameof(ChargeWinner));

    Status = SettlementStatus.WinnerCharged;
    session.Events.Append(Id, new WinnerCharged(Id, WinnerId, HammerPrice, DateTimeOffset.UtcNow));

    return new OutgoingMessages { new CalculateFee(Id) };
}
```

Use this shape when each step depends on the previous step and no external traffic should arrive between steps. Guard every phase entry with the expected status. Settlement also appends to `FinancialEventStream` because financial history matters even after the mutable saga document is deleted.

### Timeout-chain saga: Obligations

`ObligationsSaga` owns cancellable scheduled messages: seller reminders, shipping deadlines, dispute windows, and terminal fulfilment paths. It stays open while sub-workflows (for example a dispute) are active; premature `MarkCompleted()` is worse than late closure because late messages find no saga.

## Correlation conventions

Prefer a direct saga id on commands (`{SagaName}Id`) or `[SagaIdentityFrom(nameof(Command.SomeId))]` when the inbound contract already carries the saga key — **but only when the saga is the message type's sole handler** (see the Separated-mode caveat below).

For composite or one-to-many correlation, use a dispatcher command instead of inventing unsupported Wolverine identity resolvers. `ProxyBidManagerSaga.Id` is derived from `(ListingId, BidderId)`, and a single `BidPlaced` can target many proxy sagas, so CritterBids uses:

1. A non-saga `ProxyBidDispatchHandler` queries active proxy sagas for the listing.
2. It emits one internal `ProxyBidObserved(SagaId, ...)` per target via `OutgoingMessages`.
3. `ProxyBidManagerSaga` correlates normally with `[SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))]`.

This keeps correlation infrastructure out of the original integration contract.

## Separated-mode rule: never let a saga subscribe directly to a multi-consumer contract event

Under `MultipleHandlerBehavior.Separated`, Wolverine 6.5.1 keeps a SINGLE saga type's
continue-handlers in the chain's default `Handlers` (`SagaChain.maybeAssignStickyHandlers` only
separates sagas into sticky chains when more than one saga type handles the message). A chain
with a default handler never fans externally-delivered (RabbitMQ) messages out to the sticky
local queues the other handlers live on — the saga silently consumes every broker delivery,
every other consumer starves, the log says "Successfully processed", and nothing dead-letters.
This was the root cause of M8 Bug #2 (`BidPlaced` never reaching Listings / Relay / Operations).

Rules:

- A saga may handle a message type directly only when the saga is that type's **sole** in-process
  handler (e.g. a scheduled `CloseAuction`).
- When a contract event has other consumers, bridge the saga behind an internal dispatcher
  command — `AuctionClosingDispatchHandler` → `Closing*Observed` is the 1:1 shape (pure
  translation, no query); `ProxyBidDispatchHandler` → `ProxyBidObserved` is the one-to-many shape.
- Saga **start** via a separate static handler class is safe: that chain is a plain
  `HandlerChain`, all handlers go sticky, and fan-out works (this is why `BiddingOpened` never
  exhibited the bug).
- Bridge-relayed commands arrive once per consuming queue (at-least-once × fan-out); keep the
  saga's idempotency guards and add static `NotFound` absorbers for post-completion stragglers.

Root cause analysis + upstream fix proposal:
`docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md` and
`docs/research/wolverine-upstream-saga-sticky-separation-handoff.md`. Revisit this rule if the
upstream fix ships (the bridge remains the preferred design either way — it decouples the saga
from contract churn).

## Scheduling rule

`bus.ScheduleAsync()` is the only justified `IMessageBus` use inside CritterBids handlers. Use it for delayed delivery such as auction close timers and obligation deadlines. Use `OutgoingMessages` for immediate self-sent continuation commands and integration messages.

Store cancellation tokens/message ids when the business process must cancel or reschedule a timer (extended bidding / anti-snipe windows, fulfilled obligations before deadline). Do not use `PublishAsync` for fire-and-forget work.

## Terminal lifecycle rule

Every terminal path calls `MarkCompleted()`. Handle **all** terminal states from every BC a saga consumes.

CritterBids terminal examples:

- Auction closing: `ListingSold`, `ListingPassed`, `ListingWithdrawn`.
- Proxy bidding: max exhausted, listing sold/passed, bidder cancellation.
- Settlement: completed, reserve failed, payment failed.
- Obligations: fulfilled, dispute resolved, cancellation/timeout terminal.

If a correlated message can legitimately arrive after completion, add a static `NotFound` method on the saga for that message type and make the no-op explicit. Use it only to absorb late retries or timers; do not resurrect a workflow that closed too early.

## Testing traps

- Under `MultipleHandlerBehavior.Separated`, use `SendMessageAndWaitAsync` for messages with more than one handler. `InvokeMessageAndWaitAsync` is single-handler targeted and can throw `NoHandlerForEndpointException` against the default type-named endpoint.
- `SendMessageAndWaitAsync` waits for full saga-to-saga cascades in observed CritterBids tests. Assert final state after one tracked invocation, but seed all upstream Marten state the cascade needs.
- Adding a handler can move messages from `tracked.NoRoutes` to `tracked.Sent`. Search for `tracked.NoRoutes.MessagesOf<T>()` when a message gains a new handler.
- `IMessageBus` is scoped. Tests that need raw `ScheduleAsync` must create a scope or use the fixture helper.

## Common pitfalls

- **Missing terminal handlers.** Handling `SettlementCompleted` but not `PaymentFailed` leaves a dangling saga document.
- **Premature `MarkCompleted()`.** Closing while a dispute/return/timeout branch is still active drops later compensation messages.
- **Overwide saga state.** Carrying emission-only fields through the saga increases numeric-revision conflicts and schema churn.
- **Composite correlation in the public contract.** Derived one-to-many saga ids belong in an internal dispatcher command, not a cross-BC event.
- **Saga continue-handler on a multi-consumer contract event.** Under `Separated`, the lone saga stays the default handler and silently starves every sticky consumer of broker deliveries (M8 Bug #2) — bridge it via a dispatcher command.
- **Invoke path in separated mode.** Use send/publish for fanout messages; reserve invoke for a known single handler.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-handlers-fundamentals`, `wolverine-handlers-declarative-persistence` — handler returns, persistence, and `OutgoingMessages`.
- `wolverine-messaging-message-routing`, `wolverine-messaging-resiliency-policies` — routing, delayed delivery, retries, DLQ policies.
- `marten-aggregate-handler-workflow` — event-sourced aggregates when history, not orchestration, is the center.
- `wolverine-testing-integration-marten` — integration-test harness patterns.

**Prerequisites:**

- `wolverine-message-handlers` — handler shape, `OutgoingMessages`, and the outbox routing-rule footgun.
- `marten-event-sourcing` — all-Marten host wiring, stream identity, and aggregate boundaries.

**Downstream:**

- `integration-messaging` — cross-BC contract and RabbitMQ posture.
- `critter-stack-testing-patterns` — cross-BC fixture isolation and tracked-session assertions.

**External:**

- ADR 011 (All-Marten Pivot), ADR 009 (shared primary store) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions and § BC Module Quick Reference.
