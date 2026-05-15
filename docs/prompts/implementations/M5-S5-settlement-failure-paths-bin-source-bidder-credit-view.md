# M5-S5: Settlement Failure Paths + BIN Source + BidderCreditView

**Milestone:** M5 ([Settlement BC](../../milestones/M5-settlement-bc.md))
**Slice:** S5 of 6 (Settlement Workflow Failure Paths + BIN Source + BidderCreditView projection)
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
**Agent:** @PSA
**Estimated scope:** one PR; ~12 files added (prompt + `FailSettlement` command + `BidderCreditView` document + `BidderCreditViewHandler` + `ParticipantSessionStarted` contract promotion + failure-path tests file + BIN-source tests file + BidderCreditView tests file + retro + three doc-update touches), ~5 files modified (`SettlementSaga.cs`, `StartSettlementSagaHandler.cs`, `SettlementModule.cs`, `Program.cs` for the new Participants→Settlement queue route, `Participants/Features/StartParticipantSession/ParticipantSessionStarted.cs` to type-forward to the contracts-side promotion)

---

## Goal

Close the three remaining behavioral gaps in the Settlement saga before M5-S6 wires the cross-BC publish routes. **Workstream A — failure paths:** replace the M5-S4 `NotImplementedException` stub in `SettlementSaga.Handle(CheckReserve)`'s reserve-not-met branch with a real `FailSettlement` self-send command + `Handle(FailSettlement)` handler that appends `PaymentFailed` to the financial event stream and `MarkCompleted()`s in `Failed` state. The §3.2 decider scenario and §9.3 reserve-not-met end-to-end scenario both go green; seven invalid-transition tests (§1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2) cover the state-guard contract M5-S4 wired structurally but never tested directly. **Workstream B — BIN source:** add `StartSettlementSagaHandler.Handle(BuyItNowPurchased, ...)` overload that constructs the saga at `Status: ReserveChecked` (skipping the reserve-check phase per W003 Phase 1 Part 5's evolver branching), and the §9.2 BIN end-to-end test asserts a five-event stream where the absence of `ReserveCheckCompleted` is the canonical audit signal for "this was a BIN settlement." **Workstream C — `BidderCreditView` projection:** the third Settlement-side document type, seeded from a Participants-side integration event and updated on the Settlement-internal `WinnerCharged` event, surfacing remaining credit for Relay's `SettlementCompleted` broadcast (post-M5 consumer) and any future bidder-balance endpoint.

A pre-step unblocks Workstream C: `ParticipantSessionStarted` is currently a BC-internal record at `src/CritterBids.Participants/Features/StartParticipantSession/ParticipantSessionStarted.cs` and is **not** in the `CritterBids.Contracts` project. W003 Phase 1 Part 7's "Initialise on `ParticipantSessionStarted`" call cannot be honored from Settlement without first promoting the event to a contracts-side type. The promotion is narrow — a new `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` record with the same five-field payload, plus updates to the Participants BC's emit site (the `StartParticipantSession` slice handler) to publish the contracts-side type. The Participants-side internal record can either type-forward, be deleted in favor of the contract directly, or remain as a parallel internal type depending on what the existing emit sites do. The slice resolves the question on first read; defaulting to "delete the internal type and use the contracts type directly" if Participants has no other consumer of the internal shape.

S5 walks in with the M5-S4 surface green: 96 tests passing, the saga's six-event bidding-source happy path live, the deterministic UUID v5 SettlementId helper authored, the `wolverine-sagas.md` M5-S4 multi-phase pattern section in place. The `NotImplementedException("Reserve-not-met failure path lands at M5-S5")` stub at `SettlementSaga.Handle(CheckReserve)`'s reserve-not-met branch is the failure-workstream entry point. The `SettlementSource.BuyItNow` enum value exists but no consumer; the BIN-source Start handler overload is the BIN-workstream entry point.

The slice **does not** wire the `PaymentFailed` outbound RabbitMQ publish route — Operations BC (the consumer) is post-M5, and per M5-S4 retro's "what M5-S5 should know" item 6, the publish route can defer to post-M5 without consumer-side coupling. The Settlement-side emission via `OutgoingMessages` is fully in scope and reaches the local-in-process `PendingSettlementHandler.Handle(PaymentFailed, ...)` consumer (M5-S3-authored, currently uncovered by an end-to-end test — §9.3 closes that). `SettlementCompleted` and `SellerPayoutIssued` outbound publish routes are M5-S6 territory.

