# Auctions BC Dossier

**Maturity:** Implemented (largest BC by file count — 37 cs files in `src/CritterBids.Auctions/` plus 15 contracts in `src/CritterBids.Contracts/Auctions/`; 17 test classes in `tests/CritterBids.Auctions.Tests/`).

Source of truth: `src/CritterBids.Auctions/`, `src/CritterBids.Contracts/Auctions/`, `tests/CritterBids.Auctions.Tests/`. Cross-cuts read at `src/CritterBids.Api/Program.cs`.

---

## Purpose

Owns the bidding lifecycle of a listing — from the moment a published listing opens for bidding through every bid-acceptance decision, every proxy-bid auto-reaction, every extended-bidding extension, and every terminal close path (sold / passed / BIN / withdrawn). Settlement begins where this BC ends.

Two formats coexist (Workshop 002):

- **Timed** — one listing opens for bidding on publish; closes at `PublishedAt + Duration`.
- **Flash session** — ops staff create a `Session`, attach already-published listings, then start it. All attached listings open simultaneously through a fan-out (`SessionStartedHandler`) and close together.

## Module bootstrap (`AuctionsModule.cs`)

`services.AddAuctionsModule()` contributes to the shared `IDocumentStore` via `services.ConfigureMarten(...)`:

- **Documents (schema `auctions`):**
  - `AuctionClosingSaga` — `Identity(x => x.Id)`, `UseNumericRevisions(true)` (Wolverine `Saga` document)
  - `ProxyBidManagerSaga` — `Identity(x => x.Id)`, `UseNumericRevisions(true)` (Wolverine `Saga` document)
  - `ParticipantCreditCeiling` — natural-key-as-id Marten document (M4-D4 duplicate-projection pattern)
  - `PublishedListings` — natural-key-as-id Marten document (M4-D4 duplicate-projection pattern, OQ1 Path A full payload)
  - `Listing` — `LiveStreamAggregation<Listing>()`
  - `Session` — `LiveStreamAggregation<Session>()`
- **Event types registered:** `BiddingOpened`, `BidPlaced`, `BidRejected`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved`, `BuyItNowPurchased`, `ListingWithdrawn`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`. Outcome events `BiddingClosed` / `ListingSold` / `ListingPassed` are intentionally not registered — they cascade through `OutgoingMessages` from saga handlers, never appended to a Marten stream (`AuctionsModule.cs:87-90`).
- **DCB tag type:** `opts.Events.RegisterTagType<ListingStreamId>("listing").ForAggregate<BidConsistencyState>()` (`AuctionsModule.cs:113`). `ListingStreamId` is a record wrapper around `Guid` — direct `Guid` registration breaks `ValueTypeInfo.ForType` because .NET 10 added `Variant`/`Version` public Guid properties (`ListingStreamId.cs:5-9`).
- **Retry policies (`AuctionsConcurrencyRetryPolicies`, `AuctionsModule.cs:137-155`):**
  - `ConcurrencyException` → cooldown 100ms, 250ms (saga writes)
  - `DcbConcurrencyException` → cooldown 100ms, 250ms (DCB writes; sibling to ConcurrencyException, not parent/child)
  - `ParticipantCreditCeilingNotFoundException` → cooldown 100ms, 250ms, 500ms (projection-lag race)

## Aggregates and projections

### `Listing` aggregate (`Listing.cs`)
Live-aggregated event-sourced model of a listing's auction state. Stream id is the UUID v7 flowed through from Selling's `ListingPublished`. Currently has one `Apply` method — `Apply(BiddingOpened)` — that seeds full state (starting bid, reserve, BIN, scheduled close, extension config, max duration). `Apply` for outcome events (`BiddingClosed` / `ListingSold` / `ListingPassed`) is documented as "S5 scope" but is not present in the read file. The aggregate is loaded by `AuctionClosingSaga.Handle(CloseAuction)` to read `SellerId` at close time because the saga's start handler doesn't capture it (`AuctionClosingSaga.cs:106-107`).

