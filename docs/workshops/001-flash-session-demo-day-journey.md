# Workshop 001 — Flash Session Demo-Day Journey

**Type:** User Journey (cross-cutting)
**Date started:** 2026-04-09
**Status:** In progress — Phase 3 next

**Scope:** The complete happy-path demo scenario from the overview doc. A presenter at a conference runs a Flash Session with live audience participation, from QR scan through obligation fulfillment. This is the demo-day spine.

**Personas active:** All eight (`@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@FrontendDeveloper`, `@QA`, `@ProductOwner`, `@UX`)

**Tradeoffs acknowledged:** This cross-cutting journey produces the horizontal map (BC handoffs, integration events, milestone scope). It defers to BC-focused workshops: aggregate internals, saga state machine details, DCB boundary model design, compensation/failure paths, and the deeper "what if" edge cases.

---

## Phase 1 — Verification Brain Dump

CritterBids already has an event vocabulary (`docs/vision/domain-events.md`). Phase 1 is a verification pass: walk the demo-day journey beat by beat and confirm the vocabulary accounts for everything that happens.

### Act 1 — Setup (before the audience arrives)

**Beat 1: Seller creates listings in advance**
Seller (the presenter, pre-registered) drafts, submits, approves, and publishes listings.
Events: `DraftListingCreated` → `DraftListingUpdated` → `ListingSubmitted` → `ListingApproved` → `ListingPublished`
All in Selling BC. All accounted for in vocabulary.

**Beat 2: Operations staff creates a Flash Session and attaches listings**
Events: `SessionCreated`, `ListingAttachedToSession`
Both in Auctions BC.
`ListingAttachedToSession` was **missing from the vocabulary** — identified during this phase and added.

**Beat 3: Infrastructure ready**
Stable URL, QR code on the projector. Infrastructure concern, not a domain event.

### Act 2 — Audience arrives

**Beat 4: Participants scan the QR code**
Event: `ParticipantSessionStarted` (Participants BC → Auctions, Relay)
Accounted for. Participant receives generated display name and hidden credit ceiling.

**Beat 5: Participants browse the catalog**
Read path only. Listings BC serves the catalog view. No command, no event.

> **Parked (UX):** What does the participant see for a listing attached to an unstarted session? "Starting soon"? "Opens when session begins"? Frontend/UX concern, not an event model question.

### Act 3 — The session goes live

**Beat 6: Presenter starts the Flash Session**
Event: `SessionStarted` (Auctions BC → Listings, Relay, Operations)
Cascades to `BiddingOpened` for each attached listing.
`BiddingOpened` crosses to Listings and Relay.

> **Parked (Architect):** Is `SessionStarted` one event that triggers N `BiddingOpened` events, or does the handler fan out internally? Implementation detail for Auctions BC workshop.

### Act 4 — Bidding

**Beat 7: A participant places a bid**
Command: `PlaceBid`. Event: `BidPlaced` (Auctions → Relay).
Accounted for.

**Beat 8: Another participant is outbid**
Not a separate event. `BidPlaced` itself is the signal. Relay pushes "outbid" notification to previous high bidder.

**Beat 9: A participant registers a proxy bid**
Event: `ProxyBidRegistered` (internal to Auctions). Proxy Bid Manager saga starts.
When a competing bid arrives, the proxy auto-bids → another `BidPlaced` (flagged as proxy-initiated).
Accounted for.

**Beat 10: A proxy bid's maximum is exceeded**
Event: `ProxyBidExhausted` (internal to Auctions). Proxy saga terminates.

> **Parked (QA):** Should `ProxyBidExhausted` be promoted to integration so Relay can send a "your proxy is done" notification? Current reasoning: the participant already gets an outbid notification from `BidPlaced`. The exhaustion is a UX nicety, not a domain necessity. Revisit in Auctions BC workshop.

**Beat 11: Buy It Now is used**
Event: `BuyItNowPurchased` (Auctions → Settlement). Bypasses the auction entirely.
Accounted for.

**Beat 12: First bid removes Buy It Now**
Event: `BuyItNowOptionRemoved` (Auctions → Relay, Listings).
Accounted for.

**Beat 13: High bid reaches the reserve**
Event: `ReserveMet` (Auctions → Relay). Reserve amount is never revealed.
Accounted for.

### Act 5 — Extended bidding and close