This slice also surfaces and forwards **two M5-close-blocking documentation items** to S6 / M5 retro: (1) ADR 007 Gate 4's status — its trigger ("M5-S1 Settlement BC, owned by Erik") fired with PR #25 but the ADR's status line still reads "Re-Deferred (M4-S1)"; either Settlement's event-row-ID strategy is now closed by lived-fact (engine default; no v7 row IDs) and ADR 007 needs the amendment, or the gate re-defers again with a new trigger. (2) The `BidderCreditView`-from-`ParticipantSessionStarted` initialization path is W003 Phase 1 Part 7's design, but if S5 defers the Participants contract promotion (Workstream C's pre-step) the projection initializes lazily on `WinnerCharged` instead — a `document-as-intentional` workshop-update finding for the M5 retro.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M5-settlement-bc.md` §7 (Slice Breakdown) + §2 (Cross-BC wiring) | S5 deliverables — the slice row reads "failure-branch scenarios from §3.2 (PaymentFailed); state-guard scenarios from §1.3 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2"; the cross-BC wiring table identifies the Participants→Settlement seam (currently unwired) |
| `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 5 (BIN path) + Part 7 (BidderCreditView) | Part 5 specifies BIN evolver branching to `ReserveChecked(WasMet: true)` initial state; Part 7 specifies BidderCreditView shape `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)`, lifecycle (init on `ParticipantSessionStarted`, update on `WinnerCharged`, idempotency via `LastChargedSettlementId` equality), consumers, and the no-DCB-consumer rule per Part 4 Option A |
| `docs/workshops/003-scenarios.md` §1.2 / §1.3 / §2.4 / §3.2 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 / §9.2 / §9.3 | The 11 scenarios S5 implements: §1.2 BIN initiation decider; §9.2 BIN end-to-end five-event stream; §3.2 PaymentFailed decider; §9.3 reserve-not-met end-to-end; §1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 invalid-transition guards |
| `docs/retrospectives/M5-S4-settlement-saga-happy-path-retrospective.md` §"What M5-S5 should know" (lines 343–366) | The 10-item handoff list authored at S4 close; this prompt cross-references the items rather than restating them |
| `docs/decisions/019-settlement-workflow-hosting.md` §Revisit trigger + §Consequences | Saga shape preserved; pure-function helper extraction (Option C) deferred unless M5-S5 surfaces concrete pain; the §3.2 PaymentFailed branch is one of the natural places for a pure-function decider helper if extraction earns its keep — flag in retro |
| `docs/skills/wolverine-sagas.md` "Multi-Phase Sagas with Self-Sent Continuation Commands" section (M5-S4 amendment) | The pattern reference; S5 extends the saga along the established shape, no skill-file edits unless a new sub-pattern surfaces |
| `docs/skills/marten-projections.md` "Handler-Driven Projections — Tolerant Upsert" (M3-S6 + M5-S3 amendment site) | The shape for `BidderCreditViewHandler` — mirrors `PendingSettlementHandler` (M5-S3) and `AuctionStatusHandler` (M3-S6) |
| `src/CritterBids.Settlement/SettlementSaga.cs` + `src/CritterBids.Settlement/StartSettlementSagaHandler.cs` + `src/CritterBids.Settlement/PendingSettlementHandler.cs` | The lived M5-S4 surface — failure-branch entry point, the start handler that S5 adds the BIN overload to, the tolerant-upsert handler shape `BidderCreditViewHandler` mirrors |
| `src/CritterBids.Participants/Features/StartParticipantSession/ParticipantSessionStarted.cs` + `StartParticipantSession.cs` | The Participants-side internal event that Workstream C's pre-step promotes to the contracts project; the emit site that must switch to the contracts type |
| `src/CritterBids.Contracts/Auctions/BuyItNowPurchased.cs` + `src/CritterBids.Contracts/Settlement/PaymentFailed.cs` | The two existing contracts S5 consumes / emits without modification |

---

## In scope

### Pre-step — promote `ParticipantSessionStarted` to a cross-BC contract

The contracts-side promotion is the unblock for Workstream C's seed path. Three files touch:

- **`src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` (new)** — `public sealed record ParticipantSessionStarted(Guid ParticipantId, string DisplayName, string BidderId, decimal CreditCeiling, DateTimeOffset StartedAt)` with triple-slash XML docstring naming the BC origin (Participants BC), the consumers (Settlement BC `BidderCreditViewHandler`, future Relay broadcast handler when Relay ships), the field rationale (BidderId carries the Participants-side display correlation; CreditCeiling is the initial RemainingCredit value for `BidderCreditView`), and the M5-S5 promotion provenance note.
- **`src/CritterBids.Participants/Features/StartParticipantSession/ParticipantSessionStarted.cs` (modified or removed)** — if Participants emits this type from `StartParticipantSession.cs`'s handler, change the emit to the contracts-side type. If no internal consumer remains, delete the internal file; otherwise leave it as a parallel type with the same payload and document the parallel. Confirm with `Grep` for `ParticipantSessionStarted` references at session start.
- **`src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs` (modified)** — the emit site switches from `CritterBids.Participants.Features.StartParticipantSession.ParticipantSessionStarted` to `CritterBids.Contracts.Participants.ParticipantSessionStarted`. Verify any `[WriteAggregate]` decoration or event-type registration in `ParticipantsModule.cs` carries the contracts-side type.
- **`src/CritterBids.Api/Program.cs` (modified)** — add `participants` → `settlement-participants-events` queue route publishing `ParticipantSessionStarted` from Participants and listening from Settlement. Mirrors the existing `settlement-auctions-events` and `listings-selling-events` route shapes per M5-S3 precedent.

The promotion is conservative — payload identical, namespace migrated. The M5 milestone doc §2 cross-BC wiring table does not list Participants→Settlement as a queue, so the milestone doc needs a one-line update (the table is the doc's wiring catalog; S5 extending it is a `workshop-update` lane finding per the four-lane discipline, recorded in the retro).

### Workstream A — Failure path

- **`src/CritterBids.Settlement/FailSettlement.cs` (new)** — `public sealed record FailSettlement(Guid SettlementId, string Reason)`. The two-field self-send command pattern parallels the five M5-S4 commands but carries the failure-reason classification string. Reason field values for M5 scope: `"ReserveNotMet"` only (matching `PaymentFailed.Reason`'s M5 single-value posture per the contract's field rationale docstring). Triple-slash docstring names §3.2 / §9.3 scenarios.
- **`src/CritterBids.Settlement/SettlementSaga.cs` (modified)** —
  - `Handle(CheckReserve, IDocumentSession)`: replace the `throw new NotImplementedException("Reserve-not-met failure path lands at M5-S5")` stub with `return new OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }`. The `ReserveCheckCompleted` event with `WasMet: false` is still appended to the stream (it was in S4); the not-met branch now self-sends `FailSettlement` instead of throwing.
  - Add `public OutgoingMessages Handle([SagaIdentityFrom(nameof(FailSettlement.SettlementId))] FailSettlement command, IDocumentSession session)`:
    - State guard: `if (Status is SettlementStatus.Completed or SettlementStatus.Failed) throw new InvalidSettlementTransitionException(...)`. The handler is reachable from any non-terminal status (the §3.2 decider scenario shows the reserve-not-met failure can be reached from `ReserveChecked(WasMet: false)`, and Wolverine's saga-state guard implicitly catches duplicate FailSettlement deliveries via the terminal-status check).
    - Mutate state: `Status = SettlementStatus.Failed; FailureReason = command.Reason`.
    - Append `PaymentFailed` to the financial event stream — `session.Events.Append(Id, new PaymentFailed(Id, ListingId, WinnerId, command.Reason, DateTimeOffset.UtcNow))`.
    - Return `OutgoingMessages` containing the `PaymentFailed` integration event for local-in-process dispatch (the M5-S3 `PendingSettlementHandler.Handle(PaymentFailed, ...)` consumer transitions the projection row to `Status: Failed` per §8.7).
    - Call `MarkCompleted()` — terminal state, saga document is removed.
  - Failure-path event count: §9.3 specifies the stream contains exactly three events (`SettlementInitiated`, `ReserveCheckCompleted(WasMet: false)`, `PaymentFailed`). No `WinnerCharged`, no `FinalValueFeeCalculated`, no `SellerPayoutIssued`, no `SettlementCompleted`.

### Workstream B — BIN source

- **`src/CritterBids.Settlement/StartSettlementSagaHandler.cs` (modified)** — add a new overload:
  ```
  public static async Task<(SettlementSaga?, OutgoingMessages)> Handle(
      BuyItNowPurchased message,
      IDocumentSession session,
      CancellationToken cancellationToken)
  ```
  Body is structurally identical to the `ListingSold` overload **with three differences**:
  1. Load `PendingSettlement` by `message.ListingId` (same shape).
  2. `SettlementInitiated` payload carries `Source: SettlementSource.BuyItNow` and `Price: message.Price` (not `HammerPrice` — the field name on the contract is `Price` per §1.2).
  3. Constructed saga has `Status: SettlementStatus.ReserveChecked` (skipping the reserve-check phase per W003 Phase 1 Part 5's evolver branching). `ReserveWasMet: true` on the state. **No `ReserveCheckCompleted` event is appended** — the absence is the §9.2 canonical audit signal.
  4. Return `(saga, new OutgoingMessages { new ChargeWinner(sagaId) })` — bypassing `CheckReserve`, going straight to the charge phase.
  
  Deterministic UUID v5 `SettlementId` derivation: same `SettlementsIdentityNamespaces.SettlementId(message.ListingId)` call as the bidding overload. The same listing-A can only have one settlement (either bidding-source via `ListingSold` or BIN-source via `BuyItNowPurchased`, never both — the upstream Auctions BC enforces "BIN removes after first bid" per M3 lived ground), so the deterministic id collision is structurally impossible.

### Workstream C — `BidderCreditView` projection

- **`src/CritterBids.Settlement/BidderCreditView.cs` (new)** — `public sealed record BidderCreditView { public Guid BidderId { get; init; } public decimal RemainingCredit { get; init; } public Guid? LastChargedSettlementId { get; init; } public DateTimeOffset UpdatedAt { get; init; } public Guid Id => BidderId; }`. Per W003 Phase 1 Part 7's "Schema" subsection. `BidderId` doubles as the Marten document id (mirrors `PendingSettlement.Id = ListingId` shape from M5-S3). Triple-slash docstring names the consumers (Relay broadcast handler post-M5; future bidder-balance endpoint), the no-DCB-consumer rule per Part 4 Option A, and the idempotency-via-`LastChargedSettlementId` contract.
- **`src/CritterBids.Settlement/BidderCreditViewHandler.cs` (new)** — `public static class BidderCreditViewHandler` mirroring `PendingSettlementHandler`'s shape. Two `Handle` methods:
  - `Handle(ParticipantSessionStarted message, IDocumentSession session, CancellationToken ct)` — tolerant upsert: `LoadAsync<BidderCreditView>(message.ParticipantId, ct)`; if absent, construct fresh `{ BidderId = ParticipantId, RemainingCredit = CreditCeiling, LastChargedSettlementId = null, UpdatedAt = StartedAt }`; if present, leave the row unchanged (a `ParticipantSessionStarted` re-delivery should not regress a row that's already been charged — the existing `LastChargedSettlementId != null` is the signal). `session.Store(...)`. Idempotency-by-shape: re-delivery of the same `ParticipantSessionStarted` upserts identical fields if the row hasn't been charged.
  - `Handle(WinnerCharged message, IDocumentSession session, CancellationToken ct)` — tolerant upsert: `LoadAsync<BidderCreditView>(message.WinnerId, ct)`; if absent (lazy-init for the no-ParticipantSessionStarted-yet case during the deferral window), construct fresh `{ BidderId = WinnerId, RemainingCredit = -message.Amount, LastChargedSettlementId = message.SettlementId, UpdatedAt = message.ChargedAt }` — negative initial RemainingCredit is the explicit "row had no prior state" sentinel; the Relay broadcast handler reads it verbatim. If present, idempotency check: `if (existing.LastChargedSettlementId == message.SettlementId) return` (no-op on re-delivery per Part 7); else debit `RemainingCredit -= message.Amount`, set `LastChargedSettlementId = message.SettlementId`, set `UpdatedAt = message.ChargedAt`, `session.Store(...)`.
- **`src/CritterBids.Settlement/SettlementModule.cs` (modified)** — add `opts.Schema.For<BidderCreditView>().DatabaseSchemaName("settlement")` to the existing `ConfigureMarten` block (alongside the saga, `PendingSettlement`, and `FinancialEventStream` registrations). Four `Schema.For<T>` calls total after this slice.

### Invalid-transition tests (Workstream A continuation)

The seven scenarios — §1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 — are pure state-guard assertions on the saga document. They do **not** need the Wolverine harness; per the M5-S4 retro's handoff item 3, "per-scenario unit tests against the saga document directly (no Wolverine harness needed) work for these — they're pure state-guard assertions." Approach:

- Construct a `SettlementSaga` document directly in the test with the precondition `Status` set to the invalid-source phase (`Status = SettlementStatus.WinnerCharged` for §3.3's "ChargeWinner from WinnerCharged").
- Call the saga's `Handle(invalidCommand, session)` method directly (or with a mock `IDocumentSession`).
- Assert `Should.Throw<InvalidSettlementTransitionException>(...)` with the expected current-status / command-type pair embedded in the exception.

Per-scenario coverage table:

| Scenario | Precondition `Status` | Command | Expected throw |
|---|---|---|---|
| §1.3 | `Initiated` | — (Start handler called twice via deterministic SettlementId; idempotent return `(null, empty)`) | No throw; idempotent re-delivery returns null saga |
| §2.4 | `ReserveChecked` (already advanced) | `CheckReserve` | throws `InvalidSettlementTransitionException` |
| §3.3 | `WinnerCharged` | `ChargeWinner` | throws |
| §3.4 | `Initiated` (no reserve check yet) | `ChargeWinner` | throws (skipped phase) |
| §4.3 | `WinnerCharged` (correct phase for `CalculateFee`, but) — invalid: `FeeCalculated` already advanced | `CalculateFee` | throws |
| §5.2 | `PayoutIssued` | `IssueSellerPayout` | throws (duplicate) |
| §6.2 | `Completed` (terminal) | any command | throws or no-op per saga document removal (`MarkCompleted` deleted the row; subsequent dispatch fails the saga lookup) |

Resolve the §1.3 and §6.2 scenarios at session start — §1.3 is **not** an invalid-transition test in the throwing sense; it's the idempotent-re-delivery return-`null` path that `StartSettlementSagaHandler` already implements (verified at M5-S4). §6.2 is a saga-lifecycle-vs-state-guard ambiguity: if the saga document is removed at `MarkCompleted()`, a subsequent dispatch can't reach the saga's `Handle` method at all (Wolverine saga lookup fails before the handler runs). Confirm via direct test which path Wolverine takes; document the answer in the retro under "Key Learnings."

### Test files

Author **three** new test files in `tests/CritterBids.Settlement.Tests/`:

- **`SettlementSagaFailurePathsTests.cs`** — Workstream A coverage. `[Collection(SettlementTestCollection.Name)]`. Three `[Fact]`s minimum:
  1. **`ReserveNotMet_ProducesThreeEventStream_AndTerminatesInFailedState`** — §9.3 end-to-end. Seed `PendingSettlement` with `ReservePrice: 50.00`; dispatch `ListingSold` with `HammerPrice: 45.00` (below reserve); assert event stream contains exactly three events in order (`SettlementInitiated`, `ReserveCheckCompleted(WasMet: false)`, `PaymentFailed(Reason: "ReserveNotMet")`); assert `PendingSettlement.Status = Failed` (the M5-S3 handler's transition per §8.7); assert saga document removed.
  2. **`InvalidTransitionGuards_ThrowOnUnexpectedStatus`** — `[Theory]` with seven `[InlineData]` rows for §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 / §1.3-as-confirmed. Direct invocation of the saga's `Handle` methods with constructed pre-state; assertion that `InvalidSettlementTransitionException` is thrown carrying the expected `(currentStatus, commandType)` pair.
  3. **`FailSettlement_FromAlreadyFailedSaga_NoOpsViaSagaLookup`** — verifies the §6.2-style "saga is already terminal" path: after a successful failure dispatch, a duplicate `FailSettlement` cannot reach the saga (saga document removed by `MarkCompleted`). Use `Host.LoadSaga<SettlementSaga>(sagaId)` returning null as the verification surface.

- **`SettlementSagaBinSourceTests.cs`** — Workstream B coverage. Two `[Fact]`s:
  1. **`Full_BuyItNowSource_HappyPath_ProducesFiveEventStream`** — §9.2 end-to-end. Seed `PendingSettlement` (same shape as §9.1); dispatch `BuyItNowPurchased { Price: 100.00, BuyerId, ... }`; assert event stream contains exactly five events in order (`SettlementInitiated(Source: BuyItNow, Price: 100.00)`, `WinnerCharged(Amount: 100.00)`, `FinalValueFeeCalculated(FeeAmount: 10.00, SellerPayout: 90.00)`, `SellerPayoutIssued(PayoutAmount: 90.00, FeeDeducted: 10.00)`, `SettlementCompleted(HammerPrice: 100.00, FeeAmount: 10.00, SellerPayout: 90.00)`) — **no `ReserveCheckCompleted`** is the canonical audit signal per §9.2's "audit query 'show me all BIN settlements' is literally event streams where no `ReserveCheckCompleted` appears."
  2. **`BinSource_DeterministicSettlementId_MatchesBiddingSourceDerivation`** — assertion that the SettlementId for a given listing-id is identical regardless of source. (Belt-and-suspenders structural test — the deterministic helper is a pure function so this is theoretically trivial, but the test documents the contract.)

- **`BidderCreditViewTests.cs`** — Workstream C coverage. Four `[Fact]`s minimum:
  1. **`ParticipantSessionStarted_InitializesRowAtCreditCeiling`** — dispatch `ParticipantSessionStarted { CreditCeiling: 500m }`; assert `BidderCreditView` row exists at the participant id with `RemainingCredit = 500m`, `LastChargedSettlementId = null`.
  2. **`WinnerCharged_DebitsRemainingCredit`** — seed via `ParticipantSessionStarted`; dispatch `WinnerCharged { WinnerId = same, Amount = 85m }`; assert `RemainingCredit = 415m`, `LastChargedSettlementId = <event's SettlementId>`.
  3. **`WinnerCharged_Idempotent_OnDuplicateSettlementId`** — seed, charge once, then dispatch a second `WinnerCharged` with the same `SettlementId`; assert `RemainingCredit` unchanged (no second debit).
  4. **`WinnerCharged_LazyInit_WhenNoParticipantSessionStartedSeed`** — dispatch `WinnerCharged` for a `WinnerId` with no prior row; assert row created with `RemainingCredit = -85m` (the explicit "no prior state" sentinel) and `LastChargedSettlementId = <event's SettlementId>`.

