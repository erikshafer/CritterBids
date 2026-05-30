---
name: wolverine-signalr
description: "Wolverine SignalR in CritterBids: Relay hubs, marker-interface CloudEvents types, group routing, request/reply, and group changes. Use when wiring real-time updates."
cluster: wolverine
tags: [wolverine, signalr, relay, realtime, cloudevents]
---

# Wolverine + SignalR

> CritterBids conventions for real-time push through Wolverine's SignalR transport.
> Generic SignalR transport mechanics live in ai-skills `wolverine-integrations-signalr`; **this skill documents only the CritterBids-specific routing, envelope, and posture decisions.**

## When to apply this skill

Use this skill when:

- Adding a Relay BC notification from an integration event.
- Changing `BiddingHub` or `OperationsHub` group enrollment.
- Debugging the JavaScript client’s CloudEvents `type` switching.
- Deciding whether a future feature needs `WolverineHub`, request/reply, client-driven group changes, or JSON serializer overrides.

Do NOT use this skill for: generic Wolverine handler mechanics (see `wolverine-message-handlers`), integration contract design (see `integration-messaging`), or Marten projection side-effect mechanics (see `marten-projections` / `marten-event-sourcing`).

## Read upstream first

Generic Wolverine SignalR mechanics are covered upstream. Read this ai-skill (license required; install via `npx skills add`) before this skill — it covers ~80% of the transport:

1. `wolverine-integrations-signalr` — SignalR transport setup, `ToSignalR`, `WolverineHub`, group targeting, request/reply, client transport testing.

This skill picks up at CritterBids' Relay BC decisions, CloudEvents naming finding, and “not used today, but use when…” posture.

## CritterBids architecture

CritterBids has two hubs, both owned by Relay BC:

| Hub | Route | Audience | Current posture |
|---|---|---|---|
| `BiddingHub` | `/hub/bidding` | anonymous participants | outbound-only plain `Hub`; group enrollment by `BidderId` / `listingId` query string |
| `OperationsHub` | `/hub/operations` | staff/operators | outbound-only plain `Hub`; MVP passphrase, production JWT claims |

Relay is a consumer-only BC for real-time surfaces. It consumes integration events and returns SignalR messages. It does not publish integration events and should not inject `IMessageBus` for fanout.

CritterBids uses plain `Hub` today because client-to-server commands go through HTTP endpoints. Use `WolverineHub` only if a future feature genuinely needs commands delivered over the WebSocket connection.

## CloudEvents type format — CritterBids finding

Wolverine wraps outbound hub messages in a CloudEvents envelope. The JavaScript client must read `cloudEvent.data` for the payload and inspect `cloudEvent.type` for the message kind.

Source verification of `WolverineMessageNaming` found these `type` shapes:

| If the message type… | `type` field is… | Example |
|---|---|---|
| Inherits `WebSocketMessage` | kebab-case / transport naming from type name | `WebSocketBidPlaced` → `bid_placed` |
| Implements a marker interface only | fully-qualified .NET type name | `CritterBids.Relay.BiddingHub.BidPlacedNotification` |
| Carries `[MessageIdentity("custom")]` | the attribute value | `bid.placed.v1` |

CritterBids uses marker interfaces (`IBiddingHubMessage`, `IOperationsHubMessage`) because they carry routing metadata such as `ListingId` and `BidderId`. Therefore the React client splits the FQN and switches on the short type name:

```typescript
connection.on("ReceiveMessage", cloudEvent => {
    const typeName = (cloudEvent.type ?? "").split(".").pop() ?? "";
    const data = cloudEvent.data;

    switch (typeName) {
        case "BidPlacedNotification": onBidPlaced(data); break;
        case "BidderOutbidNotification": onOutbid(data); break;
        case "ExtendedBiddingNotification": onExtended(data); break;
    }
});
```

Do not “fix” the client to expect kebab-case unless the server message model switches from marker interfaces to `WebSocketMessage` or `[MessageIdentity]` aliases.

## Marker-interface routing

Marker interfaces live in `CritterBids.Relay`, not `CritterBids.Api`. They express domain-level routing intent.

```csharp
public interface IBiddingHubMessage
{
    Guid? ListingId { get; }
    Guid? BidderId { get; }
}

public interface IOperationsHubMessage { }
```

Representative records:

```csharp
public sealed record BidPlacedNotification(
    Guid ListingId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    DateTimeOffset OccurredAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;
    Guid? IBiddingHubMessage.BidderId => null; // listing-wide feed
}

public sealed record LiveBidActivityUpdate(
    Guid ListingId,
    decimal CurrentHighBid,
    int BidCount,
    string HighBidderDisplay,
    DateTimeOffset OccurredAt) : IOperationsHubMessage;
```

