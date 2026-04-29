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

## Moment 1: The keyboard enters Settlement

**Implements:** slice 6.1.

**Context.** The `ListingSold` integration event is in flight on RabbitMQ from narrative entry. Settlement's handler pool is registered against the cross-BC bus and ready to consume. SwiftFerret42's "You Won" banner is still onscreen; her credit-balance display reads $500.00. The `PendingSettlement` row for the keyboard sits in `Status: Pending`, cached since `ListingPublished` arrived days before with the seller identity, reserve price, BIN price, and fee percentage.

**Interaction.** Settlement's handler consumes `ListingSold { ListingId: keyboard, SellerId: GreyOwl12, WinnerId: SwiftFerret42, HammerPrice: $55.00, BidCount: 4, SoldAt: <now> }`. The handler derives `SettlementId` deterministically as `UuidV5(AuctionsNamespace, "settlement:" + ListingId)` per W003 Phase 1 Part 6's idempotence convention. It loads the `PendingSettlement` row by `ListingId` to retrieve the reserve, BIN price, and fee percentage cached since the listing was published; these are the fields the inbound `ListingSold` payload deliberately does not carry, by the BC boundary contract. The handler issues `InitiateSettlement { SettlementId, ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, Price: $55.00, Source: Bidding, ReservePrice: $50.00, FeePercentage: 10.0 }` to the decider.

**Response.** The decider runs against `state = null` and emits `SettlementInitiated { SettlementId, ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, Price: $55.00, Source: Bidding, InitiatedAt: <now> }`. The event is appended to a fresh stream keyed on `SettlementId`; the saga's state advances from `null` to `Initiated`. The `PendingSettlement` row for the keyboard transitions from `Status: Pending` to `Status: Consumed` as part of the same workflow start, marking that this settlement has claimed it; no other consumer of `ListingSold` will start a second settlement against the keyboard. SwiftFerret42's phone shows no change yet; the saga is below her perception, and the journey from auction-terminal-outcome to charge-and-payout has just begun.

**Why this matters to the bidder.** The keyboard's terminal auction outcome has crossed the BC boundary into Settlement, and a stream now exists that will carry the financial commitment to its conclusion. The deterministic `SettlementId` is the saga's idempotence guarantee: any replay of `ListingSold` will derive the same `SettlementId`, the decider will reject re-initiation against an already-`Initiated` state, and SwiftFerret42's $55 commitment will be applied to her ledger exactly once. She perceives nothing in this Moment, but the system's contract with her - you won, and you will be charged - is now durable across the cross-BC handoff.

### Things deliberately not included

- The `PendingSettlement` "row not found" retry path (W003 Phase 1 Part 1 Option A: Wolverine retry with backoff). *(`alternate-path-failure`; the happy-path Moment assumes the projection is caught up.)*
- BIN settlement entry (`Source: BuyItNow`, the W003 §1.2 scenario). *(`separate-narrative`.)*
- Re-initiation rejection from a non-null state (W003 §1.3 invalid-transition). *(`alternate-path-failure`.)*

## Moment 2: Settlement verifies the reserve

**Implements:** slice 6.1.

**Context.** The saga's state is `Initiated`. The state carries `HammerPrice: $55.00, ReservePrice: $50.00, FeePercentage: 10.0` and the participant identifiers, hydrated from `SettlementInitiated`'s payload at the evolver step that closed Moment 1. The next saga phase is the binding reserve check. Auctions' `ReserveMet` was published earlier in the auction lifecycle (narrative 001 Moment 6, when SwiftFerret42's $55 retaliation bid crossed the threshold); that was the UX promise to her. Settlement's job now is the financial verification.

**Interaction.** The saga issues `CheckReserve` to the decider against `state = SettlementState.Initiated { HammerPrice: $55.00, ReservePrice: $50.00, ...participant fields }`.

