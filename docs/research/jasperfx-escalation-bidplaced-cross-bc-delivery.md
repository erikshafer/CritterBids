# JasperFx Escalation — ROOT CAUSE FOUND: single-saga chains suppress sticky-handler fan-out under `MultipleHandlerBehavior.Separated`

**Status:** Root cause identified (source-confirmed + live-verified) — ready to file upstream with proposed fix
**Owner:** Erik Shafer
**Date:** 2026-06-09 (root cause session: same day, follow-up investigation)
**Project:** CritterBids (Wolverine + Marten modular monolith)
**Full investigation trail:** [`dcb-marten-blog-series-research.md`](./dcb-marten-blog-series-research.md) §5.0–§5.5

---

## TL;DR

**The bug was never on the publish side.** Every one of the eight original experiments — fast event
forwarding, explicit `bus.PublishAsync`, `OutgoingMessages`, async-202 — was already sending
`BidPlaced` to its three RabbitMQ queues correctly. Debug-level logs show the envelopes enqueued,
sent, received back in-process, and **"Successfully processed."**

The defect is in **consume-side handler dispatch** under `MultipleHandlerBehavior.Separated`:

> **`SagaChain.maybeAssignStickyHandlers` only separates saga handlers into their own sticky chains
> when MORE THAN ONE saga type handles the message (`groupedSagas.Length > 1`). With exactly ONE
> saga type plus other handlers, the saga's handler calls remain in the chain's default `Handlers`
> collection. `HasDefaultNonStickyHandlers()` is then true, so
> `HandlerGraph.HandlerFor(messageType, endpoint)` never takes the `FanoutMessageHandler` branch for
> deliveries arriving at external (broker) endpoints — it executes the default chain (the saga)
> only. Every non-saga handler, sticky on its own local queue, silently starves.**

Wolverine 6.5.1, `src/Wolverine/Persistence/Sagas/SagaChain.cs` (`maybeAssignStickyHandlers`) +
`src/Wolverine/Runtime/Handlers/HandlerGraph.cs` (`HandlerFor(Type, Endpoint)`).

No dead letters, no warnings — the message is "successfully processed" (by the saga alone), so the
starvation is invisible to every existing diagnostic.

## Environment

| Component | Version |
|---|---|
| .NET | 10 |
| Marten | 9.6.0 |
| JasperFx / JasperFx.Events | 2.8.2 |
| WolverineFx (+ .Marten, .Http, .Http.Marten, .RabbitMQ, .RuntimeCompilation) | 6.5.1 |
| PostgreSQL | 17 |
| RabbitMQ | (Aspire-provisioned) |
| Host | single process (modular monolith), `MultipleHandlerBehavior.Separated`, `MessageIdentity.IdAndDestination` |

## The mechanism, step by step (6.5.1 source)

The two message types that exposed the asymmetry:

| | `BiddingOpened` (works) | `BidPlaced` (starves) |
|---|---|---|
| Saga involvement | saga **started** by a separate static class (`StartAuctionClosingSagaHandler`, returns the new saga) | saga **continued** by an instance method `AuctionClosingSaga.Handle([SagaIdentityFrom] BidPlaced)` |
| Chain type (`HandlerGraph.buildHandlerChain`) | plain `HandlerChain` (no call satisfies `isSagaMethod`) | **`SagaChain`** (non-static method on a `Saga` subclass) |
| Other handlers in-process | Listings, Relay, Operations consumers | `ProxyBidDispatchHandler` + Listings, 2× Relay, 2× Operations consumers |

1. **Grouping.** `HandlerChain` ctor: `if (grouping.Count() > 1) maybeAssignStickyHandlers(...)`.
   - Plain `HandlerChain.maybeAssignStickyHandlers` (the `BiddingOpened` case): **every** handler
     call goes through `tryAssignStickyEndpoints`; under `Separated` each gets a sticky local queue
     (`local://<handlertype>/`) and is **removed from the default `Handlers`**. The chain ends up
     all-sticky, no defaults.
   - `SagaChain.maybeAssignStickyHandlers` override (the `BidPlaced` case): non-saga calls get the
     same sticky treatment, **but saga calls are only separated when `groupedSagas.Length > 1`**
     (the multi-saga branch added for "Multiple saga types handle message" support). With one saga
     type, the saga's calls **stay in `Handlers`** — the chain has sticky handlers *and* a default
     handler.

