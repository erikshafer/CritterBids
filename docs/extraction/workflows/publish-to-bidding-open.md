# Publish a listing through to bidding open

**Maturity:** Implemented end-to-end (Timed format). Flash-format listings use the [Flash session flow](./flash-session.md) instead.

## Trigger

`SubmitListing(ListingId, SellerId)` accepted by `SellerListing` aggregate, `Status` is `Draft` or `Rejected`, and `ListingValidator` passes.

Source: `src/CritterBids.Selling/SubmitListingHandler.cs:54-58`.

## Hops

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 1 | Selling | Submit accepted | Aggregate appends `ListingSubmitted + ListingApproved + ListingPublished` (internal) atomically; emits `Contracts.Selling.ListingPublished` via `OutgoingMessages` | `src/CritterBids.Selling/SubmitListingHandler.cs:54-58` |
| 2 | (transport) | Wolverine outbox publishes contract event to RabbitMQ | `ListingPublished` → 3 queues: `listings-selling-events`, `auctions-selling-events`, `settlement-selling-events` | `src/CritterBids.Api/Program.cs:45-51, 100-104` |
| 3 | Listings | Catalog seed | `ListingPublishedHandler.Handle(ListingPublished)` — upsert `CatalogListingView` with M2 fields; `Status = "Published"` | `src/CritterBids.Listings/ListingPublishedHandler.cs:34-73` |
| 4 | Auctions | Cache publish payload | `PublishedListingsHandler.Handle(ListingPublished)` — upsert `PublishedListings` document (`Status = Published`) | `src/CritterBids.Auctions/PublishedListingsHandler.cs:41-71` |
| 5a | Auctions (Timed only) | Open Listing stream | `ListingPublishedHandler.Handle(ListingPublished)` — guards on `Duration is not null`; appends `BiddingOpened` to a new stream keyed on `ListingId` via `session.Events.StartStream<Listing>` | `src/CritterBids.Auctions/ListingPublishedHandler.cs:47-83` |
| 5b | Auctions (Flash only) | Skip — wait for `SessionStarted` | Handler returns early when `Duration is null`; the [Flash session flow](./flash-session.md) opens the stream later | `src/CritterBids.Auctions/ListingPublishedHandler.cs:55-59` |
| 6 | Settlement | Seed pending row | `PendingSettlementHandler.Handle(ListingPublished)` — upsert `PendingSettlement` document (`Status = Pending`) | `src/CritterBids.Settlement/PendingSettlementHandler.cs:41-63` |
| 7 | Auctions (in-process) | `BiddingOpened` forwarded via `UseFastEventForwarding` | `StartAuctionClosingSagaHandler.Handle(BiddingOpened)` — creates `AuctionClosingSaga` document; `bus.ScheduleAsync(new CloseAuction(ListingId, ScheduledCloseAt), ScheduledCloseAt)` | `src/CritterBids.Auctions/StartAuctionClosingSagaHandler.cs:19-44` |
| 8 | (transport) | `BiddingOpened` published to RabbitMQ for Listings | Route: `listings-auctions-events` | `src/CritterBids.Api/Program.cs:67-68` |
| 9 | Listings | Flip catalog status | `AuctionStatusHandler.Handle(BiddingOpened)` — `CatalogListingView.Status = "Open"`, sets `ScheduledCloseAt` | (sibling handler under `src/CritterBids.Listings/`, per `bcs/listings.md`) |

## Outcome

- `CatalogListingView.Status == "Open"` with `ScheduledCloseAt`.
- `AuctionClosingSaga` exists at `Status = AwaitingBids` with a scheduled `CloseAuction` in `IMessageStore.ScheduledMessages`.
- `PendingSettlement.Status == "Pending"`.
- `PublishedListings.Status == "Published"`.

## Notes

- The internal Selling event `ListingPublished` carries only `ListingId + PublishedAt`. The cross-BC payload lives in the contract event of the same name (`src/CritterBids.Contracts/Selling/ListingPublished.cs`), emitted from the same handler via `OutgoingMessages` (`bcs/selling.md`).
- All four consumers (Listings, Auctions × 2 handlers, Settlement) run on separate sticky queues per `MultipleHandlerBehavior.Separated` (`Program.cs:20`). In tests, dispatch via the bus must use `SendMessageAndWaitAsync`, not `InvokeMessageAndWaitAsync` (`bcs/auctions.md` Notable internal conventions).
- Idempotency on re-delivery is per-handler: stream-state pre-query (Auctions `ListingPublishedHandler`), upsert with terminal-status preservation (Auctions `PublishedListingsHandler`, Settlement `PendingSettlementHandler`), or named-field preservation block (Listings `ListingPublishedHandler`).
