# Live Queries and Streaming Handlers

Forward-looking notes on how CritterBids should deliver reactive data to the
frontend using the Critter Stack. Covers two complementary mechanisms:
Marten's **projection side-effects model** (available today, push-shaped,
broadcast-friendly) and Wolverine's upcoming **`StreamAsync`** primitive paired
with **Marten 9 live queries** (future, pull-shaped, per-viewer). Written
during early M2 work and revised in M2 after a conversation with Jeremy that
clarified the side-effect model's role as a live-query engine in its own right.

This document is a reference for future architectural decisions and will
inform ADRs once the upstream APIs stabilize. A companion skill file will
cover the how-to for the side-effects-for-broadcast-live-views pattern
separately.

## Background

### The two reactive shapes

The Critter Stack provides two distinct paths for pushing state to the
frontend, with different ergonomics, different scale profiles, and different
availability timelines. They are complementary, not competing.

**Projection side-effects** give us a push-shaped broadcast primitive. An
async projection updates its snapshot when new events land, and its
`RaiseSideEffects` hook can outbox a message through Wolverine atomically
with the projection update. That message is then routed wherever we want,
typically to a SignalR Group that fans out to every client watching the
aggregate. One projection update produces one outgoing message regardless of
how many viewers are watching. Available today in Marten 7.27+. This is the
right tool for the canonical watch-an-auction case.

**`StreamAsync` + Marten live queries** give us a pull-shaped per-viewer
primitive. A Wolverine handler returns `IAsyncEnumerable<TResponse>` and each
caller gets their own enumeration session with their own DI scope, auth
context, cancellation token, and completion semantics. This is the right
tool for viewer-specific derived views, cross-aggregate compositions, and
ad-hoc reactive queries where the result set depends on who is asking.
Neither `StreamAsync` nor Marten 9 live queries have shipped yet, so this
path is future work.

Both paths read from the same event log, both use Wolverine handlers
somewhere in the flow, and both land reactive data on the client. The
difference is whether the server *broadcasts* state changes to a population
of watchers or *yields* computed results to a specific caller.

### Projection side-effects, mechanically

Every aggregate projection (`SingleStreamProjection<T, TId>` or
`MultiStreamProjection<T, TId>`) can override:

```csharp
public override ValueTask RaiseSideEffects(
    IDocumentOperations operations,
    IEventSlice<AuctionSnapshot> slice)
```

This hook fires at a specific point in the async daemon's aggregation flow:
after the new events in the current batch have been applied to the snapshot,
but before the batch commits. Inside the hook we have four tools:

```csharp
// 1. Append a new event to THIS stream (derived domain event)
slice.AppendEvent(new AuctionExtended(...));

// 2. Append to a DIFFERENT stream
slice.AppendEvent(otherStreamId, new SomethingElsewhere(...));

// 3. Outbox an outgoing message through Wolverine
slice.PublishMessage(new AuctionSnapshotUpdated(slice.Snapshot));

// 4. Arbitrary Marten document operations
operations.Store(new SomeOtherDocument { ... });
```

Critical properties of this model:

- **Atomic.** All four of these commit in the same PostgreSQL transaction as
  the projection update. No split-brain possibilities, no "message sent but
  projection wasn't updated."
- **Outboxed.** `PublishMessage` writes to Wolverine's outbox as part of that
  same commit. Delivery is durable. Process crash does not lose the message.
- **Only runs during continuous async daemon execution by default.** Rebuilds
  do *not* fire side effects, which is a safety property: we do not want to
  re-broadcast ten thousand `AuctionSnapshotUpdated` messages when rebuilding
  a projection from scratch. We can opt into inline side-effect execution
  with `opts.Events.EnableSideEffectsOnInlineProjections = true` when the
  low-latency path matters more than daemon batching.
- **Transport-agnostic.** The published message does not know whether it is
  going to SignalR, Kafka, a queue, or another handler. That routing is a
  Wolverine configuration concern.

Worked end-to-end for CritterBids:

1. `AuctionSnapshot` is a Marten async projection over the auction event
   stream.
2. `BidPlaced` event lands on the stream.
3. Async daemon batch picks it up, applies the event, `AuctionSnapshot` now
   reflects the new high bid and `endsAt`.
