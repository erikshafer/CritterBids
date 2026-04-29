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
