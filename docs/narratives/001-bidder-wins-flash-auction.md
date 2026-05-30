---
slug: 001-bidder-wins-flash-auction
status: accepted
journey: bidder
perspective: single-bidder
scope: happy-path
bounded_contexts: [Auctions, Listings]
boundaries_touched: [Participants, Selling, Settlement, Relay, Operations]
slices_implemented: [0.2, 1.3, 1.4, 2.3, 3.1, 3.3, 4.1, 4.3, 5.1, 6.1]
canonical_id: ListingId
---

# Bidder Wins a Flash Auction (Happy Path)

A spine narrative. SwiftFerret42 scans a conference QR code, browses the catalog, watches a Flash session start, places a bid, gets outbid, claws back the high-bid position in the trigger window, and the gavel falls in her favor. Settlement runs end-to-end and her credit is charged. Single bidder, single listing, no rejected bids, no payment failures, no reserve miss. Failure paths - the rejected bid, the listing that passes, the payment that fails, the obligation that stalls - belong to subsequent narratives, not as branches inside this story.

This narrative implements the happy-path P0 slices of the Flash demo journey (workshop 001, Tier 0 through Tier 6). It cites slice numbers; it does not restate the workshop's Given/When/Then scenarios. Three of the eight Moments (3, 5, 8) describe lifecycle features that have not yet shipped lived implementation: the Auctions-BC Flash session aggregate and `BiddingOpened` cascade scheduled for M4-S5/M4-S6 (Moment 3), the Relay BC's BiddingHub and Outbid pushes (Moment 5), and the Settlement BC's settlement saga (Moment 8). Those Moments narrate the journey as the system is designed to run; their lived-code audit is deferred under the `defer` disposition until those BCs and slices land.

## Cast

- **SwiftFerret42** - the bidder. Anonymous, system-named via the `<Adjective><Animal><Number>` convention, system-assigned `BidderId`, hidden credit ceiling, no prior session activity when the story opens. Single protagonist; the narrative is told entirely from her vantage.
- **BoldPenguin7** - the competing bidder. Offstage. SwiftFerret42 sees BoldPenguin7 only as a display name attached to a `BidPlaced` push and to the targeted `Outbid` push that follows. BoldPenguin7's credit ceiling, motivation, and post-loss experience are out of frame.
- **GreyOwl12** - the seller. Offstage. Drafted, submitted, and published the listing in advance of the session; never appears in the bidder's view. The seller's perspective on the same listing is a candidate for a future seller-perspective narrative.
- **The auction operator** - offstage. Created the Flash session, attached the listings, and started it. SwiftFerret42 perceives the operator's actions only as state changes in the catalog and the lot board.
- **Participants** - the bounded context that owns SwiftFerret42's anonymous session lifecycle, her `BidderId`, and her hidden credit ceiling. Onstage briefly in Moment 1.
- **Selling** - the bounded context that owned the listing through draft, submission, approval, and publish. Quiet in this narrative; its work is finished by the time the bidder arrives. Mentioned for context in Setting.
- **Auctions** - the bounded context that owns the auction lifecycle: `BiddingOpened`, `BidPlaced`, the bid-placement DCB, `ExtendedBiddingTriggered`, the Auction Closing saga, `BiddingClosed`, `ListingSold`. Onstage in Moments 3, 4, 6, and 7.
- **Listings** - the bounded context that owns the read-side: `CatalogListingView`, `ListingDetailView`. SwiftFerret42 reads from Listings throughout; Listings is onstage in Moments 2, 3, and 7.
- **Settlement** - the bounded context whose saga charges the winner, calculates the fee, and pays out the seller. Forward-spec only; no lived code yet. Onstage in Moment 8.
- **Relay** - the bounded context that owns the BiddingHub and Outbid SignalR pushes. Forward-spec only; no lived code yet. Onstage in Moments 5 and 6.

## Setting

A weekday afternoon at Nebraska.Code(). The conference floor is full; SwiftFerret42 has just scanned the QR code on a printed sign at the CritterBids booth. Her phone has loaded the public catalog page and her thumb is hovering over the listing she came for. The auction operator is two minutes from starting the demo Flash session. Roughly forty other attendees have already scanned in; several are seasoned bidders, several are first-time scanners, and one is BoldPenguin7. None of this is visible to SwiftFerret42; she sees a catalog and a clock.

Three listings have been published by sellers in the days before the conference and attached to the operator's Flash session: a Vintage Mechanical Keyboard, a Rare Pokemon Card, and a Hand-Carved Wooden Bowl. The narrative follows only the first. The keyboard was published by GreyOwl12 with a starting bid of $25.00, a confidential reserve of $50.00, and a Buy It Now of $100.00 that no one will exercise. Extended bidding is enabled on the keyboard with a thirty-second trigger window and a fifteen-second extension; these values were set on the listing at publish time and travel on `ListingPublished` to the Auctions BC at session start.

Auction-system policy is at MVP defaults. Flash sessions run for five minutes from start. Anonymous bidder sessions are minted with a randomly assigned credit ceiling drawn from one of nine values between $200 and $1000 in $100 steps, hidden from the bidder, enforced by the bid-placement DCB. The operator has set this session's duration at five minutes; the keyboard's scheduled close therefore lands at session-start plus five minutes. There are no surge effects, no rate limits in play, no manual interventions. SignalR delivery to all forty connected clients is healthy; the BiddingHub and OperationsHub are both reachable. RabbitMQ is up; the Wolverine outbox is draining cleanly.

This is the cleanest possible run through the system. SwiftFerret42's first bid clears credit-ceiling and above-current-high checks on first try. BoldPenguin7's outbid clears the same checks. SwiftFerret42's re-bid in the trigger window triggers extended bidding cleanly and the new scheduled close lands without saga drift. The close timer fires once at the new time; the listing sells; the settlement saga runs end-to-end with no payment failure and no reserve miss. The hammer price is $55.00; the fee is $5.50; the seller payout is $49.50. SwiftFerret42's credit balance after the charge is $445.00 of her hidden $500.00 ceiling. Every other narrative in this collection - the rejected bid, the listing that passes, the obligation that stalls, the payment that fails - documents what happens when one of these clean conditions is not in fact clean.

## Moment 1: SwiftFerret42 scans the QR code

**Implements:** slice 0.2.

**Context.** SwiftFerret42 stands at the CritterBids booth with her phone in hand. She has just framed and scanned the QR code on the printed sign. Her phone has loaded the demo's landing route and is about to dispatch an anonymous session-start request. She has no prior events in the system; nothing about her exists in any stream yet. The auction operator is still at the SessionManager screen finalizing the lineup; the Flash session has not yet started.

**Interaction.** The page POSTs an empty body to `/api/participants/session`. Wolverine routes the request to the `StartParticipantSession` handler, which treats the call as the first event in a new lifecycle.

**Response.** Participants mints a fresh `ParticipantId` with `Guid.CreateVersion7()`. The UUID v7 timestamp prefix gives the new stream insert locality on the Marten event store; the UUID's randomized low bytes are the source of every other identifier the bidder will carry. The handler derives `DisplayName` from random byte 8 (one of twenty-five Adjectives), byte 9 (one of twenty-nine Animals), and a four-digit suffix from bytes 10 and 11; SwiftFerret42's stream rolls "SwiftFerret42" out of that derivation. It derives a short `BidderId` in the form "Bidder N" from bytes 12 and 13, the identifier that will ride on every bid she places. It rolls a credit ceiling from byte 14, one of nine values from $200 to $1000 in $100 steps; SwiftFerret42 lands at $500. `ParticipantSessionStarted` is committed to a new stream keyed on the `ParticipantId`. The `Participant` aggregate's `Apply` flips `HasActiveSession` to true and her existence in the system is established. The HTTP response returns only the `ParticipantId` and a Location header; the credit ceiling never appears in the response payload. SwiftFerret42's phone transitions to the catalog page with "SwiftFerret42" displayed in the header.

**Why this matters to the bidder.** SwiftFerret42 now has an identity that will travel with every bid she places for the rest of the journey. She does not see her credit ceiling and will not, unless she tries to bid above $500, in which case the bid would be rejected. The hidden ceiling is the silent ceiling: for this happy path, every bid she places will be inside it.

