# M8-S3a: Bid Placement Endpoint — expose the existing `PlaceBid` DCB command over HTTP

**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — slice plan §7, row **M8-S3a**; the one sanctioned backend exception per §3
**Slice:** S3a of M8 (**backend precursor** to the M8-S3b frontend live-bidding slice; the only backend change M8 takes on)
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` (Moment 4 — "SwiftFerret42 places her first bid") — this slice lands the **HTTP surface** for Moment 4's bid placement; the placement *UI* is M8-S3b
**Agent:** @PSA (Auctions BC) with @QAE on the DCB / rejection-path test coverage
**Estimated scope:** one PR, **backend only** (Auctions BC) — one HTTP endpoint + its request/response contract, a result path over the existing DCB handler, server-side credit-ceiling sourcing, and integration tests. **No frontend, no ADR-014, no second endpoint.**

---

## Preconditions

- **M8 milestone doc is amended to v0.2** (`docs/milestones/M8-frontend-spas.md`) recording the S3a/S3b split and the §3 sanctioned-exception carve-out. This prompt is authored alongside that amendment; if the milestone does not show the M8-S3a row and the §3 exception, **stop and escalate** — do not infer the backend exception from this prompt (AUTHORING rule 4).
- **M8-S2 is merged** (bidder shell + catalog on `main`); M8-S2's `client/` is untouched by this slice.
- **This is the only sanctioned backend change in all of M8.** Do not expand beyond the bid endpoint — no BuyNow HTTP surface, no proxy-bid surface, no other BC touch. Anything larger is a fresh escalation, not this slice.

## Goal

Expose the **existing** internal `PlaceBid` DCB command (`src/CritterBids.Auctions/PlaceBidHandler.cs`) over HTTP as `POST /api/auctions/bids`, `[AllowAnonymous]`, returning a **clear accept-vs-reject result** so the M8-S3b frontend can place a bid and do optimistic-update-then-rollback on rejection. This slice originates **no new domain capability** — the DCB consistency boundary, the rejection rules (below-minimum, exceeds-credit-ceiling, listing-closed/not-open, seller-cannot-bid), the `$1`-under-`$100` / `$5`-at-`$100+` increment policy, the `BidPlaced`/`ReserveMet`/`ExtendedBiddingTriggered` acceptance events, and the `BidRejected` audit stream all already exist. The slice's work is the **HTTP contract over them**: a result path (the bus handler currently returns void and swallows the outcome into the audit stream), server-side credit-ceiling sourcing (the browser must not supply its own ceiling), and the rejection → ProblemDetails mapping the frontend reads.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M8-frontend-spas.md` | **Authoritative for scope.** §3 sanctioned-exception carve-out (the bounds of what this slice may touch) + §7 row M8-S3a. The exception adds **no new domain capability** — hold that line. |
| `docs/narratives/001-bidder-wins-flash-auction.md` (Moment 4) | The bid-placement journey, jointly authoritative (rule 3). Moment 4 names the bidder placing a bid; this slice is its HTTP half. Note the bid-increment policy and credit-ceiling beats. |
| `src/CritterBids.Auctions/PlaceBid.cs` + `PlaceBidHandler.cs` | The **existing** command + DCB handler. `EvaluateRejection` returns the reason strings; `AcceptanceEvents` builds the events; `HandleAsync` does the `FetchForWritingByTags` + `AssertDcbConsistency` write but **returns void** (rejection → `BidRejected` audit + silent return). The HTTP result path is the design problem. |
| `src/CritterBids.Auctions/BidConsistencyState.cs` + `docs/skills/dynamic-consistency-boundary/SKILL.md` | The DCB pattern the endpoint must **preserve** (`FetchForWritingByTags<BidConsistencyState>` queues the `AssertDcbConsistency` operation; the optimistic-concurrency guarantee must survive the HTTP path). |
| `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs` | `CreditCeiling` is minted into `ParticipantSessionStarted` and **"never returned in HTTP responses."** The command's `CreditCeiling` field is an M3 test-era shape; for an HTTP bid the ceiling must be sourced **server-side**, not trusted from the client. This is the central Open Question. |
| `docs/skills/wolverine-message-handlers/SKILL.md` + the project's ProblemDetails/railway convention | HTTP endpoint discovery, the `(ResponseType, …)` tuple shape, and rejection → `ProblemDetails` (the M8-S2 frontend consumes ProblemDetails on 4xx). |
| `CLAUDE.md` + `docs/skills/frontend/LESSONS.md` (§A) | Global conventions (`sealed record`, `[AllowAnonymous]` participant-facing per ADR-024, UUID v7 for ids, `OutgoingMessages` not `IMessageBus`, no "paddle"). LESSONS §A records the wire conventions the S3b frontend will consume (camelCase, empty-vs-404, ProblemDetails) — design the response with that consumer in mind. |