**Beat 14: Bid arrives in the trigger window**
Event: `ExtendedBiddingTriggered` (Auctions → Relay, Operations). Close time pushed out.
Accounted for.

> **Parked (QA):** What if extended bidding triggers multiple times in sequence? Each trigger produces its own `ExtendedBiddingTriggered` with a new close time. Saga cancels and reschedules. Detail for Auctions BC workshop.

**Beat 15: Bidding closes**
Event: `BiddingClosed` (internal to Auctions). Auction Closing saga evaluates the result.

**Beat 16: Winner declared (reserve met)**
Event: `ListingSold` (Auctions → Settlement, Listings, Relay, Operations).
This is the happy-path business outcome. Accounted for.

**Beat 17: No winner (alternate path)**
Event: `ListingPassed` (Auctions → Listings, Relay, Operations).
Reserve not met or no bids. Accounted for.

### Act 6 — Settlement

**Beat 18: Settlement saga starts**
Event: `SettlementInitiated` (internal). Triggered by `ListingSold` or `BuyItNowPurchased`.

**Beat 19: Reserve comparison**
Event: `ReserveCheckCompleted` (internal). Settlement compares hammer price to opaque reserve.

> **Parked (QA/Architect):** The reserve check tension between Auctions (`ReserveMet` as a threshold signal) and Settlement (`ReserveCheckCompleted` as the binding comparison). Is there a scenario where they disagree? Should `ListingSold` be published before Settlement confirms? Design tension for Auctions + Settlement BC workshops.

**Beat 20: Winner charged**
Event: `WinnerCharged` (internal). Credit ceiling debited.

**Beat 21: Fee calculated**
Event: `FinalValueFeeCalculated` (internal). Percentage of hammer price.

**Beat 22: Seller payout recorded**
Event: `SellerPayoutIssued` (Settlement → Relay). Notify seller.

**Beat 23: Settlement completes**
Event: `SettlementCompleted` (Settlement → Obligations). Green light for post-sale coordination.

### Act 7 — Obligations

**Beat 24: Post-sale coordination starts**
Event: `PostSaleCoordinationStarted` (internal). Triggered by `SettlementCompleted`.

**Beat 25: Shipping reminder sent**
Event: `ShippingReminderSent` (internal). Scheduled message.

**Beat 26: Seller provides tracking**
Event: `TrackingInfoProvided` (Obligations → Relay). Cancels pending reminders.

**Beat 27: Delivery confirmed**
Event: `DeliveryConfirmed` (internal).

**Beat 28: Obligation fulfilled**
Event: `ObligationFulfilled` (Obligations → Relay, Operations). Saga completes.

### Throughout — Operations Dashboard

The ops dashboard (Operations BC) is live the entire time, consuming integration events from every BC. The presenter's projector shows bid activity, saga state transitions, settlement progress, and obligation status updating in real time.

---

## Phase 1 Summary

**Vocabulary changes made:**
- Added `ListingAttachedToSession` (🔵 Integration, Auctions BC) to `domain-events.md`
- Added to `bounded-contexts.md` integration out (Auctions), integration in (Listings, Operations), and topology diagram

**Parked questions for BC-focused workshops:**

| # | Question | Source Persona | Target Workshop |
|---|----------|---------------|-----------------|
| 1 | What does the participant UI show for a listing in an unstarted session? | `@UX` | Frontend / Listings BC |
| 2 | `SessionStarted` → N x `BiddingOpened` fan-out: handler design | `@Architect` | Auctions BC |
| 3 | Should `ProxyBidExhausted` be promoted to integration? | `@QA` | Auctions BC |
| 4 | Extended bidding triggering multiple times: saga state detail | `@QA` | Auctions BC |
| 5 | Reserve check authority tension: Auctions `ReserveMet` vs Settlement `ReserveCheckCompleted` | `@QA` / `@Architect` | Auctions + Settlement BCs |

**Verdict:** The existing domain-events vocabulary covers this journey. One event added (`ListingAttachedToSession`). All parked questions are BC-depth concerns appropriate for follow-up workshops.

---

## Phase 2 — Storytelling

Phase 1 confirmed we have the right events. Phase 2 arranges them into a temporal narrative and asks the harder questions: what's happening concurrently? What must happen before what? Where does the participant experience have gaps or tension? What makes this compelling to watch from the audience?

