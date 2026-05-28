# Proxy bidding

**Maturity:** Implemented end-to-end (registration → reactive auto-bidding → exhaustion → terminal absorption). The `ProxyBidRegistered` and `ProxyBidExhausted` audit-event consumers are post-M5 (Relay/Operations).

## Trigger

`RegisterProxyBid(ListingId, BidderId, MaxAmount)` cross-BC command dispatched against a listing where bidding is open. Dispatch is test-only at M5 (M6 adds HTTP) per the M2.5 dispatch-precedent.

Source: `src/CritterBids.Contracts/Auctions/RegisterProxyBid.cs`, `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs`.

## Registration hops

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 1 | Auctions (saga-start) | `StartProxyBidManagerSagaHandler.Handle(RegisterProxyBid)` — derives composite-key saga id: `UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")` | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:48-49`, `AuctionsIdentityHelpers.cs:14-22` |
| 2 | Auctions | Idempotency dedup | `LoadAsync<ProxyBidManagerSaga>(sagaId)` — early-return `(null, empty)` if saga already exists for this `(ListingId, BidderId)` | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:53-58` |
| 3 | Auctions | Credit ceiling lookup | `LoadAsync<ParticipantCreditCeiling>(BidderId)` — throws `ParticipantCreditCeilingNotFoundException` if projection has not caught up; `AuctionsConcurrencyRetryPolicies` retries with 100ms / 250ms / 500ms cooldown | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:63-66`, `AuctionsModule.cs:147-153` |
| 4 | Auctions | Create saga | `ProxyBidManagerSaga { Id, ListingId, BidderId, MaxAmount, BidderCreditCeiling, LastBidAmount = 0, Status = Active }` | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:68-77` |
| 5 | Auctions | Audit emission | `ProxyBidRegistered(ListingId, BidderId, MaxAmount, RegisteredAt)` via `OutgoingMessages` — no `PublishMessage` route in `Program.cs`, so the event lands in `tracked.NoRoutes` (consumer is Relay, post-M5) | `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:79-85` |

## Reactive hops (per `BidPlaced` while saga is `Active`)

The Proxy Bid Manager's `Id` is a composite key that no contract event carries, so Wolverine's `[SagaIdentityFrom]` cannot route `BidPlaced` directly. The **`ProxyBidDispatchHandler`** is the correlation bridge (M4-S3 OQ1 Path C):

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| R1 | Auctions (dispatcher) | `ProxyBidDispatchHandler.Handle(BidPlaced)` — queries `Query<ProxyBidManagerSaga>().Where(s => s.ListingId == message.ListingId && s.Status == Active)` | `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs:45-77` |
| R2 | Auctions | Fan out | One `ProxyBidObserved(SagaId, ListingId, BidId, BidderId, Amount, BidCount, IsProxy, PlacedAt)` per active saga via `OutgoingMessages`. Empty result (most listings) emits nothing. | `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs:63-75` |
| R3 | Auctions (saga) | `ProxyBidManagerSaga.Handle(ProxyBidObserved)` routed via standard `[SagaIdentityFrom(nameof(SagaId))]` property-pull | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:66-67` |
| R4a | Auctions (saga) | Own bid (`message.BidderId == this.BidderId`) | Monotone tracking: `LastBidAmount = message.Amount` if strictly higher. No emission. | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:75-86` |
| R4b | Auctions (saga) | Competing bid — compute `increment = message.Amount >= 100m ? 5m : 1m` (third co-located copy alongside `PlaceBidHandler.cs:174-175` and `BuyNowHandler` — CLAUDE.md "three similar lines is better than a premature abstraction"); compute `capped = min(message.Amount + increment, MaxAmount, BidderCreditCeiling)` | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:88-101` |
| R5a | Auctions (saga) | Exhaustion (`capped <= message.Amount`) | `Status = Exhausted`; `MarkCompleted()`; emit `ProxyBidExhausted(ListingId, BidderId, MaxAmount, ExhaustedAt)` via `OutgoingMessages` (also `tracked.NoRoutes`; Relay post-M5) | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:103-120` |
| R5b | Auctions (saga) | Defensive bid | Emit `PlaceBid(ListingId, BidId = Guid.CreateVersion7(), BidderId, Amount = capped, CreditCeiling = BidderCreditCeiling)` via `OutgoingMessages` with `IsProxy: true` semantic (note: `PlaceBid` command itself has no `IsProxy` field; the in-process re-entry path through `PlaceBidHandler` sets `IsProxy: false` on the emitted `BidPlaced` — see Open question below) | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:122-130` |

## Terminal hops (per `ListingSold` / `ListingPassed` / `ListingWithdrawn`)

The same dispatcher fans out terminal events to active proxy sagas:

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| T1 | Auctions (dispatcher) | `ProxyBidDispatchHandler.Handle(ListingSold)` / `.Handle(ListingPassed)` / `.Handle(ListingWithdrawn)` — same `Query<ProxyBidManagerSaga>` lookup; emit one `ProxyListingSoldObserved` / `ProxyListingPassedObserved` / `ProxyListingWithdrawnObserved` per active saga | `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs:80-128` |
| T2 | Auctions (saga) | `ProxyBidManagerSaga.Handle(ProxyListing*Observed)` — terminal guard (early return if not `Active`); `Status = ListingClosed`; `MarkCompleted()`. Each handler has a `public static OutgoingMessages NotFound(...) => new()` absorber for post-`MarkCompleted` redeliveries | `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:148-176` |

## State diagram

```
ProxyBidManagerSaga (Auctions BC)
─────────────────────────────────
                       (own ProxyBidObserved: monotone LastBidAmount update)
                       (competing ProxyBidObserved with capped > Amount: emit PlaceBid)
                                       │
                                       ▼
   RegisterProxyBid ───────────────► Active ─────competing bid where capped ≤ Amount─────► Exhausted
                                       │                                                       (MarkCompleted)
                                       │
                                       └──ProxyListingSoldObserved───────────► ListingClosed
                                       │  ProxyListingPassedObserved        (MarkCompleted)
                                       └──ProxyListingWithdrawnObserved