4. `RaiseSideEffects` fires. Projection calls
   `slice.PublishMessage(new AuctionSnapshotUpdated(slice.Snapshot))`.
5. Transaction commits. Projection document is updated and message is
   outboxed in one atomic step.
6. After commit, Wolverine's outbox flushes. A Wolverine message handler for
   `AuctionSnapshotUpdated` receives the message, injects
   `IHubContext<AuctionHub>`, and calls
   `Clients.Group($"listing-{msg.ListingId}").SendAsync(...)`.
7. Every browser watching that listing receives the push.

That is a live query. Every state change on the aggregate reaches every
subscriber, with transactional durability guarantees, using primitives that
ship today.

### StreamAsync

The [Wolverine PR](https://github.com/JasperFx/wolverine/pull/2525) adds a
new method to the `ICommandBus` interface (and therefore to `IMessageBus`):

```csharp
IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
    object message,
    CancellationToken cancellation = default);

IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
    object message,
    DeliveryOptions options,
    CancellationToken cancellation = default);
```

Implementation-wise, this is local-only and synchronous in the mediator sense.
`MessageBus.StreamAsync<TResponse>(...)` resolves the handler through
`Runtime.FindInvoker(message.GetType()).StreamAsync<TResponse>(...)`, the same
invoker pipeline used by `InvokeAsync<T>`. It does not participate in the
outbox, does not route through transports, and does not persist the envelope.
The XML doc is explicit: "Only supported for locally-handled messages."

That constraint is a feature. It positions streaming as a first-class pattern
for *per-caller reactive queries backed by local handler logic*, not as a
distributed messaging primitive. Distributed cases still belong to
`SendAsync`/`PublishAsync`, and broadcast cases belong to the side-effect
model described above.

The handler shape this unlocks is the natural C# idiom:

```csharp
public async IAsyncEnumerable<AuctionSnapshot> Handle(
    WatchAuction query,
    IDocumentSession session,
    IMartenLiveQueries liveQueries,
    [EnumeratorCancellation] CancellationToken ct)
{
    var snapshot = await session.LoadAsync<AuctionSnapshot>(query.ListingId, ct);
    if (snapshot is not null) yield return snapshot;

    await foreach (var update in liveQueries
        .Watch<AuctionSnapshot>(query.ListingId)
        .WithCancellation(ct))
    {
        yield return update;
    }
}
```

Caller side, any surface that consumes `IAsyncEnumerable<T>` becomes trivially
reactive:

```csharp
await foreach (var snap in bus.StreamAsync<AuctionSnapshot>(
    new WatchAuction(id), ct))
{
    // push to SignalR caller, yield to gRPC server-stream,
    // serialize as NDJSON, etc.
}
```

The bus produces a typed stream; the transport layer decides how to deliver
it. That decoupling is what makes `StreamAsync` a general-purpose primitive
rather than a gRPC-shaped one.

### Marten 9 live queries

Marten's existing subscription machinery is projection-oriented and designed
for server-side background work. A "live query" in the Marten 9 sense is a
client-facing API: an `IAsyncEnumerable<T>` that yields the current projected
state and subsequent updates as the underlying stream advances. Under the hood
this is expected to be LISTEN/NOTIFY plus the existing async daemon plumbing,
exposed as a first-class API.

Once both features exist, `StreamAsync` composition is essentially free. The
Wolverine handler is the place where CritterBids:

- chooses what to project (which aggregate, which slice of state),
- enforces per-viewer filtering (privacy, permissions, tenancy),
- decides shape (full snapshot, delta, or both),
- controls lifetime (`yield break` on terminal events).

Handler logic stays Wolverine-shaped with DI, middleware, logging, tracing,
and tenancy. The data path stays Marten-shaped. Neither concern leaks into
HTTP, gRPC, or SignalR endpoints.

## Architectural Mapping to Industry References

The side-effect model is not novel. It is the Critter Stack's version of a
pattern that production auction platforms have been using for years. The
cleanest public analog is Artsy's Causality system.

### Artsy's Causality maps 1:1 to the side-effect path

| Artsy (Scala/Akka) | Critter Stack equivalent |
|---|---|
| Akka Persistence actor | Marten async projection |
| Akka Distributed Pub/Sub broadcast | `slice.PublishMessage(...)` via Wolverine outbox |
| WebSocket outflow merge | Wolverine handler pushes to `IHubContext` Group |
| Atomic Store strict consistency | DCB + Marten's natural stream ordering |
| Cluster node pub/sub routing | SignalR Redis backplane (when we scale out) |

This mapping is load-bearing. Every decision Artsy documented in their
engineering post (append-only state, strict bid ordering, cluster fan-out,
WebSocket outflow) has a Critter Stack primitive that provides the same
guarantee. We do not have to build these from scratch like Artsy did in
2016. That is the practical payoff for picking the stack we picked.

### Whatnot's Live Service is a different shape

Whatnot models each auction as a live GenServer process holding in-memory
state, coordinated via Horde across a BEAM cluster. This is not the same
architectural shape as event sourcing with projections. The BEAM model puts
the canonical state in memory in a process; the Marten model puts it in
PostgreSQL as an event stream with projected snapshots.

The *outcomes* converge (live broadcast to N subscribers watching one
auction) but the *mechanisms* differ. Worth knowing because the Whatnot
engineering posts are excellent references for load-shedding and scale
techniques, but they are not a direct blueprint for the Marten path. Our
equivalent of "auction as a GenServer" is "auction as an event stream with
an attached async projection," and the fan-out story is Wolverine + SignalR
rather than Phoenix Channels + Horde.

### The side-effect model is an explicit design choice, not a workaround

Worth naming for the ADR: picking the side-effect path puts us in the same
architectural family as Artsy, which we already identified as the closest
public reference for the auction live-view problem. Going with `StreamAsync`
as the primary reactive path instead would be a different architectural
choice, closer to reactive-query systems (Hasura subscriptions, Firestore
real-time listeners, GraphQL subscriptions over WebSocket) than to the
event-sourced live-auction references.

Neither choice is wrong. Side-effects align better with the precedent.

## Use Cases in CritterBids

Candidate reactive surfaces, split by which mechanism fits them.

### Broadcast cases: projection side-effects + SignalR Group

These are the cases where every viewer of a given aggregate sees essentially
the same data and the watch population is expected to be larger than one.

**Live auction detail view.** `AuctionSnapshot` projection publishes
`AuctionSnapshotUpdated` on every update. Wolverine handler pushes to
`Group($"listing-{listingId}")`. All viewers of that listing get the new
high bid, bidder count, status, and `endsAt` in lockstep. This is the
canonical case and should be the first reactive surface built.

**Flash Session dashboard broadcast.** `FlashSessionSnapshot` projection
publishes `FlashSessionUpdated` on every change (current listing, running
revenue, queue length, viewer count). Handler pushes to
`Group($"session-{sessionId}")`. Visible to seller, co-hosts, and audience
through whatever UI shells subscribe.

**Category activity ticker.** "Someone just bid on Widget X" broadcasts
across a category. Emitted from the `BidPlaced` projection side effect,
routed to a category-wide group. Cheap because one event produces one
outgoing message regardless of subscriber count.

**Chat messages and system announcements.** Not projection-driven in the
strict sense, but architecturally the same SignalR Group path. These
messages do not flow through the side-effect hook; they come from Wolverine
handlers directly. Noted here for completeness because they share the
broadcast transport.

### Per-viewer cases: `StreamAsync` + live queries (future)

These are the cases where the result set depends on who is asking, composes
across aggregates, or needs per-viewer filtering that does not belong at a
broadcast boundary.

**My active auctions.** `WatchMyActiveAuctions` yields a stream of
`MyAuctionsSnapshot` scoped to the authenticated participant. The set is
dynamic (auctions join and leave as the user bids), auth context is
per-viewer, and there is no natural projection that represents "user X's
active set" at scale. Better handled as a per-caller stream.

**Secret max-bid status.** Viewers who have set a max bid on a listing want
to see their own max, the current high bid's relationship to it, and
whether they are still winning. This data must not leak to other viewers.
Handler-side filtering at the yield site is the cleanest place for this,
which `StreamAsync` provides for free and broadcast doesn't.

**Cross-aggregate composed views.** "My dashboard" combining data from the
Participants BC, Selling BC, and Settlement BC. Composition happens inside
the handler. No single projection owns this view.

**Ad-hoc analytics-style live views.** "Top 10 listings by current bid right
now." Not a first-class aggregate, more a query that needs to re-run when
its inputs change. Handler-owned.

**Replay with live tail.** `WatchAuctionWithHistory(listingId)` yields the
last N events followed by live updates. Users joining mid-auction get
context, then live. Handler performs a Marten event query, yields those
events, then switches to live mode. This one could *also* be done with
broadcast plus a one-shot history fetch from the frontend, but the
"snapshot + subscribe" ergonomics of a single stream are cleaner.

**Operations console.** `WatchSystemHealth()` yields `OpsSnapshot` with
per-BC health, active auction counts, and DCB contention metrics.
Restricted to operators. Handler composes multiple sources into one stream.

**Settlement progress for a specific obligation.** `WatchSettlement(obligationId)`
lets a winning bidder see payment processing progress without polling.
Scope is per-user, lifecycle is bounded. Broadcast would work but is
heavier than needed.

## Techniques

### Techniques that apply to both mechanisms

**Authoritative server timer.** Clients do not own the clock. `AuctionSnapshot`
includes a server-sent `endsAt` timestamp; the client renders a countdown
from that timestamp. Anti-snipe and auction-extension policy lives on the
server and manifests as events (`AuctionExtended`) regardless of which
reactive path delivers them.

**Snapshot-first, then delta.** Clients receive the current state followed
by updates through the same channel. In the side-effect path this means the
client subscribes to the Group and separately fetches the current snapshot
once on connect. In the `StreamAsync` path this means the handler yields
the snapshot then enters the live loop (shown in the handler sketch above).
Same ergonomic outcome, different mechanics.

**Conflation for fast-moving state.** In a Flash auction where bids may
arrive every 200ms, pushing every intermediate snapshot is wasteful.
Conflation happens at different places depending on the path:

- **Side-effect path:** conflation is a projection concern. Either the
  projection's batch cadence provides natural batching (the daemon processes
  ranges of events, not one at a time), or the projection decides inside
  `RaiseSideEffects` whether the delta since last publish crosses a
  threshold worth announcing.