## In scope

1. **`POST /api/auctions/bids` endpoint.** `[AllowAnonymous]`. Request carries `listingId`, `bidderId`, `amount`. It invokes the bid decision over the DCB path and returns:
   - **Acceptance** → a success response (status + body designed for the S3b optimistic-update/rollback consumer — e.g. the new high bid, bid count, and whether extended-bidding triggered with the new close time).
   - **Rejection** → a `ProblemDetails` 4xx carrying the machine-readable reason (`BelowMinimumBid`, `ExceedsCreditCeiling`, `ListingClosed`, `ListingNotOpen`, `SellerCannotBid`).
2. **A result path over the existing DCB write.** `HandleAsync` currently returns void and routes rejection into the `BidRejected` audit stream. Design a path where the **HTTP endpoint learns accept/reject + reason** while **preserving** (a) the `BidRejected` audit-stream write and (b) the DCB `AssertDcbConsistency` optimistic-concurrency guarantee. (E.g. a dedicated endpoint handler that evaluates + writes + returns an outcome, or a refactor of the decision/write split — your design, signed off.)
3. **Server-side credit-ceiling sourcing.** The browser must not supply `CreditCeiling`. Source the participant's real ceiling server-side without violating BC boundaries (the value is owned by Participants, minted into `ParticipantSessionStarted`). Resolve where Auctions obtains it — see Open Questions; **escalate if it requires more than a thin read surface.**
4. **Integration tests** (Auctions suite, already in the CI matrix): the acceptance path (a `BidPlaced` is appended and is observable on `BiddingHub`); **each** rejection reason → correct HTTP status + ProblemDetails; a client-supplied inflated ceiling **cannot** bypass the credit check; the DCB concurrency assertion still holds.
5. **Auth posture:** `[AllowAnonymous]`, consistent with the anonymous bidder app and ADR-024 (participant-facing endpoints stay anonymous; `BidderId` is a display identifier, not a trust anchor — per the `BiddingHub` precedent).

## Explicitly out of scope

- **Any frontend.** No `client/` change, no placement UI, no ADR-014, no SignalR client work — that is **M8-S3b**.
- **The BuyNow HTTP endpoint** (the sibling `BuyNow` DCB command) and **any proxy-bid HTTP surface** — not this slice.
- **New domain capability:** no new events, no change to the DCB rules, the rejection reasons, or the `$1`/`$5` increment policy. This slice only wraps existing behavior in HTTP.
- **Auth / `StaffToken` changes** — the bid endpoint is anonymous; ADR-024's staff posture is untouched.
- **`BiddingHub` or its push handlers** — the live-bidding read side is already complete; do not modify it.
- **A general Participants read API** beyond the thin surface the credit-ceiling sourcing strictly needs — if sourcing the ceiling balloons into a broader cross-BC contract, **escalate** rather than build it here.

## Conventions to pin or follow

- **DCB:** `docs/skills/dynamic-consistency-boundary/SKILL.md` owns the `FetchForWritingByTags` + `AssertDcbConsistency` pattern; the HTTP path must preserve the consistency assertion and the `BidRejected` audit write.
- **Rejection = ProblemDetails, not exceptions** (railway style): the frontend switches on the 4xx ProblemDetails reason to roll back its optimistic update.
- **Core conventions (CLAUDE.md):** `sealed record` request/response; `IReadOnlyList<T>`; **UUID v7** for the server-generated `BidId`; `[AllowAnonymous]`; integration events via `OutgoingMessages`; `IMessageBus` only for `ScheduleAsync`.
- **Consumer-aware response design:** the S3b frontend reads camelCase JSON and ProblemDetails (LESSONS §A) — shape the acceptance/rejection responses for that consumer.

