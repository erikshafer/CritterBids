---
slug: 003-bidder-starts-anonymous-session
status: draft
journey: bidder
perspective: single-bidder
scope: happy-path
bounded_contexts: [Participants]
boundaries_touched: []
slices_implemented: [0.2]
canonical_id: ParticipantId
---

# Bidder Starts Anonymous Session (Happy Path)

A Participants-grain narrative. BoldPenguin7 — known offstage in narrative 001 as the bidder who outbids SwiftFerret42 mid-Flash session — does not yet have a name when this narrative opens. She is a human at the conference floor with a phone in hand and a QR code in front of her. By the end of the narrative's three Moments she has an anonymous session, a system-derived display name, a `BidderId`, a hidden credit ceiling, and a catalog page loaded. Where narrative 001 Moment 1 collapsed the entire session-start cascade into one bidder-visible beat from SwiftFerret42's window, this narrative dramatises the Participants BC's internal mechanics — the UUID v7 stream creation, the byte-derived field mints, the aggregate's `HasActiveSession` flip — at the grain at which a Participants developer would think about the system.

The audit floor is shipped Participants code at `src/CritterBids.Participants/`. M1 has shipped; the `StartParticipantSession` handler is real; the `Participant` aggregate is real; the M1-S5 retrospective records the design history. Findings surfaced during authoring route through the four-lane discipline; `code-update` is a real lane this narrative will exercise (the line-12 comment misclaim that narrative 001 Finding 002 left in the code is a known candidate, surfaced and resolved in this PR).

## Cast

- **BoldPenguin7** — the bidder, protagonist. Anonymous human-with-a-phone at journey start; system-named participant by journey end. Single protagonist; the narrative is told entirely from her vantage. She is the offstage competitor in narrative 001 Moments 5 and 6 (the bidder who places the $35 outbid against SwiftFerret42's opening $30 and is later outbid by SwiftFerret42's $55 retaliation); narrative 003 covers her entry into the system, hours before that bid lands.
- **The Participants BC handler** — onstage in Moment 2. The narrator dramatises the `StartParticipantSession` handler's mint cascade: UUID v7 generation, byte-by-byte field derivations, event commit, aggregate hydration.
- **The `Participant` aggregate** — onstage in Moment 2's tail. The narrator names its `HasActiveSession` flip and the post-flip state shape (`Id`, `HasActiveSession`, `IsRegisteredSeller`). The aggregate carries an additional `Apply` for `SellerRegistered` from slice 0.3 (`RegisterAsSeller`); BoldPenguin7 will not exercise that path in this narrative, but the aggregate's two-event Apply surface is part of the BC's design that the narrator may name once for orientation.
- **Wolverine and Marten** — onstage as runtime primitives. The narrator names the `MartenOps.StartStream<Participant>` commit, the UUID v7 timestamp-prefix-plus-random-low-bytes structure, the `[AllowAnonymous]` endpoint posture per M1. Implementation-detail surfaces (handler-method-tuple-return-shape, the `IStartStream` Marten primitive's exact API) stay in skill-file territory; the narrator describes the runtime work without naming the API patterns.
- **The conference Wi-Fi and the demo's API host** — offstage runtime infrastructure. Setting names them; Moments do not dramatise them.
- **SwiftFerret42, GreyOwl12, the auction operator, the seller, BCs other than Participants** — offstage. Out of frame for this narrative; appear in narratives 001, 002, 004, 005 at their natural beats.

## Setting

A weekday afternoon at Nebraska.Code(). The conference floor has been open for several hours; the keynote is over and attendees are filtering through booths. BoldPenguin7 — not yet named that — is a human in the crowd with a phone in hand. She has just walked up to the CritterBids booth and noticed a printed sign with a QR code. SwiftFerret42 is somewhere else on the floor and has not yet scanned in; in roughly thirty minutes she will. The auction operator is at the SessionManager screen finalising the demo Flash session lineup. Three listings are published and pending session attachment.

The system's MVP infrastructure is healthy. The CritterBids API host is running; the Participants BC's `[AllowAnonymous]` endpoint at `/api/participants/session` is reachable; Wolverine is processing requests; Marten's event store on PostgreSQL is up. The conference Wi-Fi is fast enough that the QR-scan-to-page-load round trip will land in well under a second. No rate limit is in play, no infrastructure hiccup is queued, no UUID v7 collision will fire (a collision in a single millisecond requires two 80-bit random values to coincide; the demo's bidder count is forty, the millisecond window is the request-arrival rate, the probability is below the floor of practical concern).