### Session retrospective

- **`docs/retrospectives/M5-S5-settlement-failure-paths-bin-source-bidder-credit-view-retrospective.md`** — mirrors the M5-S4 retro shape. Records the three workstreams, the pre-step (Participants contract promotion + new RabbitMQ queue route), the seven invalid-transition scenarios (and any §1.3 / §6.2 lifecycle-vs-state-guard surprises), the §9.2 BIN canonical-five-event-stream pattern, the `BidderCreditView` lazy-init posture for the no-ParticipantSessionStarted deferral case, and a "what M5-S6 should know" handoff covering the three outbound RabbitMQ publish routes M5-S6 wires (`SettlementCompleted` → Listings; `SellerPayoutIssued` → Relay-stub-or-test-fixture; `PaymentFailed` → deferred-to-post-M5 with Operations consumer).

### Documentation forwarding (not implementation; one-line touches)

- **`docs/milestones/M5-settlement-bc.md` §2 Cross-BC wiring table** — add the Participants→Settlement row: `Participants (M1) | ParticipantSessionStarted | Settlement (M5) | Initialize BidderCreditView with assigned credit ceiling`. One-line addition. Companion: list the new queue `settlement-participants-events` in §2 Cross-BC wiring's "New RabbitMQ queue routes" bullet list.
- **`docs/decisions/007-uuid-strategy.md` §Status / Gate 4 — flag for M5 retro disposition.** Do **not** amend this ADR in S5; instead, record in the S5 retro under "What M5 retro should resolve" that Gate 4's trigger fired at PR #25 (M5-S1) and Settlement has shipped its event streams on engine-default row IDs without surfaced incident. The amendment lands at M5 retro (post-S6) as part of M5's exit-criteria honoring of "ADR 007 Gate 4 honored." S5 surfaces the issue; M5 retro closes it.
- **`docs/workshops/PARKED-QUESTIONS.md`** — update W003-P1-3 (Saga vs Process Manager) Load-Bearing Assumptions section: ADR-019 closed the choice on 2026-05-03; the load-bearing-assumption framing is stale. One-line update: move the assumption to the "Resolved" framing or strike it through with the ADR-019 reference.

