# JasperFx Escalation — one Marten event type forwards only locally, never to its external routes

**Status:** Ready to send
**Owner:** Erik Shafer
**Date:** 2026-06-09
**Project:** CritterBids (Wolverine + Marten modular monolith)
**Full investigation trail:** [`dcb-marten-blog-series-research.md`](./dcb-marten-blog-series-research.md) §5.0–§5.4

---

## TL;DR

In a Wolverine modular monolith, two sibling Marten event types are appended **identically** (same
DCB-tagged append, from Wolverine message handlers) and routed **identically**
(`PublishMessage<T>().ToRabbitQueue(...)` to the same three queues). One of them (`BiddingOpened`) is
delivered to **both** its local in-process handler and its external RabbitMQ routes via
`UseFastEventForwarding`. The other (`BidPlaced`) is delivered **only to its local handler** — its
external routes never fire, via forwarding *or* explicit `OutgoingMessages`/`PublishAsync`, from HTTP
*or* from a queued-worker handler. The decisive control: **the same handler class
(`Listings.AuctionStatusHandler`) consumes both event types from the same queue** — `BiddingOpened`
arrives, `BidPlaced` never does.

**Question:** Why would `UseFastEventForwarding` (and explicit cascading publish) deliver one event
type only to its local handler while a sibling type with identical append + routing is delivered to
both local and external subscribers?

---

## Environment

| Component | Version |
|---|---|
| .NET | 10 |
| Marten | 9.6.0 |
| JasperFx / JasperFx.Events | 2.8.2 |
| WolverineFx (+ .Marten, .Http, .Http.Marten, .RabbitMQ, .RuntimeCompilation) | 6.5.1 |
| PostgreSQL | 17 |
| RabbitMQ | (Aspire-provisioned) |
| Host | single process (modular monolith), .NET Aspire orchestration |

## Architecture / relevant configuration

CritterBids is a modular monolith: all bounded contexts run in one process. Cross-BC integration
events are published to RabbitMQ queues that the **same process** also listens on
(`PublishMessage<T>().ToRabbitQueue(q)` + `ListenToRabbitQueue(q)`). Marten is the single shared event
store; `UseFastEventForwarding` forwards appended events to Wolverine.

`Program.cs` (`UseWolverine`):

```csharp
opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
opts.Durability.MessageStorageSchemaName = "wolverine";

// cross-BC routing (same shape for BiddingOpened and BidPlaced)
opts.PublishMessage<BiddingOpened>().ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<BidPlaced>().ToRabbitQueue("listings-auctions-events");
opts.PublishMessage<BidPlaced>().ToRabbitQueue("operations-auctions-events");
opts.PublishMessage<BidPlaced>().ToRabbitQueue("relay-auctions-events");
// ... BiddingOpened has the analogous operations-/relay- routes too ...
opts.ListenToRabbitQueue("listings-auctions-events");   // consumed in-process by Listings

opts.Policies.AutoApplyTransactions();
opts.Policies.UseDurableLocalQueues();
```

```csharp
builder.Services.AddMarten(opts => { /* ... */ })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true);
```

Both event types are registered: `opts.Events.AddEventType<BiddingOpened>()`,
`opts.Events.AddEventType<BidPlaced>()`.

## How both events are produced (identical mechanism)

Both are appended to the **same per-listing event stream**, tagged with the same DCB tag
(`ListingStreamId`), from inside a Wolverine **message handler**:

```csharp
// BiddingOpened — appended by SessionStartedHandler (a message handler) via OpenListingForBidding:
var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
var wrapped  = session.Events.BuildEvent(biddingOpened);
wrapped.AddTag(new ListingStreamId(listingId));
session.Events.Append(listingId, wrapped);
// AutoApplyTransactions commits; UseFastEventForwarding forwards.

// BidPlaced — appended by PlaceBidHandler (a message handler) via the same code path:
var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(query);
var wrapped  = session.Events.BuildEvent(bidPlaced);
wrapped.AddTag(new ListingStreamId(listingId));
session.Events.Append(listingId, wrapped);
```

DCB tag registration:

```csharp
opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>();
```

## Symptom

