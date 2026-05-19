# M4-S5: Session Aggregate + SessionStarted Fan-Out + PublishedListings Projection

**Milestone:** M4 — Auctions BC Completion
**Slice:** S5 of 7 (with pre-drafted S5b split slot for the `SessionStarted → BiddingOpened` fan-out handler if S5 overflows; see §Session sizing notes)
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 ("The Flash session starts and the lot board comes alive") — the cascade is forward-spec in narrative 001 with the trigger named as M4-S5 / M4-S6. S5 closes the M4-S5 half (Auctions-side aggregate + fan-out + projection); the Listings-side `SessionMembershipHandler` half stays at M4-S6.
**Agent:** @PSA
**Estimated scope:** one PR; ~14 new test methods; ~14 new files + 2 modified
**Baseline:** 134 tests passing (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 51 Auctions) · `dotnet build` 0 errors, 24 pre-existing NU1904 NuGet warnings (Marten) · M4-S4 closed at the squash-merge of PR #35 (`723c4a6`). At session open: Session aggregate does not exist; `CreateSession` / `AttachListingToSession` / `StartSession` commands do not exist; `SessionStartedHandler` (fan-out) does not exist; the `PublishedListings` projection does not exist; `ListingPublishedHandler.cs:46` unconditionally unwraps `message.Duration!.Value` and would `NullReferenceException` on Flash listings (`Duration == null`). The seven Auctions contract stubs (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`) authored at M4-S1 are present but have no producer or in-BC consumer yet.

---

## Goal

