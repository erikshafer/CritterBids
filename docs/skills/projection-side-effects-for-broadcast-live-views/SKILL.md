---
name: projection-side-effects-for-broadcast-live-views
description: "Projection side effects in CritterBids: broadcast live views, derived events, rebuild safety, and SignalR routing. Use when a Marten projection should push shared live updates."
cluster: marten
tags: [marten, projections, side-effects, signalr, live-views]
---

# Projection Side Effects for Broadcast Live Views

> CritterBids pattern for using Marten projection side effects as the broadcast primitive for live auction views.
> Generic `RaiseSideEffects` mechanics live in ai-skills `marten-projections-raise-side-effects`; **this skill documents only the CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- A Marten projection update should broadcast the same public state to all clients watching a listing/session/dashboard.
- Designing `AuctionSnapshotUpdated`, operations-dashboard pushes, category tickers, or Flash Session live views.
- Deciding whether a rule belongs in projection side effects, a command handler, a saga, or a SignalR handler.
- Testing projection-driven broadcasts and derived events.

Do NOT use this skill for: generic projection authoring (see `marten-projections`), SignalR hub/group mechanics (see `wolverine-signalr`), or correctness-essential domain workflows (use command handlers, event subscriptions, or sagas).

## Read upstream first

Generic Marten side-effect mechanics are covered upstream. Read this ai-skill (license required; install via `npx skills add`) before this skill — it covers ~80% of the mechanics:

1. `marten-projections-raise-side-effects` — `RaiseSideEffects`, `PublishMessage`, `AppendEvent`, auxiliary document operations, testing basics.

That covers ~80% of the topic. This skill picks up at CritterBids' broadcast/live-view decisions.

## CritterBids broadcast posture

Projection side effects are CritterBids' **broadcast primitive** for reactive views. One aggregate-state update produces one outbound message, then Wolverine routes that message to SignalR groups.

Use side effects when:

- Many watchers need the same public view: high bid, bid count, current status, `endsAt`.
- The payload is safe for every watcher of that aggregate.
- The broadcast should reflect confirmed persisted state.
- A simple derived domain event should be appended from newly projected state.
- Small auxiliary document writes must be atomic with the projection update.

Do not use side effects when:

- The view is per-viewer (`my bids`, secret max-bid state, owner-only dashboards).
- Private data needs per-viewer filtering.
- The message is essential to correctness or must replay during rebuild.
- External HTTP/IO work would run inside the daemon transaction.

## Canonical flow — AuctionSnapshot live view

1. `AuctionSnapshot` is an async Marten projection over the auction/listing event stream.
2. `BidPlaced` lands.
3. The async daemon applies the batch; snapshot now has high bid, bid count, and current end time.
4. `RaiseSideEffects` publishes `AuctionSnapshotUpdated`.
5. Marten commits snapshot + Wolverine outbox message in one transaction.
6. Wolverine routes the message to SignalR through the `IBiddingHubMessage` marker.
7. Clients in `Group($"listing:{listingId}")` receive the update.

```csharp
public sealed class AuctionSnapshotProjection : SingleStreamProjection<AuctionSnapshot, Guid>
{
    public override ValueTask RaiseSideEffects(
        IDocumentOperations operations,
        IEventSlice<AuctionSnapshot> slice)
    {
        if (slice.Snapshot is null) return ValueTask.CompletedTask;

        slice.PublishMessage(new AuctionSnapshotUpdated(
            slice.Snapshot.ListingId,
            slice.Snapshot.Status,
            slice.Snapshot.CurrentHighBid,
            slice.Snapshot.BidCount,
            slice.Snapshot.HighBidderId,
            slice.Snapshot.EndsAt,
            DateTimeOffset.UtcNow));

        return ValueTask.CompletedTask;
    }
}
```

Message shape:

```csharp
public sealed record AuctionSnapshotUpdated(
    Guid ListingId,
    AuctionStatus Status,
    decimal CurrentHighBid,
    int BidCount,
    Guid? HighBidderId,
    DateTimeOffset EndsAt,
    DateTimeOffset OccurredAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;
    Guid? IBiddingHubMessage.BidderId => null;
}
```

No `IHubContext` is needed in the projection or a bridge handler. The projection publishes a message; Wolverine's SignalR transport handles fan-out.

### Snapshot-first bootstrapping

Side effects push updates only. A client joining mid-auction first loads the current snapshot over HTTP, then subscribes for pushes:

```csharp
[WolverineGet("/api/auctions/{listingId:guid}/snapshot")]
[AllowAnonymous]
public static Task<AuctionSnapshot?> Get(Guid listingId, IQuerySession session) =>
    session.LoadAsync<AuctionSnapshot>(listingId);
```

## Derived events technique

A projection can append a first-class domain event when the newly projected state crosses a threshold. For anti-snipe bidding, the projection can append `AuctionExtended` after seeing a `BidPlaced` near the current end time:

