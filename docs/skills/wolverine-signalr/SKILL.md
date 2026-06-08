---
name: wolverine-signalr
description: "Wolverine SignalR in CritterBids: Relay plain hubs, direct IHubContext push, the raw-record ReceiveMessage wire contract (ADR-023, no CloudEvents envelope), group routing, request/reply, and group changes. Use when wiring real-time updates."
cluster: wolverine
tags: [wolverine, signalr, relay, realtime]
---

# Wolverine + SignalR

> CritterBids conventions for real-time push from Relay BC handlers. CritterBids uses **plain `Hub` + direct `IHubContext` push** (ADR-023 path b), **not** the Wolverine SignalR transport.
> Generic SignalR transport mechanics live in ai-skills `wolverine-integrations-signalr`; **this skill documents only the CritterBids-specific routing, wire contract, and posture decisions.**

## When to apply this skill

Use this skill when:

- Adding a Relay BC notification from an integration event.
- Changing `BiddingHub` or `OperationsHub` group enrollment.
- Confirming the server-side wire contract a client consumes (raw record on `ReceiveMessage`).
- Deciding whether a future feature needs `WolverineHub`, request/reply, client-driven group changes, or JSON serializer overrides.

Do NOT use this skill for: generic Wolverine handler mechanics (see `wolverine-message-handlers`), integration contract design (see `integration-messaging`), or Marten projection side-effect mechanics (see `marten-projections` / `marten-event-sourcing`).

## Read upstream first

Generic Wolverine SignalR mechanics are covered upstream. Read this ai-skill (license required; install via `npx skills add`) before this skill — it covers ~80% of the transport:

1. `wolverine-integrations-signalr` — SignalR transport setup, `ToSignalR`, `WolverineHub`, group targeting, request/reply, client transport testing.

This skill picks up at CritterBids' Relay BC decisions, the ADR-023 wire contract, and the “not used today, but use when…” posture.

## CritterBids architecture

CritterBids has two hubs, both owned by Relay BC:

| Hub | Route | Audience | Current posture |
|---|---|---|---|
| `BiddingHub` | `/hub/bidding` | anonymous participants | outbound-only plain `Hub`; group enrollment by `BidderId` / `listingId` query string |
| `OperationsHub` | `/hub/operations` | staff/operators | outbound-only plain `Hub`; `[Authorize(Policy = "StaffOnly")]`-gated (ADR-024); group enrollment auto-enrolled to `ops:staff` on connect |

Relay is a consumer-only BC for real-time surfaces. It consumes integration events and returns SignalR messages. It does not publish integration events and should not inject `IMessageBus` for fanout.

CritterBids uses plain `Hub` today because client-to-server commands go through HTTP endpoints. Use `WolverineHub` only if a future feature genuinely needs commands delivered over the WebSocket connection.

## Wire contract — raw notification record (ADR-023)

CritterBids pushes the **raw notification record** as the `ReceiveMessage` payload. There is **no CloudEvents envelope**: the handler's argument *is* the record the client deserializes. ADR-023 chose plain-`Hub` + direct `IHubContext` push (path b) and explicitly rejected the Wolverine SignalR transport (path a) — that transport wraps each message in a CloudEvents envelope and pushes it to `IHubContext<WolverineHub>`, a hub the application never maps, so it never reaches a client connected at `/hub/bidding`.

So on the wire there is **no `cloudEvent.type`, no `cloudEvent.data`, and no FQN to `.split(".")`**. A client reading those fields gets `undefined` on every branch.

```typescript
// The handler argument IS the domain record (ADR-023 path b) — no envelope to unwrap.
connection.on("ReceiveMessage", (notification: unknown) => {
    // validate through a Zod schema at the wire boundary (ADR-013), then use it
});
```

The client-side hook, lifecycle, Zod validation, and per-hub auth live in the companion **client** skill — [`.claude/skills/signalr/SKILL.md`](../../../.claude/skills/signalr/SKILL.md). Server and client skills tell the same wire-contract story; keep them in sync.

