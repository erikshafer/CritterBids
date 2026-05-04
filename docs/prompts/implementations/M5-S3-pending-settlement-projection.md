# M5-S3: PendingSettlement Projection + Cross-BC Consumers

**Milestone:** M5 ([Settlement BC](../../milestones/M5-settlement-bc.md))
**Slice:** S3 of 6 (PendingSettlement Projection + ListingPublished Consumer)
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
**Agent:** @PSA
**Estimated scope:** one PR; ~9 files added (prompt + projection doc + handler + 1 handler-tests file + skill amendment + retro + 3 small support files), ~3 files modified (`Program.cs`, `SettlementModule.cs`, `CritterBids.Settlement.csproj`)

---

## Goal

Land the `PendingSettlement` Marten document projection — Settlement's first projection — and the five Wolverine handlers that maintain its lifecycle from cross-BC integration events. The projection is the Settlement BC's local cache of `(SellerId, ReservePrice, BuyItNowPrice, FeePercentage, PublishedAt)` per listing, seeded at publish time, so that when `ListingSold` or `BuyItNowPurchased` arrives in M5-S4 the saga can read those fields without crossing the Settlement / Selling boundary. The slice closes the projection's full lifecycle vocabulary (Pending → Consumed / Expired / Failed) in one pass per the foundation slice's user-confirmed scoping rule; M5-S4 / M5-S5 / M5-S6 do not re-touch the projection.

This is also the first CritterBids projection seeded from a *cross-BC integration event* (`ListingPublished` from Selling) rather than from a same-BC Marten stream. The pattern is structurally distinct from the M3 / M4 same-BC projections — `CatalogListingView`'s sibling-handler shape is closest, but it operates on multiple cross-BC events feeding one view; `PendingSettlement` is one cross-BC event seeding one Settlement-internal cache. The `marten-projections.md` skill file's "Pending: M5-S3 amendment" flag (added at M5-S1) cashes in at this slice's retrospective, with the lived ground from the implementation grounding the pattern documentation.

S3 walks in with the M5-S2 scaffold green: `CritterBids.Settlement` registers cleanly, the saga shell exists, the test fixture excludes Selling / Auctions / Listings handlers via three `IWolverineExtension` exclusions. The Settlement-side Contracts ProjectReference is added in this slice (per M3-S2 → M3-S3 precedent of "scaffold defers Contracts; first consumer adds it"). The saga shell stays untouched — S4 lands its seven-phase implementation.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M5-settlement-bc.md` | Milestone scope — S3 deliverables are §2 (cross-BC wiring table + RabbitMQ queue routes) + §5 (infrastructure) + §7 (slice breakdown S3 row) + §6's `FeePercentage` configuration note |
| `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 1 | The `PendingSettlement` projection's design — schema, lifecycle, race-condition discipline (Option A: Wolverine retry-on-not-found); Settlement's M5-S3 obligation is the projection itself, not the saga-side retry policy (S4 territory) |
| `docs/workshops/003-scenarios.md` §8.1 / §8.4 / §8.5 / §8.6 / §8.7 / §8.8 | Six in-scope projection scenarios; §8.2 / §8.3 deferred (no `ListingRevised` contract producer exists) |
| `docs/skills/marten-projections.md` | The "Pending: M5-S3" flag at file top; the §"Handler-Driven Projections — Tolerant Upsert" pattern is the canonical shape for cross-BC consumers writing one document |
| `docs/skills/integration-messaging.md` | RabbitMQ routing rules; `OutgoingMessages` discipline (relevant for the cross-BC handler isolation footprint) |
| `src/CritterBids.Listings/AuctionStatusHandler.cs` and `tests/CritterBids.Listings.Tests/AuctionStatusHandlerTests.cs` | The lived precedent for a multi-event handler maintaining one projection document. Single static class, multiple `Handle(EventType, IDocumentSession, CancellationToken)` overloads, tolerant-upsert (LoadAsync ?? new) shape per handler, idempotent under at-least-once redelivery |
| `tests/CritterBids.Auctions.Tests/BiddingOpenedConsumerTests.cs` | Direct-handler-invocation test pattern (`await Handler.Handle(message, session); await session.SaveChangesAsync();`). Avoids the `MultipleHandlerBehavior.Separated`-induced `NoHandlerForEndpointException` per memory `project_wolverine_sticky_handler.md`; correct path for projection-handler tests |