### `Session` aggregate (`Session.cs`)
Live-aggregated sealed record. Three events on its stream: `SessionCreated` (via `static Create`), `ListingAttachedToSession` (`Apply` returns new instance via `with`), `SessionStarted` (sets `StartedAt`). Stream id UUID v7 per M4-D2. `Title` not unique. Lifecycle is one-shot: sessions do not unstart, pause, or cancel after start (M4 non-goals).

### `BidConsistencyState` (DCB boundary model, `BidConsistencyState.cs`)
The Dynamic Consistency Boundary's tag-aggregate for bid-acceptance decisions. Projected from `BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased` — `BidRejected` is excluded by type from the `EventTagQuery` (W002-7 decision, `BidConsistencyState.cs:11`). Has a public `Id` property even though Wolverine University's DCB example omits one — Marten 8 treats the tag-aggregate as a document once `RegisterTagType.ForAggregate` wires it, so fixture cleanup throws `InvalidDocumentException` without an `Id` (M3-S4 OQ2 empirical answer, `BidConsistencyState.cs:14-17`).

### `ParticipantCreditCeiling` (`ParticipantCreditCeiling.cs` + handler)
Auctions-local cache of per-participant credit ceilings, projected from `ParticipantSessionStarted` on the `auctions-participants-events` queue. Read by `StartProxyBidManagerSagaHandler` to populate `ProxyBidManagerSaga.BidderCreditCeiling`. Immutable after first creation; re-delivery preserves existing row. **M4-D4 duplicate-projection pattern, second lived application** (first is Settlement's `BidderCreditView`).

### `PublishedListings` (`PublishedListings.cs` + handler)
Auctions-local cache of Selling's listing publish payload, projected from `ListingPublished` and `ListingWithdrawn` on the `auctions-selling-events` queue. **OQ1 Path A** — full BiddingOpened-precursor payload (SellerId, StartingBid, ReservePrice, BuyItNowPrice, extended-bidding fields, etc.) rather than minimal status-only. Two consumers within Auctions:
1. `AttachListingToSessionHandler` — Workshop 002 §5.3 reject-not-published check.
2. `SessionStartedHandler` — per-listing `BiddingOpened` payload for the fan-out.

Status enum (`PublishedListingsStatus.cs`): `Published`, `Withdrawn`. Withdrawn is absorbing — re-delivered `ListingPublished` on a Withdrawn row preserves terminal state.

### `BidRejectionAudit` (`BidRejected.cs:36`)
Stream-type marker for the per-listing rejection audit log. Stream key derived by Guid XOR (not UUID v5 — "a cryptographic UUID v5 would be overkill for a single-domain, fixed-prefix derivation", `BidRejected.cs:33`). Not projected to a live aggregate.

## Commands

Internal commands (consumed within Auctions BC only):

| Command | Purpose | Handler |
| --- | --- | --- |
| `PlaceBid` | Submit a bid on a listing | `PlaceBidHandler` (DCB) |
| `BuyNow` | Exercise Buy It Now on a listing | `BuyNowHandler` (DCB) |
| `CreateSession` | Create a new Flash session aggregate | `CreateSessionHandler` |
| `AttachListingToSession` | Attach a published listing to a not-yet-started session | `AttachListingToSessionHandler` |
| `StartSession` | Start a Flash session — opens all attached listings | `StartSessionHandler` |
| `CloseAuction` (scheduled) | Scheduled close signal dispatched by the saga | `AuctionClosingSaga.Handle(CloseAuction)` |
| `ProxyBidObserved` (dispatcher) | Wrapped per-saga targeting of `BidPlaced` | `ProxyBidManagerSaga.Handle(ProxyBidObserved)` |
| `ProxyListingSoldObserved` / `ProxyListingPassedObserved` / `ProxyListingWithdrawnObserved` | Wrapped per-saga targeting of terminal events | `ProxyBidManagerSaga` (three handlers) |

Contract commands (from `CritterBids.Contracts.Auctions.*` — cross-BC dispatch):

| Command | Purpose | Handler |
| --- | --- | --- |
| `RegisterProxyBid` | Register a max-amount proxy on a listing | `StartProxyBidManagerSagaHandler` |

## Handlers (non-saga)

| Handler | Trigger | Effect |
| --- | --- | --- |
| `ListingPublishedHandler` | `ListingPublished` (queue `auctions-selling-events`) | For Timed listings only: opens Listing event stream with `BiddingOpened` (idempotent via stream-state pre-query). Flash listings (Duration null) are skipped. |
| `PublishedListingsHandler` | `ListingPublished` + `ListingWithdrawn` | Tolerant-upsert of the `PublishedListings` cache. Terminal-status preserving on re-delivery; lazy-init at Withdrawn if no prior Published row. |
| `ParticipantCreditCeilingHandler` | `ParticipantSessionStarted` (queue `auctions-participants-events`) | Tolerant-upsert of `ParticipantCreditCeiling`. Existing rows preserved verbatim — `RegisteredAt` and `CreditCeiling` never overwritten. |
| `SessionStartedHandler` | `SessionStarted` | Flash fan-out: one `BiddingOpened` per attached listing. Loads `Session` aggregate for `DurationMinutes` (OQ5 Path B). Pre-queries each listing's stream state for idempotency (OQ2 — milestone doc's "DCB-primary" framing conflated two mechanisms; stream-existence idempotency is what actually applies at open time). `MaxDuration = DurationMinutes * 2` (Workshop 002 platform default). |
| `ProxyBidDispatchHandler` | `BidPlaced`, `ListingSold`, `ListingPassed`, `ListingWithdrawn` | Bridges composite-key correlation to property-pull. Queries active `ProxyBidManagerSaga` documents on the listing; emits one wrapped `ProxyBidObserved` / `ProxyListing*Observed` per match. Empty fan-out (common case) emits nothing. |