- **`StreamAsync` path:** conflation is a handler concern. Hold the latest
  snapshot, yield at most every N milliseconds or on semantic thresholds
  (`AuctionExtended`, `StatusChanged`, `NewHighBidder`).

Either way, conflation is business logic. Keep it out of the transport.

### Techniques specific to the side-effect path

**Derived events via `slice.AppendEvent`.** The projection has the new
snapshot available and can raise new first-class domain events based on
threshold crossings. `AuctionExtended`, `AuctionReachedReservePrice`,
`NewHighBidder` belong in the event log, not just in UI payloads. This is
often a cleaner home for anti-snipe logic than a dedicated process manager,
because the projection already holds the state the decision depends on and
the side-effect model gives it transactional permission to append.

**Projection as rate limiter.** Because the side effect only fires once per
daemon batch per aggregate, the broadcast rate is naturally bounded by the
daemon's batching rhythm. This is a free load-shedding mechanism at the
write side.

**Inline opt-in for low-latency paths.** `EnableSideEffectsOnInlineProjections`
lets us choose, per projection, whether side effects are async (safer under
rebuild, eventually consistent) or inline (lower latency, runs in the save
transaction). Default to async for auction state. Inline is plausible for
something like a real-time admin override where we want immediate broadcast.

**Rebuild awareness.** Side effects do not fire during rebuilds by design.
Any new reactive surface should assume it will be rebuildable without
collateral damage. This is one of the nicer properties of the pattern and
it deserves explicit acknowledgment in the skill file.