**Response.** The decider compares hammer price against reserve, finds $55.00 exceeds $50.00, and emits `ReserveCheckCompleted { HammerPrice: $55.00, ReservePrice: $50.00, WasMet: true, CompletedAt: <now> }`. The event is appended to the SettlementId stream. The evolver advances state from `Initiated` to `ReserveChecked(WasMet: true)` while preserving the base fields. SwiftFerret42 perceives nothing; the reserve check fires and resolves below her window. If the hammer had landed below the reserve - the alternate-path-failure variant - the saga would route to `PaymentFailed` per W003 Phase 1 Part 3, and the listing would settle to a fail-state without charging her credit. That branch is consciously not dramatised here.

**Why this matters to the bidder.** The reserve she could not see in the catalog has now been authoritatively confirmed as met. Auctions' `ReserveMet` told her the threshold was crossed at her bid time; Settlement's `ReserveCheckCompleted` confirms it as the financially binding decision by the BC that owns reserve enforcement. From this Moment forward, the sale is no longer contingent on the reserve question: SwiftFerret42 will be charged, GreyOwl12 will be paid, and the listing will close to a sold outcome. The two reserve checks - Auctions' UX-grade `ReserveMet` and Settlement's authority-grade `ReserveCheckCompleted` - cannot disagree in this happy path, and would route to an `alternate-path-failure` narrative if they ever did.

### Things deliberately not included

- The `WasMet: false` (sale-fails) branch from non-met reserve. *(`alternate-path-failure`.)*
- Reserve disagreement between Auctions' `ReserveMet` and Settlement's `ReserveCheckCompleted` (W001 parked question 5; the disagreement-handling discipline is decided ground but not dramatised here). *(`alternate-path-failure`.)*
- BIN-source reserve-check skip (`Source: BuyItNow` lands directly in `ReserveChecked(WasMet: true)` per W003 §1.2 and Phase 1 Part 5; BIN is out of scope). *(`separate-narrative`.)*

## Moment 3: SwiftFerret42's balance drops to $445

**Implements:** slice 6.1.

**Context.** The saga's state is `ReserveChecked(WasMet: true)`. The reserve threshold has been confirmed; the sale is no longer contingent. State still carries `HammerPrice: $55.00, FeePercentage: 10.0` and the participant identifiers. SwiftFerret42's "You Won" banner is still onscreen; her credit-balance display still reads $500.00. The next saga phase is the winner charge - the first phase where real (demo) money moves.

**Interaction.** The saga issues `ChargeWinner` to the decider against `state = SettlementState.ReserveChecked(WasMet: true) { HammerPrice: $55.00, WinnerId: SwiftFerret42, ...participant fields }`.

**Response.** The decider emits `WinnerCharged { SettlementId, WinnerId: SwiftFerret42, Amount: $55.00, ChargedAt: <now> }`. The event is appended to the SettlementId stream; the evolver advances state from `ReserveChecked(WasMet: true)` to `WinnerCharged`. The bidder-credit ledger debits SwiftFerret42's hidden $500.00 ceiling by $55.00, and her remaining credit lands at $445.00. Her phone's "You Won" banner ticks forward to "Charged $55.00." Her credit-balance display, which has read $500.00 since narrative 001 Moment 1, ticks down to $445.00. **First bidder-visible beat in narrative 002.**

**Why this matters to the bidder.** SwiftFerret42 has now committed (demo) money to the keyboard. The provisional ownership from narrative 001 Moment 7 has resolved into a definitive purchase: she has paid $55.00, the system has acknowledged the charge, and her remaining credit is the durable record of how much she has left to spend on any further listings in this session. The hidden credit ceiling she was assigned in narrative 001 Moment 1 is no longer entirely hidden - $445.00 visible in her phone's display tells her she had at least that much before the charge, though it does not yet reveal the original $500.00 ceiling. The MVP credit-ledger posture means no real banking integration is involved; the charge is a ledger entry against Settlement's read-side, not a payment-processor transaction.

### Things deliberately not included