## Sagas

### `AuctionClosingSaga` (`AuctionClosingSaga.cs`, `Wolverine.Saga`)

**Correlation:** `Id == ListingId` (M3-S5 OQ1 Path A). Each handler uses `[SagaIdentityFrom(nameof(X.ListingId))]` so contracts stay unchanged. Started by `StartAuctionClosingSagaHandler` on `BiddingOpened`.

**State (`AuctionClosingStatus`):** `AwaitingBids`, `Active`, `Extended`, `Closing`, `Resolved`.

**Behavior:**
- On `BiddingOpened` (start) — saga document created at `AwaitingBids`; schedules `CloseAuction` at `ScheduledCloseAt` via `bus.ScheduleAsync`.
- On `BidPlaced` — idempotent via `message.BidCount <= BidCount` monotone guard. Transitions `AwaitingBids → Active`.
- On `ReserveMet` — sets `ReserveHasBeenMet = true`. Idempotent by set-to-true.
- On `ExtendedBiddingTriggered` — cancels pending `CloseAuction` via `IMessageStore.ScheduledMessages.CancelAsync` (narrow ±100ms window keyed on `MessageType + ExecutionTime` to avoid cross-listing cancels, `AuctionClosingSaga.cs:191-208`). Reschedules at `NewCloseAt`. Transitions to `Extended`.
- On `CloseAuction` (scheduled) — terminal evaluation: emits `BiddingClosed`, then `ListingSold` (bids and reserve met) / `ListingPassed("ReserveNotMet", ...)` (bids but reserve not met) / `ListingPassed("NoBids", ...)`. Loads `Listing` aggregate to read `SellerId` for `ListingSold`. Calls `MarkCompleted()`.
- On `BuyItNowPurchased` — terminal; cancels pending `CloseAuction`; `MarkCompleted()`. No outcome cascade.
- On `ListingWithdrawn` — terminal; cancels pending `CloseAuction`; `MarkCompleted()`. No outcome cascade.
- **`public static OutgoingMessages NotFound(CloseAuction message) => new()`** — Wolverine convention; absorbs `CloseAuction` arrivals after `MarkCompleted()` deleted the saga document (`AuctionClosingSaga.cs:142-146`).