Auction-system policy is at MVP defaults. The `[Authorize]` global convention from `CLAUDE.md` is overridden by the Participants BC's `[AllowAnonymous]` posture per M1 — real authentication and account binding are deferred to M6. UUID v7 stream IDs apply per ADR 007: the stream ID is generated fresh, the timestamp prefix gives Marten insert locality, the random low bytes drive the byte-derived display name and `BidderId`. Display names follow the `<Adjective><Animal><Number>` convention with 25 adjectives, 29 animals, and a 1-9999 number suffix — roughly 7.25 million tuples, well above the demo's bidder count, with collision probability under 0.001% at conference scale. Credit ceilings are drawn from nine discrete values between $200 and $1000 in $100 steps, derived deterministically from byte 14 of the stream ID and hidden from the bidder by design (never returned in any HTTP response payload). The cleanest possible run.

## Moment 1: BoldPenguin7 scans the QR code

**Implements:** slice 0.2.

**Context.** BoldPenguin7 stands at the CritterBids booth on the conference floor with her phone in hand. She has just framed and scanned the QR code on the printed sign. Her phone has decoded the QR's URL — the demo's landing route, served by the CritterBids API host — and is loading the page. She has no prior events in the system; nothing about her exists in any stream yet. The auction operator is still finalising the Flash session lineup; the demo session has not yet started.

**Interaction.** The page POSTs an empty body to `/api/participants/session`. Wolverine routes the request to the `StartParticipantSession` handler, which treats the call as the first event in a new lifecycle. The HTTP request travels over the conference Wi-Fi, lands at the API host, and is queued for handler dispatch.

**Response.** The request is in flight; nothing has committed yet. BoldPenguin7's phone shows a brief loading state. The handler is about to run.

**Why this matters to the bidder.** This is the moment the bidder crosses the threshold from anonymous-pre-system to anonymous-with-system. Before the QR scan, she has no relationship to CritterBids: no Participant stream exists for her, no `BidderId` has been computed, no credit ceiling has been rolled. After the request lands, all of those are about to be true in the next handler tick. Her perception is a brief loading spinner; the system's perception is that a new lifecycle is opening.

### Things deliberately not included

- Rejoin-vs-new-session behavior on QR re-scan (the system unconditionally mints a fresh session per scan; whether the same human scanning twice should land in the same session is product-decision territory). *(`defer`.)*
- Authentication or account binding. *(`post-MVP`; M6 introduces real authentication and the `[AllowAnonymous]` posture lifts at that point.)*
- Failure modes for the request itself: lost Wi-Fi connectivity, rate limit (none configured at MVP), API-host downtime. *(`alternate-path-failure`.)*

## Moment 2: The system mints BoldPenguin7

**Implements:** slice 0.2.

**Context.** The HTTP POST from Moment 1 has landed at the `StartParticipantSession` handler. BoldPenguin7's phone is showing the loading state; her connection is held open awaiting the response. No state has yet committed; her presence in the system is a single in-flight request and the moment-old loading spinner. The handler is about to run.

**Interaction.** Wolverine invokes `StartParticipantSession.Handle(cmd)`. The handler runs a single synchronous block of derivations, builds an event, and returns a tuple of the HTTP response and a Marten `IStartStream` instruction. Marten's commit follows when the handler returns.

