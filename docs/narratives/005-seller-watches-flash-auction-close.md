---
slug: 005-seller-watches-flash-auction-close
status: draft
journey: seller
perspective: single-seller
scope: happy-path
bounded_contexts: [Auctions]
boundaries_touched: [Selling, Listings, Settlement, Participants, Relay]
slices_implemented: [2.3, 3.1, 3.3, 5.1, 5.2]
canonical_id: ListingId
---

# Seller Watches Flash Auction Close (Happy Path)

An Auctions-grain narrative. GreyOwl12 watches what the system does with the Vintage Mechanical Keyboard he published in narrative 004. Today is conference day; the operator is about to start the Flash session demo; SwiftFerret42 and BoldPenguin7 have scanned in (narratives 001 and 003 territory); the keyboard is one of three listings attached to the session. Over the next several minutes, GreyOwl12 watches bids roll in on his seller dashboard ($30, $35, then $55 in the trigger window), sees his confidential $50 reserve cross at SwiftFerret42's $55 retaliation, watches the close timer extend by 15 seconds, and sees the gavel fall on a $55 hammer. Narrative 002 picks up at the `ListingSold` integration-event commit and dramatises the settlement that follows.

The audit floor splits by Moment. Moment 1 (the session-start cascade — operator clicks Start Session, `SessionStarted` fan-outs trigger `BiddingOpened` events on each attached listing) is **forward-spec** because M4-S5 and M4-S6 have not shipped and no implementation prompts exist for them. The spec source for Moment 1 is W002 plus narrative 001 Setting paragraph 2's listing-time-field ground. Moments 2-4 audit against shipped M3 code at `src/CritterBids.Auctions/`; the M3-S2 through M3-S6 retros, plus M3-S5b (the auction-closing saga's terminal paths) and M4-S1 (auctions completion foundation decisions), are the design-time references.

Narrative 005 is CritterBids' first **observer-protagonist** narrative. GreyOwl12 does not act during the auction — he has no commands to send, no bids to place, no UI buttons to click that would change system state. He watches. The narrator's responsibility is rendering an observer-protagonist's experience while still dramatising the system's internal saga at finer grain than narrative 001 reached from the bidder side. Cross-narrative consistency with narrative 001 Moments 4-7 (the same auction from SwiftFerret42's window) is the principal new audit surface; the same domain events fire in both narratives, the same dollar amounts move, and the same gavel falls.

## Cast

- **GreyOwl12** — the seller, observer-protagonist. Continuing as protagonist from narrative 004 (where he registered, drafted, and published the keyboard plus the camera). Single protagonist; the narrative is told from his vantage. He does not act during the auction; he observes. The narrator dramatises what he perceives from his seller dashboard plus the saga-grain mechanics underneath.
- **The Auctions BC** — onstage across all four Moments. The state machine that drives the auction lifecycle (`BiddingOpened` → bids → `ExtendedBiddingTriggered` → `BiddingClosed` → `ListingSold`).
- **The Auctions-side `Listing` aggregate** — onstage in Moments 1-4. Distinct CLR type from the Selling-side `SellerListing`; both keyed on the same `ListingId` but tracking different state. The narrator names the distinction once for orientation.
- **The `BidConsistencyState`** — onstage in Moment 2. The DCB consistency record that the place-bid handler validates against. Carries the listing's bidding configuration plus accumulated state (current high bid, bid count, extended-bidding trigger evaluation).
- **The `AuctionClosingSaga`** — onstage in Moments 3 and 4. The saga that handles `ExtendedBiddingTriggered` (cancels and reschedules the close) and `CloseAuction` (emits the closing events).
- **SwiftFerret42** — offstage but named. Bidder from narrative 001 / 003. Her bids ($30 first, $55 retaliation) land on the keyboard's stream; GreyOwl12 sees her display name attached to bid notifications.
- **BoldPenguin7** — offstage but named. Bidder from narrative 001 / 003. Her $35 outbid lands as a bid notification GreyOwl12 perceives.
- **The Flash session aggregate** — forward-spec; onstage briefly in Moment 1 for the session-start cascade. Not yet implemented (M4-S5 territory).
- **The auction operator** — offstage. Starts the Flash session at Moment 1; otherwise out of frame.
- **Wolverine, Marten, RabbitMQ, the Wolverine outbox** — runtime primitives. Named in Setting and at saga / integration-event commit boundaries.
- **The integration events `BiddingOpened`, `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BiddingClosed`, `ListingSold`** — onstage at their respective Moments. The narrator names their cross-BC routing at Moment 4's commit boundary.
- **Listings BC** — offstage. Consumes `BiddingOpened` to flip `CatalogListingView.Status` to "Open"; consumes the closing events to flip to "Sold".
- **Settlement BC** — offstage. Will consume `ListingSold` at the Moment 4 commit boundary; this is narrative 002's entry point.
- **Participants BC** — offstage. Provides bidder credit-ceiling validation backing the DCB place-bid checks.
- **Relay BC** — offstage. Broadcasts bid pushes to bidder UIs (narrative 001 Moments 5-6 covered this from the bidder side); narrative 005 does not dramatise the broadcasts.
- **The Vintage Mechanical Keyboard listing** — onstage as the journey's subject. Listing-time fields inherited verbatim from narrative 004 Moment 2 and narrative 001 Setting paragraph 2.
- **The Vintage Folding Camera, the Pokemon Card, the Wooden Bowl** — offstage. The camera was withdrawn in narrative 004 Moment 5 and is not in the session; the Pokemon Card and Wooden Bowl are other sellers' listings in the Flash session lineup, in their own resolution paths but out of frame for GreyOwl12.

## Setting

Conference day at Nebraska.Code(). The Flash session demo is about to start. SwiftFerret42 has scanned in (narrative 001 Moment 1 territory); BoldPenguin7 has scanned in (narrative 003 territory); roughly forty other attendees have scanned in too. Three listings are attached to the operator's Flash session: GreyOwl12's Vintage Mechanical Keyboard, plus the Pokemon Card and Wooden Bowl from other sellers. The operator is at the SessionManager screen with the lineup finalised; he is moments from clicking Start Session.

GreyOwl12 is not at the conference floor. He is at home (or elsewhere) watching his seller dashboard, which streams real-time bid activity for his published listings. The dashboard is forward-spec UI for M6 frontend MVP; the narrative renders his perception of state-changes through it without designing the screen. The Vintage Folding Camera he withdrew in narrative 004 Moment 5 is no longer in his published list; only the keyboard appears with `Status: Published` and an indicator that it is attached to the demo session.

The system's MVP infrastructure is healthy. The Auctions BC's `Listing` aggregate is opened on `ListingPublished` consumption per the M3 lived behavior; the keyboard's `BidConsistencyState` exists and carries the keyboard's listing-time fields. The auction-closing saga's wiring is in place but the saga has not yet started for the keyboard (Flash listings start their saga on `BiddingOpened`, which fires at session start; the saga's start handler is part of the M4-S5 forward-spec). Wolverine is processing requests; Marten's event store on PostgreSQL is up; the cross-BC RabbitMQ queues (`listings-auctions-events`, `auctions-selling-events`, `listings-selling-events`, the Settlement-side queue Settlement consumes from) are draining cleanly.