- The `PaymentFailed` branch from charge failure (insufficient credit, payment-provider rejection, ledger-divergence). *(`alternate-path-failure`.)*
- Invalid-transition paths for `ChargeWinner` from `Initiated` (W003 §3.4; reserve check skipped) and from `WinnerCharged` (W003 §3.3; double-charge prevention). *(`alternate-path-failure`.)*
- The bidder-credit projection's lifecycle and read-model shape. *(`implementation-detail`; W003 does not define a named bidder-credit projection - see Finding 005 at session close.)*

## Moment 4: GreyOwl12's payout clears

**Implements:** slice 6.1.

**Context.** The saga's state is `WinnerCharged`. SwiftFerret42's $55.00 has been debited; her credit-balance display reads $445.00. The state carries `HammerPrice: $55.00, FeePercentage: 10.0`, `SellerId: GreyOwl12`, and the rest of the participant fields. The next two saga phases land in close succession: the platform fee calculation, then the seller payout to GreyOwl12. Both happen below SwiftFerret42's perception window; this Moment is narrator-led.

**Interaction.** The saga issues `CalculateFee` to the decider against `state = SettlementState.WinnerCharged { HammerPrice: $55.00, FeePercentage: 10.0, ... }`. Once the fee event is committed and the evolver advances state to `FeeCalculated`, the saga issues `IssueSellerPayout` against the new state.

**Response.** The first decider call computes the fee as $55.00 × 10% = $5.50, applying banker's rounding to two decimal places per W003 §4.2's MVP convention, and the seller payout as $55.00 - $5.50 = $49.50. It emits `FinalValueFeeCalculated { SettlementId, HammerPrice: $55.00, FeePercentage: 10.0, FeeAmount: $5.50, SellerPayout: $49.50, CalculatedAt: <now> }`. The event is appended to the SettlementId stream; the evolver advances state from `WinnerCharged` to `FeeCalculated(FeeAmount: $5.50, SellerPayout: $49.50)`. The fee-amount and payout-amount fields are now non-nullable on the state by design, so the next phase can read them without null checks.

The second decider call emits `SellerPayoutIssued { SettlementId, SellerId: GreyOwl12, PayoutAmount: $49.50, FeeDeducted: $5.50, IssuedAt: <now> }`. The event is appended to the SettlementId stream; the evolver advances state from `FeeCalculated` to `PayoutIssued`. GreyOwl12's seller-credit ledger increments by $49.50; in the MVP credit-ledger posture, this is a ledger entry against Settlement's seller-side read model rather than a banking transfer. SwiftFerret42 perceives nothing - her phone display still reads "Charged $55.00" and her balance still reads $445.00.

**Why this matters to the bidder.** SwiftFerret42 does not see this Moment, but it is the Moment in which her $55.00 commitment becomes the seller's $49.50 receipt. The two-step path - fee calculation, then payout - is what allows the seller and the platform to never disagree on totals: the rounding rule is the single source of truth, applied once, in a single `FinalValueFeeCalculated` event whose math any auditor can replay. From SwiftFerret42's perspective, her $55.00 split into a $5.50 platform fee and a $49.50 seller payout is the journey's commercial outcome, the Moment GreyOwl12 receives the value of his keyboard. The seller's experience of this beat - "$49.50 just landed in my account" - is a candidate for a future seller-perspective narrative; from SwiftFerret42's vantage, the seller's gain is implicit in the system's silent acknowledgement that her purchase has gone through.

### Things deliberately not included

- Banker's rounding edge cases for fractional-cent prices (W003 §4.2; the keyboard's $55.00 lands on a clean $5.50 fee with no rounding ambiguity). *(`implementation-detail`.)*
- Invalid-transition paths for `CalculateFee` from `FeeCalculated` (W003 §4.3) and `IssueSellerPayout` from `WinnerCharged` (W003 §5.2). *(`alternate-path-failure`.)*
- Compensation paths if the seller payout fails to land (W003 Phase 1 Part 3 defers compensation design beyond MVP). *(`post-MVP`.)*

## Moment 5: The keyboard is hers

**Implements:** slice 6.1, slice 6.3.

