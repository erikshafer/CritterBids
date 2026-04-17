# Projection Side Effects for Broadcast Live Views

Guide for using Marten's projection side-effect model to broadcast aggregate state changes to all watching clients through Wolverine + SignalR. This is CritterBids' primary mechanism for real-time live views (watch-an-auction, Flash Session dashboards, category tickers). Covers when to reach for the pattern, the mechanical recipe, the derived-events technique, registration, testing, and pitfalls.

> **CritterBids status:** Authored ahead of first implementation. Targeted for M3–M4 when `AuctionSnapshot` lands. Update this skill with concrete findings when the first side-effect projection ships.

---

## Table of Contents

1. [When to Use Side Effects](#when-to-use-side-effects)
2. [Mechanical Recipe](#mechanical-recipe)
3. [Canonical Example: AuctionSnapshot Live View](#canonical-example-auctionsnapshot-live-view)
4. [Derived Events Technique](#derived-events-technique)
5. [Async vs Inline Opt-In](#async-vs-inline-opt-in)
6. [Rebuild Safety](#rebuild-safety)
7. [Registration](#registration)
8. [Testing](#testing)
9. [Pitfalls](#pitfalls)
10. [Lessons Learned](#lessons-learned)
11. [References](#references)

---

## When to Use Side Effects

Projection side-effects are the Critter Stack's **broadcast primitive** for reactive views. Every aggregate state change can atomically produce an outbound message that Wolverine routes wherever we want, typically to a SignalR Group that fans out to every client watching that aggregate.

See `docs/vision/live-queries-and-streaming.md` for the architectural framing of this pattern relative to `StreamAsync`. This skill is the how-to.

✅ **Use side effects when:**

- **Many watchers need the same view.** Every viewer of an auction sees the same high bid, bidder count, and `endsAt`. One projection update produces one outbound message regardless of how many clients are subscribed. Scales with projection update rate, not viewer count.
- **The data is safe for all watchers of the aggregate.** No per-viewer filtering is needed at the publish site (or any needed filtering is safely done in the Wolverine handler that forwards to SignalR).
- **The broadcast reflects confirmed, persisted state.** Side effects run inside the daemon's transactional boundary, so the message you publish is always backed by state that committed.
- **You want derived domain events from projection state.** Threshold crossings like "auction extended" or "reserve reached" are first-class events, not UI state. `slice.AppendEvent(...)` writes them back into the log atomically. See [Derived Events Technique](#derived-events-technique).
- **Atomic cross-document operations are needed alongside the projection update.** The side-effect hook has access to `IDocumentOperations` so you can write auxiliary documents, delete related records, etc., in the same transaction.

❌ **Do NOT use side effects when:**

- **The view is per-viewer.** Secret max-bid status, "my active auctions," dashboards composing across aggregates with per-user scope — these belong on the `StreamAsync` path (see vision doc). A broadcast message cannot know who is watching.
- **Private data needs to be filtered per-viewer.** Do not try to smuggle per-viewer filtering into a broadcast payload. Either (a) broadcast only the public-safe subset and have the frontend request the private overlay through an authenticated query, or (b) use `StreamAsync` for that specific view.
- **The message is essential to correctness.** Side effects do NOT fire during projection rebuilds (by design). If a message must fire exactly once when a state transition occurs, use a command handler, an inline projection with explicit publishing, or an event forwarding subscription. See [Rebuild Safety](#rebuild-safety).
- **Sub-batch freshness matters.** The async daemon publishes side effects once per event batch per aggregate. If a scenario truly needs per-event broadcast latency (sub-second, event-by-event), measure daemon batch latency first and consider the inline opt-in, but be honest that broadcast cadence is bounded by daemon cadence.

---

## Mechanical Recipe

Every aggregate projection (`SingleStreamProjection<T, TId>` or `MultiStreamProjection<T, TId>`) can override one method:

```csharp
public override ValueTask RaiseSideEffects(
    IDocumentOperations operations,
    IEventSlice<TSnapshot> slice)
```

This hook runs at a specific point in the async daemon's aggregation flow: **after** the new events in the current batch have been applied to the snapshot, and **before** the batch commits. Everything the hook does — snapshot update, appended events, published messages, auxiliary document operations — lands in the same PostgreSQL transaction.

### The four tools

```csharp
// 1. Append a new derived event to THIS stream
slice.AppendEvent(new AuctionExtended(...));

// 2. Append an event to a DIFFERENT stream
slice.AppendEvent(otherStreamId, new SomethingElsewhere(...));

// 3. Outbox an outgoing message through Wolverine
slice.PublishMessage(new AuctionSnapshotUpdated(slice.Snapshot));

// 4. Arbitrary Marten document operations
operations.Store(new SomeOtherDocument { ... });
operations.Delete<SomeOtherDocument>(id);
```

### Critical properties

- **Atomic.** All four of these commit in one PostgreSQL transaction with the projection update itself. No split-brain. No "message sent but projection state never landed." No "projection updated but outgoing message lost."
- **Outboxed.** `PublishMessage` writes to Wolverine's outbox as part of that same commit. Delivery is durable. Process crash does not lose the message.
- **Async daemon by default.** Side effects only fire during continuous async daemon execution. Not during `Inline` projection execution unless explicitly opted in. Not during rebuilds, ever.
- **Transport-agnostic.** The published message does not know or care whether it is going to SignalR, Kafka, RabbitMQ, a local handler, or all of the above. That routing is a Wolverine configuration concern.
- **Requires Wolverine integration.** `slice.PublishMessage(...)` requires `AddMarten(...).IntegrateWithWolverine()` in bootstrap. Without it, the call does nothing useful.

### End-to-end flow

1. `AuctionSnapshot` is a Marten async projection over the auction event stream.
2. `BidPlaced` event lands on the stream.
3. The async daemon batch picks it up and applies `Evolve` (or `Apply`). `AuctionSnapshot` now reflects the new high bid and `endsAt`.
4. `RaiseSideEffects` fires. Projection calls `slice.PublishMessage(new AuctionSnapshotUpdated(slice.Snapshot))`.
5. The transaction commits. Projection document updated + outbox message enqueued, one atomic step.
6. After commit, Wolverine's outbox flushes. The Wolverine transport layer routes `AuctionSnapshotUpdated` — in CritterBids, directly to SignalR via the `IBiddingHubMessage` marker interface (see `docs/skills/wolverine-signalr.md`).
7. SignalR delivers the push to every client in `Group($"listing:{listingId}")`.

---

## Canonical Example: AuctionSnapshot Live View

The projection:

```csharp
public sealed class AuctionSnapshotProjection :
    SingleStreamProjection<AuctionSnapshot, Guid>
{
    // Create/Apply methods evolve the snapshot as events land.
    public static AuctionSnapshot Create(IEvent<AuctionStarted> @event) =>
        new AuctionSnapshot
        {
            Id = @event.StreamId,
            ListingId = @event.Data.ListingId,
            Status = AuctionStatus.Running,
            StartedAt = @event.Timestamp,
            EndsAt = @event.Data.ScheduledEnd,
            CurrentHighBid = @event.Data.StartingBid,
            BidCount = 0
        };

    public static AuctionSnapshot Apply(BidPlaced @event, AuctionSnapshot snap) =>
        snap with
        {
            CurrentHighBid = @event.Amount,
            BidCount = snap.BidCount + 1,
            HighBidderId = @event.BidderId
        };

    public static AuctionSnapshot Apply(AuctionExtended @event, AuctionSnapshot snap) =>
        snap with { EndsAt = @event.NewEndsAt };

    public static AuctionSnapshot Apply(AuctionClosed @event, AuctionSnapshot snap) =>
        snap with { Status = AuctionStatus.Closed, ClosedAt = @event.Timestamp };

    // The side-effect hook: broadcast on every update.
    public override ValueTask RaiseSideEffects(
        IDocumentOperations operations,
        IEventSlice<AuctionSnapshot> slice)
    {
        if (slice.Snapshot is null) return ValueTask.CompletedTask;

        // Broadcast the updated snapshot to every client watching this listing.
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

The broadcast message. Implements the `IBiddingHubMessage` marker interface so Wolverine routes it to SignalR — see `docs/skills/wolverine-signalr.md` for the marker-interface pattern:

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
    Guid? IBiddingHubMessage.BidderId => null; // Broadcast to all listing watchers
}
```

That's it. No Wolverine handler needed to bridge projection → hub. Marten publishes `AuctionSnapshotUpdated`, Wolverine sees the `IBiddingHubMessage` marker, the SignalR transport delivers to `Group($"listing:{ListingId}")`.

### Snapshot-first bootstrapping

The side-effect path pushes *updates*. New clients joining mid-auction still need the current state. Standard pattern: the client hits a plain HTTP endpoint for the current snapshot on connect, *then* subscribes to the hub for updates.

```csharp
// CritterBids.Auctions/Api/GetAuctionSnapshotEndpoint.cs
[WolverineGet("/api/auctions/{listingId:guid}/snapshot")]
public static Task<AuctionSnapshot?> Get(Guid listingId, IQuerySession session) =>
    session.LoadAsync<AuctionSnapshot>(listingId);
```

The React client loads this once on mount, then lets subsequent `AuctionSnapshotUpdated` pushes update local state. See `docs/skills/wolverine-signalr.md` for the hub connection pattern.

---

## Derived Events Technique

The projection has the new snapshot available *before* it commits. It can raise new first-class domain events based on threshold crossings by calling `slice.AppendEvent(...)`. These become real events in the stream, not just UI payloads.

**Example: anti-snipe auction extension.** When a bid lands within the last N seconds, extend the auction.

```csharp
public override ValueTask RaiseSideEffects(
    IDocumentOperations operations,
    IEventSlice<AuctionSnapshot> slice)
{
    if (slice.Snapshot is null) return ValueTask.CompletedTask;

    // Look at the events just applied to decide whether to extend.
    var latestBid = slice.Events()
        .OfType<IEvent<BidPlaced>>()
        .LastOrDefault();

    if (latestBid is not null)
    {
        var timeRemaining = slice.Snapshot.EndsAt - latestBid.Timestamp;
        if (timeRemaining <= TimeSpan.FromSeconds(10) &&
            slice.Snapshot.Status == AuctionStatus.Running)
        {
            var newEndsAt = latestBid.Timestamp.AddSeconds(10);
            slice.AppendEvent(new AuctionExtended(newEndsAt));
        }
    }

    // Always broadcast the current snapshot
    slice.PublishMessage(new AuctionSnapshotUpdated(/* ... */));

    return ValueTask.CompletedTask;
}
```

When the projection commits, the `AuctionExtended` event is part of the auction's event stream. On the next daemon batch, the projection's `Apply(AuctionExtended, AuctionSnapshot)` method runs and the new `EndsAt` propagates into the snapshot, which triggers another `AuctionSnapshotUpdated` broadcast.

**Why this is worth naming.** In a command-handler-centric model, anti-snipe logic lives in the `PlaceBid` handler. It has to load the current auction state, decide whether to extend, and append both `BidPlaced` and `AuctionExtended` in one save. That works but puts the extension policy in the command path.

In the projection, the policy lives where the state already is. The command handler stays small ("append `BidPlaced` and save"). The projection is authoritative for "given an updated snapshot, should a new event be raised?" This often reads more naturally for rules that look at aggregate *state* rather than command *input*.

**Caveat.** `slice.AppendEvent` writes to the event stream. Any consumers of that stream (other projections, subscriptions, Wolverine event handlers) will see the appended event on the next batch. Be deliberate about what you raise: these are real domain events, not debug notifications.

**Anti-snipe alternative.** You can also model anti-snipe as a process manager or saga that reacts to `BidPlaced` and appends `AuctionExtended` when appropriate. That's fine too, and sometimes preferable if the policy is complex. The projection-side approach wins when the decision is a simple function of the new snapshot state.

---

## Async vs Inline Opt-In

By default, side effects run only during async daemon execution. This is the default CritterBids should use.

**Why async is the default:**
- Survives the rebuild-safety property (rebuilds do not fire side effects; see below).
- Publishing cadence is bounded by daemon batch rate, which is free load-shedding.
- Projection commit is decoupled from the command that triggered the event, so command latency is not affected by fan-out work.

**When to opt into inline:**

```csharp
opts.Events.EnableSideEffectsOnInlineProjections = true;
```

Use this only when:
- The broadcast must reflect state from the *same transaction* as the command that triggered the event (not "eventually").
- Missing a broadcast during temporary daemon unavailability is worse than blocking the command on the broadcast path.

An example where inline makes sense: an admin override that closes an auction immediately and needs all watchers notified before the admin's response returns. An example where async is better: every regular bid-placed broadcast, where eventual delivery within a daemon batch is fine.

Default to async. Make the inline opt-in a deliberate, documented decision.

---

## Rebuild Safety

**Side effects do not fire during projection rebuilds, by design.**

This is a safety property, not a limitation. A rebuild replays every event from the beginning of the stream. Without this guard, rebuilding `AuctionSnapshot` would re-broadcast every historical `AuctionSnapshotUpdated` to clients — potentially tens of thousands of duplicate messages, and worse, firing downstream side effects that may no longer be valid.

**Two corollaries worth internalizing:**

1. **Never use side effects for anything essential to correctness.** If a message MUST fire when state X is reached, and MUST fire even after a rebuild, you need a different mechanism:
   - A command handler that publishes the message directly when the event is appended.
   - A Wolverine event subscription that fires per-event (not projection-driven).
   - An inline projection with explicit publishing inside the Apply method.

   Side effects are for **broadcast enrichment**, not for **domain workflow steps**.

2. **Any reactive surface should be safe to rebuild.** When you build a new side-effect-emitting projection, ask: "What happens if this projection is rebuilt tomorrow?" The answer should be "nothing visible changes from the perspective of downstream consumers, because side effects are suppressed during the rebuild." If the answer is "we lose real business state," the logic is in the wrong place.

**Practical corollary.** `AuctionExtended` raised via `slice.AppendEvent` during live processing *does* persist and become part of the event stream. During a rebuild, those events are already in the stream — the rebuild replays them and `Apply(AuctionExtended, ...)` runs as normal. The rebuild doesn't re-raise them (side effects don't fire), but it doesn't need to: they're already there. This is the reason `AppendEvent` plus `PublishMessage` is the stable idiom — append the event to the log (permanent), then publish the message about it (ephemeral notification).

---

## Registration

Two registrations required for the pattern to work end-to-end.

### Marten projection registration (per BC)

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(builder.Configuration.GetConnectionString("marten"));

    // Register the projection as Async.
    opts.Projections.Add<AuctionSnapshotProjection>(ProjectionLifecycle.Async);

    // Marten automatically applies transactions to sessions
    opts.Policies.AutoApplyTransactions();
})
.IntegrateWithWolverine() // REQUIRED for slice.PublishMessage to route through outbox
.AddAsyncDaemon(DaemonMode.HotCold); // HotCold for HA, Solo for single-instance dev
```

**The `IntegrateWithWolverine()` call is load-bearing.** Without it, `slice.PublishMessage(...)` does nothing useful. With it, messages go through Wolverine's outbox in the same transaction as the projection commit.

### SignalR routing (in Relay BC)

The Relay BC owns hub registration and outbound message routing. See `docs/skills/wolverine-signalr.md` for the full pattern. The key bit relevant here:

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseSignalR();

    // Route every IBiddingHubMessage to the bidding hub.
    // AuctionSnapshotUpdated matches this because it implements IBiddingHubMessage.
    opts.Publish(x =>
    {
        x.MessagesImplementing<IBiddingHubMessage>();
        x.ToSignalR();
    });
});
```

The projection does not know about SignalR. It publishes a message. The Relay BC's routing rules send anything implementing `IBiddingHubMessage` to the bidding hub. This keeps the Auctions BC transport-agnostic.

---

## Testing

Three layers worth testing separately.

### 1. Unit test the projection's Apply logic

The `Apply`/`Evolve`/`Create` methods are pure functions. Test them without the daemon.

```csharp
[Fact]
public void Apply_BidPlaced_updates_high_bid_and_increments_count()
{
    var snap = new AuctionSnapshot
    {
        Id = Guid.NewGuid(),
        Status = AuctionStatus.Running,
        CurrentHighBid = 10m,
        BidCount = 2
    };

    var bid = new BidPlaced(
        ListingId: snap.ListingId,
        BidderId: Guid.NewGuid(),
        Amount: 15m);

    var updated = AuctionSnapshotProjection.Apply(bid, snap);

    updated.CurrentHighBid.ShouldBe(15m);
    updated.BidCount.ShouldBe(3);
    updated.HighBidderId.ShouldBe(bid.BidderId);
}
```

Fast, deterministic, no infrastructure. Covers the majority of projection logic.

### 2. Integration test the full side-effect flow

Verify the daemon runs the projection, commits, and publishes the expected message. Pattern uses Wolverine's tracked session on the host.

```csharp
[Fact]
public async Task BidPlaced_triggers_AuctionSnapshotUpdated_broadcast()
{
    using var host = await Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddMarten(opts =>
                {
                    opts.Connection(_postgresFixture.ConnectionString);
                    opts.DatabaseSchemaName = "auctions_test";
                    opts.Projections.Add<AuctionSnapshotProjection>(ProjectionLifecycle.Async);
                })
                .IntegrateWithWolverine()
                .AddAsyncDaemon(DaemonMode.Solo); // Solo for tests, starts faster
        })
        .UseWolverine()
        .StartAsync();

    var listingId = Guid.NewGuid();
    var auctionStreamId = Guid.NewGuid();

    // Append events and wait for both outgoing messages AND projection catch-up.
    var tracked = await host.SaveInMartenAndWaitForOutgoingMessagesAsync(session =>
    {
        session.Events.StartStream(
            auctionStreamId,
            new AuctionStarted(listingId, DateTimeOffset.UtcNow.AddMinutes(5), 10m),
            new BidPlaced(listingId, Guid.NewGuid(), 15m));
    }, 10_000);

    // Wait for projection to be current
    var store = host.Services.GetRequiredService<IDocumentStore>();
    await store.WaitForNonStaleProjectionDataAsync(5.Seconds());

    // Assert the broadcast fired
    var published = tracked.Sent.SingleMessage<AuctionSnapshotUpdated>();
    published.ListingId.ShouldBe(listingId);
    published.CurrentHighBid.ShouldBe(15m);
    published.BidCount.ShouldBe(1);

    // And the snapshot is queryable
    await using var query = store.QuerySession();
    var snap = await query.LoadAsync<AuctionSnapshot>(auctionStreamId);
    snap!.CurrentHighBid.ShouldBe(15m);
}
```

**Key helpers:**
- `SaveInMartenAndWaitForOutgoingMessagesAsync` — commits Marten session and waits for the outbox to flush. Returns `ITrackedSession`.
- `WaitForNonStaleProjectionDataAsync` — blocks until the async daemon catches up to the latest event. Essential for asserting projection state in tests.
- `tracked.Sent.SingleMessage<T>()` — finds the one published message of type `T` in the tracked session. Fails the test if there are zero or multiple.

See `docs/skills/critter-stack-testing-patterns.md` for shared fixture patterns (Testcontainers PostgreSQL, schema-per-test isolation).

### 3. End-to-end test through the HTTP surface

Combines tracked sessions with real HTTP calls via Alba or similar. Covers the command handler → projection → side effect → SignalR routing chain.

```csharp
[Fact]
public async Task PlaceBid_endpoint_produces_broadcast_to_bidding_hub()
{
    var (tracked, _) = await TrackedHttpCall(x =>
    {
        x.Post.Json(new PlaceBidRequest(listingId, bidderId, 50m))
            .ToUrl("/api/auctions/bid");
        x.StatusCodeShouldBeOk();
    });

    var broadcast = tracked.Sent.SingleMessage<AuctionSnapshotUpdated>();
    broadcast.ListingId.ShouldBe(listingId);
    broadcast.CurrentHighBid.ShouldBe(50m);
}
```

The `TrackedHttpCall` helper is the CritterSupply/CritterBids convention for wrapping Alba scenarios in Wolverine's `ExecuteAndWaitAsync`. Pattern is documented in `docs/skills/critter-stack-testing-patterns.md`.

### Testing derived events

If the projection also appends derived events via `slice.AppendEvent`, assert on the event stream itself:

```csharp
// After the tracked session commits:
await using var session = store.LightweightSession();
var events = await session.Events.FetchStreamAsync(auctionStreamId);

events.OfType<IEvent<AuctionExtended>>()
    .ShouldContain(e => e.Data.NewEndsAt > original.EndsAt);
```

This is both useful testing and a nice illustration of why derived events belong in the log: they're queryable like any other event.

---

## Pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Missing `IntegrateWithWolverine()` | `slice.PublishMessage` called but no outbound message ever delivered | Add `.IntegrateWithWolverine()` after `AddMarten(...)` in bootstrap |
| Testing with `ProjectionLifecycle.Inline` and not opting in | Side effect doesn't fire in test | Either register projection as `Async` + use `AddAsyncDaemon(DaemonMode.Solo)`, OR set `opts.Events.EnableSideEffectsOnInlineProjections = true` |
| Relying on side effect for a correctness-essential message | Message missing after projection rebuild; hard-to-diagnose bug weeks later | Use a command handler or Wolverine event subscription for correctness-essential publishing. Side effects are for broadcast enrichment only |
| Leaking per-viewer data in broadcast payload | Max-bid amounts or owner-only fields visible to all watchers | Either broadcast only the public-safe subset and serve private data via `StreamAsync` / authenticated query, or strip the sensitive fields in the Wolverine handler before SignalR fan-out |
| Multiple `PublishMessage` calls per slice | N redundant broadcasts per projection update | Publish once at the end of `RaiseSideEffects` with the final snapshot. Let downstream conflation happen at the transport if needed |
| Forgetting the `IBiddingHubMessage` marker | Broadcast outboxed but never reaches SignalR | Message type must implement the routing marker interface (`IBiddingHubMessage` or `IOperationsHubMessage`). See `wolverine-signalr.md` |
| Using `IHubContext` directly from a Wolverine handler that consumes the side-effect message | Works but duplicates what Wolverine transport does for free | Return a typed message from the projection side effect; let Wolverine's SignalR transport do the fan-out |
| Assertion against `tracked.Sent` before daemon catches up | Flaky test — message sometimes present, sometimes not | Use `SaveInMartenAndWaitForOutgoingMessagesAsync` AND `WaitForNonStaleProjectionDataAsync` together |
| `ValueTask` not returned from async work in hook | Compile fails; or silent swallowed exception if patched | `RaiseSideEffects` returns `ValueTask`. If no async work, `return ValueTask.CompletedTask;`. If async data access, `async`-ify the method |
| Side effect hook holds DB cursors or leaks operations | Transaction bloat, connection pool pressure | Keep the hook short. The `IDocumentOperations` parameter is for simple Store/Delete operations; long-running data access belongs in enrichment, not side effects |

---

## Lessons Learned

*This section is intentionally empty until we ship the first side-effect projection in CritterBids. Update with concrete findings from M3–M4 work.*

Expected topics once we have real experience:
- Batch latency characteristics under Flash auction load
- How conflation decisions actually play out in practice
- Whether derived events via `slice.AppendEvent` prove more useful than process manager-driven extension logic
- Testing ergonomics and patterns that emerged
- How the rebuild-safety property holds up during real schema changes

---

## References

- [Marten: Side Effects](https://martendb.io/events/projections/side-effects.html)
- [Marten: Aggregate Projections](https://martendb.io/events/projections/aggregate-projections.html)
- [Marten: Asynchronous Projections (Async Daemon)](https://martendb.io/events/projections/async-daemon.html)
- [Wolverine: Marten Integration](https://wolverinefx.net/guide/durability/marten/)
- [Wolverine: Integration Testing](https://wolverinefx.net/guide/testing.html)
- `docs/vision/live-queries-and-streaming.md` — architectural framing of the side-effect model vs `StreamAsync`
- `docs/skills/wolverine-signalr.md` — hub design, marker interfaces, group management, React client
- `docs/skills/marten-event-sourcing.md` — projection basics, event stream design
- `docs/skills/wolverine-message-handlers.md` — command handler patterns
- `docs/skills/critter-stack-testing-patterns.md` — `TrackedHttpCall`, fixture patterns, Testcontainers setup
- `docs/skills/dynamic-consistency-boundary.md` — when DCB changes how projections compose