**Response.** The handler generates a UUID v7 via `Guid.CreateVersion7()`. The UUID's high bytes carry the millisecond timestamp prefix; the low bytes are independently randomised. This is the `ParticipantId` — the stream key under which BoldPenguin7's lifecycle will live for the rest of the conference. The timestamp prefix gives Marten insert locality on PostgreSQL; the random low bytes are the source of every other identifier the bidder will carry.

The byte-derived field mints fire next, all from the UUID's low bytes. Byte 8 modulo 25 selects the adjective (one of `Bold`, `Swift`, `Fierce`, `Calm`, `Bright`, `Nimble`, `Keen`, and eighteen others); byte 9 modulo 29 selects the animal (one of `Ferret`, `Penguin`, `Otter`, `Falcon`, and twenty-five others); bytes 10 and 11 combined modulo 9999 plus 1 yield the number suffix (1 to 9999). For BoldPenguin7's stream the bytes happen to land on `Bold`, `Penguin`, and `7`, producing the display name `BoldPenguin7`. Bytes 12 and 13 combined modulo 9999 plus 1 yield the `BidderId` number; in BoldPenguin7's case, `Bidder 4523`. Byte 14 modulo 9 multiplied by 100 plus 200 yields the credit ceiling; for BoldPenguin7, $700 (the sixth of nine discrete values: $200, $300, $400, $500, $600, **$700**, $800, $900, $1000). The credit ceiling is hidden by design — it lives on the event payload but is never returned through the HTTP response.

The handler constructs `ParticipantSessionStarted(ParticipantId, DisplayName: "BoldPenguin7", BidderId: "Bidder 4523", CreditCeiling: $700, OccurredAt: <now>)` and returns `(CreationResponse<Guid>(Location: "/api/participants/{ParticipantId}", participantId), MartenOps.StartStream<Participant>(participantId, evt))`. Marten commits the new stream when the handler returns: a single `ParticipantSessionStarted` event lands as the first entry in BoldPenguin7's stream, keyed on her `ParticipantId`.

The `Participant` aggregate's `Apply` for `ParticipantSessionStarted` runs against the freshly committed event. It sets `Id` to the `ParticipantId` and flips `HasActiveSession` to true. The aggregate's third property, `IsRegisteredSeller`, remains false — BoldPenguin7 is a bidder, not a seller, and the slice 0.3 `RegisterAsSeller` flow is out of scope for this narrative. Her aggregate state shape now reads `(Id: ParticipantId, HasActiveSession: true, IsRegisteredSeller: false)`.

**Why this matters to the bidder.** BoldPenguin7 perceives nothing in this Moment — her phone still shows the loading state; the response has not yet returned. But her relationship with CritterBids has just been established as a durable system fact. The `ParticipantId` will travel with every bid she places for the rest of the conference; the `BidderId` will appear on every `BidPlaced` event whose payload includes her bid; the credit ceiling will silently gate her bid acceptances for the rest of her session. Three of those four facts are now committed to the event stream. None of them will appear in the response payload she is about to receive — the credit ceiling is hidden by design, and the `BidderId` is event-only. The display name `BoldPenguin7` is the one identifier she will see in Moment 3.

### Things deliberately not included

- The aggregate's `Apply` method for `SellerRegistered` (slice 0.3); the aggregate's `IsRegisteredSeller` flip. The aggregate carries the seller-side Apply as part of the BC's two-event design but BoldPenguin7 will never exercise that path in this narrative. *(`separate-narrative`; candidate for narrative 004 (Selling BC) or a future seller-perspective narrative.)*
- Failure modes during the commit: Marten transaction failure, PostgreSQL connection drop, UUID v7 collision. None will fire in this happy-path Moment. *(`alternate-path-failure`.)*

## Moment 3: BoldPenguin7's phone lands on the catalog

**Implements:** slice 0.2.

