---
slug: 001-bidder-wins-flash-auction
status: draft
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