### Things deliberately not included

- Rejoin-vs-new-session behavior on QR re-scan. *(`defer`)*
- Display-name uniqueness enforcement across active sessions. *(`workshop-update`; see Finding 002. The MVP posture is probabilistic uniqueness; a uniqueness index is a defer-grade follow-up if the bidder count grows beyond the band where collisions are practically unobservable.)*
- The credit-ceiling distribution strategy (random-byte choice versus a more sophisticated approach). *(`implementation-detail`; skill-file territory if it ever lands.)*
- Authentication or account binding. *(`post-MVP`; M6 introduces real authentication and the `[AllowAnonymous]` posture lifts at that point.)*

## Moment 2: SwiftFerret42 browses the catalog and opens the keyboard's detail

**Implements:** slices 1.3, 1.4.

**Context.** SwiftFerret42 is on the catalog page with "SwiftFerret42" in the header. Her phone's session cookie carries the `ParticipantId`; her HTTP requests will not need to re-authenticate for the rest of the journey. Three published listings already exist in the system as `CatalogListingView` documents in Listings' Marten store, projected days ago when GreyOwl12 and the other two sellers finished their submit-and-publish flows. The auction operator's Flash session has not started; each listing's `Status` field on the view reads `"Published"` - the pre-bidding state.

**Interaction.** Her phone GETs `/api/listings`. Wolverine routes to the `GetCatalog` endpoint. Then she taps the Vintage Mechanical Keyboard tile and her phone GETs `/api/listings/{listingId}` for the keyboard's UUID.

**Response.** `GetCatalog` issues `session.Query<CatalogListingView>().OrderByDescending(x => x.PublishedAt).ToListAsync()` and returns three items: the keyboard, the Pokemon card, and the wooden bowl, in publish-recency order. Each tile carries `Title`, `StartingBid`, `BuyItNow` when present, the `Format` ("Flash"), and `Status: "Published"`. SwiftFerret42 reads the keyboard tile: title, starting bid $25.00, Buy It Now $100.00, format Flash. She taps it.

The detail GET resolves to the same `CatalogListingView` document. There is no separate `ListingDetailView`; M3-S6 collapsed the Catalog and Detail Marten projections under OQ2 Path A symmetry with `Format`, and the detail endpoint just `LoadAsync`-es the document by primary key. The endpoint returns 200 with the document; SwiftFerret42's detail page renders the same fields she saw on the tile, plus whatever auction-status fields are populated. At this moment none of the auction-status fields are set: the listing has not yet been opened for bidding, `CurrentHighBid` is null, `BidCount` is zero, and `Status` is still `"Published"`. There is no reserve information on the page. SwiftFerret42 has no signal that GreyOwl12's confidential reserve of $50.00 exists; the lived view carries no reserve-related field.

**Why this matters to the bidder.** SwiftFerret42 now knows what she's bidding on at the catalog grain: title, the $25 starting point, and the $100 Buy It Now ceiling. She does not know whether a reserve exists, much less its amount. This is by design: the system holds reserve existence and amount equally confidential between seller and Settlement until a bid first crosses the threshold, at which point the `ReserveMet` event signals the meeting moment to the bidder over the BiddingHub. SwiftFerret42 will cross her reserve at $55 in Moment 7. From the catalog view alone, she has no way to anticipate whether the keyboard's reserve sits above her starting interest or well below it.

### Things deliberately not included

- The `HasReserve` boolean signal the workshop scenarios formerly asserted. *(`document-as-intentional` per Finding 004; the design holds reserve existence confidential until crossed, signaled only via `ReserveMet`.)*
- Watchlist add or remove (slice 8.1, P2). *(`post-MVP`.)*
- The Selling BC's listing-publish lifecycle (draft, submit, approve, publish). *(`separate-narrative`; future seller-perspective narrative will dramatize it.)*
- The Selling-domain `ListingPublished` (internal to Selling) versus the integration contract `CritterBids.Contracts.Selling.ListingPublished` (the cross-BC carrier). The narrative names only the integration contract; the domain event is not bidder-visible. *(`document-as-intentional`.)*
- Catalog search, filter, and sort UX. *(`UX-or-UI-detail`.)*

## Moment 3: The Flash session starts and the lot board comes alive

**Implements:** slice 2.3.

**Context.** SwiftFerret42 is on the keyboard's detail page. The clock has ticked the operator's two minutes down to zero. From her vantage the page has not changed; the keyboard's `Status` still reads `"Published"`. The operator, offstage, has finalized the session lineup at the SessionManager screen and is about to dispatch the start command. Three listings sit attached to `session-001`: the keyboard, the Pokemon card, and the wooden bowl. Each carries its `ListingAttachedToSession` fact in the operator-side state; none has yet been opened for bidding under Flash semantics.

**Interaction.** The operator taps Start. The ops console POSTs `StartSession { SessionId: "session-001" }` to the Auctions BC.

**Response.** Auctions opens a new Session stream keyed on the `SessionId` (UUID v7 per M4-D2) and appends `SessionStarted` carrying `SessionId, IReadOnlyList<Guid> ListingIds, StartedAt`. A `SessionStartedHandler` reads the listing IDs and emits one `BiddingOpened` per attached listing as a fan-out: three events, one per listing, each appending to the listing's own Auctions-side stream and carrying the per-listing policy fields the listing's `ListingPublished` originally supplied (`StartingBid`, `ReserveThreshold`, `BuyItNowPrice`, `ExtendedBiddingEnabled`, `ExtendedBiddingTriggerWindow`, `ExtendedBiddingExtension`, and a `ScheduledCloseAt` of session-start plus five minutes). The integration-event copies of the three `BiddingOpened` events flow over the `listings-auctions-events` queue to the Listings BC, where the `AuctionStatusHandler` consumes each one and tolerantly upserts `CatalogListingView.Status = "Open"` and `ScheduledCloseAt = startedAt + duration`. Operations BC's session-management projection records `SessionStarted` and the `LiveLotBoardView` populates with three open listings. Relay's BiddingHub broadcasts the open-event to all forty connected clients on each listing's group.

SwiftFerret42's phone is connected to the keyboard's BiddingHub group. Her detail page reflects the change: the `Status` flips from `"Published"` to `"Open"`, the close timer appears showing four minutes fifty-something seconds remaining and counting, the Place Bid affordance becomes active. The other two listings, on her catalog tab, also show `"Open"` status simultaneously. The roughly forty bidders in the room all see the lot board come alive at the same instant.

**Why this matters to the bidder.** SwiftFerret42 cannot bid until the listing's `Status` is `"Open"`. Up to this moment the listing has been a static catalog card; from this moment forward it is a live auction with five minutes on the clock. The transition is system-driven, not bidder-driven: she has no agency over when bidding opens, only over what she does once it is open. The simultaneity of all three listings opening at once is the Flash format's signature: the auction is collective, not individual, and the bidder enters a five-minute window that closes (modulo extended bidding) on a single shared deadline.

### Things deliberately not included

- Lived-code audit of the cascade. *(`defer`; the Flash session aggregate, `StartSession` command handler, `SessionStartedHandler` fan-out, and Listings-side `SessionMembershipHandler` are scheduled for M4-S5 and M4-S6 per the M4-S1 retro. Until those slices ship, the cascade as narrated is forward-spec only. See Finding 006.)*
- The current M3 Timed-only behavior, where `BiddingOpened` fires immediately on `ListingPublished` consumption rather than on session start. *(`separate-narrative`; the Timed-listing journey deserves its own narrative since the lifecycle differs structurally from Flash.)*
- The operator's perspective on creating the session, attaching listings, and dispatching Start (slices 2.1, 2.2, 2.3 from the operator vantage). *(`separate-narrative`.)*
- The OperationsHub push of `SessionStarted` to the ops dashboard (slice 4.2). *(`separate-narrative`; ops-perspective.)*
- Demo-mode timeout configuration with a cap (workshop Phase 2 PO decision). *(`implementation-detail`; saga skill-file territory.)*
- Failure modes of the cascade: partial fan-out, a listing whose `ListingPublished` never reached Auctions, a session with zero attached listings (workshop scenario covers the rejection). *(`alternate-path-failure`.)*

