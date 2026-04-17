# M3-S3: BiddingOpened Consumer (Selling → Auctions)

**Milestone:** M3 — Auctions BC
**Session:** S3 of 7
**Prompt file:** `docs/prompts/M3-S3-bidding-opened-consumer.md`
**Baseline:** 45 tests passing · `dotnet build` 0 errors, 0 warnings · M3-S2 complete

---

## Goal

Wire the first cross-BC consumer in Auctions. A `ListingPublished` event produced by Selling lands on a new `auctions-selling-events` RabbitMQ queue, a Wolverine handler in `CritterBids.Auctions` consumes it, and the handler produces `CritterBids.Contracts.Auctions.BiddingOpened` by starting a new `Listing` event stream. Re-delivery of the same `ListingPublished` must not produce a duplicate `BiddingOpened`. Nothing else moves — no DCB, no saga, no second queue, no Listings catalog work, no remaining event type registrations, no `ScaffoldPlaceholder` removal.

This session is the first time `CritterBids.Auctions` references `CritterBids.Contracts`, the first `opts.Events.AddEventType<T>()` call in `AuctionsModule`, and the first integration path end-to-end from Selling's publisher through RabbitMQ into an Auctions stream. S1 locked the `BiddingOpened` payload per W002-9; S3 populates it without redefining it. If the S1 stub shape does not give the handler every field it needs, stop and flag — do not edit Contracts in-session.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M3-auctions-bc.md` | Milestone scope — S3 deliverables in §9, RabbitMQ routing in §5, integration contracts map in §2 and the Appendix |
| `docs/retrospectives/M3-S1-auctions-foundation-decisions-retrospective.md` | W002-9 resolution (final `BiddingOpened` payload shape); W002-7 `BidRejected` placement (reference only — not exercised in S3) |
| `docs/retrospectives/M3-S2-auctions-bc-scaffold-retrospective.md` | "What M3-S3 should know" — ScaffoldPlaceholder stays; Contracts ProjectReference lands here; AuctionsTestFixture shape; Marten-8 validator landmine resolution in place |
| `docs/skills/integration-messaging.md` | Queue naming `<consumer>-<publisher>-<category>`, L2 payload discipline, publish/listen registration shape |
| `docs/skills/wolverine-message-handlers.md` | Handler shape, `IDocumentSession.Events.StartStream<T>` pattern, in-handler idempotency for at-least-once delivery |
| `docs/skills/critter-stack-testing-patterns.md` | Consumer integration-test shape; Wolverine tracking helpers (`ExecuteAndWaitAsync`, `TrackedSession`) per `SellingTestFixture` |
| `src/CritterBids.Api/Program.cs` | Edit site — existing RabbitMQ-guarded publish/listen block; `selling-participants-events` is the shape to mirror |

---

## In scope

- **Add `<ProjectReference>` from `CritterBids.Auctions.csproj` to `CritterBids.Contracts.csproj`.** First reference from Auctions to Contracts; placed alphabetically as the single reference the project has. The scaffold deferred this; S3 is the session that needs it.

- **Register `BiddingOpened` in `AuctionsModule.AddAuctionsModule`.** Single new line inside the existing `ConfigureMarten` callback: `opts.Events.AddEventType<BiddingOpened>()`. No other event types register in S3. `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved`, `BuyItNowPurchased`, `BiddingClosed`, `ListingSold`, and `ListingPassed` stay unregistered until their first-use session (S4 for the bid-and-friends batch; S5 for the closing-outcome batch).

- **Author the Wolverine message handler.** New file under `src/CritterBids.Auctions/` (name per `wolverine-message-handlers.md` conventions). Consumes `CritterBids.Contracts.Selling.ListingPublished`, produces `CritterBids.Contracts.Auctions.BiddingOpened`, starts a new `Listing` event stream via `IDocumentSession.Events.StartStream<Listing>`. Stream ID is `ListingPublished.ListingId` (upstream UUID v7 flows through — not regenerated). Payload for `BiddingOpened` is populated from `ListingPublished` fields per the W002-9 S1 resolution; no invented defaults.

- **Make the handler idempotent under duplicate delivery.** At-least-once is the contract; the handler must absorb re-delivery of the same `ListingPublished` without appending a second `BiddingOpened`. Approach follows `wolverine-message-handlers.md`. See Open Questions below for the stop-and-flag condition if neither documented approach fits cleanly.

- **Wire the `auctions-selling-events` queue in `src/CritterBids.Api/Program.cs`.** Two edits inside the existing RabbitMQ-guarded block, mirroring the placement and shape of `selling-participants-events`:
  1. Add the `opts.PublishMessage<ListingPublished>().ToRabbitQueue("auctions-selling-events")` line to the publish block.
  2. Add the `opts.ListenToRabbitQueue("auctions-selling-events")` line to the listen block.

