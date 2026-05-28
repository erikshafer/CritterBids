# Listings BC

**Maturity:** Partial.

**Evidence for the call:** `src/CritterBids.Listings` exists with the `CatalogListingView` document, four sibling handlers (`ListingPublishedHandler`, `AuctionStatusHandler`, `AuctionsSessionHandler`, `SellingListingWithdrawnHandler`, `SettlementStatusHandler`), and two HTTP read endpoints (`Features/Catalog/CatalogEndpoints.cs`). The module is registered in `Program.cs` line 200. Test coverage is two files (`CatalogListingViewTests`, `SettlementStatusHandlerTests`). The vision-doc surface for **watchlists**, **`LotWatchAdded` / `LotWatchRemoved`**, **per-category and per-price filtering**, and **full-text search** has no implementation — no document, no handler, no command, no endpoint corresponding to those capabilities exists.

## Business purpose

Projection-first BC. Builds and serves the public listing catalog as a single Marten document (`CatalogListingView`) per listing, populated by integration events from Selling (`ListingPublished`, `ListingWithdrawn`), Auctions (`BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, `ListingAttachedToSession`, `SessionStarted`), and Settlement (`SettlementCompleted`). Originates no domain events.

## Project layout

Mostly flat. Five sibling handler classes and the `CatalogListingView` document at the top level. The single HTTP endpoint pair lives under `Features/Catalog/`.

## Aggregates

None. Listings is projection-only.

## Domain events

None. Listings originates no events.

## Commands and handlers

The four `Handle` overloads on `AuctionStatusHandler`, two on `AuctionsSessionHandler`, one each on `ListingPublishedHandler`, `SellingListingWithdrawnHandler`, and `SettlementStatusHandler` are all message-handler entry points reacting to integration events. There are no Listings-owned commands.

## Read models / projections

| View | File | Source events | Fields |
|---|---|---|---|
| `CatalogListingView` | `CatalogListingView.cs` | `ListingPublished` (Selling); `ListingWithdrawn` (Selling); `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`, `ListingAttachedToSession`, `SessionStarted` (Auctions); `SettlementCompleted` (Settlement). | M2 fields: `Id`, `SellerId`, `Title`, `Format`, `StartingBid`, `BuyItNow`, `Duration`, `PublishedAt`. M3-S6 auction-status fields: `Status` (string state machine), `ScheduledCloseAt`, `CurrentHighBid`, `CurrentHighBidderId`, `BidCount`, `HammerPrice`, `WinnerId`, `PassedReason`, `FinalHighestBid`, `ClosedAt`. M5-S6 settlement field: `SettledAt`. M4-S6 session fields: `SessionId`, `SessionStartedAt`. |

The `Status` field is a string, not an enum (intentional, per `CatalogListingView.cs` line 49: "symmetry with Format above (M2-S7 precedent, OQ2 Path A)"). Documented transitions on `CatalogListingView.cs` lines 35–43:

- Bidding-sold: `Published → Open → Closed → Sold → Settled`
- BIN: `Published → Open → Sold → Settled`
- Passed: `Published → Open → (Closed →) Passed`
- Withdrawn (terminal): `Published → Withdrawn` or `Open → Withdrawn`

`Passed` listings never reach `Settled` — the financial workflow only runs on the sold paths.

## Handler pattern

Each of the five sibling handler classes is one source BC (M3-D2 Path A formalized by ADR 014). The shared write shape is:

1. `LoadAsync<CatalogListingView>(message.ListingId)`.
2. On miss, construct a minimal row with `Id` set (tolerant upsert; absorbs cross-queue races).
3. Apply guard if the handler is on a terminal-state path (e.g. `SettlementStatusHandler` requires `Status == "Sold"`; `SellingListingWithdrawnHandler` requires `Status ∈ {"Published", "Open"}`; `AuctionStatusHandler.Handle(BiddingOpened)` short-circuits when `Status == "Withdrawn"`).
4. `session.Store(view with { ... })` — Marten upsert.
5. No `SaveChangesAsync()` — `AutoApplyTransactions()` (Program.cs) commits.
6. No `OutgoingMessages` — Listings is a pure consumer.

`ListingPublishedHandler.Handle` (`ListingPublishedHandler.cs` lines 34–73) uses a named-field preservation block to preserve downstream-handler state on re-delivery — every field added by a sibling-handler requires its own preservation line.

## HTTP endpoints (read)

| Route | Handler | Notes |
|---|---|---|
| `GET /api/listings` | `CatalogEndpoints.GetCatalog` | All published listings, `OrderByDescending(PublishedAt)`. Empty array on no rows — never 404. |
| `GET /api/listings/{id}` | `CatalogEndpoints.GetListingDetail` | Single view by id; 404 if missing. |

Both endpoints are `[AllowAnonymous]`.

## Integration events (in)

| Event | Source | Handler | Effect |
|---|---|---|---|
| `Contracts.Selling.ListingPublished` | Selling | `ListingPublishedHandler` | Seeds M2 fields; preserves downstream state. |
| `Contracts.Selling.ListingWithdrawn` | Selling | `SellingListingWithdrawnHandler` | Sets `Status = "Withdrawn"`, `ClosedAt = WithdrawnAt`; guards against absorbing-terminal regression. |
| `Contracts.Auctions.BiddingOpened` | Auctions | `AuctionStatusHandler` | `Status → "Open"`, sets `ScheduledCloseAt`; no-op when `Status == "Withdrawn"`. |
| `Contracts.Auctions.BidPlaced` | Auctions | `AuctionStatusHandler` | Sets `CurrentHighBid`, `CurrentHighBidderId`, `BidCount` (authoritatively from the message — never incremented). |
| `Contracts.Auctions.BiddingClosed` | Auctions | `AuctionStatusHandler` | `Status → "Closed"`, sets `ClosedAt`. Not emitted on BIN or Withdrawn paths. |
| `Contracts.Auctions.ListingSold` | Auctions | `AuctionStatusHandler` | `Status → "Sold"`, sets `HammerPrice`, `WinnerId`, `BidCount`, `ClosedAt = SoldAt`. |
| `Contracts.Auctions.ListingPassed` | Auctions | `AuctionStatusHandler` | `Status → "Passed"`, sets `PassedReason`, `FinalHighestBid`, `BidCount`, `ClosedAt = PassedAt`. |
| `Contracts.Auctions.BuyItNowPurchased` | Auctions | `AuctionStatusHandler` | `Status → "Sold"` direct from `Published` or `Open`, sets `HammerPrice = Price`, `WinnerId = BuyerId`, `ClosedAt = PurchasedAt`. |
| `Contracts.Auctions.ListingAttachedToSession` | Auctions | `AuctionsSessionHandler` | Sets `SessionId`. |
| `Contracts.Auctions.SessionStarted` | Auctions | `AuctionsSessionHandler` | Batch-loads all listings in `message.ListingIds`, sets `SessionStartedAt` on each. |
| `Contracts.Settlement.SettlementCompleted` | Settlement | `SettlementStatusHandler` | `Status: "Sold" → "Settled"`, sets `SettledAt`. |

`SessionCreated` is intentionally not handled (`AuctionsSessionHandler.cs` lines 22–24: "the catalog has no per-session document").

## Integration events (out)

None.

## Vision-doc capabilities NOT implemented

| Vision element | Source | Status in `src/` |
|---|---|---|
| Watchlist (per-participant private list) | `bounded-contexts.md` line 99 | No document, no handler, no endpoint. |
| Watch counts (social proof on listings) | `bounded-contexts.md` line 104 | No counter on `CatalogListingView`. |
| `LotWatchAdded` / `LotWatchRemoved` integration events | `bounded-contexts.md` line 110, `domain-events.md` lines 71–72 | No CLR types in `Contracts/Listings/` (no `Listings` subfolder exists). No emitter. No consumer. |
| Category and price filtering | `bounded-contexts.md` line 95 | No category field on `CatalogListingView`; no filter endpoints. |
| Full-text search via Marten/PostgreSQL | `bounded-contexts.md` line 98 | No search endpoint; no full-text-index configuration in `ListingsModule.cs`. |
| `ListingRevised` consumer | `bounded-contexts.md` line 108 | The event has no contract type to consume (see Selling dossier). |

## Storage

PostgreSQL via Marten. `CatalogListingView` lives in the `listings` schema (`ListingsModule.cs` line 17).

## Identity strategy

Document id is the `ListingId` from the upstream `ListingPublished` event. Listings generates no new ids.

## Test-evidenced behaviors

From `tests/CritterBids.Listings.Tests/`:

- `CatalogListingViewTests` — projection writes for the seed handler and the auction-status sibling, and the four documented status-transition paths.
- `SettlementStatusHandlerTests` — `Sold → Settled` transition; tolerant upsert on absent row; non-`Sold` arrival no-ops.

## Open questions

- None at the BC level beyond the named missing capabilities (recorded as drift, not open questions).
