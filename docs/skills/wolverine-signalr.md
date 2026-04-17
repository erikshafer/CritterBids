# Wolverine + SignalR: Real-Time Transport Patterns

Guide for using Wolverine's native SignalR transport in CritterBids — covering hub design, server configuration, marker interfaces, group management, authentication, the Marten projection side effects pipeline, React client integration, and integration testing.

---

## Table of Contents

1. [CritterBids SignalR Architecture](#critterbids-signalr-architecture)
2. [Core Concepts](#core-concepts)
3. [Server Configuration](#server-configuration)
4. [Marker Interfaces and Message Routing](#marker-interfaces-and-message-routing)
5. [Hub Group Management](#hub-group-management)
6. [Authentication Patterns](#authentication-patterns)
7. [Marten Projection Side Effects Pipeline](#marten-projection-side-effects-pipeline)
8. [React Client Integration](#react-client-integration)
9. [SignalR Client Transport (Integration Testing)](#signalr-client-transport-integration-testing)
10. [Scaling Considerations](#scaling-considerations)
11. [Common Pitfalls](#common-pitfalls)
12. [Lessons Learned](#lessons-learned)

---

## CritterBids SignalR Architecture

CritterBids uses two hubs:

**`BiddingHub`** (`/hub/bidding`) — participant-facing. Delivers real-time bid feed, outbid notifications, extended bidding triggers, and listing status changes to anonymous participants during a live auction. Group enrollment based on `BidderId` query string — acceptable for anonymous sessions with no sensitive commercial data.

**`OperationsHub`** (`/hub/operations`) — staff-facing. Drives the live ops dashboard during conference demos and day-to-day operations. Delivers bid activity feed, saga state changes, settlement updates, and dispute queue changes. Protected by staff passphrase.

Both hubs are registered in `CritterBids.Relay` BC — the single outbound communication module.

---

## Core Concepts

### The CloudEvents Envelope

Wolverine wraps every outbound message in a [CloudEvents](https://cloudevents.io/) JSON envelope:

```json
{
  "specversion": "1.0",
  "type": "CritterBids.Relay.BiddingHub.BidPlacedNotification",
  "source": "critterbids-api",
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "time": "2026-04-08T14:30:00Z",
  "datacontenttype": "application/json",
  "data": {
    "listingId": "...",
    "bidderId": "...",
    "amount": 125.00,
    "bidCount": 7,
    "occurredAt": "2026-04-08T14:30:00Z"
  }
}
```

**The `data` field is the actual payload** — your JavaScript client must unwrap it. The `type` field is the fully-qualified .NET type name (or a kebab-case alias if using `WebSocketMessage`).

All outgoing messages from the server go through the `ReceiveMessage` client method:

```javascript
connection.on("ReceiveMessage", (cloudEvent) => {
    const payload = cloudEvent.data; // unwrap the envelope
});
```

### WolverineHub vs Plain Hub

This is the most important architectural decision for each hub:

| Scenario | Hub Type |
|---|---|
| Client sends messages **via WebSocket** and Wolverine routes them to handlers | `WolverineHub` (inherit from it) |
| Server pushes to clients; client→server goes via HTTP endpoints | Plain `Hub` (inherit from `Hub`) |

**CritterBids uses plain hubs for both `BiddingHub` and `OperationsHub`.** Participants and ops staff interact with the server via HTTP endpoints (PlaceBid, StartSession, etc.). SignalR is outbound-only — push from server to client. `WolverineHub` is not needed.

> **Rule:** If you find yourself inheriting `WolverineHub`, ask whether client→server communication actually needs to travel via WebSocket or whether HTTP endpoints are the right answer. They usually are.

---

## Server Configuration

```bash
dotnet add package WolverineFx.SignalR
```

In the Relay BC's `AddRelayModule()`:

```csharp
builder.Host.UseWolverine(opts =>
{
    // Registers Wolverine SignalR transport + calls AddSignalR() internally
    opts.UseSignalR();

    // Route all IBiddingHubMessage to the bidding hub
    opts.Publish(x =>
    {
        x.MessagesImplementing<IBiddingHubMessage>();
        x.ToSignalR();
    });

    // Route all IOperationsHubMessage to the ops hub
    opts.Publish(x =>
    {
        x.MessagesImplementing<IOperationsHubMessage>();
        x.ToSignalR();
    });
});

// Map hub routes
app.MapHub<BiddingHub>("/hub/bidding")
   .DisableAntiforgery(); // Required — ASP.NET Core 10+ enables antiforgery by default

app.MapHub<OperationsHub>("/hub/operations")
   .DisableAntiforgery();
```

> **`.DisableAntiforgery()` is required** on hub routes in ASP.NET Core 10+. Without it, the WebSocket negotiation POST fails with 400/403. JWT-authenticated hubs (no ambient browser credentials) are safe to disable it. The participant-facing `BiddingHub` uses no cookies, so disabling is appropriate.

---

## Marker Interfaces and Message Routing

Marker interfaces live in the **Relay BC domain project**, not the API project. They express routing intent at the domain level.

```csharp
// CritterBids.Relay/Hubs/IBiddingHubMessage.cs
public interface IBiddingHubMessage
{
    /// <summary>
    /// Listing ID — used to target the "listing:{listingId}" hub group.
    /// Null for broadcast messages.
    /// </summary>
    Guid? ListingId { get; }

    /// <summary>
    /// Bidder ID — used to target the "bidder:{bidderId}" hub group.
    /// Null for listing-wide messages.
    /// </summary>
    Guid? BidderId { get; }
}

// CritterBids.Relay/Hubs/IOperationsHubMessage.cs
public interface IOperationsHubMessage { } // Broadcast to all ops staff connections
```

Message types are sealed records implementing the appropriate interface:

```csharp
public sealed record BidPlacedNotification(
    Guid ListingId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    decimal CurrentHighBid,
    DateTimeOffset OccurredAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;
    Guid? IBiddingHubMessage.BidderId => null; // Broadcast to all listing watchers
}

public sealed record BidderOutbidNotification(
    Guid ListingId,
    Guid BidderId,
    decimal NewHighBid,
    DateTimeOffset OccurredAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;
    Guid? IBiddingHubMessage.BidderId => BidderId; // Target only the outbid participant
}

public sealed record LiveBidActivityUpdate(
    Guid ListingId,
    decimal CurrentHighBid,
    int BidCount,
    string HighBidderDisplay,
    DateTimeOffset OccurredAt) : IOperationsHubMessage;
```

---

## Hub Group Management

### BiddingHub — Participant Connections

```csharp
public sealed class BiddingHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpCtx = Context.GetHttpContext();
        var bidderId = httpCtx?.Request.Query["bidderId"].ToString();
        var listingId = httpCtx?.Request.Query["listingId"].ToString();

        // Enroll in bidder-specific group (for outbid notifications)
        if (!string.IsNullOrEmpty(bidderId) && Guid.TryParse(bidderId, out var bidderGuid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"bidder:{bidderGuid}");
        }

        // Enroll in listing group (for live bid feed on this listing)
        if (!string.IsNullOrEmpty(listingId) && Guid.TryParse(listingId, out var listingGuid))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"listing:{listingGuid}");
        }

        await base.OnConnectedAsync();
    }
}
```

> **Query string identity is acceptable here.** Anonymous sessions carry no sensitive commercial data. The `BidderId` is a display identifier, not a trust anchor. For a staff-facing hub with access controls, derive identity from JWT claims (see OperationsHub below).

### OperationsHub — Staff Connections

```csharp
public sealed class OperationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Simple staff passphrase check for MVP
        var token = Context.GetHttpContext()?.Request.Query["staffToken"].ToString();
        if (token != _config["Operations:StaffToken"])
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, "ops:staff");
        await base.OnConnectedAsync();
    }
}
```

> For production beyond MVP, replace the passphrase with JWT bearer auth using `[Authorize]` on the hub and `JwtBearerEvents.OnMessageReceived` to extract the token from the query string. See the JWT pattern below.

### JWT Bearer Auth Pattern (Production)

WebSocket upgrade requests cannot carry an `Authorization` header. Extract the token from the query string:

```csharp
services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"].ToString();
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/hub/operations"))
                    context.Token = token;
                return Task.CompletedTask;
            }
        };
    });

[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OperationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Identity comes ONLY from cryptographically-verified JWT claims
        var staffId = Context.User!.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (staffId is null) { Context.Abort(); return; }

        await Groups.AddToGroupAsync(Context.ConnectionId, "ops:staff");
        await base.OnConnectedAsync();
    }
}
```

### Targeted Group Publishing

```csharp
public static class BidPlacedHandler
{
    public static IEnumerable<object> Handle(CritterBids.Contracts.Auctions.BidPlaced message)
    {
        // Push to all participants watching this listing
        yield return new BidPlacedNotification(message.ListingId, message.BidderId, message.Amount, ...)
            .ToWebSocketGroup($"listing:{message.ListingId}");

        // Push outbid notification ONLY to the previous high bidder
        if (message.PreviousHighBidderId.HasValue)
            yield return new BidderOutbidNotification(message.ListingId, message.PreviousHighBidderId.Value, message.Amount, ...)
                .ToWebSocketGroup($"bidder:{message.PreviousHighBidderId}");

        // Push to ops dashboard
        yield return new LiveBidActivityUpdate(message.ListingId, message.Amount, message.BidCount, ...);
    }
}
```

---

## Authentication Patterns

| Hub | Auth Pattern | Identity Source | Notes |
|---|---|---|---|
| `BiddingHub` | None / query string | `BidderId` from query string | Anonymous sessions, no sensitive data |
| `OperationsHub` (MVP) | Config passphrase | Query string token | Simple, adequate for demo |
| `OperationsHub` (production) | JWT Bearer | JWT claims from query string | Use `JwtBearerEvents.OnMessageReceived` |

**Never** derive group keys from query string for contexts where the key provides data access (e.g., if `listingId` restricted visibility of sensitive data). In CritterBids, listing data is public, so query string enrollment is acceptable.

---

## Marten Projection Side Effects Pipeline

One of the most powerful Wolverine + SignalR patterns: Marten projection side effects publish messages that Wolverine routes directly to the SignalR hub. Zero manual bridging code.

```csharp
// Domain Event → Marten projection → side effect message → Wolverine → SignalR → Client
public sealed class LiveListingViewProjection : SingleStreamProjection<LiveListingView>
{
    // ... Apply methods ...

    public override ValueTask RaiseSideEffects(
        IDocumentOperations ops,
        IEventSlice<LiveListingView> slice)
    {
        if (slice.Snapshot is not null)
        {
            // Wolverine sees IBiddingHubMessage and routes to BiddingHub
            slice.PublishMessage(new LiveListingStateUpdate(
                slice.Snapshot.ListingId,
                slice.Snapshot.CurrentHighBid,
                slice.Snapshot.BidCount,
                slice.Snapshot.TimeRemaining,
                DateTimeOffset.UtcNow));
        }
        return ValueTask.CompletedTask;
    }
}
```

`RaiseSideEffects` runs inside the same transaction that commits projection state — the message is outboxed atomically with the commit, so consumers only ever observe messages whose backing state has persisted.

Use this pipeline for: live bid feed updates, listing countdown timer corrections, ops dashboard live state.

---

## React Client Integration

CritterBids uses React + TypeScript, not Blazor. The client pattern is a SignalR hook:

```typescript
// hooks/useBiddingHub.ts
import * as signalR from "@microsoft/signalr";
import { useEffect, useRef, useCallback } from "react";

export function useBiddingHub(
    listingId: string,
    bidderId: string,
    onBidPlaced: (data: BidPlacedNotification) => void,
    onOutbid: (data: BidderOutbidNotification) => void,
    onExtended: (data: ExtendedBiddingNotification) => void
) {
    const connectionRef = useRef<signalR.HubConnection | null>(null);

    useEffect(() => {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl(`/hub/bidding?bidderId=${bidderId}&listingId=${listingId}`, {
                transport: signalR.HttpTransportType.WebSockets,
                skipNegotiation: true // required for cross-origin local dev
            })
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: ctx => {
                    if (ctx.previousRetryCount === 0) return 0;
                    if (ctx.previousRetryCount === 1) return 2000;
                    if (ctx.previousRetryCount === 2) return 10000;
                    return 30000;
                }
            })
            .build();

        // All Wolverine messages arrive on "ReceiveMessage"
        connection.on("ReceiveMessage", (cloudEvent) => {
            const typeName = (cloudEvent.type ?? "").split(".").pop() ?? "";
            const data = cloudEvent.data;

            switch (typeName) {
                case "BidPlacedNotification":    onBidPlaced(data);  break;
                case "BidderOutbidNotification": onOutbid(data);     break;
                case "ExtendedBiddingNotification": onExtended(data); break;
            }
        });

        connection.start().catch(console.error);
        connectionRef.current = connection;

        return () => { connection.stop(); };
    }, [listingId, bidderId]);
}
```

**Package:** Pin the SignalR client version explicitly — never use `@latest`.

```json
{
  "dependencies": {
    "@microsoft/signalr": "8.0.0"
  }
}
```

**Load order:** The SignalR package is a module import in React — no CDN script tag needed.

---

## SignalR Client Transport (Integration Testing)

The `WolverineFx.SignalR` package includes a .NET SignalR Client transport for integration testing. Critical constraint: **`WebApplicationFactory` does not work** — you must use real Kestrel.

```csharp
public class BiddingHubTestFixture : IAsyncLifetime
{
    private WebApplication? _app;
    protected IHost? ClientHost;
    private int _port;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(opts => opts.ListenLocalhost(0)); // OS picks port

        // Full app configuration...
        _app = builder.Build();
        await _app.StartAsync();

        // Discover actual port
        _port = new Uri(
            _app.Urls.Single()
                .Replace("//[::]:","//localhost:")
                .Replace("//0.0.0.0:","//localhost:")
        ).Port;

        // Boot Wolverine client host
        ClientHost = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.UseClientToSignalR(_port);
                opts.Publish(x =>
                {
                    x.MessagesImplementing<IBiddingHubMessage>();
                    x.ToSignalRWithClient(_port);
                });
            }).StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (ClientHost is not null) await ClientHost.StopAsync();
        if (_app is not null) await _app.StopAsync();
    }
}
```

Verify end-to-end delivery with tracked sessions:

```csharp
[Fact]
public async Task BidPlaced_SendsBidPlacedNotification_ToListingGroup()
{
    var tracked = await ClientHost
        .TrackActivity()
        .IncludeExternalTransports()
        .AlsoTrack(_app)
        .Timeout(TimeSpan.FromSeconds(10))
        .ExecuteAndWaitAsync(c =>
            c.PublishAsync(new CritterBids.Contracts.Auctions.BidPlaced(listingId, ...)));

    var received = tracked.Received.SingleRecord<BidPlacedNotification>();
    received.Envelope.Destination.ShouldBe(new Uri("signalr://wolverine"));
}
```

---

## Scaling Considerations

SignalR connections are instance-affine — messages published to a hub group must be delivered by the instance holding the group's connections. With multiple `CritterBids.Api` instances, a backplane is required:

```csharp
// Redis backplane (recommended)
builder.Services.AddSignalR()
    .AddStackExchangeRedis(connectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("critterbids");
    });

// Azure SignalR Service (fully managed, scales to millions of connections)
opts.UseAzureSignalR(hub => { }, service =>
{
    service.ApplicationName = "critterbids";
    service.ConnectionString = config["AzureSignalR:ConnectionString"];
});
```

For the Hetzner single-VPS conference deployment, a backplane is not needed. Design the seam; wire it when scaling.

---

## Common Pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Missing `.DisableAntiforgery()` | Hub negotiation fails 400/403 in .NET 10+ | Add to `MapHub<T>()` call |
| Inheriting `WolverineHub` when not needed | Over-engineered; client→server via HTTP is simpler | Use plain `Hub` when HTTP handles client→server |
| Calling `base.OnConnectedAsync()` wrong order | Group enrollment partially works | Always call `await base.OnConnectedAsync()` last |
| `WebApplicationFactory` for SignalR tests | Tests hang / handshake fails | Use real Kestrel on a dynamic port |
| Query string identity for sensitive data | Tenant/participant spoofing risk | Use JWT claims for any commercially sensitive group key |
| No reconnect handlers in React client | Users lose live data silently | Register `onreconnecting` / `onreconnected` with UI feedback |
| Unpinned `@microsoft/signalr` version | Silent breaks when format changes | Pin to specific version |
| Forgetting cleanup in React component | Ghost connections after unmount | Return cleanup function from `useEffect` that calls `connection.stop()` |

---

## Lessons Learned

**Return typed messages from handlers — let Wolverine route them.** Don't inject `IHubContext<T>` and call `SendAsync` manually. Return an object implementing your marker interface and let the transport handle delivery. Cleaner, testable, transport-agnostic.

**Don't hand-roll a broadcaster.** Before `opts.UseSignalR()`, CritterSupply had a 70-line `EventBroadcaster` with `ConcurrentDictionary`, cleanup logic, and thread-safety bookkeeping. Two lines replace it entirely. Never build this.

**Build with SignalR from the start.** Starting with SSE (Server-Sent Events) and migrating later cost a full rework in CritterSupply. CritterBids is real-time from day one — no SSE.

**Use role-based groups instead of iterating users.** Sending to a role group (`ops:staff`) is a single Wolverine call. Querying all staff user IDs and looping is fragile, slow, and scales poorly.

**React `useEffect` cleanup is mandatory.** The cleanup function returned from `useEffect` must call `connection.stop()`. Without it, the connection persists after component unmount, callbacks fire on stale closures, and connections accumulate.

**Marker interfaces in the domain project, not the API project.** `IBiddingHubMessage` belongs in `CritterBids.Relay` (domain), not `CritterBids.Api`. This keeps routing intent at the domain level.

---

## References

- [Wolverine SignalR Transport Docs](https://wolverinefx.net/guide/messaging/transports/signalr.html)
- [WolverineChat Sample App](https://github.com/JasperFx/wolverine/tree/main/src/Samples/WolverineChat)
- [CloudEvents Specification](https://cloudevents.io/)
- [ASP.NET Core SignalR Authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz)
- `docs/skills/wolverine-message-handlers.md`
- `docs/skills/marten-event-sourcing.md` — projection side effects