- **Two integration tests in `tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs`:**
  1. `ListingPublished_FromSelling_ProducesBiddingOpened` — dispatches a `ListingPublished` through Wolverine; asserts exactly one `BiddingOpened` is present on the `Listing` stream with payload matching the W002-9 resolved shape.
  2. `ListingPublished_Duplicate_IsIdempotent` — dispatches the same `ListingPublished` twice; asserts exactly one `BiddingOpened` on the stream, and no handler-level exception propagates.
  Both tests are integration level (Testcontainers Postgres + Alba) against `AuctionsTestFixture`. If the tests need Wolverine tracking helpers, add them to `AuctionsTestFixture` following the `SellingTestFixture` shape — do not invent a third fixture-helper pattern.

- **Session retrospective** at `docs/retrospectives/M3-S3-bidding-opened-consumer-retrospective.md`. **Gate: the retrospective commit does not land until `dotnet build` is clean and `dotnet test` is fully green.** If tests are red at session close, the retrospective records the red state and what blocks — not a premature "done."

---

## Explicitly out of scope

- **`ScaffoldPlaceholder` removal.** Stays until S4, paired with the first real `Apply(BiddingOpened)`. Removing it in S3 recreates the Marten-8 projection validator blocker M3-S2 resolved.
- **Any event type registration beyond `BiddingOpened`.** The bid-and-friends batch (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowOptionRemoved`) lands in S4. The closing-outcome batch (`BuyItNowPurchased`, `BiddingClosed`, `ListingSold`, `ListingPassed`) lands in S5.
- **DCB artifacts.** No `BidConsistencyState`, no `[BoundaryModel]`, no `EventTagQuery` — S4.
- **Saga artifacts.** No `AuctionClosingSaga`, no scheduled messages, no cancel-and-reschedule — S5.
- **`listings-auctions-events` queue** (Auctions publishes, Listings consumes) — S5 or S6.
- **`CatalogListingView` auction-status field additions** — S6.
- **Any HTTP endpoints.** No `PlaceBid`, no `BuyNow`, no Auctions controller or `WolverineHttp` map calls.
- **`Listing` aggregate behavior.** The empty shell plus the `ScaffoldPlaceholder` no-op is all S3 touches on the aggregate. Bidding-state fields, real `Apply()` methods, and DCB boundary wiring belong to S4.
- **Contracts project edits.** The S1 `BiddingOpened` stub and the other eight Auctions contract stubs are immutable in S3. If the stub is missing a field, stop and flag — do not add fields mid-session.
- **Listings BC edits** of any kind.
- **ADR, skill, or workshop doc edits.** Skill gaps surfaced in-session (for example any gap in `wolverine-message-handlers.md` idempotency coverage, or the HTTP-vs-handler discovery distinction flagged in the M3-S2 retro) are recorded in the retrospective for the next skills-maintenance pass — never edited in-session.
- **Gate 4 re-evaluation.** Event row ID strategy stays deferred per the S1 disposition; trigger is the M3-S4 prompt draft, not this session.
- **Auth.** `[AllowAnonymous]` stance through M5 unchanged; S3 adds no endpoints.

---

## Conventions to pin or follow

- **Queue name `auctions-selling-events`.** `<consumer>-<publisher>-<category>` per `integration-messaging.md`. Same shape as `selling-participants-events` (M2) and `listings-selling-events` (M2).
- **Publish/listen routing in `Program.cs`, not in the BC module.** Threading `WolverineOptions` into module methods remains deferred (M2 retro, M3 milestone §8). S3 follows precedent.
- **L2 payload discipline.** `BiddingOpened` carries every field future consumers (saga, catalog, Relay, Settlement) need at first commit. The S1 stub was authored under this rule; S3 populates it whole, no trimming.
- **`AddEventType<T>()` at first use.** `BiddingOpened` registers here because it is produced here. The M2 learning about silent `AggregateStreamAsync<T>` null returns is the reason the rest of the Auctions event vocabulary stays unregistered until its producing session.
- **UUID v7 stream IDs.** `Guid.CreateVersion7()` elsewhere in the stack — but here the stream ID is `ListingPublished.ListingId` flowing through from Selling, not a new generation. Do not regenerate; the upstream ID is the listing's identity across BCs.
- **Idempotency at the consumer.** Wolverine's transactional inbox is not configured for exactly-once (M2 decision stands). The handler body absorbs duplicates. Approach per `wolverine-message-handlers.md`.
- **`AuctionsTestFixture` is the fixture.** If tracking helpers are needed for the two consumer tests, they land in the fixture in the `SellingTestFixture` shape — no new fixture class, no mid-session rewrite.

---

## Acceptance criteria

- [ ] `src/CritterBids.Auctions/CritterBids.Auctions.csproj` contains a `<ProjectReference>` to `CritterBids.Contracts.csproj`.
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — `opts.Events.AddEventType<BiddingOpened>()` present inside the existing `ConfigureMarten` callback; zero other `AddEventType<T>` calls.
- [ ] A new Wolverine handler file exists under `src/CritterBids.Auctions/` that consumes `CritterBids.Contracts.Selling.ListingPublished` and starts a `Listing` event stream appending `CritterBids.Contracts.Auctions.BiddingOpened`.
- [ ] Handler uses `ListingPublished.ListingId` as the stream ID; does not regenerate.
- [ ] Handler populates `BiddingOpened` from `ListingPublished` per the W002-9 resolved payload shape; no hardcoded defaults for W002-9-owned fields.
- [ ] `src/CritterBids.Api/Program.cs` — `opts.PublishMessage<ListingPublished>().ToRabbitQueue("auctions-selling-events")` present in the publish block; `opts.ListenToRabbitQueue("auctions-selling-events")` present in the listen block; both inside the existing RabbitMQ-guarded block; placed alongside the existing `selling-participants-events` routing.
- [ ] `Program.cs` contains zero `listings-auctions-events` lines and zero publish lines for any other Auctions-produced event.
- [ ] `src/CritterBids.Auctions/Listing.cs` — `ScaffoldPlaceholder` record and its paired no-op `Apply(ScaffoldPlaceholder)` method still present and unchanged.
- [ ] `src/CritterBids.Contracts/Auctions/BiddingOpened.cs` unchanged from the S1 stub.
- [ ] `tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs` exists with the two specified tests, both integration-level, both green.
- [ ] If `AuctionsTestFixture` gained tracking helpers, they mirror `SellingTestFixture` shape and are referenced by the new tests.
- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [ ] `dotnet test CritterBids.slnx` — all green; 45-test baseline preserved; new total is 47 (45 + 2).
- [ ] `docs/retrospectives/M3-S3-bidding-opened-consumer-retrospective.md` exists and records: the Contracts `ProjectReference` diff, the `AddEventType<BiddingOpened>()` placement, the handler idempotency approach chosen with rationale, the `Program.cs` routing diff, any `AuctionsTestFixture` helper additions, any skill gap surfaced, and a "what M3-S4 should know" note covering ScaffoldPlaceholder removal timing and the first real `Apply(BiddingOpened)` placement.

---

## Open questions

- **Idempotency strategy.** The handler must tolerate duplicate `ListingPublished` without producing a second `BiddingOpened`. `wolverine-message-handlers.md` documents the canonical approaches (stream-state check ahead of `StartStream`, append-only-if-not-present, inbox-level message deduplication). Pick the shape the skill file endorses for new-stream handlers of this kind. If the skill does not clearly specify for the "first event on a new stream" case, default to a stream-state check: `FetchStreamStateAsync(listingId)` returning null means append; non-null means no-op and log. If neither the skill-documented nor the default shape passes the `ListingPublished_Duplicate_IsIdempotent` test cleanly, stop and flag rather than shipping a third pattern — this is a repo-first idempotency decision and should not get made silently.

- **W002-9 payload parity with `ListingPublished`.** S1 resolved `BiddingOpened` to carry full extended-bidding config (scheduled close, reserve threshold, BIN price, trigger window, max duration, etc.). The handler sources these from `ListingPublished`. If any field `BiddingOpened` requires is absent from `ListingPublished`'s shape, S1 underspecified the consumer contract and S3 cannot silently fix it. Stop and flag. Do not edit either contract in-session and do not invent defaults.

- **Stream-type / stream-ID collision.** Stream aggregate is `Listing` (the empty shell from S2). Stream ID is `ListingPublished.ListingId`. No pre-existing Auctions streams exist, so a collision on first use would indicate either the ScaffoldPlaceholder survived an earlier session at runtime or the test fixture is not cleaning between runs. Either case is a stop-and-flag — do not add collision-retry logic.

- **`AuctionsTestFixture` tracking helpers.** If the two consumer tests need Wolverine tracking (to await message handling before asserting), add the helpers to the fixture in the `SellingTestFixture` shape per the M3-S2 retro (item 5 of "What M3-S3 should know"). If the existing Listings-minimal shape is sufficient (e.g. the tests can call `IMessageBus.InvokeAsync` and then query directly), use it as-is. The choice is mirror-Selling vs mirror-Listings-minimal — not design-something-new.

---

## Commit sequence

Three commits, in this order. The retrospective is gated on green `dotnet test`.

1. `feat(auctions): add Contracts reference; register BiddingOpened; consume ListingPublished and start Listing stream`
2. `feat(auctions): wire auctions-selling-events RabbitMQ routing; add BiddingOpenedConsumer integration tests`
3. `docs: write M3-S3 retrospective`

Commit 1 adds the Auctions-internal changes — the Contracts reference, the event type registration, and the handler itself. The project compiles and the 45-test baseline still passes. Commit 2 adds the Program.cs RabbitMQ routing and the two integration tests that now exercise the end-to-end path. Commit 3 is the retrospective and lands only after commit 2's tests are green.