**Context.** The saga's state is `PayoutIssued`. Both money-moving phases have completed: SwiftFerret42's $55.00 has been debited; GreyOwl12's $49.50 has been credited. The state carries the full settlement record - `HammerPrice: $55.00, FeeAmount: $5.50, SellerPayout: $49.50, ListingId, WinnerId, SellerId`. SwiftFerret42's "Charged $55.00" banner is still onscreen; her balance reads $445.00. Relay's BiddingHub holds her live SignalR connection from narrative 001 onward, ready to broadcast Settlement-grade pushes.

**Interaction.** The saga issues `CompleteSettlement` to the decider against `state = SettlementState.PayoutIssued(FeeAmount: $5.50, SellerPayout: $49.50) { ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, HammerPrice: $55.00 }`. This is the saga's terminal command; the decider's pattern match against the `PayoutIssued` state is the only legitimate entry to settlement closure (W003 §6.2 rejects `CompleteSettlement` from any other state).

**Response.** The decider emits `SettlementCompleted { SettlementId, ListingId: keyboard, WinnerId: SwiftFerret42, SellerId: GreyOwl12, HammerPrice: $55.00, FeeAmount: $5.50, SellerPayout: $49.50, CompletedAt: <now> }`. The event is appended to the SettlementId stream as the terminal entry; the evolver advances state from `PayoutIssued` to `Completed`. The settlement's financial event stream is closed at terminal state and persists as the audit log per W003 §"Financial Event Stream"; no further events are appended.

Relay's BiddingHub broadcasts the completion to SwiftFerret42's connection: `{ type: "SettlementCompleted", listingId: keyboard, hammerPrice: 55.00, remainingCredit: 445.00 }`. Her phone's "Charged $55.00" banner ticks forward to "Charged $55.00 to your credit. The keyboard is yours." The banner has now traversed all three states it carries through Settlement: "You Won" (inherited from narrative 001 Moment 7), "Charged $55.00" (Moment 3's tick-forward), and "Charged $55.00 to your credit. The keyboard is yours." (this Moment's tick-forward). **Final bidder-visible beat in narrative 002.**

**Why this matters to the bidder.** The journey arc closes here. SwiftFerret42 began this narrative inheriting a "You Won" banner whose meaning was only provisional - the gavel had fallen, but no money had moved. Five Moments later, every Settlement phase has resolved cleanly: the reserve verified, the charge committed, the fee carved, the seller paid, the saga closed at terminal state. The keyboard is durably hers; her credit balance reads the post-charge $445.00 that will carry her through any further listings in the session; the seller has been compensated; the platform has taken its cut. The hammer's promise from narrative 001 Moment 7 - "you won this listing" - has been redeemed in financial fact. Every alternate narrative branch the project will eventually author - the failed payment, the reserve-not-met sale-fails branch, the BIN settlement variant, the seller-perspective companion to this beat - measures itself against this happy-path closure as the canonical reference of what completion looks like.

### Things deliberately not included

- The `CompleteSettlement` from `FeeCalculated` invalid-transition (W003 §6.2; payout not issued). *(`alternate-path-failure`.)*
- The Wolverine Saga primitive's `MarkCompleted()` and saga-document-removal mechanics. *(`implementation-detail`; W003 Phase 1 Part 2 hosting territory.)*
- The Operations BC's dashboard view of the settlement closing for the auction operator. *(`separate-narrative`; operator-perspective.)*

## Deferred from this narrative

The following were deliberately not narrated in this Settlement-perspective happy-path narrative. Each is named with its disposition so future sessions can pull from this list when scoping the next narrative, ADR, skill file, or implementation prompt. Items here are not bugs or omissions; they are consciously deferred and traceable. Items recorded in `002-findings.md` as `document-as-intentional` (settled design choices) are not duplicated here - the cumulative section is a backlog feeder, not a transparency footnote.

### `defer` (revisit when trigger lands)

- Lived-code audit of the Settlement BC saga across all five Moments (trigger: M5 ship target. The narrative renders the design from W003; the lived-code audit lane reactivates when Settlement BC ships and the M5-S1 slice's retrospective surfaces concrete divergence between W003's spec and the implementation).
- Lived-code audit of the Relay BC's BiddingHub broadcast (Moment 5; trigger: M4 Tier 4 ship target. The Moment renders the broadcast as the system is designed to run).

### `post-MVP` (beyond v1 scope)

- Compensation paths if the seller payout fails to land (Moment 4; W003 Phase 1 Part 3 explicitly defers compensation design beyond MVP).

### `separate-narrative` (other journey perspectives)

- BIN settlement entry path (`Source: BuyItNow`; Moments 1 and 2 reference; W003 §1.2 and Phase 1 Part 5 cover the structurally-distinct flow).
- The Operations BC's dashboard view of the settlement closing for the auction operator (Moment 5; operator-perspective).
- The seller's experience of receiving the payout notification (Moment 4 and Moment 5; candidate for narrative 004 Selling-BC backfill or a future seller-perspective Settlement narrative).

### `implementation-detail` (skill file or ADR territory)

- The bidder-credit projection's lifecycle and read-model shape (Moment 3; W003 does not define a named bidder-credit projection - see Finding 005).
- Banker's rounding edge cases for fractional-cent prices (Moment 4; W003 §4.2; the keyboard's $55.00 lands on a clean $5.50 fee with no rounding ambiguity).
- The Wolverine Saga primitive's `MarkCompleted()` and saga-document-removal mechanics (Moment 5; W003 Phase 1 Part 2 hosting choice between Wolverine Saga and `ProcessManager<TState>` decider remains deferred).
- Wolverine inbox deduplication mechanics underpinning the deterministic-`SettlementId` idempotence story (Moment 1).