### Techniques specific to the `StreamAsync` path

**Stream-per-slice, composed.** Instead of one large `AuctionSnapshot`,
offer multiple narrow streams like `WatchAuctionTimer`, `WatchAuctionBids`,
and `WatchAuctionPresence`. Let the frontend decide which to subscribe to.
Reduces wasted bandwidth when a component only cares about one slice, and
maps cleanly to signal-based reactivity in the frontend where each stream
becomes its own signal or store.

Tradeoff: more handler surface to maintain. Start with one composite stream
per aggregate, split by slice only when a measurable frontend need emerges.
This technique applies less naturally to the side-effect path because
splitting into narrow broadcast topics fragments the fan-out.

**Scoped lifetime via process manager.** The Auctions BC will have a process
manager per live auction, with a lifecycle of `Started` → `Running` →
`Closing` → `Closed`. When the process manager records `AuctionClosed`, the
handler's live query emits one final snapshot and `yield break`s. Clients
get clean stream completion with no special "is the stream dead?" logic on
the frontend. This is the Critter-Stack-native version of what Whatnot gets
implicitly from GenServer process death.

**Per-viewer authorization filter.** Because the handler runs through
Wolverine's invoker pipeline, middleware applies. Auth context is available
where the handler executes. Enforce privacy rules (hiding max bid amounts
from non-owners, tenant isolation) at the yield site, not downstream in the
transport layer. Cleaner boundary, easier to test. This is the single
biggest pragmatic reason to reach for `StreamAsync` over broadcast for any
view that has per-viewer visibility rules.