**Context.** The handler from Moment 2 has returned. Marten's commit has landed: the `ParticipantSessionStarted` event sits as the first entry in BoldPenguin7's stream, the `Participant` aggregate's state shape reads `(Id: ParticipantId, HasActiveSession: true, IsRegisteredSeller: false)`, and the HTTP response is on its way back over the conference Wi-Fi. BoldPenguin7's phone is still showing the loading spinner from Moment 1; the network round-trip from QR scan to handler-response-arrival has taken under a second.

**Interaction.** The response arrives. The HTTP status is 201 Created. The body carries the `ParticipantId` Guid. The Location header reads `/api/participants/{ParticipantId}` — the URI under which any future participant lookup would happen, though the system has no GET endpoint at that URI today (Finding 002). The frontend loader sees the response and transitions to the catalog page route.

**Response.** BoldPenguin7's phone displays the catalog page. The header reads "BoldPenguin7" — her display name, surfaced for the first time. The list of published listings is visible: a Vintage Mechanical Keyboard, a Rare Pokemon Card, a Hand-Carved Wooden Bowl. None of them are bid-able yet because the operator has not started the Flash session. Her credit balance is not displayed; the system's design hides the credit ceiling from the bidder by intent, and the catalog page does not query any balance endpoint. From this point until she places her first bid (narrative 001 Moment 5 from SwiftFerret42's window), BoldPenguin7's experience of the system is browsing the catalog and waiting for the session to start.

**Why this matters to the bidder.** The journey from anonymous human to anonymous-with-system is now complete from her perception. She has a name, a catalog to read, and the implicit promise of a session that will start when the operator starts it. The system has done the durable identity work — the `ParticipantId` is keyed on her stream forever; the `BidderId` "Bidder 4523" lives on the `ParticipantSessionStarted` event payload; the credit ceiling $700 silently gates the bids she will place when the session runs. From her phone's perspective, the system has acted: she scanned, she waited briefly, the catalog appeared. The deeper system mechanics from Moment 2 are invisible to her by design. The QR code on the printed sign at the booth has done its work.

### Things deliberately not included

- The catalog-page UI rendering itself: how listings are laid out, what fields are shown per listing, what happens when she taps one. *(`UX-or-UI-detail`; design artifact territory.)*
- The display-name source for the catalog-page header. The frontend somehow renders "BoldPenguin7" but the system has no `GET /api/participants/{id}` endpoint today; the UI claim is forward-spec for M6's frontend MVP and surfaces the backend gap as Finding 002 at session close. *(`defer`; trigger is M6 frontend ship.)*
- Pre-session catalog interactions: tapping into a listing's detail, watchlist actions, filter-and-sort. *(`separate-narrative`; covered in narrative 001 Moments 2-3 from SwiftFerret42's window.)*

## Deferred from this narrative

The following were deliberately not narrated in this Participants-perspective happy-path narrative. Each is named with its disposition so future sessions can pull from this list when scoping the next narrative, ADR, skill file, or implementation prompt. Items here are not bugs or omissions; they are consciously deferred and traceable. Items recorded in `003-findings.md` (whether `code-update` resolved in-PR or routed to a stub follow-up) are not duplicated here.

### `defer` (revisit when trigger lands)

- Rejoin-vs-new-session behavior on QR re-scan (Moment 1; trigger: production usage at scale revealing duplicate-scan patterns).
- Display-name source for the catalog-page header / `GET /api/participants/{id}` endpoint backing the UI claim (Moment 3; trigger: M6 frontend MVP ship; the backend-gap component routes as Finding 002).

### `post-MVP` (beyond v1 scope)

- Authentication or account binding (Moment 1; M6 introduces real authentication and the `[AllowAnonymous]` posture lifts at that point).

### `separate-narrative` (other journey perspectives)

- The aggregate's `Apply` for `SellerRegistered` and the slice 0.3 `RegisterAsSeller` flow (Moment 2; candidate for narrative 004 Selling BC backfill or a future seller-perspective narrative).
- Pre-session catalog interactions: tapping into a listing's detail, watchlist actions, filter-and-sort (Moment 3; covered in narrative 001 Moments 2-3 from SwiftFerret42's window at finer journey grain).

