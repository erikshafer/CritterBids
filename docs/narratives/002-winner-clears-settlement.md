---
slug: 002-winner-clears-settlement
status: draft
journey: bidder
perspective: single-bidder
scope: happy-path
bounded_contexts: [Settlement]
boundaries_touched: [Auctions, Selling, Participants, Relay, Listings, Operations]
slices_implemented: [6.1, 6.3]
canonical_id: SettlementId
---

# Winner Clears Settlement (Happy Path)

A Settlement-grain narrative. SwiftFerret42 has just won a Vintage Mechanical Keyboard at $55.00 hammer in narrative 001 Moment 7; this narrative picks up at that beat and follows what the Settlement saga does over the next several seconds, ending with her credit charged and the seller paid out. Where narrative 001 Moment 8 collapsed the entire saga into one bidder-visible beat, this narrative dramatises each saga phase: initiation, reserve check, winner charge, fee calculation and payout, completion. Single bidder, single listing, no payment failure, no reserve disagreement, no `PendingSettlement` projection lag. Failure paths and the BIN settlement variant belong to subsequent narratives, not as branches inside this story.

This narrative is **forward-spec**: the Settlement BC has not shipped (M5 territory), so there is no `src/CritterBids.Settlement/` to audit Moment-by-Moment. The audit surface is W003 (`003-settlement-bc-deep-dive.md`) and `003-scenarios.md`; the lived-code audit lane is reserved for upstream BC contracts that the saga consumes (`ListingSold`, `ListingPublished`) and downstream broadcasts the bidder perceives (`SettlementCompleted` via Relay's BiddingHub). Findings discovered during authoring routed primarily to `workshop-update` (W003 storage-layer staleness against ADR 011) and `narrative-update` (reserve-check payload reconciliation against narrative 001 Moment 8); zero `code-update` findings against Settlement itself, by structural impossibility.

## Cast

- **SwiftFerret42** - the bidder, now the winner. Continuity from narrative 001: same anonymous session, same `BidderId`, $500 credit ceiling (hidden), provisional commitment to $55.00 hammer price. Single protagonist; the narrative is told entirely from her vantage.
- **GreyOwl12** - the seller. Offstage. Drafted, submitted, and published the keyboard in the days before the conference; receives the seller payout in Moment 4 but never appears in SwiftFerret42's view. Seller-perspective on the same payout is a candidate for narrative 004 (Selling BC backfill) or a future seller-perspective narrative.
- **The Settlement saga** - onstage across all five Moments. The dramatic engine of the narrative. The narrator dramatises the saga's progression through `Initiated`, `ReserveChecked`, `WinnerCharged`, `FeeCalculated`, `PayoutIssued`, `Completed`. Hosting choice (Wolverine Saga vs `ProcessManager<TState>` decider) is W003 Phase 1 Part 2 territory and `implementation-detail`; the narrator names phases and events, not the host.
- **The `PendingSettlement` projection** - onstage in Moment 1. A cached document the saga loads at workflow start to retrieve the keyboard's reserve, fee percentage, and seller identity without crossing the Settlement-Selling boundary. Seeded by `ListingPublished` at publish time, days before the conference; status flips from `Pending` to `Consumed` when the saga begins.
- **Auctions** - the bounded context that just published `ListingSold` over the cross-BC bus at narrative entry. Offstage; its work is finished by the time Settlement begins.
- **Selling** - the bounded context that emitted `ListingPublished` days before the conference. Offstage; its work seeded the `PendingSettlement` row.
- **Listings** - the bounded context that owns the read-side. Offstage; its `CatalogListingView` for the keyboard already reads `Status: "Sold"` from narrative 001 Moment 7.
- **Participants** - the bounded context that minted SwiftFerret42's session, her `BidderId`, and the hidden $500 credit ceiling. Offstage; the credit-ledger debit happens via Settlement's `WinnerCharged` event and updates Settlement's bidder-credit projection (not a Participants-internal projection).
- **Relay** - the bounded context whose BiddingHub broadcasts `SettlementCompleted` to SwiftFerret42's connection. Onstage in Moment 5. Forward-spec; Relay BC has not shipped (slice 6.3 P1 territory). The Moment narrates the broadcast as the system is designed to run.
- **Operations** - the bounded context whose `OperationsHub` may surface settlement state to the auction operator's dashboard. Offstage; SwiftFerret42 does not perceive the operator's view. Operator-perspective on the settlement is a `separate-narrative` deferral.
- **The auction operator** - offstage. May glance at the Operations dashboard to see settlement complete; SwiftFerret42 does not see this.

## Setting

Immediately after the gavel falls in narrative 001 Moment 7. SwiftFerret42's phone shows the "You Won" banner over the keyboard's `ListingDetailView`; the `CatalogListingView` for the keyboard already reads `Status: "Sold"`, `WinnerId: SwiftFerret42's ParticipantId`, `HammerPrice: $55.00`. Her credit-balance display still reads $500.00; the provisional commitment to $55 has not yet hit her ledger. The Flash session is in its terminal seconds; the operator's session timer reads roughly four minutes elapsed of the configured five. The other two listings in the lineup (Rare Pokemon Card, Hand-Carved Wooden Bowl) are in their own resolution paths and out of frame.

Auctions has just published `ListingSold { ListingId: keyboard, SellerId: GreyOwl12, WinnerId: SwiftFerret42, HammerPrice: $55.00, BidCount: 4, SoldAt: <now> }` over the cross-BC bus via the Wolverine transactional outbox. The message is in flight on RabbitMQ and Settlement's handler is about to consume it. The `PendingSettlement` row for the keyboard has been cached since the day GreyOwl12 published the listing: `(ListingId: keyboard, SellerId: GreyOwl12, ReservePrice: $50.00, BuyItNowPrice: $100.00, FeePercentage: 10.0, PublishedAt: <pre-conference>, Status: Pending)`. The row has been waiting hours or days for a `ListingSold` or `BuyItNowPurchased` to consume it; in this happy path, the projection is fully caught up by the time Settlement's handler fires.

Auction-system policy is at MVP defaults inherited from narrative 001 unchanged. The MVP credit-ledger posture applies: the winner charge debits SwiftFerret42's hidden $500 ceiling against Settlement's bidder-credit projection rather than crossing a real payment-processor boundary; the seller payout to GreyOwl12 is a ledger entry rather than a banking integration; compensation paths beyond MVP are deferred per W003 Phase 1 Part 3. Reserve-check authority lies with Settlement, not Auctions: Auctions' `ReserveMet` was the UX promise to SwiftFerret42 at the moment her bid crossed $50; Settlement's `ReserveCheckCompleted` is the binding financial verification. The five intermediate saga events resolve cleanly: no `PaymentFailed`, no reserve disagreement between Auctions and Settlement, no `PendingSettlement` projection lag. The keyboard's hammer price is $55.00; the platform fee is $5.50 (10%); the seller payout is $49.50; SwiftFerret42's credit balance after the charge lands at $445.00. Every other Settlement-perspective narrative (the failed payment, the reserve-not-met sale-fails branch, the BIN settlement path) documents what happens when one of these clean conditions is not in fact clean.