The key insight for the Flash Session journey is that **time has four distinct speeds**:

- **Prep phase** (hours/days before) — relaxed, sequential, no audience
- **Arrival phase** (minutes before) — concurrent onboarding, browsing, anticipation
- **Hot phase** (5-10 minutes) — everything happening at once, multiple listings, concurrent bidders, real-time pressure
- **Resolution phase** (seconds to minutes after close) — settlement cascade, winner declarations, the climax payoff

### The Timeline

#### T-days: Prep (Selling + Auctions setup)

```
Selling BC                          Auctions BC                    Listings BC
─────────────────────────────────────────────────────────────────────────────────
DraftListingCreated (×3-5)
DraftListingUpdated (×N)
ListingSubmitted (×3-5)
ListingApproved (×3-5)
ListingPublished (×3-5) ──────────► receives ListingPublished ──► receives ListingPublished
                                    (knows listing exists,         (catalog: "upcoming")
                                     can be attached to session)
                                    SessionCreated ────────────────►
                                    ListingAttachedToSession (×3-5) ► (catalog: "in session X")
```

**Hard dependencies at this stage:**

- `ListingPublished` must happen before `ListingAttachedToSession`. You cannot attach a draft.
- `SessionCreated` must happen before `ListingAttachedToSession`. The container must exist.
- `SellerRegistrationCompleted` must have happened before any listing creation. The presenter must be a registered seller.
- These dependencies are enforced by the Auctions BC (it rejects attachment if the listing hasn't been published) and the Selling BC (it rejects listing creation if the participant isn't a registered seller).

**No time pressure.** The presenter does this before the talk, possibly the night before. Mistakes can be corrected. Listings can be revised or re-attached.

**Ops dashboard at this point:** Shows the session in a "created" state with its attached listings. Staff can verify everything looks right before the audience arrives. This is the "preflight check" moment.

#### T-minutes: Arrival (Participants + Listings read path)

```
Participants BC                     Listings BC (read path)         Relay BC
─────────────────────────────────────────────────────────────────────────────────
ParticipantSessionStarted ─────────────────────────────────────────► (SignalR enrollment)
ParticipantSessionStarted ─────────────────────────────────────────► (SignalR enrollment)
ParticipantSessionStarted ─────────────────────────────────────────► (SignalR enrollment)
  ... (30-40 concurrent)            ◄── browse catalog ──►
                                    Listings serves "upcoming"
                                    listings with session info
```

**Concurrency is the defining characteristic.** Thirty to forty developers are scanning a QR code within a minute or two. Each produces a `ParticipantSessionStarted`. These are independent — no ordering dependency between participants. Relay enrolls each participant in SignalR groups as their session starts.

**What the participant sees:** They land on a page with their generated display name ("SwiftFerret42"), their bidder number, and a catalog showing the session's listings. Each listing shows its title, starting bid, and a status indicator: something like "Opens when session starts" or a countdown if the presenter has announced timing.

**What the participant does NOT see:** Their credit ceiling. This is hidden. They discover it only when they try to bid above it and get a `BidRejected`.

**Ops dashboard at this point:** Participant count climbing in real time. The presenter can see "32 participants connected" before hitting the start button. This builds visible anticipation.

**Narrative tension for the demo:** The audience is in their seats, phones out, connected. The presenter has the ops dashboard on the projector. Everyone can see the participant count. The room knows something is about to happen. This is the "countdown" moment, and it's entirely a read-path and SignalR experience — no write events needed.

#### T-0: The Start (Auctions — the cascade)

```
Auctions BC                         Relay BC                       Listings BC
─────────────────────────────────────────────────────────────────────────────────
SessionStarted ─────────────────────► push "Session is live!" ────► flip listings to "active"
  │
  ├── BiddingOpened (Listing A) ───► push "Listing A now open" ──► catalog: "open, accepting bids"
  ├── BiddingOpened (Listing B) ───► push "Listing B now open" ──► catalog: "open, accepting bids"
  ├── BiddingOpened (Listing C) ───► push "Listing C now open" ──► catalog: "open, accepting bids"
  └── BiddingOpened (Listing D) ───► push "Listing D now open" ──► catalog: "open, accepting bids"
```

**This is a single command (`StartSession`) producing a cascade.** `SessionStarted` is the one integration event from the command. The Auctions BC handler then produces `BiddingOpened` for each attached listing internally. Each `BiddingOpened` carries its own scheduled close time (e.g., 5 minutes from now).