After an accepted bid:
- `mt_events` contains `bid_placed` (the append + DCB commit succeed).
- The local `AuctionClosingSaga.Handle(BidPlaced)` **runs** (saga advances `BidCount`/`CurrentHighBid`)
  — so `BidPlaced` **is** forwarded locally.
- `wolverine.wolverine_incoming_envelopes` for `BidPlaced` (and outgoing, dead-letter) = **0**.
- The Listings read model `CatalogListingView.CurrentHighBid` stays `null`, and the Relay `BiddingHub`
  never receives a bid push — i.e. the **external/cross-BC** consumers never receive `BidPlaced`.

By contrast, `BiddingOpened` (same append, same routing) reaches the external consumers normally:
`CatalogListingView.Status` flips to `"Open"` and `wolverine_incoming_envelopes` shows `BiddingOpened`.

## `describe` routing table (routes exist — confirmed)

```
CritterBids.Contracts.Auctions.BiddingOpened → rabbitmq://queue/listings-auctions-events
CritterBids.Contracts.Auctions.BiddingOpened → rabbitmq://queue/operations-auctions-events
CritterBids.Contracts.Auctions.BiddingOpened → rabbitmq://queue/relay-auctions-events
CritterBids.Contracts.Auctions.BidPlaced     → rabbitmq://queue/listings-auctions-events
CritterBids.Contracts.Auctions.BidPlaced     → rabbitmq://queue/operations-auctions-events
CritterBids.Contracts.Auctions.BidPlaced     → rabbitmq://queue/relay-auctions-events
```

(Neither shows a `local://` route — the local handlers are reached via `UseFastEventForwarding`, not
the routing table.)

## The decisive control

`CritterBids.Listings.AuctionStatusHandler` is a single handler class with handler methods for
**both** event types, both consumed from `listings-auctions-events`:

```csharp
public static async Task Handle(BiddingOpened message, IDocumentSession session, CancellationToken ct)
{
    var view = await session.LoadAsync<CatalogListingView>(message.ListingId, ct)
               ?? new CatalogListingView { Id = message.ListingId };
    session.Store(view with { Status = "Open", ScheduledCloseAt = message.ScheduledCloseAt });
}

public static async Task Handle(BidPlaced message, IDocumentSession session, CancellationToken ct)
{
    var view = await session.LoadAsync<CatalogListingView>(message.ListingId, ct)
               ?? new CatalogListingView { Id = message.ListingId };
    session.Store(view with { CurrentHighBid = message.Amount, CurrentHighBidderId = message.BidderId, BidCount = message.BidCount });
}
```

`BiddingOpened` reaches it; `BidPlaced` does not. **Same handler class, same BC, same queue, same
routing config** — only the event type differs. This rules out a different-consumer, different-route,
or missing-handler explanation.

## What both event types have in common vs. what differs

Identical: append mechanism (DCB-tagged `FetchForWritingByTags` + `BuildEvent` + `AddTag` + `Append`),
target stream, tag type, `AddEventType` registration, `PublishMessage().ToRabbitQueue()` routes,
producing context (a Wolverine message handler), consumer handler class.

Difference we can see: each has a **local handler** on the `AuctionClosingSaga`, but of different
kinds — `BiddingOpened` → the saga **start** handler (`IAmStartedBy` / creates the saga);
`BidPlaced` → a saga **continue** handler via `[SagaIdentityFrom(nameof(BidPlaced.ListingId))]`. This
is our prime suspect: whether a message type having a local (sticky, under
`MultipleHandlerBehavior.Separated`) handler interacts with its external publish routes during route
resolution, such that the forwarded event is delivered only locally.

## Minimal reproduction (against the running app)

1. Run the stack (Aspire provisions Postgres + RabbitMQ + the API host).
2. `POST /api/dev/seed-flash` — drives a Flash listing to `Open` (this path's `BiddingOpened` reaches
   Listings fine; the read model shows `Status: "Open"`).
3. Place an accepted bid (any of: the real `POST /api/auctions/bids` HTTP endpoint; or publishing the
   internal `PlaceBid` command to a queued handler).