## Moment 4: SwiftFerret42 places her first bid

**Implements:** slice 3.1.

**Context.** SwiftFerret42 is on the keyboard's detail page. The lot board shows the listing as `"Open"`, the close timer reads four minutes and forty seconds, and her Place Bid affordance is active. The keyboard's Auctions-side stream carries one event so far: `BiddingOpened`, applied to the `BidConsistencyState` at projection time, which now reads `StartingBid: $25.00`, `ReserveThreshold: $50.00`, `BuyItNowPrice: $100.00`, `BuyItNowAvailable: true`, `ReserveMet: false`, `BidCount: 0`, `CurrentHighBid: $0`. SwiftFerret42's hidden credit ceiling is $500. She has decided to bid $30.

**Interaction.** She enters $30 in the Place Bid sheet and submits. Her client constructs a `PlaceBid` command carrying `ListingId`, a freshly-minted `BidId` (UUID v7), her `BidderId`, and `Amount: 30.00`. The command also carries her `CreditCeiling: 500.00` directly: an M3 transitional shape until the M4 Session aggregate projects the ceiling into the boundary state on its own (see Finding 009). The command dispatches via the Wolverine bus to `PlaceBidHandler` inside the Auctions BC.

**Response.** `PlaceBidHandler` opens a Dynamic Consistency Boundary on the keyboard's tag-stream by calling `session.Events.FetchForWritingByTags<BidConsistencyState>(query)`. The query selects every acceptance-relevant event by tag: `BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`. Only `BiddingOpened` is on the stream, so the boundary state is the post-open initial state. The handler runs `EvaluateRejection` and clears every check: the listing is open, the close timer has not passed, the seller (GreyOwl12) is not the bidder, the amount ($30) is within the credit ceiling ($500), and the amount meets the starting-bid minimum ($25). The bid is accepted.

Acceptance emits two events atomically. The handler appends `BidPlaced { ListingId, BidId, BidderId, Amount: 30.00, BidCount: 1, IsProxy: false, PlacedAt }` to the keyboard's stream, tagged with `ListingStreamId(listingId)` so the next DCB load can find it. Because `state.BuyItNowAvailable` is true and this is the first bid, the handler also appends `BuyItNowOptionRemoved` as a sibling acceptance event in the same write - the workshop's slice 5.4 ("Buy It Now removal *(system, on first bid)*") is implemented inside slice 3.1's acceptance path rather than as a downstream reaction (Finding 008). The bid amount of $30 is below the $50 reserve, so `ReserveMet` is not emitted. The auction's remaining four minutes and forty seconds is well outside the thirty-second extended-bidding trigger window, so `ExtendedBiddingTriggered` is not emitted either. The DCB's consistency-assertion fires on save: a concurrent first bid on the same listing would lose the optimistic-concurrency race; no such race occurs.

The two `BidPlaced` and `BuyItNowOptionRemoved` integration events flow over `listings-auctions-events` to the Listings BC, where the `AuctionStatusHandler` upserts `CatalogListingView.CurrentHighBid = 30.00`, `BidCount = 1`, `CurrentHighBidderId = SwiftFerret42's ParticipantId`, and clears the BIN field. Operations BC's BidFeed projection logs the bid; Relay's BiddingHub broadcasts the `BidPlaced` to all forty connected clients on the keyboard's group. SwiftFerret42's lot-board tile updates: her display name is now atop the keyboard, the current bid reads $30.00, and the Buy It Now affordance is gone.

**Why this matters to the bidder.** SwiftFerret42 is now the high bidder on the keyboard. She has not met the reserve and she does not know that. The Buy It Now option, which she could have exercised at $100 to skip the auction entirely, is now gone for her and for everyone else; she has no way to reverse the side effect. From this moment until either someone outbids her, the close timer fires, or extended bidding intervenes, the keyboard is provisionally hers at $30. Her credit ceiling has not been touched: she has $500 of credit available and has committed (provisionally) to $30. The remaining $470 is what she has left to defend her position over the next four minutes and forty seconds.

### Things deliberately not included

- The bidder-facing HTTP endpoint and PlaceBidSheet UI. *(`defer`; the M3 `PlaceBid` command is bus-dispatched test-only per its docstring, and the HTTP/UI path is M6 frontend-MVP territory.)*
- Bid rejection paths (slice 3.2): below minimum, exceeds credit ceiling, listing closed, seller-cannot-bid. *(`alternate-path-failure`; rejection journeys deserve their own narratives.)*
- The `BidRejected` audit-stream design and its exclusion from the DCB query. *(`implementation-detail`; M3-S4 retro and W002-7 captured the rationale.)*
- Bid-increment policy ($1 below $100, $5 at $100+). The narrative renders the minimum as "starting bid or current-high-plus-increment" without naming the increment scale. *(`implementation-detail`.)*
- The relationship between `BidConsistencyState` (the DCB tag-aggregate) and the `Listing` aggregate (live-aggregation). *(`implementation-detail`; the DCB skill file is the home for this discussion.)*
- Concurrent bid races and DCB consistency-assertion mechanics. *(`alternate-path-failure`.)*
- The current high bidder's identity exposure on `CatalogListingView.CurrentHighBidderId` (M3-S6 OQ5 Path C). *(`implementation-detail`; redacted at endpoint in M6.)*

## Moment 5: SwiftFerret42 sees her bid echo back, then a competitor outbids her

**Implements:** slices 4.1, 4.3.

**Context.** SwiftFerret42 is on the keyboard's detail page. The lot board shows her display name atop the listing at $30.00, BidCount 1, and the close timer ticking down through four minutes thirty seconds. Her phone is connected to the BiddingHub group for the keyboard's listing ID; BoldPenguin7's phone is also connected to that group, having been watching the keyboard from before the session started. The Auctions BC has just published `BidPlaced` for SwiftFerret42's $30 bid as an integration event over the queue Relay subscribes to; Relay's BiddingHub handler is about to consume it.

**Interaction.** Relay's BiddingHub handler consumes SwiftFerret42's `BidPlaced`. Relay's per-listing high-bidder projection (state Relay maintains internally, not queried from Auctions, per workshop slice 4.3) updates: the keyboard's high bidder is now SwiftFerret42 at $30. The handler broadcasts a SignalR message of shape `{ type: "BidPlaced", listingId, bidderDisplayName: "SwiftFerret42", amount: 30.00 }` to the keyboard's BiddingHub group. Eight seconds later, BoldPenguin7 places a $35 bid; the Auctions DCB accepts it (above SwiftFerret42's $30 by more than the $1 increment, his credit ceiling not exceeded, listing still open) and `BidPlaced { ListingId, BidId, BidderId: BoldPenguin7's id, Amount: 35.00, BidCount: 2, IsProxy: false, PlacedAt }` is committed. Relay's BiddingHub handler consumes the integration-event copy.

**Response.** SwiftFerret42 receives her own $30 bid echoed back through the SignalR group; her phone's lot-board tile already showed $30 from the catalog-side update Moments ago, so the push is reinforcing rather than informing. No targeted Outbid fires on her own bid because there was no prior high bidder to displace.

When BoldPenguin7's $35 lands, Relay's projection observes the high-bidder transition: SwiftFerret42 was the prior high bidder; BoldPenguin7 is the new one. The handler broadcasts a SignalR message `{ type: "BidPlaced", listingId, bidderDisplayName: "BoldPenguin7", amount: 35.00 }` to the keyboard's group. Both bidders receive it; the lot-board tile flips everywhere with BoldPenguin7's display name atop and $35.00 as the current bid. Then the handler issues a targeted SignalR push to SwiftFerret42's connection only: `{ type: "Outbid", listingId, newHighBid: 35.00, yourBid: 30.00 }`. SwiftFerret42's phone vibrates; an Outbid banner appears on her screen showing she was the prior high bidder at $30 and BoldPenguin7 just took her position at $35.

