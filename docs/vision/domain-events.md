# CritterBids — Domain Event Vocabulary

A flat vocabulary reference for all events in CritterBids. This is a glossary, not a technical specification. Fields, payloads, and Given-When-Then specifications are produced through Event Modeling workshops.

**Legend:**
- 🟠 Internal — domain event within a single BC; not published to `CritterBids.Contracts`
- 🔵 Integration — published to `CritterBids.Contracts`; crosses BC boundaries via message bus

---

## Participants

| Event | Type | Meaning |
|---|---|---|
| `ParticipantSessionStarted` | 🔵 Integration | An anonymous session was created. Participant received a generated display name and a hidden credit ceiling. |
| `SellerRegistrationCompleted` | 🔵 Integration | A participant completed the one-time seller registration gate. They may now create listings. |
| `ParticipantSessionEnded` | 🔵 Integration | An active participant session was terminated (timeout or explicit). |

---

## Selling

| Event | Type | Meaning |
|---|---|---|
| `DraftListingCreated` | 🟠 Internal | A seller started a new listing in draft state. |
| `DraftListingUpdated` | 🟠 Internal | A seller saved changes to a draft listing. |
| `ListingSubmitted` | 🟠 Internal | A seller submitted a listing for approval. |
| `ListingApproved` | 🟠 Internal | The platform approved the listing for publication. (Automated in MVP.) |
| `ListingRejected` | 🟠 Internal | The platform rejected the listing submission. |
| `ListingPublished` | 🔵 Integration | A listing is now live on the platform. Starting bid, reserve, Buy It Now, duration, and extended bidding config are established here. Settlement consumes this event to populate its `PendingSettlement` projection (caching the reserve, BIN price, fee percentage, and seller ID for later resolution). |
| `ListingRevised` | 🔵 Integration | A seller updated an already-published listing. Settlement updates the corresponding `PendingSettlement` row's mutable fields, but never re-reads platform fee config — the fee is fixed at publish time. |
| `ListingEndedEarly` | 🔵 Integration | A seller ended an active listing before its scheduled close. |
| `ListingRelisted` | 🔵 Integration | A passed or ended listing was relisted for a new auction period. |

---

## Auctions