```

## Notable design decisions

- **Composite-key correlation (M4-S3 OQ1 Path C).** Wolverine's `[SagaIdentityFrom]` only resolves a Guid property by name; the Proxy Bid Manager's id is a derived UUID v5 that no contract event carries. Path A (resolver-based) unavailable in Wolverine. Path B (add `ProxyBidManagerSagaId` field to `BidPlaced`) ruled out: a single `BidPlaced` can target N proxy sagas (one per registered bidder on the listing), and one Guid field cannot address many. Hence the dispatcher.
- **Credit ceiling lookup via local projection (M4-D4 duplicate-projection pattern, second lived application).** Saga-start reads `ParticipantCreditCeiling`, a local Auctions cache projected from `ParticipantSessionStarted` on the `auctions-participants-events` queue. Settlement's `BidderCreditView` (M5-S5) is the first lived application. Each BC maintains its own copy on a BC-specific queue.
- **Triple terminal-absorber pattern.** Every terminal-handler pair (`Handle(X)` + `public static NotFound(X) => new()`) on the saga absorbs late-delivery after `MarkCompleted()` deletes the document. Without the absorber Wolverine throws `UnknownSagaException`. Mirrors `AuctionClosingSaga.NotFound(CloseAuction)` precedent.

## Outcome

- On exhaustion: `ProxyBidExhausted` audit event published; saga document deleted.
- On listing termination: saga document deleted; `Status` was briefly `ListingClosed`.
- Defensive bids flow back through `PlaceBidHandler` as ordinary `PlaceBid` commands and emerge as `BidPlaced` integration events — observable to other proxy sagas on the same listing (bidding war scenario 4.10).

## Open questions

- The `PlaceBid` command emitted by the saga in R5b has no `IsProxy` field — `PlaceBidHandler.AcceptanceEvents` hardcodes `IsProxy: false` on the emitted `BidPlaced` (`PlaceBidHandler.cs:116`). The `BidPlaced` contract docstring says "M4 wires the Proxy Bid Manager saga to set `IsProxy=true` on auto-bids" but the wiring is not present in the read code. Recorded in `OPEN-QUESTIONS.md`.