## Spec delta

Per ADR 020: this slice's spec consequence is that **narrative 001 Moment 4 gains its HTTP surface** — the `PlaceBid` command, previously invokable only internally (tests, the proxy saga), becomes a bidder-reachable `POST /api/auctions/bids` — and the workshop-002 §1 `PlaceBid` scenarios gain **HTTP-level** test coverage for the acceptance path and each rejection reason. No ADR is authored (ADR-014 is M8-S3b). The bid-placement journey's **backend half** lands; the frontend half (optimistic UI, live feed) is M8-S3b. The retro's `## Spec delta — landed?` paragraph confirms the endpoint exists, returns accept-vs-reject correctly, sources the credit ceiling server-side, and preserves the DCB guarantee + audit stream.

## Acceptance criteria

- [ ] `POST /api/auctions/bids` exists, `[AllowAnonymous]`, binds `{ listingId, bidderId, amount }` (with a JSON body — empty body 400s, per the Wolverine.HTTP command-body rule, LESSONS §A #1)
- [ ] Acceptance returns a 2xx with a response shaped for optimistic-update/rollback (new high bid + bid count + extended-bidding outcome); a `BidPlaced` event is appended and is observable on `BiddingHub`
- [ ] Each rejection reason (`BelowMinimumBid`, `ExceedsCreditCeiling`, `ListingClosed`, `ListingNotOpen`, `SellerCannotBid`) returns the correct 4xx **ProblemDetails** with a machine-readable reason
- [ ] The DCB `AssertDcbConsistency` optimistic-concurrency guarantee and the `BidRejected` audit-stream write are **preserved** (test-covered, not regressed by the HTTP path)
- [ ] The credit ceiling is **sourced server-side**; an integration test proves a client cannot supply an inflated ceiling to bypass `ExceedsCreditCeiling`
- [ ] `BidId` is server-generated **UUID v7** (or the resolved idempotency scheme — see Open Questions)
- [ ] `dotnet build CritterBids.slnx` → 0 errors / 0 warnings; `dotnet test` green; the Auctions integration suite is extended with the new endpoint tests
- [ ] **No** `client/` change, **no** ADR-014, **no** BuyNow endpoint, **no** auth/`StaffToken` change, **no** DCB-rule or increment-policy change, **no** `BiddingHub` change
- [ ] `docs/retrospectives/M8-S3a-bid-placement-endpoint-retrospective.md` written with the `**Prompt:**` header and `## Spec delta — landed?` paragraph
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer

## Open questions

- **Credit-ceiling sourcing (the central design decision).** The browser must not supply `CreditCeiling`. Where does Auctions obtain the trusted value? Candidates: (a) the `Session` aggregate (M4) already carries it into a stream the bid boundary can read; (b) a thin Participants read the endpoint calls; (c) it already lands in `BidConsistencyState` via an integration event. Resolve consistent with the modular-monolith boundary (no reading another BC's internals). **If the clean answer requires more than a thin read surface, escalate** — it may be its own small slice rather than smuggled in here.
- **Accept/reject result path.** `HandleAsync` returns void today. Options: a dedicated HTTP endpoint handler that runs the decision + DCB write + returns an outcome; or a refactor that has the decision return an outcome the audit/HTTP paths both consume. Either way preserve the audit write + DCB assertion. **Sign off the chosen shape before wiring.**
- **`BidId` idempotency.** Server-generate a UUID v7 per request (simplest) vs accept a client idempotency key (lets the frontend safely retry a dropped request without double-bidding). Decide against the S3b retry/rollback story.
- **HTTP status mapping.** `400` for validation rejections (below-minimum, exceeds-ceiling, seller-cannot-bid) vs `409 Conflict` for state rejections (listing closed / not open) — confirm the split serves the frontend's rollback logic.
- **Acceptance response shape.** Exactly which fields the success body returns (new high bid, bid count, `extendedBidding` { newCloseAt }?) — design for the S3b optimistic-update reconciliation, and record it so the S3b prompt can bind to it.
