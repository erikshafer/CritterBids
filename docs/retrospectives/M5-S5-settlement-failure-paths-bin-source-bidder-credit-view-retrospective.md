# M5-S5: Settlement Failure Paths + BIN Source + BidderCreditView — Retrospective

**Date:** 2026-05-15
**Milestone:** M5 — Settlement BC
**Slice:** S5 of 6 (Settlement Workflow Failure Paths + BIN Source + BidderCreditView projection)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M5-S5-settlement-failure-paths-bin-source-bidder-credit-view.md`
**Narrative (joint authority):** `docs/narratives/002-winner-clears-settlement.md`

---

## Baseline

- 96 tests passing at M5-S4 close (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + 9 Settlement); `dotnet build CritterBids.slnx` 0 errors, 24 pre-existing NU1904 Marten vulnerability warnings; M5-S4 closed at PR #28 (SHA `d953469`)
- `src/CritterBids.Settlement/` carries the M5-S4 saga happy-path surface — six event types, five self-send commands, `SettlementSaga` with five `Handle` methods, `StartSettlementSagaHandler` with the `ListingSold` overload, `SettlementsConcurrencyRetryPolicies`, `SettlementsIdentityNamespaces.SettlementId(listingId)` deterministic UUID v5 derivation
- `Handle(CheckReserve)`'s reserve-not-met branch throws `NotImplementedException("Reserve-not-met failure path lands at M5-S5")` — the entry point for Workstream A
- `SettlementSource.BuyItNow` enum value defined but no consumer at M5-S4 — the entry point for Workstream B
- `ParticipantSessionStarted` lives as a Participants-internal record at `src/CritterBids.Participants/Features/StartParticipantSession/ParticipantSessionStarted.cs`, blocking Workstream C's `BidderCreditView` seed path
- `wolverine-sagas.md` "Multi-Phase Sagas with Self-Sent Continuation Commands" section authored at M5-S4 — the pattern reference; S5 extends the saga along the established shape
- `marten-projections.md` "Handler-Driven Projections — Tolerant Upsert" section carries the M5-S3 amendment for `PendingSettlementHandler`; `BidderCreditViewHandler` mirrors that shape

---

## Items completed

| Item | Description |
|------|-------------|
| S5a | `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` — new cross-BC contract with the five-field payload promoted from the Participants-internal record. Triple-slash docstring names the M5-S5 Settlement consumer, the post-M5 Relay consumer, the field rationale, and the M5-S5 promotion provenance. |
| S5b | Participants-side migration to the contracts-side type — internal record deleted; `StartParticipantSession.cs` emits the contracts type; `Participant.cs`'s `Apply(ParticipantSessionStarted)` consumes the contracts type; `ParticipantsModule.cs` registers the contracts type via `AddEventType<T>`; two test files (`StartParticipantSessionTests.cs`, `RegisterAsSellerTests.cs`) updated. |
| S5c | `src/CritterBids.Api/Program.cs` — `settlement-participants-events` RabbitMQ queue route added; Participants publishes `ParticipantSessionStarted`; Settlement listens. Mirrors the `settlement-auctions-events` shape from M5-S3. |
| S5d | `src/CritterBids.Settlement/FailSettlement.cs` — `sealed record FailSettlement(Guid SettlementId, string Reason)`. Triple-slash docstring names §3.2 / §9.3 scenarios and the M5 reason-vocabulary posture (`"ReserveNotMet"` only). |
| S5e | `src/CritterBids.Settlement/SettlementSaga.cs` — reserve-not-met branch in `Handle(CheckReserve)` switched from `NotImplementedException` to `return new OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }`; new `Handle(FailSettlement)` method appends `PaymentFailed` to the financial event stream, emits the integration event via `OutgoingMessages`, mutates state to `Failed` with `FailureReason`, and calls `MarkCompleted()`. Saga docstring updated to remove M5-S5 deferral language and document the failure-path / BIN-source M5-S5 surface. |
| S5f | `src/CritterBids.Settlement/StartSettlementSagaHandler.cs` — second `Handle(BuyItNowPurchased, ...)` overload. Loads `PendingSettlement` (same retry contract as bidding); derives deterministic `SettlementId` via UUID v5; idempotent re-delivery guard; constructs saga at `Status: ReserveChecked`, `ReserveWasMet: true`; appends `SettlementInitiated(Source: BuyItNow, Price: message.Price)` to the financial event stream; **does NOT append `ReserveCheckCompleted`** (the absence is §9.2's canonical audit signal); returns `(saga, OutgoingMessages { new ChargeWinner(sagaId) })` — first self-send bypasses CheckReserve. |
| S5g | `src/CritterBids.Settlement/BidderCreditView.cs` — `public sealed record BidderCreditView { BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt, Id => BidderId }`. Per W003 Phase 1 Part 7. Triple-slash docstring documents the lifecycle, idempotency-by-`LastChargedSettlementId`, lazy-init negative-credit sentinel posture, and no-DCB-consumer rule per Part 4 Option A. |
| S5h | `src/CritterBids.Settlement/BidderCreditViewHandler.cs` — `public static class` with two `Handle` methods. `ParticipantSessionStarted` seeds at `RemainingCredit = CreditCeiling`; preserves an already-charged row (`LastChargedSettlementId != null`) on re-delivery rather than regressing the balance. `WinnerCharged` debits; lazy-inits a row at `RemainingCredit = -Amount` when no prior row exists; idempotent no-op when `LastChargedSettlementId == message.SettlementId`. Mirrors the `PendingSettlementHandler` (M5-S3) tolerant-upsert shape. |
| S5i | `src/CritterBids.Settlement/SettlementModule.cs` — fourth `Schema.For<T>` registration: `opts.Schema.For<BidderCreditView>().DatabaseSchemaName("settlement")`. |
| S5j | `tests/CritterBids.Settlement.Tests/SettlementSagaFailurePathsTests.cs` — eight `[Fact]`s: §9.3 end-to-end three-event failure stream + six invalid-transition state-guard tests (§2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2-Completed) + one §6.2-lifecycle duplicate-dispatch test asserting saga-document-removal durability. |
| S5k | `tests/CritterBids.Settlement.Tests/SettlementSagaBinSourceTests.cs` — two `[Fact]`s: §9.2 BIN happy-path five-event stream (with explicit `events.ShouldNotContain(e => e.Data is ReserveCheckCompleted)` belt-and-suspenders assertion) + deterministic SettlementId source-invariance assertion. |
| S5l | `tests/CritterBids.Settlement.Tests/BidderCreditViewTests.cs` — five `[Fact]`s: `ParticipantSessionStarted` init at ceiling + `WinnerCharged` debit + idempotent re-delivery via `LastChargedSettlementId` equality + lazy-init negative-credit sentinel + `ParticipantSessionStarted` re-delivery preserves already-charged row. |
| S5m | `tests/CritterBids.Settlement.Tests/SettlementSagaTests.cs` — §9.1 happy-path test extended with a fourth assertion block asserting the lazy-init `BidderCreditView` row created by the saga-emitted `WinnerCharged`. Per Open Question Q6 — documents the full M5-S5 surface in the bidding integration test without weakening the independent `BidderCreditViewTests` coverage. |
| S5n | `docs/milestones/M5-settlement-bc.md` §2 cross-BC wiring — Participants→Settlement row added; new `settlement-participants-events` queue listed alongside the existing settlement-inbound queues. |
| S5o | `docs/workshops/PARKED-QUESTIONS.md` — W003-P1-3 load-bearing-assumption closed by ADR-019; the assumption is struck through with the resolution date and the ADR cross-reference. |
| S5p | This retrospective. |

The prompt structured scope as three commits:

| Commit | Items covered |
|--------|---------------|
| 1 — `feat(participants,contracts): promote ParticipantSessionStarted to cross-BC contract; wire settlement-participants-events RabbitMQ route` | S5a, S5b, S5c |
| 2 — `feat(settlement): author FailSettlement command, BIN source Start handler overload, BidderCreditView document + handler; extend SettlementSaga with Handle(FailSettlement); register BidderCreditView schema` | S5d, S5e, S5f, S5g, S5h, S5i |
| 3 — `feat(settlement): failure-path / BIN-source / BidderCreditView integration tests; milestone wiring doc + PARKED-QUESTIONS update; write M5-S5 retrospective` | S5j, S5k, S5l, S5m, S5n, S5o, S5p |

---

## Workstream A — Failure path

### Shape

```csharp
// Reserve-not-met branch in Handle(CheckReserve):
if (!met)
{
    return new OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") };
}