### `UX-or-UI-detail` (app design)

- The catalog-page UI rendering itself: layout, per-listing fields, tap interactions (Moment 3).

### `alternate-path-failure` (failure modes warranting their own narratives)

- Failure modes for the QR-scan request: lost Wi-Fi connectivity, rate limit (none configured at MVP), API-host downtime (Moment 1).
- Failure modes during the Marten commit: transaction failure, PostgreSQL connection drop, UUID v7 collision (Moment 2; collision probability vanishingly small but non-zero).

## Retrospective

### Narrative intent vs. outcome

Stated goal at session start: author the Participants BC's backfill narrative covering BoldPenguin7's experience as her phone scans the QR code, the system mints her anonymous session and identity, and she lands on the catalog page. Audit W001 §"Tier 0 — Bidder onboarding" and lived `src/CritterBids.Participants/` code; route disagreements through the four-lane findings discipline; add per-row narrative back-references on W001's slice 0.2 entry.

**Outcome.** Three Moments covering W001 slice 0.2. One Moment fully bidder-visible (Moment 1's QR scan), one Moment fully narrator-led (Moment 2's mint cascade), one Moment landing the bidder-visible response (Moment 3's catalog landing). Two findings filed in `003-findings.md`: F001 (`StartParticipantSession.cs` line 12 + line 48 comment misclaims about display-name uniqueness) routed `code-update` and resolved in-PR; F002 (missing `GET /api/participants/{id}` endpoint backing the catalog-header display-name UI) routed `code-update` and routed to a stub follow-up implementation prompt per Phase 2.5 discipline. BoldPenguin7's anchored cross-narrative values (BidderId "Bidder 4523", credit ceiling $700) established as canonical from this narrative since narrative 001 left them unspecified. Cast and Setting locked first; Moment-by-Moment sign-off cadence held throughout. Goal met.

### What worked

- **Lived-code audit posture flipped cleanly from narrative 002's forward-spec.** Two `code-update` findings surfaced naturally; the audit floor (shipped Participants code) made every Moment's claim verifiable against `StartParticipantSession.cs` and `Participant.cs`. The findings-lane mix matched the prompt's heads-up section: `code-update` as a real lane, `narrative-update` and `workshop-update` not surfaced (slice 0.2's prior Finding 002 from narrative 001 had already corrected the workshop-side; nothing fresh to surface there).
- **Pre-Moment lived-code reads surfaced both findings.** F001 (the line-12 / line-48 comment misclaims) was caught by reading the handler before drafting Moment 2. F002 (missing GET endpoint) was caught by searching the Features directory for any GET registration before drafting Moment 3. Lesson: read the code path *plus* the surrounding directory before drafting the bidder-visible Moments; a single-handler read may not surface adjacent gaps.
- **Anchored cross-narrative values established at the right narrative.** BoldPenguin7's `BidderId "Bidder 4523"` and `$700` credit ceiling are canonical from this narrative onward. Narrative 001 left them unspecified; narrative 003 is the natural place to anchor them because it dramatises the mint cascade that produces them.
- **Em-dash hygiene drop unblocked cleaner prose.** Em-dashes used naturally for parentheticals throughout; no audit overhead. Pre-existing convention from narratives 001 and 002 ( ` - ` for parentheticals) was preserved where it already existed but not enforced for new prose.
- **Multi-paragraph `Response.` worked for the dense Moment 2.** Five paragraphs walked the saga of UUID v7 → byte derivations → event construction → MartenOps commit → aggregate Apply, all without crossing into implementation prose. The README's multi-slice convention extends naturally to multi-system-phase Moments at finer grain.
- **The fold-into-one-PR pattern (inherited from narrative 002) carried clean.** Prompt and narrative session co-landed on the same branch with per-commit cadence. Eight commits between the prompt and the closing arc; each commit served one beat.

