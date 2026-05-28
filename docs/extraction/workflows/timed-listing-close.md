# Timed listing close (Auction Closing saga → outcomes → Settlement)

**Maturity:** Implemented through Settlement Completed / Failed. Downstream Relay and Operations consumers are post-M5 (see [`post-sale-obligations.md`](./post-sale-obligations.md)).

## Trigger

Scheduled `CloseAuction(ListingId, ScheduledAt)` message fires from Wolverine's durable scheduled-message store at the listing's currently-scheduled close time. The schedule was created by `StartAuctionClosingSagaHandler` on `BiddingOpened` (see [`publish-to-bidding-open.md`](./publish-to-bidding-open.md)) and may have been cancelled-and-rescheduled by `AuctionClosingSaga.Handle(ExtendedBiddingTriggered)`.

Source: `src/CritterBids.Auctions/AuctionClosingSaga.cs:84-139`.

## Bid hops (before the close fires)

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| B1 | Auctions | `PlaceBid` command dispatched (test-only in M3-M5; M6 adds HTTP) | DCB `FetchForWritingByTags<BidConsistencyState>` loads tag-aggregate; evaluates 5 rejection reasons | `src/CritterBids.Auctions/PlaceBidHandler.cs:32-56, 142-172` |
| B2a | Auctions | Rejection | `BidRejected` appended to per-listing `BidRejectionAudit` stream | `src/CritterBids.Auctions/PlaceBidHandler.cs:81-103` |
| B2b | Auctions | Acceptance | Up to 4 events tagged + appended to listing stream: `BidPlaced` (always), `BuyItNowOptionRemoved` (if BIN was available), `ReserveMet` (if reserve threshold just crossed), `ExtendedBiddingTriggered` (if within trigger window) | `src/CritterBids.Auctions/PlaceBidHandler.cs:105-140` |
| B3 | Auctions (saga) | `BidPlaced` consumed by `AuctionClosingSaga.Handle(BidPlaced)` | Monotone `BidCount` guard; updates high-bid state | `src/CritterBids.Auctions/AuctionClosingSaga.cs:47-59` |
| B4 | Auctions (saga) | `ReserveMet` consumed | Sets `ReserveHasBeenMet = true` | `src/CritterBids.Auctions/AuctionClosingSaga.cs:61-64` |
| B5 | Auctions (saga) | `ExtendedBiddingTriggered` consumed | Cancels pending `CloseAuction` via `IMessageStore.ScheduledMessages.CancelAsync` (narrow ±100ms window keyed on `MessageType + ExecutionTime`); reschedules at `NewCloseAt`; transitions to `Extended` | `src/CritterBids.Auctions/AuctionClosingSaga.cs:66-82, 191-208` |

## Close hops

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 1 | Auctions (saga) | `CloseAuction` fires | `AuctionClosingSaga.Handle(CloseAuction)` — terminal evaluation | `src/CritterBids.Auctions/AuctionClosingSaga.cs:84-139` |
| 2 | Auctions (saga) | Always emit | `BiddingClosed(ListingId, now)` via `OutgoingMessages` | `src/CritterBids.Auctions/AuctionClosingSaga.cs:96-99` |
| 3a | Auctions (saga) | Bids and reserve met | Load `Listing` aggregate to read `SellerId`; emit `ListingSold(ListingId, SellerId, WinnerId, HammerPrice, BidCount, SoldAt)` | `src/CritterBids.Auctions/AuctionClosingSaga.cs:101-116` |
| 3b | Auctions (saga) | Bids but reserve not met | Emit `ListingPassed(ListingId, "ReserveNotMet", HighestBid, BidCount, PassedAt)` | `src/CritterBids.Auctions/AuctionClosingSaga.cs:117-125` |
| 3c | Auctions (saga) | No bids | Emit `ListingPassed(ListingId, "NoBids", null, 0, PassedAt)` | `src/CritterBids.Auctions/AuctionClosingSaga.cs:126-134` |
| 4 | Auctions (saga) | Mark complete | `Status = Resolved`; `MarkCompleted()` (deletes saga document) | `src/CritterBids.Auctions/AuctionClosingSaga.cs:136-138` |
| 5 | (transport) | Cascade events published | `BiddingClosed`, `ListingSold`, `ListingPassed` → `listings-auctions-events`; `ListingSold` and `ListingPassed` → `settlement-auctions-events` | `src/CritterBids.Api/Program.cs:71-78, 114-120` |
| 6 | Listings | Update catalog | `AuctionStatusHandler` — `Status = "Closed"`, then `Status = "Sold"` with hammer fields OR `Status = "Passed"` with reason + final-bid fields | `bcs/listings.md` Integration events (in) table |
| 7a | Settlement (sold path) | Start saga | `StartSettlementSagaHandler.Handle(ListingSold)` — loads `PendingSettlement` (throws `PendingSettlementNotFoundException` if absent → retry policy re-queues), derives `SettlementId = UuidV5(namespace, $"settlement:{ListingId}")`, opens financial event stream with `SettlementInitiated(Source=Bidding, Price=HammerPrice, ...)`, self-sends `CheckReserve` | `src/CritterBids.Settlement/StartSettlementSagaHandler.cs:41-98` |
| 7b | Settlement (passed path) | No saga | `PendingSettlementHandler.Handle(ListingPassed)` — transitions `Pending → Expired`; no settlement runs | `src/CritterBids.Settlement/PendingSettlementHandler.cs:65-76` |
| 8 | Auctions (ProxyBidDispatchHandler) | Terminal fan-out to any active `ProxyBidManagerSaga` | One wrapped `ProxyListingSoldObserved` / `ProxyListingPassedObserved` per active saga | See [`proxy-bidding.md`](./proxy-bidding.md) |

