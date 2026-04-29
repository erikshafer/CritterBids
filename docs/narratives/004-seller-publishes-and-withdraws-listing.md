---
slug: 004-seller-publishes-and-withdraws-listing
status: draft
journey: seller
perspective: single-seller
scope: happy-path
bounded_contexts: [Selling]
boundaries_touched: [Participants, Listings, Auctions]
slices_implemented: [0.3, 1.1, 1.2]
canonical_id: ListingId
---

# Seller Publishes and Withdraws Listing (Happy Path)

A Selling-grain narrative. GreyOwl12 — known offstage in narratives 001 and 002 as the seller of the Vintage Mechanical Keyboard that SwiftFerret42 wins for $55 — does the work that makes the keyboard available in the demo: he registers as a seller, drafts the keyboard listing with its starting bid and reserve and Buy It Now and extended-bidding settings, submits it, and watches the system auto-approve and publish. The keyboard then waits days for narrative 001's session to start. Along the way, GreyOwl12 also drafts and publishes a second listing — a Vintage Folding Camera — which he subsequently changes his mind about and withdraws before any session attaches. This narrative covers four lived M2 listing-pipeline beats (Moments 1-4) and one forward-spec M4-S2 WithdrawListing beat (Moment 5).

The audit floor splits by Moment. Moments 1-4 audit against shipped Selling BC code at `src/CritterBids.Selling/`; M2-S2 (BC scaffold), M2-S5 (slice 1.1 create-draft), M2-S6 (slice 1.2 submit), and M2.5-S2 (update-draft) retros are the design-time references. Moment 5 audits against the M4-S2 implementation prompt at `docs/prompts/M4-S2-selling-withdraw-listing.md`; the WithdrawListing code has not shipped, so the lived-code lane defers under `defer` until M4-S2 runs. This is the same mixed posture narrative 001 used for its three forward-spec Moments embedded in a lived journey, applied at finer grain.

Narrative 004 is the first seller-perspective narrative for CritterBids. The system surface GreyOwl12 sees — listing dashboard, draft state, submission outcomes — is structurally different from the bidder-side surfaces narratives 001-003 dramatised. The narrator's responsibility is to render the seller's view faithfully without slipping into bidder-side framing.

## Cast

- **GreyOwl12** — the seller, protagonist. A registered seller on CritterBids; in his off-conference life, an artisan or prosumer with items to list. Single protagonist; the narrative is told entirely from his vantage. He never sees the auction itself; that is bidder territory covered by narratives 001-003.
- **The Selling BC** — onstage across all five Moments. The state machine that drives the listing lifecycle (Draft → Submitted → Approved/Rejected → Published → Ended/Withdrawn).
- **The `SellerListing` aggregate** — onstage in Moments 2-5. The narrator names its state transitions, validation passes, and Apply methods.
- **The auto-approval handler** — onstage in Moments 3 and 4. The internal Selling-BC handler chain that consumes `ListingSubmitted` and emits `ListingApproved` followed by `ListingPublished`. M2-S6 retro records the choice for automated approval over manual review for MVP.
- **The `Participant` aggregate's seller-flag flip** — onstage in Moment 1. GreyOwl12's seller-registration flips the `IsRegisteredSeller` property of his Participant aggregate from false to true (the same aggregate the Participants BC owns and that narrative 003 dramatised the bidder-side of).
- **Listings BC** — offstage. Consumes `ListingPublished` to build the `CatalogListingView` that bidders read; GreyOwl12 perceives this only as "the listing is now visible to bidders" (when the listing-detail page renders).
- **Auctions BC** — offstage. Consumes `ListingPublished` to wire Timed-listing bidding (per M3) and Flash-listing session attachment (M4-S5/S6). Consumes `ListingWithdrawn` (M4-S2 forward-spec) to invalidate any pending bidding state. GreyOwl12 doesn't perceive Auctions directly.
- **Wolverine and Marten** — onstage as runtime primitives. The `[WolverinePost]` endpoints, the `MartenOps.StartStream` / `MartenOps.AppendEvent` patterns, and the integration-event publishing through the Wolverine outbox to RabbitMQ queues (`listings-selling-events`, `auctions-selling-events`) are nameable runtime facts.
- **The integration events `ListingPublished` and `ListingWithdrawn`** — onstage at Moment-3 and Moment-5 commit boundaries. The narrator names their cross-BC routing.
- **The Vintage Mechanical Keyboard listing** — onstage in Moments 2-3, then offstage. Goes to narrative 001's Flash session.
- **The Vintage Folding Camera listing** — onstage in Moments 4-5. Sibling ground to the keyboard; published but never attached; withdrawn by deliberate seller decision.
- **SwiftFerret42, BoldPenguin7, the auction operator, the bidders, the Settlement BC** — offstage. None appear in narrative 004.

## Setting

A weekday afternoon roughly five days before the Nebraska.Code() conference where SwiftFerret42 and BoldPenguin7 will scan their QR codes. GreyOwl12 is at his desk at home — perhaps in a workshop with shelves of items he has been meaning to list — with a laptop open to the CritterBids seller dashboard (a forward-spec UI surface; M6 frontend territory). The conference is on the calendar; the operator has been corresponding with sellers about which listings will appear in the Flash session demo; GreyOwl12 has signed up to participate. He has decided to list two items: a Vintage Mechanical Keyboard he bought in college and never used, and a Vintage Folding Camera that has been on his shelf for years.

The system's MVP infrastructure is healthy. The CritterBids API host is running; the Selling BC's endpoints are reachable; Wolverine is processing requests; Marten's event store on PostgreSQL is up and accepting new streams. The conference Wi-Fi will not be a factor for these Moments — GreyOwl12 is on a stable home connection. Auto-approval is the configured policy for MVP; manual review is `post-MVP`.

Auction-system policy is at MVP defaults. The `[Authorize]` global convention is overridden by `[AllowAnonymous]` posture per M1; even seller-side endpoints carry `[AllowAnonymous]` because real authentication and seller-account binding are deferred to M6. UUID v7 stream IDs apply per ADR 007 for both `Participant` (seller-registration target) and `SellerListing` (per-listing stream).

The keyboard's listing-time fields, inherited verbatim from narrative 001 Setting paragraph 2, are: title "Vintage Mechanical Keyboard", format Flash, starting bid $25.00, reserve $50.00, BIN $100.00, extended bidding enabled with 30-second trigger window and 15-second extension, FeePercentage 10.0, duration determined by session attachment. The camera's listing-time fields, established here as canonical for this narrative onward, are: title "Vintage Folding Camera", format Timed, starting bid $40.00, no reserve (null), BIN $80.00, extended bidding disabled, FeePercentage 10.0, duration 7 days from publication. The reserve = null path on the camera exercises a different `SellerListing` aggregate branch than the keyboard's reserve-set path; the Timed format on the camera (vs the keyboard's Flash) means the camera would, if not withdrawn, have its bidding open immediately on `ListingPublished` consumption per M3 lived behavior rather than wait for a Flash session start. The camera's withdrawal in Moment 5 closes its journey before either the auction-side or the bidder-side ever perceive it as bid-able.

The cleanest possible run on both listings, modulo the deliberate camera-withdrawal: no draft-validation rejections, no submission rejections, no post-publication revisions, no `ListingWithdrawn` failures. GreyOwl12's two listings move through the Selling state machine cleanly until the camera's deliberate exit at Moment 5.
