# Workshop 001 — Flash Session Demo-Day Journey

**Type:** User Journey (cross-cutting)
**Date started:** 2026-04-09
**Status:** In progress — Phase 2 next

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

*Next: Sequence the verified events into a chronological timeline. Surface ordering dependencies, timing questions, and handoff mechanics between BCs.*

*(to be continued)*

---

## Phase 3 — Storyboarding

*(not yet started)*

---

## Phase 4 — Identify Slices

*(not yet started)*

---

## Phase 5 — Scenarios (Given/When/Then)

*(not yet started)*
