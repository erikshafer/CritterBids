# Glossary

The CritterBids ubiquitous language. Each entry: definition in the auction domain, where it is honored in code (cited), and — where applicable — a cross-reference to the gap register (Phase 4) when code or docs drift from the term.

Linguistic conventions (the "paddle" prohibition, the no-`Event`-suffix rule, the `BiddingClosed` vs `ListingSold`/`ListingPassed` distinction) are captured as glossary entries because they govern speech, not just code.

---

## Aggregate identifier rule

Aggregate ID is always the first property on every event record. Carried in `docs/vision/domain-events.md:130`. Enforced by inspection — no compile-time guard. Honored in code: `src/CritterBids.Contracts/Auctions/BidPlaced.cs`, `src/CritterBids.Contracts/Selling/ListingPublished.cs`, every internal event under `src/CritterBids.<BC>/`.

## Aggregate root

A Marten event-sourced type loaded via `FetchForWriting` (single-stream) or `FetchForWritingByTags` (DCB). Five exist: `Participant` (`src/CritterBids.Participants/Participant.cs`), `SellerListing` (`src/CritterBids.Selling/SellerListing.cs`), `Listing` (`src/CritterBids.Auctions/Listing.cs`), `Session` (`src/CritterBids.Auctions/Session.cs`), and the financial event stream (no in-memory aggregate; raw event stream rehydration only — `src/CritterBids.Settlement/SettlementSaga.cs:67-86`).

## Anti-snipe