Relay handlers return typed messages and let Wolverine route them to SignalR. Do not hand-roll `IHubContext<T>.SendAsync` broadcasters unless a feature needs a SignalR capability Wolverine cannot express.

## Group keys

| Group key | Hub | Meaning |
|---|---|---|
| `listing:{ListingId}` | BiddingHub | participants watching a listing live feed |
| `bidder:{BidderId}` | BiddingHub | one participant's private-ish outbid / post-sale notifications |
| `ops:staff` | OperationsHub | all connected operators |

Query-string group enrollment is acceptable for current anonymous participant sessions because listing/bid feed data is public demo data and `BidderId` is not a trust anchor. For staff or commercially sensitive group keys, derive identity from JWT claims only.

## Obligations → hub routing posture

M6 obligations notifications reuse existing group keys; no new group convention is introduced.

| Event | Hub | Group key | Rationale |
|---|---|---|---|
| `TrackingInfoProvided` | BiddingHub | `bidder:{WinnerId}` | tracking confirmation to winner; payload may need additive winner id if not available from correlation |
| `ObligationFulfilled` | BiddingHub | `bidder:{WinnerId}` and `bidder:{SellerId}` | completion notice to both parties |
| `DisputeOpened` | OperationsHub | `ops:staff` | staff work queue item |
| `DisputeResolved` | BiddingHub + OperationsHub | affected `bidder:{...}` + `ops:staff` | participant notification and staff board update |

Relay handlers for these events return SignalR notification records only: no `OutgoingMessages`, no `IMessageBus`.

## Request/reply posture

`ResponseToCallingWebSocket<T>` is useful only when a command was invoked through a SignalR client send and the reply should go to that one connection.

CritterBids does not use this today. Both hubs are outbound-only and client-to-server traffic goes through HTTP endpoints. Reach for request/reply only if a future staff tool needs WebSocket-scoped request/response without an HTTP endpoint.

## JSON serialization override posture

SignalR transport JSON settings are isolated from HTTP and Marten serialization. CritterBids accepts the default camelCase shape because it matches the React/TypeScript client.

Use `OverrideJson(...)` only if a downstream WebSocket consumer has incompatible serializer expectations. Do not change host-level JSON or Marten settings to solve a SignalR-only payload issue.

## Client-driven group changes posture

`AddConnectionToGroup` / `RemoveConnectionToGroup` are for commands that arrive via `WolverineHub`, so Wolverine knows the calling connection.

CritterBids does not use this today. Current group membership is assigned at connection time; switching listing feeds can reconnect with new query parameters. Use client-driven group changes only if a future BiddingHub feature needs mid-session listing/bidder subscription changes without reconnect. HTTP-delivered group changes cannot use these return types because the SignalR connection is not in the HTTP handler context.

## Testing posture

The upstream SignalR skill covers the client transport mechanics. CritterBids-specific reminder: do not use `WebApplicationFactory` for SignalR handshake tests. Use real Kestrel on a dynamic port and track both the server host and Wolverine client host.

## Common pitfalls

- **Expecting kebab-case `type` with marker interfaces.** CritterBids marker-interface messages produce FQN CloudEvents types; split on `.` in the JS client.
- **Using `WolverineHub` by default.** Plain `Hub` is correct while client-to-server commands use HTTP.
- **Putting marker interfaces in `CritterBids.Api`.** They belong in Relay BC so routing intent stays in the module.
- **Query-string identity for sensitive groups.** Fine for anonymous demo bidding; not fine for staff or commercial data access.
- **Manual hub broadcasters.** Return typed messages and let Wolverine route them unless the framework lacks the required capability.
- **Missing React cleanup.** `useEffect` must call `connection.stop()` to avoid ghost connections.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-integrations-signalr` — SignalR transport setup, hub routing, group targeting, request/reply, and client transport testing.

**Prerequisites:**

- `integration-messaging` — Relay consumes cross-BC contracts before pushing to hubs.
- `wolverine-message-handlers` — typed return values and handler shape.

**Downstream:**

- `marten-projections` — projection side effects that publish live-view updates.
- `projection-side-effects-for-broadcast-live-views` — live view broadcast pattern.
- `critter-stack-testing-patterns` — integration testing and cross-BC fixture isolation.

**External:**

- [`docs/vision/live-queries-and-streaming.md`](../../vision/live-queries-and-streaming.md) — real-time design notes and open questions.
- [`CLAUDE.md`](../../../CLAUDE.md) § BC Module Quick Reference and § Core Conventions.
