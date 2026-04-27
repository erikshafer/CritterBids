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

This narrative implements the happy-path P0 slices of the Flash demo journey (workshop 001, Tier 0 through Tier 6). It cites slice numbers; it does not restate the workshop's Given/When/Then scenarios. Two of the eight Moments (5 and 8) describe BCs that have not yet shipped lived implementation - Relay (Moment 5) and Settlement (Moment 8). Those Moments narrate the journey as the system is designed to run; their lived-code audit is deferred under the `defer` disposition until those BCs land.

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