| Event | Type | Meaning |
|---|---|---|
| `SessionCreated` | 🔵 Integration | An Operations staff member created a Flash Session container. Consumed by Operations (session management board) and Relay (announce upcoming session). |
| `ListingAttachedToSession` | 🔵 Integration | A published listing was assigned to a Flash Session. Consumed by Operations (display session lineup) and Listings (mark listing as part of an upcoming session). |
| `SessionStarted` | 🔵 Integration | An Operations staff member started a Flash Session; all attached listings open simultaneously. Consumed by Operations (dashboard goes live), Relay (announce session is live to participants), and Listings (attached listings become active in catalog). A dedicated Wolverine handler reacts to this event by producing one `BiddingOpened` per attached listing (fan-out pattern, see W002 Phase 1). |
| `BiddingOpened` | 🔵 Integration | A listing is now accepting bids. The scheduled close time is established. |
| `BidPlaced` | 🔵 Integration | A bid was accepted as the new high bid. Contains the amount, bidder ID, and whether it was a proxy auto-bid. |
| `BidRejected` | 🔵 Integration | A bid attempt was rejected — below current high bid, credit ceiling exceeded, or listing not open. Stored in a separate stream from the listing's primary stream so the DCB tag query stays lean (W002 Phase 2). Tagged with `ListingId` for Operations and Relay consumption. |
| `ProxyBidRegistered` | 🟠 Internal | A participant registered a proxy bid with a maximum amount. The Proxy Bid Manager saga begins. |
| `ProxyBidExhausted` | 🔵 Integration | A proxy bid's maximum was exceeded by another bidder. The proxy saga terminates. Consumed by Relay to push a specific "your proxy bid has been exceeded" notification to the bidder, distinct from the generic outbid notification. |
| `BuyItNowOptionRemoved` | 🔵 Integration | The Buy It Now option was removed from the listing because a bid was placed. |
| `BuyItNowPurchased` | 🔵 Integration | A participant purchased a listing at the Buy It Now price, bypassing the auction. Consumed by Settlement, which initiates the workflow with `Source: BuyItNow` (skipping the reserve check phase entirely). |
| `ReserveMet` | 🔵 Integration | The current high bid has reached or exceeded the reserve price. **This is the real-time UX signal** — produced by the Auctions DCB handler atomically with `BidPlaced`, consumed by Relay for the "Reserve met!" badge. **Auctions' `ReserveMet` is not authoritative for settlement** — Settlement performs its own binding comparison via `ReserveCheckCompleted` using the reserve cached in its `PendingSettlement` projection. The two should never disagree in practice (same source data), but if they did, Settlement wins. See W001 #5 (resolved across W002 + W003). |
| `ExtendedBiddingTriggered` | 🔵 Integration | A bid arrived within the seller-configured trigger window before close. The close time is extended. Subject to a per-listing `MaxDuration` cap (platform default in MVP, e.g., 2× original duration) to prevent runaway extensions. |
| `BiddingClosed` | 🟠 Internal | The mechanical fact that bidding has stopped. Distinct from the business outcome. Used internally by the Auction Closing saga to evaluate reserve and declare a result. |
| `ListingSold` | 🔵 Integration | A listing closed with a winning bidder who met the reserve. This is the happy-path business outcome. Consumed by Settlement, which initiates the workflow with `Source: Bidding`. |
| `ListingPassed` | 🔵 Integration | A listing closed without a winning bidder — either no bids were placed, or the reserve was not met. Consumed by Settlement's `PendingSettlement` projection, which marks the row `Expired` (no settlement workflow runs). |
| `ListingWithdrawn` | 🔵 Integration | A listing was forcibly withdrawn by Operations staff. Consumed by Settlement's `PendingSettlement` projection (marks row `Expired`), the Auction Closing saga (terminates without sold/passed evaluation), and any active Proxy Bid Manager sagas on the listing (terminate immediately). |

---

## Listings

| Event | Type | Meaning |
|---|---|---|
| `LotWatchAdded` | 🔵 Integration | A participant added a listing to their watchlist. |
| `LotWatchRemoved` | 🔵 Integration | A participant removed a listing from their watchlist. |

> Listings BC is a pure consumer. Its read models are built from events produced by Selling and Auctions. It does not produce domain events that originate here.

---

## Settlement

| Event | Type | Meaning |
|---|---|---|
| `SettlementInitiated` | 🟠 Internal | The settlement workflow started in response to `ListingSold` or `BuyItNowPurchased`. Carries a `Source` field (`Bidding \| BuyItNow`) that drives the evolver's initial state branching: `Bidding` produces `Initiated` state (proceeds to reserve check), `BuyItNow` produces `ReserveChecked(WasMet: true)` state directly (skips reserve check entirely). The absence of a `ReserveCheckCompleted` event in a settlement's stream is therefore meaningful — it identifies the settlement as a Buy It Now purchase. |
| `ReserveCheckCompleted` | 🟠 Internal | Settlement compared the hammer price to the reserve value cached from `ListingPublished` in the `PendingSettlement` projection. **This is the binding financial check** — distinct from Auctions' real-time UX signal `ReserveMet`. Result is met or not met. Not produced for Buy It Now settlements (the BIN price is the agreed price regardless of reserve). |
| `WinnerCharged` | 🟠 Internal | The winning bidder was charged for the hammer price (or BIN price). In MVP this is a virtual credit debit recorded in Settlement's audit stream — the DCB in Auctions does not subtract this from the credit ceiling (the ceiling is a per-bid maximum, not a running balance). Post-MVP, when a real payment processor is wired in, this is where the actual charge call lives. |
| `FinalValueFeeCalculated` | 🟠 Internal | The platform's fee was calculated as a percentage of the hammer price. The fee percentage was captured into the `PendingSettlement` projection at `ListingPublished` time and is fixed for the life of the listing — if platform fees change, already-published listings retain the rate they were published under. Uses banker's rounding to 2 decimal places. |
| `SellerPayoutIssued` | 🔵 Integration | The seller's payout (hammer price minus final value fee) was recorded and issued. Consumed by Relay to notify the seller. |
| `PaymentFailed` | 🔵 Integration | A settlement workflow terminated in failure. Carries both `SettlementId` and `ListingId` explicitly so downstream consumers don't need a lookup. The only failure path in MVP is `Reason: "ReserveNotMet"` from Settlement's defense-in-depth check (an `Auctions`-published `ListingSold` whose hammer price is below the cached reserve — theoretically unreachable, but Settlement verifies independently). Consumed by Operations to flag the settlement for staff attention, and by Settlement's own `PendingSettlement` projection (marks row `Failed`). Post-MVP will introduce additional reasons when real payment processing is wired in. |
| `SettlementCompleted` | 🔵 Integration | All settlement steps resolved successfully. Carries `HammerPrice`, `FeeAmount`, and `SellerPayout` for downstream auditing. Triggers the Obligations saga. Also consumed by Settlement's own `PendingSettlement` projection (marks row `Consumed`). |