## The Architectural Line

Revised framing after the side-effect discovery. The previous version of
this document framed it as "projection-driven views use `StreamAsync`,
broadcast-driven events use SignalR Groups." That framing was too narrow.
The corrected rule:

> **Broadcast state to many watchers of an aggregate: projection
> side-effects, published through Wolverine, fanned out via SignalR Group.**
>
> **Yield per-viewer computed or filtered views: `StreamAsync` with Marten
> live queries, delivered over HTTP streaming, gRPC, or a SignalR stream.**

Concretely for CritterBids:

- `WatchAuction` broadcast → side-effect path, available today.
- "My active auctions" per-viewer → `StreamAsync` path, future.
- "Secret max-bid visibility" per-viewer → `StreamAsync` path, future.
- Category activity ticker broadcast → side-effect path, available today.
- Chat messages and admin announcements → direct Wolverine handler to
  SignalR Group, which is a simpler cousin of the side-effect path (no
  projection, just a handler).

SignalR Groups are the fan-out transport for every broadcast case. The
side-effect mechanism is what generates the broadcast payloads from
aggregate state changes. These two should not be conflated: the Group is
the pipe, the side effect is the source.

## Forward-Looking Pattern Shape

With `StreamAsync` in place alongside the side-effect model, the Critter
Stack gains a genuinely complete handler surface for front-end-facing work.
Four message-handling shapes with distinct purposes:

- `InvokeAsync` for command/query, one result, transactional.
- `SendAsync`/`PublishAsync` for fire-and-forget, durable, outboxed.
- **`slice.PublishMessage` inside `RaiseSideEffects`** for projection-driven
  broadcast, outboxed atomically with the projection commit.
- `StreamAsync` for local, typed, reactive, per-viewer views.

That set is a clean teaching framework. The story becomes: "your UI gets
reactive data the same way your BCs get reactive projections: through
primitives that already exist in the Critter Stack, with no new framework
required." The side-effect path is what makes the claim land, because it
means the reactive story is not blocked on unshipped upstream features.

This strengthens the "Swapping the Bus" talk narrative. Transports change
(HTTP, gRPC, SignalR). The handler and projection shapes are invariant.

## Concerns and Risks

### Upstream timing and API churn (applies to `StreamAsync` path only)

Neither `StreamAsync` nor Marten live queries have shipped in a stable
release. Both APIs could change shape before GA. CritterBids should not
take a hard dependency on either until they are in a preview build we can
pin to, and any code written against them should be behind an interface
seam we control so we can adapt.

The side-effect path is unaffected by this concern. It ships today.

**Mitigation:** define CritterBids-internal interfaces
(`IMartenLiveQueries`, whatever else) and implement them against whatever
shim is available now (polling, in-memory, manual subscription). Swap the
implementation when upstream lands. Handlers stay untouched.

### Scale per connection (applies to `StreamAsync` path only)

`StreamAsync` creates one server-side enumerator per subscriber. For Flash
auctions with tens of thousands of concurrent viewers, that is not the
right tool. SignalR Group broadcast (driven by the side-effect path) is.
The architectural line is load-bearing.

