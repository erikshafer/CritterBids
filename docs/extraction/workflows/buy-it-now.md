# Buy It Now terminal path

**Maturity:** Implemented end-to-end through Settlement. Diverges from the [Timed close flow](./timed-listing-close.md) in that BIN is its own terminal outcome — no `BiddingClosed` mechanical signal, no `ListingSold`/`ListingPassed` cascade, settlement starts at `ReserveChecked` directly.

## Trigger

`BuyNow(ListingId, BuyerId, CreditCeiling)` command dispatched against a listing while BIN is still available (no prior bid has landed — the first accepted bid emits `BuyItNowOptionRemoved`).

Source: `src/CritterBids.Auctions/BuyNow.cs` + `BuyNowHandler.cs`.

## Hops

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 1 | Auctions | DCB load | `BuyNowHandler.HandleAsync` — `FetchForWritingByTags<BidConsistencyState>` with `EventTagQuery.For(ListingStreamId).AndEventsOfType<BiddingOpened, BidPlaced, BuyItNowOptionRemoved, ReserveMet, ExtendedBiddingTriggered, BuyItNowPurchased>` (includes `BuyItNowPurchased` so a second BIN on a terminal listing loads the projection and rejects) | `src/CritterBids.Auctions/BuyNowHandler.cs:34-41, 94-97` |
| 2 | Auctions | Rejection evaluation | 5 reasons: `ListingNotOpen` (no `BiddingOpened`), `ListingClosed` (scheduled close in past OR `IsOpen == false`), `BuyItNowNotAvailable` (option removed or never set), `ExceedsCreditCeiling` (BIN price > command credit ceiling) | `src/CritterBids.Auctions/BuyNowHandler.cs:125-152` |
| 2a | Auctions | On rejection | Append `BidRejected` to per-listing `BidRejectionAudit` stream (`AttemptedAmount = state.BuyItNowPrice ?? 0`, `BidderId = command.BuyerId`) | `src/CritterBids.Auctions/BuyNowHandler.cs:99-123` |
| 2b | Auctions | On acceptance | Tag and append `BuyItNowPurchased(ListingId, BuyerId, Price = state.BuyItNowPrice!.Value, PurchasedAt = now)` to listing stream | `src/CritterBids.Auctions/BuyNowHandler.cs:50-63` |
| 3 | Auctions (BidConsistencyState) | `Apply(BuyItNowPurchased)` | `IsOpen = false`, `BuyItNowAvailable = false` — locks out concurrent BIN/bid attempts via DCB consistency assertion | `src/CritterBids.Auctions/BidConsistencyState.cs:84-88` |
| 4 | Auctions (saga) | `BuyItNowPurchased` consumed by `AuctionClosingSaga.Handle(BuyItNowPurchased)` | Terminal guard (early return if already `Resolved`); cancels pending `CloseAuction`; `Status = Resolved`; `MarkCompleted()`. **No outcome event emitted.** | `src/CritterBids.Auctions/AuctionClosingSaga.cs:148-169` |
| 5 | (transport) | `BuyItNowPurchased` published to RabbitMQ | 2 queues: `listings-auctions-events`, `settlement-auctions-events` | `src/CritterBids.Api/Program.cs:77-78, 116-117` |
| 6 | Listings | `AuctionStatusHandler.Handle(BuyItNowPurchased)` — `Status` to `"Sold"` directly from `Published` or `Open`; `HammerPrice = Price`, `WinnerId = BuyerId`, `ClosedAt = PurchasedAt` | `bcs/listings.md` |
| 7 | Settlement | Start saga via BIN overload | `StartSettlementSagaHandler.Handle(BuyItNowPurchased)` — loads `PendingSettlement`, derives `SettlementId`, opens financial event stream with `SettlementInitiated(Source=BuyItNow, Price=message.Price)`. **Initial saga state is `ReserveChecked` with `ReserveWasMet = true` directly.** **No `ReserveCheckCompleted` appended** — its absence in the stream is the §9.2 audit signal "this was a BIN settlement". Self-sends `ChargeWinner` (not `CheckReserve`) | `src/CritterBids.Settlement/StartSettlementSagaHandler.cs:100-164` |
| 8 | Auctions (ProxyBidDispatchHandler) | **No fan-out** — `BuyItNowPurchased` is not a trigger for the dispatcher (verified in `ProxyBidDispatchHandler.cs` — only `BidPlaced`, `ListingSold`, `ListingPassed`, `ListingWithdrawn` are subscribed). Any active `ProxyBidManagerSaga` for this listing remains `Active` until a subsequent `ListingSold`/`ListingPassed`/`ListingWithdrawn` arrives — **but none ever will**. This is potential drift; recorded in `gaps-and-drift.md`. | — |
| 9 | Settlement (saga) | Remaining 5 phases identical to bidding path | `ChargeWinner → CalculateFee → IssueSellerPayout → CompleteSettlement` | See [`timed-listing-close.md`](./timed-listing-close.md) §"Settlement saga seven-phase progression" |

## Outcome

- **Stream-level audit:** financial event stream contains 5 events on the BIN happy path: `SettlementInitiated(Source=BuyItNow)`, `WinnerCharged`, `FinalValueFeeCalculated`, `SellerPayoutIssued`, `SettlementCompleted`. Six events on the bidding happy path (extra `ReserveCheckCompleted` between Initiated and WinnerCharged).
- **No `BiddingClosed`** emitted on this path (`Contracts.Auctions.BiddingClosed.cs:14-20` — "Not emitted on the BuyItNow terminal path").
- **No `ListingSold`** emitted on this path. `BuyItNowPurchased` is its own terminal contract event for cross-BC consumers.
- `CatalogListingView.Status` reaches `"Sold"` directly from `"Open"` (or even `"Published"` for the never-bid-on case), skipping `"Closed"`.

## Notes

- The same listing can settle exactly once across sources because Auctions enforces "BIN removes after first bid" via DCB — `Apply(BidPlaced)` is registered on `BidConsistencyState` but updates `CurrentHighBid`/`CurrentHighBidderId`/`BidCount`; the actual BIN removal is the `BuyItNowOptionRemoved` event the bid acceptance path emits explicitly (`PlaceBidHandler.cs:119-120`).
- The deterministic `SettlementId` derivation means a duplicate `BuyItNowPurchased` consumption resolves to the same saga document key — existing-saga check at `StartSettlementSagaHandler.cs:122-126` early-returns `(null, empty)`.
- `BidRejected` rejections on the BIN path reuse the same `BidRejectionAudit` stream as `PlaceBid` rejections (per `BuyNowHandler.cs:11-15` — "Rejections reuse `BidRejected` and the `BidRejectionAudit` stream").