---

## Obligations

| Event | Type | Meaning |
|---|---|---|
| `PostSaleCoordinationStarted` | 🟠 Internal | The obligations saga started in response to `SettlementCompleted`. |
| `ShippingReminderSent` | 🟠 Internal | A scheduled shipping reminder was dispatched to the seller. |
| `DeadlineEscalated` | 🟠 Internal | The seller missed the shipping deadline; the saga escalated to staff review. |
| `TrackingInfoProvided` | 🔵 Integration | The seller provided a tracking number. Scheduled reminders are cancelled. |
| `DeliveryConfirmed` | 🟠 Internal | Delivery was confirmed (buyer confirmation or carrier signal). |
| `ObligationFulfilled` | 🔵 Integration | Both parties completed their post-sale obligations. The saga completes. |
| `DisputeOpened` | 🔵 Integration | A participant raised a dispute — missed deadline, non-delivery, or item condition. |
| `DisputeResolved` | 🔵 Integration | Operations staff resolved a dispute. Contains resolution type (refund, extension, closed). |

---

## Relay

Relay is a pure consumer — it routes events from all BCs outbound to participants via SignalR and notification channels. It does not produce domain events that originate within the Relay BC.

---

## Operations

| Event | Type | Meaning |
|---|---|---|
| `DemoResetInitiated` | 🟠 Internal | A staff member triggered a demo environment reset. Post-MVP. |

---

## Naming Conventions

These rules apply everywhere in CritterBids. See `docs/skills/csharp-coding-standards.md` for the C# implementation conventions.

- **Past tense** — events are facts that already happened: `BidPlaced` not `PlaceBid`
- **No "Event" suffix** — `ListingSold` not `ListingSoldEvent`
- **Aggregate ID is always the first property** — every event record must carry the ID of its owning aggregate as the first field
- **DateTimeOffset timestamps** — always `*At` suffix: `PlacedAt`, `SoldAt`, `InitiatedAt`
- **`IReadOnlyList<T>` for collections** — never `List<T>` or arrays on event records
- **`BiddingClosed` ≠ `ListingSold`** — the mechanical close (bidding stopped) is intentionally separate from the business outcome (listing sold or passed). Downstream BCs subscribe to outcome events, not the mechanical close.
- **`ReserveMet` ≠ `ReserveCheckCompleted`** — the real-time UX signal (Auctions) is intentionally separate from the binding financial check (Settlement). They use the same source data but have different authorities, different consumers, and different timing.
- **Meaningful event absence** — the absence of `ReserveCheckCompleted` from a settlement's event stream identifies it as a Buy It Now purchase. Some patterns rely on what's NOT in a stream as much as what is.
