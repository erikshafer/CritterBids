# M5-S4: Settlement Saga Happy Path (Bidding Source)

**Milestone:** M5 ([Settlement BC](../../milestones/M5-settlement-bc.md))
**Slice:** S4 of 6 (Settlement Workflow Happy Path — Bidding Source)
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
**Agent:** @PSA
**Estimated scope:** one PR; ~16 files added (prompt + saga events × 4 + saga commands × 5 + helpers × 4 + start handler + retry policy + tests + skill amendment + retro), ~5 files modified (`SettlementSaga.cs`, `SettlementModule.cs`, `Program.cs` if any RabbitMQ adjustment, three contract / workshop docstrings for the namespace drift fix)

---

## Goal

Implement the Settlement saga's full bidding-source happy path per W003 Phase 1 Part 2 Approach A and §9.1's six-event stream. The saga starts on `ListingSold`, derives a deterministic UUID v5 `SettlementId` from the listing id, loads the `PendingSettlement` projection (M5-S3) for reserve / fee / seller fields, then walks the seven phases via self-sent continuation commands: `Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed`. Each phase appends one domain event to the financial event stream (`mt_events` keyed by `SettlementId`); the terminal phase emits `SettlementCompleted` and `SellerPayoutIssued` integration events via `OutgoingMessages` for downstream consumers (the M5-S3 `PendingSettlementHandler` updates the projection's status to `Consumed` via local in-process dispatch; cross-BC publish routes wire at S6).

The slice is the first CritterBids use of the deterministic UUID v5 pattern — it authors `UuidV5.Create(namespace, name)` (RFC 4122 SHA-1 helper, ~20 lines, pure function) and `SettlementsIdentityNamespaces.SettlementSaga` as the namespace constant. The `AuctionsIdentityNamespaces.ProxyBidManagerSaga` constant from M4-S1 is the existing namespace-constant precedent; the pure-function helper itself has no precedent until this slice authors it. M4-S3 (Proxy Bid Manager saga, originally planned as the helper's first author) has not shipped; M5-S4 is the first lived UUID v5 use in the codebase.

The slice also fixes a workshop docstring drift surfaced during M5-S3: W003 Phase 1 Part 6 and downstream contract docstrings reference `AuctionsNamespace` for the SettlementId derivation (`UuidV5(AuctionsNamespace, $"settlement:{ListingId}")`). The correct namespace is Settlement-side per the ProxyBidManagerSaga precedent (`SettlementsIdentityNamespaces.SettlementSaga`); the W003 reference appears to be a workshop drift from earlier authoring. This slice corrects W003 plus the `SettlementSaga.cs` and `SettlementCompleted.cs` docstring references in the same PR per the four-lane `workshop-update` discipline.

S4 walks in with the M5-S3 surface green: `PendingSettlement` projection seeded from `ListingPublished`, five-event handler maintaining the lifecycle, `settlement-auctions-events` queue listening for `ListingSold` / `BuyItNowPurchased` / `ListingPassed`, foreign-BC fixtures excluding Settlement handlers. The M5-S2 saga shell (`SettlementSaga : Wolverine.Saga` with `Guid Id` only) becomes the full implementation. The BIN source (§9.2), failure paths (§3.2 / §9.3), `BidderCreditView` projection, and integration-event RabbitMQ publish routes are S5 / S6 territory — S4 holds those branches as either deferred entirely (BIN, BidderCreditView, publish routes) or authored as `throw NotImplementedException` stubs with explicit S5 markers (the reserve-not-met branch in `Handle(CheckReserve)`).

This slice cashes in the `wolverine-sagas.md` skill amendment flagged at ADR-019 §Consequences — the seven-phase Settlement saga is structurally distinct from the M3-S5 Auction Closing saga's two-phase shape and earns its own pattern-variant subsection.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M5-settlement-bc.md` | Milestone scope — S4 deliverables are §1 Goal + §2 Cross-BC wiring (the saga's `ListingSold` entry point) + §6 Conventions (`[WriteAggregate]`-equivalent for sagas; UUID v5 SettlementId; `OutgoingMessages` discipline) |
| `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 2 Approach A | The W003 saga sketch — implementation-ready C# code for the saga document, Start handler, and per-phase Handle methods |
| `docs/workshops/003-scenarios.md` §1.1 / §2.1 / §2.3 / §3.1 / §4.1 / §5.1 / §6.1 / §9.1 / §9.4 | The seven happy-path decider scenarios (one per phase) plus the §9.1 end-to-end integration scenario plus the §9.4 retry-on-PendingSettlement-not-found scenario |
| `docs/decisions/019-settlement-workflow-hosting.md` | Settlement Workflow Hosting ADR — Option A chosen; pure-function helper extraction (Option C) deferred unless implementation surfaces a concrete pain |
| `docs/skills/wolverine-sagas.md` | Saga conventions + `[SagaIdentityFrom]` correlation + Start handler shape + `MarkCompleted` + the M5-S4 amendment site |
| `src/CritterBids.Auctions/AuctionClosingSaga.cs` and `src/CritterBids.Auctions/StartAuctionClosingSagaHandler.cs` | The lived precedent — Wolverine Saga with `[SagaIdentityFrom]` correlation, separate Start handler returning `Task<TSaga?>` for idempotent re-delivery, `bus.ScheduleAsync` for delayed messages, `MarkCompleted` at terminal state |
| `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` and `src/CritterBids.Auctions/BidRejectionAudit.cs` | Existing namespace-constant precedent + the stream-type-marker pattern for `UseMandatoryStreamTypeDeclaration = true` (`BidRejectionAudit` is the canonical raw-audit-stream marker class — Settlement's `FinancialEventStream` mirrors its shape) |

---

## In scope

### UUID v5 helper + namespace constant

- **`src/CritterBids.Settlement/UuidV5.cs`** — `internal static class UuidV5` with `public static Guid Create(Guid namespaceId, string name)` per RFC 4122 §4.3 (SHA-1 hash of namespace + name, with version-5 and variant-2 bit twiddling). Pure function; ~20 lines; no third-party dependency. The implementation handles the GUID-byte-order quirk (.NET `Guid` stores Data1/Data2/Data3 little-endian internally; RFC 4122 specifies big-endian for the hash input — the helper performs the byte swap on input and on output). Unit-testable; the smoke test for the helper itself can be folded into the saga's integration test (every saga start derives a SettlementId via this helper, so the helper is exercised transitively).

- **`src/CritterBids.Settlement/SettlementsIdentityNamespaces.cs`** — `internal static class SettlementsIdentityNamespaces` with one `public static readonly Guid SettlementSaga = new Guid("...")` constant. Generate the namespace Guid once at session start; the value is committed as a hard-coded literal (changing it would invalidate every existing deterministic SettlementId). Mirrors `AuctionsIdentityNamespaces.cs`'s shape verbatim. Add a static helper `public static Guid SettlementId(Guid listingId) => UuidV5.Create(SettlementSaga, $"settlement:{listingId}")` so the saga's call site reads `SettlementsIdentityNamespaces.SettlementId(message.ListingId)` rather than re-deriving the format string at every use.

### Settlement-internal events (4)

Author each in its own `.cs` file under `src/CritterBids.Settlement/`:

| File | Record signature | Source scenario |
|---|---|---|
| `SettlementInitiated.cs` | `(Guid SettlementId, Guid ListingId, Guid WinnerId, Guid SellerId, decimal Price, SettlementSource Source, decimal? ReservePrice, decimal FeePercentage, DateTimeOffset InitiatedAt)` | §1.1 (8 payload fields + InitiatedAt; `Source` enum value `Bidding` for S4) |
| `ReserveCheckCompleted.cs` | `(Guid SettlementId, decimal Price, decimal? ReservePrice, bool WasMet, DateTimeOffset CompletedAt)` | §2.1 / §2.3 (canonical stream-internal event; `Price` field name per W003 Field Name Convention from M5-S1's F002 amendment) |
| `WinnerCharged.cs` | `(Guid SettlementId, Guid WinnerId, decimal Amount, DateTimeOffset ChargedAt)` | §3.1 (Amount field name per W003 Field Name Convention) |
| `FinalValueFeeCalculated.cs` | `(Guid SettlementId, decimal HammerPrice, decimal FeePercentage, decimal FeeAmount, decimal SellerPayout, DateTimeOffset CalculatedAt)` | §4.1 (HammerPrice field name post-initiation) |

Plus the `SettlementSource` enum: `public enum SettlementSource { Bidding, BuyItNow }` in `src/CritterBids.Settlement/SettlementSource.cs`. S4 uses only `Bidding`; `BuyItNow` is the S5 evolver-branch input.

Each event record carries triple-slash docstrings naming: workshop scenario reference (§N.M), evolver state-shape interaction (which `SettlementState` phase it advances state from / to), and stream-internal designation (these events live in `src/CritterBids.Settlement/`, never cross BC boundaries — for that, the integration contracts in `src/CritterBids.Contracts/Settlement/` exist).

> **The two integration contracts (`SellerPayoutIssued`, `SettlementCompleted`) authored at M5-S1 are also appended to the financial event stream by the saga.** Per §9.1's six-event stream listing, those contracts are dual-role — stream-stored AND bus-published. The saga's terminal-phase handlers `Handle(IssueSellerPayout)` and `Handle(CompleteSettlement)` both `session.Events.Append` the contract event AND emit it via `OutgoingMessages` for the bus (for local in-process consumers like the M5-S3 `PendingSettlementHandler` and any cross-BC consumer once the publish route lands at S6).

### Saga self-send commands (5)

Author each in its own `.cs` file under `src/CritterBids.Settlement/`:

| File | Record signature | Source phase |
|---|---|---|
| `CheckReserve.cs` | `(Guid SettlementId)` | After `Initiated` |
| `ChargeWinner.cs` | `(Guid SettlementId)` | After `ReserveChecked(WasMet: true)` |
| `CalculateFee.cs` | `(Guid SettlementId)` | After `WinnerCharged` |
| `IssueSellerPayout.cs` | `(Guid SettlementId)` | After `FeeCalculated` |
| `CompleteSettlement.cs` | `(Guid SettlementId)` | After `PayoutIssued` (terminal) |

Each is a `sealed record` with a single `Guid SettlementId` field. The saga's Handle methods correlate via `[SagaIdentityFrom(nameof(CheckReserve.SettlementId))]` per the AuctionClosingSaga precedent — Wolverine's default convention looks for `{SagaName}Id`; explicit `[SagaIdentityFrom]` overrides it.

### Stream-type marker

- **`src/CritterBids.Settlement/FinancialEventStream.cs`** — `public class FinancialEventStream { public Guid Id { get; set; } }`. Marker class for `session.Events.StartStream<FinancialEventStream>(settlementId, ...)` under `UseMandatoryStreamTypeDeclaration = true`. Mirrors `BidRejectionAudit`'s shape. The class is never projected, never registered with `LiveStreamAggregation` — its sole purpose is satisfying Marten's mandatory stream-type-declaration rule. Triple-slash docstring explains the role.

### Invalid-transition exception

- **`src/CritterBids.Settlement/InvalidSettlementTransitionException.cs`** — custom exception thrown by the saga's Handle methods when a command arrives at an incompatible state. Carries the saga `SettlementId`, the command type name, and the current `SettlementStatus`. Triple-slash docstring names the §1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 invalid-transition scenarios it serves; S4 implements only the happy-path-relative throws (e.g. `CheckReserve` from already-`ReserveChecked` state); S5 fleshes out the rest.

### PendingSettlement-not-found exception + retry policy

- **`src/CritterBids.Settlement/PendingSettlementNotFoundException.cs`** — custom exception thrown by `StartSettlementSagaHandler.Handle` when `LoadAsync<PendingSettlement>(message.ListingId, ct)` returns null. Carries the listing id. Retryable per W003 Phase 1 Part 1 Option A.

- **`src/CritterBids.Settlement/SettlementsConcurrencyRetryPolicies.cs`** — `internal sealed class SettlementsConcurrencyRetryPolicies : IWolverineExtension` registering `OnException<PendingSettlementNotFoundException>().RetryWithCooldown(...)` per the Wolverine retry-policy pattern. Mirrors `AuctionsConcurrencyRetryPolicies`'s shape. Cooldown values: `100ms, 250ms, 500ms` (three retries with progressive backoff per W003 Phase 1 Part 1's "exponential backoff" framing — Wolverine's `RetryWithCooldown` takes a discrete sequence of `TimeSpan` values).

  > **The exception class also serves as the "saga already exists" idempotency guard's signal.** When `StartSettlementSagaHandler` finds an existing saga at the deterministic `SettlementId`, it returns `null` to skip saga creation per the AuctionClosingSaga precedent — no exception. The `PendingSettlementNotFoundException` is only thrown when the projection genuinely hasn't caught up yet.

### Saga implementation

- **`src/CritterBids.Settlement/SettlementSaga.cs`** — the M5-S2 empty shell becomes the full implementation per W003 Phase 1 Part 2 Approach A:

  - State fields: `Id`, `ListingId`, `WinnerId`, `SellerId`, `HammerPrice`, `ReservePrice` (nullable), `FeePercentage`, `FeeAmount` (nullable), `SellerPayout` (nullable), `ReserveWasMet`, `Status` (`SettlementStatus` enum), `FailureReason` (nullable string).
  - `SettlementStatus` enum: `Initiated`, `ReserveChecked`, `WinnerCharged`, `FeeCalculated`, `PayoutIssued`, `Completed`, `Failed`.
  - Five Handle methods (one per self-send command):
    - `Handle(CheckReserve, IDocumentSession)` — guard against `Status != Initiated`; compute `ReserveWasMet`; mutate state; `session.Events.Append(Id, new ReserveCheckCompleted(...))`; `OutgoingMessages { new ChargeWinner(Id) }` on happy path. **Reserve-not-met branch throws `NotImplementedException("Reserve-not-met failure path lands at M5-S5")` for S4 scope; S5 replaces with `OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }`.**
    - `Handle(ChargeWinner, IDocumentSession)` — guard against `Status != ReserveChecked`; mutate state; append `WinnerCharged`; emit `CalculateFee`. MVP credit-ledger posture per W003 — no real payment processor.
    - `Handle(CalculateFee, IDocumentSession)` — guard against `Status != WinnerCharged`; banker's rounding `Math.Round(HammerPrice * (FeePercentage / 100m), 2, MidpointRounding.ToEven)`; mutate state; append `FinalValueFeeCalculated`; emit `IssueSellerPayout`. Note: scenario §4.1's example uses `FeePercentage: 10.0` so the input is the decimal percentage (10 for 10%), not the multiplicative ratio (0.10). The `ListingPublished` contract field carries `0.10m` per the existing constant placeholder. **`PendingSettlement` stores `FeePercentage` as the contract-source value; the saga normalizes by multiplying by 100 if needed.** Verify at session start which form the saga reads — the implementation must produce `FeeAmount: 8.50` for `HammerPrice: 85.00, FeePercentage: 10.0` per §4.1 / §9.1.
    - `Handle(IssueSellerPayout, IDocumentSession)` — guard against `Status != FeeCalculated`; mutate state; append `SellerPayoutIssued` (the integration contract; appended to stream AND emitted on bus); emit `CompleteSettlement` self-send + `SellerPayoutIssued` integration event in `OutgoingMessages`.
    - `Handle(CompleteSettlement, IDocumentSession)` — guard against `Status != PayoutIssued`; mutate state; append `SettlementCompleted` (integration contract); emit `SettlementCompleted` integration event; call `MarkCompleted()`. Returns `OutgoingMessages` containing the integration event.

- **`src/CritterBids.Settlement/StartSettlementSagaHandler.cs`** — separate static class per the `StartAuctionClosingSagaHandler` precedent. Signature:

  ```
  public static async Task<(SettlementSaga?, OutgoingMessages)> Handle(
      ListingSold message,
      IDocumentSession session,
      CancellationToken cancellationToken)
  ```

  Body:
  1. Load `PendingSettlement` by `message.ListingId`. Throw `PendingSettlementNotFoundException` if absent (Wolverine retry policy catches).
  2. Compute `sagaId = SettlementsIdentityNamespaces.SettlementId(message.ListingId)`.
  3. Load existing `SettlementSaga` at that id; if present, return `(null, new OutgoingMessages())` for idempotent re-delivery (Wolverine skips saga creation).
  4. Construct new saga at `Status: Initiated` with fields populated from `message` and `pending`.
  5. `session.Events.StartStream<FinancialEventStream>(sagaId, new SettlementInitiated(...))` — first event on the financial stream.
  6. Return `(saga, new OutgoingMessages { new CheckReserve(sagaId) })` — Wolverine persists the saga at `sagaId` and dispatches the self-send.

  The signature returns `(SettlementSaga?, OutgoingMessages)` per Wolverine's convention for Start handlers that want both saga creation AND outgoing messages on first delivery. The Auctions Start handler returns `Task<AuctionClosingSaga?>` (no tuple) because that saga uses `bus.ScheduleAsync` for its first follow-up — a side-effect rather than a return value. The Settlement Start uses `OutgoingMessages` because the self-send is a regular bus message, not a scheduled one.

### SettlementModule registrations

- Add `opts.Schema.For<FinancialEventStream>().DatabaseSchemaName("settlement")` to the existing `ConfigureMarten` block (alongside the saga and projection registrations).
- Add `opts.Events.AddEventType<SettlementInitiated>()` plus the other three internal events plus the three integration contracts (`SellerPayoutIssued`, `SettlementCompleted`, `PaymentFailed` — even though `PaymentFailed` doesn't emit at S4, registering it now means S5 doesn't have to extend the module). Six `AddEventType` calls total.
- Register `services.AddSingleton<IWolverineExtension, SettlementsConcurrencyRetryPolicies>()` after the existing `services.ConfigureMarten` call. Mirrors `AuctionsModule`'s `IWolverineExtension` registration shape.

### W003 + docstring drift fixes

The `workshop-update` finding surfaced at S4 design-time: W003 Phase 1 Part 6 references "AuctionsNamespace" as the namespace input for the SettlementId derivation, but the namespace should be Settlement-side (`SettlementsIdentityNamespaces.SettlementSaga`) per the `AuctionsIdentityNamespaces.ProxyBidManagerSaga` precedent. Three fixes in this PR:

1. `docs/workshops/003-settlement-bc-deep-dive.md` — Phase 1 Part 6's `UuidV5(AuctionsNamespace, $"settlement:{ListingId}")` reference corrected to `UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")` (or analogous shape). Inline rationale clarifies the namespace ownership convention.
2. `src/CritterBids.Settlement/SettlementSaga.cs` — docstring reference (line 6) corrected.
3. `src/CritterBids.Contracts/Settlement/SettlementCompleted.cs` — docstring reference (line 26) corrected.