The keyboard's listing-time fields are inherited verbatim from narrative 004 Moment 2 and narrative 001 Setting paragraph 2: title "Vintage Mechanical Keyboard", format Flash, starting bid $25.00, reserve $50.00 (confidential to GreyOwl12 + Settlement), BIN $100.00 (no bidder will exercise it), extended bidding enabled with 30-second trigger window and 15-second extension, FeePercentage 10.0, duration determined by session attachment (the operator's session timer set at five minutes; the keyboard's scheduled close lands at session-start plus five minutes).

The bid sequence the narrative dramatises is locked by narrative 001 Setting paragraph 4 and Moments 4-6: SwiftFerret42 places $30 (the first bid, anchored above the $25 starting); BoldPenguin7 outbids her with $35; SwiftFerret42 places $55 in the trigger window (which crosses both the reserve and the extended-bidding-trigger threshold simultaneously); no further bids land in the 15-second extension; the close timer fires once at the new time; the listing sells. Hammer price $55, bid count 3 at close. Cross-narrative consistency with narrative 001 is essential: the same dollar amounts, the same display names, the same temporal sequence.

The cleanest possible run, modulo the deliberate extended-bidding trigger: no bid rejections (every bid clears the credit ceiling and the above-current-high check on first try), no infrastructure hiccup, no DCB consistency-state drift, no saga drift on the close reschedule. The reserve crosses cleanly at SwiftFerret42's $55 bid; the extended-bidding trigger fires cleanly at the same bid; the close lands cleanly at session-start plus five minutes plus fifteen seconds.

## Moment 1: The keyboard goes live

**Implements:** slice 2.3 (forward-spec; M4-S5 / M4-S6 not yet shipped).

**Context.** The operator has finalised the demo session's listing lineup (the keyboard, the Pokemon Card, the Wooden Bowl) and is moments from clicking Start Session. SwiftFerret42 and BoldPenguin7 have scanned in (narratives 001 and 003 territory) and are watching the conference floor's lot board for the session to begin. GreyOwl12 is at home watching his seller dashboard; the keyboard sits in his published list with `Status: Published` and an indicator showing it is attached to the demo session, not yet open for bidding. The Auctions-side `Listing` aggregate for the keyboard exists from when the Selling-BC `ListingPublished` integration event was consumed on the day of publication; its state carries the keyboard's listing-time fields plus a "configured but not yet open" posture. The `BidConsistencyState` projection for the keyboard is similarly seeded from `ListingPublished` and is in the same configured-but-not-open state.

**Interaction.** The operator clicks Start Session. The Flash session aggregate (forward-spec; M4-S5 / M4-S6 territory; the `SessionStartedHandler` fan-out and `SessionMembershipHandler` projection updates have not shipped) emits `SessionStarted`. Per W002's session-start cascade design, the Auctions-side `SessionStartedHandler` consumes the event and fans out a `BiddingOpened` event for each listing attached to the session.

