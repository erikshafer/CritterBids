---
name: integration-messaging
description: "Integration messaging in CritterBids: contracts, RabbitMQ queue posture, OutgoingMessages, durability, DeliverWithin, and fanout. Use when changing BC integrations."
cluster: wolverine
tags: [wolverine, rabbitmq, messaging, contracts, modular-monolith]
---

# Integration Messaging

> CritterBids conventions for asynchronous cross-BC communication.
> Generic RabbitMQ, routing, and resiliency mechanics live in ai-skills `wolverine-integrations-rabbitmq`, `wolverine-messaging-message-routing`, and `wolverine-messaging-resiliency-policies`; **this skill documents only the CritterBids-specific topology and posture.**

## When to apply this skill

Use this skill when:

- Adding or changing a message in `src/CritterBids.Contracts`.
- Wiring a publisher or subscriber queue between bounded contexts.
- Choosing durable inbox vs buffered listener posture for a CritterBids queue.
- Deciding whether a message may expire, needs a circuit breaker, or must fan out to multiple BC handlers.

Do NOT use this skill for: local handler shape (see `wolverine-message-handlers`), saga lifecycle rules (see `wolverine-sagas`), or event-sourced aggregate writes (see `marten-event-sourcing`).

## Read upstream first

Generic Wolverine messaging is fully covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of the mechanics:

1. `wolverine-integrations-rabbitmq` — RabbitMQ transport setup, exchanges, queues, durability, provisioning.
2. `wolverine-messaging-message-routing` — publish/send routing, local queues, delivery options, delayed delivery.
3. `wolverine-messaging-resiliency-policies` — retries, circuit breakers, DLQ, and policy precedence.
4. `critterstack-arch-modular-monolith` — separated handlers and modular-monolith fanout.

This skill picks up at the CritterBids contract, queue, and BC-specific posture decisions.

## Contract boundary

Integration messages cross BC boundaries and live only in `src/CritterBids.Contracts/<PublisherBc>/`. The namespace reflects the publisher, not the consumer.

```csharp
namespace CritterBids.Contracts.Auctions;

/// <summary>
/// Published by Auctions BC when a listing closes above reserve.
/// Consumed by Settlement, Obligations, Relay, and Operations.
/// </summary>
public sealed record ListingSold(
    Guid ListingId,
    Guid SellerId,
    Guid WinnerId,
    decimal HammerPrice,
    DateTimeOffset SoldAt);
```

CritterBids contract rules:

- `sealed record` for every integration message.
- Required, non-nullable fields only.
- `DateTimeOffset` timestamps with `*At` suffix.
- `IReadOnlyList<T>` for collections.
- XML comments list all known consumers.
- Payloads are rich enough for every known consumer; do not force downstream BCs to query back.
- `BidderId` names participant identity consistently across contracts.

## Publishing rule

Handlers and HTTP endpoints publish integration messages by returning `OutgoingMessages`, never by injecting `IMessageBus.PublishAsync`. This keeps message delivery inside the same Wolverine/Marten transaction and outbox boundary.

```csharp
public static (Events, OutgoingMessages) Handle(CloseAuction cmd, [WriteAggregate] Listing listing)
{
    var outgoing = new OutgoingMessages();
    outgoing.Add(new CritterBids.Contracts.Auctions.ListingSold(
        listing.Id,
        listing.SellerId,
        listing.HighBidderId!.Value,
        listing.CurrentHighBid,
        DateTimeOffset.UtcNow));

    return (new Events(new BiddingClosed(listing.Id, DateTimeOffset.UtcNow)), outgoing);
}
```

The only justified `IMessageBus` use in handlers is `bus.ScheduleAsync()` for delayed delivery. Immediate fire-and-forget work returns messages.

## Queue names

Pattern: `<consumer-bc>-<publisher-bc>-<category>`.

| Queue | Consumer | Publisher | Category |
|---|---|---|---|
| `selling-participants-events` | Selling | Participants | seller registration |
| `listings-selling-events` | Listings | Selling | listing publication |
| `settlement-auctions-events` | Settlement | Auctions | sale outcomes |
| `obligations-settlement-events` | Obligations | Settlement | payment/settlement outcomes |
| `relay-auctions-events` | Relay | Auctions | bid activity + close outcomes |
| `relay-settlement-events` | Relay | Settlement | payout/status notifications |
| `listings-auctions-events` | Listings | Auctions | listing status changes |
| `operations-auctions-events` | Operations | Auctions | ops dashboard state |

The queue name must match exactly on publisher and subscriber. One character off silently strands messages in the wrong queue.

## Durability posture by BC queue

Use host-level RabbitMQ wiring once in `Program.cs`; BC modules only declare publishes/listens. CritterBids is all-Marten/PostgreSQL per ADR 011, so queue posture is consistent across all BCs.