### Skill amendment

- **`docs/skills/wolverine-sagas.md`** — add a "Multi-Phase Sagas with Self-Sent Continuation Commands" subsection documenting the Settlement saga as a pattern variant. Cross-reference to M3-S5's Auction Closing saga as the two-phase precedent. Cover: per-phase Handle methods + state guards + `session.Events.Append` for stream audit + `OutgoingMessages` for self-sends + `MarkCompleted` at terminal state. Include the `[SagaIdentityFrom]` correlation idiom for non-`{SagaName}Id` field names. Per ADR-019 §Consequences's flag.

### Saga integration tests

- **`tests/CritterBids.Settlement.Tests/SettlementSagaTests.cs`** — `[Collection(SettlementTestCollection.Name)]` test class implementing `IAsyncLifetime`. Two `[Fact]`s:

  1. **`Full_BiddingSource_HappyPath_ProducesSixEventStream`** — §9.1 end-to-end:
     - Seed `PendingSettlement { Status: Pending, SellerId, ReservePrice: 50m, FeePercentage: 10m }` via `PendingSettlementHandler.Handle(ListingPublished, ...)` (the M5-S3 surface).
     - Dispatch `ListingSold { ListingId, WinnerId, HammerPrice: 85m, ... }` via `Host.InvokeMessageAndWaitAsync` — the saga starts, walks all five continuation phases via Wolverine's local queue, and reaches `MarkCompleted`.
     - Assert: financial event stream at `SettlementId` contains six events in order — `SettlementInitiated`, `ReserveCheckCompleted` (`WasMet: true`), `WinnerCharged` (Amount: 85), `FinalValueFeeCalculated` (FeeAmount: 8.50, SellerPayout: 76.50), `SellerPayoutIssued` (PayoutAmount: 76.50, FeeDeducted: 8.50), `SettlementCompleted` (HammerPrice: 85, FeeAmount: 8.50, SellerPayout: 76.50).
     - Assert: `PendingSettlement` row has `Status: Consumed` (the M5-S3 `PendingSettlementHandler.Handle(SettlementCompleted, ...)` fires from local in-process dispatch).
     - Assert: saga document at `SettlementId` is removed (Wolverine deletes saga documents at `MarkCompleted()`) — `LoadAsync<SettlementSaga>(sagaId)` returns null.

  2. **`PendingSettlement_NotFound_ThrowsRetryableException`** — §9.4 retry-policy assertion (path A per the prompt's open-questions confirmation):
     - Do NOT seed `PendingSettlement`.
     - Direct-invoke `StartSettlementSagaHandler.Handle(listingSold, session, ct)` — assert that `PendingSettlementNotFoundException` is thrown.
     - The retry policy's behavior (Wolverine re-queues the message) is a Wolverine convention; not asserted directly per the "trust the framework" stance. The exception throw IS the contract; the policy is what makes it retryable.

  Test infrastructure: the existing `SettlementTestFixture` is sufficient — it has `CleanAllMartenDataAsync`, `GetDocumentSession`, the three foreign-BC exclusions, and `Host.InvokeMessageAndWaitAsync` is available on `IAlbaHost.Services`. Add a tracked-message helper if the §9.1 assertion shape needs explicit `Host.ExecuteAndWaitAsync` (the saga's continuation loop needs Wolverine to drain its local queue before assertions fire — the Auctions saga tests use `TrackActivity().DoNotAssertOnExceptionsDetected().InvokeMessageAndWaitAsync(message)` for this).

### Session retrospective

- **`docs/retrospectives/M5-S4-settlement-saga-happy-path-retrospective.md`** — mirrors the M5-S3 retro shape. Records the saga shape choice (Option A only; deferred Option C), the §9.1 integration test pattern, the §1-§7 per-scenario test deferral rationale, the UUID v5 helper authoring, the W003 namespace drift fix, the `wolverine-sagas.md` cash-in, and a "what M5-S5 should know" handoff note.

---

## Explicitly out of scope

- **`BuyItNowPurchased` consumer (BIN source path).** §1.2 / §9.2. M5-S5 territory. The `SettlementSource.BuyItNow` enum value lands here for completeness, but the saga's `StartSettlementSagaHandler` only handles `ListingSold` in S4.
- **`PaymentFailed` integration event emission.** S5 territory. The contract type is registered at the module (so S5 doesn't have to re-touch it), but the saga never emits it in S4.
- **`FailSettlement` self-send command + `Handle(FailSettlement)` saga handler.** S5 territory. The reserve-not-met branch in `Handle(CheckReserve)` throws `NotImplementedException` with an explicit S5 marker comment.
- **§3.2 reserve-not-met defense-in-depth scenario.** S5 territory.
- **§1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 invalid-transition scenarios.** S4 implements `InvalidSettlementTransitionException` and basic guards (each Handle method throws on an incompatible status), but full per-scenario invalid-transition tests are S5 territory. The §9.1 happy-path test transitively exercises every valid-state guard; explicit invalid-state tests can come later.
- **Per-scenario pure-function decider/evolver tests for §1-§7.** Deferred under Option A per the prompt's open-questions confirmation. The §9.1 integration test covers the seven happy-path scenarios transitively. If S5's failure-path implementation surfaces a regression, per-scenario tests come back at S5's retrospective.
- **`BidderCreditView` projection.** W003 Phase 1 Part 7. M5-S5 territory.
- **`SettlementCompleted` outbound RabbitMQ publish route.** S6 wires `listings-settlement-events` for cross-BC delivery to Listings's catalog-status update. S4 emits the event via `OutgoingMessages` only — local in-process dispatch reaches the M5-S3 `PendingSettlementHandler`, but no cross-BC publish fires until S6.
- **`SellerPayoutIssued` outbound publish route.** Same as above. The Relay BC (post-M5) is the eventual consumer.
- **`PaymentFailed` outbound publish route.** S5 (Operations BC consumer) or post-M5 (Operations not yet shipped at M5 close).
- **Listings-side `CatalogListingView.Status = "Settled"` extension.** M5-S6 territory.
- **Real payment-processor integration.** Post-MVP per W003 §"Winner Charge". The MVP credit-ledger posture means `Handle(ChargeWinner)` is a state-mutation + event-emit only; no Stripe / Braintree / banking call.
- **Compensation paths if the seller payout fails to land.** Post-MVP per W003 Phase 1 Part 3.
- **The integration-event publish routes for the three Settlement contracts.** Already noted above; this is the catch-all reminder.
- **Contracts-project edits beyond the docstring drift fix.** All contract types this slice consumes already exist (`ListingSold` from M3, the three Settlement integration events from M5-S1).

---

## Conventions to pin or follow

- **Saga shape per W003 Phase 1 Part 2 Approach A (ADR-019).** Single mutable `SettlementSaga : Wolverine.Saga` document with `SettlementStatus` enum + nullable fields. Per-phase Handle methods directly mutate state. No pure-function `Decide` / `Evolve` helper extraction (Option C deferred unless a concrete pain surfaces).
- **`[SagaIdentityFrom(nameof(X.SettlementId))]` correlation per the AuctionClosingSaga precedent.** Each self-send command's Handle parameter is decorated; Wolverine's `{SagaName}Id` default convention does not apply (would be `SettlementSagaId`, ugly).
- **Separate `StartSettlementSagaHandler` static class.** Per the `StartAuctionClosingSagaHandler` precedent in `wolverine-sagas.md` — the Start pattern lives outside the saga type so Wolverine distinguishes "create + persist" from "load + handle".
- **`session.Events.Append` for stream audit + `OutgoingMessages` for bus messages.** The pattern: every domain event (4 internal + 2 integration) is appended to the financial event stream at `SettlementId` for audit, and integration events are also returned in `OutgoingMessages` so they reach local + cross-BC consumers via the bus. Self-send continuation commands are `OutgoingMessages`-only (no stream append). The first event uses `StartStream<FinancialEventStream>` (initial creation); subsequent events use `Events.Append`.
- **One file per event / command / class.** Per the user-confirmed convention from session-start scoping. Future event-subscription handlers (none in S4) would co-locate with their event's `.cs` file.
- **`Math.Round(HammerPrice * (FeePercentage / 100m), 2, MidpointRounding.ToEven)`** — banker's rounding per W003 §4.2's MVP convention.
- **UUID v5 derivation: `SettlementsIdentityNamespaces.SettlementId(listingId)`** — the static helper hides the `UuidV5.Create(SettlementSaga, $"settlement:{listingId}")` call. `AuctionsNamespace` references in W003 / docstrings are corrected to `SettlementsIdentityNamespaces.SettlementSaga` per the workshop-update finding.
- **Wolverine retry policy via `IWolverineExtension`.** `OnException<PendingSettlementNotFoundException>().RetryWithCooldown(100ms, 250ms, 500ms)` per the `AuctionsConcurrencyRetryPolicies` shape. Three retries with progressive backoff; W003 Phase 1 Part 1 calls for "exponential backoff" — `RetryWithCooldown` accepts a discrete sequence of `TimeSpan` values, which is the Wolverine idiom for "exponential" via three values.
- **Em-dash hygiene** is external-prose-only per memory `feedback_em_dash_scope.md`. Saga code, retro, skill amendment, prompt — all may use em dashes freely.

---

## Acceptance criteria

- [ ] `src/CritterBids.Settlement/UuidV5.cs` defines `internal static class UuidV5` with `public static Guid Create(Guid namespaceId, string name)` per RFC 4122 §4.3 (SHA-1 + version-5 + variant-2 bit twiddling + GUID-byte-order swap).
- [ ] `src/CritterBids.Settlement/SettlementsIdentityNamespaces.cs` defines `SettlementSaga` Guid namespace constant + `SettlementId(Guid listingId) → Guid` static helper that wraps `UuidV5.Create(SettlementSaga, $"settlement:{listingId}")`.
- [ ] `src/CritterBids.Settlement/SettlementSource.cs` defines `public enum SettlementSource { Bidding, BuyItNow }`.
- [ ] Four Settlement-internal event records in `src/CritterBids.Settlement/`: `SettlementInitiated.cs`, `ReserveCheckCompleted.cs`, `WinnerCharged.cs`, `FinalValueFeeCalculated.cs`. Each is a `sealed record` with the W003-canonical payload + triple-slash docstring.
- [ ] Five self-send command records in `src/CritterBids.Settlement/`: `CheckReserve.cs`, `ChargeWinner.cs`, `CalculateFee.cs`, `IssueSellerPayout.cs`, `CompleteSettlement.cs`. Each is a `sealed record` with `Guid SettlementId` only.
- [ ] `src/CritterBids.Settlement/FinancialEventStream.cs` defines a marker class with `Guid Id { get; set; }` for `UseMandatoryStreamTypeDeclaration` compatibility per the `BidRejectionAudit` precedent.
- [ ] `src/CritterBids.Settlement/InvalidSettlementTransitionException.cs` and `src/CritterBids.Settlement/PendingSettlementNotFoundException.cs` exist with appropriate `Exception`-derived class shape and triple-slash docstrings.
- [ ] `src/CritterBids.Settlement/SettlementsConcurrencyRetryPolicies.cs` defines `internal sealed class SettlementsConcurrencyRetryPolicies : IWolverineExtension` with `OnException<PendingSettlementNotFoundException>().RetryWithCooldown(...)` configured.
- [ ] `src/CritterBids.Settlement/SettlementSaga.cs` extends from the M5-S2 shell to the full implementation: state fields, `SettlementStatus` enum, five `Handle(SelfSendCommand, IDocumentSession)` methods with state guards + stream-append + outgoing-message emission per W003 Phase 1 Part 2 Approach A.
- [ ] `src/CritterBids.Settlement/StartSettlementSagaHandler.cs` defines the saga's start handler returning `Task<(SettlementSaga?, OutgoingMessages)>` per the prompt's body section.
- [ ] `src/CritterBids.Settlement/SettlementModule.cs` updated: `opts.Schema.For<FinancialEventStream>().DatabaseSchemaName("settlement")`; six `opts.Events.AddEventType<T>()` calls (4 internal + 3 integration); `services.AddSingleton<IWolverineExtension, SettlementsConcurrencyRetryPolicies>()`.
- [ ] `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 Part 6's `AuctionsNamespace` reference corrected to `SettlementsIdentityNamespaces.SettlementSaga`.
- [ ] `src/CritterBids.Settlement/SettlementSaga.cs` and `src/CritterBids.Contracts/Settlement/SettlementCompleted.cs` docstring references corrected to match.
- [ ] `docs/skills/wolverine-sagas.md` carries a new "Multi-Phase Sagas with Self-Sent Continuation Commands" subsection documenting the Settlement saga as a pattern variant of the M3-S5 Auction Closing saga's two-phase shape.
- [ ] `tests/CritterBids.Settlement.Tests/SettlementSagaTests.cs` exists; two `[Fact]`s — `Full_BiddingSource_HappyPath_ProducesSixEventStream` and `PendingSettlement_NotFound_ThrowsRetryableException`.
- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [ ] `dotnet test CritterBids.slnx` — all green; 94 baseline tests still pass; two new saga tests pass; total 96.
- [ ] `docs/retrospectives/M5-S4-settlement-saga-happy-path-retrospective.md` exists; mirrors the M5-S3 retro shape; records the saga shape choice, the §9.1 integration test pattern, the §1-§7 per-scenario test deferral rationale, the UUID v5 helper authoring, the W003 namespace drift fix, the `wolverine-sagas.md` cash-in, and a "what M5-S5 should know" handoff note.

---

## Open questions

- **`FeePercentage` units — decimal percentage (10 for 10%) vs multiplicative ratio (0.10 for 10%).** §4.1's example uses `FeePercentage: 10.0`; the `ListingPublished` contract field is documented as carrying the constant `0.10m` placeholder. The saga's `Handle(CalculateFee)` must produce `FeeAmount: 8.50` for `HammerPrice: 85.00` per §9.1. If `PendingSettlement.FeePercentage` is `0.10m`, the formula is `Math.Round(HammerPrice * FeePercentage, 2)`. If it's `10m` (percentage), the formula is `Math.Round(HammerPrice * (FeePercentage / 100m), 2)`. Verify the value `PendingSettlementHandler` writes (M5-S3) and pin the saga's formula to match. The acceptance criteria's verification path is: §9.1's expected values (`FeeAmount: 8.50, SellerPayout: 76.50`) must hold regardless of which form is in scope.
- **`Wolverine.Saga` document deletion semantics on `MarkCompleted()`.** The Auctions Closing saga's terminal handler calls `MarkCompleted()` and the test's `LoadSaga<AuctionClosingSaga>(listingId)` returns null. Verify that the same shape holds for Settlement at S4 — `Host.LoadSaga<SettlementSaga>(sagaId)` returns null after `Handle(CompleteSettlement)`. If it doesn't, the test's third assertion ("saga document at SettlementId is removed") needs adjusting; the contract behavior holds either way.
- **`Host.InvokeMessageAndWaitAsync(ListingSold)` vs `Host.TrackActivity().InvokeMessageAndWaitAsync(message)`.** The saga's continuation-command loop fires five self-sends through Wolverine's local queue between the inbound `ListingSold` and the terminal `MarkCompleted`. `InvokeMessageAndWaitAsync` should drain the local queue before returning per Wolverine's tracking convention; if it doesn't, the test needs `TrackActivity().IncludeExternalTransports()` or a longer timeout. Per the AuctionClosingSagaTests precedent (which dispatches a single `BiddingOpened` and tracks the saga's terminal `BiddingClosed` emission), `InvokeMessageAndWaitAsync` should suffice. Verify at session start; flag in retro if the §9.1 test needs a tracking adjustment.
- **The skill amendment's placement inside `wolverine-sagas.md`.** The existing skill file has sections covering single-message sagas, scheduled-message sagas, and the M3-S5 Auction Closing saga. The Settlement saga is a "multi-phase progression with self-sent continuation commands" pattern. Two paths: (a) new top-level subsection between existing subsections; (b) extension of the existing Auction Closing example with a "compare to Settlement's seven-phase shape" callout. Lean (a) — the Settlement pattern is structurally distinct and earns its own subsection. Confirm at session start.

---

## Commit sequence

Three commits, in this order:

1. `feat(settlement): author UUID v5 helper, Settlement-internal events and self-send commands, FinancialEventStream marker, exception types, and the SettlementSaga happy-path implementation`
2. `feat(settlement): wire SettlementsConcurrencyRetryPolicies and SettlementModule registrations; saga integration tests for §9.1 happy path and §9.4 retry; W003 + docstring namespace drift fixes`
3. `docs(settlement): cash in wolverine-sagas.md M5-S4 amendment; write M5-S4 retrospective`

The saga implementation lands in commit 1 as a self-contained set of new files in `src/CritterBids.Settlement/` plus the saga shell extension. Commit 2 lands the integration surface — module registrations, retry policy, the two integration tests, and the workshop-update fixes that surfaced at session-start scoping. Commit 3 is docs-grade and naturally bundles.