```csharp
public override ValueTask RaiseSideEffects(
    IDocumentOperations operations,
    IEventSlice<AuctionSnapshot> slice)
{
    if (slice.Snapshot is null) return ValueTask.CompletedTask;

    var latestBid = slice.Events()
        .OfType<IEvent<BidPlaced>>()
        .LastOrDefault();

    if (latestBid is not null &&
        slice.Snapshot.Status == AuctionStatus.Running &&
        slice.Snapshot.EndsAt - latestBid.Timestamp <= TimeSpan.FromSeconds(10))
    {
        slice.AppendEvent(new AuctionExtended(latestBid.Timestamp.AddSeconds(10)));
    }

    slice.PublishMessage(new AuctionSnapshotUpdated(/* final public snapshot */));
    return ValueTask.CompletedTask;
}
```

Use this only when the derived event is a simple function of the updated snapshot. If the policy is complex, long-running, or needs external inputs, move it to a command handler, process manager, or event subscription.

## Auxiliary documents

`IDocumentOperations` can store small rollups atomically with the projection update and broadcast:

```csharp
var rollup = await operations.LoadAsync<DailyAuctionRollup>(day)
             ?? new DailyAuctionRollup { Id = day };
rollup.ClosedCount++;
rollup.SoldRevenue += slice.Snapshot.HammerPrice ?? 0m;
operations.Store(rollup);
```

Keep this for small convenience rollups. If the rollup has complex grouping, rebuild requirements, or independent correctness value, promote it to its own projection.

## Async vs inline posture

Default to async side effects:

- Command latency is not coupled to broadcast fan-out.
- Daemon batch rate naturally load-sheds during flash auction bursts.
- Derived events via `slice.AppendEvent` are supported.

Only enable inline side effects for a documented same-transaction broadcast requirement:

```csharp
opts.Events.EnableSideEffectsOnInlineProjections = true;
```

Inline side effects cannot call `slice.AppendEvent`; derived events must come from another mechanism on that path.

## Rebuild safety rule

Side effects do **not** fire during projection rebuilds. Treat this as a design invariant:

- Broadcast messages are ephemeral notifications.
- Domain facts belong in the event log.
- Correctness-essential work belongs in command handlers, event subscriptions, or sagas.

If rebuilding `AuctionSnapshot` would need to send real business messages to make the system correct, the logic is in the wrong place. `slice.AppendEvent` is safe because the derived event is persisted during live processing and replayed as a normal event during rebuild; the rebuild does not re-raise it.

## Registration shape in CritterBids

CritterBids uses one primary Marten store in `Program.cs`. BC modules contribute projection registration through `ConfigureMarten`; they do not call `AddMarten()` themselves.

```csharp
// AddAuctionsModule()
services.ConfigureMarten(opts =>
{
    opts.Projections.Add<AuctionSnapshotProjection>(ProjectionLifecycle.Async);
});
```

`Program.cs` owns `.IntegrateWithWolverine()` and the async daemon setup. `UseWolverine()` owns `AutoApplyTransactions()` globally. Do not copy older examples that put `AddMarten()` or `AutoApplyTransactions()` inside a BC module.

SignalR routing belongs to Relay/Wolverine configuration:

```csharp
opts.Publish(x =>
{
    x.MessagesImplementing<IBiddingHubMessage>();
    x.ToSignalR();
});
```

The Auctions projection remains transport-agnostic.

## Testing posture

Test four layers separately:

1. **Projection `Apply`/`Create` logic** without infrastructure.
2. **`RaiseSideEffects` branching** with a minimal `IEventSlice<T>` test double for fast message-shape assertions.
3. **Full daemon/outbox flow** with Testcontainers, async daemon, `WaitForNonStaleProjectionDataAsync`, and Wolverine tracked sessions.
4. **HTTP/SignalR surface** through Alba/tracked helpers when verifying end-to-end routing.

Always pair outgoing-message assertions with projection catch-up waits; otherwise async daemon timing makes tests flaky.

## Common pitfalls

- **Missing `IntegrateWithWolverine()`.** `slice.PublishMessage` requires store integration with Wolverine's outbox.
- **Leaking private data.** Broadcast payloads are public to all watchers in the group. Serve private overlays through authenticated queries/streams.
- **Using side effects for correctness.** Rebuilds suppress side effects; correctness logic must live elsewhere.
- **Forgetting the SignalR marker interface.** `AuctionSnapshotUpdated` must implement the relevant hub marker (`IBiddingHubMessage`, `IOperationsHubMessage`) or routing will not fan out.
- **Publishing N times per slice.** Publish once with the final snapshot; don't spam clients with intermediate events from the same batch.
- **Blocking the daemon with external IO.** Publish a Wolverine message for external calls; keep the hook short.
- **Infinite `AppendEvent` loops.** A side-effect-appended event is seen by the same projection later. Include a terminating predicate.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-projections-raise-side-effects` — full `RaiseSideEffects` API and mechanics.

**Prerequisites:**

- `marten-projections` — projection selection and read-model shape.
- `wolverine-signalr` — hub marker interfaces, groups, and client connection posture.
- `marten-event-sourcing` — single-store Marten bootstrap and async daemon posture.

**Downstream:**

- `critter-stack-testing-patterns` — tracked sessions and async projection testing.
- `integration-messaging` — routing side-effect messages beyond SignalR.

**External:**

- [`docs/vision/live-queries-and-streaming.md`](../../vision/live-queries-and-streaming.md) — live-view architecture.
- ADR 011 (All-Marten Pivot) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Canonical Bootstrap Sequence.