**Why this matters to the bidder.** SwiftFerret42 has lost the high-bidder position on the keyboard and has been told so explicitly. The Outbid push is the system telling her: act if you want this. She has slightly more than four minutes to respond. Her credit ceiling is intact at $500, so financially she has room to bid up to $470 further. The strategic question - whether to bid again, and at what amount - is hers; the system has handed her the information and the decision.

### Things deliberately not included

- Lived-code audit. *(`defer`; the Relay BC has not yet been implemented. The BiddingHub, the per-listing high-bidder projection, the connection-tracking infrastructure, and the targeted-push routing are all scheduled for M4 per the W001 milestone mapping (post-Finding-006 edit). Until Relay ships, this Moment is forward-spec.)*
- BoldPenguin7's perspective on placing the $35 bid. *(`separate-narrative`; future competitor-perspective narrative.)*
- Relay's connection-management lifecycle (joining the BiddingHub group on subscription, leaving on disconnect, reconnect handling). *(`implementation-detail`; Relay skill file or a SignalR-specific narrative.)*
- The OperationsHub broadcast of the same `BidPlaced` to the ops dashboard (slice 4.2). *(`separate-narrative`; ops-perspective.)*
- Bid-feed time ordering and at-least-once delivery considerations. *(`implementation-detail`.)*
- The `BidPlaced` integration contract's `IsProxy` flag and how Relay handles proxy-originated bids visually. *(`separate-narrative`; the proxy bidding journey is a P1 narrative candidate.)*
- Relay's exact SignalR-group subscription semantics (one group per listing, opt-in by detail-page visit, etc.). *(`implementation-detail`.)*

## Moment 6: SwiftFerret42 reclaims the high-bidder position in the trigger window

**Implements:** slice 3.1 (return), slice 5.1.

**Context.** SwiftFerret42 is on the keyboard's detail page. The Outbid banner from Moment 5 is still onscreen; her phone shows BoldPenguin7 atop at $35 with a close timer reading twenty seconds - within the keyboard's thirty-second extended-bidding trigger window. The Auctions BC's `BidConsistencyState` for the keyboard reads `CurrentHighBid: $35`, `BidCount: 2`, `ReserveThreshold: $50`, `ReserveMet: false`, `ScheduledCloseAt: session-start + 5m`, `BuyItNowAvailable: false`. The auction-closing saga's `AuctionClosingSaga` document has the same `ScheduledCloseAt` and an outstanding `CloseAuction` scheduled message in Wolverine's scheduled-message store, set to fire at the same instant.

**Interaction.** SwiftFerret42 enters $55 in the Place Bid sheet and submits. Her client constructs a `PlaceBid` command carrying `ListingId`, a fresh `BidId`, her `BidderId`, `Amount: 55.00`, and `CreditCeiling: 500.00`. The command dispatches via the Wolverine bus to `PlaceBidHandler`.

**Response.** `PlaceBidHandler` opens the DCB, fetches `BidConsistencyState`, runs `EvaluateRejection`. All checks pass: the listing is open, the close timer has not yet passed, GreyOwl12 is not the bidder, $55 fits inside the $500 ceiling, $55 meets the minimum bid (current high $35 plus $1 increment = $36). The bid is accepted.

Acceptance emits a three-event cascade alongside `BidPlaced`. The handler appends `BidPlaced { Amount: 55.00, BidCount: 3 }` to the keyboard's stream. `BuyItNowAvailable` is already false (cleared in Moment 4) so no `BuyItNowOptionRemoved`. Because `state.ReserveThreshold = $50`, `state.ReserveMet = false`, and `command.Amount = $55 >= $50`, the handler appends `ReserveMet { ListingId, Amount: 55.00, MetAt }` - this is the first time the keyboard's reserve has been crossed (the Auctions-side production-path of slice 5.2 is fully shipped in M3 even though the workshop marks the slice P1; see Finding 010). And because `state.ExtendedBiddingEnabled = true`, `remaining = 20s`, and `triggerWindow = 30s`, the handler computes `newCloseAt = now + 15s` (the extension), checks it against `OriginalCloseAt + MaxDuration` (the safety cap, not exceeded), and appends `ExtendedBiddingTriggered { ListingId, PreviousCloseAt, NewCloseAt, TriggeredByBidderId: SwiftFerret42's BidderId, TriggeredAt }`. Four events written atomically through the DCB consistency assertion.

The integration-event copies fan out. The `AuctionClosingSaga` for the keyboard receives all four. Its `Handle(BidPlaced)` updates `CurrentHighBid = 55`, `CurrentHighBidderId = SwiftFerret42's id`, `BidCount = 3`. Its `Handle(ReserveMet)` flips `ReserveHasBeenMet = true` - the saga now knows that its close evaluation will produce `ListingSold` rather than `ListingPassed`. Its `Handle(ExtendedBiddingTriggered)` cancels the pending `CloseAuction` scheduled message via `CancelPendingCloseAsync` (a narrow ±100ms window query that isolates the one pending `CloseAuction` for this listing without cross-listing collateral), schedules a new `CloseAuction(ListingId, NewCloseAt)` fifteen seconds out via `bus.ScheduleAsync`, updates `ScheduledCloseAt = NewCloseAt`, and flips status to `Extended`.

The Listings BC's `AuctionStatusHandler` consumes `BidPlaced`, `ReserveMet`, and `ExtendedBiddingTriggered` and upserts `CatalogListingView`: `CurrentHighBid = 55.00`, `BidCount = 3`, `CurrentHighBidderId = SwiftFerret42's ParticipantId`, `ScheduledCloseAt = NewCloseAt`. Operations BC's BidFeed projection logs the bid; the LiveLotBoardView reflects the timer extension.

SwiftFerret42's phone, connected to the keyboard's BiddingHub group, receives the `BidPlaced` echo of her own bid, and the close timer on her detail page resets from twenty seconds to thirty-five seconds. Her display name is back atop the keyboard at $55. When Relay ships, BoldPenguin7 will receive a targeted Outbid push and a `ReserveMet` push will go to the keyboard's BiddingHub group signaling that the reserve has been crossed (workshop slice 5.2's bidder-facing surface). In the M3 reality this Moment audits, the Auctions-side event production is fully implemented; only the Relay push surface remains M4 work.

**Why this matters to the bidder.** SwiftFerret42 is back atop the keyboard at $55, and her bid bought her an additional fifteen seconds. The trigger-window mechanic is the Flash format's anti-snipe defense: had she bid a few seconds later (after the timer crossed zero) the auction would have closed on BoldPenguin7's $35; bidding inside the window converts a near-loss into a fresh contest. She has now committed $55 of her $500 ceiling, leaving $445 to defend the position if BoldPenguin7 escalates. She does not know that her bid just met the keyboard's reserve; the `ReserveMet` event was emitted, the saga state updated, but the bidder-facing signal that "you just made the reserve" lives in the unimplemented Relay push.

### Things deliberately not included

- Lived-code audit of the Relay-side `ReserveMet` and `ExtendedBiddingTriggered` SignalR pushes. *(`defer`; Relay BC not implemented.)*
- BoldPenguin7's targeted Outbid receipt and his subsequent decision (escalate, abandon). *(`separate-narrative`; competitor-perspective.)*
- The `MaxDuration` safety cap (extended bidding's outer limit). The narrative names it as a check that did not bind here. *(`alternate-path-failure`; the cap-bound case is its own narrative.)*
- Multiple sequential extended-bidding triggers (W001 parked question 4). *(`alternate-path-failure`.)*
- The `CancelPendingCloseAsync` query's ±100ms window choice and what happens if two listings share an exact scheduled close time. *(`implementation-detail`; saga skill file or M3-S5b retro is the home for this discussion.)*
- The Wolverine scheduled-message store's redelivery semantics. The saga's static `NotFound(CloseAuction)` handler covers a `CloseAuction` that races cancellation. *(`implementation-detail`.)*
- The bidder-facing UI for "auction extended" - banner, animation, audio cue. *(`UX-or-UI-detail`.)*

## Moment 7: The gavel falls and SwiftFerret42 wins

**Implements:** slice 3.3.

**Context.** SwiftFerret42 watches the close timer count down through the keyboard's extended-bidding window. The saga's last reschedule scheduled `CloseAuction` for session-start + 5m15s; the Wolverine scheduled-message store holds it. The saga's state at this moment: `CurrentHighBid: $55`, `CurrentHighBidderId: SwiftFerret42's BidderId`, `BidCount: 3`, `ReserveHasBeenMet: true`, `Status: Extended`. No further bids have arrived during the extension. SwiftFerret42's phone shows $55, her name atop, and the timer ticking through the final seconds.