### `alternate-path-failure` (failure modes warranting their own narratives)

- The `PendingSettlement` "row not found" retry path (Moment 1; W003 Phase 1 Part 1 Option A: Wolverine retry with backoff).
- Re-initiation rejection from a non-null state (Moment 1; W003 §1.3).
- The `WasMet: false` (sale-fails) branch from non-met reserve (Moment 2).
- Reserve disagreement between Auctions' `ReserveMet` and Settlement's `ReserveCheckCompleted` (Moment 2; W001 parked question 5).
- The `PaymentFailed` branch from charge failure (Moment 3; insufficient credit, payment-provider rejection, ledger-divergence).
- Invalid-transition paths for `ChargeWinner` from `Initiated` (Moment 3; W003 §3.4) and from `WinnerCharged` (Moment 3; W003 §3.3).
- Invalid-transition paths for `CalculateFee` from `FeeCalculated` (Moment 4; W003 §4.3) and `IssueSellerPayout` from `WinnerCharged` (Moment 4; W003 §5.2).
- The `CompleteSettlement` from `FeeCalculated` invalid-transition (Moment 5; W003 §6.2; payout not issued).

## Retrospective

### Narrative intent vs. outcome

Stated goal at session start: author the Settlement BC's backfill narrative covering SwiftFerret42's experience as the saga charges her credit, calculates the platform fee, pays GreyOwl12 the seller payout, and emits `SettlementCompleted`. Audit W003 and `003-scenarios.md` against the narrative as drafted, route every disagreement through the four-lane findings discipline, surface the W003 storage-layer staleness against ADR 011 (All-Marten Pivot), add per-row narrative back-references on the W003 slices the narrative implements.

**Outcome.** Five Moments covering W001 slices 6.1 and 6.3. Bidder-visible bookends (Moments 3 and 5; the credit-debit and the closing banner update) frame three narrator-led Moments (1, 2, 4) where SwiftFerret42 is offstage and the narrator carries the saga state. Five findings filed in `002-findings.md` across three routing lanes: 1 `narrative-update` (F001, against narrative 001 Moment 8), 3 `workshop-update` (F003 storage-staleness, F004 §1.1-vs-§7.1 payload mismatch, F005 missing bidder-credit projection), 1 `document-as-intentional` (F002 Price/HammerPrice rename across initiation). Zero `code-update` findings against Settlement itself, by structural impossibility - Settlement BC is unshipped. Cast and Setting locked first; Moment-by-Moment sign-off cadence held through all five Moments. Forward-spec posture handled cleanly. Goal met.