**Mitigation:** default to the side-effect path for anything that might
scale. Reserve `StreamAsync` for per-viewer cases where the subscriber
count is bounded by "users who have a reason to hold this specific view
open."

### Projection cadence equals publish cadence (side-effect path)

The async daemon processes events in batches, which means the broadcast
cadence is inherently coupled to the daemon's rhythm. For most auction
scenarios this is fine (bids are already batched at a human-perceptible
granularity), but if a scenario demands stricter real-time semantics, the
side-effect path may not be the right tool.

**Mitigation:** measure actual daemon batch latency under expected load
before committing any scenario that needs sub-second freshness. If a use
case truly needs per-event broadcast latency, consider inline projections
with `EnableSideEffectsOnInlineProjections` as a fallback.

### Per-viewer filtering at the publish site (side-effect path)

The side-effect hook does not know who is watching. Any filtering that
depends on the viewer must happen downstream (in the Wolverine message
handler that calls `IHubContext`, or on the frontend after receipt). This
is fine for public data, uncomfortable for private data.

**Mitigation:** for data that is safe for all watchers of an aggregate,
broadcast freely. For data that has per-viewer visibility rules (secret
max-bid amount, owner-only fields), either (a) use `StreamAsync` instead,
or (b) broadcast only the public-safe subset and have the frontend request
the private overlay through a separate authenticated query. Do not try to
smuggle per-viewer filtering into a broadcast payload.

### Rebuild semantics (side-effect path)

Side effects do not fire during projection rebuilds by design. This is a
safety property. But it means any workflow that relies on a side effect
firing ("when the projection reaches state X, publish message Y") will not
replay that side effect if the projection is rebuilt.

**Mitigation:** do not use side effects for anything that is essential to
correctness. Use them for broadcast enrichment only. If a message must be
published exactly once when a state transition occurs, use an inline
projection or a command handler, not a side effect.

### Cancellation and resource cleanup (applies to `StreamAsync` path)

If a handler holds a database cursor, a subscription handle, or a Marten
session open for the duration of the stream, proper disposal on caller
cancellation is critical. Leaked resources accumulate silently and show up
as performance degradation or connection pool exhaustion, not as errors.

**Mitigation:** treat `[EnumeratorCancellation]` discipline as required in
the CritterBids streaming handler skill. Use `await using` for sessions
and subscriptions inside the handler body. Stress-test with aggressive
subscribe/cancel churn before any conference demo.

### Backpressure (applies to `StreamAsync` path)

`IAsyncEnumerable<T>` is inherently pull-based, which gives us free
backpressure from consumer to handler. But if the handler is producing from
a live query that is itself push-based (LISTEN/NOTIFY events arriving
faster than the consumer pulls), we need a bounded buffer and an explicit
policy for overflow. Options are drop-oldest, conflate-to-latest, or
refuse-further. Drop-oldest and conflate-to-latest are both acceptable for
auction state; refuse-further probably is not.

**Mitigation:** the conflation technique described above is partially a
backpressure strategy. Document the buffer size and overflow policy
per-stream when we build each one. Make this an explicit part of the
streaming handler skill.

### Authentication context lifetime (applies to `StreamAsync` path)

HTTP streaming connections can outlive the auth token that opened them. If
a bidder's token expires mid-stream, the stream should end cleanly and the
client should reconnect with a fresh token.

SignalR's reconnection model handles the broadcast-path equivalent of this
for us, which is another reason broadcast is the safer default.

**Mitigation:** align stream lifetime with token lifetime in the
`StreamAsync` path. Emit a synthetic terminal event (`StreamExpired` or
similar) when the auth context goes stale so the client can distinguish
"token expired, reconnect" from "auction closed, stop."

### Testing story

Both paths need deliberate testing patterns, for different reasons.

Streaming handlers are harder to test than `InvokeAsync<T>` handlers
because of the enumeration semantics. Side-effect-emitting projections are
harder to test than plain projections because the side effect is a
transactional concern that only fires in async daemon mode (or opt-in
inline).