4. Observe:
   - `select version,type from public.mt_events where stream_id = '<listingId>'` → includes `bid_placed`.
   - `select data->>'BidCount' from auctions.mt_doc_auctionclosingsaga where id = '<listingId>'` → `1`
     (local forward fired).
   - `select count(*) from wolverine.wolverine_incoming_envelopes where message_type ilike '%BidPlaced%'`
     → `0`.
   - `select data->>'CurrentHighBid' from listings.mt_doc_cataloglistingview where id = '<listingId>'`
     → `null` (the cross-BC consumer never received `BidPlaced`).

## What we tried (8 experiments, all reproduce the symptom)

| # | Approach | External `BidPlaced` delivered? |
|---|---|---|
| 1 | Canonical `[BoundaryModel] IEventBoundary<T>` Wolverine.HTTP endpoint (`boundary.AppendOne`) | No |
| 2 | Inject `IMessageBus` into the HTTP endpoint (outbox-middleware trigger) | No |
| 3 | Explicit `bus.PublishAsync(bidPlaced)` from the HTTP endpoint | No |
| 4 | Add `WolverineFx.Http.Marten` package (was missing from the API) | No |
| 5 | Return `(IResult, OutgoingMessages)` from the HTTP endpoint | No |
| 6 | Manual (non-`[BoundaryModel]`) HTTP endpoint + package + `OutgoingMessages` | No |
| 7 | async-202: HTTP publishes `PlaceBid` to an explicit durable local queue → queued `PlaceBidHandler` | No (command **does** traverse the queue; handler runs; still no external `BidPlaced`) |
| 8 | Queued `PlaceBidHandler` returns `OutgoingMessages` carrying the accepted events | No |

Plus (from an earlier manual run, no package): returning `OutgoingMessages`, `IMartenOutbox.PublishAsync`,
and `bus.InvokeAsync<BidOutcome>` — also no external delivery.

In **every** case `BidPlaced` is forwarded **locally** (the saga advances). Only its external/cross-BC
delivery fails. Experiment 7 confirms a `CritterBids.Auctions.PlaceBid` incoming envelope (the command
genuinely went through a durable local queue worker — a real message-handler execution context), and
`BidPlaced` still did not route externally from there.

## The question for JasperFx

In this configuration, **why is `BidPlaced` forwarded/published only to its local handler and never to
its external `ToRabbitQueue` routes, while `BiddingOpened` — appended and routed identically, consumed
by the same handler class — is delivered to both?**

Specifically:
1. Does `UseFastEventForwarding`'s route resolution treat an event type with a **local handler**
   differently under `MultipleHandlerBehavior.Separated` — e.g. resolving only the sticky local
   endpoint and dropping the external publish routes?
2. Does a saga **continue** handler (`[SagaIdentityFrom]`) vs a saga **start** handler change how the
   forwarded/published event is routed?
3. Is there a supported way to ensure an event with a local handler is **also** published to its
   external routes from a Wolverine message handler (forwarding or explicit `OutgoingMessages`)?

## Notes / what is NOT the cause (already ruled out)

- Not "routes missing" — `describe` shows all three RabbitMQ routes for `BidPlaced`.
- Not "no consumer" — `Listings.AuctionStatusHandler`, `Relay.BidPlacedHandler`,
  `Relay.AuctionsOperationsHandlers` all handle `BidPlaced`.
- Not "HTTP request pipeline" — fails identically from a durable-local-queue worker (Experiment 7/8).
- Not DCB / `[BoundaryModel]` / the append API — fails with `session.Events.Append` and
  `boundary.AppendOne`, with and without `[BoundaryModel]`.
- Not "event type unregistered" — both are `AddEventType<>`-registered.
- Not `bus.PublishAsync(object)` runtime-type routing — Wolverine routes by `message.GetType()`.

## Working plumbing (for the eventual fix)

Routing `PlaceBid` to an explicit durable local queue
(`opts.PublishMessage<PlaceBid>().ToLocalQueue("auctions-place-bid")`) works — the command is processed
by a separate queued worker. Once the cause above is identified, pairing that with whatever makes
`BidPlaced` reach its external routes completes the fix.
