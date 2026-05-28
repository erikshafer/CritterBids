# Flash session container flow

**Maturity:** Implemented end-to-end (create → attach → start → fan-out → per-listing close). Diverges from the [Timed publish-to-bidding-open flow](./publish-to-bidding-open.md) starting at hop 5 of that trace.

## Trigger

`CreateSession(Title, DurationMinutes)` cross-BC command dispatched against the Auctions BC. M6 will add the HTTP surface; M5 invokes via `IMessageBus` only.

Source: `src/CritterBids.Auctions/CreateSession.cs`, `Session.cs`, `AttachListingToSession.cs`, `StartSession.cs`, `SessionStartedHandler.cs`.

## Lifecycle hops

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 1 | Auctions | `CreateSessionHandler.Handle(CreateSession)` — generates UUID v7 `SessionId`; returns `(CreationResponse<Guid>, IStartStream)` via `MartenOps.StartStream<Session>(sessionId, new SessionCreated(SessionId, Title, DurationMinutes, CreatedAt))`. No validation, no uniqueness check (Workshop 002 §5.1 — two Flash sessions can share a title). | `src/CritterBids.Auctions/CreateSession.cs:31-47` |
| 2 | (transport) | `SessionCreated` published to RabbitMQ queue `listings-auctions-events` | `src/CritterBids.Api/Program.cs:84-85` |
| 3 | Listings | Documented as M4-S6 (`Contracts.Auctions.SessionCreated.cs:18-25`): may maintain a lightweight `SessionCatalog` view summarizing active sessions for ops tooling. **Implementation status in code:** `AuctionsSessionHandler.cs` explicitly does NOT handle `SessionCreated` ("the catalog has no per-session document" — `bcs/listings.md`); only `ListingAttachedToSession` and `SessionStarted` are handled by Listings. | — |
| 4 | (separate trigger) | Independently for each Timed `ListingPublished` while session not yet started: see [`publish-to-bidding-open.md`](./publish-to-bidding-open.md). Listings must be in `PublishedListings.Status == Published` to attach. | — |
| 5 | Auctions | `AttachListingToSession(SessionId, ListingId)` command — `AttachListingToSessionHandler.Handle` with `[WriteAggregate(nameof(SessionId))] Session session` | `src/CritterBids.Auctions/AttachListingToSession.cs:39-62` |
| 5a | Auctions | Invariant guard | If `session.StartedAt is not null` → throw `SessionAlreadyStartedException` (Workshop 002 §5.4) | `src/CritterBids.Auctions/AttachListingToSession.cs:45-46` |
| 5b | Auctions | Cross-projection guard | `LoadAsync<PublishedListings>(ListingId)` — if `null` or `Status == Withdrawn` → throw `ListingNotPublishedException` (Workshop 002 §5.3) | `src/CritterBids.Auctions/AttachListingToSession.cs:48-52` |
| 5c | Auctions | Emit | Aggregate appends `ListingAttachedToSession(SessionId, ListingId, AttachedAt)` via `Events` return type | `src/CritterBids.Auctions/AttachListingToSession.cs:54-60` |
| 5d | Auctions (aggregate) | `Session.Apply(ListingAttachedToSession)` — `AttachedListingIds = [..AttachedListingIds, attached.ListingId]` (attachment-order preserving) | `src/CritterBids.Auctions/Session.cs:78-83` |
| 6 | (transport) | `ListingAttachedToSession` published to RabbitMQ queue `listings-auctions-events` | `src/CritterBids.Api/Program.cs:86-87` |
| 7 | Listings | `AuctionsSessionHandler.Handle(ListingAttachedToSession)` — sets `CatalogListingView.SessionId` | `bcs/listings.md` |
| 8 | Auctions | `StartSession(SessionId)` command — `StartSessionHandler.Handle` with `[WriteAggregate]` | `src/CritterBids.Auctions/StartSession.cs:37-57` |
| 8a | Auctions | Invariant guards | If `session.StartedAt is not null` → `SessionAlreadyStartedException` (§5.7); if `session.AttachedListingIds.Count == 0` → `SessionHasNoListingsException` (§5.6) | `src/CritterBids.Auctions/StartSession.cs:43-47` |
| 8b | Auctions | Emit | Aggregate appends `SessionStarted(SessionId, ListingIds = session.AttachedListingIds (verbatim), StartedAt)` | `src/CritterBids.Auctions/StartSession.cs:49-56` |
| 8c | Auctions (aggregate) | `Session.Apply(SessionStarted)` — sets `StartedAt` (terminal; sessions do not unstart, pause, or cancel — M4 non-goals) | `src/CritterBids.Auctions/Session.cs:88-91` |

## Fan-out hops (the per-listing `BiddingOpened` emission)

`SessionStarted` is forwarded as an in-process Wolverine message (`UseFastEventForwarding = true` in `Program.cs:193`) AND published externally:

| # | BC | Step | Event / command | File |
|---|---|---|---|---|
| 9 | (transport) | `SessionStarted` published to RabbitMQ queue `listings-auctions-events` | `src/CritterBids.Api/Program.cs:88-89` |
| 10 | Listings | `AuctionsSessionHandler.Handle(SessionStarted)` — batch-loads all listings in `ListingIds`; sets `SessionStartedAt` on each | `bcs/listings.md` |
| 11 | Auctions (fan-out) | `SessionStartedHandler.Handle(SessionStarted)` — the **first in-repo one-inbound-N-outbound fan-out handler** | `src/CritterBids.Auctions/SessionStartedHandler.cs:62-120` |
| 11a | Auctions | Load `Session` aggregate via `AggregateStreamAsync` to read `DurationMinutes` (OQ5 Path B — `SessionStarted` contract carries only `StartedAt` and `ListingIds`, not duration) | `src/CritterBids.Auctions/SessionStartedHandler.cs:71-79` |
| 11b | Auctions | Compute `scheduledCloseAt = message.StartedAt + TimeSpan.FromMinutes(DurationMinutes)`; `maxDuration = TimeSpan.FromMinutes(DurationMinutes * 2)` (Workshop 002 platform default 2x) | `src/CritterBids.Auctions/SessionStartedHandler.cs:81-85` |
| 11c | Auctions | For each `listingId` in `message.ListingIds`: load `PublishedListings` (skip if null — data-availability constraint); pre-query listing stream state (skip if exists — idempotency, OQ2 stream-existence pattern from M3 `ListingPublishedHandler`); append `BiddingOpened` to a new stream via `session.Events.StartStream<Listing>(listingId, bidding)` | `src/CritterBids.Auctions/SessionStartedHandler.cs:87-113` |
| 12 | Auctions (in-process per listing) | Each appended `BiddingOpened` forwards via `UseFastEventForwarding` to `StartAuctionClosingSagaHandler.Handle(BiddingOpened)` — creates per-listing `AuctionClosingSaga`; schedules per-listing `CloseAuction` at `scheduledCloseAt` (all listings share the same close time = `StartedAt + DurationMinutes`) | `src/CritterBids.Auctions/StartAuctionClosingSagaHandler.cs:19-44` |
| 13 | (transport) | Each `BiddingOpened` also published to RabbitMQ queue `listings-auctions-events` | `src/CritterBids.Api/Program.cs:67-68` |
| 14 | Listings | `AuctionStatusHandler.Handle(BiddingOpened)` — `CatalogListingView.Status = "Open"` with `ScheduledCloseAt` | `bcs/listings.md` |

## After fan-out: per-listing flows are independent

From hop 12 onward, each attached listing follows the standard [Timed listing close](./timed-listing-close.md) flow — its own DCB, its own `AuctionClosingSaga`, its own scheduled `CloseAuction`, its own outcome events (`BiddingClosed`, `ListingSold`, `ListingPassed`, or `BuyItNowPurchased`), and its own [Settlement](./post-sale-obligations.md). The session aggregate plays no role after `SessionStarted`.

**The session is the container for the open event, not for the close.** All listings share `ScheduledCloseAt`, but each can extend independently via `ExtendedBiddingTriggered`, BIN out independently, or be withdrawn independently.

## Notes

- **Skip in step 5b of [`publish-to-bidding-open.md`](./publish-to-bidding-open.md).** `ListingPublishedHandler.Handle(ListingPublished)` guards on `Duration is not null` — Flash listings (Duration null per the W002 Phase 1 contract) are skipped at publish time. The Flash listing's Auctions-side stream is empty until `SessionStartedHandler` fans out the per-listing `BiddingOpened` (`ListingPublishedHandler.cs:55-59`).
- **`PublishedListings` field shape OQ1 Path A — full payload.** The Auctions-side cache carries SellerId, StartingBid, ReservePrice, BuyItNowPrice, and full extended-bidding config, not just status. Picked at M4-S5 because `SessionStartedHandler` ALSO consults the projection (not just the attach-time `Status` check). Without Path A, the fan-out would need a third lookup mechanism for Flash listings (their primary stream is empty at fan-out time).
- **Idempotency at the fan-out handler is stream-existence based**, mirroring the M3 `ListingPublishedHandler` idiom. The handler comments explicitly note that the M4-S5 milestone-doc framing about "DCB-primary" conflated two distinct mechanisms (`SessionStartedHandler.cs:46-56`).
- **No defensive pre-filtering for withdrawn listings (OQ3 Path α).** A listing attached and then withdrawn before `StartSession` still receives a `BiddingOpened` append; termination happens reactively via the `AuctionClosingSaga`'s `ListingWithdrawn` consumption (`AttachListingToSession.cs` comment block).
- **MaxDuration computed in handler, not on contract.** `SessionStarted` doesn't carry `MaxDuration` — the handler computes `DurationMinutes * 2` from the aggregate. Same platform-default formula as the M3 Timed path uses (`Duration * 2`).

## Outcome

- `Session.Apply(SessionStarted)` sets terminal `StartedAt` on the session aggregate.
- Per-listing `BiddingOpened` events appended to per-listing Marten streams (one per attached listing).
- One `AuctionClosingSaga` per listing, all scheduled to close at `StartedAt + DurationMinutes`.
- `CatalogListingView.SessionId` and `CatalogListingView.SessionStartedAt` populated.

From here each listing is independent; the session has no further role in the lifecycle of its listings.
