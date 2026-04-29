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
