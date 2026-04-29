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

The audit floor splits by Moment. Moments 1-4 audit against shipped Selling BC code at `src/CritterBids.Selling/`; M2-S2 (BC scaffold), M2-S5 (slice 1.1 create-draft), M2-S6 (slice 1.2 submit), and M2.5-S2 (update-draft) retros are the design-time references. Moment 5 audits against the M4-S2 implementation prompt at `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md`; the WithdrawListing code has not shipped, so the lived-code lane defers under `defer` until M4-S2 runs. This is the same mixed posture narrative 001 used for its three forward-spec Moments embedded in a lived journey, applied at finer grain.

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

## Moment 1: GreyOwl12 registers as a seller

**Implements:** slice 0.3.

**Context.** GreyOwl12's CritterBids session was minted minutes earlier when he first navigated to the seller dashboard — the same `StartParticipantSession` flow that narrative 003 dramatised for BoldPenguin7, just initiated through a different UI surface. He has a `ParticipantId`, a system-derived display name (unanchored here; it does not appear in the seller-side beats narrative 004 dramatises), a `BidderId` he will never use, and a hidden credit ceiling irrelevant to his seller activity. His `Participant` aggregate state reads `(Id: ParticipantId, HasActiveSession: true, IsRegisteredSeller: false)`. He has clicked through to the seller-onboarding flow on the dashboard; the next interaction commits him as a seller.

**Interaction.** The seller dashboard POSTs `RegisterAsSeller { ParticipantId }` to `/api/participants/{id}/register-seller`. Wolverine routes the request to `RegisterAsSellerHandler.Handle`, with the `Participant` aggregate loaded for the `[WriteAggregate]` workflow.

**Response.** The handler's `Before()` guard runs first. The `HasActiveSession` check passes (he has one); the `IsRegisteredSeller` check passes (he is not yet registered, so the 409 idempotency guard does not trigger). Control passes to the body of `Handle`. The handler emits `SellerRegistered { ParticipantId: GreyOwl12, CompletedAt: <now> }` as a domain event appended to GreyOwl12's `Participant` stream, alongside an integration event `SellerRegistrationCompleted { ParticipantId, CompletedAt }` published through `OutgoingMessages` to the Wolverine transactional outbox.

The `Participant` aggregate's `Apply(SellerRegistered)` runs against the freshly committed event. It flips `IsRegisteredSeller` to true. GreyOwl12's aggregate state shape now reads `(Id: ParticipantId, HasActiveSession: true, IsRegisteredSeller: true)`. The HTTP response is 200 OK with no body — appending to an existing resource (his Participant stream), not creating a new one.

The Selling BC's `SellerRegistrationCompletedHandler` consumes the integration event from the outbox queue. It performs `session.Store(new RegisteredSeller { Id = ParticipantId })`, upserting a document keyed on the same `ParticipantId` into the Selling BC's document store. Idempotency is guaranteed by the upsert semantics: a duplicate consumption of `SellerRegistrationCompleted` would store the same document state.

The seller dashboard refreshes; the seller-registration step shows complete. GreyOwl12 can now draft listings.

**Why this matters to the seller.** GreyOwl12 has crossed the threshold from "anonymous participant who could only bid" to "registered seller who can both bid and list." His identity has been augmented: the same `ParticipantId` that anchored him in the system as an anonymous participant is now also the key to a `RegisteredSeller` document in the Selling BC's schema, the unique-by-construction reference under which any listing he creates will be persisted. The dual presence — `Participant` aggregate in Participants BC, `RegisteredSeller` document in Selling BC — is the cross-BC coordination that makes seller-side product features possible without violating BC isolation. From his window, this is a one-click action that confirms his right to list; from the system's window, it is a small saga across two BCs over the Wolverine outbox that establishes durable seller status.

### Things deliberately not included

- The seller-dashboard UI rendering: the layout of the registration form, the post-registration confirmation visual, navigation to the listing-draft flow. *(`UX-or-UI-detail`; M6 frontend MVP territory.)*
- The 400 / 409 rejection paths in the `Before()` guard: no-active-session, already-registered-seller. *(`alternate-path-failure`.)*

## Moment 2: GreyOwl12 drafts the keyboard listing

**Implements:** slice 1.1.

**Context.** GreyOwl12 has just registered as a seller in Moment 1. His `Participant` aggregate carries `IsRegisteredSeller: true`; the Selling BC's `RegisteredSeller` document for his `ParticipantId` has been upserted; the seller dashboard now shows the "Create new listing" affordance enabled. He has navigated to the listing-draft form and filled in the keyboard's details: title, format, starting bid, reserve, BIN, duration field (which for Flash format remains null), and the extended-bidding settings.