## Settlement saga seven-phase progression (sold path only)

| Phase | Status before → after | Stream event appended | Continuation |
|---|---|---|---|
| 1. Initiated | (none) → `Initiated` | `SettlementInitiated` | `CheckReserve` |
| 2. Reserve check | `Initiated` → `ReserveChecked` | `ReserveCheckCompleted(WasMet)` | `ChargeWinner` (met) / `FailSettlement("ReserveNotMet")` (not met) |
| 3. Charge winner | `ReserveChecked` → `WinnerCharged` | `WinnerCharged(SettlementId, WinnerId, Amount)` | `CalculateFee` |
| 4. Fee calc | `WinnerCharged` → `FeeCalculated` | `FinalValueFeeCalculated(HammerPrice, FeePercentage, FeeAmount, SellerPayout)` | `IssueSellerPayout` |
| 5. Seller payout | `FeeCalculated` → `PayoutIssued` | `SellerPayoutIssued` (also emitted as integration event) | `CompleteSettlement` |
| 6. Complete | `PayoutIssued` → `Completed` | `SettlementCompleted` (also emitted as integration event) | `MarkCompleted()` |

Failure branch (reserve-not-met): `ReserveChecked` → `Failed`; appends `PaymentFailed` (also emitted as integration event); `MarkCompleted()`. Three-event failure stream: `SettlementInitiated`, `ReserveCheckCompleted(WasMet: false)`, `PaymentFailed`.

Source: `src/CritterBids.Settlement/SettlementSaga.cs:67-233`.

## Outcome cascade (after settlement)

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 9 | (transport) | Settlement-out events published | `SettlementCompleted` → `listings-settlement-events`; `SellerPayoutIssued` → `relay-settlement-events` (no listener yet); `PaymentFailed` → `operations-settlement-events` (no listener yet) | `src/CritterBids.Api/Program.cs:145-163` |
| 10 | Listings | `SettlementStatusHandler.Handle(SettlementCompleted)` — `Status: "Sold" → "Settled"`, sets `SettledAt` | `bcs/listings.md` |
| 10 | Settlement (self-handler) | `PendingSettlementHandler.Handle(SettlementCompleted)` — `Pending → Consumed`; or `Handle(PaymentFailed)` → `Pending → Failed` | `src/CritterBids.Settlement/PendingSettlementHandler.cs:91-115` |
| 10 | Settlement (self-handler) | `BidderCreditViewHandler.Handle(WinnerCharged)` — debits `RemainingCredit` by `Amount` (lazy-init with negative sentinel if no prior session) | `src/CritterBids.Settlement/BidderCreditViewHandler.cs:55-86` |
| 11 | Relay / Operations / Obligations | See [`post-sale-obligations.md`](./post-sale-obligations.md) | post-M5 | — |

## State diagrams

```
AuctionClosingSaga (Auctions BC)
─────────────────────────────────
AwaitingBids ──first BidPlaced──> Active ──ExtendedBiddingTriggered──> Extended
     │                              │                                     │
     │                              └─CloseAuction fires─┐                │
     └────────────CloseAuction fires (no bids)──────────►├────────────────┘
                                                         ▼
                                  ┌──────────────── Resolved ────────────────┐
                                  │            (MarkCompleted)               │
                                  │                                          │
                                  │  + BuyItNowPurchased → Resolved          │
                                  │  + ListingWithdrawn → Resolved           │
                                  └──────────────────────────────────────────┘

SettlementSaga (Settlement BC, sold path)
─────────────────────────────────────────
Initiated ──CheckReserve(met)──> ReserveChecked ──ChargeWinner──> WinnerCharged
                                       │                                │
                                       │ (not met)                      │
                                       ▼                                ▼
                                    Failed (MarkCompleted)        FeeCalculated
                                                                        │
                                                                        ▼
                                                                  PayoutIssued
                                                                        │
                                                                        ▼
                                                            Completed (MarkCompleted)
```

## Notes

- `BiddingClosed` is the **mechanical** close signal ("bidding no longer accepted"); `ListingSold` / `ListingPassed` are the **business outcomes**. Per `Contracts.Auctions.BiddingClosed.cs:7-12` ("Separate from the outcome events ... so a consumer that only cares about 'bids no longer accepted' has a single type to subscribe to"). BIN does not emit `BiddingClosed` — its `BuyItNowPurchased` is both mechanical and business signal.
- Outcome events (`BiddingClosed`, `ListingSold`, `ListingPassed`) are **intentionally not registered** as Marten event types in `AuctionsModule.cs:87-90` — they cascade through `OutgoingMessages` only and are never appended to a Marten stream on the Auctions side.
- The Settlement saga state guards throw `InvalidSettlementTransitionException` on out-of-order delivery — Wolverine inbox dedup should prevent it but the exception is the correctness contract if dedup fails (`SettlementSaga.cs:24-30`).
- The `PendingSettlementNotFoundException` retry policy backs off 100ms → 250ms → 500ms (~850ms cumulative budget) — in practice the race rarely fires because `ListingPublished` precedes `ListingSold` by hours/days (`SettlementsConcurrencyRetryPolicies.cs:11-26`).