**Response.** The keyboard's Auctions-side `Listing` aggregate receives `BiddingOpened { ListingId: keyboard, OpenedAt: <now>, ScheduledCloseAt: <now + 5 minutes> }` appended to its stream. The aggregate's state transitions from "configured" to "open for bidding". The `BidConsistencyState` projection updates analogously, setting `BiddingStartedAt` and `ScheduledCloseAt` such that subsequent place-bid validations against the projection succeed for any in-window bid that meets the credit-ceiling and above-current-high checks.

The auction-closing saga for the keyboard starts on `BiddingOpened` consumption per the M3-S5 saga-skeleton wiring. The saga schedules a `CloseAuction` message to fire at the keyboard's `ScheduledCloseAt` (session-start + 5 minutes); this scheduled message is the saga's primary state. Until the timer fires or `ExtendedBiddingTriggered` cancels and reschedules it, the saga waits.

The Listings BC's `SessionMembershipHandler` (M4-S6 forward-spec) consumes `BiddingOpened` and updates the keyboard's `CatalogListingView.Status` from "Published" to "Open". Bidder-side dashboards (narratives 001 and 003 territory) reflect this change immediately; the lot board flashes the keyboard's status to bid-active. GreyOwl12's seller dashboard reflects the same status flip from his window.

The session-start cascade also fires `BiddingOpened` for the Pokemon Card and Wooden Bowl on their own streams, with their own scheduled-close timers and saga starts. Those listings are out of frame for narrative 005.

**Why this matters to the seller.** GreyOwl12's keyboard has crossed the threshold from a published-but-not-yet-bid-able listing to an active auction. The 5-minute close timer is now ticking; for the next several minutes (or longer if extended bidding triggers), the keyboard's price can climb. The reserve $50 he set days ago is still confidential — no bidder sees it; only he and Settlement know. The Buy It Now $100 he set is also live, but no bidder will exercise it. From his window, the journey from "I have something to sell" (narrative 004 closing) to "the system is selling it now" (narrative 005 opening) closes here.

### Things deliberately not included

- The Flash session aggregate's full lifecycle (creation by operator, listing attachment, session start, session end). M4-S5 territory; out of scope for narrative 005's seller-perspective. *(`separate-narrative`; future operator-perspective narrative.)*
- The session-start cascade's parallel fan-out semantics: how `SessionStarted` produces multiple `BiddingOpened` events, whether they fire in parallel or in sequence, what happens if one of them fails partway through the cascade. *(`implementation-detail`; M4-S5 design choices.)*

## Moment 2: The reserve crosses

**Implements:** slice 3.1 (place bid), slice 5.2 (reserve met).

**Context.** The keyboard is open for bidding from Moment 1. The `Listing` aggregate is in "open for bidding" state with `BiddingStartedAt: <session-start>` and `ScheduledCloseAt: <session-start + 5 minutes>`. The `BidConsistencyState` is configured with `StartingBid: $25.00`, `CurrentHighBid: 0`, `BidCount: 0`, `ReserveThreshold: $50.00`, `ReserveMet: false`, `BuyItNowAvailable: true`, `ExtendedBiddingEnabled: true`, `ExtendedBiddingTriggerWindow: 30 seconds`, `ExtendedBiddingExtension: 15 seconds`. The auction-closing saga is waiting on its scheduled `CloseAuction` message. SwiftFerret42 and BoldPenguin7 are about to start bidding from the conference floor; GreyOwl12 watches his seller dashboard.

**Interaction.** Three bid commands arrive at the Auctions BC over the next several minutes. Each runs through `PlaceBidHandler.HandleAsync`, which queries the keyboard's stream via the DCB (`FetchForWritingByTags<BidConsistencyState>`) to load the consistency state, evaluates rejection conditions, and either appends `BidRejected` to a `BidRejectionAudit` stream or appends acceptance events to the keyboard's primary stream tagged with `ListingStreamId`.

**Response.** SwiftFerret42's $30 bid arrives first. The state's `BidCount` is 0; the minimum-bid check uses `StartingBid` ($25); $30 >= $25 passes. None of the other rejection conditions trigger (the listing is open; the close has not fired; she is not the seller; her credit ceiling clears $30). The handler emits `BidPlaced { ListingId, BidId, BidderId: SwiftFerret42, Amount: $30, BidCount: 1, IsProxy: false, PlacedAt: <now> }`. Because `state.BuyItNowAvailable` is still true and this is the first bid, the handler also emits `BuyItNowOptionRemoved { ListingId, At: <now> }` — the BIN option is now gone for the rest of the auction's life. The reserve check sees $30 < $50 reserve, so `ReserveMet` does not emit. The trigger-window check sees `remaining ≈ 5 minutes` (well outside the 30-second window), so `ExtendedBiddingTriggered` does not emit. Both events commit atomically with the DCB consistency assertion firing at `SaveChanges`. GreyOwl12's seller dashboard refreshes: `CurrentHighBid: $30`, `BidCount: 1`, BIN indicator removed.