**Interaction.** The seller dashboard POSTs `CreateDraftListing { SellerId: ParticipantId, Title: "Vintage Mechanical Keyboard", Format: Flash, StartingBid: $25.00, ReservePrice: $50.00, BuyItNowPrice: $100.00, Duration: null, ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: 30 seconds, ExtendedBiddingExtension: 15 seconds }` to `/api/listings/draft`. Wolverine routes the request to `CreateDraftListingHandler`, which is a compound handler with a `ValidateAsync` pre-check.

**Response.** `ValidateAsync` queries `ISellerRegistrationService.IsRegisteredAsync(SellerId)`; the service consults the `RegisteredSellers` projection and returns true (GreyOwl12's Selling-side document was upserted in Moment 1). Validation passes; control passes to `Handle`.

`Handle` generates a fresh UUID v7 — the keyboard's `ListingId`. It constructs `DraftListingCreated { ListingId, SellerId: GreyOwl12, Title: "Vintage Mechanical Keyboard", Format: Flash, StartingBid: $25.00, ReservePrice: $50.00, BuyItNowPrice: $100.00, Duration: null, ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: 30 seconds, ExtendedBiddingExtension: 15 seconds, CreatedAt: <now> }` and opens a new stream via `MartenOps.StartStream<SellerListing>(ListingId, evt)`. The handler returns a tuple of `(CreationResponse<Guid>(Location: "/api/listings/{ListingId}", listingId), startStreamOp)`. Marten commits the stream when the handler returns.

The `SellerListing` aggregate's `Apply(DraftListingCreated)` runs against the freshly committed event. It sets all eleven listing-time fields plus `Status` to `Draft`. The aggregate's state shape now reads `(Id: ListingId, SellerId: GreyOwl12, Title, Format, StartingBid, ReservePrice, BuyItNowPrice, Duration: null, ExtendedBiddingEnabled, ExtendedBiddingTriggerWindow, ExtendedBiddingExtension, Status: Draft, PublishedAt: null)`. The HTTP response is 201 Created with the Location header pointing to the new listing's URI.

The seller dashboard refreshes and shows the keyboard listing in his draft list with status "Draft." It is private to him — no bidder can see it; no Listings BC `CatalogListingView` exists for it yet; no Auctions BC stream has opened. The keyboard exists only in the Selling BC's event store at this point.

**Why this matters to the seller.** GreyOwl12 has committed the keyboard's listing-time fields to durable system state under his `SellerId`. The fields are private until publication and editable until submission (per W004 §1's update-draft scenarios). The reserve price he set ($50) is confidential ground that will travel through the system as the binding threshold for the auction's reserve check; he, the system, and eventually Settlement will know it, but no bidder will see it until it crosses. The Flash format choice means the duration field is null (Flash listings inherit duration from session attachment, not from listing-time configuration). All eleven fields are now state on the keyboard's stream, ready for the next state transition when he submits.

### Things deliberately not included