2. **Dispatch at an external endpoint.** `HandlerGraph.HandlerFor(messageType, endpoint)` for a
   message arriving on a RabbitMQ listener:
   ```csharp
   var sticky = chain.ByEndpoint.FirstOrDefault(x => x.Endpoints.Contains(endpoint)); // none — all sticky chains are local
   if (sticky == null)
   {
       if (!chain.HasDefaultNonStickyHandlers())   // ← FALSE for SagaChain w/ single saga: Handlers.Any() == true
       {
           // all-local fanout: relay the message from the external endpoint to each sticky local queue
           ... getOrBuildFanoutHandler(messageType, chain) ...
       }
       return HandlerFor(messageType);              // ← executes the DEFAULT chain = the saga ONLY
   }
   ```
   The `FanoutMessageHandler` relay — the only path by which the sticky local handlers ever see an
   externally-delivered message — is **gated on the chain having NO default handlers**. The lone
   saga keeps a default handler alive, so the gate never opens.

3. **Observable result.** Each RabbitMQ delivery of `BidPlaced` executes the saga continue-handler
   (idempotent, so 3 queue deliveries are harmless to it) and **nothing else**. The Listings read
   model, the Relay `BiddingHub` push, and the Operations feeds never receive the message. Log says
   "Successfully processed." No dead letters.

## Live proof (debug log, one accepted HTTP bid)

**Publish side works** (this alone falsifies the entire original escalation framing):

```
Enqueued for sending BidPlaced#0...0 to rabbitmq://queue/listings-auctions-events
Enqueued for sending BidPlaced#0...0 to rabbitmq://queue/operations-auctions-events
Enqueued for sending BidPlaced#0...0 to rabbitmq://queue/relay-auctions-events
Received BidPlaced#0...0 at rabbitmq://queue/listings-auctions-events from rabbitmq://queue/wolverine.response...
Successfully processed message CritterBids.Contracts.Auctions.BidPlaced#0...0 from rabbitmq://queue/listings-auctions-events
(... same for the other two queues — and NO local:// relays follow ...)
```

**Contrast — `BiddingOpened` on the same queues fans out** (the missing piece for `BidPlaced`):

```
Received BiddingOpened#0...0 at rabbitmq://queue/listings-auctions-events ...
Successfully processed message ... BiddingOpened ...
Enqueued for sending BiddingOpened#0...1 to local://critterbids.auctions.startauctionclosingsagahandler/
Enqueued for sending BiddingOpened#0...2 to local://critterbids.listings.auctionstatushandler/
Enqueued for sending BiddingOpened#0...3 to local://critterbids.operations.lotboardauctionshandler/
Enqueued for sending BiddingOpened#0...4 to local://critterbids.relay.handlers.auctionsbiddinghandler/
```

**Routing is symmetric and correct** — a live `PreviewSubscriptions` probe
(`src/CritterBids.Api/Dev/RoutingProbeEndpoint.cs`, runs `RoutingFor(GetType()).RouteForPublish`,
the exact path `PublishIncomingEventsBeforeCommit` hits) shows `Event<BidPlaced>` and
`Event<BiddingOpened>` both resolving to their three RabbitMQ unwrapping routes
(`EventUnwrappingMessageRoute` → raw event type). The `MartenEventRouter` chain
(`RoutingFor(Event<T>)` → `RoutingFor(IEvent<T>)` → unwrap over `RoutingFor(T)` = the three
`ExplicitRouting` subscriptions) behaves exactly as designed for both types.

**Predictions verified live** (same session): `ReserveMet` and `ExtendedBiddingTriggered` — both
saga continue-handlers with a Relay route — show the identical signature: sent → received at
`relay-auctions-events` → "Successfully processed" → **no local relay** → Relay starved. The saga
meanwhile consumed everything correctly (`BidCount=2, ReserveHasBeenMet=true, Status=Extended`).

**Predicted but not yet verified:** `BuyItNowPurchased` and `ListingWithdrawn` (also
`AuctionClosingSaga` continue-handlers with multiple cross-BC consumers) should starve their
Listings / Settlement / Operations / ProxyBidDispatch consumers by the same mechanism — two latent
integration bugs that nothing has surfaced yet.

## Why every prior experiment "failed"

All eight experiments (`[BoundaryModel]`, `IMessageBus` enrollment, explicit `PublishAsync`,
`WolverineFx.Http.Marten`, `(IResult, OutgoingMessages)`, manual endpoint + package +
`OutgoingMessages`, async-202 durable local queue, `OutgoingMessages` from the queued handler)
varied the **publish** mechanism. Each one published successfully to RabbitMQ; each delivery was
then eaten by the saga default chain at the listener. The observation "saga advances, consumers
don't" was misread as "local forwarding works, external doesn't" — in fact the saga advance WAS the
external delivery, consumed inline (the rabbit listeners run `ProcessInline`/buffered, so no
`wolverine_incoming_envelopes` rows; the rows seen for other types are the durable **local sticky
queue** copies created by fan-out, which `BidPlaced` never reaches).