### What worked

- **Forward-spec posture handled cleanly.** Zero `code-update` findings; no spurious shoehorning of "Settlement code is wrong" claims against absent code. The narrator-renders-design framing held throughout.
- **Findings-during-drafting cadence** carried over from narrative 001 unchanged. Five findings surfaced as Moments were drafted, not retroactively at session close. F002's mid-session character shift (from `workshop-update` to `document-as-intentional` once the §7.1 evolver evidence surfaced) demonstrated that the lane semantics tolerate routing revision when new evidence lands.
- **Multi-phase Moment 4 worked at the multi-paragraph `Response.` level.** The README's multi-slice convention (paragraphs grow, labels do not) extends naturally to multi-saga-phase Moments. Moment 4's `Response.` block dramatised the fee calculation and the seller payout as two paragraphs under one label; no new structural pattern was needed.
- **Cross-narrative continuity carried clean.** Narrative 001's terminal state (the "You Won" banner inherited at narrative 002 entry) flowed into narrative 002's opening Setting paragraph and remained the bidder's onscreen anchor through Moments 1 and 2. The phone-banner state-machine (You Won → Charged $55.00 → Charged $55.00 to your credit. The keyboard is yours.) became a per-Moment journey-grain through-line.
- **Title-ambiguity check caught a misnamed Moment 1 before commit.** The user-flagged ambiguity in "Settlement claims the keyboard" (the natural English parse misplaces SwiftFerret42 as the subject) led to a pivot to "The keyboard enters Settlement". Lesson: title verbs need an ambiguity-check pass that asks "does the natural English subject match the intended subject?".
- **Per-Moment deferred lists trimmed to three entries** after the user redirected from heavier per-Moment lists. The trim discipline (deferred shouldn't restate body content; narrative-level dispositions consolidate at the narrative level) held across Moments 2 through 5. Moment 1's revision provided the calibration; subsequent Moments matched it.
- **Em-dash hygiene held in the narrative file.** Zero em dashes in any committed narrative or retrospective prose. The em-dash audit ran clean before every narrative commit. Pre-existing em dashes in W003 were grandfathered per the convention's grandfather clause. One slip occurred in the closing-section commit's message itself (the commit subject for this section's commit used an em dash where a hyphen was correct); the slip is recorded so narratives 003-005 carry the lesson into commit-message authoring as well.
- **PR-shape fold honoured the Cast/Setting/Moment cadence.** When the user folded the prompt PR and the narrative session into a single branch mid-session, the per-Moment commit discipline carried straight through; the PR boundary changed but the working pattern did not.

### What was hard

- **Moment 3's missing disposition tag** on the bidder-credit projection deferred entry was caught only at the consolidation step (`## Deferred from this narrative`), not at the per-Moment commit. Lesson: per-Moment "Things deliberately not included" lists should always carry an explicit disposition tag at draft time, not at consolidation time. Narrative 001 caught this consistently; narrative 002 missed it once. Future narratives should treat the tag as a checklist item before sign-off.
- **F002's character shifted mid-session** from `workshop-update` to `document-as-intentional` once §7.1's evolver scenarios revealed that the `Price` → `HammerPrice` rename across initiation is intentional (source-agnostic at command time, semantically `HammerPrice` post-initiation), not an inconsistency. Lesson: read the full scenarios section - including evolver and projection - before routing field-naming findings; an apparent inconsistency in §1 may dissolve once §7's evolver evidence is in hand.
- **Forward-spec narrating amplifies workshop-fragility.** When the audit floor isn't there, the narrative inherits W003's gaps wholesale. F004 (the §1.1-vs-§7.1 SettlementInitiated payload mismatch) and F005 (the missing bidder-credit projection name) both surfaced because the narrator had to commit to specific event payloads and projection names that W003 either truncated or never named. Lesson: forward-spec narratives surface workshop gaps the lived-code audit would have masked, because the narrator has nowhere else to look.

### Decisions about how to author (meta-decisions worth carrying forward)

- **For forward-spec narratives, lived-code-audit defer is narrative-level, not per-Moment.** Five identical "lived-code audit deferred to M5" entries would be noise; one consolidated `defer` entry in the narrative-level `## Deferred from this narrative` section is correct.
- **Field names render verbatim from scenarios.** When scenarios use `Price` on initiation events and `HammerPrice` on later events, the narrative preserves that asymmetry. Smoothing field names in prose would mask findings that should surface.
- **Multi-phase Moments use multi-paragraph `Response.` blocks.** Same convention as multi-slice Moments per the README; extending the convention to per-saga-phase grouping does not require a README amendment.
- **Narrator-led Moments use `Why this matters to the bidder.` as load-bearing.** When the protagonist is offstage for an entire Moment (Moments 1, 2, and 4), the Why-paragraph anchors the bidder's journey arc by naming the invariant the saga's offscreen work establishes for her.
- **Cite-and-edit fixes across narratives are constrained to single paragraphs (Phase 5 §7) and bundled into one parent finding.** F001 covers three narrative 001 Moment 8 corrections in one parent finding because all three live in the same `Response.` paragraph; splitting into F001a/b/c would have proliferated findings without changing the resolution scope.
- **Title verbs need an ambiguity-check pass.** The Moment 1 title revision is the canonical example for narratives 003-005: "claims the keyboard" → "enters Settlement" because the natural English subject of "claims" is a person, not a system, and SwiftFerret42 was the wrong person.

### Patterns established for narratives 003-005

Inherited from narrative 001 unchanged: bounded frontmatter v1, prose-paragraph Moment body, multi-slice (and now multi-saga-phase) Moments grow in paragraphs, single-named-protagonist plus omniscient narrator, seven disposition tags for deferral, per-Moment plus cumulative deferral discipline, code-style backticks for events and projection names.

CritterBids-specific patterns established or refined this session:

- **Forward-spec posture is its own discipline.** Future narratives that hit forward-spec slices apply this session's playbook: render the workshop's design, route absent code as `defer` at the narrative level, expect zero `code-update` findings against the absent BC, expect elevated `workshop-update` findings as the narrator forces W003 (or its analogue) to commit on payloads and projections it may have left ambiguous.
- **Disposition tag at draft time, not at consolidation time.** Every per-Moment "Things deliberately not included" entry carries an explicit tag. Narrative 002's Moment 3 missed one and the consolidation step caught it; narratives 003-005 should not need the consolidation-step catch.
- **F002-class findings (`document-as-intentional` for intentional-but-undocumented W003 conventions) are real.** When a workshop's prose underspecifies a deliberate convention, the routing is `document-as-intentional` against the workshop, not `workshop-update`. The convention is not wrong; the documentation is incomplete. The resolution is a workshop edit that names the convention, not a behavioral change.
- **W003 minimum-scope sweep approach is reusable for narratives 003-005.** When a narrative's findings discipline routes a `workshop-update` against its source workshop, the minimum-scope sweep edits only the framing the narrative directly touches; the broader sweep is a follow-up. This held for narrative 002's F003 (Polecat / SQL Server staleness) and is the recommended posture for narratives 003 (W001), 004 (W004), and 005 (W002).

### Quality signal from the session

User feedback was clean throughout. Two minor amendments at sign-off (Moment 1 title revision after the "claims" ambiguity flag; Moment 1 deferred-list trim from five entries to three). All five findings' routings held under user adjudication, including F002's mid-session re-routing once the evolver evidence surfaced. The lean-opinions-on-questions practice continued to land. The fold-into-one-PR decision did not destabilise the per-Moment cadence.

The forward-spec narrative-as-design-rendering posture surfaced exactly where the prompt's "Heads-up sources of likely findings" anticipated: at W003's Polecat staleness (F003), at the Price/HammerPrice convention (F002), at the §1.1-vs-§7.1 payload mismatch (F004), and at the missing bidder-credit projection name (F005). The discipline absorbed all four cleanly.

### Follow-ups generated

- **F001** (narrative 001 Moment 8 saga-event payload corrections: `SettlementInitiated` `HammerPrice` → `Price`; `ReserveCheckCompleted` `Result: "Met"` → `WasMet: true`; `WinnerCharged` `AmountCharged` → `Amount` and drop `RemainingCredit`) **resolved in this PR** via single-paragraph edit to narrative 001 Moment 8's `Response.` block.
- **F003** (W003 storage-layer staleness against ADR 011) **resolved in this PR** via minimum-scope edit to W003 Phase 1 Part 1 PendingSettlement framing and the Ubiquitous Language Financial Event Stream entry. Polecat / SQL Server references replaced with Marten / PostgreSQL.
- **F002, F004, F005 deferred to a W003 follow-up PR.** The follow-up authors a coordinated W003 edit covering the Price/HammerPrice convention documentation (F002), the §1.1-vs-§7.1 SettlementInitiated payload reconciliation (F004), and the named bidder-credit projection (F005). Stub follow-up prompt is not authored this session because none of the three findings is `code-update`; the W003 follow-up is workshop-edit territory and is its own session.
- **W003 broader storage-staleness sweep** (beyond minimum-scope F003 fix) deferred per the user's Q4 minimum-scope lean. A future session may sweep the remaining Polecat references in W003 Phase 2 storytelling, Phase 3 scenarios cross-references, and the @BackendDeveloper notes; that sweep is not narrative-driven and can run as its own workshop-cleanup session.
- **M5 milestone doc and M5-S1 prompt** are Phase 5 Item 4 territory (later session). Narrative 002 enables them: it is now the canonical Settlement narrative the M5-S1 prompt will cite per the cutover-gate definition.
- **Methodology log Entry 001 considered and consciously skipped** at session close. Phase 4 retro updated the time-box to "after Phase 5 closes, or after methodology log carries three entries, whichever comes first." The three lived-BC narratives ahead (003 Participants, 004 Selling, 005 Auctions) are stronger Entry 001 anchors than narrative 002 because they will surface concrete `code-update` findings against shipped BCs; narrative 002's forward-spec posture means its cross-cutting observations are about narrative-authoring-against-spec-only, which is interesting but premature as a load-bearing methodology-log entry. The entry-criteria gate held; silence was the right call.

### Narrative #3 candidate

Per Phase 5 prompt §2.3, narrative 003 is the Participants-BC backfill (`003-bidder-starts-anonymous-session`). Smallest lived-code surface (M1 baseline plus Participants BC scaffold from M1-S2); companion to narrative 001 Moment 1 at finer grain. Narrative 002's discipline hands off cleanly: the per-Moment cadence, findings-during-drafting, em-dash hygiene, title-ambiguity check, narrator-led Moments where applicable, the per-Moment-disposition-tag-at-draft-time refinement.

### Narrative status

**Complete (v0.1, 2026-04-29).** Five Moments, cumulative deferred section, retrospective. Format conventions inherited from narrative 001 unchanged; CritterBids-specific patterns refined for forward-spec posture. Status flipped to `accepted` in the session-close commit (the final commit on this branch).

---

## Document History

- **v0.1** (2026-04-29): Initial authoring as foundation-refresh Phase 5 Item 1a deliverable. Five Moments covering W001 slices 6.1 and 6.3; the closing Moment is multi-slice. Forward-spec posture (Settlement BC unshipped, M5 ship target) handled at the narrative level via consolidated `defer` entry in the cumulative deferred section. Five findings filed in `002-findings.md` across three routing lanes (`narrative-update`, `workshop-update`, `document-as-intentional`); zero `code-update` findings against Settlement, by structural impossibility. F001 (narrative 001 Moment 8 saga-event payload) and F003 (W003 storage-layer staleness against ADR 011) resolved in the same PR as the narrative landed. F002, F004, and F005 deferred to a W003 follow-up PR. Single-PR fold (prompt + narrative session) per user direction at session start; narrative-internal retro records the fold.