Synonym for [Extended Bidding](#extended-bidding).

## Approved (listing status)

A transient state on the `SellerListing` aggregate's `ListingStatus` enum (`src/CritterBids.Selling/ListingStatus.cs:13`). In MVP, the submit handler chain produces `ListingSubmitted + ListingApproved + ListingPublished` atomically — `Approved` is observed in the event log but never as a resting state. Kept as a real enum value so a post-MVP migration to manual approval is a handler split, not an event-vocabulary change (`src/CritterBids.Selling/SubmitListingHandler.cs:54-56`).

## Auction Closing saga

The Wolverine saga that owns one timed-listing's close lifecycle. States: `AwaitingBids → Active → (Extended) → Resolved`. Identity = `ListingId`. Schedules and re-schedules the `CloseAuction` durable message; emits `BiddingClosed`, `ListingSold` or `ListingPassed` at terminal evaluation; absorbs `BuyItNowPurchased` and `ListingWithdrawn` as alternate terminations. Source: `src/CritterBids.Auctions/AuctionClosingSaga.cs`. See [`workflows/timed-listing-close.md`](./workflows/timed-listing-close.md).

## AutoApplyTransactions

Wolverine policy that wraps each handler in an implicit transaction so handlers can return `Events`/`OutgoingMessages` without calling `SaveChangesAsync()`. Configured once globally in `src/CritterBids.Api/Program.cs:184` (`opts.Policies.AutoApplyTransactions()`). See also: `gaps-and-drift.md` entry on `.github/copilot-instructions.md` claiming this is registered per-BC.

## Banker's rounding

`MidpointRounding.ToEven` used for all monetary rounding. `Math.Round(HammerPrice * FeePercentage, 2, MidpointRounding.ToEven)` in fee calculation. Source: `src/CritterBids.Settlement/SettlementSaga.cs`.

## BidConsistencyState

The DCB tag aggregate that gates bid acceptance. Lives at `src/CritterBids.Auctions/BidConsistencyState.cs`. Aggregates over six event types projected from the listing stream (`BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`) keyed by the `ListingStreamId` tag. Loaded by `PlaceBidHandler` and `BuyNowHandler` via `FetchForWritingByTags<BidConsistencyState>`.

## BidderId

A human-readable string ("Bidder N") generated at session start and carried on every bid for display. **Not** the cross-BC participant identifier — that is `ParticipantId` (Guid). The `BidderId` field exists on `ParticipantSessionStarted`, `BidPlaced`, `BidRejected`, and similar events for UI rendering only. See `gaps-and-drift.md` — vision doc claims `BidderId` is "the participant's identifier across all BCs" but code routes cross-BC lookups (`BidderCreditView`, `ParticipantCreditCeiling`) on `ParticipantId`. Source: `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs:33-37`.

## Bidding (settlement source)

Value of the `Source` field on `SettlementInitiated` when settlement was triggered by `ListingSold`. The settlement state machine begins at `Initiated` and proceeds through `ReserveCheckCompleted` before reaching `WinnerCharged`. Compare with [Buy It Now](#buy-it-now-bin) (source). Source: `src/CritterBids.Settlement/SettlementSaga.cs:67-98`.

## BiddingClosed (mechanical close)

The internal event the Auction Closing saga emits when scheduled close time arrives. Intentionally distinct from the [business outcome](#listingsold) events `ListingSold` and `ListingPassed`. "A consumer that only cares about 'bids no longer accepted' has a single type to subscribe to" — `src/CritterBids.Contracts/Auctions/BiddingClosed.cs:7-12`. Not emitted on the BIN terminal path. Honored in code: `src/CritterBids.Auctions/AuctionClosingSaga.cs:96-99`.

## BiddingOpened

The integration event the system emits when a listing becomes accepting of bids. For Timed listings, emitted from `ListingPublishedHandler` immediately on publish. For Flash listings, emitted from `SessionStartedHandler` for each attached listing when the session starts. Carries `ListingFormat` and `ScheduledCloseAt`. Source: `src/CritterBids.Contracts/Auctions/BiddingOpened.cs`.

## BidderCreditView

The local Settlement projection of `ParticipantId → RemainingCredit`, seeded by `ParticipantSessionStarted` and debited by `WinnerCharged`. Lazy-init with negative-sentinel pattern when `WinnerCharged` arrives before the seeding event. First lived application of the M4-D4 duplicate-projection pattern. Source: `src/CritterBids.Settlement/BidderCreditViewHandler.cs`.

## Bid increment ladder

Tier rule: `$1 < $100; $5 ≥ $100`. Co-located in three places per CLAUDE.md "three is better than premature abstraction": `src/CritterBids.Auctions/PlaceBidHandler.cs:174-175`, `BuyNowHandler` (minimum-bid calc), and `src/CritterBids.Auctions/ProxyBidManagerSaga.cs:88-89`.

## BidRejected

An integration event emitted when a bid attempt fails for one of 5 reasons (`ListingNotOpen`, `ListingClosed`, `BelowMinimum`, `ExceedsCreditCeiling`, `BuyItNowNotAvailable`). Stored in a per-listing `BidRejectionAudit` stream, separate from the listing's primary stream, so the DCB tag query stays lean. Source: `src/CritterBids.Auctions/PlaceBidHandler.cs:81-103`. The `BuyNow` rejection path reuses both the event and the audit stream (`src/CritterBids.Auctions/BuyNowHandler.cs:99-123`).

## Buy It Now (BIN)

Terminal purchase path that bypasses the auction. A `BuyNow(ListingId, BuyerId, CreditCeiling)` command emits `BuyItNowPurchased`, which both terminates the [Auction Closing saga](#auction-closing-saga) and triggers Settlement at `Source: BuyItNow`. BIN settlements start the financial event stream at `ReserveChecked(WasMet: true)` — `ReserveCheckCompleted` is **never** appended to a BIN settlement stream. The absence is the [meaningful event absence](#meaningful-event-absence) audit signal for "this was a BIN purchase." Source: `src/CritterBids.Auctions/BuyNow.cs`, `src/CritterBids.Auctions/BuyNowHandler.cs`, `src/CritterBids.Settlement/StartSettlementSagaHandler.cs:100-164`.

## Buy It Now (settlement source)

Value of the `Source` field on `SettlementInitiated` when settlement was triggered by `BuyItNowPurchased`. Distinct from [Bidding](#bidding-settlement-source) in that no `ReserveCheckCompleted` is appended. Source: `src/CritterBids.Settlement/StartSettlementSagaHandler.cs:142-153`.

## BuyItNowOptionRemoved

Integration event emitted by `PlaceBidHandler` when the first accepted bid lands on a listing that had a BIN price set. After this event, BIN attempts are rejected. Honored in code: `src/CritterBids.Auctions/PlaceBidHandler.cs:119-120`. Applied to `BidConsistencyState` to lock out subsequent BIN attempts via DCB.

## CatalogListingView

The single Marten document the [Listings BC](./bcs/listings.md) maintains per listing. Aggregates state from Selling (`ListingPublished`, `ListingWithdrawn`), Auctions (`BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, `ListingAttachedToSession`, `SessionStarted`), and Settlement (`SettlementCompleted`). `Status` field is a string state machine: `Published → Open → Closed → Sold → Settled` (bidding sold path), `Published → Open → Sold → Settled` (BIN), `Published → Open → (Closed →) Passed`, or `Published/Open → Withdrawn`. Source: `src/CritterBids.Listings/CatalogListingView.cs`.

## CloseAuction

Scheduled command stored in Wolverine's durable scheduled-message store. Created by `StartAuctionClosingSagaHandler` on `BiddingOpened`. Cancellable and re-schedulable via narrow ±100ms-window query on `MessageType + ExecutionTime` (`src/CritterBids.Auctions/AuctionClosingSaga.cs:191-208`). Re-scheduled by `Handle(ExtendedBiddingTriggered)`. Consumed terminally by `Handle(CloseAuction)`. Has a `NotFound` absorber for post-`MarkCompleted` redeliveries.

## Closing saga

See [Auction Closing saga](#auction-closing-saga).

## Composite-key saga correlation

The Proxy Bid Manager saga's identity is `UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")` — a derived Guid that no contract event carries directly. Wolverine's `[SagaIdentityFrom]` cannot route to this id, so a dedicated `ProxyBidDispatchHandler` queries active sagas by `ListingId` and fans out wrapped events (`ProxyBidObserved`, `ProxyListingSoldObserved`, etc.) that carry the resolved `SagaId`. M4-S3 OQ1 Path C. Source: `src/CritterBids.Auctions/AuctionsIdentityHelpers.cs:14-22`, `ProxyBidDispatchHandler.cs`.

## Credit ceiling

The hidden per-bid maximum generated at session start (random 200..1000, 100-step). Bids and BIN purchases above the participant's ceiling are rejected with `ExceedsCreditCeiling`. The ceiling is a per-bid cap, **not** a running balance — `WinnerCharged` debits `BidderCreditView.RemainingCredit` but does not feed back into the ceiling check. Source: `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs`, `src/CritterBids.Auctions/PlaceBidHandler.cs:142-172`.

## DCB — Dynamic Consistency Boundary

A pattern for evaluating consistency across event streams without owning a single aggregate stream. Implemented via `FetchForWritingByTags<TagAggregate>` + `EventTagQuery`. The only DCB in CritterBids is `BidConsistencyState`, which gates bid acceptance per listing. Source: `src/CritterBids.Auctions/BidConsistencyState.cs`, `ListingStreamId.cs`.

## Decider pattern

Workflow modeling principle from W003 — design around decider semantics regardless of hosting (saga, projection, handler). The Settlement workflow is a 7-state decider hosted as a Wolverine saga but designed independent of that hosting choice.

## Display name

Generated string for participants (adjective × animal × 1-9999). UX-facing identity; no uniqueness guarantee across active sessions other than display-distinct via the random suffix. Source: `src/CritterBids.Participants/Features/StartParticipantSession/`.

## Domain event vocabulary

The canonical list of internal (🟠) and integration (🔵) events. Lives at `docs/vision/domain-events.md`. Drift candidates are recorded in `gaps-and-drift.md`.

## Draft (listing status)

Initial state of a `SellerListing` aggregate after `DraftListingCreated`. Mutable via `UpdateDraftListing` (Title, ReservePrice, BuyItNowPrice only). Terminal transitions: `Draft → Submitted` (via `SubmitListing`). Source: `src/CritterBids.Selling/ListingStatus.cs`.

## DraftListingCreated / DraftListingUpdated

Internal Selling events. `DraftListingCreated` carries the full 11-field listing payload; `DraftListingUpdated` is nullable-field and only updates `Title`, `ReservePrice`, `BuyItNowPrice` per validator rules. Source: `src/CritterBids.Selling/DraftListingCreated.cs`, `DraftListingUpdated.cs`.

## Event forwarding (UseFastEventForwarding)

Marten + Wolverine configuration (`UseFastEventForwarding = true` in `src/CritterBids.Api/Program.cs:193`) that forwards aggregate-stream events as in-process Wolverine messages. Enables `StartAuctionClosingSagaHandler.Handle(BiddingOpened)` and `SessionStartedHandler.Handle(SessionStarted)` to react to events without explicit `IMessageBus.PublishAsync` calls.

## Extended bidding

Anti-snipe timer extension. When a bid arrives within the per-listing `TriggerWindow` before close, `ExtendedBiddingTriggered` is emitted and the `CloseAuction` is rescheduled. Subject to a per-listing `MaxDuration` cap (platform default in M3-M5: `Duration * 2`; Flash default: `DurationMinutes * 2`). The `AuctionClosingSaga` transitions to `Extended` state. Source: `src/CritterBids.Auctions/AuctionClosingSaga.cs:66-82`, `SessionStartedHandler.cs:81-85`.

## FeePercentage

The platform's final-value fee rate captured into `ListingPublished` at publish time. Vision text says "captured from platform config at publish time and fixed for the life of the listing" (`docs/vision/domain-events.md:34`). **Code drift:** hardcoded to `0.10m` in `src/CritterBids.Selling/SubmitListingHandler.cs:70` ("M5 placeholder — no fee engine exists yet"). The contract event still carries the field. Recorded in `gaps-and-drift.md`.

## Final Value Fee (FVF)

The platform fee charged to the seller. Computed as `HammerPrice * FeePercentage`, rounded with [banker's rounding](#bankers-rounding) to 2 decimal places. Always charged to the seller, never the buyer. Source: `src/CritterBids.Settlement/SettlementSaga.cs` (FinalValueFeeCalculated handler).

## Flash (listing format)

Listing format with `Duration: null`. Requires session attachment via `AttachListingToSession` before bidding can open. All Flash listings in the same session share `ScheduledCloseAt = StartedAt + DurationMinutes`. Compare with [Timed](#timed-listing-format). Source: `src/CritterBids.Contracts/Selling/ListingFormat.cs`, [`workflows/flash-session.md`](./workflows/flash-session.md).

## Flash Session

See [Sale / Flash Session](#sale--flash-session).

## Hammer Price

The final winning-bid price at close, before any fees. Equals `BuyItNowPrice` for BIN settlements (carried as `Price` on `BuyItNowPurchased`). Equals the high bid for bidding settlements (carried as `HammerPrice` on `ListingSold`). Source: `src/CritterBids.Contracts/Auctions/ListingSold.cs`, `BuyItNowPurchased.cs`.

## Integration event (🔵)

A domain event published to `src/CritterBids.Contracts/` and routed across BC boundaries via the Wolverine message bus (RabbitMQ in production wiring, in-process in tests). Source: `docs/vision/domain-events.md:6`.

## Internal event (🟠)

A domain event scoped to a single BC's `CritterBids.<BC>` namespace; not published to `CritterBids.Contracts` and not crossing BC boundaries. Source: `docs/vision/domain-events.md:5`.

## Listing

The thing being auctioned. The primary domain noun, owned at draft/lifecycle level by [Selling](./bcs/selling.md) (`SellerListing` aggregate), at bidding level by [Auctions](./bcs/auctions.md) (`Listing` aggregate), at catalog level by [Listings](./bcs/listings.md) (`CatalogListingView`), and at financial level by [Settlement](./bcs/settlement.md) (`PendingSettlement`). The vocabulary deliberately avoids "lot" in public-facing contexts.

## ListingFormat

Enum `{Timed, Flash}`. Chosen at draft creation and immutable thereafter. Drives whether bidding opens immediately on publish (Timed) or only on session start (Flash). Source: `src/CritterBids.Contracts/Selling/ListingFormat.cs`.

## ListingPublished

The load-bearing integration event for the entire system. Published from Selling's `SubmitListingHandler` via `OutgoingMessages`. Consumed by 4 sibling handlers: Listings (`ListingPublishedHandler` — catalog seed), Auctions (`ListingPublishedHandler` — Timed open stream; `PublishedListingsHandler` — projection cache), Settlement (`PendingSettlementHandler` — seed Pending row). Vision text: `docs/vision/domain-events.md:34`. Source: `src/CritterBids.Contracts/Selling/ListingPublished.cs`.

## ListingPassed

Business outcome event for an auction that closed without producing a sale. Reasons: `NoBids` (zero bids) or `ReserveNotMet` (bids existed but high bid was below reserve). Settlement marks `PendingSettlement.Status = Expired` (no settlement runs). Distinct from `ListingSold`. Source: `src/CritterBids.Contracts/Auctions/ListingPassed.cs`.

## ListingSold

Business outcome event for an auction that closed with a winner who met reserve. Triggers Settlement at `Source: Bidding`. Distinct from `BiddingClosed` (mechanical close) and from `BuyItNowPurchased` (which has its own terminal contract event). Source: `src/CritterBids.Contracts/Auctions/ListingSold.cs`.

## ListingWithdrawn

Integration event for a published listing being terminated by the seller. Carries `ListingId`, `WithdrawnBy`, `Reason?` (null in M4), `WithdrawnAt`. Both Selling (`SellerListing.WithdrawListing` command) and Auctions (`AuctionClosingSaga.Handle(ListingWithdrawn)`) honor it. Vision-doc distinction from `ListingEndedEarly` (seller-initiated vs ops-initiated) is **not implemented** — only `ListingWithdrawn` exists in code. Source: `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs`.

## Lot

A word the public-facing vocabulary deliberately avoids in favor of [Listing](#listing). Internal stream names (`BidRejectionAudit` *for a listing*) and DCB tag wrappers (`ListingStreamId`) do not contain "lot". The vision-doc event names `LotWatchAdded` / `LotWatchRemoved` are the only places it surfaces, and those events are not implemented (`gaps-and-drift.md`).

## MarkCompleted

Wolverine saga API call that deletes the saga document. All terminal handlers on `AuctionClosingSaga`, `ProxyBidManagerSaga`, and `SettlementSaga` call `MarkCompleted()` on their final transition. All terminal-event handlers have a paired `public static NotFound(X) => new()` absorber for post-`MarkCompleted` redeliveries.

## Meaningful event absence

Naming convention from `docs/vision/domain-events.md:135`: some patterns rely on what's NOT in a stream. Canonical example: the absence of `ReserveCheckCompleted` from a settlement's financial event stream identifies it as a Buy It Now purchase (Settlement's BIN overload starts the saga directly at `ReserveChecked` and never appends the check event). Honored in code: `src/CritterBids.Settlement/StartSettlementSagaHandler.cs:100-164` and §9.2 audit guarantee.

## MessageIdentity

Wolverine inbox dedup pattern. CritterBids uses `MessageIdentity.IdAndDestination` (set per consumer in `Program.cs` RabbitMQ wiring) so the same logical message arriving on different sticky queues is deduped per (id, queue) tuple — required when `MultipleHandlerBehavior.Separated` puts sibling handlers on distinct queues.

## MultipleHandlerBehavior.Separated

Wolverine setting (`opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated` in `Program.cs:20`) that gives each handler for the same message type its own sticky queue. Required for cross-BC fan-out where one event has multiple distinct consumers. Test consequence: dispatch via the bus must use `SendMessageAndWaitAsync`, not `InvokeMessageAndWaitAsync`.

## No-Event-suffix rule

Naming convention: event types are nouns/past participles, never with an `Event` suffix. `ListingSold` not `ListingSoldEvent`. Source: `docs/vision/domain-events.md:129`. Honored universally in `src/`.

## OPEN-QUESTIONS

The corpus-level register of items the extraction could not resolve from code. Lives at `docs/extraction/OPEN-QUESTIONS.md`. Each entry: source artifact, question, what could not be reconciled.

## OutgoingMessages

Wolverine return type for a handler. Messages added via `OutgoingMessages` ride the outbox transactionally with any aggregate appends in the same handler. Integration events MUST be published this way; direct `IMessageBus.PublishAsync` is forbidden in handlers (CLAUDE.md non-negotiable). The one allowed `IMessageBus` use in a handler is `ScheduleAsync` for durable scheduled messages.

## Paddle

A word the CritterBids vocabulary **forbids**. Use [BidderId](#bidderid) for display, [ParticipantId](#participantid) for cross-BC identity. CLAUDE.md non-negotiable. The repository grep returns zero hits in `src/` or `docs/`.

## ParticipantCreditCeiling

The local Auctions projection of `ParticipantId → CreditCeiling`, seeded by `ParticipantSessionStarted`. Used by `StartProxyBidManagerSagaHandler` for the cap calculation. Second instance of the M4-D4 duplicate-projection pattern (alongside Settlement's `BidderCreditView`). Source: `src/CritterBids.Auctions/ParticipantCreditCeilingHandler.cs`.

## ParticipantId

The Guid identifier for a participant. The **actual** cross-BC participant key — used by `BidderCreditView`, `ParticipantCreditCeiling`, and every cross-BC event that needs to address a participant. See `gaps-and-drift.md` — vision doc inverts and claims `BidderId` plays this role. Source: `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs`.

## ParticipantSessionStarted

Integration event published when a `Participant` aggregate is created. Forwarded via Marten's `UseFastEventForwarding`. Carries `ParticipantId`, `DisplayName`, `BidderId`, `CreditCeiling`, `StartedAt`. Consumed by Auctions (`ParticipantCreditCeilingHandler`) and Settlement (`BidderCreditViewHandler`). Source: `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs`.

## ParticipantSessionEnded

Vision-doc-declared integration event with **no implementation**. Declared at `docs/vision/bounded-contexts.md:29` and `docs/vision/domain-events.md:17`. No CLR type in `Contracts/Participants/`; no emitter; no consumer. Recorded in `gaps-and-drift.md`.

## PaymentFailed

Integration event emitted from `SettlementSaga` on the failure path. M5 only produces it for `Reason: "ReserveNotMet"` (defense-in-depth check against an Auctions-published `ListingSold` whose hammer price is below the cached reserve — theoretically unreachable). Carries both `SettlementId` and `ListingId` explicitly so consumers don't need a lookup. Post-MVP will introduce additional reasons. Routed to `operations-settlement-events` (no consumer wired yet). Source: `src/CritterBids.Contracts/Settlement/PaymentFailed.cs`.

## PendingSettlement

Settlement's projection of every published listing's financial cache. Built from `ListingPublished` (Status: Pending), transitions to `Consumed` on `SettlementCompleted`, `Failed` on `PaymentFailed`, `Expired` on `ListingPassed`/`ListingWithdrawn`. Source: `src/CritterBids.Settlement/PendingSettlement.cs`, `PendingSettlementHandler.cs`.

## PendingSettlementNotFoundException

Thrown by `StartSettlementSagaHandler` if `ListingSold` or `BuyItNowPurchased` arrives before the `ListingPublished` consumer has built the row. Retry policy backs off 100ms / 250ms / 500ms. In practice the race rarely fires because `ListingPublished` precedes terminal events by hours/days. Source: `src/CritterBids.Settlement/SettlementsConcurrencyRetryPolicies.cs:11-26`.

## Proxy Bid Manager saga

Per-bidder-per-listing saga that auto-bids on behalf of a registered participant up to a maximum. Composite-key correlation via `UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")`. Three states: `Active`, `Exhausted`, `ListingClosed`. Source: `src/CritterBids.Auctions/ProxyBidManagerSaga.cs`. See [`workflows/proxy-bidding.md`](./workflows/proxy-bidding.md).

## ProxyBidDispatchHandler

The correlation bridge for the Proxy Bid Manager saga. Subscribes to `BidPlaced`, `ListingSold`, `ListingPassed`, `ListingWithdrawn`; queries active sagas by `ListingId`; emits one wrapped `Proxy*Observed` event per active saga carrying the resolved `SagaId` for downstream `[SagaIdentityFrom]` routing. Source: `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs`.

## ProxyBidObserved / ProxyListingSoldObserved / ProxyListingPassedObserved / ProxyListingWithdrawnObserved

Internal Auctions wrapper events emitted by `ProxyBidDispatchHandler`. Each carries the saga's resolved `SagaId` so Wolverine can route to the correct `ProxyBidManagerSaga` document. Source: `src/CritterBids.Auctions/ProxyBidDispatchHandler.cs`.

## ProxyBidRegistered / ProxyBidExhausted

Audit-only events emitted by `ProxyBidManagerSaga`. Neither has a `PublishMessage` route in `Program.cs` — they land in `tracked.NoRoutes`. Intended consumer is Relay (post-M5). Source: `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs:79-85`, `ProxyBidManagerSaga.cs:103-120`.

## PublishedListings

Auctions' local projection caching the full listing-publish payload (SellerId, StartingBid, ReservePrice, BuyItNowPrice, extended-bidding config). Consulted by `AttachListingToSessionHandler` for the published-status guard and by `SessionStartedHandler` for the fan-out. M4-D4 duplicate-projection pattern; OQ1 Path A "full payload" shape. Source: `src/CritterBids.Auctions/PublishedListings.cs`, `PublishedListingsHandler.cs`.

## RabbitMQ routing

Integration events route to per-(consumer-BC × source-BC) queues following the convention `<consumer>-<source>-events`. Examples: `listings-auctions-events`, `settlement-selling-events`, `relay-settlement-events`. Wired in `src/CritterBids.Api/Program.cs:36-164`.

## RegisteredSeller

Single-field projection (`Id`) maintained by [Selling](./bcs/selling.md) from `SellerRegistrationCompleted`. Read by `ISellerRegistrationService.IsRegisteredAsync` to gate `CreateDraftListing`. Source: `src/CritterBids.Selling/RegisteredSeller.cs`, `SellerRegistrationCompletedHandler.cs`, `SellerRegistrationService.cs`.

## Reserve

The confidential minimum the seller is willing to accept. Never revealed to bidders (UX shows only "Reserve met!" / "Reserve not yet met"). Captured at draft creation; immutable after publish. Held in `BidConsistencyState`, `PublishedListings`, `PendingSettlement`. The 14-rule `ListingValidator` enforces `ReservePrice >= StartingBid` and `BuyItNowPrice >= ReservePrice` (`src/CritterBids.Selling/ListingValidator.cs:22-88`).

## ReserveMet (UX signal)

Integration event emitted by `PlaceBidHandler` atomically with `BidPlaced` when the bid amount first crosses the reserve threshold. Real-time signal for the "Reserve met!" UI badge. **Not authoritative for settlement** — distinct from [ReserveCheckCompleted](#reservecheckcompleted-binding-check). Source: `src/CritterBids.Auctions/PlaceBidHandler.cs:105-140`.

## ReserveCheckCompleted (binding check)

Internal Settlement event written to the financial event stream during phase 2 of the Settlement saga (bidding source only). The binding financial check. Carries `WasMet`. Never appended on BIN settlements — its absence is the [meaningful event absence](#meaningful-event-absence) signal. Source: `src/CritterBids.Settlement/SettlementSaga.cs`.

## Sale / Flash Session

The container for grouped Flash-format listings. The `Session` aggregate (`src/CritterBids.Auctions/Session.cs`) is created via `CreateSession(Title, DurationMinutes)`, attached to via `AttachListingToSession`, and started via `StartSession` — which fans out one `BiddingOpened` per attached listing via `SessionStartedHandler`. The vocabulary uses **Sale** and **Flash Session** interchangeably at the domain level; "Session" is the code-level type name. Two Flash sessions can share a title (W002 §5.1). See [`workflows/flash-session.md`](./workflows/flash-session.md).

## ScheduledCloseAt

The wall-clock time at which a listing's bidding ends. Established at `BiddingOpened` emission; mutable only via `ExtendedBiddingTriggered`'s re-schedule. Source: `src/CritterBids.Auctions/AuctionClosingSaga.cs`.

## SellerListing

The `SellerListing` event-sourced aggregate owned by the [Selling BC](./bcs/selling.md). Stream lifecycle: `DraftListingCreated` → `DraftListingUpdated*` (0..n) → `ListingSubmitted` → `ListingApproved + ListingPublished` (atomic) or `ListingRejected` → optionally `ListingWithdrawn`. Source: `src/CritterBids.Selling/SellerListing.cs`.

## SellerPayoutIssued

Integration event emitted at phase 5 of the Settlement saga. Carries the seller's payout amount (`HammerPrice - FeeAmount`). Routed to `relay-settlement-events` (no consumer wired yet). Source: `src/CritterBids.Contracts/Settlement/SellerPayoutIssued.cs`.

## SellerProfile

A vision-doc-declared aggregate (`docs/vision/bounded-contexts.md:18`) that does **not exist** as a separate type in code. The seller-registered flag is folded into the `Participant` aggregate as a single `bool IsRegisteredSeller`. Recorded in `gaps-and-drift.md`.

## SellerRegistered

Internal Participants event appended on registration. Distinct from the integration event [`SellerRegistrationCompleted`](#sellerregistrationcompleted) by namespace — the duplication is intentional per the type's XML doc to maintain BC boundary separation. Source: `src/CritterBids.Participants/Features/RegisterAsSeller/SellerRegistered.cs`.

## SellerRegistrationCompleted

Integration event published from `RegisterAsSellerHandler` via `OutgoingMessages`. Consumed by Selling's `SellerRegistrationCompletedHandler` to build the `RegisteredSeller` projection. Lives at `src/CritterBids.Contracts/SellerRegistrationCompleted.cs` (flat — **not** in a `Participants/` subfolder). Source: `src/CritterBids.Participants/Features/RegisterAsSeller/RegisterAsSeller.cs:61`.

## SettlementId

Deterministic UUID v5 derived as `UuidV5(SettlementsIdentityNamespaces.SettlementId, $"settlement:{listingId}")`. The first UUID v5 in CritterBids. Used as both the saga document id and the financial event stream id. Drift-corrected at M5-S4: W003 Phase 1 Part 6 originally specified `AuctionsNamespace`. Source: `src/CritterBids.Settlement/SettlementsIdentityHelpers.cs`.

## SettlementCompleted

Terminal-phase integration event emitted by `SettlementSaga` on the happy path. Carries `HammerPrice`, `FeeAmount`, `SellerPayout`. Routed to `listings-settlement-events`. Consumed by Listings (`SettlementStatusHandler`: `Status: "Sold" → "Settled"`) and Settlement itself (`PendingSettlementHandler`: `Pending → Consumed`). Source: `src/CritterBids.Contracts/Settlement/SettlementCompleted.cs`.

## Settlement Saga

7-phase Wolverine saga: `Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed`, plus terminal `Failed`. Hosted via decider pattern (W003). Source: `src/CritterBids.Settlement/SettlementSaga.cs`. See [`workflows/timed-listing-close.md`](./workflows/timed-listing-close.md) §"Settlement saga seven-phase progression".

## SignalR

Real-time push channel for Relay (winner/seller notifications) and Operations (live ops dashboard). Both BCs are [Planned-only](./bcs/relay.md) — the SignalR layer is not wired.

## Starting Bid

The minimum first bid on a listing. Set at draft creation; immutable after publish. Validator enforces `StartingBid > 0` and downstream `ReservePrice >= StartingBid`. Vocabulary deliberately avoids "opening bid". Source: `src/CritterBids.Selling/ListingValidator.cs:48-50`.

## Sticky queue

A per-handler dedicated queue created by `MultipleHandlerBehavior.Separated`. Each consumer's local instance of an integration event lands on its own queue, allowing independent retry/dedup. Configured in `src/CritterBids.Api/Program.cs:42-164`.

## Tag aggregate

An aggregate type loaded via DCB tag queries rather than stream id. `BidConsistencyState` is the only one. Loaded by `FetchForWritingByTags<BidConsistencyState>` with `EventTagQuery.For(ListingStreamId).AndEventsOfType<...>()`. Source: `src/CritterBids.Auctions/BidConsistencyState.cs`.

## Three-is-better-than-premature-abstraction

CLAUDE.md house rule: a logic copy that appears in three places without divergence is still preferable to a premature abstraction. Concrete example: the bid-increment ladder `$1 < $100; $5 ≥ $100` is co-located in `PlaceBidHandler`, `BuyNowHandler`, and `ProxyBidManagerSaga`. When divergence pressure arrives, the abstraction is extracted; until then, three exact copies are the design.

## Timed (listing format)

Listing format with `Duration` set. Bidding opens immediately on publish (Timed-flow in `ListingPublishedHandler`). `ScheduledCloseAt = PublishedAt + Duration`. Compare with [Flash](#flash-listing-format).

## Tracked.NoRoutes

Wolverine diagnostic bucket for messages published via `OutgoingMessages` that have no `PublishMessage` route configured. `ProxyBidRegistered` and `ProxyBidExhausted` currently land here; intended Relay consumers are post-M5.

## UUID v5

Deterministic UUID derived from `(namespace, name)` via SHA-1. Used in CritterBids for: the Settlement saga + financial event stream id (`SettlementId(listingId)`), the Proxy Bid Manager saga id (`UuidV5(ns, $"{ListingId}:{BidderId}")`), and the per-BC namespace constants (e.g. `AuctionsIdentityNamespaces`, `SettlementsIdentityNamespaces`). ADR 007 carries the rationale.

## UUID v7

Time-ordered UUID with millisecond-precision Unix-time prefix. Used in CritterBids for: new `Participant` streams, new `SellerListing` streams, new `Session` streams, and new bid identifiers (`BidId = Guid.CreateVersion7()`). Picked when no natural business key enables deterministic stream creation. ADR 007. Honored in code: `Guid.CreateVersion7()` calls in `StartParticipantSessionHandler`, `CreateDraftListingHandler`, `CreateSessionHandler`, `ProxyBidManagerSaga.Handle(ProxyBidObserved)`.

## ValidateAsync

Wolverine middleware seam invoked before `Handle`. `CreateDraftListing` uses it to call `ISellerRegistrationService.IsRegisteredAsync` and return HTTP 403 if the participant has not completed seller registration. Source: `src/CritterBids.Selling/CreateDraftListingHandler.cs`.

## WriteAggregate

Wolverine attribute on a handler parameter that triggers `FetchForWriting<T>` aggregate loading. Used in `[WriteAggregate] SellerListing listing` and `[WriteAggregate(nameof(SessionId))] Session session`. Source: handler signatures across `src/CritterBids.Selling/`, `src/CritterBids.Auctions/`.

## WinnerCharged

Internal Settlement event at phase 3. Represents the virtual debit of `HammerPrice` against the winner's credit. In MVP this is bookkeeping in the financial event stream — no real payment processor is wired (M5-M6 scope). Drives `BidderCreditView.RemainingCredit` debit. Source: `src/CritterBids.Settlement/SettlementSaga.cs`.

---

## Cross-references

Terms appearing here that drift from code or vision are cross-referenced inline. The full register of drift is `gaps-and-drift.md` (Phase 4).