**Interaction.** At session-start + 5m15s the scheduled `CloseAuction(ListingId, NewCloseAt)` message dispatches. Wolverine routes it to the saga via the `[SagaIdentityFrom(nameof(CloseAuction.ListingId))]` correlation: the saga document is loaded from the keyboard's listing id.

**Response.** The saga's `Handle(CloseAuction)` evaluates. Status is `Extended` (not `Resolved`), so the handler proceeds rather than no-op'ing. It emits `BiddingClosed { ListingId, ClosedAt: now }` first - the workshop distinguishes `BiddingClosed` (the timer fired) from the outcome event (sold/passed) per slice 3.3's terminal-event sequence.

Then the handler decides the outcome. `BidCount(3) > 0` and `ReserveHasBeenMet(true)`, so the listing is sold. The handler needs `SellerId` for the `ListingSold` event but the saga state does not carry it: `StartAuctionClosingSagaHandler` (frozen since M3-S5) doesn't capture `SellerId` on saga start. The handler loads the Auctions-side `Listing` aggregate via `session.Events.AggregateStreamAsync<Listing>(ListingId)`, which rebuilds the aggregate from its event stream; `Listing.Apply(BiddingOpened)` populated `SellerId = GreyOwl12's id` at open time, so the aggregate rebuild reads the seller cleanly (Finding 012 documents this design choice). The handler appends `ListingSold { ListingId, SellerId: GreyOwl12, WinnerId: SwiftFerret42, HammerPrice: $55.00, BidCount: 3, SoldAt: now }` to the `OutgoingMessages` collection.

The saga's status flips to `Resolved` and `MarkCompleted()` removes the saga document from the saga store. The `OutgoingMessages` (BiddingClosed + ListingSold) commit atomically via Wolverine's outbox: both events arrive at their downstream consumers via the `listings-auctions-events` queue. The Listings BC's `AuctionStatusHandler` consumes both: `CatalogListingView.Status` flips from `"Open"` to `"Closed"` (on BiddingClosed) and immediately to `"Sold"` (on ListingSold), and the view also updates `ClosedAt`, `WinnerId = SwiftFerret42's ParticipantId`, and `HammerPrice = $55.00`. Operations BC's projections record both events; the LiveLotBoardView shows the keyboard as won by SwiftFerret42 at $55.

Relay's BiddingHub broadcasts `ListingSold` to the keyboard's group: `{ type: "ListingSold", listingId, winnerDisplayName: "SwiftFerret42", hammerPrice: 55.00 }`. SwiftFerret42 sees a "You Won" banner on her detail page with the hammer price. BoldPenguin7, also in the group, sees the same broadcast: a "Sold to SwiftFerret42 at $55" notification.

**Why this matters to the bidder.** The keyboard is hers. The provisional ownership from her $55 bid has resolved into a definitive sale; the gavel's fall is the system's commitment that no further bids can overturn the outcome. She has met the reserve she could not see, so the listing sells rather than passes. The hammer price of $55 is what she will be charged in Moment 8; until Settlement runs, the charge has not yet hit her credit ceiling - but the price is locked. The auction is over for the keyboard; the lot board still shows the Pokemon card and the wooden bowl in their own resolutions, but those are other bidders' stories.

### Things deliberately not included

- The competing terminal paths: `ListingPassed` (ReserveNotMet, NoBids - slice 3.4), `BuyItNowPurchased` (slice 5.3 via M3-S4b). *(`alternate-path-failure`; each is its own narrative.)*
- The saga's `NotFound(CloseAuction)` static handler. *(`implementation-detail`; covers a `CloseAuction` that races cancellation when a terminal (BIN purchased, withdrawn) preempts the scheduled close.)*
- The Listings BC's status transition through `"Closed"` to `"Sold"` in two sequential upserts versus a single combined apply. *(`implementation-detail`; the M3-S6 retro covers OQ4 Path II tolerant upsert.)*
- The OperationsHub broadcast of `BiddingClosed`/`ListingSold` to the ops dashboard. *(`separate-narrative`; ops-perspective.)*
- Idempotency under at-least-once redelivery of `ListingSold` to downstream consumers. *(`implementation-detail`.)*
- The frontend's "Closing..." UI state during the small window between timer-zero and the saga's actual emission of `BiddingClosed` (W001 parked question 12). *(`UX-or-UI-detail`.)*
- The `ListingWithdrawn` terminal path (the M4-S2 flow). *(`alternate-path-failure`.)*

## Moment 8: SwiftFerret42 is charged and the sale is complete

**Implements:** slice 6.1.

**Context.** SwiftFerret42's "You Won" banner from Moment 7 is still onscreen. The Auctions BC has just published `ListingSold` as an integration event over the cross-BC bus; Settlement subscribes to it. Her credit ceiling is $500 untouched; her provisional commitment to $55 has not yet hit her ledger. The keyboard's `CatalogListingView` reads `Status: "Sold"`, `WinnerId: SwiftFerret42's ParticipantId`, `HammerPrice: $55.00` - the read-side has already absorbed the auction's terminal outcome.

**Interaction.** Settlement's saga handler consumes `ListingSold`. The saga is keyed on a fresh `SettlementId` (UUID v7) and a new stream opens on it. The start handler captures the inputs from `ListingSold`: `ListingId`, `WinnerId`, `SellerId`, `HammerPrice`, `BidCount`.

**Response.** The saga emits `SettlementInitiated { SettlementId, ListingId, WinnerId: SwiftFerret42, SellerId: GreyOwl12, Price: $55.00, Source: Bidding, InitiatedAt }` as the saga's first event. The `Price` field is source-agnostic at initiation per W003 §1's convention (it carries the hammer price for Bidding source and the BIN price for BIN source; `Source` disambiguates); the post-initiation rename to `HammerPrice` happens at the evolver step that hydrates state for the downstream phases.

The reserve check follows. Settlement reads the listing's reserve from the `ListingPublished` integration event Settlement consumed at publish time and stored in its own boundary state - the workshop's slice 1.2 view note ("ReservePrice: 50.00 - opaque - passed to Settlement") describes this contract. The saga compares HammerPrice ($55) against ReservePrice ($50), notes the threshold is met, and emits `ReserveCheckCompleted { SettlementId, HammerPrice: $55.00, ReservePrice: $50.00, WasMet: true, CompletedAt }`. For SwiftFerret42's journey the reserve was met at her bid moment in Moment 6; the saga's check is the final authoritative verification by the BC that owns reserve enforcement, not the first observation.

The winner charge follows. The saga charges SwiftFerret42's credit ledger by the hammer price: `WinnerCharged { SettlementId, WinnerId: SwiftFerret42, Amount: $55.00, ChargedAt }`. Her credit ceiling was $500; after the charge, $445 remains. Settlement's bidder-credit projection is updated.

The fee calculation follows. The saga reads the configured fee percentage (10% in the MVP defaults), computes $55 × 10% = $5.50, and the seller payout as $55 - $5.50 = $49.50. It emits `FinalValueFeeCalculated { SettlementId, HammerPrice: $55.00, FeePercentage: 10.0, FeeAmount: $5.50, SellerPayout: $49.50, CalculatedAt }`.

The seller payout follows. The saga issues the payout to GreyOwl12 - for the demo, this is a ledger entry rather than a banking integration. `SellerPayoutIssued { SettlementId, SellerId: GreyOwl12, PayoutAmount: $49.50, FeeDeducted: $5.50, IssuedAt }`.