BoldPenguin7's $35 bid arrives next. The state now carries `BidCount: 1`, `CurrentHighBid: $30`, `BuyItNowAvailable: false` (BIN was removed at the first bid). The minimum-bid check uses `CurrentHighBid + Increment(CurrentHighBid)` = $30 + $1 = $31; $35 >= $31 passes. The handler emits `BidPlaced { ListingId, BidId, BidderId: BoldPenguin7, Amount: $35, BidCount: 2, IsProxy: false, PlacedAt: <now> }`. No `BuyItNowOptionRemoved` (BIN is already gone); $35 < $50 so no `ReserveMet`; remaining still well outside the trigger window so no `ExtendedBiddingTriggered`. Single-event commit. GreyOwl12's dashboard: `CurrentHighBid: $35`, `BidCount: 2`.

SwiftFerret42's $55 bid arrives in the trigger window — the close timer is roughly 25 seconds away from firing. The state carries `BidCount: 2`, `CurrentHighBid: $35`, `ReserveMet: false`. The minimum-bid check uses $35 + $1 = $36; $55 >= $36 passes. The handler emits three events in the same transaction. First, `BidPlaced { ListingId, BidId, BidderId: SwiftFerret42, Amount: $55, BidCount: 3, IsProxy: false, PlacedAt: <now> }`. Second, the reserve check sees `ReserveThreshold: $50`, `ReserveMet: false`, $55 >= $50 — so `ReserveMet { ListingId, Amount: $55.00, At: <now> }` emits. Third, the trigger-window check sees `remaining ≈ 25 seconds` (within the 30-second window) and `ExtendedBiddingEnabled: true`. The post-Phase-2.5 fix anchors `NewCloseAt = ScheduledCloseAt + extension` rather than `now + extension`, keeping the close monotone for any in-window bid. So `NewCloseAt = <original ScheduledCloseAt> + 15 seconds`. The handler emits `ExtendedBiddingTriggered { ListingId, PreviousCloseAt: <original>, NewCloseAt: <original + 15s>, TriggeredByBidderId: SwiftFerret42, TriggeredAt: <now> }`. Three events commit atomically.

GreyOwl12's seller dashboard refreshes one more time: `CurrentHighBid: $55`, `BidCount: 3`, **`ReserveMet: true`** (the indicator he could not see crossed; his confidential reserve is now satisfied), and the close timer extended by 15 seconds. No further bids land in the 15-second extension; the bid count is locked at 3 for the rest of the auction's life.

**Why this matters to the seller.** GreyOwl12 has just watched the keyboard's price cross the threshold he set at publish time. His confidential reserve $50 — never visible to bidders — has been quietly verified by the system at SwiftFerret42's $55 retaliation bid. Until this Moment, the keyboard could have closed without selling (passed-listing alternate-path-failure if the close timer fired with the high bid below $50). After this Moment, the sale is no longer contingent on the reserve question; it is contingent only on whether further bids extend the auction or the close fires cleanly. The auction-closing saga's terminal-path decision tree is now narrowed: from "sold OR passed" to "sold-at-current-or-higher-hammer". From his window, the moment his dashboard's reserve indicator flipped is the moment the keyboard became a guaranteed sale.

The extended-bidding trigger is the second journey-relevant beat of this Moment. SwiftFerret42's bid landed inside the 30-second trigger window — the system's signal that the bid arrived close enough to the close to warrant giving competitors more time. The 15-second extension is the system's policy for "if you bid this late, others get a chance to respond." From his window, the close timer he watched ticking down 25 seconds away from firing has just extended back to 40 seconds away.

### Things deliberately not included

- The DCB rejection paths: `ListingNotOpen`, `ListingClosed`, `SellerCannotBid`, `ExceedsCreditCeiling`, `BelowMinimumBid`. Each rejection appends `BidRejected` to a dedicated `BidRejectionAudit` stream rather than the listing's primary stream (narrative 001 Finding 010 territory). *(`alternate-path-failure`.)*
- The `BidRejectionAudit` stream's lifecycle (separate stream per listing, append-only, never deleted). *(`implementation-detail`; W002's audit-stream design choice.)*
- BoldPenguin7's possible re-entry in the extended-bidding window (a counter-bid above $55 within the 15-second extension that would re-trigger another extension and prolong the auction). None lands in narrative 001's locked sequence. *(`separate-narrative`; future trading-the-trigger-window narrative.)*

## Moment 3: The close timer extends

**Implements:** slice 5.1 (extended bidding triggered).

**Context.** The keyboard's `ExtendedBiddingTriggered` event committed in Moment 2's transaction. The event is now in flight to subscribers; the auction-closing saga is registered as one. The saga's state at Moment 3 entry: `(Id: ListingId, CurrentHighBid: $55.00, CurrentHighBidderId: SwiftFerret42, BidCount: 3, ReserveHasBeenMet: true, ScheduledCloseAt: <original session-start + 5 minutes>, OriginalCloseAt: <same>, ExtendedBiddingEnabled: true, Status: Active)`. The pending `CloseAuction` message scheduled in Moment 1 is still in the scheduled-message store, set to fire at the original close.

**Interaction.** Wolverine routes `ExtendedBiddingTriggered` to `AuctionClosingSaga.Handle(ExtendedBiddingTriggered)`. The saga's document is loaded by `ListingId` (the saga correlation key per M3-S5 OQ1 Path A: `Saga.Id = ListingId`).