**Mitigation:** add sections to `critter-stack-testing-patterns.md` for
each. For side-effect projections, follow the patterns in Marten's own
tests: use `Host.ExecuteAndWaitAsync` or equivalents to wait for Wolverine
message activity, assert on captured `IMessageBus.Sent` collections via
`TrackedHttpCall` or Wolverine's tracked-session support. For streaming
handlers, collect enumerated results into a list and assert over that
list; use `CancellationTokenSource` with short timeouts to end streams
deterministically.

### Observability

Long-lived streams complicate tracing. An OpenTelemetry span that stays
open for the duration of a 30-second Flash auction is not useful, and
creating a span per yielded snapshot is probably too noisy. Side-effect
publishes create one message per projection batch, which fits a normal
span-per-message model more naturally.

**Mitigation:** revisit when the Critter Stack's tracing story around
`StreamAsync` settles upstream. For the side-effect path, lean on Marten
and Wolverine's built-in OTel instrumentation; it already produces a span
per outbox publish.

## Concrete Next Steps

Revised priority after the side-effect discovery. The short version: the
MVP reactive path is now **available today** through the side-effect model.
`StreamAsync` work becomes future/additive.

1. **Author a companion skill file.** A new
   `docs/skills/projection-side-effects-for-broadcast-live-views.md` covering
   the how-to for the side-effect pattern: when to use it, the mechanical
   recipe, the derived-events technique, testing, and the rebuild caveat.
   Available-today skills deserve their own home, separate from vision-level
   discussions.
2. **Build the side-effect path for `AuctionSnapshot` in M3 or M4.** This
   is the MVP reactive surface. No shim, no upstream dependency, no
   waiting. Projection publishes `AuctionSnapshotUpdated`, Wolverine
   handler fans out to SignalR Group, React client subscribes.
3. **Keep the M1 SignalR direct-handler path.** Chat messages and direct
   admin broadcasts do not need a projection. Wolverine handler to
   `IHubContext` remains the pattern for those. Do not retrofit.
4. **Sketch `IMartenLiveQueries` when a per-viewer case actually needs it.**
   Previously this was in the critical path. Now it is opportunistic.
   Implement against whatever shim is available (manual Marten
   subscription, polling, in-memory channel) when the first per-viewer
   streaming case materializes. "My active auctions" is the most likely
   first trigger.
5. **Validate the pattern with Jeremy when the time comes.** When we are
   ready to build the first `StreamAsync` handler against a
   `liveQueries`-shaped interface, that is the moment to propose the
   CritterBids usage as a validation vehicle for the Marten 9 API shape.
   Not yet.
6. **Revise ADR candidacy.** This document should spawn at least one ADR
   in `docs/decisions/` once the side-effect `AuctionSnapshot` pattern is
   proven in CritterBids. Candidate framing: "Projection side-effects +
   Wolverine + SignalR Group is the broadcast primitive for reactive UIs
   in the Critter Stack; `StreamAsync` is reserved for per-viewer cases."
   A second ADR may follow when `StreamAsync` lands and we adopt it for
   the first per-viewer surface.

## References

- Marten docs on projection side effects:
  <https://martendb.io/events/projections/side-effects.html>
- Marten docs on aggregate projections:
  <https://martendb.io/events/projections/aggregate-projections.html>
- Marten docs on the async daemon:
  <https://martendb.io/events/projections/async-daemon.html>
- Wolverine PR #2525 adding `StreamAsync` to `ICommandBus`/`IMessageBus`:
  <https://github.com/JasperFx/wolverine/pull/2525>
- Artsy "The Tech Behind Live Auction Integration" (Causality, Akka
  Persistence, WebSocket fan-out):
  <https://artsy.github.io/blog/2016/08/09/the-tech-behind-live-auction-integration/>
- Whatnot "Keeping Up with the Fans" (Elixir Live Service, Phoenix
  Channels, load-shedding patterns):
  <https://medium.com/whatnot-engineering/keeping-up-with-the-fans-scaling-for-big-events-at-whatnot-with-elixir-and-phoenix-1916eba58a76>
- ADR 004 (React frontend): `docs/decisions/004-react-frontend.md`
- Skill: `docs/skills/wolverine-signalr.md`
- Skill: `docs/skills/wolverine-message-handlers.md`
- Skill: `docs/skills/marten-projections.md`
- Skill (to be authored): `docs/skills/projection-side-effects-for-broadcast-live-views.md`