Also explained in passing:
- **Bug #3** (saga-start `DocumentAlreadyExistsException` dead letters ×2 on `BiddingOpened`): the
  fan-out delivers the saga-start to its sticky local queue once per RabbitMQ queue (3×); the first
  creates the saga, the others race past the `LoadAsync` guard. At-least-once noise, not a defect
  in the handler.
- `BuyItNowOptionRemoved` / `BidRejected` (single handler, no saga): single-handler chains skip
  sticky grouping entirely (`grouping.Count() > 1` gate), stay default, and execute directly at the
  rabbit endpoint — which is why they deliver fine.

## Proposed framework fix (two candidate shapes)

1. **In `SagaChain.maybeAssignStickyHandlers`:** under `MultipleHandlerBehavior.Separated`, run the
   per-saga separation (the existing `groupedSagas.Length > 1` block) for `Length >= 1` whenever the
   grouping also contains non-saga handlers — i.e. a single saga type gets its own sticky
   local-queue chain exactly like the multi-saga case. The chain then has no default handlers and
   external deliveries take the existing fan-out path (saga included, which is safe — saga handlers
   already need at-least-once idempotency).
   *Compat note:* making the saga sticky changes `InvokeAsync`-style invocation of that message
   type (sticky handlers + `InvokeAsync` → `NoHandlerForEndpointException`), so this should likely
   be scoped to `Separated` mode only, where that trade is already the documented behavior.
2. **In `HandlerGraph.HandlerFor(Type, Endpoint)`:** when a chain has BOTH sticky-local handlers
   and default handlers and the message arrives at an external endpoint with no endpoint-specific
   sticky match, execute the default handler AND relay to the sticky local queues (fan-out is
   currently all-or-nothing on `HasDefaultNonStickyHandlers()`). This is the more general fix — it
   also covers a future mixed default+sticky chain that doesn't involve sagas.

## Minimal reproduction (generic, no CritterBids required)

One process: `MultipleHandlerBehavior.Separated`; one message type `Ping`;
`PublishMessage<Ping>().ToRabbitQueue("q1")` + `ListenToRabbitQueue("q1")`; handlers = one saga
continue-handler (`MySaga.Handle([SagaIdentityFrom] Ping)`) + one plain static handler
(`PingViewHandler.Handle(Ping)`). Publish `Ping` from anywhere. Expected: both handlers run.
Actual: only the saga runs; `PingViewHandler` (sticky on `local://pingviewhandler/`) never fires.
Replace the saga continue-handler with a static start-style handler (or add a second saga type) and
`PingViewHandler` starts receiving via fan-out.

CritterBids in-repo repro: run the Aspire stack, `POST /api/dev/seed-flash`, place a bid via
`POST /api/auctions/bids`, observe `bid_placed` in `mt_events` + `AuctionClosingSaga` advanced +
`CatalogListingView.CurrentHighBid` stuck at `null`. The dev routing probe
(`GET /api/dev/routing-probe`) shows the routes are correct.

## Secondary upstream finding (separate, small)

`dotnet run -- wolverine-diagnostics describe-routing <MessageType> --explain` throws
`NullReferenceException` at `MessageRoute.Describe()` (MessageRoute.cs:204) in this app —
a description-mode route (null-tolerant member assignment; the throwing dereference is the
route's `Serializer`) reached the explanation's `FinalRoutes` mapping (adjacent to the GH-2897
description-mode null-Sender case leaking into `Describe()`). The live `PreviewSubscriptions`
probe was the workaround.

## CritterBids application-level options (until a framework fix ships)

- **(a) Dispatcher-bridge — ADOPTED (branch `fix/m8-auction-closing-dispatch-bridge`,
  live-verified: read model + Relay + Operations all receive `BidPlaced`; 293 tests green):** mirror
  `ProxyBidDispatchHandler` (M4-S3 "Path C"). Remove the contract-event continue-handlers from
  `AuctionClosingSaga` (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`,
  `ListingWithdrawn`) and route them through a plain static `AuctionClosingDispatchHandler` that
  emits Auctions-internal per-saga commands (`[SagaIdentityFrom(SagaId)]`). Every contract-event
  chain becomes saga-free → all-sticky → fan-out works; the saga receives its updates via the
  internal commands' default chains. `CloseAuction` stays direct on the saga (single-handler chain,
  unaffected).
- **(b) Patched Wolverine:** apply fix (1) locally / land it upstream (Erik has the channel) and
  bump the package pin.
- **NOT a fix:** any change to the publish side (forwarding vs subscriptions vs explicit publish) —
  empirically and now analytically irrelevant; the consume-side dispatch eats the delivery
  regardless of how it was sent.