---

## Explicitly out of scope

- **`SettlementCompleted` outbound RabbitMQ publish route.** M5-S6 territory. The integration event still emits via `OutgoingMessages` from the saga (reaching the local-in-process `PendingSettlementHandler` consumer); no cross-BC publish wires until S6.
- **`SellerPayoutIssued` outbound publish route.** M5-S6 territory. Relay BC is post-M5; the M5-S6 prompt decides whether to wire a structural publish route now or defer with Operations / Relay.
- **`PaymentFailed` outbound publish route.** Deferred to post-M5 per M5-S4 retro item 6. Operations BC has not shipped at M5 close; wiring a publish route to a non-existent consumer adds queue-config noise without value. Settlement-side emission via `OutgoingMessages` is fully in scope (the M5-S3 `PendingSettlementHandler.Handle(PaymentFailed, ...)` is the in-process consumer).
- **`SettlementCompleted` → Listings `CatalogListingView.Status = "Settled"` extension.** M5-S6 territory. This is the third Path A read-model extension and triggers ADR-014 authoring per M3-S7's ADR candidate review and M4 milestone §"ADR 014 authoring" (though the M4 path-A precedent surface — Session-membership fields + Withdrawn status — has not shipped, the M5-S6 application is independent of that and can stand on the M3-S6 lived ground alone for evidence, with the M4 surface following in a future M4-S6 slice).
- **Real payment-processor integration on the BIN path.** Same MVP credit-ledger posture as the bidding-source path per W003 §"Winner Charge." `Handle(ChargeWinner)` is a state-mutation + event-emit only; no Stripe / Braintree / banking call.
- **`BidderCreditView` cross-BC consumer wiring (Relay's `SettlementCompleted` broadcast).** Relay BC is post-M5; the projection is authored, persisted, and queried-as-document-load only in M5. No Relay-side handler ships in S5.
- **Future bidder-balance endpoint.** Post-MVP per W003 Phase 1 Part 7. `BidderCreditView` is shaped to support it (the document's read shape is the endpoint's response shape) but no endpoint is authored in S5.
- **`SessionExpired` / `BidderCreditView` cleanup lifecycle.** Per W003 Phase 1 Part 7's final paragraph: "Projection lifecycle persists for session duration; post-MVP cleanup follows Participants' session-expiry convention." Cleanup is post-MVP; no expiry handler in S5.
- **The proposed `ProcessManager<TState>` framework primitive.** Per ADR-019. Out of scope as an implementation choice; the Saga shape is the chosen host through M5. If S5 implementation surfaces concrete friction the ADR's revisit-trigger would fire, but the default response per ADR-019 §Revisit trigger is "extract pure-function decider helpers per Option C inside the existing Saga host" — not a migration to ProcessManager.
- **Per-scenario pure-function decider/evolver tests for §1-§7 beyond the invalid-transition coverage.** The M5-S4 retro and ADR-019 §Consequences both note these can be authored as helper-method tests if the saga's per-phase handlers extract pure decider/evolver helpers. S5 does not extract those helpers absent a specific friction signal; the §9.1 (bidding happy path), §9.2 (BIN happy path), §9.3 (reserve-not-met) integration tests transitively cover Sections 1–7's happy/failure paths. The retro's "Key Learnings" section captures any helper-extraction case that S5 implementation actually surfaces.
- **Marten event-type registration for `ParticipantSessionStarted` on the Settlement side.** The contract is consumed via Wolverine handler discovery (the `BidderCreditViewHandler.Handle(ParticipantSessionStarted, ...)` method); Marten's `AddEventType<T>` is only needed if the type is appended to a stream or read from one. `BidderCreditView`'s handler does NOT append `ParticipantSessionStarted` to a stream — it stores the projection document directly. No new `AddEventType<T>` call lands in `SettlementModule.cs`.

---

## Conventions to pin or follow

- **Saga shape extension per W003 Phase 1 Part 2 Approach A + ADR-019.** The same shape extension already lived at M5-S4 — per-phase Handle methods, state guards, `session.Events.Append` for stream audit, `OutgoingMessages` for self-sends and integration emits, `MarkCompleted` at terminal state. Workstream A's `Handle(FailSettlement)` is the seventh Handle method on `SettlementSaga`; the pattern is mechanical.
- **BIN-source initial state per W003 Phase 1 Part 5.** Skip the reserve-check phase. The saga is constructed at `Status: ReserveChecked` with `ReserveWasMet: true` directly; no `ReserveCheckCompleted` event is appended (the absence is the canonical audit signal). The first self-send is `ChargeWinner`, not `CheckReserve`.
- **Tolerant-upsert projection handlers per `marten-projections.md` "Handler-Driven Projections — Tolerant Upsert."** `BidderCreditViewHandler` mirrors `PendingSettlementHandler` (M5-S3) and `AuctionStatusHandler` (M3-S6) — `LoadAsync` by document id; tolerant upsert if absent; mutate via `with`; `session.Store`. The idempotency-by-`LastChargedSettlementId` check is W003 Phase 1 Part 7's contract.
- **`BidderCreditView.BidderId` doubles as the Marten document id.** Same shape as `PendingSettlement.Id = ListingId` per M5-S3. The `public Guid Id => BidderId` expression-bodied property is the Marten convention.
- **Lazy-init posture for `BidderCreditView` rows.** If a `WinnerCharged` arrives for a `WinnerId` with no prior `ParticipantSessionStarted`-seeded row (the deferral case), the handler creates the row with `RemainingCredit = -Amount`. The negative-credit sentinel surfaces the deferral as data rather than an exception. Documented in the handler's triple-slash docstring with the M5-S5 provenance.
- **`PaymentFailed.Reason` field value posture.** M5 produces only `"ReserveNotMet"` per the contract docstring's field-rationale section. The `FailSettlement` command record's `Reason` field carries the value through to `PaymentFailed.Reason` verbatim; no normalization, no enum.
- **Invalid-transition exception shape per M5-S4.** `InvalidSettlementTransitionException` is the existing exception type from M5-S4. Per-scenario tests use it as-is; no new exception types in S5.
- **Em-dash hygiene** is external-prose-only per memory `feedback_em_dash_scope.md`. Saga code, retro, prompt — all may use em dashes freely.

---

## Acceptance criteria

- [ ] `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` exists as a `public sealed record` with the five-field payload + triple-slash docstring naming Settlement BC as the M5-S5 consumer.
- [ ] `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs` emits the contracts-side `ParticipantSessionStarted`; the internal-namespace record is removed or documented as a parallel type.
- [ ] `src/CritterBids.Api/Program.cs` carries a new `settlement-participants-events` queue route — Participants publishes `ParticipantSessionStarted`; Settlement listens. Mirrors the `settlement-auctions-events` shape from M5-S3.
- [ ] `src/CritterBids.Settlement/FailSettlement.cs` defines `public sealed record FailSettlement(Guid SettlementId, string Reason)`.
- [ ] `src/CritterBids.Settlement/SettlementSaga.cs`'s `Handle(CheckReserve)` reserve-not-met branch returns `OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }` (no `NotImplementedException`).
- [ ] `SettlementSaga.cs` defines `public OutgoingMessages Handle([SagaIdentityFrom(...)] FailSettlement command, IDocumentSession session)` that appends `PaymentFailed` to the financial event stream, emits `PaymentFailed` via `OutgoingMessages`, mutates `Status = Failed` + `FailureReason`, and calls `MarkCompleted()`.
- [ ] `src/CritterBids.Settlement/StartSettlementSagaHandler.cs` defines a second `Handle` overload accepting `BuyItNowPurchased` that constructs the saga at `Status: ReserveChecked` (`ReserveWasMet: true`), does NOT append `ReserveCheckCompleted`, and returns `OutgoingMessages { new ChargeWinner(sagaId) }` as the first self-send.
- [ ] `src/CritterBids.Settlement/BidderCreditView.cs` exists with `BidderId`, `RemainingCredit`, `LastChargedSettlementId` (nullable), `UpdatedAt`, and `Id => BidderId` expression-bodied property.
- [ ] `src/CritterBids.Settlement/BidderCreditViewHandler.cs` defines a `public static class` with two `Handle` methods (`ParticipantSessionStarted`, `WinnerCharged`) per the tolerant-upsert shape + Part 7's idempotency-by-`LastChargedSettlementId` contract + the lazy-init posture for the no-prior-row case.
- [ ] `src/CritterBids.Settlement/SettlementModule.cs` updated with `opts.Schema.For<BidderCreditView>().DatabaseSchemaName("settlement")`.
- [ ] `tests/CritterBids.Settlement.Tests/SettlementSagaFailurePathsTests.cs` exists with three `[Fact]`s covering §9.3 end-to-end, the seven invalid-transition guard scenarios, and the §6.2 saga-document-removed-on-MarkCompleted lifecycle assertion.
- [ ] `tests/CritterBids.Settlement.Tests/SettlementSagaBinSourceTests.cs` exists with two `[Fact]`s covering §9.2 BIN happy-path five-event stream (absence of `ReserveCheckCompleted` asserted explicitly) and deterministic SettlementId match across source paths.
- [ ] `tests/CritterBids.Settlement.Tests/BidderCreditViewTests.cs` exists with four `[Fact]`s covering ParticipantSessionStarted init, WinnerCharged debit, idempotent re-delivery via `LastChargedSettlementId`, and lazy-init when no prior row exists.
- [ ] `docs/milestones/M5-settlement-bc.md` §2 cross-BC wiring table extended with the Participants→Settlement row + new queue listed.
- [ ] `docs/workshops/PARKED-QUESTIONS.md` updated: W003-P1-3 Load-Bearing Assumption marked closed by ADR-019.
- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [ ] `dotnet test CritterBids.slnx` — all green; 96 baseline tests still pass; 9 new tests pass (3 + 2 + 4); total **at least** 105 (some Participants-side test adjustments may add or shift counts depending on the contract promotion's effect on existing Participants tests).
- [ ] `docs/retrospectives/M5-S5-settlement-failure-paths-bin-source-bidder-credit-view-retrospective.md` exists; mirrors the M5-S4 retro shape; covers the three workstreams + pre-step; surfaces ADR 007 Gate 4 status and the W003 Phase 1 Part 7 lazy-init posture as M5-retro-disposition items; carries a "what M5-S6 should know" handoff section.

---

## Open questions

- **Q1 — `ParticipantSessionStarted` contract-promotion scope.** The Participants BC currently emits the internal-namespace type from `StartParticipantSession.cs`. Three resolution paths: (a) replace the internal record with the contracts-side type entirely (cleanest if no other internal consumer); (b) keep the internal record and have Participants publish a contracts-side type as a separate emit (most conservative but parallel types); (c) make the internal record `[Obsolete]` and type-forward. Confirm via `Grep` for `ParticipantSessionStarted` references at session start. Default to (a) unless an internal consumer is found.
- **Q2 — `BidderCreditView` initialization deferral if Workstream C's pre-step is descoped.** If the Participants contract promotion turns out to be larger than the pre-step's narrow scope (e.g., the Participants emit site has surprising surface), defer the `ParticipantSessionStarted` seed-path entirely and ship `BidderCreditView` with the lazy-init-on-`WinnerCharged` posture only. The deferral is a `document-as-intentional` workshop-update finding (W003 Phase 1 Part 7 is authoritative for the design; the lazy-init posture is an MVP simplification, not a design pivot). Recorded in the S5 retro with explicit M5-retro hand-off.
- **Q3 — §6.2 saga-already-terminal disposition.** When a `FailSettlement` (or any saga command) arrives at a SettlementId whose saga is already `MarkCompleted()`-removed, Wolverine's saga-lookup fails before the handler runs. The behavior is per-framework-convention; assert via `Host.LoadSaga<SettlementSaga>(sagaId)` returning null and dispatch failing silently (no exception, no state change). If Wolverine throws on saga-lookup failure, the test asserts the throw type and the retro records the framework-convention finding.
- **Q4 — `BidderCreditView` lazy-init negative-credit sentinel value.** The lazy-init posture writes `RemainingCredit = -Amount` when no prior row exists. The negative-credit value surfaces "no prior state" as data. An alternative would be a separate `Initialized: bool` field on the document, defaulting to `false` for lazy-init rows. The negative-credit posture is recommended per W003 Phase 1 Part 7's implicit framing (the document's shape is `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)` with no Initialized flag), but flag for retro confirmation; an explicit `Initialized` field is a one-record-shape change if the lived ground surfaces friction.
- **Q5 — `Marten event-type registration for `PaymentFailed` on the contracts-vs-internal axis.** `PaymentFailed` is registered in `SettlementModule.cs`'s `AddEventType` block (per M5-S4). It is appended to the financial event stream by the saga's `Handle(FailSettlement)` AND emitted via `OutgoingMessages`. The same dual-role as `SellerPayoutIssued` + `SettlementCompleted` from M5-S4; no new registration needed. Confirm at session start that the M5-S4 module registration covers this path.
- **Q6 — Bidding-source `WinnerCharged` and `BidderCreditView` interaction in the §9.1 test (M5-S4-authored).** The M5-S4 §9.1 integration test does **not** seed a `BidderCreditView` row before dispatching `ListingSold` (it predates Workstream C). When S5 ships, the test fixture's `WinnerCharged` emission will hit the new `BidderCreditViewHandler.Handle(WinnerCharged, ...)` via Wolverine's handler discovery — and since no prior row exists, the lazy-init path fires. The existing §9.1 test should remain green (no assertion against `BidderCreditView`), but the §9.1's three assertions ("six-event stream", "PendingSettlement consumed", "saga removed") may need a fourth ("BidderCreditView row exists with RemainingCredit: -85m") for documentation completeness. Decide at session start: either extend the §9.1 test to assert the new lazy-init row, or leave it as documentation-grade-omission and let `BidderCreditViewTests.cs`'s lazy-init `[Fact]` cover the same path independently. Lean toward extending §9.1 — the assertion is one line and the test now exercises the full M5-S5 surface, not just S4's.
- **Q7 — Test count delta.** S5 adds at least nine tests across three new files: failure-paths (3) + bin-source (2) + bidder-credit-view (4) = 9. The Participants contract promotion may force adjustments in existing Participants-side tests if any assert against the internal-namespace `ParticipantSessionStarted` type — defer the count to session-close.
- **Q8 — Workshop drift: M5 milestone doc §2 cross-BC wiring table.** The table currently lists Settlement-inbound queues from Selling and Auctions but no Participants→Settlement row. Workstream C requires it. The one-line addition is in-scope for S5 per the AUTHORING.md rule 3 joint-authority clause (milestone doc + narrative 002). The narrative 002 itself does not dramatize the Participants→Settlement seam (the credit-ceiling allocation happens at QR-scan time in narrative 003, not at settlement time in narrative 002), so no narrative-side drift; this is a milestone-doc completeness item only.

---

## Commit sequence

Three commits, in this order:

1. `feat(participants,contracts): promote ParticipantSessionStarted to cross-BC contract; wire settlement-participants-events RabbitMQ route` — the pre-step. Smallest commit; isolates the contracts-promotion blast radius to a single reviewable diff before the Settlement-side handler consumption lands.
2. `feat(settlement): author FailSettlement command, BIN source Start handler overload, BidderCreditView document + handler; extend SettlementSaga with Handle(FailSettlement); register BidderCreditView schema` — the bulk of the implementation. Three workstreams land together because they share the saga / module / start-handler files and a partial-commit splitting them would require interleaved diffs across the same files.
3. `feat(settlement): failure-path / BIN-source / BidderCreditView integration tests; milestone wiring doc + PARKED-QUESTIONS update; write M5-S5 retrospective` — tests + docs + retro bundle.

The Participants-side promotion lands in commit 1 because Workstream C's handler import depends on the contracts-side type existing; the implementation order is enforced by the type system. If Workstream C is descoped at session start per Q2's deferral path, commit 1 drops the Participants-side pre-step entirely and Workstream A + B land in commit 2 with Workstream C following the lazy-init-only posture in commit 2's same surface.

---

## Document history

- **v0.1** (2026-05-15): Authored at M5-S4 close as the M5-S5 slice prompt. Three workstreams (failure paths, BIN source, BidderCreditView projection) plus the Participants contract-promotion pre-step. Cutover-gate joint-authority discipline carried from M5-S1 through M5-S4; this prompt continues to cite narrative 002 in its metadata block per AUTHORING.md rule 3. Two M5-close-blocking items surfaced for M5 retro disposition: ADR 007 Gate 4 trigger fired but unamended; W003 Phase 1 Part 7 BidderCreditView lazy-init posture as MVP simplification of the `ParticipantSessionStarted`-seeded design.