### What was hard

- **The catalog-header display-name UI claim had no backing GET endpoint.** Narrative 001 Moment 1 made the claim ("the catalog page transitions with display name in header") without auditing the backend. Narrative 003's audit revealed the gap — no `GET /api/participants/{id}` exists. The decision surface: (a) route as `narrative-update` against narrative 001 Moment 1 per Phase 5 §7 cite-and-edit; (b) route as `code-update` Finding 002 with stub follow-up; (c) document as forward-spec UI deferral only. Chose (b): the gap is real, the resolution is a real code addition, and the lived narrative authoring it surfaces is the right place to file. Lesson: forward-spec UI claims inherited from earlier narratives should be re-audited at the BC-narrative grain, not assumed.
- **Slice 0.3's mixed BC ownership.** The `RegisterAsSeller` feature lives in `Features/RegisterAsSeller/` *inside* the Participants BC, but its event is `SellerRegistered` (Selling-domain semantics). The `Participant` aggregate has an `Apply` for both `ParticipantSessionStarted` and `SellerRegistered`; the second is half-Participants-half-Selling structurally. Narrative 003 chose to scope to slice 0.2 only and route the seller-side surface as `separate-narrative` deferred. Narrative 004 (Selling BC) will need to navigate the same mixed-ownership question from the seller side.
- **F001 and F002 needed different resolution scopes despite both being `code-update`.** F001 is a one-line comment edit; in-PR resolution per session-start lean. F002 is a real code addition (new endpoint, GET handler, response shape decision); stub follow-up per Phase 2.5 discipline. Routing-and-resolution are separate decisions even when the lane is the same.

### Decisions about how to author (meta-decisions worth carrying forward)

- **Em-dash hygiene applies to external prose only.** Memory updated at narrative 002 close (2026-04-29); narrative 003 is the first session to author with the corrected scope. Em-dashes are fine in narratives, workshops, retros, ADRs, prompts, and commit messages.
- **`code-update` resolution scope splits by edit size.** Comment-only fixes land in-PR. Non-trivial additions (new endpoints, new handlers, new aggregates) get stub follow-up prompts under `docs/prompts/implementations/<slug>.md` per Phase 2.5 discipline.
- **Anchored cross-narrative values land in the canonical-anchor narrative.** BoldPenguin7's specifics anchor here, not in narrative 001 (which left them unspecified). When narrative 004 dramatises GreyOwl12's seller-side specifics, those anchor in narrative 004. Subsequent references inherit; the canonical narrative is the source of truth for the anchored value.
- **Pre-Moment audit reads include the surrounding directory, not just the handler.** F002 was caught by searching `Features/` for any GET registration; a handler-only read would have missed it.
- **Forward-spec UI claims inherited from earlier narratives are re-auditable at BC-narrative grain.** Narrative 001's catalog-header-display-name claim turned out to have no backend backing; narrative 003 surfaced the gap as Finding 002 rather than papering over it. Phase 5 §7's cite-and-edit allowance against earlier narratives is available but not always the best surface — the lived BC narrative is often a better home for the finding because that's where the audit naturally happens.

### Patterns refined for narratives 004-005

Inherited from narratives 001 and 002 unchanged: bounded frontmatter v1, prose-paragraph Moment body, multi-slice / multi-saga-phase / multi-system-phase Moments grow in paragraphs, single-named-protagonist plus omniscient narrator, seven disposition tags for deferral, per-Moment plus cumulative deferral discipline, code-style backticks for events and projection names.

CritterBids-specific patterns refined this session:

- **Lived-code BC narratives surface real `code-update` findings.** Forward-spec narratives (like narrative 002) have zero by structural impossibility; lived BCs (like narratives 003-005) will routinely produce them. The `code-update` resolution-scope split (in-PR vs stub follow-up) becomes a per-finding routing decision.
- **Anchored cross-narrative values establish canonical specifics at the right narrative.** Narratives 004 and 005 should anchor GreyOwl12 (seller specifics, including listing details and seller-side credit/payout posture) and a TBD bidder/auctioneer (depending on protagonist choice). The canonical anchor is the narrative that naturally dramatises the value's first appearance.
- **Forward-spec UI gaps inherited from narrative 001 are re-auditable.** Narrative 004 (Selling BC) and narrative 005 (Auctions BC) may surface analogous UI gaps (seller dashboard, auction operator console) where narrative 001 made claims without backend backing. The audit at the BC-narrative grain is the right place to file.
- **Em-dash hygiene drop is permanent.** Memory `feedback_em_dash_scope.md` carries forward. Narratives 004-005 use em-dashes naturally; no audit step.

### Quality signal from the session

User feedback was clean throughout. Zero Moment titles needed revision (vs narrative 002's "Settlement claims the keyboard" → "The keyboard enters Settlement" pivot). Both findings' routings held under user adjudication; the in-PR-vs-stub resolution split for F001 vs F002 was author-leaned-and-user-confirmed. Em-dash hygiene drop was a smooth transition; the memory update mid-session (rather than at session start) did not destabilise the working pattern.

### Follow-ups generated

- **F001** (lived `StartParticipantSession.cs` line 12 + line 48 comment misclaims about display-name uniqueness) **resolved in this PR** via comment-only edits to the handler file. `dotnet build` and `dotnet test` verified per the standard `.cs`-touch discipline.
- **F002** (missing `GET /api/participants/{id}` endpoint to back the catalog-header display-name UI claim) **stub follow-up prompt** authored at `docs/prompts/implementations/<slug>.md` per Phase 2.5 discipline. Slice scope: add a GET endpoint on the Participants BC returning the participant's `DisplayName` and `BidderId` (not the credit ceiling). Resolution runs in subsequent product work.
- **Methodology log Entry 001 considered and consciously skipped** at session close. Two of the three lived-BC narratives have surfaced findings (narrative 003 today, narrative 002 a few hours ago); the cross-cutting observations are accumulating but remain narrative-grain rather than methodology-grain. Narratives 004 and 005 will be the final lived chances before Phase 5 closes.

### Narrative #4 candidate

Per Phase 5 prompt §2.3, narrative 004 is the Selling BC backfill (`004-seller-publishes-and-withdraws-listing`). Medium lived surface: M2 listing pipeline (draft, submit, automated approval, publish) plus M4-S2 WithdrawListing flow. Default protagonist: GreyOwl12 (offstage seller in narrative 001). Narrative 003's discipline hands off cleanly: lived-code audit posture, pre-Moment surrounding-directory reads, anchored cross-narrative values for GreyOwl12 if narrative 004 dramatises seller-specific specifics (seller-side credit / payout configuration, listing-draft vs published-listing payload differences).

### Narrative status

**Complete (v0.1, 2026-04-29).** Three Moments, cumulative deferred section, retrospective. Format conventions inherited from narratives 001 and 002. Status flipped to `accepted` in the session-close commit.

---

## Document History

- **v0.1** (2026-04-29): Initial authoring as foundation-refresh Phase 5 Item 1b deliverable. Three Moments covering W001 slice 0.2: BoldPenguin7's QR scan (Moment 1), the system's mint cascade (Moment 2, multi-paragraph Response), the catalog-page landing (Moment 3, surfacing the missing GET endpoint as Finding 002). Two findings filed: F001 lived comment-misclaim fix in-PR, F002 missing GET endpoint stub follow-up. BoldPenguin7's cross-narrative values anchored: `BidderId "Bidder 4523"`, credit ceiling `$700`. First session to author with the em-dash hygiene drop in effect (per memory clarification at narrative 002 close); em-dashes used naturally throughout. Slice 0.3 (RegisterAsSeller) routed `separate-narrative` deferred, deferring its mixed-Participants-Selling-ownership question to narrative 004.