**Response.** The saga's defensive guard checks `message.NewCloseAt > ScheduledCloseAt`. The post-Phase-2.5 fix at the emission site already enforces this invariant (the `TryComputeExtension` check `candidate <= state.ScheduledCloseAt` returns false), but the saga keeps the guard for defence-in-depth. The keyboard's `NewCloseAt = <original> + 15 seconds > <original> = ScheduledCloseAt`, so the guard passes.

The saga calls `CancelPendingCloseAsync(messageStore, ScheduledCloseAt, ct)`. This issues a `ScheduledMessageQuery` against the Wolverine scheduled-message store: a ±100-millisecond window around the original `ScheduledCloseAt`, filtered by message type `typeof(CloseAuction).FullName`. The narrow window isolates the one pending close for the keyboard without risking cross-listing cancellations if two listings happened to share a scheduled time. The pending `CloseAuction` is cancelled.

The saga then schedules a new `CloseAuction(ListingId: keyboard, ScheduledFor: NewCloseAt)` via `bus.ScheduleAsync(...)`. The new message lands in the scheduled-message store at the new close time (original + 15 seconds). The saga updates its state: `ScheduledCloseAt = NewCloseAt`, `Status = AuctionClosingStatus.Extended`.

GreyOwl12's seller dashboard reflects the new close time. The countdown that read 25 seconds during Moment 2's $55 bid now reads 40 seconds (5 of the 15-second extension elapsed during the saga handler's runtime, leaving 40 seconds; rough numbers — the dashboard's exact display depends on the polling cadence). The keyboard is still bid-able for the rest of the extension.

**Why this matters to the seller.** GreyOwl12 has just watched the system's extended-bidding policy fire on the keyboard's behalf. The 15-second extension is the protection-against-snipe guarantee: if SwiftFerret42 (or anyone) bid in the trigger window, the system gives competitors the same window-of-opportunity to respond. From his window, the close timer that he had been watching tick down has just reset to a longer time; the sale is not closing as soon as he expected. From the system's window, the saga has cancelled one pending `CloseAuction` and scheduled another, with no possibility of both firing (the narrow time-window query plus the named-method `NotFound(CloseAuction)` static-method safety net handle the race-condition edges per M3-S5b's OQ2 Path A discipline). The auction's terminal beat is now 15 seconds further away.

### Things deliberately not included

- The `ScheduledMessageQuery` ±100ms window's edge-case behavior: what happens if two listings' close timers fire within 100ms of each other (the narrow window prevents cross-listing cancellation, but the design choice is worth understanding for any future multi-extension session). *(`implementation-detail`; W002 + M3-S5b retro design choice.)*
- The named-method `NotFound(CloseAuction)` static safety net: handles the race where a `CloseAuction` arrives but the saga document has already been deleted by `MarkCompleted` from a terminal handler (BIN, withdraw). Not exercised in narrative 005's happy path. *(`implementation-detail`; M3-S5b OQ2 Path a defence.)*
- Re-entry in the 15-second extension. The narrative 001 sequence lands no further bids in the extension; if a counter-bid had landed, the saga would receive another `ExtendedBiddingTriggered` and reschedule again. *(`separate-narrative`; future trading-the-trigger-window narrative.)*

## Moment 4: The gavel falls

**Implements:** slice 3.3 (scheduled close → BiddingClosed → ListingSold).

**Context.** The keyboard's saga is in `Status: Extended`; the new `CloseAuction` message is in the scheduled-message store waiting to fire at `<original close + 15 seconds>`. No further bids land in the extension. SwiftFerret42's $55 stands as the keyboard's high bid; she is the high bidder; bid count is 3; reserve has been met. GreyOwl12 watches the close timer count down.

**Interaction.** The scheduled `CloseAuction(ListingId: keyboard, ScheduledFor: <original + 15s>)` fires. Wolverine routes it to `AuctionClosingSaga.Handle(CloseAuction)`. The saga's document is loaded by `ListingId`.

**Response.** The idempotency guard checks `Status == Resolved` (false; the saga is `Extended`); control passes. The saga emits `BiddingClosed(ListingId: keyboard, At: <now>)` to its outgoing-messages tuple — the bidding window is closed; no further bids will be accepted for the keyboard.

The saga then decides the terminal outcome. The decision tree: `BidCount > 0 && ReserveHasBeenMet`. The keyboard's saga state has `BidCount: 3` and `ReserveHasBeenMet: true`, so the `ListingSold` branch fires. The saga does not track `SellerId` on its own state (the start handler from M3-S5 chose not to capture it); it must load it at close time. The saga calls `session.Events.AggregateStreamAsync<Listing>(ListingId, ct)` to rebuild the Auctions-side `Listing` aggregate and read `SellerId` from its projected state — populated by `Apply(BiddingOpened)` when Moment 1's session-start cascade landed. The aggregate's `SellerId` reads as GreyOwl12.

The saga emits `ListingSold(ListingId: keyboard, SellerId: GreyOwl12, WinnerId: SwiftFerret42, HammerPrice: $55.00, BidCount: 3, SoldAt: <now>)` to the outgoing-messages tuple. The saga's state advances: `Status = AuctionClosingStatus.Resolved`. `MarkCompleted()` is called; Wolverine's saga persistence will delete the saga document on the next commit. The handler returns the outgoing-messages tuple.