Land the Session aggregate (`CreateSession` / `AttachListingToSession` / `StartSession`), the `SessionStarted → BiddingOpened` fan-out handler that opens every attached listing for bidding when the session starts, and the Auctions-side `PublishedListings` projection that the attach-time published-status check consults (M4-D4's resolution). Scope covers seven Session scenarios (§5.1 / §5.2 / §5.3 / §5.4 / §5.5 / §5.6 / §5.7), the two fan-out tests (per-listing emission + redelivery idempotency), and the projection's tolerant-upsert idempotency tests. At S5 close, an ops user can create a Flash session, attach published listings to it, and start it — at which point all attached listings open for bidding simultaneously via the fan-out handler.

S5 is the moderate-risk session of M4 per the milestone doc §9 (S3 + S4 were the highest-risk nodes; S6 is pattern-stable). Three first-use surfaces land in S5: the **first aggregate in Auctions that is not `Listing`** (Session — event-sourced, UUID v7 stream IDs per M4-D2), the **first in-repo one-inbound-N-outbound fan-out handler** (`SessionStartedHandler` emits one `BiddingOpened` per attached listing through `OutgoingMessages`), and the **third lived application of the M4-D4 duplicate-projection pattern** (`PublishedListings` joins the `ParticipantCreditCeiling` projection authored at S4 and Settlement's `BidderCreditView` from M5-S5 as named, repeated applications — the pattern is now a settled in-repo idiom rather than a novelty).

If any of those three surfaces grows past the session's budget, the candidate split is `SessionStartedHandler` → S5b per the milestone doc §9 pre-drafted slot. S5 base scope is then Session aggregate (7 scenarios) + `PublishedListings` projection (2 tests) + 3 dispatch tests with S5b covering the fan-out handler (2 tests + the `ListingPublishedHandler.cs` Flash guard).

## Context to load

- `docs/milestones/M4-auctions-bc-completion.md` — §2 (Session aggregate / fan-out scope), §6 (M4-D4 resolution + SessionStarted fan-out idempotency proposal + Session command semantics), §7 (§5 test row mapping), §8 (M4-D2, M4-D4 disposition), §9 (S5 + S5b sizing)
- `docs/workshops/002-scenarios.md` §5.1 through §5.7 only (the seven Session scenarios). §1–§4 are M3 + M4-S2/S3/S4-shipped.
- `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 (the SessionStarted cascade; narrative-side spec for what S5's fan-out implements end-to-end)
- `docs/retrospectives/M4-S4-proxy-bid-manager-terminal-paths-retrospective.md` — the "What M4-S5 should know" section (handoff payload)
- Skill files: `docs/skills/marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert" for the projection shape; `docs/skills/wolverine-message-handlers.md` §"OutgoingMessages — Producer Pattern" for the fan-out emission; `docs/skills/wolverine-sagas.md` §"Multiple Handlers + Separated — Send, Don't Invoke" and §"Saga-to-Saga Cascades" for the multi-handler `ListingPublished` cross-cut
- In-repo references: `src/CritterBids.Auctions/ListingPublishedHandler.cs` (must grow a Flash-listing guard for the `Duration is null` case; currently crashes), `src/CritterBids.Settlement/PendingSettlement.cs` + `PendingSettlementHandler.cs` (the canonical reference for the `PublishedListings` projection's shape — ListingId-keyed Marten document, tolerant upsert, terminal-status preservation)

(Seven items. Workshop §1–§4 and the M4-S3/M4-S4 prompts are out of scope — they shipped and do not need re-loading. The Settlement BC files are reference-only; do not modify Settlement.)

## In scope (numbered)

1. **`src/CritterBids.Auctions/Session.cs`** — new event-sourced aggregate. Sealed record with `Id`, `Title`, `DurationMinutes`, `AttachedListingIds` (initialized empty), `StartedAt` (null until §5.5). Three event-application methods, one per Session contract event. UUID v7 stream id per M4-D2; live stream aggregation registered in `AuctionsModule`.
2. **`src/CritterBids.Auctions/CreateSession.cs`** — create command carrying Title and DurationMinutes. Always creates a new aggregate; no uniqueness check on Title. Return shape per Selling's `CreateDraftListing` precedent — verify at session open.
3. **`src/CritterBids.Auctions/AttachListingToSession.cs`** — mutate command carrying SessionId and ListingId. Loads the Session aggregate and the `PublishedListings` projection for the listing; rejects if the projection row is absent, the row's Status is Withdrawn, or the session has already started; emits `ListingAttachedToSession` on accept.
4. **`src/CritterBids.Auctions/StartSession.cs`** — mutate command carrying SessionId. Loads the Session aggregate; rejects if AttachedListingIds is empty or StartedAt is non-null; emits `SessionStarted` carrying the full attached listing id list on accept.
5. **`src/CritterBids.Auctions/SessionStartedHandler.cs`** — new non-saga handler. On inbound `SessionStarted`, iterates the carried listing-id list, loads `PublishedListings` for each, and emits one `BiddingOpened` per listing via `OutgoingMessages` to the local bus. Each emission carries the per-listing seller / starting-bid / reserve / BIN / extended-bidding fields from the projection; `ScheduledCloseAt` is derived from the session's started-at + duration — but DurationMinutes is on `SessionCreated` not `SessionStarted`, so see Open Question 5 for the carrier resolution. Idempotency mechanism per milestone doc §6 (primary: DCB rejection on a second `BiddingOpened` append to an already-open stream; fallback: pre-query each listing's bidding state). First run confirms which path is needed.
6. **`src/CritterBids.Auctions/PublishedListings.cs`** — new Marten document, ListingId-keyed. **Verify the field shape at session open per Open Question 1.** Recommended payload: the full `BiddingOpened`-precursor payload (seller, starting bid, reserve, BIN, extended-bidding fields, status, published-at, withdrawn-at) so the fan-out handler reads it without an additional cross-BC call. The milestone doc §6 says "no fields duplicated beyond what the handler needs"; the fan-out is the handler that needs them. If Open Question 1 resolves differently, the field list shrinks.
7. **`src/CritterBids.Auctions/PublishedListingsStatus.cs`** — two-value enum (Published, Withdrawn); both declared at skeleton per the `AuctionClosingStatus` / `ProxyBidManagerStatus` precedent.
8. **`src/CritterBids.Auctions/PublishedListingsHandler.cs`** — new projection handler with two methods:
   - On `ListingPublished` — upserts the row at Status: Published; tolerant on re-delivery (preserves an already-Withdrawn row per the M5-S3 PendingSettlement terminal-status pattern).
   - On `ListingWithdrawn` — transitions to Status: Withdrawn; idempotent on re-delivery; stamps `WithdrawnAt`.
9. **`src/CritterBids.Auctions/ListingPublishedHandler.cs`** — modify: add a Flash-listing guard at the top of the handler so Flash listings (Duration null) are skipped. Flash listings are opened by the Session fan-out path, not the per-listing path. Update the existing XML doc comment to reflect the two-path topology.
10. **`src/CritterBids.Auctions/AuctionsModule.cs`** — additive only: schema registration for `Session` (auctions schema, live stream aggregation alongside the existing Listing aggregation); schema registration for `PublishedListings` (auctions schema); event-type registrations for the three Session contract events.
11. **`src/CritterBids.Api/Program.cs`** — additive only: three new publish-route rules for the Session trio to the existing `listings-auctions-events` queue (already wired at M3-S6). No new queue.
12. **`tests/CritterBids.Auctions.Tests/SessionAggregateTests.cs`** — new `[Collection(AuctionsTestCollection.Name)]` test class with **seven** `[Fact]` methods, method names exactly per milestone doc §7:
    - `CreateSession_ProducesSessionCreated` (§5.1)
    - `AttachListing_Published_ProducesListingAttachedToSession` (§5.2)
    - `AttachListing_NotPublished_Rejected` (§5.3)
    - `AttachListing_SessionStarted_Rejected` (§5.4)
    - `StartSession_WithAttachedListings_ProducesSessionStarted` (§5.5)
    - `StartSession_NoListings_Rejected` (§5.6)
    - `StartSession_AlreadyStarted_Rejected` (§5.7)
    Tests seed `PublishedListings` rows directly via a new fixture helper (item 16) rather than going through the cross-BC event flow, mirroring the M4-S4 `SeedParticipantCreditCeilingAsync` pattern.
13. **`tests/CritterBids.Auctions.Tests/SessionStartedFanOutTests.cs`** — new test class with **two** `[Fact]` methods per milestone doc §7:
    - `SessionStarted_ProducesBiddingOpenedPerListing` — dispatch `SessionStarted` with N seeded `PublishedListings` rows; assert N `BiddingOpened` emissions, one per listing, each with the per-listing payload from the projection.
    - `SessionStarted_Redelivery_DoesNotDoubleFireBiddingOpened` — dispatch `SessionStarted` twice; assert exactly N `BiddingOpened` events on the listing streams (not 2N). Per milestone doc §6's primary mechanism: DCB rejects the second `BiddingOpened` append to an already-open stream and the test asserts no exception propagates.
    Both tests dispatch via `SendMessageAndWaitAsync` because `SessionStarted` has multiple Auctions-local handlers after S5 (the Session aggregate's projection apply + the new fan-out handler — verify the actual handler count at session open). See Open Question 6.
14. **`tests/CritterBids.Auctions.Tests/CreateSessionDispatchTests.cs`** *(new)* — single `[Fact]` dispatching `CreateSession` via `InvokeMessageAndWaitAsync` (single handler). Mirrors `RegisterProxyBidDispatchTests` shape.
15. **`tests/CritterBids.Auctions.Tests/AttachListingToSessionDispatchTests.cs`** *(new)* — single `[Fact]` dispatching `AttachListingToSession` via `InvokeMessageAndWaitAsync`. Requires `PublishedListings` seed + an existing Session aggregate.
16. **`tests/CritterBids.Auctions.Tests/StartSessionDispatchTests.cs`** *(new)* — single `[Fact]` dispatching `StartSession` via `InvokeMessageAndWaitAsync`. Requires an existing Session with at least one attached listing.
17. **`tests/CritterBids.Auctions.Tests/PublishedListingsProjectionTests.cs`** *(new)* — **two** `[Fact]` methods verifying the projection's idempotency, mirroring M4-S4's `ParticipantCreditCeilingProjectionTests` shape:
    - `ListingPublished_InitializesRowAtPublished`
    - `ListingWithdrawn_TransitionsToWithdrawn` (and verifies redelivery preserves the terminal state — one test, two assertions)
18. **`tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs`** — additive only: a new `SeedPublishedListingAsync` helper carrying the full `BiddingOpened`-precursor payload with workshop-default parameter values (StartingBid $25, no reserve, no BIN, no extended bidding — per Workshop 002 §0 preamble), and a new `SeedSessionAsync` helper for seeding the Session aggregate's event stream directly via Marten's `StartStream` API. Default values explicit in helper signatures per the M4-S4 `SeedParticipantCreditCeilingAsync` precedent.
19. **`tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs`** *(verify only, possible fix)* — after item 9 lands (the Flash-listing guard in `ListingPublishedHandler`), verify both existing tests still pass. `ListingPublished_FromSelling_ProducesBiddingOpened` and `ListingPublished_Duplicate_IsIdempotent` both call the handler directly (not via the bus), so the guard is additive and harmless when `Duration` is non-null. Note in retro if anything surfaces.
20. **`docs/skills/marten-projections.md`** *(optional)* — append a §"Duplicate-Projection Pattern (M4-D4)" subsection if and only if S5's lived implementation surfaces something the skill does not predict beyond what the M5-S3 PendingSettlement section already covers. Likely target: the multi-handler-within-BC subtlety (Auctions now has two `ListingPublished` handlers: `ListingPublishedHandler` and `PublishedListingsHandler`). If nothing new surfaces, record "nothing new surfaced beyond what the skill already covers" in the retro per M3-S4b / M4-S2 / M4-S3 / M4-S4 precedent.
21. **`docs/retrospectives/M4-S5-session-aggregate-retrospective.md`** — written last. Gate below.

## Explicitly out of scope

- Listings BC catalog extension (`SessionMembershipHandler`, `CatalogListingView` Session-membership fields) — **M4-S6**. S5 does not touch any file under `src/CritterBids.Listings/`.
- ADR 014 — Cross-BC Read-Model Extension Shape — **M4-S6**. Authored alongside the Listings extension that justifies it. S5 may surface evidence the ADR will cite, but does not draft the ADR itself.
- The fourth proxy terminal (`BuyItNowPurchased`) — deferred to post-MVP per the M4-S4 OQ6a Path B resolution. The orphan-saga gap is a known issue from S4's retro and is not S5's scope to close.
- Any modification to `AuctionClosingSaga.cs` or `ProxyBidManagerSaga.cs` production code. Byte-level diff on both files must be zero.
- Any modification to M4-S1 contract stubs (`SessionCreated`, `ListingAttachedToSession`, `SessionStarted`). Byte-level diff on `src/CritterBids.Contracts/Auctions/Session*.cs` must be zero.
- HTTP endpoint surface for `CreateSession` / `AttachListingToSession` / `StartSession` — **M6** with the ops frontend. Commands continue to be exercised via `IMessageBus` dispatch in tests.
- Session lifecycle beyond the three events — no `SessionEnded`, no pre-start cancellation, no detach-listing, no proxy registration tied to an unstarted session. Per milestone doc §3 non-goals.
- `StartSession` filtering of listings withdrawn since attach — per milestone doc §3 ("Defensive pre-filtering at `StartSession` time is post-MVP hardening"). The fan-out emits `BiddingOpened` for every `ListingId` in `SessionStarted`; the DCB on an already-withdrawn listing is the terminal mechanism (or `BiddingOpened` on a withdrawn `PublishedListings` row is a no-op — exact behaviour observed at run-time per Open Question 3).
- Real authentication — `[AllowAnonymous]` through M6 remains the project stance (CLAUDE.md core conventions).
- SignalR / Relay BC wiring for `SessionStarted` push — post-M5.
- Any modification to `wolverine-sagas.md` — append-only at retro time per discipline; if a skill update is warranted, it likely targets `marten-projections.md` or `wolverine-message-handlers.md` instead.
- Operations BC live-board indicator for `SessionStarted` — post-M5.

## Conventions to pin or follow

Inherit all conventions from CLAUDE.md and prior milestones (M3-S5 / M4-S3 / M4-S4 saga conventions, M4-S1 / M4-S2 contract conventions, M5-S3 projection conventions, M5-S4 not-found-exception + retry-policy pattern, M4-S4 cascade-bucket-flip pattern). New conventions introduced or pinned in this slice:

- **Session aggregate stream id** — `Guid.CreateVersion7()` per M4-D2 (resolved at M4-S1). No natural business key; v7 provides insert locality via its Unix-ms prefix. Computed in `CreateSession`'s handler, returned via `CreationResponse<Guid>` so the HTTP surface (M6) can echo it back to the ops frontend.
- **Session command shape** — `[WriteAggregate(nameof(Command.SessionId))]` on `AttachListingToSession` and `StartSession` from first commit, per the M2.5 / M3 precedent reinforced through M4-S2. `CreateSession` is the create command and uses `IStartStream` (no `[WriteAggregate]` because no aggregate exists yet).
- **`PublishedListings` projection field shape** — per Open Question 1 resolution. Recommended: full `BiddingOpened`-precursor payload (i.e. the same fields as Settlement's `PendingSettlement` projection, minus the financial-specific `FeePercentage` if Auctions doesn't need it). The milestone doc §6's "no fields beyond what the handler needs" framing extends to "what the fan-out handler needs" because the fan-out is also an Auctions consumer of the projection. Document the expansion in the retro.
- **Fan-out handler idempotency** — per milestone doc §6 primary mechanism: rely on DCB rejection of a second `BiddingOpened` append. The fan-out handler emits unconditionally; the per-listing `BidConsistencyState` boundary rejects on already-open. Fallback (pre-query bidding state per listing before emission) is named for the second-run shape only if the primary mechanism does not hold cleanly at first run. Pin the lived behaviour in retro and fold into `docs/skills/wolverine-message-handlers.md` if novel.
- **Multi-handler-on-`ListingPublished`** — after S5, the Auctions BC has TWO local handlers for `ListingPublished` (`ListingPublishedHandler` from M3 + `PublishedListingsHandler` from S5). Same cross-cut as M4-S3's `BidPlaced` two-handler topology. The existing `BiddingOpenedConsumerTests` calls `ListingPublishedHandler.Handle()` directly (not via the bus), so the Send-vs-Invoke trap does not apply there. If any new test in S5 dispatches `ListingPublished` via the bus, it must use `SendMessageAndWaitAsync` per `wolverine-sagas.md` §"Multiple Handlers + Separated".
- **Cascade-bucket flip** — same precedent from M4-S4. The fan-out handler emits N `BiddingOpened`s via `OutgoingMessages`. `BiddingOpened` has no Auctions-local handler currently (the M3 `ListingPublishedHandler` produces it; no handler consumes it within the BC), so it should land in `tracked.NoRoutes` in fan-out tests. **Verify at run-time** — if the cascade ends up triggering a follow-on handler (e.g. an existing saga's `Handle(BiddingOpened)` like `AuctionClosingSaga.Handle(BiddingOpened)` from M3-S5), the bucket assignment may differ. Pin in retro.
- **Flash-listing path bifurcation** — `ListingPublishedHandler.Handle` returns early when `message.Duration is null`. The Session fan-out is the alternate path for Flash listings. The XML doc on `ListingPublishedHandler` updates to name both paths explicitly.
- **`SeedPublishedListingAsync` workshop defaults** — `StartingBid: $25`, `ReservePrice: null`, `BuyItNowPrice: null`, `ExtendedBiddingEnabled: false` per Workshop 002 §0 ("Listing-A" preamble). Defaults are explicit in the helper signature so test bodies show their data choices, per the M4-S4 `SeedParticipantCreditCeilingAsync` precedent.

## Commit sequence (proposed)

1. `feat(auctions): PublishedListings projection + handler + Marten schema + Program.cs queue routes` — items 6, 7, 8, 10 (PublishedListings portion), 11 (publish routes — note these are for the Session trio, not for ListingPublished consumption which is already wired at the queue level)
2. `fix(auctions): ListingPublishedHandler skips Flash listings (Duration == null)` — item 9 + verification that `BiddingOpenedConsumerTests` still passes
3. `test(auctions): PublishedListings projection idempotency` — item 17 (2 tests) + item 18 `SeedPublishedListingAsync` helper
4. `feat(auctions): Session aggregate + Apply methods + Marten schema` — items 1, 10 (Session portion)
5. `feat(auctions): CreateSession + AttachListingToSession + StartSession commands + scenarios 5.1-5.7` — items 2, 3, 4, 12 (7 tests) + item 18 `SeedSessionAsync` helper
6. `feat(auctions): SessionStarted fan-out handler + scenarios + fan-out tests` — items 5, 13 (2 tests). If S5b is triggered, this commit is the split boundary.
7. `test(auctions): three Session dispatch tests` — items 14, 15, 16
8. *(optional)* `docs(skills): append M4-S5 learnings to marten-projections.md` — item 20, only if something novel surfaced beyond the M5-S3 PendingSettlement section
9. `docs: write M4-S5 retrospective` — item 21

## Acceptance criteria

- [ ] `dotnet build` — 0 errors, 0 new warnings beyond the pre-existing 24 NU1904 NuGet warnings (Marten)
- [ ] `dotnet test` — 134-test baseline preserved; +7 Session aggregate scenarios + 2 fan-out tests + 3 dispatch tests + 2 projection idempotency tests = **148 total** (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + **65 Auctions**); zero skipped, zero failing
- [ ] `src/CritterBids.Auctions/Session.cs` — sealed record event-sourced aggregate with three `Apply` (or equivalent) methods consuming `SessionCreated` / `ListingAttachedToSession` / `SessionStarted`
- [ ] `src/CritterBids.Auctions/CreateSession.cs`, `AttachListingToSession.cs`, `StartSession.cs` — three commands authored; `[WriteAggregate]` from first commit on the two non-create commands
- [ ] `src/CritterBids.Auctions/SessionStartedHandler.cs` — fan-out handler emitting one `BiddingOpened` per listing in `SessionStarted.ListingIds`, loading per-listing payload from `PublishedListings`
- [ ] `src/CritterBids.Auctions/PublishedListings.cs` + `PublishedListingsStatus.cs` + `PublishedListingsHandler.cs` exist; schema registered in `AuctionsModule.ConfigureMarten`; handler has two `Handle` methods (`ListingPublished` / `ListingWithdrawn`)
- [ ] `src/CritterBids.Auctions/ListingPublishedHandler.cs` — Flash-listing guard added (skip when Duration is null); existing two `BiddingOpenedConsumerTests` still green
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — three new event-type registrations for the Session trio; two new schema registrations (Session, PublishedListings); Session live-stream-aggregation registered alongside the existing Listing aggregation
- [ ] `src/CritterBids.Api/Program.cs` — three new publish-route rules for the Session trio to the `listings-auctions-events` queue (no new queue; queue already wired at M3-S6)
- [ ] All 7 Session scenario test methods in `SessionAggregateTests.cs` named exactly per milestone doc §7 §5 and green
- [ ] Both fan-out test methods named per milestone doc §7 and green
- [ ] Three dispatch tests (one per command) green
- [ ] `src/CritterBids.Auctions/AuctionClosingSaga.cs`, `ProxyBidManagerSaga.cs`, `PlaceBidHandler.cs`, `BuyNowHandler.cs`, `BidConsistencyState.cs`, `Listing.cs` — production code unchanged (byte-level diff zero)
- [ ] `src/CritterBids.Contracts/Auctions/Session*.cs` — unchanged (byte-level diff zero)
- [ ] `src/CritterBids.Listings/**/*.cs` — unchanged (byte-level diff zero — Listings work is M4-S6)
- [ ] No `[Obsolete]`, no `#pragma warning disable`, no `throw new NotImplementedException()` in production code
- [ ] `docs/retrospectives/M4-S5-session-aggregate-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test` shows 148 passing and `dotnet build` shows no new warnings beyond the pre-existing 24 NU1904.

Retrospective content requirements:

- Baseline numbers (134 before, 148 after) with a phase table matching the M4-S4 retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit hashes
- Each of the seven Open Questions answered with which path was taken and why; for OQ1 (PublishedListings field shape), confirm whether the recommended full-payload shape or a minimal alternative was chosen and how the fan-out handler obtains the missing fields; for OQ2 (fan-out idempotency mechanism), confirm whether the DCB-only primary mechanism worked or the fallback pre-query shape was needed; for OQ5 (DurationMinutes carrier), name the resolution
- Whether the skill append in item 20 was written; if so, the appended sections listed; if not, an explicit "nothing new surfaced beyond what the skill already covers" observation
- Any blocker encountered — verbatim error message, root cause, fix path — with particular attention to:
  - Multi-handler `ListingPublished` cross-cut (the Send-vs-Invoke trap if any new test dispatches via the bus)
  - Fan-out cascade-bucket assignments for `BiddingOpened`
  - Flash-listing guard regression on existing `BiddingOpenedConsumerTests`
  - Session aggregate's `[WriteAggregate]` codegen interaction with the two non-create commands
- A **"What M4-S6 should know"** section covering at minimum:
  - Auctions BC test count at S5 close (65, up from 51)
  - The lived `PublishedListings` projection shape — input to ADR 014's evidence section
  - Whether the multi-source sibling handler choice (milestone doc §2 ADR 014 sub-question — Option A vs Option B) has any new evidence from S5's projection consumer that should bias S6's resolution
  - The fan-out handler's idempotency mechanism (lived primary vs fallback), so S6's `SessionMembershipHandler` knows what to expect from the SessionStarted event flow
  - Whether any cascade-bucket flips surfaced beyond what the M4-S4 retro and skill file already cover
  - Bid-increment helper status (still inline, threshold-of-three still uncrossed — confirm)

## Open questions (pre-mortems — flag, do not guess)

1. **`PublishedListings` field shape — minimal vs full `BiddingOpened`-precursor payload.** The milestone doc §6 specifies "small Marten document projection keyed by `ListingId` recording only the published/withdrawn transition — no fields duplicated beyond what the handler needs." That framing was set at M4-S1 when only the `AttachListingToSession` handler's needs were traced. The fan-out handler authored in this slice ALSO consults the projection — for SellerId, StartingBid, ReservePrice, BuyItNowPrice, ExtendedBidding fields. Two paths:

   - **Path A (recommended):** enrich `PublishedListings` to carry the full `BiddingOpened`-precursor payload, i.e. the same fields as Settlement's `PendingSettlement`. The fan-out handler reads the projection directly; no additional cross-BC lookups. Pattern symmetric with M5-S3's `PendingSettlement` shape — the third lived application of the M4-D4 duplicate-projection pattern lands with a richer payload, and the next slice (S6's `SessionMembershipHandler`) inherits the fully-shipped reference. Document the expansion in the retro as a stretch of "no fields beyond what the handler needs" to include the fan-out as a consumer.

   - **Path B:** keep `PublishedListings` minimal (Status + a few attached fields). Fan-out handler loads the Listing aggregate's primary stream to read BiddingOpened-precursor fields. But that stream only has events AFTER `ListingPublishedHandler` opens it — and S5's Flash-listing guard means Flash listings never reach `ListingPublishedHandler`. So the stream is empty for Flash listings at fan-out time. Path B requires either re-routing the Flash path through `ListingPublishedHandler` (defeats the bifurcation) or a third mechanism.

   Path A is structurally cleanest. Flag in retro which path landed.

2. **Fan-out handler idempotency — DCB primary vs pre-query fallback.** Milestone doc §6 names two mechanisms. Primary: emit unconditionally; DCB rejects the second `BiddingOpened` append to an already-open stream. Fallback: pre-query each listing's bidding state before emission. First-run question: does Wolverine's handler-failure semantics treat a per-listing DCB rejection as a per-event no-op (primary works) or as a handler-level failure that aborts the whole `OutgoingMessages` fan-out (fallback needed)? Resolution depends on the lived behaviour at the §"SessionStarted_Redelivery_DoesNotDoubleFireBiddingOpened" test's first run. **Halt and consult if the redelivery test surfaces handler-level failure** — the fix shape (pre-query) is mechanical but the test infrastructure may need helper changes.

3. **`StartSession` with a withdrawn listing in `ListingIds` — observed terminal path.** Milestone doc §3 says: "If a listing is attached to a session and then withdrawn via Selling's `WithdrawListing` before the session starts, `StartSession` still emits `SessionStarted` with the full `ListingIds[]`, and the fan-out handler produces `BiddingOpened` for that listing. Termination happens reactively." The "exact terminal path observed at S5 is captured in the retrospective." Two candidate paths:

   - **Path α:** the fan-out handler iterates and loads `PublishedListings` for each listing. A withdrawn listing has Status: Withdrawn. The handler chooses to skip the emission for any non-Published row, OR emits `BiddingOpened` and lets DCB / downstream reject. Per milestone doc, no defensive pre-filtering — the second alternative aligns. But DCB has no prior `BiddingOpened` to compete with for a withdrawn-not-opened listing, so DCB acceptance succeeds; the cleanup path then has to come from elsewhere (the Auction Closing saga's `Handle(ListingWithdrawn)` would not have fired because the listing was never opened in Auctions, so there's no saga to terminate).
   - **Path β:** the fan-out handler skips withdrawn listings as a defensive measure. The milestone doc says no, but the lived implementation may surface that the alternative leaves orphan-state.

   Implement Path α at first run; pin Path α vs Path β in retro based on the observed behaviour. If Path α leaks orphan state, the retro flags it as a tracked decision for post-MVP cleanup (similar shape to the M4-S4 OQ6a BIN-orphan flag).

4. **Session aggregate command shape — verify against Selling's CreateDraftListing.** The `CreateSession` handler creates a new Marten event stream; the `AttachListingToSession` and `StartSession` handlers mutate an existing stream. Two precedents to verify at session open:

   - **Selling's `CreateDraftListingHandler`** (M2) — `[WolverinePost]` HTTP endpoint with `IStartStream` return; the handler returns `(CreationResponse<Guid>, IStartStream)`.
   - **Settlement's `StartSettlementSagaHandler`** (M5-S4) — non-HTTP handler returning `(SettlementSaga?, OutgoingMessages)`.

   `CreateSession` is non-HTTP at M5 (HTTP surface is M6). The shape probably resembles Settlement's start handler more than Selling's, but with `IStartStream<Session>` for the aggregate-creation semantics. Verify at session open by reading both precedents and matching `CreateSession`'s shape to the closer one. Flag the chosen precedent in retro.

5. **`DurationMinutes` carrier on `SessionStarted` — read from session state or pass via the event.** The fan-out handler needs `DurationMinutes` to compute `BiddingOpened.ScheduledCloseAt = StartedAt + Duration`. `SessionStarted` currently carries only `(SessionId, ListingIds, StartedAt)` per the M4-S1 contract. Two options:

   - **Path A:** add `DurationMinutes: int` to `SessionStarted`. Requires modifying a contract stub — explicitly out of scope per "byte-level diff zero on Session*.cs." So this path is blocked unless the contract is revised.
   - **Path B:** the fan-out handler loads the Session aggregate from its event stream to read `DurationMinutes` (which is on `SessionCreated`). Adds one extra Marten read per fan-out invocation; acceptable.
   - **Path C:** cache `DurationMinutes` on a new lightweight Auctions-side projection (a `SessionDurations` projection keyed by SessionId). Probably overkill for one field.

   Recommended: **Path B**. The fan-out handler already accesses Marten for `PublishedListings` lookups; one additional aggregate load is the lowest-friction path and aligns with the existing pattern. Pin in retro. If Path A is needed, the contract revision moves to a separate slice or the scope decision changes here.

6. **`SessionStarted` handler count after S5 — verify Send-vs-Invoke.** S5 adds at least one `SessionStarted` handler (the fan-out handler). The Session aggregate's `Apply(SessionStarted)` is not a Wolverine handler — it's a Marten event projection — so it does not count toward the "handler" definition. After S5, `SessionStarted` should have exactly one handler in the Auctions BC (the fan-out handler). `InvokeMessageAndWaitAsync` is correct for the dispatch tests. **Verify at session open** by reading the lived handler count post-implementation; if a second handler surfaces (e.g. the Listings-BC `SessionMembershipHandler` from M4-S6 isn't loaded in the Auctions test fixture per the SettlementBcDiscoveryExclusion-style pattern, but verify the fixture exclusions cover Listings) the dispatch test shape changes.

7. **Multi-handler `ListingPublished` cross-cut — verify no existing test regresses.** After S5 the Auctions BC has TWO `ListingPublished` handlers: `ListingPublishedHandler` (M3) + new `PublishedListingsHandler` (S5). Same shape as the M4-S3 `BidPlaced` two-handler scenario. The existing `BiddingOpenedConsumerTests` calls `ListingPublishedHandler.Handle()` directly (not via the bus), so the Send-vs-Invoke trap does not apply there. `RealSellingProducerSagaTerminationTests` (M4-S2) and `PlaceBidDispatchTests` use `ListingPublished` indirectly — verify both still pass. No pre-emptive test fix is anticipated, but the agent should grep `tests/CritterBids.Auctions.Tests/*.cs` for any in-test `IMessageBus.InvokeAsync(new ListingPublished(...))` shape at session open, just in case.

8. **Session aggregate's `[WriteAggregate]` codegen vs the new `Session` type — first lived non-Listing aggregate in Auctions.** The two M2/M3 lived `[WriteAggregate]` precedents (Selling's `SellerListing`, Participants' `Participant`) are non-Auctions BCs. Within Auctions, the only aggregate is `Listing` (live-stream-aggregated via DCB, not `[WriteAggregate]`-routed). S5 introduces the first `[WriteAggregate]` shape inside Auctions. Verify at session open that `LiveStreamAggregation<Session>()` in `AuctionsModule.ConfigureMarten` is sufficient for `[WriteAggregate(nameof(X.SessionId))]` resolution on `AttachListingToSession` / `StartSession`, or whether an additional `Schema.For<Session>().Identity(x => x.Id)` registration is required. The pattern is well-established but the in-BC first-use surfaces this question. Halt and consult if codegen fails on `[WriteAggregate]` resolution.

---

## Session sizing notes

- **S5 is moderate** per the milestone doc §9. Three first-use surfaces, fourteen tests, fourteen new files. Below M4-S3 / M4-S4 in scope but above M4-S2.
- **S5b split slot pre-drafted** per milestone doc §9. Trigger conditions:
  - The fan-out handler's idempotency mechanism (OQ2) needs the fallback pre-query shape AND the test infrastructure overhead exceeds the S5 budget; OR
  - The PublishedListings field-shape resolution (OQ1) requires Path B (load the Listing aggregate) and the Flash-listing handling needs additional scaffolding; OR
  - Acceptance criteria approaching or exceeding 14 items at session midpoint.
  Candidate split boundary: §`SessionStartedHandler` and its two fan-out tests → S5b. Base S5 covers Session aggregate (7 scenarios), PublishedListings projection (2 tests), three dispatch tests, plus the `ListingPublishedHandler` Flash guard. S5b absorbs the fan-out handler with cleaner test infrastructure.
- **No further split slots after S5b.** If S5 + S5b both overflow, M4-S7 (retrospective + skills + M4 close) absorbs any tail work or M4 closes with the residual flagged.
- **S5 is the last implementation session before M4-S6 (Listings + ADR 014)**, so any S5 lessons that influence ADR 014 evidence land in the retro's "What M4-S6 should know" section.

## Document history

- **v0.1** (2026-05-19): Authored at the close of M4-S4 per the retro's "What M4-S5 should know" handoff payload. The eight Open Questions are framed by S4's lived discoveries (duplicate-projection pattern, eager cascade timing, multi-handler-within-BC cross-cut) and the milestone doc's S1-resolved M4-D4 disposition. The S5b split slot is named explicitly per milestone doc §9. Narrative 001 Moment 3 is named as the joint-authoritative narrative per CLAUDE.md rule 3.