The saga emits `SettlementCompleted { SettlementId, ListingId, WinnerId: SwiftFerret42, SellerId: GreyOwl12, HammerPrice: $55.00, FeeAmount: $5.50, SellerPayout: $49.50, CompletedAt }` and calls `MarkCompleted()`. The saga document is removed from the saga store. Settlement's `SettlementProgressView` records `Status: "complete"` for this settlement.

Relay's BiddingHub broadcasts a notification to SwiftFerret42's connection: `{ type: "SettlementCompleted", listingId, hammerPrice: 55.00, remainingCredit: 445.00 }`. Her phone's "You Won" banner ticks forward to "Charged $55.00 to your credit. The keyboard is yours." Her credit balance display updates from $500 to $445. The journey arc closes.

**Why this matters to the bidder.** SwiftFerret42 has now committed real (demo) money to the keyboard. The provisional ownership from Moment 7 has resolved into a definitive purchase: she has paid $55, the system has acknowledged the charge, the seller has been paid out. Her credit ceiling now reads $445 - what she has left to spend on any remaining listings in the session. The reserve she could not see has been verified one more time as part of the saga's authoritative check; if Settlement's reserve verification had disagreed with the Auctions-side acceptance, the listing would have rolled into a settlement-failure path (which is `alternate-path-failure` for a future narrative). For this happy path, all five intermediate saga events resolve cleanly and the sale completes.

### Things deliberately not included