| Queue pattern | Endpoint type | Rationale |
|---|---|---|
| `settlement-*-events`, `obligations-*-events` | `UseDurableInbox()` | Financial and obligation paths need at-least-once delivery. |
| `listings-*-events`, `auctions-*-events` | `UseDurableInbox()` | Catalog state transitions drive downstream decisions. |
| `relay-*-events` | `BufferedInMemory()` | Notifications are reconstructable/retryable from source events; loss is tolerable. |
| `operations-*-events` | `BufferedInMemory()` | Ops dashboards are views; stale is bad, lost is acceptable. |
| `operations-*-live-ticks` | `BufferedInMemory()` + `DeliverWithin` | Flash-session ticks are transient and time-sensitive. |

Do not globally apply `UseDurableInboxOnAllListeners()` unless every queue truly needs persistence. Over-persisting Relay and operations live ticks adds database pressure without improving business correctness.

## DeliverWithin rule

Domain events and integration messages that represent business facts never expire. Operational signals may expire.

| Message kind | `DeliverWithin`? | Examples |
|---|---|---|
| Domain/business fact | Never | `ListingSold`, `ListingPassed`, `SettlementCompleted`, `ObligationFulfilled` |
| Reconstructable notification | Usually no TTL; loss-tolerant listener | Relay bid notifications derived from source events |
| Operational signal | Yes when stale delivery is harmful | `FlashSessionTick`, ops dashboard refresh, typing/presence signal |

If “deliver this late” is worse than “drop this,” use `DeliverWithin`. Otherwise keep the fact durable.

## Resiliency posture

Circuit breakers are per endpoint, not global. In CritterBids they belong first on external-facing BC queues:

| BC / queue | Circuit breaker posture | Why |
|---|---|---|
| Settlement (`settlement-*-events`) | Yes when payment/provider calls land | Payment gateway outages should pause this queue only. |
| Relay (`relay-*-events`) | Yes for outbound providers / SignalR fanout pressure | Notification-provider or connection-fanout failures should not pause catalog/financial processing. |
| Listings / Auctions / Obligations | No default breaker | Primarily internal Marten work; retries usually suffice. |
| Operations live ticks | Prefer discard/TTL over breaker | Stale ticks are disposable. |

Pair external-system retries with the endpoint circuit breaker. A retry-only policy against a down provider creates a retry storm and DLQ noise.

## Modular-monolith fanout settings

These are required once in `Program.cs`'s `UseWolverine()` block:

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
    opts.Durability.MessageStorageSchemaName = "wolverine";
});
```

`Separated` gives each BC handler for the same message its own queue/transaction/policy. `IdAndDestination` prevents durable inbox dedup from treating fanout deliveries as duplicates just because they share the same message id.

## Adding a new integration

1. Define the contract in `src/CritterBids.Contracts/<PublisherBc>/`.
2. Document publisher and all known consumers in XML comments.
3. Build a consumer payload table before finalizing fields.
4. Publisher returns the message through `OutgoingMessages`.
5. Publisher declares `PublishMessage<T>().ToRabbitQueue("...")`.
6. Consumer declares `ListenToRabbitQueue("...")` with the queue posture above.
7. Consumer handler uses the single primary Marten store; no `[MartenStore]` attribute is needed.
8. Add a cross-BC smoke test with real RabbitMQ when the route matters.
9. Update `docs/vision/bounded-contexts.md` only for a new integration direction, not for another message on an existing direction.

## Common pitfalls

- **Missing publish route.** `OutgoingMessages` without a routing rule can be silently unrouted; add `PublishMessage<T>()` / `Publish(...)` and verify with diagnostics.
- **Queue name mismatch.** Publisher and subscriber strings must be identical.
- **Contract changed before consumers.** Deploy consumers before publishers; dual-publish during retirements with multiple consumers.
- **Thin payload.** If Relay, Operations, and Settlement need different fields, design for all before publishing.
- **Terminal saga states omitted.** Any saga consuming integration events must handle every terminal state and call `MarkCompleted()`.
- **Building outgoing messages from mutated entity state.** Use command/message values when they are the source of truth for the emitted payload.
- **Global circuit breaker.** Pause only the endpoint touching the failing external dependency.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-integrations-rabbitmq` — RabbitMQ transport and provisioning mechanics.
- `wolverine-messaging-message-routing` — publish/listen routing and delivery options.
- `wolverine-messaging-resiliency-policies` — retries, circuit breakers, DLQ, and diagnostics.
- `critterstack-arch-modular-monolith` — separated multi-handler fanout in modular monoliths.

**Prerequisites:**

- `wolverine-message-handlers` — handler return values and the outbox routing-rule footgun.
- `marten-event-sourcing` — all-Marten host wiring and transaction placement.

**Downstream:**

- `wolverine-sagas` — orchestration over cross-BC messages.
- `wolverine-signalr` — Relay BC real-time push from integration events.
- `critter-stack-testing-patterns` — cross-BC smoke tests and fixture isolation.

**External:**

- ADR 011 (All-Marten Pivot), ADR 009 (shared primary store) in [`docs/decisions/`](../../decisions/).
- [`docs/vision/bounded-contexts.md`](../../vision/bounded-contexts.md) — current integration topology.
- [`CLAUDE.md`](../../../CLAUDE.md) § Modular Monolith Rules and § Core Conventions.