---

## In scope

### Projection document

- **`src/CritterBids.Settlement/PendingSettlement.cs`** — `public sealed record PendingSettlement` with the W003 Phase 1 Part 1 schema: `Id` (Guid; same value as ListingId — Marten document primary key), `SellerId`, `ReservePrice` (nullable decimal — no reserve is valid), `BuyItNowPrice` (nullable decimal), `FeePercentage` (decimal), `PublishedAt` (DateTimeOffset), `Status` (`PendingSettlementStatus`). Use `init`-only properties so `with` expressions are the canonical mutation idiom (matches `CatalogListingView`).

  > **Field name:** the projection field is `BuyItNowPrice`; the source `ListingPublished.BuyItNow` contract field renames into `BuyItNowPrice` at projection time. This matches W003 Phase 1 Part 1's schema sketch and the `003-scenarios.md` §8 vocabulary. The rename is carried in the handler's `with` expression — no contract change.

  > **`Id` vs `ListingId`:** Marten requires a property named `Id` for document primary key resolution. Setting `Id = ListingId` makes the document keyable by `LoadAsync<PendingSettlement>(listingId)` per the W003 Phase 1 Part 1 framing. Do not author a separate `ListingId` property; the natural key *is* the document key here.

- **`src/CritterBids.Settlement/PendingSettlementStatus.cs`** — `public enum PendingSettlementStatus { Pending, Consumed, Expired, Failed }`. Four members per W003 Phase 1 Part 7's lifecycle (the `Failed` value was added at the Phase 2 amendment per scenario §8.7's footnote). Either separate file or co-located in `PendingSettlement.cs` — author's call; both files end up with one type each in the alphabetically-adjacent slot.

### Cross-BC consumer handler