- Lived-code audit of the entire Settlement saga. *(`defer`; the Settlement BC has not yet been implemented. M5 is the ship target per the W001 milestone mapping (post-Finding-006). Until Settlement ships, this Moment is forward-spec.)*
- The `BuyerNotified` event the workshop's settlement scenario does not list but Settlement might emit. *(`implementation-detail`; Settlement skill or M5 design choice.)*
- Settlement-payment failures: insufficient credit, payment-provider rejection, ledger-divergence. *(`alternate-path-failure`; future failure-path narrative.)*
- Settlement reserve disagreement (Auctions-side reserve check passed but Settlement's check disagrees). The W001 parked question 5 names this. *(`alternate-path-failure`.)*
- The `ListingSold`-versus-`ListingPassed` branch on Settlement entry. *(`alternate-path-failure`.)*
- Settlement-from-`BuyItNowPurchased` (slice 6.2, P1). *(`separate-narrative`.)*
- Seller-perspective on payout receipt (slice 6.3 `SellerPayoutIssued` push, P1). *(`separate-narrative`.)*
- Demo-mode timeout configuration with a cap for the saga (workshop Phase 2 PO decision). *(`implementation-detail`.)*
- The Tier 7 obligations saga that follows `SettlementCompleted` (shipping reminder, tracking, delivery confirmation). *(`separate-narrative`; out of scope per the prompt.)*

## Deferred from this narrative

The following were deliberately not narrated in this happy-path bidder spine. Each is named with its disposition so future sessions can pull from this list when scoping the next narrative, ADR, skill file, or implementation prompt. Items here are not bugs or omissions; they are consciously deferred and traceable. Items recorded in `001-findings.md` as `document-as-intentional` (settled design choices) are not duplicated here - the cumulative section is a backlog feeder, not a transparency footnote.

### `defer` (revisit when trigger lands)

- Rejoin-vs-new-session behavior on QR re-scan (Moment 1; trigger: production usage at scale revealing duplicate-scan patterns).
- Lived-code audit of the Flash session-start cascade (Moment 3; trigger: M4-S5 ships the Auctions-side `SessionStartedHandler` fan-out and M4-S6 ships the Listings-side `SessionMembershipHandler`).
- Bidder-facing HTTP endpoint and PlaceBidSheet UI for `PlaceBid` (Moment 4; trigger: M6 frontend MVP, when the `[AllowAnonymous]` posture lifts and the `/api/auctions/bids` endpoint is wired).
- Lived-code audit of the Relay BC's BiddingHub, Outbid push, and connection projection (Moments 5 and 6; trigger: M4 Tier 4 ship).
- Lived-code audit of the entire Settlement saga (Moment 8; trigger: M5 ship).

### `post-MVP` (beyond v1 scope)

- Authentication or account binding (Moment 1; M6 introduces real authentication and the `[AllowAnonymous]` posture lifts).
- Watchlist add or remove, slice 8.1 P2 (Moment 2).

### `separate-narrative` (other journey perspectives)

- Selling BC's listing-publish lifecycle from the seller's perspective: draft, submit, approve, publish (Moment 2). Phase 5 Item 1's Selling-BC narrative covers this.
- Current M3 Timed-only auction lifecycle (where `BiddingOpened` fires immediately on `ListingPublished` consumption rather than at session start) as a structurally distinct flow from Flash (Moment 3).
- Operator-perspective on session creation, listing attach, and session start (Moment 3; slices 2.1, 2.2, 2.3 from operator vantage).
- OperationsHub broadcasts to the ops dashboard across multiple Moments: `SessionStarted` cascade (Moment 3), `BidPlaced` ops feed (Moment 5), `BiddingClosed` and `ListingSold` ops view (Moment 7).
- BoldPenguin7's competitor-perspective on placing the $35 outbid (Moment 5) and on receiving SwiftFerret42's $55 retaliation (Moment 6).
- IsProxy flag and proxy bidding journey, slices 5.5 / 5.6 (Moment 5).
- Settlement-from-`BuyItNowPurchased`, slice 6.2 P1 (Moment 8).
- Seller-perspective on payout receipt, slice 6.3 P1 `SellerPayoutIssued` push (Moment 8). Phase 5 Item 1's Selling-BC narrative may cover this.
- Tier 7 obligations saga that follows `SettlementCompleted`: shipping reminder, tracking provision, delivery confirmation, demo-mode timeout (Moment 8; out of scope per the prompt).

### `separate-workshop` (BCs not yet event-modeled)

None. CritterBids has all four lived BCs already workshopped (W001-W004); the four unshipped BCs (Settlement, Obligations, Relay, Operations) await their own workshops alongside their implementations.

### `implementation-detail` (skill file or ADR territory)

- Credit-ceiling distribution strategy (Moment 1; random-byte choice versus a more sophisticated approach).
- `BidRejected` audit-stream design and its exclusion from the DCB query, W002-7 decision (Moment 4).
- Bid-increment policy: $1 below $100, $5 at $100+ (Moment 4).
- Relationship between `BidConsistencyState` (DCB tag-aggregate) and the `Listing` aggregate (live-aggregation) (Moment 4; DCB skill file).
- `CurrentHighBidderId` privacy on `CatalogListingView` and M6 endpoint redaction, M3-S6 OQ5 Path C (Moment 4).
- Relay's connection-management lifecycle: subscription, disconnect, reconnect (Moment 5).
- Bid-feed time ordering and at-least-once delivery considerations (Moment 5).
- Relay's SignalR group subscription semantics: one-group-per-listing, opt-in by detail-page visit (Moment 5).
- `CancelPendingCloseAsync` ±100ms window choice and cross-listing collision risk (Moment 6; saga skill file).
- Wolverine scheduled-message store redelivery semantics and the saga's `NotFound(CloseAuction)` static handler (Moments 6, 7).
- Demo-mode timeout configuration with a cap for the saga (Moments 3, 8; workshop Phase 2 PO decision).
- Two sequential `CatalogListingView` upserts (Closed, then Sold) versus a single combined apply on the terminal-event pair (Moment 7).
- Idempotency under at-least-once redelivery of `ListingSold` to downstream consumers (Moment 7).
- `BuyerNotified` event the workshop's Settlement scenario does not list but Settlement might emit (Moment 8).

### `alternate-path-failure` (failure modes warranting their own narratives)

- Bid rejection paths from slice 3.2: below minimum, exceeds credit ceiling, listing closed, seller-cannot-bid (Moment 4).
- Concurrent bid races and DCB consistency-assertion mechanics (Moment 4).
- Failure modes of the session-start cascade: partial fan-out, a listing whose `ListingPublished` never reached Auctions, a session with zero attached listings (Moment 3).
- `MaxDuration` safety cap binding case (Moment 6; the bid-in-trigger-window-but-cap-exceeded path).
- Multiple sequential extended-bidding triggers (Moment 6; W001 parked question 4).
- Competing terminal paths: `ListingPassed` (ReserveNotMet, NoBids - slice 3.4), `BuyItNowPurchased` (slice 5.3 via M3-S4b) (Moment 7).
- `ListingWithdrawn` terminal path from M4-S2 (Moment 7).
- Settlement-payment failures: insufficient credit, payment-provider rejection, ledger-divergence (Moment 8).
- Settlement reserve disagreement (Auctions-side reserve check passed but Settlement's check disagrees), W001 parked question 5 (Moment 8).
- `ListingSold`-versus-`ListingPassed` branch on Settlement entry (Moment 8).

### `UX-or-UI-detail` (app design)

- Catalog search, filter, and sort UX (Moment 2).
- The "auction extended" UI: banner, animation, audio cue (Moment 6).
- The "Closing..." UI state during the small window between timer-zero and the saga's emission of `BiddingClosed` (Moment 7; W001 parked question 12).

## Retrospective

### Narrative intent vs. outcome

Stated goal at session start: author the first NDD-informed narrative for CritterBids covering a single bidder's happy-path Flash auction journey from anonymous session start through settlement, audit lived M3 and M4 code against it, route every disagreement through the four-lane findings discipline.

**Outcome.** Eight Moments covering W001 slices 0.2, 1.3, 1.4, 2.3, 3.1, 3.3, 4.1, 4.3, 5.1, and 6.1. Three Moments (3, 5, 8) authored as forward-spec because the Auctions-side Flash session aggregate, Relay BC, and Settlement BC respectively are unshipped. Twelve findings filed in `001-findings.md` across four routing lanes: 2 `narrative-update`, 5 `workshop-update`, 1 `code-update`, 4 `document-as-intentional`. Cast and Setting locked first; Moment-by-Moment sign-off cadence held throughout the eight Moments. Format dialect inherited from CritterCab v0.1; CritterBids-specific patterns established for the four Phase 5 backfill narratives. Goal met.

### What worked

- **Moment-by-Moment sign-off cadence** held for all eight Moments without slipping. Every Moment's draft prose plus its findings landed in one commit on the Phase 2 branch; no batched outputs, no speculative artifact content.
- **Per-Moment "Things deliberately not included" subsection** captured authorial calls inline, then aggregated into the cumulative `## Deferred from this narrative` section at session close per the README convention.
- **Setting carrying canonical numbers once** (reserve $50, hammer $55, fee 10%, credit ceiling $500) anchored each Moment without re-establishing the configuration. The single Setting-paragraph drift caught at Moment 1 (Finding 001) reinforced the lesson: read lived code before drafting Setting, not after.
- **Findings discipline routed cleanly under user adjudication.** Twelve findings, four lanes; each finding's routing was either author-leaned-and-user-confirmed or user-redirected (Finding 004 routed `document-as-intentional` rather than `code-update`). The lane semantics held.
- **Forward-spec routing for unshipped BCs** preserved the journey arc without inventing audit material. Moments 3, 5, and 8 narrated the system as designed to run, deferred lived-code audit under `defer`, and avoided spurious `code-update` findings against absent code.
- **Multi-paragraph `Response.` convention** worked for the multi-slice Moments (5, 6) and the saga-arc Moment (8) per the README's multi-slice rule. Paragraphs grew, labels did not.
- **Em-dash hygiene held** across all narrative and findings edits. Zero em dashes in any committed narrative-text the session authored. Pre-existing em dashes in workshops and scenarios were preserved per the convention's grandfather clause.
- **Reading the slice's retrospective alongside its code** (M3-S5b retro for Moments 6 and 7; M3-S6 retro for Moment 2; M4-S1 retro for Moment 3) revealed design-time decisions the code alone did not show. The retros' OQ outcomes (Path A, Path B, Path C labels) became load-bearing finding evidence.

### What was hard

- **The `TryComputeExtension` bug (Finding 011) revealed itself only at Moment 7's prep, by reading the saga's `Handle(ExtendedBiddingTriggered)` defensive guard.** The PlaceBidHandler's extension calculation `candidate = now + extension` produces NewCloseAt earlier than ScheduledCloseAt for early-trigger-window bids; the saga's `if (NewCloseAt <= ScheduledCloseAt) return` guard prevents the broken reschedule but the broken event still commits to the stream and corrupts DCB boundary state. Recognizing the bug required reading the saga's reaction code, then reading back to PlaceBidHandler's emission code with the discrepancy in mind. Lesson: per-Moment retro reads catch bugs the per-Moment code reads alone might miss.
- **Setting paragraph 3 was authored before reading the lived Participants code**, and the credit-ceiling-band claim ($200-$500) was a fabrication. Caught at Moment 1; patched. Lesson: lived-code orientation is per-Moment, but Cast and Setting still benefit from a quick code skim before drafting; otherwise the Setting's specific numbers may not match production.
- **Three Moments turned out to be forward-spec, not two.** The intro paragraph at Cast-and-Setting time claimed "Two of the eight (5 and 8)"; Moment 3's audit revealed the Auctions-side Flash session aggregate is also M4-S5+. Caught at Moment 3 via Finding 007 and patched in the same commit. Lesson: pre-walk the BC inventory against the slices being narrated; do not assume "the Auctions BC has shipped" maps to "every Auctions slice has shipped."
- **Spec-anchored framing for Moment 6.** The lived `TryComputeExtension` bug means SwiftFerret42's $55 re-bid does NOT actually extend the auction in M3 production. Two routings were available: render lived behavior (no extension; the timer doesn't reset) or render workshop intent (extension fires; the timer resets to 35 seconds). ADR 016's spec-anchored framing authorized the latter (narrative renders intent; code is authoritative for runtime; divergence routes to `code-update`). The narrative voice held; Phase 2.5 absorbs the fix.

### Decisions about how to author (meta-decisions worth carrying forward)

- Cast and Setting locked first; Moment-by-Moment sign-off cadence thereafter.
- Findings surface as the draft is written, not retroactively. Resolution edits go in the same commit as the Moment.
- Per-Moment "Things deliberately not included" subsections persist in the published narrative file (the prompt's interpretation of the README's "in its proposal phase" wording) and consolidate into the cumulative `## Deferred from this narrative` at session close.
- Forward-spec Moments use the same prose shape as audited Moments; the difference shows up in the deferred subsection (`defer` disposition with code-not-yet-shipped justification) and in the absence of `code-update` findings.
- Spec-anchored framing wins when narrative intent and lived code diverge: the narrative remains aligned with the workshop's design, the divergence routes to `code-update`, Phase 2.5 absorbs.
- `document-as-intentional` is a finding-routing lane, not a deferral disposition. Items routed `document-as-intentional` are settled design choices documented in `001-findings.md`; they do not roll up into the cumulative deferred section (which is a backlog feeder).

### Patterns established for future narratives

CritterCab v0.1 patterns inherited unchanged: bounded frontmatter v1, prose-paragraph Moment body, multi-slice Moments grow in paragraphs, single-named-protagonist plus omniscient narrator, seven disposition tags for deferral, per-Moment plus cumulative deferral discipline, code-style backticks for events and projection names.

CritterBids-specific patterns established for narratives 002-005 (the Phase 5 backfills):

- **Forward-spec Moments are normal, not exceptional.** Future narratives will routinely audit shipped BCs and forward-spec unshipped ones. The Settlement-BC narrative will hit forward-spec for slice 6.1 until M5; the Auctions-BC narrative may hit it for the Flash session aggregate until M4-S5/M4-S6; the Selling-BC narrative may hit it for the manual seller-approval path; the Participants-BC narrative is fully audited (M1 only).
- **Findings file as parallel artifact.** `00N-findings.md` next to the narrative file. Numbered findings with Routing / Surfaced at / Discrepancy / Resolution. The findings file is the audit evidence; the narrative is the journey.
- **Stub follow-up prompts for `code-update` findings** at session close, named under `docs/prompts/implementations/<slug>.md`, deferred to Phase 2.5 for fleshing out. The session that authored the finding does not implement the fix.
- **Pre-Moment lived-code reads include the relevant retro.** Reading M3-S5b retro alongside the saga code revealed the `Handle(CloseAuction)` SellerId-via-AggregateStreamAsync design (Finding 012); reading M3-S4 retro alongside `PlaceBidHandler.cs` revealed the `TryComputeExtension` bug (Finding 011). Code-only reads would have caught both, but slower.
- **Workshop edits cascade from findings.** Workshop-update findings produce concrete W001 or `001-scenarios.md` edits in the same commit as the Moment that surfaced them; the workshop converges to lived code (or to the design decision the lived code embodies) within the narrative session, not at a future cleanup pass.
- **Em-dash sweeping when editing existing prose.** Pre-existing em dashes in workshops are grandfathered, but rows being rewritten as part of a finding's resolution sweep their em dashes to hyphens to keep the table internally consistent.

### Quality signal from the session

User feedback was clean throughout: every Moment locked as proposed with no full revision rounds. Two minor amendments at sign-off (Finding 004's routing choice; Finding 010's note placement). All twelve findings' routings held under user adjudication. The `TryComputeExtension` bug (Finding 011) was acknowledged as a real bug worth fixing in Phase 2.5. The lean-opinions-on-questions practice inherited from prior CritterBids sessions continued to land.

The narrative-as-spec-anchored-vs-lived-as-runtime tension surfaced exactly where ADR 016 predicted: at the one place where lived code diverged from design intent (the extension calculation). The discipline absorbed it cleanly.

### Follow-ups generated

- **Phase 2.5 stub follow-up prompt for Finding 011** committed at session close at `docs/prompts/implementations/phase2-5-extension-calculation-fix.md`. Slice scope: change `var candidate = now + extension` in `PlaceBidHandler.TryComputeExtension` to compute against `state.ScheduledCloseAt`, add a defensive guard against non-monotone reschedules, harden test coverage for early-trigger-window bids.
- **Phase 5 backfill narratives 002-005** inherit this session's patterns: forward-spec routing, findings file convention, stub-prompt-on-close, retro-alongside-code reading.
- **Methodology log Entry 001 considered and consciously skipped** at session close. The 5-`workshop-update`-to-1-`code-update` finding-lane ratio is interesting but the project has authored exactly one narrative session; one data point is not a load-bearing observation about drift accumulation. Defer Entry 001 to narrative #2's close, when a comparison ratio becomes available. The methodology log's entry-criteria gate held.
- **W001 cross-references on directly-implemented slices** committed in the session-close PR via a consolidated Narrative Cross-References note at the start of W001 §"Phase 4 - Identify Slices", listing slices 0.2, 1.3, 1.4, 2.3, 3.1, 3.3, 4.1, 4.3, 5.1, and 6.1 as implemented by narrative 001.
- **Narratives README Index row 001** committed in the session-close PR.

### Narrative #2 candidate list

Per Phase 5 Item 1, four backfill narratives are scoped, each authored under this session's discipline. In rough order of structural readiness:

1. **Auctions-BC narrative** - seller- or operator-perspective on the auction lifecycle through W002 (auction-closing saga terminal paths from M3-S5b, extended-bidding mechanics from M3-S4, reserve-met semantics). Most likely seller-perspective on a winning Flash auction with extended bidding. Strongest candidate for narrative #2: highest density of lived M3 code; Phase 5 Item 1 scopes it explicitly.
2. **Selling-BC narrative** - seller-perspective on listing creation, submission, automated approval, publish, and the M4-S2 WithdrawListing flow through W004.
3. **Participants-BC narrative** - bidder-perspective on anonymous session start through credit-ceiling assignment. Companion to narrative 001's Moment 1 at finer grain. Lightest narrative; lived M1 code is small and stable.
4. **Settlement-BC narrative** - winner-perspective on the settlement saga happy path through W003. Companion to narrative 001's Moment 8 at finer grain. Heavily forward-spec until M5 ships.

The Auctions-BC narrative is the recommended next session, both for structural readiness and because Phase 5 Item 1's broader scope leans on the same lived-code audit muscle this session exercised.

### Narrative status

**Complete (v0.1, 2026-04-27).** Eight Moments, cumulative deferred section, retrospective. Format conventions inherited from CritterCab v0.1; CritterBids-specific patterns named for Phase 5 backfills. Status flipped to `accepted` in the session-close commit. The narrative is ready to serve as input to implementation prompt documents covering the directly-implemented slices.

---

## Document History

- **v0.1** (2026-04-27): Initial authoring as foundation-refresh Phase 2 deliverable. Eight Moments covering W001 slices 0.2 / 1.3 / 1.4 / 2.3 / 3.1 / 3.3 / 4.1 / 4.3 / 5.1 / 6.1; three Moments (3, 5, 8) authored as forward-spec for unshipped BCs and slices. Twelve findings filed in `001-findings.md` across four routing lanes. Format dialect locked from CritterCab v0.1 with two guardrails (prose-paragraph Moment bodies; bounded frontmatter vocabulary). Single-bidder POV locked. Cumulative deferred section + narrative-internal retrospective + Document History committed at session close.
- **v0.2** (2026-05-29): M6-S5 partial lived-code landing for the Relay BC's `BiddingHub` push surface. The `CritterBids.Relay` BC scaffold shipped with three participant-facing notification handlers — `BidPlacedHandler`, `ListingSoldHandler`, `SettlementCompletedHandler` — pushing to `BiddingHub` groups (`listing:{ListingId}` for the first two, `bidder:{WinnerId}` for settlement) via direct `IHubContext<BiddingHub>` (ADR 023, Path b). This moves the `BidPlaced` (Moment 6), `ListingSold` (Moment 7 close), and `SettlementCompleted` (Moment 8) pushes from `defer` to **partially lived**: the core broadcast plumbing is now audited code, integration-tested over a real Kestrel + SignalR client. Still `defer` after S5 and re-scoped to M6-S6/S7: the `Outbid` targeted push and `ReserveMet`/`ExtendedBiddingTriggered` pushes (Moment 6), Relay's per-listing high-bidder projection and `NotificationHistoryView` (Moment 6, deferred to S6), the remaining inbound consumers (participants/selling/obligations/listings), the full `OperationsHub` handler set (S6), and the connection-management lifecycle / group-subscription semantics (implementation-detail). Note the deferred-section trigger text ("trigger: M4 Tier 4 ship") predates the Relay→M6 re-scope recorded in Finding 006; the BC landed in M6, not M4.
- **v0.3** (2026-05-30): M6-S6 landed Relay's remaining inbound routes and staff feed handlers: `relay-participants-events`, `relay-selling-events`, `relay-obligations-events`, and `relay-listings-events` now listen in `Program.cs`; remaining participant and operations pushes are implemented for Auctions / Selling / Participants / Obligations / Listings / Settlement (`SellerPayoutIssued`) using ADR 023's plain `Hub` + `IHubContext` path. Relay now owns its first Marten-backed read model, `NotificationHistoryView` (keyed by `BidderId`) with handler-driven accumulation from participant-targeted notifications. Remaining defer items stay in S7 scope: end-to-end journey coverage and final route-topology + CI matrix audit.
- **v0.4** (2026-05-30): M6-S7 close-out proved Moment 8's `SettlementCompleted` winner push **end-to-end as part of the post-sale fan-out** — no new behaviour, integration coverage only. A composed real-Kestrel + Marten host (`PostSaleFanOutTestFixture`, the inverse of the per-BC sibling-exclusion fixtures: Obligations **and** Relay both active under `MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination`) asserts that one `SettlementCompleted` drives **two independent sibling consumers** in a single publish: the Relay `bidder:{WinnerId}` `BiddingHub` push (observed on a real SignalR client) and the Obligations `PostSaleCoordinationSaga` start (asserted via `ObligationStatusView` = `AwaitingShipment`) — siblings off one event, not a chain. The M6-S5 partial settlement-push landing (v0.2) is now journey-covered. The `Program.cs` seven-route topology audit (milestone §5) found all routes correctly wired with correct direction — no defect, no production change. Still deferred past M6: the `Outbid`/`ReserveMet`/`ExtendedBiddingTriggered` targeted pushes, connection-lifecycle semantics, and the M6 milestone retrospective.
