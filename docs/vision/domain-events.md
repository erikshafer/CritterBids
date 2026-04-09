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
| `ListingPublished` | 🔵 Integration | A listing is now live on the platform. Starting bid, reserve, Buy It Now, duration, and extended bidding config are established here. |
| `ListingRevised` | 🔵 Integration | A seller updated an already-published listing. |
| `ListingEndedEarly` | 🔵 Integration | A seller ended an active listing before its scheduled close. |
| `ListingRelisted` | 🔵 Integration | A passed or ended listing was relisted for a new auction period. |

---

## Auctions

| Event | Type | Meaning |
|---|---|---|
| `SessionCreated` | 🔵 Integration | An Operations staff member created a Flash Session container. Consumed by Operations (session management board) and Relay (announce upcoming session). |
| `ListingAttachedToSession` | 🔵 Integration | A published listing was assigned to a Flash Session. Consumed by Operations (display session lineup) and Listings (mark listing as part of an upcoming session). |
| `SessionStarted` | 🔵 Integration | An Operations staff member started a Flash Session; all attached listings open simultaneously. Consumed by Operations (dashboard goes live), Relay (announce session is live to participants), and Listings (attached listings become active in catalog). |
| `BiddingOpened` | 🔵 Integration | A listing is now accepting bids. The scheduled close time is established. |
| `BidPlaced` | 🔵 Integration | A bid was accepted as the new high bid. Contains the amount, bidder ID, and whether it was a proxy auto-bid. |
| `BidRejected` | 🔵 Integration | A bid attempt was rejected — below current high bid, credit ceiling exceeded, or listing not open. |
| `ProxyBidRegistered` | 🟠 Internal | A participant registered a proxy bid with a maximum amount. The Proxy Bid Manager saga begins. |
| `ProxyBidExhausted` | 🟠 Internal | A proxy bid's maximum was exceeded by another bidder. The proxy saga terminates. |
| `BuyItNowOptionRemoved` | 🔵 Integration | The Buy It Now option was removed from the listing because a bid was placed. |
| `BuyItNowPurchased` | 🔵 Integration | A participant purchased a listing at the Buy It Now price, bypassing the auction. |
| `ReserveMet` | 🔵 Integration | The current high bid has reached or exceeded the reserve price. Reserve is never revealed — only this signal is published. |
| `ExtendedBiddingTriggered` | 🔵 Integration | A bid arrived within the seller-configured trigger window before close. The close time is extended. |
| `BiddingClosed` | 🟠 Internal | The mechanical fact that bidding has stopped. Distinct from the business outcome. Used internally by the Auction Closing saga to evaluate reserve and declare a result. |
| `ListingSold` | 🔵 Integration | A listing closed with a winning bidder who met the reserve. This is the happy-path business outcome. |
| `ListingPassed` | 🔵 Integration | A listing closed without a winning bidder — either no bids were placed, or the reserve was not met. |
| `ListingWithdrawn` | 🔵 Integration | A listing was forcibly withdrawn by Operations staff. |

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
| `SettlementInitiated` | 🟠 Internal | The settlement saga started in response to `ListingSold` or `BuyItNowPurchased`. |
| `ReserveCheckCompleted` | 🟠 Internal | Settlement compared the hammer price to the opaque reserve value received from `ListingPublished`. Result is met or not met. (Note: the reserve check that produces this lives in Settlement, not Auctions.) |
| `WinnerCharged` | 🟠 Internal | The winning bidder's credit ceiling was debited for the hammer price. |
| `FinalValueFeeCalculated` | 🟠 Internal | The platform's fee was calculated as a percentage of the hammer price. |
| `SellerPayoutIssued` | 🔵 Integration | The seller's payout (hammer price minus final value fee) was recorded and issued. |
| `PaymentFailed` | 🔵 Integration | A settlement step failed — winner credit exhausted or other payment error. |
| `SettlementCompleted` | 🔵 Integration | All settlement steps resolved successfully. Triggers the Obligations saga. |

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