**Ordering dependency:** `SessionStarted` must happen before any `BiddingOpened`. But the N `BiddingOpened` events have no ordering dependency on each other — they can be processed concurrently.

**Wall-clock time:** This entire cascade takes milliseconds. From the participant's perspective, they tap "refresh" or their SignalR connection pushes the update, and all listings flip to "open" essentially simultaneously.

**What the audience sees:** The projector shows the ops dashboard lighting up — all listings going live at once. On their phones, the catalog flips from "upcoming" to "bid now" on every listing. This is the "go" moment.

#### T+0 to T+5min: The Hot Phase (concurrent bidding)

This is where the linear timeline breaks down. Multiple things are happening simultaneously across multiple listings. Each listing has its own independent event stream:

```
                    Listing A              Listing B              Listing C
                    ─────────              ─────────              ─────────
T+0:10              BidPlaced ($10)
T+0:15                                     BidPlaced ($25)
T+0:22              BidPlaced ($15)                                BidPlaced ($5)
T+0:30              ProxyBidRegistered                             BidPlaced ($8)
T+0:35                                     BidPlaced ($30)
T+0:40              BidPlaced ($18)  ◄── proxy auto-bid
T+0:45              ReserveMet                                     BuyItNowPurchased ──► Settlement
T+0:50                                     BuyItNowOptionRemoved
T+1:00              BidPlaced ($22)
  ...                 ...                    ...
```

**Key observations about the hot phase:**

**Each listing is its own world.** The DCB operates per-listing. Proxy bid managers operate per-listing-per-bidder. Close timers are per-listing. There is no cross-listing dependency during bidding.

**A participant's attention is split.** They might be watching Listing A, place a bid, then switch to Listing C. Their phone shows one listing at a time but they're mentally tracking several. The bid feed (via Relay/SignalR) keeps them informed of activity on listings they're watching even when they're looking at a different one.

**Buy It Now can short-circuit a listing.** If Listing C has a Buy It Now price and someone takes it at T+0:45, that listing jumps straight to Settlement while A and B are still in active bidding. The catalog updates immediately — Listing C shows "Sold (Buy It Now)" while others are still live. This is visually dramatic on the ops dashboard.

**Proxy bids create "ghost bidding."** When a proxy auto-bids, the Relay push looks identical to a manual bid from the audience's perspective — the bid count goes up, the price goes up. But the ops dashboard can distinguish proxy bids from manual ones (the `isProxyBid` flag on `BidPlaced`). This is a good ops dashboard demo moment: "see that bid? Nobody actually tapped their phone. That was a proxy bid firing automatically."

**The ops dashboard during hot phase:** This is the "engine running" view. Every `BidPlaced` updates the bid feed in real time. The presenter can narrate: "Look — 14 bids across three listings in the last 30 seconds. Two proxy bid managers are active. Listing A just hit reserve."

#### T+4:30 to T+5:30: Extended Bidding and Close (the climax)

```
Auctions BC (per listing)           Relay BC                       Ops Dashboard
─────────────────────────────────────────────────────────────────────────────────

Listing B closes first (no late bids):
  BiddingClosed (internal)
  ListingSold ─────────────────────► "Listing B sold to            "Listing B: SOLD
                                      SwiftFerret42 for $45!"       Winner: SwiftFerret42
                                                                    Hammer: $45"

Listing A — bid at T+4:55 (within trigger window):
  ExtendedBiddingTriggered ────────► "Extended! New close: 5:25"   "Listing A: EXTENDED
                                                                    New close: T+5:25"
  ... more bids possible ...
  BiddingClosed (internal)
  ListingSold ─────────────────────► "Listing A sold to            "Listing A: SOLD ..."
                                      BoldPenguin7 for $22!"

Listing D — no bids placed:
  BiddingClosed (internal)
  ListingPassed ───────────────────► "Listing D passed             "Listing D: PASSED
                                      (reserve not met)"            No winner"
```

**Extended bidding desynchronizes the close.** Even though all listings in a Flash Session start simultaneously and have the same configured duration, extended bidding means they may not close at the same time. A listing with no late bids closes on schedule. A listing with a snipe attempt extends. This creates a staggered close sequence that's more dramatic than a simultaneous slam.