Wolverine's transactional outbox dispatches `BiddingClosed` and `ListingSold` to the cross-BC RabbitMQ queues. The Listings BC consumes both events and updates the keyboard's `CatalogListingView`: `Status: "Sold"`, `WinnerId: SwiftFerret42's ParticipantId`, `HammerPrice: $55.00`. The Settlement BC consumes `ListingSold` and begins the settlement journey — this is narrative 002's Moment 1 entry point.

GreyOwl12's seller dashboard refreshes. The keyboard moves from his "Live" section to a "Sold" section. The hammer price $55.00 is displayed; the winner's display name "SwiftFerret42" is displayed; the bid count 3 is displayed. The auction is over from his window. The settlement saga is now in flight (narrative 002 territory); the post-fee $49.50 payout will land on his seller-side account when the saga emits `SellerPayoutIssued`.

**Why this matters to the seller.** GreyOwl12's keyboard has sold. The terminal outcome he could not have predicted at publish time — would the auction reach reserve, would extended bidding extend it, would a high bidder emerge — is now resolved. The hammer price $55.00 means he will receive $49.50 after the platform's 10% fee (per narrative 002's settlement walkthrough); a non-trivial outcome above his $50 reserve. SwiftFerret42 is the winner; she will be charged in narrative 002 Moment 3. From his window, the journey from "I have something to sell" (narrative 004's opening) to "the system has sold it for me" closes here. The settlement that will land him his payout is narrative 002's responsibility; narrative 005 hands off cleanly at the `ListingSold` integration-event commit boundary.

The five narratives covering this auction stack: narrative 001 dramatised the bidder-perspective on this same auction (SwiftFerret42's QR scan through her settlement charge); narrative 002 dramatises the settlement that follows the gavel-fall; narrative 003 dramatised BoldPenguin7's session-start (her competitor-perspective on the same Flash session); narrative 004 dramatised GreyOwl12's listing-publication weeks earlier; narrative 005 closes the auction half. Five perspectives, one keyboard.

### Things deliberately not included

- The `ListingPassed` terminal branches (`ReserveNotMet` if reserve had not been met with bids; `NoBids` if no bids had landed). Not exercised in narrative 005's happy path. *(`alternate-path-failure`.)*
- The `NotFound(CloseAuction)` static safety net's invocation path (a `CloseAuction` arrives after the saga document has been deleted by `MarkCompleted`). Not exercised here either; the saga is intact when the close fires. *(`implementation-detail`; M3-S5b OQ2 Path a defence.)*
- The Listings BC's `CatalogListingView` projection-handler logic in detail. *(`separate-narrative`; covered in narrative 001 Moment 7 from the bidder's window at coarser grain.)*

## Deferred from this narrative

The following were deliberately not narrated in this Auctions-perspective happy-path narrative. Each is named with its disposition. Cross-Moment duplications are consolidated.

### `defer` (revisit when trigger lands)

- Lived-code audit of the M4-S5 / M4-S6 session-start cascade (Moment 1; trigger: M4-S5 ships the Auctions-side `SessionStartedHandler` fan-out and M4-S6 ships the Listings-side `SessionMembershipHandler`).

### `separate-narrative` (other journey perspectives)

- The Flash session aggregate's full lifecycle: creation by operator, listing attachment, session start, session end (Moment 1; future operator-perspective narrative; M4-S5 territory).
- Bidder re-entry in the 15-second extension; trading-the-trigger-window beats where successive bids re-trigger extensions and prolong the auction (Moments 2 and 3; future trading-the-trigger-window narrative).
- The Listings BC's `CatalogListingView` projection-handler logic in detail (Moment 4; covered in narrative 001 Moment 7 from the bidder's window at coarser grain).

### `implementation-detail` (skill file or ADR territory)

- The session-start cascade's parallel fan-out semantics (Moment 1; M4-S5 design choices).
- The `BidRejectionAudit` stream's lifecycle: separate stream per listing, append-only, never deleted (Moment 2; W002's audit-stream design choice).
- The `ScheduledMessageQuery` ±100ms window edge-case behavior (Moment 3; W002 + M3-S5b retro design choice).
- The `NotFound(CloseAuction)` static safety net (Moments 3 and 4; M3-S5b OQ2 Path a defence; not exercised in the happy path).

### `alternate-path-failure` (failure modes warranting their own narratives)