> **Client message-type discrimination is out of scope here.** Because there is no envelope `type`, how a client tells one notification record from another on the single `ReceiveMessage` method — a discriminator field on the record vs. distinct hub-method names, plus its Zod schema — is **M8-S3 live-bidding work, to be recorded in ADR-014**, not decided in this skill. See the client skill's "Open question" section; do not invent a discrimination scheme here.

## Marker interfaces (documentation / future-door only)

Marker interfaces live in `CritterBids.Relay`, not `CritterBids.Api`, so domain intent stays in the module. Under ADR-023 path (b) they **carry no routing behaviour** — group targeting is explicit in each handler (see below). They are retained as documentation and as a future-door affordance should Option C (transport-routed hubs) ever be reopened.

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

In CritterBids Relay, handlers inject `IHubContext<THub>` and call `SendAsync(...)` directly. This is intentional and aligns with ADR 023.

## Lived Relay update (M6)

The first lived CritterBids Relay implementation (M6-S5/S6) validated that the Wolverine SignalR transport path is not the right fit for Relay's mapped plain hubs. Relay uses plain `Hub` endpoints (`/hub/bidding`, `/hub/operations`) and direct `IHubContext<THub>` pushes from handlers.

Use this pattern for Relay handlers:

```csharp
public static Task Handle(SomeIntegrationEvent message, IHubContext<BiddingHub> hub, CancellationToken cancellationToken)
{
    var notification = new SomeNotification(...);

    return hub.Clients
        .Group($"listing:{message.ListingId}")
        .SendAsync(RelayHubMethods.ReceiveMessage, notification, cancellationToken);
}
```

Guidance:

- Keep handlers as pure consumers (`Task`/`void` return only).
- Keep group targeting explicit in each handler.
- Keep payload records strongly typed; no anonymous objects.
- Use `RelayHubMethods.ReceiveMessage` as the hub method name.

## Group keys

| Group key | Hub | Meaning |
|---|---|---|
| `listing:{ListingId}` | BiddingHub | participants watching a listing live feed |
| `bidder:{BidderId}` | BiddingHub | one participant's private-ish outbid / post-sale notifications |
| `ops:staff` | OperationsHub | all connected operators |

Query-string group enrollment is acceptable for current anonymous participant sessions because listing/bid feed data is public demo data and `BidderId` is not a trust anchor. For staff or commercially sensitive group keys, derive identity from JWT claims only.

## Hub authentication (ADR-024)

OperationsHub is gated by `[Authorize(Policy = "StaffOnly")]` (ADR-024). The StaffToken authentication handler reads the credential from:

1. `X-Staff-Token` header — for regular HTTP endpoints.
2. `access_token` query string — for the OperationsHub WebSocket negotiate request only (SignalR clients cannot set custom headers on the negotiate POST).

BiddingHub remains anonymous (`[AllowAnonymous]` is implicit — no attribute required since the default challenge scheme falls through for unauthenticated connections).

Security notes:

- The `access_token` query-string fallback is scoped to the `/hub/operations` path only in the StaffToken handler.
- No HTTP request logging middleware is registered, so the access_token is never written to logs.
- Production must terminate TLS in front of this host so the query-string credential is never sent in cleartext — this is host/ingress configuration, not application code.

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

- **Expecting a CloudEvents envelope on the client.** ADR-023 path (b) delivers the raw notification record on `ReceiveMessage`; there is no `.type` / `.data` and nothing to `.split(".")`. See [`.claude/skills/signalr/SKILL.md`](../../../.claude/skills/signalr/SKILL.md).
- **Using `WolverineHub` by default.** Plain `Hub` is correct while client-to-server commands use HTTP.
- **Putting marker interfaces in `CritterBids.Api`.** They belong in Relay BC so domain intent stays in the module.
- **Query-string identity for sensitive groups.** Fine for anonymous demo bidding; not fine for staff or commercial data access.
- **Routing pushes through the Wolverine SignalR transport.** Under ADR-023 the transport pushes to `IHubContext<WolverineHub>`, not the mapped plain hubs, so it never reaches CritterBids clients. The endorsed Relay pattern is a handler injecting `IHubContext<THub>` and calling `SendAsync` directly — the handler *is* the broadcaster; do not hand-roll a separate broadcaster singleton either.
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