**The `BiddingClosed` → `ListingSold`/`ListingPassed` gap.** `BiddingClosed` is internal — the participant never sees it. From their perspective, the listing goes from "open, accepting bids" directly to "Sold to SwiftFerret42!" or "Passed — reserve not met." The gap is the Auction Closing saga evaluating the result. In practice this is milliseconds — the saga checks reserve status and publishes the outcome event immediately. But it's architecturally important that these are two distinct events because the saga could, in theory, need to do more work (e.g., a future "pending verification" state).

**`ListingPassed` is not a failure — it's a legitimate business outcome.** On eBay, lots pass all the time. The reserve wasn't met, or nobody bid. The audience sees this on the ops dashboard and it's a teaching moment: "This listing passed because the reserve was $50 and the highest bid was $40. The reserve is confidential — the bidders never knew how close they were."

**The climax moment for the demo:** Listings closing in rapid succession. "Sold!" notifications popping on everyone's phones. The ops dashboard showing winner declarations streaming in. If a listing extends due to a snipe, the audience can see it happen — the close time pushes out, last-second bidding resumes, and the eventual close is even more satisfying. This is the part the presenter is building toward.

#### T+5min to T+8min: Settlement Cascade (concurrent, per-listing)

```
Settlement BC (per listing)         Relay BC                       Ops Dashboard
─────────────────────────────────────────────────────────────────────────────────

Listing B settlement:
  SettlementInitiated (internal)                                   "Listing B: settling..."
  ReserveCheckCompleted (internal)
  WinnerCharged (internal)                                         "Winner charged: $45"
  FinalValueFeeCalculated (internal)                               "Fee: $4.50 (10%)"
  SellerPayoutIssued ──────────────► "Payout: $40.50"             "Seller payout: $40.50"
  SettlementCompleted ─────────────────────────────────────────────"Listing B: SETTLED"
      │
      └──► Obligations BC starts

Listing A settlement (slightly later):
  SettlementInitiated (internal)                                   "Listing A: settling..."
  ... same flow ...
  SettlementCompleted                                              "Listing A: SETTLED"
      │
      └──► Obligations BC starts
```

**Settlement runs per-listing, concurrently.** Each `ListingSold` triggers its own independent settlement saga. There's no ordering dependency between them. In the demo, this means the ops dashboard shows settlement progress ticking through for multiple listings simultaneously.

**Settlement is fast.** No real payment processor, no external API calls. Credit ceiling debit and fee calculation are in-memory operations against the event store. The entire settlement saga completes in under a second per listing. From the audience's perspective, "sold" and "settled" happen almost back to back.

**The ops dashboard tells the financial story:** "Listing B sold for $45. Platform fee: $4.50. Seller payout: $40.50. Settlement complete." This is the moment where the audience sees the business model in action — the platform takes a cut, the seller gets paid, and it all happened automatically because `ListingSold` triggered a saga.

**`BuyItNowPurchased` settlements are already done by now.** If Listing C was bought via Buy It Now during the hot phase (T+0:45), its settlement completed minutes ago. The ops dashboard already shows it as settled while other listings are still closing. Another visual teaching point.

#### T+8min onward: Obligations (extends beyond the demo window)

```
Obligations BC (per listing)        Relay BC                       Ops Dashboard
─────────────────────────────────────────────────────────────────────────────────

PostSaleCoordinationStarted                                        "Post-sale: started"
  │
  ├── ShippingReminderSent (scheduled: T+24hr)                     "Reminder queued"
  │
  ... time passes (hours/days) ...
  │
  ├── TrackingInfoProvided ────────► "Tracking: 1Z999AA10..."     "Tracking provided"
  │   (cancels pending reminders)
  │
  ├── DeliveryConfirmed                                            "Delivered"
  │
  └── ObligationFulfilled ────────► "Transaction complete!"        "FULFILLED"
```

**Obligations extends well beyond the demo window.** In a real deployment, shipping reminders fire over days. In a Flash Session demo, the presenter has two options:

1. **Show the saga starting and the first scheduled message queuing.** The ops dashboard shows "Post-sale coordination started" and "Shipping reminder scheduled for 24 hours from now." The presenter explains the timeout chain conceptually. This is honest and educational.