- The DCB rejection paths: `ListingNotOpen`, `ListingClosed`, `SellerCannotBid`, `ExceedsCreditCeiling`, `BelowMinimumBid` (Moment 2; each rejection appends `BidRejected` to a dedicated `BidRejectionAudit` stream rather than the listing's primary stream).
- The `ListingPassed` terminal branches: `ReserveNotMet` (bids exist but reserve unmet), `NoBids` (no bids landed) (Moment 4).

## Retrospective

### Narrative intent vs. outcome

Stated goal at session start: author the Auctions BC's backfill narrative covering GreyOwl12's seller-perspective experience as the keyboard goes to auction in the demo Flash session. Audit W002, lived `src/CritterBids.Auctions/` code, and narrative 001 Moments 4-7 for cross-narrative consistency. Route disagreements through the four-lane findings discipline. Add per-row narrative back-references on W001 (slices 2.3, 3.1, 3.3, 5.1, 5.2) and a new Narrative Cross-References section on W002.

**Outcome.** Four Moments covering W001 slices 2.3 (forward-spec; Moment 1), 3.1 (place bid; Moment 2 multi-bid cascade), 5.2 (reserve met; Moment 2 sub-beat), 5.1 (extended bidding triggered; Moment 3), 3.3 (scheduled close; Moment 4). Mixed posture: forward-spec Moment 1 (M4-S5/S6 unshipped, no implementation prompts exist; spec source is W002 + narrative 001 Setting paragraph 2), lived Moments 2-4 against shipped M3 code. **Zero new findings surfaced** — the lived M3 code matches W002 + retros; narrative 001's Finding 011 (the `TryComputeExtension` bug) was verified as fixed in place via Phase 2.5 PR #14; Finding 012 (saga loads `SellerId` via `AggregateStreamAsync`) was already routed `document-as-intentional` in narrative 001 and the lived inline comment preserves the design rationale; cross-narrative consistency with narrative 001 Moments 4-7 holds (same bidders, same dollar amounts, same sequence). W002 confirmed clean against ADR 011 (zero Polecat / SQL Server references; same as W004; in contrast to W003). Slice 5.2 (`ReserveMet`) lifted from "P1 / forward-spec per narrative 001 Moment 7" to "shipped lived" via the `PlaceBidHandler.cs:126` lived emission. The five-narrative stacking pattern on the keyboard (narratives 001 bidder-spine, 002 settlement, 003 BoldPenguin7's session-start, 004 GreyOwl12's listing-publication, 005 this auction-close) made explicit at Moment 4's close. Cast and Setting locked first; Moment-by-Moment sign-off cadence held throughout. Goal met.

### What worked

- **Pre-Moment surrounding-directory reads + code-comment-as-routing-evidence (from narrative 004) confirmed F012's intentional design** quickly. Reading `AuctionClosingSaga.cs:103-105`'s inline comment about why `SellerId` isn't tracked on saga state preserved the design rationale and avoided re-surfacing F012 as a new finding.
- **Path-citation pre-check (from narrative 004) caught zero issues at prompt-author time.** The discipline's small win: confirmed M4-S5/M4-S6 prompts do not exist; confirmed W002 / `002-scenarios.md` paths; confirmed all M3 retro filenames before citing.
- **Observer-protagonist Voice held throughout.** GreyOwl12's window is structurally passive (he watches state changes; he doesn't act). The narrator carries saga-grain mechanics; the protagonist carries journey-grain through observation. The two responsibilities split cleanly per-Moment without the narrator over-narrating internals or under-narrating to the point of reading as a thin journal.
- **Cross-narrative consistency with narrative 001 Moments 4-7 held without drift.** Same bid sequence ($30 → $35 → $55), same bidders, same trigger-window behavior, same gavel-fall outcome. The audit confirmed narrative 001 was already accurate at the bidder grain; narrative 005 added Auctions-saga-grain detail that narrative 001 didn't reach.
- **Mixed-posture pattern (narrative-004 lesson) carried clean.** One forward-spec Moment in a four-lived-Moment journey worked exactly as the inherited pattern predicted. The narrator's grain shifted from "renders the design from W002" (Moment 1) to "renders the lived behavior" (Moments 2-4) without friction.
- **Em-dash hygiene drop continued without friction.** No audit step; em-dashes used naturally throughout. Path-citation pre-check confirmed zero path drift.

### What was hard

- **Verifying the post-Phase-2.5 F011 fix required a broader code search than the original method name.** A grep for `TryComputeExtension` returned nothing on `PlaceBidHandler.cs` at first; needed to broaden to `ExtendedBidding` and read the file. The method is still named `TryComputeExtension` per the lived code at line 177; the early grep failure was an artifact of glob-pattern + line-context behavior, not the code's actual shape. Lesson for future post-fix audits: verify with multiple search patterns (method name, surrounding behavior, comment fragments) before concluding a fix has been moved or renamed.
- **The four Moments span very different abstractions.** Moment 1 is system-cascade (operator action triggering fan-out); Moment 2 is DCB-plus-events (handler validating against consistency state); Moment 3 is saga-state-machine (cancel-and-reschedule); Moment 4 is saga-terminal-decision-tree-plus-cross-BC-handoff. The narrator's grain shifted across Moments more than in earlier narratives. The Moment titles ("The keyboard goes live", "The reserve crosses", "The close timer extends", "The gavel falls") did the grain-shift markers' work, but the body's pacing varied accordingly.

### Decisions about how to author (meta-decisions worth carrying forward)