### `ProxyBidManagerSaga` (`ProxyBidManagerSaga.cs`)

**Correlation (M4-S3 OQ1 Path C — composite key):** `Id = UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")` via `AuctionsIdentityHelpers.ProxyBidManagerSagaId`. Wolverine's `[SagaIdentityFrom]` only resolves a Guid property by name — neither Path A (resolver) nor Path B (add field to `BidPlaced`) was viable, hence the `ProxyBidDispatchHandler` bridge. Started by `StartProxyBidManagerSagaHandler` on `RegisterProxyBid`.

**State (`ProxyBidManagerStatus`):** `Active`, `Exhausted`, `ListingClosed`.

**Behavior:**
- On `RegisterProxyBid` (start) — composite-key dedup via `LoadAsync`. Loads `ParticipantCreditCeiling`; throws `ParticipantCreditCeilingNotFoundException` if absent (retry policy re-queues). Emits `ProxyBidRegistered`.
- On `ProxyBidObserved` (own-bid: `BidderId == message.BidderId`) — monotone tracking of `LastBidAmount` if strictly higher. No further emissions.
- On `ProxyBidObserved` (competing bid) — compute `increment = message.Amount >= 100m ? 5m : 1m` (Workshop 002 — third co-located copy of the increment ladder alongside `PlaceBidHandler.cs:174-175`; CLAUDE.md's "three similar lines is better than a premature abstraction" rule). Compute `capped = min(message.Amount + increment, MaxAmount, BidderCreditCeiling)`. If `capped <= message.Amount` → exhaustion (emit `ProxyBidExhausted`, transition to `Exhausted`, `MarkCompleted()`). Otherwise emit `PlaceBid` at `capped` with `IsProxy: true` (`ProxyBidManagerSaga.cs:124-130`).
- On `ProxyListingSoldObserved` / `ProxyListingPassedObserved` / `ProxyListingWithdrawnObserved` — terminal; transition to `ListingClosed`; `MarkCompleted()`. Each has a `public static OutgoingMessages NotFound(...) => new()` absorber.

## DCB and bid-acceptance decision (`PlaceBidHandler.cs`)

The DCB handler explicitly does **not** use the canonical `[BoundaryModel]` auto-append path because contract events carry `Guid ListingId`, not `ListingStreamId ListingTag` — the project refuses to leak the tag wrapper into `CritterBids.Contracts.Auctions.*` (`PlaceBidHandler.cs:10-15`). Instead the handler:

1. `IDocumentSession.Events.FetchForWritingByTags<BidConsistencyState>(query)` — returns aggregate AND queues `AssertDcbConsistency` on the session.
2. On rejection, appends `BidRejected` to the per-listing `BidRejectionAudit` stream (separate stream — never on the listing's primary stream).
3. On acceptance, `session.Events.BuildEvent` per acceptance event, `wrapped.AddTag(new ListingStreamId(...))`, `session.Events.Append(listingId, wrapped)`.

Rejection reason vocabulary (`PlaceBidHandler.EvaluateRejection`): `ListingNotOpen`, `ListingClosed`, `SellerCannotBid`, `ExceedsCreditCeiling`, `BelowMinimumBid`. BIN-handler shares the reason set and adds `BuyItNowNotAvailable` (`BuyNowHandler.EvaluateRejection`).

Acceptance-path events (`PlaceBidHandler.AcceptanceEvents`): `BidPlaced` (always); `BuyItNowOptionRemoved` (if BIN was available); `ReserveMet` (if reserve threshold crossed this bid); `ExtendedBiddingTriggered` (if `TryComputeExtension` returns true).

**Extended-bidding math (`PlaceBidHandler.TryComputeExtension`):** Anchored to `state.ScheduledCloseAt + extension` (not `now + extension` — that would shorten early-window bids). Capped at `state.OriginalCloseAt + state.MaxDuration`. Monotone-by-construction (`NewCloseAt > PreviousCloseAt` for every reachable producer).

## Integration events published

Published via Wolverine outbox to RabbitMQ queue `listings-auctions-events` (per Program.cs lines 67-89):

- `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`.

Also published to other queues from Auctions BC outputs:

- `ListingSold` + `BuyItNowPurchased` + `ListingPassed` → `settlement-auctions-events` (Program.cs:114-120).

Cross-BC-but-no-current-consumer (queue established but consumer is post-M5):

- `ProxyBidRegistered`, `ProxyBidExhausted` — Relay (post-M5). Currently land in `tracked.NoRoutes` (no `PublishMessage` route wired yet — confirmed by reading Program.cs).

## Identifiers

- **Stream ids:** UUID v7 (`Listing` stream id flows through from Selling; `Session` stream id minted in `CreateSessionHandler.cs:35` via `Guid.CreateVersion7()`).
- **`AuctionClosingSaga.Id` == `ListingId`** (M3-S5 OQ1 Path A).
- **`ProxyBidManagerSaga.Id` == UUID v5(`AuctionsIdentityNamespaces.ProxyBidManagerSaga`, `$"{ListingId}:{BidderId}"`)** (M4-D1 — composite key from Workshop 002 §4.1 verbatim string form).
- **`BidRejectionAudit` stream key** — Guid XOR of `(listingId, fixed namespace)` (`BidRejected.cs:42-50`); cryptographic UUID v5 deemed overkill for a single-domain derivation.

## Tests (`tests/CritterBids.Auctions.Tests/`)

`AttachListingToSessionDispatchTests`, `AuctionClosingSagaTests`, `AuctionsModuleTests`, `BiddingOpenedConsumerTests`, `BuyNowDispatchTests`, `BuyNowHandlerTests`, `CreateSessionDispatchTests`, `ParticipantCreditCeilingProjectionTests`, `PlaceBidDispatchTests`, `PlaceBidHandlerTests`, `ProxyBidManagerSagaTests`, `PublishedListingsProjectionTests`, `RealSellingProducerSagaTerminationTests`, `RegisterProxyBidDispatchTests`, `SessionAggregateTests`, `SessionStartedFanOutTests`, `StartSessionDispatchTests`.

## Notable internal contracts and conventions

- `CloseAuction` (saga-internal scheduled command) is `public` for C# accessibility — Wolverine's discovery scans only public Handle methods, and the saga's `public Handle(CloseAuction)` requires at-least-as-accessible parameters. The architectural boundary holds via the project reference graph, not C# visibility (`CloseAuction.cs:8-11`).
- `ProxyBidObserved` is similarly `public` for the same reason (`ProxyBidObserved.cs:21-24`).
- All saga terminal handlers carry a static `NotFound(Cmd)` absorber alongside the regular `Handle(Cmd)` so post-`MarkCompleted` redeliveries don't throw `UnknownSagaException`. Pattern lifted from `AuctionClosingSaga.NotFound(CloseAuction)` originally (`AuctionClosingSaga.cs:142-146`).
- Both sagas use `Status` field guards before mutating — Wolverine inbox dedup should prevent re-delivery but the guards are the correctness contract if dedup fails (consistent comment shape across both sagas).

## Open questions / fixture-stance items captured

- M4-S3 OQ4 was deferred to M4-S4 — resolved by adding `ParticipantCreditCeiling` projection + `StartProxyBidManagerSagaHandler` lookup with retry policy. Already lived.
- `ProxyBidRegistered` / `ProxyBidExhausted` lack a `PublishMessage` route in Program.cs at M5 close — events land in `tracked.NoRoutes`. Consumer is Relay (post-M5).
- `Listing` aggregate has only `Apply(BiddingOpened)` in source as of this read — `Apply` for outcome events documented as S5-scope is not present. Outcome events cascade via saga `OutgoingMessages` only, never appended to a Marten stream (`AuctionsModule.cs:87-90`).