- The seller-dashboard form-rendering UI: input layout, validation feedback, format-and-duration interaction. *(`UX-or-UI-detail`; M6 frontend MVP territory.)*
- Rejection paths: 403 from `ValidateAsync` on unregistered seller (with Wolverine retry-after-projection-catches-up per M2-S5 retro race-condition note); validation failures from `ListingValidator` (whitespace title, BIN below reserve, format-incompatible duration) per W004 §5. *(`alternate-path-failure`.)*
- Listing-format selection rationale: Flash vs Timed, why duration is null for Flash, why duration is required for Timed. *(`document-as-intentional`; W004 covers the format-and-duration interaction; the narrative renders the keyboard's specific fields without restating the format-choice rationale.)*

## Moment 3: The keyboard is published

**Implements:** slice 1.2.

**Context.** GreyOwl12's keyboard listing is in `Draft` state from Moment 2. The aggregate carries all eleven listing-time fields plus `Status: Draft, PublishedAt: null`. He has reviewed the form on his seller dashboard, decided not to make further edits (no `UpdateDraftListing` cycles), and is ready to submit. The Submit button on the dashboard is enabled.

**Interaction.** GreyOwl12 clicks Submit. The seller dashboard sends a `SubmitListing { ListingId: keyboard, SellerId: GreyOwl12 }` command — at the API surface today, this is a Wolverine-internal aggregate-handler invocation (no HTTP endpoint exists in M2 per the inline comment in `SubmitListing.cs`), but the seller-dashboard UI assumption is that a forward-spec endpoint wraps it. The handler is loaded with the keyboard's `SellerListing` aggregate via `[WriteAggregate(nameof(SubmitListing.ListingId))]`.

**Response.** `SubmitListingHandler.Handle` runs the state guard first: only `Draft` and `Rejected` listings can be submitted; the keyboard is `Draft`, so the guard passes. The handler emits `ListingSubmitted { ListingId, SellerId, At: <now> }` as the first event in the chain. The aggregate's `Apply(ListingSubmitted)` flips `Status` to `Submitted`.

`ListingValidator.Validate(listing)` runs against the aggregate's eleven listing-time fields. Validation checks (per W004 §5) cover title-not-whitespace, BIN-not-below-reserve, format-and-duration coherence, extended-bidding-window-validity, and several others. The keyboard's fields all pass: title is a non-whitespace string, BIN ($100) exceeds reserve ($50), Flash format with null duration is coherent, extended-bidding window (30s trigger, 15s extension) is valid. Validation returns `IsRejection: false`.

The handler emits two more events in the same transaction: `ListingApproved { ListingId, At: <now> }` and `ListingPublished { ListingId, PublishedAt: <now> }`. The aggregate's `Apply(ListingApproved)` flips `Status` to `Published` (the lifecycle skips an `Approved` intermediate state by design — the `ListingStatus` enum has no `Approved` value; auto-approval-and-publication is a single state transition compressed across two events). The aggregate's `Apply(ListingPublished)` flips `Status` to `Published` (idempotent) and sets `PublishedAt: <now>`. The aggregate's final state is `(Id: ListingId, ..., Status: Published, PublishedAt: <now>)`.

The handler also adds an outgoing integration event to the Wolverine transactional outbox: `CritterBids.Contracts.Selling.ListingPublished { ListingId, SellerId: GreyOwl12, Title: "Vintage Mechanical Keyboard", Format: "Flash", StartingBid: $25.00, ReservePrice: $50.00, BuyItNow: $100.00, Duration: null, ExtendedBiddingEnabled: true, ExtendedBiddingTriggerWindow: 30 seconds, ExtendedBiddingExtension: 15 seconds, FeePercentage: 0.10, PublishedAt: <now> }`. The FeePercentage is hardcoded at the handler today as an M5 placeholder (no fee engine exists yet); when M5's Settlement BC ships and the fee-engine moves into a configurable boundary, this constant becomes a lookup. The integration event lands on the `listings-selling-events` and `auctions-selling-events` queues for Listings and Auctions BC consumption.

The Listings BC consumes the integration event and projects the keyboard into a `CatalogListingView` row visible to bidders. The Auctions BC also consumes it; for Flash format, the Auctions-side reaction is forward-spec (M4-S5 / M4-S6 territory — the Flash session aggregate hasn't shipped). The seller dashboard refreshes; the keyboard moves from his draft list to his published list with `Status: Published`.

**Why this matters to the seller.** GreyOwl12 has just transferred the keyboard from his private seller-side state to the public catalog. Three things change in this Moment that did not change in Moment 2: the listing is now visible to bidders (`CatalogListingView` is built), it is now reachable by the auction operator for session attachment (the Auctions BC has the integration event), and the listing's reserve, fee, and other binding fields are now committed to a downstream PendingSettlement projection that narrative 002 dramatised at finer grain. The auto-approval pattern means he experienced no review delay — submit and publish happened in a single atomic-millisecond. From his window, the journey from "I have something to sell" to "the system says it is for sale" closed the moment he clicked Submit. The keyboard waits days for the Flash session to start and SwiftFerret42 to scan in.

### Things deliberately not included

- The `ListingValidator` rejection branch (whitespace title, BIN below reserve, format-and-duration mismatch, etc.). The keyboard passes validation cleanly; the rejected branch produces `ListingSubmitted + ListingRejected` events and no integration event, then leaves the aggregate in `Rejected` state pending another `SubmitListing`. *(`alternate-path-failure`.)*
- The state guard's invalid-transition path: submitting from `Submitted`, `Published`, or `Withdrawn` states. *(`alternate-path-failure`.)*
- Cross-BC consumer reactions in detail: Listings BC's `CatalogListingView` projection-handler logic; Auctions BC's format-specific bidding wiring (Flash forward-spec for M4-S5/S6; Timed wires bidding immediately on publish per M3 lived behavior). *(`separate-narrative`; bidder-side coverage in narratives 001 and the future narrative 005.)*

## Moment 4: GreyOwl12 publishes the camera listing

**Implements:** slice 1.1, slice 1.2.

**Context.** GreyOwl12's keyboard is published from Moment 3 (`Status: Published`, `PublishedAt` set). His seller dashboard shows it in the published list, awaiting eventual session attachment for the conference. He has a second item to list: the Vintage Folding Camera. He returns to the dashboard and starts a new listing draft. The mechanics he experiences are structurally identical to Moments 2-3, but the listing-time fields are different and the journey ends differently in Moment 5.

**Interaction.** GreyOwl12 fills in the camera's listing-time fields and submits. The seller dashboard sends two commands in sequence: `CreateDraftListing { SellerId: GreyOwl12, Title: "Vintage Folding Camera", Format: Timed, StartingBid: $40.00, ReservePrice: null, BuyItNowPrice: $80.00, Duration: 7 days, ExtendedBiddingEnabled: false, ExtendedBiddingTriggerWindow: null, ExtendedBiddingExtension: null }` followed by `SubmitListing { ListingId: camera, SellerId: GreyOwl12 }`. Both commands route through the same handler chains as Moments 2 and 3.

**Response.** The first command runs `CreateDraftListingHandler.ValidateAsync` (the seller is registered; passes), then `Handle` generates a fresh UUID v7 ListingId for the camera, builds `DraftListingCreated` with the camera's eleven listing-time fields, and `MartenOps.StartStream<SellerListing>(cameraListingId, evt)` opens the camera's stream. The aggregate's `Apply(DraftListingCreated)` populates state: `(Id: cameraListingId, SellerId: GreyOwl12, Title: "Vintage Folding Camera", Format: Timed, StartingBid: $40.00, ReservePrice: null, BuyItNowPrice: $80.00, Duration: 7 days, ExtendedBiddingEnabled: false, ExtendedBiddingTriggerWindow: null, ExtendedBiddingExtension: null, Status: Draft, PublishedAt: null)`. HTTP 201 with the camera's URI returns.

The second command runs through `SubmitListingHandler` against the camera's loaded aggregate. State guard passes (`Draft`); `ListingSubmitted` emits and Status flips to `Submitted`; `ListingValidator.Validate` runs against the camera's fields. The reserve = null path is the relevant validator distinction: the BIN-not-below-reserve check is vacuously satisfied when reserve is null, the format-and-duration check requires a non-null Duration for Timed format ($40 starting, 7 days set, valid), the title is non-whitespace. Validation passes. `ListingApproved` and `ListingPublished` emit; the aggregate's Status flips to `Published` and `PublishedAt` is set. The outgoing `Contracts.Selling.ListingPublished` lands on the outbox: `{ ListingId: camera, SellerId: GreyOwl12, Title: "Vintage Folding Camera", Format: "Timed", StartingBid: $40.00, ReservePrice: null, BuyItNow: $80.00, Duration: 7 days, ExtendedBiddingEnabled: false, ExtendedBiddingTriggerWindow: null, ExtendedBiddingExtension: null, FeePercentage: 0.10, PublishedAt: <now> }`.

The Listings BC consumes the camera's integration event and builds its `CatalogListingView` row. The Auctions BC also consumes it; for Timed format, the Auctions-side reaction is lived per M3 — bidding opens immediately on the camera's listing, and any bidder who finds the camera in the catalog could place a bid as soon as the projection catches up. The camera is now bid-able as a Timed listing, no session attachment required. The seller dashboard refreshes; the camera appears in GreyOwl12's published list alongside the keyboard.

**Why this matters to the seller.** GreyOwl12 now has two listings in the system with different runtime postures. The keyboard is Flash format and waits for session attachment to become bid-able; the camera is Timed format and is bid-able the moment its `CatalogListingView` projection catches up. Two different runtime futures, the same Selling-BC publication mechanics. The reserve = null path on the camera means there is no confidential threshold — any winning bid above the starting $40 will result in a sale at hammer price; the BIN at $80 is a ceiling that will sell the camera immediately if any bidder hits it. He has the option to leave both listings to play out, or to intervene on the camera before any bidder sees it.

### Things deliberately not included

- Update-draft cycles between create and submit: W004 §1.3-1.5 covers the `UpdateDraftListing` immutable-field-guard scenarios; the happy-path narrative does no iteration. *(`separate-narrative`; for any listing-iteration-focused future narrative.)*
- Timed-format auction wiring on `ListingPublished` consumption (the Auctions BC's lived M3 behavior of opening bidding immediately). *(`separate-narrative`; narrative 005 territory.)*
- BIN ceiling behavior on Timed listings: what happens if a bidder hits the $80 BIN before withdrawal. *(`separate-narrative`; bidder-perspective via the `BuyItNowPurchased` flow, narrative 005 territory.)*

## Moment 5: GreyOwl12 withdraws the camera

**Implements:** M4-S2 (forward-spec; no W001 slice number assigned; W004 §4 "End Early and Relist" is the workshop framing).

**Context.** GreyOwl12's camera has been published since Moment 4 (`Status: Published`, `PublishedAt` set). The Listings BC's `CatalogListingView` for the camera has caught up; it is theoretically bid-able as a Timed listing, though no bidder has yet seen it (it has been minutes since publication; the conference is still days away). GreyOwl12 has reconsidered: he was going to sell the camera but a friend reminded him it has sentimental value, or a buyer reached out privately, or he wants to wait until a future conference where camera enthusiasts are more numerous. Whatever the reason, he no longer wants the system to sell the camera. He clicks the Withdraw button on the camera's seller-dashboard entry.

**Interaction.** The seller dashboard sends a `WithdrawListing { ListingId: camera, WithdrawnBy: GreyOwl12 }` command. **Forward-spec note:** the M4-S2 implementation prompt at `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md` specifies this command and its handler; the lived code does not yet exist (M4-S2 has not run). The narrator renders the journey as the M4-S2 prompt designs it.

**Response.** `WithdrawListingHandler.Handle` runs the state guard: only `Published` listings can be withdrawn; the camera is `Published`, so the guard passes. The handler emits a Selling-internal domain event `ListingWithdrawn { ListingId: camera, WithdrawnBy: GreyOwl12, WithdrawnAt: <now> }` (the Selling-internal type is distinct from the integration contract; per the M4-S2 design they are separate CLR types). The aggregate's `Apply(ListingWithdrawn)` flips `Status` to `Withdrawn`. The aggregate's final state is `(Id: cameraListingId, ..., Status: Withdrawn, PublishedAt: <prior>)`.

The handler also adds an outgoing integration event to the Wolverine transactional outbox: `CritterBids.Contracts.Selling.ListingWithdrawn { ListingId: camera, WithdrawnBy: GreyOwl12, Reason: null, WithdrawnAt: <now> }`. The `Reason` field is null because the MVP seller-initiated withdrawal command carries no reason capture (per the contract's field-rationale documentation; future ops-staff withdrawal and fraud/abuse paths populate it). The integration event lands on two RabbitMQ queues: `auctions-selling-events` (consumed by Auctions BC) and `listings-selling-events` (consumed by Listings BC).

The Auctions BC consumes the integration event. Its Auction Closing saga (lived in M3 for Timed listings) transitions to `Resolved` without emitting any outcome event — no `BiddingClosed`, no `ListingSold`, no `ListingPassed`. Any pending `CloseAuction` scheduled for the camera's 7-day duration is cancelled. The withdrawal skips reserve evaluation entirely because the camera had no reserve to evaluate; even if it had one, the M4-S2 design says withdrawal terminates without reserve-check by design (no money moves). The Listings BC consumes the integration event and updates the camera's `CatalogListingView.Status` to indicate withdrawal (the exact field shape is M4-D5 territory, decided in M4-S6).

The seller dashboard refreshes; the camera moves from GreyOwl12's published list to a "withdrawn" section with `Status: Withdrawn`. He cannot re-submit it (the state guard rejects `SubmitListing` from `Withdrawn` state); if he wants to sell the camera in the future, he must create a new listing draft. The keyboard listing remains untouched — it is still in `Status: Published` waiting for the conference's Flash session to attach it.

**Why this matters to the seller.** GreyOwl12 has exercised seller authority over his own published listing without involving any bidder. The withdrawal terminates the camera's auction journey before it starts: no bid can ever land on it now, no settlement will ever run for it, no fee will ever be calculated. The keyboard's separate journey is unaffected — the keyboard will proceed to narrative 001's session start, narrative 002's settlement, and SwiftFerret42 will pay him the post-fee $49.50 payout for the keyboard's hammer. Two listings, two outcomes: one sells, one is withdrawn. The Selling BC's state machine has carried both cleanly from his single seller registration in Moment 1.

### Things deliberately not included

- The lived-code audit of the `WithdrawListing` handler. The M4-S2 prompt is the spec; the implementation has not shipped. *(`defer`; trigger is M4-S2 ship.)*
- Cross-BC consumer reactions in detail: the Auctions Closing saga's `Resolved`-without-outcome-event branch on `ListingWithdrawn` consumption (lived M3 territory at finer grain), and the Relay BC's "listing withdrawn" notification push to bidders and watchers (per the contract's documentation; a no-op here since the camera has no bidders or watchers). *(`separate-narrative`; narrative 005 + future Relay-perspective territory.)*

## Deferred from this narrative

The following were deliberately not narrated in this Selling-perspective happy-path narrative. Each is named with its disposition so future sessions can pull from this list when scoping the next narrative, ADR, skill file, or implementation prompt. Items here are not bugs or omissions; they are consciously deferred and traceable. Items recorded in `004-findings.md` (F001 hardcoded FeePercentage, F002 missing SubmitListing HTTP endpoint, F003 missing Approved intermediate state) are not duplicated here.

### `defer` (revisit when trigger lands)

- Lived-code audit of the `WithdrawListing` handler (Moment 5; trigger: M4-S2 ship).

### `separate-narrative` (other journey perspectives)

- Cross-BC consumer reactions on `ListingPublished`: Listings BC's `CatalogListingView` projection-handler logic; Auctions BC's format-specific bidding wiring (Flash forward-spec for M4-S5/S6; Timed wires bidding immediately on publish per M3 lived behavior) (Moment 3).
- Update-draft cycles between create and submit: W004 §1.3-1.5 covers the `UpdateDraftListing` immutable-field-guard scenarios; the happy-path narrative does no iteration (Moment 4).
- Timed-format auction wiring on `ListingPublished` consumption (Moment 4; narrative 005 Auctions territory).
- BIN ceiling behavior on Timed listings: what happens if a bidder hits the $80 BIN before withdrawal (Moment 4; bidder-perspective via the `BuyItNowPurchased` flow, narrative 005 territory).
- Cross-BC consumer reactions on `ListingWithdrawn`: Auctions Closing saga's `Resolved`-without-outcome-event branch (lived M3 at finer grain) and Relay BC's "listing withdrawn" notification push (per the contract's documentation; a no-op here since the camera has no bidders or watchers) (Moment 5).

### `UX-or-UI-detail` (app design)

- Seller-dashboard UI rendering across all five Moments: registration form layout, post-registration confirmation, listing-draft form input layout, validation feedback, format-and-duration interaction, the camera's published-list and withdrawn-section rendering. M6 frontend MVP territory.

### `document-as-intentional` (settled design choices)

- Listing-format selection rationale: Flash vs Timed, why duration is null for Flash, why duration is required for Timed (Moment 2; W004 covers the format-and-duration interaction).

### `alternate-path-failure` (failure modes warranting their own narratives)

- The 400 / 409 rejection paths in `RegisterAsSeller`'s `Before()` guard: no-active-session, already-registered-seller (Moment 1).
- Rejection paths from `CreateDraftListing`: 403 from `ValidateAsync` on unregistered seller (with Wolverine retry-after-projection-catches-up per M2-S5 retro race-condition note); validation failures from `ListingValidator` (whitespace title, BIN below reserve, format-incompatible duration) per W004 §5 (Moment 2).
- The `ListingValidator` rejection branch on submission: `ListingSubmitted + ListingRejected` events with no integration event, leaving the aggregate in `Rejected` state pending another `SubmitListing` (Moment 3).
- The state guard's invalid-transition path on submit: submitting from `Submitted`, `Published`, or `Withdrawn` states (Moment 3).

## Retrospective

### Narrative intent vs. outcome

Stated goal at session start: author the Selling BC's backfill narrative covering GreyOwl12's seller-side journey across registration, two listings' publication pipelines, and the camera's withdrawal. Audit W004, lived `src/CritterBids.Selling/` code, and the M4-S2 implementation prompt. Route disagreements through the four-lane findings discipline. Add per-row narrative back-references on W001 and a new Narrative Cross-References section on W004. Establish GreyOwl12's anchored cross-narrative values (the Vintage Folding Camera's listing-time fields).

**Outcome.** Five Moments covering W001 slices 0.3, 1.1, 1.2 plus the M4-S2 forward-spec slice. Mixed posture: lived M2 listing pipeline (Moments 1-4) plus forward-spec M4-S2 WithdrawListing (Moment 5). Three findings filed in `004-findings.md`: F001 hardcoded FeePercentage 0.10m placeholder (`document-as-intentional`), F002 missing SubmitListing HTTP endpoint (`code-update`, stub follow-up at `docs/prompts/implementations/n004-fu-submit-listing-endpoint.md`), F003 missing `Approved` intermediate state in the `ListingStatus` enum (`document-as-intentional`). The M4-S2 path-citation correction landed in the Moment 5 commit (small in-PR fix). The Vintage Folding Camera's listing-time fields anchored as canonical from this narrative onward. W004 carries zero Polecat / SQL Server staleness against ADR 011 (in contrast to W003 which surfaced narrative 002's F003); no F004 needed. Cast and Setting locked first; Moment-by-Moment sign-off cadence held throughout. Goal met.

### What worked

- **Mixed lived/forward-spec posture worked at finer grain than narrative 001's pattern.** Narrative 001 had three forward-spec Moments scattered through an eight-Moment lived journey; narrative 002 was fully forward-spec; narrative 004 has one forward-spec Moment closing a four-lived-Moment journey. The same Moment-by-Moment discipline carried each posture cleanly without the working pattern needing adaptation.
- **Multi-phase compression in Moment 4 (CreateDraftListing + SubmitListing for the camera).** The README's multi-slice convention extended naturally to multi-command compression for journey-grain reasons (the camera's pipeline is structurally identical to Moments 2-3; expansion would add no journey value).
- **Pre-Moment surrounding-directory reads caught F002 and the M4-S2 path correction.** Narrative 003 retro established this pattern; narrative 004 confirmed its value. Reading just `SubmitListing.cs` would have surfaced the inline comment about no HTTP endpoint, but searching the directory for any `[WolverinePost]`/`[WolverineGet]` registrations confirmed the gap structurally.
- **Three findings surfaced naturally during code reads.** F001-F003 emerged during Moment 3's read of `SubmitListingHandler`, `SellerListing.cs`, and `ListingStatus.cs`; not forced or shoehorned. Routing-by-finding-rather-than-routing-by-area held: F001 and F003 both surfaced from the same handler-and-aggregate read but routed differently (`document-as-intentional` for the placeholder comment vs the deliberate state-machine compression).
- **Sibling-listing pattern for WithdrawListing.** The Vintage Folding Camera as a sibling listing to the keyboard let the narrative dramatise withdrawal without contradicting narrative 001's keyboard ground. Reusable: any future narrative needing a counterfactual outcome on a journey whose primary listing already has a fixed terminal outcome can introduce a sibling.
- **Em-dash hygiene drop continued battle-tested.** No em-dash audit; em-dashes used naturally throughout commit messages and prose; no slips.

### What was hard

- **The keyboard cannot dramatise WithdrawListing without contradicting narrative 001.** This was anticipated at session start (the prompt's open-question section flagged the second-listing requirement) but worth recording because narrative 005 (Auctions) may face analogous constraints — narrative 001 establishes the keyboard's terminal outcome (sold to SwiftFerret42 at $55), so any narrative 005 Moment that needs a counterfactual auction outcome (passed listing, BIN purchase, payment failure) will need its own sibling listing.
- **F001-F003 routing was non-obvious in places.** F003 (missing `Approved` state) could have routed `code-update` (the state machine is incomplete) but the inline `ListingStatus.cs` comment explicitly notes the design choice; that made `document-as-intentional` correct. Lesson: read code comments alongside the code; the comment may signal a design choice that flips the routing.
- **The M4-S2 path was wrong in the prompt I authored.** Caught at Moment 5 by the lived-code-vs-spec read step. Path-citations in prompts need a quick `find` or `Glob` check at prompt-authoring time. Going forward: cite paths only after confirming they exist.

### Decisions about how to author (meta-decisions worth carrying forward)

- **Mixed-posture narratives are a normal pattern, not an exception.** Future narratives can freely mix lived and forward-spec Moments depending on what the journey covers. The discipline (lived audit floor for lived Moments; spec audit floor for forward-spec Moments; routing per Moment) is the same.
- **Sibling-listing pattern for counterfactual outcomes.** When a narrative's protagonist has a primary-listing terminal outcome locked by an earlier narrative, introduce a sibling listing for any Moment that needs a different outcome. This avoids contradicting earlier narratives and gives the new outcome distinct anchored ground.
- **Path-citations need a `find` check at prompt-authoring time.** A trivial Bash check before committing the prompt would have caught the M4-S2 path drift. Add to the prompt-authoring checklist for narratives 005+ and any future foundation-refresh prompts.
- **Read code comments alongside the code for finding routing.** A comment that explicitly notes a design choice (like `ListingStatus.cs:5-7`'s comment on the missing `Approved` state) flips routing from `code-update` to `document-as-intentional`. Without the comment, the same code might route differently.
- **Anchored cross-narrative values pattern compounds.** Narrative 001 (keyboard fields), 003 (BoldPenguin7 specifics), 004 (camera fields) each anchor specifics that subsequent narratives can inherit. Narrative 005 may anchor auction-grain specifics (bid sequences, extended-bidding window mechanics at finer grain than narrative 001 reached).

### Patterns refined for narrative 005

Inherited from narratives 001-003 unchanged: bounded frontmatter v1, prose-paragraph Moment body, multi-slice / multi-saga-phase / multi-command Moments grow in paragraphs, single-named-protagonist plus omniscient narrator, seven disposition tags for deferral, per-Moment plus cumulative deferral discipline, code-style backticks for events and projection names, em-dash hygiene drop.

Refined this session for narrative 005's use:

- **Mixed-posture is the default for narratives spanning shipped + planned work.** Narrative 005 (Auctions, M3 + M4-S1 lived; M4-S5/S6 Flash session forward-spec) will likely need this pattern.
- **Sibling-listing pattern.** Available if narrative 005 needs counterfactual auction outcomes.
- **Path-citation pre-check.** Prompt-authoring for narrative 005 should confirm any M3/M4 retro paths and W002 section anchors before committing the prompt.
- **Code-comment-as-routing-evidence.** Apply when auditing M3+M4 Auctions code; the codebase has rich inline comments that may flip routing.

### Quality signal from the session

User feedback clean throughout. No Moment titles needed revision. Three findings' routings held under user adjudication (F001-F003 all sat at proposed routings; the F003 `code-update`-vs-`document-as-intentional` distinction was author-leaned and user-confirmed via the in-line comment in `ListingStatus.cs`). Path-correction was caught and bundled cleanly into the Moment 5 commit. Em-dash hygiene drop continued without friction.

The seller-perspective POV held throughout — no slips into bidder-side framing. The narrator's responsibility (rendering GreyOwl12's view faithfully) was load-bearing for Moments 1, 2, 3, and 5; Moment 4's compression deliberately repeated the seller-perspective shape from Moments 2-3.

### Follow-ups generated

- **F001** (hardcoded FeePercentage 0.10m placeholder in `SubmitListingHandler`) **routed `document-as-intentional`** with the comment "M5 placeholder — no fee engine exists yet" as the design-intent evidence. No in-PR or follow-up code change. Will be revisited when M5 ships and the fee-engine work moves the constant into a configurable boundary.
- **F002** (missing `SubmitListing` HTTP endpoint) **stub follow-up prompt** at `docs/prompts/implementations/n004-fu-submit-listing-endpoint.md`. Slice scope: add a Wolverine HTTP endpoint that invokes the existing `SubmitListingHandler` so the seller dashboard can trigger submit via HTTP rather than only via aggregate-handler invocation. Resolution runs in subsequent product work.
- **F003** (missing `Approved` intermediate state in `ListingStatus` enum) **routed `document-as-intentional`** with the inline comment in `ListingStatus.cs:5-7` as the design-intent evidence. The design compresses what could be a 6-state machine (Draft, Submitted, Approved, Published, Rejected, Withdrawn) into 5 states by treating auto-approval-and-publication as a single observable transition. No in-PR or follow-up code change. W004 may benefit from explicitly naming the compression in its Phase 1 aggregate-state-machine sketch; that is a future workshop-cleanup edit, not a Phase 5 deliverable.
- **W004 storage-layer audit** confirmed clean against ADR 011. Narrative 002 surfaced F003 there for W003's Polecat / SQL Server staleness; W004 carries no analogous staleness. No F004 needed.
- **Methodology log Entry 001 considered and consciously skipped** at session close. Three of the four lived-BC narratives have now produced findings (003, 004, plus narrative 002's forward-spec workshop-update findings). Narrative 005 (Auctions) is the final lived-BC chance before Phase 5 closes. The cross-cutting observations are accumulating but remain narrative-grain rather than methodology-grain at this session's close.

### Narrative #5 candidate

Per Phase 5 prompt §3.5, narrative 005 is the Auctions BC backfill (`005-...`). Largest lived surface in the project (M3 S1-S6 plus M4-S1). Default protagonist is GreyOwl12 (seller-perspective on a winning Flash auction with extended bidding) or operator-perspective on the same. Confirm at narrative 005 session start. Likely Moments: session creation and listing attachment (operator territory), bid-by-bid sequence (auctioneer-or-operator window), auction closing saga at finer grain than narrative 001 Moment 7 reached, terminal-outcome paths.

### Narrative status

**Complete (v0.1, 2026-04-29).** Five Moments, cumulative deferred section, retrospective. Format conventions inherited from narratives 001-003. Mixed-posture pattern (lived + forward-spec) and sibling-listing pattern established. Status flipped to `accepted` in the session-close commit.

---

## Document History

- **v0.1** (2026-04-29): Initial authoring as foundation-refresh Phase 5 Item 1c deliverable. Five Moments covering W001 slices 0.3, 1.1, 1.2 plus the M4-S2 forward-spec slice. First seller-perspective narrative for CritterBids; first to use the `single-seller` perspective slot. Three findings filed: F001 hardcoded FeePercentage placeholder (`document-as-intentional`), F002 missing SubmitListing HTTP endpoint (`code-update`, stub follow-up), F003 missing Approved intermediate state (`document-as-intentional`). M4-S2 path-citation correction landed in the Moment 5 commit. Vintage Folding Camera's listing-time fields anchored as canonical: title "Vintage Folding Camera", Format Timed, StartingBid $40, ReservePrice null, BuyItNowPrice $80, ExtendedBiddingEnabled false, Duration 7 days, FeePercentage 0.10. W004 confirmed clean against ADR 011 (no Polecat / SQL Server staleness; in contrast to W003's narrative-002-surfaced F003). Mixed-posture pattern (lived Moments 1-4, forward-spec Moment 5) and sibling-listing pattern (the camera) established for narrative 005's use.