// New Handle(FailSettlement) — terminal-state guard, append PaymentFailed, MarkCompleted():
public OutgoingMessages Handle(
    [SagaIdentityFrom(nameof(FailSettlement.SettlementId))] FailSettlement command,
    IDocumentSession session)
{
    if (Status is SettlementStatus.Completed or SettlementStatus.Failed)
    {
        throw new InvalidSettlementTransitionException(Id, Status, nameof(FailSettlement));
    }

    Status = SettlementStatus.Failed;
    FailureReason = command.Reason;

    var paymentFailed = new PaymentFailed(Id, ListingId, WinnerId, command.Reason, DateTimeOffset.UtcNow);
    session.Events.Append(Id, paymentFailed);
    MarkCompleted();

    return new OutgoingMessages { paymentFailed };
}
```

### Why the terminal-state guard rejects only Completed and Failed

The reserve-not-met self-send reaches `Handle(FailSettlement)` from `ReserveChecked(WasMet: false)`. Post-MVP failure modes (insufficient credit, payment-provider rejection) may dispatch `FailSettlement` from later phases — `WinnerCharged`, `FeeCalculated`, `PayoutIssued`. Guarding on a fixed source phase would force a guard amendment every time a new failure mode lands; guarding only the terminal states keeps the failure path open-ended without sacrificing the duplicate-dispatch protection. Wolverine inbox dedup plus `MarkCompleted()`'s saga-document removal are the primary defenses; the guard is the correctness contract if both fail.

### §9.3 three-event failure stream

The §9.3 end-to-end test seeds `PendingSettlement` with `ReservePrice: 50`, dispatches `ListingSold` with `HammerPrice: 45`, and asserts the financial event stream contains exactly three events:

1. `SettlementInitiated(Price: 45, ReservePrice: 50, Source: Bidding)`
2. `ReserveCheckCompleted(Price: 45, ReservePrice: 50, WasMet: false)`
3. `PaymentFailed(Reason: "ReserveNotMet")`

No `WinnerCharged`, no `FinalValueFeeCalculated`, no `SellerPayoutIssued`, no `SettlementCompleted`. The PendingSettlement projection transitions to `Status: Failed` via the M5-S3 `PendingSettlementHandler.Handle(PaymentFailed)` consumer firing on local in-process dispatch (§8.7 closed). The saga document is removed at `MarkCompleted()`. The `BidderCreditView` row is **not** created — the failure path never emits `WinnerCharged`, so the credit ledger remains untouched. The retro tests assert this absence explicitly.

### Invalid-transition coverage

Six per-scenario state-guard tests cover §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2-Completed. All authored as direct-invocation tests against constructed `SettlementSaga` documents — no Wolverine harness needed per the M5-S4 retro's handoff item 3. The shape is:

```csharp
var saga = new SettlementSaga { Id = ..., Status = <precondition phase>, ... };
await using var session = _fixture.GetDocumentSession();

