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
