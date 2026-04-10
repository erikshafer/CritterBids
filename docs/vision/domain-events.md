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
| `SellerRegistrationCompleted` | 🔵 Integration | A participant completed the one-time seller registration gate. They may now create listings. Consumed by Selling's `RegisteredSellers` projection, which is queried by `CreateDraftListing` to verify seller eligibility. |
| `ParticipantSessionEnded` | 🔵 Integration | An active participant session was terminated (timeout or explicit). |

---

## Selling

**Supporting types:**

- **`ListingFormat` enum:** `Timed | Flash`. `Timed` listings carry their own duration and open for bidding immediately on publish. `Flash` listings have `Duration: null` and require session attachment before bidding can open. The format is chosen at draft creation and cannot be changed.

| Event | Type | Meaning |
|---|---|---|
| `DraftListingCreated` | 🟠 Internal | A seller started a new listing in draft state. Carries `Format` (`Timed \| Flash`). May carry optional `RelistedFromListingId` for analytics when the draft originated as a relist template. |
| `DraftListingUpdated` | 🟠 Internal | A seller saved changes to a draft listing. **Explicit save only** — no auto-save mechanism in MVP (W004 Phase 2 #1). Every update runs the full validator; invalid changes are rejected and no event is produced. |
| `ListingSubmitted` | 🟠 Internal | A seller submitted a listing for approval. |
| `ListingApproved` | 🟠 Internal | The platform approved the listing for publication. Automated in MVP via a single handler chain that produces `ListingSubmitted + ListingApproved + ListingPublished` atomically. The `Approved` state is transient in MVP but is modeled as a real state for post-MVP migration to manual approval without event vocabulary changes (W004 Phase 1 resolves W001 #14). |
| `ListingRejected` | 🟠 Internal | The platform rejected the listing submission. Seller can re-edit and re-submit (Rejected → Draft via `DraftListingUpdated`). |
| `ListingPublished` | 🔵 Integration | A listing is now live on the platform. **This is the load-bearing integration event for the entire system.** Carries: `SellerId`, `Title`, `Description`, `Format` (`Timed \| Flash`), `StartingBid`, `ReservePrice` (confidential), `BuyItNowPrice`, `FeePercentage` (captured from platform config at publish time and fixed for the life of the listing), `Duration` (required for Timed, null for Flash), extended bidding config, `ShippingTerms`, `PublishedAt`. Settlement consumes this to populate `PendingSettlement`. Auctions consumes this to cache listing config and (for Timed listings) schedule `BiddingOpened` immediately. Listings consumes this for the catalog projection but must NOT read `ReservePrice`. **Invariant guaranteed at publish time:** `BuyItNowPrice >= ReservePrice >= StartingBid` (W004 Phase 1 resolves W003 cross-BC #4). |
| `ListingRevised` | 🔵 Integration | A seller updated an already-published listing. **Restricted to mutable fields only: `Title`, `Description`, `ShippingTerms`.** All other parameters (prices, duration, extended bidding config, format, fee) are immutable post-publish. Sellers who need to change critical parameters must `EndListingEarly` and `Relist` (which creates a new aggregate). Listings BC's catalog projection ignores revisions for listings in an active session (seller sees the revision succeed but participants don't see the change until the session ends — W004 Phase 2 #6). Settlement's `PendingSettlement` projection is effectively a no-op on this event because none of the mutable fields are cached there. |
| `ListingEndedEarly` | 🔵 Integration | A seller ended an active listing before its scheduled close. **Distinct from `ListingWithdrawn`** (which is ops-initiated) — the two events differ in audit trail and notifications but trigger identical saga termination logic in Auctions (the Auction Closing saga and any active Proxy Bid Manager sagas terminate immediately). Sellers who end early after bids exist do NOT receive payment — Settlement marks `PendingSettlement` as `Expired` same as `ListingPassed`. Rejected at the API gateway layer if the listing is already resolved (BIN purchased or normally sold) — returns HTTP 409 (W004 Phase 2). |
| `ListingRelisted` | 🔵 Integration | A passed, ended, or withdrawn listing was relisted. **Creates a new `SellerListing` aggregate with a new `ListingId`** — carries both `OriginalListingId` and `NewListingId`. The event is appended as a marker on the original listing's stream (which remains in its terminal state) and provides a forward link. The new listing is a new agreement at current platform rates — `FeePercentage` is read fresh from platform config, not copied from the original (W004 Phase 2 #5). The seller's relist UX pre-fills a new draft form from the original's values; they can edit before submitting. |

**Projections owned by Selling:**
- **`RegisteredSellers`** — built from `SellerRegistrationCompleted` events received from Participants BC. Small lookup table used by `CreateDraftListing` to verify seller eligibility. On miss (race condition if the projection hasn't caught up), the handler throws and Wolverine retries with backoff (same pattern as W003's `PendingSettlement`).

---

## Auctions

| Event | Type | Meaning |
|---|---|---|
| `SessionCreated` | 🔵 Integration | An Operations staff member created a Flash Session container. Consumed by Operations (session management board) and Relay (announce upcoming session). |
| `ListingAttachedToSession` | 🔵 Integration | A published listing was assigned to a Flash Session. Consumed by Operations (display session lineup) and Listings (the catalog projection transitions the listing from `Hidden` to `Visible (Upcoming)` — Flash listings are hidden from the participant catalog until this event fires, per W001 #1 / W004 Phase 1). |
| `SessionStarted` | 🔵 Integration | An Operations staff member started a Flash Session; all attached listings open simultaneously. Consumed by Operations (dashboard goes live), Relay (announce session is live), and Listings (attached listings become active in catalog). A dedicated Wolverine handler reacts to this event by producing one `BiddingOpened` per attached listing (fan-out pattern, W002 Phase 1). |
| `BiddingOpened` | 🔵 Integration | A listing is now accepting bids. The scheduled close time is established. Carries `ListingFormat` for downstream consumers that branch on format. |
| `BidPlaced` | 🔵 Integration | A bid was accepted as the new high bid. Contains the amount, bidder ID, and whether it was a proxy auto-bid. |
| `BidRejected` | 🔵 Integration | A bid attempt was rejected — below current high bid, credit ceiling exceeded, or listing not open. Stored in a separate stream from the listing's primary stream so the DCB tag query stays lean (W002 Phase 2). Tagged with `ListingId` for Operations and Relay consumption. |
| `ProxyBidRegistered` | 🟠 Internal | A participant registered a proxy bid with a maximum amount. The Proxy Bid Manager saga begins. |
| `ProxyBidExhausted` | 🔵 Integration | A proxy bid's maximum was exceeded by another bidder. The proxy saga terminates. Consumed by Relay to push a specific "your proxy bid has been exceeded" notification, distinct from the generic outbid notification. |
| `BuyItNowOptionRemoved` | 🔵 Integration | The Buy It Now option was removed from the listing because a bid was placed. |
| `BuyItNowPurchased` | 🔵 Integration | A participant purchased a listing at the Buy It Now price, bypassing the auction. Consumed by Settlement, which initiates the workflow with `Source: BuyItNow` (skipping the reserve check phase entirely — the `BIN >= Reserve` invariant enforced at publish time in Selling guarantees this is safe). |
| `ReserveMet` | 🔵 Integration | The current high bid has reached or exceeded the reserve price. **This is the real-time UX signal** — produced by the Auctions DCB handler atomically with `BidPlaced`, consumed by Relay for the "Reserve met!" badge. **Not authoritative for settlement** — Settlement performs its own binding comparison via `ReserveCheckCompleted` using the reserve cached in its `PendingSettlement` projection. The two should never disagree in practice (same source data), but if they did, Settlement wins. See W001 #5 (resolved across W002 + W003). |
| `ExtendedBiddingTriggered` | 🔵 Integration | A bid arrived within the seller-configured trigger window before close. The close time is extended. Subject to a per-listing `MaxDuration` cap (platform default in MVP, e.g., 2× original duration) to prevent runaway extensions. |
| `BiddingClosed` | 🟠 Internal | The mechanical fact that bidding has stopped. Distinct from the business outcome. Used internally by the Auction Closing saga to evaluate reserve and declare a result. |
| `ListingSold` | 🔵 Integration | A listing closed with a winning bidder who met the reserve. This is the happy-path business outcome. Consumed by Settlement, which initiates the workflow with `Source: Bidding`. |
| `ListingPassed` | 🔵 Integration | A listing closed without a winning bidder — either no bids were placed, or the reserve was not met. Consumed by Settlement's `PendingSettlement` projection, which marks the row `Expired` (no settlement workflow runs). |
| `ListingWithdrawn` | 🔵 Integration | A listing was forcibly withdrawn by Operations staff. **Distinct from `ListingEndedEarly`** (seller-initiated) but triggers the same saga handling: terminates the Auction Closing saga without sold/passed evaluation, and terminates any active Proxy Bid Manager sagas on the listing. Settlement's `PendingSettlement` projection marks the row `Expired`. |

---

## Listings

| Event | Type | Meaning |
|---|---|---|
| `LotWatchAdded` | 🔵 Integration | A participant added a listing to their watchlist. |
| `LotWatchRemoved` | 🔵 Integration | A participant removed a listing from their watchlist. |

> Listings BC is a pure consumer for catalog data. Its read models are built from events produced by Selling and Auctions. Per W004 Phase 1, the catalog projection hides Flash listings from the participant-facing view until `ListingAttachedToSession` fires, and the catalog projection MUST NOT read `ReservePrice` from `ListingPublished` (reserve confidentiality is discipline-enforced).

---

## Settlement

| Event | Type | Meaning |
|---|---|---|
| `SettlementInitiated` | 🟠 Internal | The settlement workflow started in response to `ListingSold` or `BuyItNowPurchased`. Carries a `Source` field (`Bidding \| BuyItNow`) that drives the evolver's initial state branching: `Bidding` produces `Initiated` state (proceeds to reserve check), `BuyItNow` produces `ReserveChecked(WasMet: true)` state directly (skips reserve check entirely). The absence of a `ReserveCheckCompleted` event in a settlement's stream is therefore meaningful — it identifies the settlement as a Buy It Now purchase. |
| `ReserveCheckCompleted` | 🟠 Internal | Settlement compared the hammer price to the reserve value cached from `ListingPublished` in the `PendingSettlement` projection. **This is the binding financial check** — distinct from Auctions' real-time UX signal `ReserveMet`. Result is met or not met. Not produced for Buy It Now settlements (the BIN price is the agreed price regardless of reserve, and Selling BC guarantees `BIN >= Reserve` at publish time). |
| `WinnerCharged` | 🟠 Internal | The winning bidder was charged for the hammer price (or BIN price). In MVP this is a virtual credit debit recorded in Settlement's audit stream — the DCB in Auctions does not subtract this from the credit ceiling (the ceiling is a per-bid maximum, not a running balance). Post-MVP, when a real payment processor is wired in, this is where the actual charge call lives. |
| `FinalValueFeeCalculated` | 🟠 Internal | The platform's fee was calculated as a percentage of the hammer price. The fee percentage was captured into `ListingPublished` at publish time (read from platform config by Selling BC) and flows through to `PendingSettlement` — it is fixed for the life of the listing. If platform fees change, already-published listings retain the rate they were published under. Uses banker's rounding to 2 decimal places. |
| `SellerPayoutIssued` | 🔵 Integration | The seller's payout (hammer price minus final value fee) was recorded and issued. Consumed by Relay to notify the seller. |
| `PaymentFailed` | 🔵 Integration | A settlement workflow terminated in failure. Carries both `SettlementId` and `ListingId` explicitly so downstream consumers don't need a lookup (W003 Phase 2 #9). The only failure path in MVP is `Reason: "ReserveNotMet"` from Settlement's defense-in-depth check (an Auctions-published `ListingSold` whose hammer price is below the cached reserve — theoretically unreachable, but Settlement verifies independently). Consumed by Operations to flag the settlement for staff attention, and by Settlement's own `PendingSettlement` projection (marks row `Failed`). Post-MVP will introduce additional reasons when real payment processing is wired in. |
| `SettlementCompleted` | 🔵 Integration | All settlement steps resolved successfully. Carries `HammerPrice`, `FeeAmount`, and `SellerPayout` for downstream auditing. Triggers the Obligations saga. Also consumed by Settlement's own `PendingSettlement` projection (marks row `Consumed`). |

**Projections owned by Settlement:**
- **`PendingSettlement`** — built from `ListingPublished` events received from Selling BC, with status transitions driven by resolution events (`SettlementCompleted` → `Consumed`, `PaymentFailed` → `Failed`, `ListingPassed`/`ListingWithdrawn`/`ListingEndedEarly` → `Expired`). Caches `SellerId`, `ReservePrice`, `BuyItNowPrice`, `FeePercentage`, and `PublishedAt` for use when the settlement workflow eventually runs. Status enum: `Pending | Consumed | Expired | Failed`.

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
- **`ListingEndedEarly` ≠ `ListingWithdrawn`** — seller-initiated vs ops-initiated terminations. Distinct events for audit clarity despite identical saga handling in Auctions.
- **Meaningful event absence** — the absence of `ReserveCheckCompleted` from a settlement's event stream identifies it as a Buy It Now purchase. Some patterns rely on what's NOT in a stream as much as what is.
- **API gateway cross-BC validation pattern** — when a command in one BC requires knowledge of another BC's state (e.g., "is this seller registered?", "is this listing already resolved?"), the validation lives at the API layer rather than inside either BC. BCs remain internally self-contained and do not subscribe to each other's events for validation purposes (W004 Phase 2).

---

## Cross-Workshop Design Patterns Referenced

Workshops are the source of truth for detailed design reasoning. Key patterns established:

- **W001** — 34 P0 slices; the user journey and milestone framing
- **W002** — DCB boundary model (`BidConsistencyState`), Auction Closing saga (5 states), Proxy Bid Manager saga (composite key from ListingId + BidderId)
- **W003** — Settlement decider-pattern workflow (7 states), `PendingSettlement` projection, the "design around decider semantics regardless of hosting" principle
- **W004** — `SellerListing` aggregate, automated approval chain (single-handler in MVP), `RegisteredSellers` projection, API gateway cross-BC validation pattern