var ex = Should.Throw<InvalidSettlementTransitionException>(
    () => saga.Handle(new <CommandType>(saga.Id), session));

ex.CurrentStatus.ShouldBe(<precondition phase>);
ex.CommandType.ShouldBe(nameof(<CommandType>));
```

§1.3 (idempotent re-delivery of `ListingSold`) is **not** in the invalid-transition set — it's the start-handler's existing-saga check returning `(null, empty)` per M5-S4. The §9.1 and §9.3 integration tests transitively exercise the start-handler's behavior; the idempotency-via-deterministic-id is covered structurally by the `SettlementsIdentityNamespaces.SettlementId` pure-function helper.

§6.2's saga-document-removed-at-MarkCompleted path is asserted via a separate integration test (`FailSettlement_DuplicateDispatch_DoesNotRegressTerminalState`) — the test runs a full failure dispatch, verifies the saga is gone, and confirms the financial event stream still has exactly three events. Whether a second `ListingSold` re-dispatch for an already-failed settlement should be rejected by the PendingSettlement's Failed terminal status (treating it as a duplicate vs starting a fresh saga at the same SettlementId) is **routed to the M5 retro** as a saga-lifecycle-versus-projection-state-vocabulary question — the lived behavior the M5-S5 test asserts is the framework's stance, not a designed contract.

---

## Workstream B — BIN source

### Shape

`StartSettlementSagaHandler.Handle(BuyItNowPurchased, ...)` is structurally identical to the `ListingSold` overload with three differences:

1. The constructed saga's `Status` is `SettlementStatus.ReserveChecked` (not `Initiated`) with `ReserveWasMet: true`.
2. The `SettlementInitiated` event carries `Source: SettlementSource.BuyItNow` and `Price: message.Price` (the BIN contract's field name is `Price`, not `HammerPrice`).
3. No `ReserveCheckCompleted` event is appended — the absence is the §9.2 audit signal.
4. The first self-send is `ChargeWinner` (not `CheckReserve`) — bypassing the reserve-check phase entirely per W003 Phase 1 Part 5.

The deterministic UUID v5 `SettlementId` derivation is identical (`SettlementsIdentityNamespaces.SettlementId(message.ListingId)`). The same listing can only initiate one settlement across sources — Auctions enforces "BIN removes after first bid" per M3 lived ground — so deterministic-id collisions on this overload are always re-deliveries of the same `BuyItNowPurchased`, caught by the existing-saga check.

### §9.2 five-event BIN stream

```
1. SettlementInitiated(Source: BuyItNow, Price: 100.00)
2. WinnerCharged(Amount: 100.00)
3. FinalValueFeeCalculated(FeeAmount: 10.00, SellerPayout: 90.00)
4. SellerPayoutIssued(PayoutAmount: 90.00, FeeDeducted: 10.00)
5. SettlementCompleted(HammerPrice: 100.00, FeeAmount: 10.00, SellerPayout: 90.00)
```

The §9.2 test asserts both the count (5) and the position-1-is-not-ReserveCheckCompleted (position 1 is WinnerCharged) and an explicit `events.ShouldNotContain(e => e.Data is ReserveCheckCompleted)` belt-and-suspenders check. The audit query "show me all BIN settlements" is implementable as a Marten event-stream query for streams where no `ReserveCheckCompleted` appears — the absence is data, not metadata.

### Saga state-guard interaction with the BIN initial state

The BIN-source saga starts at `Status: ReserveChecked`. The existing `Handle(ChargeWinner)` guards `Status != SettlementStatus.ReserveChecked` — the BIN initial state satisfies the guard naturally. **No state-guard amendments required** for BIN support. The same state machine handles both sources; the only fork is at the start handler's evolver-branching point.

---

## Workstream C — `BidderCreditView` projection

### Shape

```csharp
public sealed record BidderCreditView
{
    public Guid BidderId { get; init; }
    public decimal RemainingCredit { get; init; }
    public Guid? LastChargedSettlementId { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public Guid Id => BidderId;
}
```

Mirrors `PendingSettlement.Id = ListingId` shape from M5-S3 — the natural business key (`BidderId`) doubles as the Marten document key via the `Id => BidderId` expression-bodied alias.

### Handler shape

`BidderCreditViewHandler` is a `public static class` with two `Handle` methods:

- `Handle(ParticipantSessionStarted, ...)` — seed at `RemainingCredit = CreditCeiling`. Guard: if the row exists and has been charged (`LastChargedSettlementId != null`), preserve it — re-seeding would regress the balance.
- `Handle(WinnerCharged, ...)` — lazy-init the row at `RemainingCredit = -Amount` if absent; idempotent no-op if `LastChargedSettlementId == message.SettlementId`; debit otherwise.

Mirrors `PendingSettlementHandler` (M5-S3) and `AuctionStatusHandler` (M3-S6) per `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert".

### Lazy-init negative-credit sentinel — why it lives in this surface

Per Open Question Q4 — the lazy-init posture writes `RemainingCredit = -Amount` when `WinnerCharged` arrives without a prior `ParticipantSessionStarted` seed. The alternative was a separate `Initialized: bool` field on the document. The single-field-with-sentinel-value shape was chosen because:

1. W003 Phase 1 Part 7's schema is `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)` — adding `Initialized` would be a workshop-update finding requiring a §"Schema" amendment. The sentinel value is implementable within the existing schema.
2. The Relay broadcast handler reads `RemainingCredit` verbatim. A negative value is renderable as a "credit deficit" or "balance unknown" UX state without handler-side branching.
3. A separate field would force every Relay / future-endpoint consumer to branch on `Initialized` vs `RemainingCredit` independently. The sentinel collapses the branch into a single signed-number comparison.

The lazy-init path is exercised in two tests: the dedicated `WinnerCharged_LazyInit_WhenNoParticipantSessionStartedSeed` test and the existing §9.1 integration test (extended per Q6 — the saga's `WinnerCharged` emission hits the handler with no prior session-started seed). Both assert `RemainingCredit = -85m` and `LastChargedSettlementId = <event's SettlementId>`.

### `ParticipantSessionStarted` re-delivery preservation

The fifth BidderCreditView test (`ParticipantSessionStarted_DoesNotRegressAlreadyChargedRow`) covers the cross-queue race scenario: if a charge has landed before a re-delivery of `ParticipantSessionStarted` (cross-queue race or at-least-once redelivery), the already-charged row must be preserved. The handler's `if (existing is { LastChargedSettlementId: not null }) return;` guard is the protection.

This is a sixth test beyond the prompt's "four `[Fact]`s minimum" specification — added because the guard logic is non-obvious and warranted explicit coverage. The prompt's minimum was four; the slice ships with five.

---

## Pre-step — `ParticipantSessionStarted` contracts promotion

### Why path (a) — replace the internal record with the contracts-side type entirely

Per Open Question Q1, three resolution paths were available. Decided path (a) at session start because:

1. The grep for `ParticipantSessionStarted` references found six production-code references (Participants `Apply`, `AddEventType`, emit site, plus a passing comment reference in Auctions docs / handlers — non-blocking) and two test-side type-alias references. None depended on the `internal`-vs-`public` access modifier or namespace-specific behavior.
2. The Participants BC had no other internal consumer that benefited from a separate internal type. The contracts-side type is identical in payload; carrying two parallel types would force every cross-BC change to be replicated twice.
3. The path (a) migration is single-PR-safe — all six production references update via using-statement swap; the tests update via type-alias swap.

### Marten event-type identity changed

Marten serializes events using fully-qualified .NET type names. By renaming `ParticipantSessionStarted` from `CritterBids.Participants.Features.StartParticipantSession.ParticipantSessionStarted` to `CritterBids.Contracts.Participants.ParticipantSessionStarted`, the event-type identifier on the wire changes. The six Participants tests passed after the promotion because each test cleans Marten data first (`CleanAllMartenDataAsync`) — no pre-existing events under the old type name need migration. **In production**, this kind of contract promotion would need either an event-type alias mapping (Marten supports this via `opts.Events.MapEventType<T>("<old-name>")`) or an event-version-upgrade pass. M5-S5 ships pre-production, so the migration is cosmetic; flagging here for the post-MVP / first-real-deploy retrospective.

### `Apply(ParticipantSessionStarted)` on the Participant aggregate

`Participant.cs`'s `Apply(ParticipantSessionStarted)` is the only event-sourcing-via-Marten consumer of this type in the Participants BC. The using-statement swap from `CritterBids.Participants.Features.StartParticipantSession` to `CritterBids.Contracts.Participants` was a single-line change. The Marten aggregate-rebuild path on the next `AggregateStreamAsync<Participant>(id)` call will use the new type name for both the event-type lookup and the Apply method dispatch.

---

## Findings against narrative

The slice operated against narrative 002 as a Moment-grain implementation reference. The §9.3 failure-path test corresponds to a narrative-implicit Moment (narrative 002's Cast / Setting framing does not dramatize the reserve-not-met branch — narrative 002 is the happy-path story per its title). The §9.2 BIN-source test corresponds to a narrative-implicit alternative path (narrative 002 is bidding-source; BIN-source is the parallel narrative 003 territory for the bidder-side journey, but narrative 002's Moment 3 "winner is charged" is the only beat shared across sources).

| Lane | Action |
|---|---|
| `narrative-update` | None. Narrative 002 dramatizes only the bidding-source happy path; the M5-S5 surface (failure / BIN / bidder-credit) extends the saga along W003 Phase 1's design without requiring narrative-side changes. |
| `workshop-update` | None directly authored in S5. Two M5-close-blocking items surfaced for M5 retro disposition (see "What M5 retro should resolve" below). The W003-P1-3 load-bearing-assumption resolution in PARKED-QUESTIONS is a stale-assumption closure, not a new workshop edit. |
| `code-update` | The contracts promotion of `ParticipantSessionStarted` to `CritterBids.Contracts.Participants.*` is the only contracts-side change. Same payload; namespace-only migration. |
| `document-as-intentional` | The `BidderCreditView` lazy-init negative-credit sentinel posture is W003 Phase 1 Part 7-adjacent (Part 7's "Schema" subsection is silent on the no-prior-row case; the sentinel posture is an MVP simplification of the implicit `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)` shape). Documented in the `BidderCreditView` docstring and the handler's lazy-init branch; carried as a finding for the M5 retro. |

The cumulative narrative 002 findings ledger at M5-S5 close is unchanged from M5-S4: F001 ✓ (PR #20), F002 ✓ (PR #25), F003 ✓ minimum-scope (PR #20), F004 ✓ (PR #25), F005 ✓ (PR #25). No new findings against narrative 002 in S5.

---

## Key learnings

### `InvokeMessageAndWaitAsync` lives in `Wolverine.Tracking`, not `Wolverine`

First-fix compile error in the new test files: the `Host.InvokeMessageAndWaitAsync(message)` extension method is in `Wolverine.Tracking`, not `Wolverine`. The existing `SettlementSagaTests.cs` (M5-S4) carries `using Wolverine.Tracking;` for exactly this reason. Worth noting as a friction point for future Settlement-side test authoring: any test that drives the saga via a real inbound dispatch needs both `using Wolverine;` (for `OutgoingMessages` and the saga primitives) and `using Wolverine.Tracking;` (for the host-side dispatch helper). The `GlobalUsings.cs` carries only `Shouldly` and `Xunit`; the per-file usings discipline keeps the imports visible at the test file's top.

### Marten event-type identity is namespace-prefix-sensitive

Documented above under the pre-step. The contracts promotion changed the wire-level event-type identifier; in pre-production it's invisible because tests clean Marten before each run. The post-MVP first-real-deploy retrospective should carry this as a load-bearing learning — the same namespace-migration pattern would break event-stream replay in production without a `MapEventType<T>("<old-name>")` migration step.

### Direct-invocation tests for state-guard scenarios are 10× faster than Wolverine-harness tests

The six `Should.Throw<InvalidSettlementTransitionException>(...)` tests in `SettlementSagaFailurePathsTests.cs` run in single-digit milliseconds combined — they construct a saga document in memory, open a session, and call the handler synchronously. The Wolverine-harness alternative (dispatch a self-send command into the inbox, wait for the handler to fire, catch the resulting message-bus exception) would add ~50–100ms per test for the saga lookup and dispatch overhead. For pure state-guard assertions on a saga's `Handle` method, direct invocation is the right tool — and the saga's state-guard logic is unit-testable without DI, which is a property worth preserving when extracting pure-function decider helpers per ADR-019 Option C.

### The BIN-source overload validated the start-handler's shape is reusable

Authoring the BIN overload was a 50-line implementation that copied the bidding overload's structure verbatim and changed exactly three places: the saga's initial `Status` and `ReserveWasMet`, the `SettlementInitiated`'s `Source` and `Price` fields, and the first self-send (`ChargeWinner` instead of `CheckReserve`). The `PendingSettlement` load, deterministic-id derivation, idempotent-re-delivery check, and `OutgoingMessages` return shape were all unchanged. This is a strong signal that the M5-S4 start-handler shape generalized cleanly — future source overloads (post-MVP payment-processor failure replay; offline ops-staff manual settlement initiation per W003 Phase 4 PARKED P-001) can follow the same pattern.

---

## Findings against the wolverine-sagas / marten-projections skills

- **`wolverine-sagas.md` "Multi-Phase Sagas with Self-Sent Continuation Commands" section** — the M5-S4 amendment cited the seven Handle-method shape; M5-S5 extends to eight methods (the seven happy-path phases plus `FailSettlement`) but the pattern doesn't fundamentally shift. **No skill amendment in S5** — the existing section's "multi-phase" framing covers both happy-path and failure-branch phases. The failure-path-specific guidance (terminal-state guard rejecting only Completed/Failed; integration event appended-to-stream + emitted-on-bus; `MarkCompleted()` after the terminal mutation) is documented in the saga's triple-slash docstring rather than the skill.
- **`marten-projections.md` "Handler-Driven Projections — Tolerant Upsert" section** — `BidderCreditViewHandler` is the third instance of the tolerant-upsert pattern (after `PendingSettlementHandler` M5-S3 and `AuctionStatusHandler` M3-S6). The lazy-init negative-credit sentinel posture is `BidderCreditView`-specific — the document's natural-key-as-id shape supports it; `PendingSettlement`'s shape does not (the seed event carries fields the projection needs; there's no analogous "deficit" framing). **Defer the lazy-init-sentinel pattern callout** to a future skills-maintenance pass — one instance is too thin a precedent for a skill amendment.
- **`critter-stack-testing-patterns.md`** — the direct-invocation pattern for state-guard tests (Key Learning #3 above) and the §9.x integration-test pattern (single inbound → multi-event stream + projection assertions) are both reusable beyond Settlement. **Defer the callout** to a future skills-maintenance pass; the M5-S4 retro flagged the same intent and it's still outstanding.

---

## Verification checklist

- [x] `src/CritterBids.Contracts/Participants/ParticipantSessionStarted.cs` exists as `public sealed record` with the five-field payload + triple-slash docstring naming Settlement BC as the M5-S5 consumer.
- [x] `src/CritterBids.Participants/Features/StartParticipantSession/StartParticipantSession.cs` emits the contracts-side `ParticipantSessionStarted`; the internal-namespace record is removed.
- [x] `src/CritterBids.Api/Program.cs` carries the new `settlement-participants-events` queue route.
- [x] `src/CritterBids.Settlement/FailSettlement.cs` defines `public sealed record FailSettlement(Guid SettlementId, string Reason)`.
- [x] `SettlementSaga.cs`'s `Handle(CheckReserve)` reserve-not-met branch returns `OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }` (no `NotImplementedException`).
- [x] `SettlementSaga.cs` defines `public OutgoingMessages Handle([SagaIdentityFrom(...)] FailSettlement command, IDocumentSession session)` that appends `PaymentFailed`, emits via `OutgoingMessages`, mutates `Status = Failed` + `FailureReason`, and calls `MarkCompleted()`.
- [x] `StartSettlementSagaHandler.cs` defines a second `Handle(BuyItNowPurchased)` overload constructing the saga at `Status: ReserveChecked` (`ReserveWasMet: true`), NOT appending `ReserveCheckCompleted`, returning `OutgoingMessages { new ChargeWinner(sagaId) }`.
- [x] `src/CritterBids.Settlement/BidderCreditView.cs` exists with `BidderId`, `RemainingCredit`, `LastChargedSettlementId` (nullable), `UpdatedAt`, `Id => BidderId`.
- [x] `src/CritterBids.Settlement/BidderCreditViewHandler.cs` defines a `public static class` with two `Handle` methods covering the tolerant-upsert + Part 7 idempotency + lazy-init contracts.
- [x] `SettlementModule.cs` updated with `opts.Schema.For<BidderCreditView>().DatabaseSchemaName("settlement")`.
- [x] `tests/CritterBids.Settlement.Tests/SettlementSagaFailurePathsTests.cs` exists with eight `[Fact]`s (§9.3 end-to-end + six invalid-transition guards + §6.2-lifecycle duplicate-dispatch).
- [x] `tests/CritterBids.Settlement.Tests/SettlementSagaBinSourceTests.cs` exists with two `[Fact]`s (§9.2 BIN happy-path + deterministic-id source-invariance).
- [x] `tests/CritterBids.Settlement.Tests/BidderCreditViewTests.cs` exists with five `[Fact]`s (init at ceiling + debit + idempotent re-delivery + lazy-init + preserve-charged-on-re-seed).
- [x] `docs/milestones/M5-settlement-bc.md` §2 cross-BC wiring table extended with the Participants→Settlement row + new queue listed.
- [x] `docs/workshops/PARKED-QUESTIONS.md` updated: W003-P1-3 Load-Bearing Assumption marked closed by ADR-019.
- [x] `dotnet build CritterBids.slnx` — 0 errors (24 pre-existing NU1904 Marten vulnerability warnings unchanged).
- [x] `dotnet test CritterBids.slnx` — all green; 111 tests pass (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + 24 Settlement). M5-S4 baseline was 96; 15 new tests added.
- [x] This retrospective exists.

---

## What M5 retro should resolve

Two M5-close-blocking documentation items surfaced at M5-S5; both defer to the M5 retro (post-S6) per the prompt's "Documentation forwarding" framing:

1. **ADR 007 Gate 4 status.** The ADR's Gate 4 trigger ("M5-S1 Settlement BC, owned by Erik") fired with PR #25 (M5-S1). Settlement has now shipped its event streams (M5-S3 `PendingSettlement`, M5-S4 `FinancialEventStream`, M5-S5 `BidderCreditView`) on engine-default row IDs without any surfaced incident. The ADR's status line still reads "Re-Deferred (M4-S1)"; either Settlement's event-row-ID strategy is closed by lived-fact (engine default; no v7 row IDs needed) and ADR 007 needs the amendment, or the gate re-defers again with a new trigger. **Disposition at M5 retro:** amend ADR 007's Gate 4 to "closed by lived-fact at M5-S5" with a status-line update, OR redefine the gate's trigger to a post-M5 milestone.

2. **W003 Phase 1 Part 7 lazy-init posture as MVP simplification.** The `BidderCreditView` projection's design in W003 Phase 1 Part 7 specifies initialization on `ParticipantSessionStarted`. The lazy-init-on-`WinnerCharged` posture is an MVP simplification of that design — it preserves the consumer-side contract (the `RemainingCredit` field is the only number Relay reads) but uses a negative-credit sentinel value to signal "no prior session-started seed" as data rather than as an exception. The simplification is recorded in the `BidderCreditView` docstring and the handler's lazy-init branch. **Disposition at M5 retro:** decide whether to treat the lazy-init posture as a permanent MVP simplification (document-as-intentional) or as a temporary state to revisit when Participants→Settlement event ordering is hardened by RabbitMQ guarantees (workshop-update finding for W003 Phase 1 Part 7).

A third item surfaced during implementation:

3. **Duplicate-`ListingSold` after an already-failed settlement.** The `FailSettlement_DuplicateDispatch_DoesNotRegressTerminalState` test asserts the saga document is removed after `MarkCompleted()` and the financial event stream's three-event prefix is preserved. The test does NOT assert whether a second `ListingSold` re-dispatch (for the same listing) would be rejected by the PendingSettlement's `Failed` terminal status (treating it as a duplicate) or would start a fresh saga at the same deterministic SettlementId (appending a fourth event). The lived behavior the framework produces is the contract; whether this is the right contract is a saga-lifecycle-vs-projection-state-vocabulary question. **Disposition at M5 retro:** confirm the lived behavior is correct, or specify a contract for the case (likely: start-handler should check `PendingSettlement.Status == Failed` and return `(null, empty)` to suppress the duplicate).

---

## What M5-S6 should know

**M5-S6 lands the three outbound RabbitMQ publish routes that M5-S5 deferred plus the Listings `CatalogListingView.Status = "Settled"` extension and ADR 014 authoring.** Per the M5 milestone doc §7 slice breakdown, S6 is "Seller Payout Notification (Relay Stub)" with `SellerPayoutIssued` as the focus; the M5-S6 prompt already exists at `docs/prompts/implementations/M5-S6-settlement-outbound-publish-routes-listings-catalog-extension-adr-014.md`. Concrete items S6 should walk in with:

1. **All three Settlement→cross-BC publish routes need wiring at S6.** `SettlementCompleted` → Listings (`listings-settlement-events`); `SellerPayoutIssued` → Relay-stub-or-test-fixture; `PaymentFailed` → deferred-to-post-M5-with-Operations consumer per the M5-S5 prompt's "Explicitly out of scope" item 3. The S6 prompt decides whether to wire the `PaymentFailed` route now or defer; default to wiring all three for queue-topology completeness even if the Relay / Operations consumers aren't shipping.

2. **`SettlementCompleted` → Listings `CatalogListingView.Status = "Settled"` extension is the third Path A read-model extension.** Triggers ADR-014 authoring per M3-S7's ADR candidate review and the M4 milestone §"ADR 014 authoring" — though the M4 path-A precedent surface (Session-membership fields + Withdrawn status) has not shipped, the M5-S6 application is independent and can stand on the M3-S6 lived ground alone for evidence.

3. **The `BidderCreditView` is queryable but has no cross-BC consumer until Relay ships.** S5 authored the projection, the handler, the schema registration, and five integration tests. Relay's `SettlementCompleted` broadcast (post-M5) will load the projection via `LoadAsync<BidderCreditView>(WinnerId)` to compose the `remainingCredit` field in the bidder-side push payload. S6 doesn't touch this surface — it's authored complete at S5.

4. **The lazy-init negative-credit sentinel is the Relay-side contract.** When Relay ships, its broadcast handler reads `BidderCreditView.RemainingCredit` verbatim. A negative value means "no prior `ParticipantSessionStarted` seed reached the projection before the `WinnerCharged` charge"; downstream UX can render this as "balance unavailable" or "credit deficit" per the post-M5 Relay design. The S6 retro should NOT need to revisit this contract — it's documented in the `BidderCreditView` docstring and asserted in the lazy-init test.

5. **The §9.1 happy-path integration test now asserts the lazy-init `BidderCreditView` row.** Per Open Question Q6 — the existing M5-S4 test was extended to assert `bidderCredit.RemainingCredit.ShouldBe(-85m)` and `bidderCredit.LastChargedSettlementId.ShouldBe(settlementId)`. If S6 changes the saga's `WinnerCharged` emission shape or the handler's lazy-init posture, the §9.1 assertion will fail before any S6-specific test does. The §9.2 BIN test and §9.3 failure test follow the same pattern — both either assert the absence of a `BidderCreditView` row (§9.3, since no `WinnerCharged` is emitted) or transitively cover the lazy-init path (§9.2's BIN happy path).

6. **The cross-BC fixture exclusion matrix is unchanged at M5-S5 close.** Settlement fixture excludes Selling / Auctions / Listings handlers; Participants fixture excludes Selling. No new handler classes shipped at M5-S5 (`BidderCreditViewHandler` is in the Settlement namespace, so foreign-BC fixtures' Settlement exclusions catch it). If S6's outbound publish routes surface a foreign-fixture failure, audit per the M5-S3 retro Key Learning #1.

7. **The `BidderCreditView` schema lives at `settlement.mt_doc_bidder_credit_view` (Marten convention).** Settlement schema now carries four document tables: `mt_doc_settlement_saga`, `mt_doc_pending_settlement`, `mt_doc_financial_event_stream`, `mt_doc_bidder_credit_view`. Plus the `mt_events_financial_event_stream` event-stream table. S6 doesn't add documents; the table layout is M5-final at S5 close.

8. **`PaymentFailed` is registered in `SettlementModule.cs`'s `AddEventType` block (since M5-S4).** It's now both appended to the financial event stream by `Handle(FailSettlement)` AND emitted via `OutgoingMessages`. The same dual-role as `SellerPayoutIssued` + `SettlementCompleted` from M5-S4; no new registration needed at S6 for this surface.

9. **Three M5-retro-disposition items are forwarded from S5 (above).** S6 may surface a fourth if the outbound publish routes' wiring uncovers anything; otherwise the M5 retro inherits these three.

10. **`wolverine-sagas.md` was not amended at M5-S5.** The existing "Multi-Phase Sagas with Self-Sent Continuation Commands" section (M5-S4) covers the failure-branch phase naturally. If S6's publish-route wiring surfaces a new sub-pattern (e.g., conditional publish based on saga state at terminal), defer the skill amendment to S6's retro.

---

## What remains / deferred into later M5 sessions

**In scope for M5, deferred to M5-S6:**

- `SettlementCompleted` cross-BC publish route + Listings `CatalogListingView.Status = "Settled"` extension
- `SellerPayoutIssued` cross-BC publish route (Relay-stub-or-test-fixture)
- `PaymentFailed` cross-BC publish route (or deferred-to-post-M5 per S6 decision)
- ADR-014 authoring (third Path A read-model extension precedent)
- M5 milestone retrospective — after S6 ships

**In scope for M5, surfaced for M5 retro disposition:**

- ADR 007 Gate 4 amendment (lived-fact closure or new trigger)
- W003 Phase 1 Part 7 lazy-init posture (document-as-intentional or workshop-update)
- Duplicate-`ListingSold`-after-Failed contract (saga-lifecycle-vs-projection-state)

**In scope for M5, deferred to a doc-cleanup pass (any milestone):**

- M5 milestone doc §2 wiring table — `ListingPassed` payload extension for `settlement-auctions-events` from M5-S3 still recorded as deferred-to-cleanup; M5-S5 didn't touch the queue but added the Participants→Settlement row.
- `marten-projections.md` — lazy-init-sentinel pattern callout (one instance is too thin a precedent).
- `critter-stack-testing-patterns.md` — direct-invocation pattern for state-guard tests; multi-phase saga integration test shape callout (carried from M5-S4 retro).

**Out of scope for M5, tracked elsewhere:**

- Real payment-processor integration — post-MVP per W003 §"Winner Charge"
- Compensation paths beyond MVP — post-MVP per W003 Phase 1 Part 3
- `BidderCreditView` cleanup on session expiry — post-MVP per W003 Phase 1 Part 7's final paragraph
- Future bidder-balance HTTP endpoint — post-MVP per W003 Phase 1 Part 7
- `ProcessManager<TState>` framework primitive — out of scope per ADR-019 (and now closed in PARKED-QUESTIONS as the load-bearing assumption is stale)

**Cumulative cross-BC handler isolation matrix at M5-S5 close** (unchanged from M5-S4):

| Fixture | Excludes |
|---|---|
| Participants | Selling |
| Selling | (none yet; Settlement-side exclusion not needed because PendingSettlementHandler is the only Settlement handler that consumes a Selling-published event, and Selling tests don't exercise that path) |
| Listings | Auctions, Selling |
| Auctions | Settlement (since M5-S3), Selling, Listings |
| Settlement | Selling, Auctions, Listings |

---

## Document history

- **v0.1** (2026-05-15): Authored at M5-S5 close as the slice retrospective. Three workstreams (failure paths, BIN source, BidderCreditView projection) plus the Participants contracts promotion pre-step landed in three commits per the prompt's commit sequence. 15 new tests added (8 failure-paths + 2 BIN-source + 5 BidderCreditView); the §9.1 happy-path test was extended with the lazy-init `BidderCreditView` assertion per Open Question Q6. Three M5-close-blocking items forwarded for M5 retro disposition: ADR 007 Gate 4 status, W003 Phase 1 Part 7 lazy-init posture, duplicate-`ListingSold`-after-Failed contract.