- **`src/CritterBids.Settlement/PendingSettlementHandler.cs`** — single `public static class PendingSettlementHandler` with five `Handle(EventType, IDocumentSession, CancellationToken)` overloads, mirroring `Listings/AuctionStatusHandler.cs`:

  | Method | Event | Status transition | Source scenario(s) |
  |---|---|---|---|
  | `Handle(ListingPublished, ...)` | `CritterBids.Contracts.Selling.ListingPublished` | (none) → `Pending`; idempotent on replay | §8.1, §8.8 |
  | `Handle(ListingPassed, ...)` | `CritterBids.Contracts.Auctions.ListingPassed` | `Pending` → `Expired` | §8.4 |
  | `Handle(ListingWithdrawn, ...)` | `CritterBids.Contracts.Selling.ListingWithdrawn` | `Pending` → `Expired` | §8.5 |
  | `Handle(SettlementCompleted, ...)` | `CritterBids.Contracts.Settlement.SettlementCompleted` | `Pending` → `Consumed` | §8.6 |
  | `Handle(PaymentFailed, ...)` | `CritterBids.Contracts.Settlement.PaymentFailed` | `Pending` → `Failed` | §8.7 |

  Tolerant-upsert shape per handler: `LoadAsync<PendingSettlement>(message.ListingId, ct) ?? new PendingSettlement { Id = message.ListingId }`; mutate via record `with`; `session.Store(updated)`. `AutoApplyTransactions()` commits after `Handle` returns. No `OutgoingMessages` returns; no `IMessageBus`. Per `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert" and the M3-S6 `AuctionStatusHandler` precedent.

  **Idempotency for §8.1 / §8.8:** if a row already exists with the same `ListingId` (re-delivered `ListingPublished`), the handler upserts the same field values — Wolverine inbox dedup should prevent the second delivery in production, but the upsert is safe under at-least-once redelivery either way. The handler does NOT short-circuit on the existing row's `Status` — a re-delivered `ListingPublished` against a `Consumed` or `Expired` row should not regress the status to `Pending`. The simplest discipline: only set `Status: Pending` when creating a new row; preserve the existing status when upserting.

  **Idempotency for §8.4 / §8.5 / §8.6 / §8.7:** terminal-status transitions are absorbing. A second `ListingPassed` against an already-`Expired` row reads `Expired`, writes `Expired` — naturally idempotent. Cross-terminal collisions (e.g., `ListingPassed` arriving after `SettlementCompleted` has marked `Consumed`) should not regress; preserve the existing terminal status when transitioning. The simplest discipline: only set the new status when the current status is `Pending`; preserve otherwise.

### Settlement module registration

- **`src/CritterBids.Settlement/SettlementModule.cs`** — extend the existing `ConfigureMarten` block with `opts.Schema.For<PendingSettlement>().DatabaseSchemaName("settlement");`. Per ADR 009, `ConfigureMarten` accumulates contributions across BCs into the primary store. No `Identity` override — Marten's default (property named `Id`) resolves the document key from the `Id` property the record exposes. No `UseNumericRevisions` — `PendingSettlement` is a tolerant-upsert document, not a saga; concurrency under at-least-once redelivery is handled by the upsert shape itself.

- **`src/CritterBids.Settlement/SettlementModule.cs`** — also add `opts.Events.AddEventType<...>()` calls? **No.** Cross-BC integration events arrive via Wolverine's bus, not via Marten's event store. They are routed and consumed; they are not appended to any Marten stream Settlement owns. Marten event-type registration is required only for events Settlement *appends to a Marten stream*. The Settlement-internal events (`SettlementInitiated`, `ReserveCheckCompleted`, etc.) land at S4 with the saga's first emit; M5-S3 adds zero `AddEventType<T>()` calls.

### Project references

- **`src/CritterBids.Settlement/CritterBids.Settlement.csproj`** — add `<ProjectReference Include="..\CritterBids.Contracts\CritterBids.Contracts.csproj" />`. First Settlement-side reference to Contracts; required for `CritterBids.Contracts.Selling.ListingPublished`, `CritterBids.Contracts.Selling.ListingWithdrawn`, `CritterBids.Contracts.Auctions.ListingPassed`, `CritterBids.Contracts.Settlement.SettlementCompleted`, and `CritterBids.Contracts.Settlement.PaymentFailed`. Per the M3-S2 → M3-S3 precedent of deferring the reference to the slice that needs it.

### RabbitMQ routing

Wire the new queues in `src/CritterBids.Api/Program.cs`'s existing RabbitMQ-guarded block, after the existing `listings-auctions-events` block:

- **`settlement-selling-events`** — Settlement listens; Selling publishes `ListingPublished` and `ListingWithdrawn` to it. Two `opts.PublishMessage<T>().ToRabbitQueue("settlement-selling-events")` lines (one per contract type) plus one `opts.ListenToRabbitQueue("settlement-selling-events")` line. `ListingPublished` already has two prior publish routes (`listings-selling-events` for Listings, `auctions-selling-events` for Auctions); this is the third. `ListingWithdrawn` has zero existing publish routes per the M3 milestone doc § 3 deferral and the test-fixture-only synthesizer note in `AuctionsTestFixture.AppendListingWithdrawnAsync`; this slice adds the first real one.

- **`settlement-auctions-events`** — Settlement listens; Auctions publishes `ListingSold`, `BuyItNowPurchased`, **and `ListingPassed`** to it. Three `opts.PublishMessage<T>().ToRabbitQueue("settlement-auctions-events")` lines plus one `opts.ListenToRabbitQueue("settlement-auctions-events")` line. The `ListingPassed` extension is the queue-payload-extension call confirmed at session-start scoping per M5 milestone doc §2's "settlement-auctions-events" entry; the milestone doc lists `ListingSold` and `BuyItNowPurchased` explicitly and the slice extends with `ListingPassed` for the projection's terminal-status §8.4 handler. Note this in the open questions for M5-milestone-doc author awareness; it is a small but real divergence from the milestone doc's wiring table.

  > **Note on the saga's S4 consumption.** The S3 wiring also makes `ListingSold` and `BuyItNowPurchased` available on `settlement-auctions-events` — but the saga that consumes them does not exist yet. Wolverine will discover the queue and listen, but only the `ListingPassed` handler fires on this queue in S3. S4 adds the saga's `Handle(ListingSold)` and `Handle(BuyItNowPurchased)` consumers; the queue topology already accommodates them. This is structurally the right shape: wire the queue once with all its payload types, and let consumer handlers come online slice by slice.

- **No new queue for self-published events.** `SettlementCompleted` and `PaymentFailed` are Settlement BC publishes (S5 / S6 produce them via the saga). Settlement also consumes them locally — `MultipleHandlerBehavior.Separated` lets the local `PendingSettlementHandler.Handle(SettlementCompleted, ...)` fire alongside the cross-BC outbound publish via the in-process bus dispatch. No `settlement-self-events` queue, no self-loop through RabbitMQ.

### Handler integration tests

- **`tests/CritterBids.Settlement.Tests/PendingSettlementHandlerTests.cs`** — one `[Collection(SettlementTestCollection.Name)]` test class implementing `IAsyncLifetime` (calls `_fixture.CleanAllMartenDataAsync()` in `InitializeAsync`); one `[Fact]` per scenario:

  | Test | Scenario | Assertion shape |
  |---|---|---|
  | `ListingPublished_CreatesPendingRow` | §8.1 | After `Handle(ListingPublished)`, row exists with all fields populated and `Status: Pending` |
  | `ListingPublished_Duplicate_IsIdempotent` | §8.8 | Second invocation against a fresh session does not throw and the row's fields remain unchanged |
  | `ListingPassed_TransitionsPendingToExpired` | §8.4 | Seed `Status: Pending`; after `Handle(ListingPassed)`, `Status: Expired`; other fields unchanged |
  | `ListingWithdrawn_TransitionsPendingToExpired` | §8.5 | Seed `Status: Pending`; after `Handle(ListingWithdrawn)`, `Status: Expired`; other fields unchanged |
  | `SettlementCompleted_TransitionsPendingToConsumed` | §8.6 | Seed `Status: Pending`; after `Handle(SettlementCompleted)`, `Status: Consumed`; other fields unchanged |
  | `PaymentFailed_TransitionsPendingToFailed` | §8.7 | Seed `Status: Pending`; after `Handle(PaymentFailed)`, `Status: Failed`; other fields unchanged |

  Direct-handler-invocation pattern per `BiddingOpenedConsumerTests`:

  ```
  await using (var session = _fixture.GetDocumentSession())
  {
      await PendingSettlementHandler.Handle(message, session, default);
      await session.SaveChangesAsync();
  }
  ```

  Each test uses `Guid.CreateVersion7()` for `ListingId` per the per-test unique-IDs strategy — no parallel-test interference. Six new tests; total expected post-S3: 88 + 6 = 94 (the `SettlementModule_BootsClean` smoke test from S2 still passes).

### Skill file amendment

- **`docs/skills/marten-projections.md`** — replace the file-top "Pending: M5-S3 amendment" callout with a new full subsection authoring the cross-BC-event-seeded projection pattern. Place the new subsection in §6 (Handler-Driven Projections — Tolerant Upsert) or as a new sibling section §6.5 — author's call. The pattern's content per the M5-S1 flag note:

  - The seed-on-publish lifecycle (one cross-BC integration event seeds one Settlement-internal cache; subsequent cross-BC events transition the row's status)
  - The load-by-listing-id correlation at workflow-start time (the projection's read at saga-start time per W003 Phase 1 Part 1; this slice authors only the projection, not the saga's load)
  - The Pending → Consumed / Expired / Failed status transition handlers
  - The W003 Phase 1 Part 1 Option A retry-on-projection-lag posture (the saga retries on `PendingSettlement` not found; this discipline lives in S4's saga, not S3's projection — but the projection's lifecycle decisions surface why the retry is necessary)

  Cross-reference the lived files: `src/CritterBids.Settlement/PendingSettlement.cs` and `src/CritterBids.Settlement/PendingSettlementHandler.cs`. Cross-reference the workshop scenarios `003-scenarios.md` §8 and the deep-dive Part 1.

### Session retrospective

- **`docs/retrospectives/M5-S3-pending-settlement-projection-retrospective.md`** — mirrors the M5-S2 / M3-S2 retro shape: Baseline / Items completed / per-item subsections with structural metrics / Test results / Build state / Key learnings / Skill gaps surfaced / Findings against narrative / Verification checklist / What M5-S4 should know / What remains.

---

## Explicitly out of scope

- **The Settlement saga's seven-phase implementation.** The saga shell stays an empty `: Wolverine.Saga` class. M5-S4 territory.
- **The `BidderCreditView` projection.** M5-S5 territory per W003 Phase 1 Part 7.
- **Settlement-internal event types.** `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated` stay unauthored. M5-S4 lands them.
- **The saga's `PendingSettlement`-not-found retry policy.** The M5 §5 reference to "retry-on-projection-lag" is the *saga's* responsibility, not the projection's. M5-S4 (which lands the saga) wires the Wolverine retry policy. M5-S3 authors only the projection that the saga will eventually load.
- **§8.2 — `FeePercentage` immutable after creation under `ListingRevised`.** Deferred: `ListingRevised` is unauthored as a contract. When it ships (post-M5 Selling-side work), §8.2 becomes implementable; M5-S3 does not pre-empt the contract.
- **§8.3 — `ListingRevised` updates mutable fields.** Same deferral as §8.2.
- **`UseNumericRevisions`** on `PendingSettlement`. Tolerant-upsert documents do not need optimistic concurrency at this scale; at-least-once redelivery semantics are handled by the upsert shape itself. If contention symptoms surface in a future slice, revisit then.
- **The integration-out events `SellerPayoutIssued` consumer.** `SellerPayoutIssued` is published in S5 / S6 to Relay (post-M5) and does not transition `PendingSettlement` per §8 — Relay broadcasts the seller-side notification directly. No handler for `SellerPayoutIssued` lands in S3.
- **Selling-side publish-route additions for events Selling does not yet publish.** `ListingWithdrawn` is currently fixture-synthesized only per the M3 milestone doc § 3 deferral. M5-S3 adds the publish route in `Program.cs` so when Selling's real publisher lands (post-M5), the route is already in place. The publisher itself stays Selling's deferral.
- **Listings-side `CatalogListingView.Status = "Settled"` extension.** M5-S6 territory.
- **Contracts-project edits.** All five contract types this slice consumes already exist (`ListingPublished`, `ListingWithdrawn`, `ListingPassed` from Selling / Auctions; `SettlementCompleted`, `PaymentFailed` authored at M5-S1). No Contracts edits.
- **W003 / narrative 002 edits.** The slice's design is rendered exactly as W003 specifies; no drift to surface. The four-lane findings discipline applies if implementation surprise surfaces drift, but the expected outcome is zero findings against narrative 002 in S3 (the projection is below the saga-Moment grain narrative 002 dramatises).
- **`SettlementSaga` shell edits.** The shell stays empty. S3 adds files alongside it; it does not modify it.
- **HTTP endpoints.** Settlement remains backend-only through M5 per milestone § 3.
- **`[AllowAnonymous]` posture, auth changes.** Unchanged through M5 per `CLAUDE.md`.

---

## Conventions to pin or follow

- **Tolerant-upsert shape from `marten-projections.md` §6.** `LoadAsync<T>(id, ct) ?? new T { Id = id }` then `with` mutation then `session.Store(updated)`. No explicit `SaveChangesAsync` in the handler — `AutoApplyTransactions()` commits.
- **Status preservation under terminal-state collisions.** Only set the new status when the current status is `Pending`; preserve otherwise. Applies across all four terminal-status handlers (§8.4 / §8.5 / §8.6 / §8.7). Same discipline applied to the §8.1 / §8.8 idempotency: do not regress `Status: Consumed` / `Expired` / `Failed` to `Pending` on a re-delivered `ListingPublished`.
- **`init`-only properties on `PendingSettlement`.** Matches `CatalogListingView`'s record shape. `with` expressions are the canonical mutation idiom.
- **`Id = ListingId` Marten convention.** The document's primary key is the `ListingId` value; `Id` is the property name Marten resolves. No separate `ListingId` property.
- **Per-test unique IDs.** `Guid.CreateVersion7()` for `ListingId` and `SellerId` in each test. No fixture-level seeded IDs. Per `critter-stack-testing-patterns.md` §Test Parallelization Strategy — unique IDs are the project default for new tests.
- **Direct-handler-invocation tests.** Per `BiddingOpenedConsumerTests`. Avoids the `MultipleHandlerBehavior.Separated`-induced `NoHandlerForEndpointException` from `Host.InvokeMessageAndWaitAsync` per memory `project_wolverine_sticky_handler.md`. Each test opens a session, invokes the static `Handle` method, calls `SaveChangesAsync`, then opens a fresh session for assertions.
- **Schema name `settlement`.** Same schema name as the saga document from S2; `PendingSettlement` shares the schema (one schema per BC, multiple document types within).
- **No `Event` suffix on event type names.** Applies to the Settlement-internal events at S4; not relevant to S3 since S3 authors no events.
- **Em-dash hygiene** is external-prose-only per memory `feedback_em_dash_scope.md`. The prompt, retro, and skill amendment may use em-dashes freely.

---

## Acceptance criteria

- [ ] `src/CritterBids.Settlement/PendingSettlement.cs` defines `public sealed record PendingSettlement` with `init`-only properties `Id` (Guid), `SellerId` (Guid), `ReservePrice` (decimal?), `BuyItNowPrice` (decimal?), `FeePercentage` (decimal), `PublishedAt` (DateTimeOffset), `Status` (`PendingSettlementStatus`).
- [ ] `src/CritterBids.Settlement/PendingSettlementStatus.cs` (or co-located in `PendingSettlement.cs`) defines `public enum PendingSettlementStatus { Pending, Consumed, Expired, Failed }`.
- [ ] `src/CritterBids.Settlement/PendingSettlementHandler.cs` defines `public static class PendingSettlementHandler` with five `Handle(EventType, IDocumentSession, CancellationToken)` overloads — one each for `ListingPublished`, `ListingPassed`, `ListingWithdrawn`, `SettlementCompleted`, `PaymentFailed`. Tolerant-upsert shape per handler.
- [ ] `src/CritterBids.Settlement/CritterBids.Settlement.csproj` has a `<ProjectReference>` to `CritterBids.Contracts.csproj`.
- [ ] `src/CritterBids.Settlement/SettlementModule.cs` `ConfigureMarten` block adds `opts.Schema.For<PendingSettlement>().DatabaseSchemaName("settlement");` after the existing `SettlementSaga` registration. Zero `opts.Events.AddEventType<T>()` calls remain (cross-BC events are bus-routed, not Marten-stored).
- [ ] `src/CritterBids.Api/Program.cs` RabbitMQ block adds: two `opts.PublishMessage<T>().ToRabbitQueue("settlement-selling-events")` lines (`ListingPublished`, `ListingWithdrawn`) plus `opts.ListenToRabbitQueue("settlement-selling-events")`; three `opts.PublishMessage<T>().ToRabbitQueue("settlement-auctions-events")` lines (`ListingSold`, `BuyItNowPurchased`, `ListingPassed`) plus `opts.ListenToRabbitQueue("settlement-auctions-events")`.
- [ ] `Program.cs` does not gain a `settlement-self-events` queue or any other Settlement-self-loop wiring.
- [ ] `tests/CritterBids.Settlement.Tests/PendingSettlementHandlerTests.cs` exists; six `[Fact]` methods cover §8.1 / §8.4 / §8.5 / §8.6 / §8.7 / §8.8 via direct handler invocation; each test opens a session, invokes the static `Handle` method, calls `SaveChangesAsync`, then opens a fresh session for assertions; class implements `IAsyncLifetime` with `CleanAllMartenDataAsync` in `InitializeAsync`.
- [ ] `docs/skills/marten-projections.md` replaces the "Pending: M5-S3 amendment" callout with a new subsection (or replaces the callout's content) authoring the cross-BC-event-seeded projection pattern, cross-referenced to `PendingSettlement.cs`, `PendingSettlementHandler.cs`, and `003-scenarios.md` §8.
- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [ ] `dotnet test CritterBids.slnx` — all green; 88 baseline tests still pass; six new Settlement projection tests pass; total 94.
- [ ] `docs/retrospectives/M5-S3-pending-settlement-projection-retrospective.md` exists; mirrors the M5-S2 retro shape; records the handler-shape choice, the queue-payload-extension decision, the §8.2 / §8.3 deferral rationale, and a "what M5-S4 should know" note.

---

## Open questions

- **`ListingPassed` on `settlement-auctions-events` is a queue-payload extension beyond the M5 milestone doc's wiring table.** §2 of `M5-settlement-bc.md` lists `ListingSold` and `BuyItNowPurchased` for the queue; M5-S3 extends with `ListingPassed` because §8.4's terminal-status handler needs the event delivered to Settlement. Confirmed at session-start scoping. Surface in the retrospective so the milestone doc can be amended in a future doc-cleanup pass; the milestone doc's table is silent on `ListingPassed` because the projection's terminal-status handlers were not enumerated at milestone-doc authoring time, not because they were intentionally excluded.

- **Status preservation under terminal collisions vs simple last-write-wins.** The prompt specifies "only set the new status when the current status is `Pending`; preserve otherwise" as the simplest correct discipline. Last-write-wins (no preservation guard) would also work for the §8 scenario set as authored, since each scenario starts from `Pending`. The preservation guard adds defensive correctness for unobserved race conditions (e.g., `ListingPassed` arriving on a `Consumed` row would otherwise regress to `Expired`, which is wrong semantically). If the implementation surfaces a case where last-write-wins is more correct (e.g., a deliberate over-write of an erroneous prior terminal state), revisit and document; the expected outcome at S3 is that the preservation guard suffices.

- **`PendingSettlement` Marten Id property name.** Marten resolves `Id` by convention. The W003 schema sketch uses `ListingId` as the primary key. Two paths: (a) author `Id` (Guid) and assign `Id = listingId` at construction; (b) author `ListingId` and explicitly call `opts.Schema.For<PendingSettlement>().Identity(x => x.ListingId)`. The prompt leans (a) — simpler, no schema override; matches the M3 / M4 BC document precedents. Confirm at session start; flag in retro if the discovery surfaces a Marten-version-specific reason to prefer (b).

- **Skill-amendment placement.** The "Pending: M5-S3 amendment" callout sits at the file top of `marten-projections.md`. Two paths: (i) replace the callout's content with the full pattern documentation, leaving the location at file top; (ii) move the pattern to §6 or a new §6.5 section, replacing the callout with a one-line pointer to the new section. Lean: (ii) — keeps the file's structure consistent with how other patterns are documented; the file-top callout was a deliberate flag, not a permanent home. Confirm at session start.

---

## Commit sequence

Three commits, in this order:

1. `feat(settlement): author PendingSettlement projection document and PendingSettlementHandler with five cross-BC consumers`
2. `feat(settlement): wire settlement-selling-events and settlement-auctions-events RabbitMQ routes; integration tests for the six §8 scenarios`
3. `docs(settlement): cash in marten-projections.md M5-S3 flag with cross-BC-event-seeded projection pattern; write M5-S3 retrospective`

The projection / handler / module wiring lands in commit 1 as a self-contained set of new and edited files in `src/CritterBids.Settlement/`. Commit 2 lands the integration surface — `Program.cs` RabbitMQ wiring plus the six handler tests that exercise the lifecycle scenarios. Commit 3 cashes in the skill flag and writes the retro; both are docs-grade and naturally bundle.