- **Observer-protagonist Voice is a real narrative-authoring option.** Complementary to active-protagonist (narratives 001 / 003 / 004). The narrator's responsibility-split (protagonist's window ↔ saga-internal dramatisation) is the defining technique for any narrative whose protagonist's role is structurally passive (watching, monitoring, awaiting outcome).
- **Cross-narrative consistency audits are a standalone audit surface.** When a narrative overlaps in domain time / event with prior narratives, the audit confirms the narratives render the same domain events, dollar amounts, and sequences consistently. Drift surfaces as `narrative-update` against the older narrative; non-drift outcomes (like narrative 005's clean check against narrative 001 Moments 4-7) confirm the project's narrative library is internally coherent.
- **Zero-findings outcomes are valid.** Narrative 005 surfaced no new findings. Earlier narratives (001 with twelve, 002 with five, 003 with two, 004 with three) covered the audit territory; narrative 005's audit verified post-fix state and cross-narrative consistency rather than finding fresh drift. The `005-findings.md` file is consciously skipped per the prompt's acceptance criterion ("OR the narrative-internal retro contains an explicit conscious-skip note with rationale"); this section is that note.

### Closing the lived-BC narrative wave

Narrative 005 closes the lived-BC backfill series. Across narratives 002-005 (the four Phase 5 Item 1 backfills), the project authored:

- **Narrative 002** — Settlement BC, fully forward-spec. Five findings: F001 narrative-update against narrative 001 Moment 8 saga-event payload corrections (resolved in-PR); F002 / F004 / F005 routed to a W003 follow-up PR (deferred); F003 W003 storage-staleness against ADR 011 (resolved in-PR minimum-scope sweep).
- **Narrative 003** — Participants BC, fully lived. Two findings: F001 lived-comment misclaim correction (resolved in-PR); F002 missing `GET /api/participants/{id}` endpoint (stub follow-up at `n003-fu-get-participant-endpoint.md`).
- **Narrative 004** — Selling BC, mixed lived M2 + forward-spec M4-S2. Three findings: F001 hardcoded FeePercentage placeholder (`document-as-intentional`); F002 missing `SubmitListing` HTTP endpoint (stub follow-up at `n004-fu-submit-listing-endpoint.md`); F003 missing `Approved` intermediate state (`document-as-intentional`).
- **Narrative 005** — Auctions BC, mixed forward-spec M4-S5/S6 + lived M3+M4-S1. Zero new findings; F011 / F012 from narrative 001 verified.

The four backfill narratives plus narrative 001 (the cross-BC bidder spine) and narrative 002 (settlement after gavel) constitute CritterBids' five-narrative library. The library covers all four lived BCs (Participants, Selling, Auctions plus the read-side Listings) plus Settlement (forward-spec) plus the cross-BC integration boundaries between them.

### Quality signal from the session

User feedback clean throughout. No Moment titles needed revision. Zero findings surfaced is a quiet outcome rather than a problem (the cumulative coverage from earlier narratives plus the post-Phase-2.5 fix landing was the right stage to expect a clean audit). Em-dash hygiene drop continued. Path-citation pre-check confirmed zero path drift. Observer-protagonist Voice held without slips into active-protagonist framing.

### Follow-ups generated

- **No findings = no per-finding follow-ups beyond what narratives 001-004 already generated.** F011's Phase 2.5 fix is in place. F012's `document-as-intentional` routing is preserved. F002 stubs from narratives 003 and 004 remain queued.
- **Methodology log Entry 001 written** at session close (separate commit). Captures the audit-floor heterogeneity observation across narratives 002-005: lived + forward-spec mixing within a single narrative is the structurally expected mode rather than an exception. The entry-criteria gate from Phase 4 retro time-box closed positively at the final lived-BC chance.
- **Phase 5 Item 4 (cutover gate) becomes the next session.** M5 milestone doc + M5-S1 prompt + Phase 5 cross-narrative retrospective. The five-narrative library is now ready to anchor M5-S1's narrative citation per AUTHORING.md rule 3's joint-authority clause.

### Narrative status

**Complete (v0.1, 2026-04-29).** Four Moments, cumulative deferred section, retrospective. Format conventions inherited from narratives 001-004. Mixed-posture pattern, observer-protagonist Voice, and zero-findings outcome established as new precedents for any future narrative authoring. Status flipped to `accepted` in the session-close commit.

---

## Document History

- **v0.1** (2026-04-29): Initial authoring as foundation-refresh Phase 5 Item 1d deliverable. Closes the lived-BC narrative backfill wave. Four Moments covering W001 slices 2.3 (forward-spec; Moment 1), 3.1 / 5.2 (Moment 2 multi-bid with reserve cross), 5.1 (Moment 3 extended bidding trigger), 3.3 (Moment 4 gavel-fall and cross-BC handoff to narrative 002). First observer-protagonist narrative for CritterBids. Mixed posture: forward-spec Moment 1 for M4-S5/S6 session-start cascade (no implementation prompts exist; spec source is W002 + narrative 001 Setting paragraph 2); lived M3 Moments 2-4. Zero new findings; `005-findings.md` consciously skipped per the prompt's acceptance criterion (the narrative-internal retro carries the skip note). F011 (`TryComputeExtension` bug from narrative 001) verified as fixed in place via Phase 2.5 PR #14. F012 (saga loads `SellerId` via `AggregateStreamAsync`) routed `document-as-intentional` in narrative 001 and the lived inline comment preserves the design rationale. W002 confirmed clean against ADR 011. Slice 5.2 (`ReserveMet`) lifted from "P1 / forward-spec per narrative 001" to "shipped lived". Five-narrative stacking pattern on the keyboard made explicit. Methodology log Entry 001 written at session close in a separate commit.