2. **Compress the timeline for demo purposes.** Configure the Obligations saga with demo-mode timeouts (e.g., 30-second reminders instead of 24-hour). The audience watches the full lifecycle play out in real time. More dramatic, but requires a demo-mode configuration flag.

> **Decision needed (`@ProductOwner`):** Does the MVP Obligations saga need a demo-mode timeout configuration? Or is showing the saga start and explaining the rest sufficient for the first demo? This affects scope.

### Dependency Chain (the critical path)

The full happy-path dependency chain for a single listing:

```
SellerRegistrationCompleted
  └─► DraftListingCreated → ... → ListingPublished
       └─► SessionCreated → ListingAttachedToSession → SessionStarted
            └─► BiddingOpened
                 └─► BidPlaced (×N) → ReserveMet
                      └─► BiddingClosed → ListingSold
                           └─► SettlementInitiated → ... → SettlementCompleted
                                └─► PostSaleCoordinationStarted → ... → ObligationFulfilled
```

Every arrow is a hard "must happen before" dependency. Breaking this chain at any point stops the flow.

**Cross-cutting dependencies (not per-listing):**

- `ParticipantSessionStarted` must happen before any `PlaceBid` command is accepted for that participant
- `SessionCreated` must happen before any `ListingAttachedToSession`
- `SessionStarted` must happen before any `BiddingOpened` for attached listings

### Concurrency Map

What's running in parallel at the peak of the demo:

```
T+3min (mid-bidding, 4 listings open):

  Auctions BC:
    ├── Listing A: accepting bids, 1 proxy bid manager active, close timer at T+5:00
    ├── Listing B: accepting bids, 2 proxy bid managers active, close timer at T+5:00
    ├── Listing C: SOLD (Buy It Now at T+0:45), no longer active
    └── Listing D: accepting bids, no proxy bids, close timer at T+5:00

  Settlement BC:
    └── Listing C settlement saga: in progress (or already completed)

  Relay BC:
    ├── BiddingHub: pushing BidPlaced to ~40 connected participants
    └── OperationsHub: pushing all events to ops dashboard

  Operations BC:
    └── Cross-BC projections updating from all incoming integration events

  Listings BC:
    └── Catalog projections updating: C is "sold", A/B/D are "open"
```

At peak, the system is handling concurrent bid processing across multiple listings, a settlement saga, real-time SignalR delivery to 40+ clients, and projection updates — all simultaneously. This is what makes it a good architecture demo.

---

## Phase 2 Summary

**No vocabulary changes.** All events were already identified in Phase 1.

**Timing insights:**

| Phase | Wall-clock time | Tempo | What's happening |
|---|---|---|---|
| Prep | Hours/days | Relaxed | Listing creation, session setup |
| Arrival | 2-5 minutes | Moderate | Concurrent onboarding, catalog browsing |
| Start | Milliseconds | Instant | `SessionStarted` cascade, all listings open |
| Hot phase | 5-10 minutes | Intense | Concurrent bidding across N listings |
| Close | 30-90 seconds | Climactic | Staggered closes, winner declarations |
| Settlement | Seconds | Fast | Per-listing saga, concurrent |
| Obligations | Days (or 30s in demo mode) | Slow | Post-sale coordination |

**New parked questions:**

| # | Question | Source Persona | Target Workshop |
|---|----------|---------------|-----------------|
| 6 | Does MVP need a demo-mode timeout config for Obligations? | `@ProductOwner` | Obligations BC / Milestone scoping |
| 7 | What does the participant see during the `BiddingClosed` → `ListingSold` gap? | `@UX` / `@FrontendDeveloper` | Frontend / Auctions BC |
| 8 | Can a proxy bid trigger extended bidding? (proxy fires in trigger window) | `@QA` | Auctions BC |

**Key storytelling takeaway:** The Flash Session demo has a natural dramatic arc. The prep is calm. The arrival builds anticipation. The start is explosive. The hot phase is chaotic and concurrent. The close is a staggered climax. Settlement is the satisfying denouement. This arc is not designed — it emerges from the domain mechanics. That's what makes it a compelling demo.

---

## Phase 3 — Storyboarding

*Next: Add UI wireframes (what does the participant see?) and views (what read models power those screens?) to the timeline. Connect commands to screens, events to updated views.*

*(to be continued)*

---

## Phase 4 — Identify Slices

*(not yet started)*

---

## Phase 5 — Scenarios (Given/When/Then)

*(not yet started)*
