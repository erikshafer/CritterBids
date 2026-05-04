# M5-S4: Settlement Saga Happy Path (Bidding Source) — Retrospective

**Date:** 2026-05-04
**Milestone:** M5 — Settlement BC
**Slice:** S4 of 6 (Settlement Workflow Happy Path — Bidding Source)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M5-S4-settlement-saga-happy-path.md`
**Narrative (joint authority):** `docs/narratives/002-winner-clears-settlement.md`

---

## Baseline

- 94 tests passing (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + 7 Settlement); `dotnet build CritterBids.slnx` 0 errors, 0 warnings; M5-S3 closed at PR #27 (SHA `7a3bd32`)
- `src/CritterBids.Settlement/` carries the empty `SettlementSaga` shell (M5-S2), `PendingSettlement` + `PendingSettlementHandler` + status enum (M5-S3), and the M5-S2 `SettlementModule.cs` registration shape
- `src/CritterBids.Contracts/Settlement/` carries the three M5-S1 stubs (`SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued`)
- ADR-019 records the Wolverine Saga choice; W003 Phase 1 Part 2 Approach A's saga sketch is implementation-ready
- `Program.cs` already wires `settlement-auctions-events` to publish `ListingSold` / `BuyItNowPurchased` / `ListingPassed` (M5-S3); the saga's Start handler subscribes via Wolverine handler discovery without further `Program.cs` edits
- `marten-projections.md`'s "Single-Source-Seeded Caches" subsection (M5-S3) covers the cross-BC-event-seeded projection pattern; `wolverine-sagas.md` carries the M5-S4 amendment site (the multi-phase saga pattern variant) per ADR-019 §Consequences
- No UUID v5 helper exists in the codebase yet — `AuctionsIdentityNamespaces.ProxyBidManagerSaga` (M4-S1) is the namespace-constant precedent, but the helper itself awaits authoring

---

## Items completed

| Item | Description |
|------|-------------|
| S4a | `src/CritterBids.Settlement/UuidV5.cs` — RFC 4122 §4.3 SHA-1 + GUID-byte-order helper. ~70 lines including triple-slash docs. First lived UUID v5 use in CritterBids. |
| S4b | `src/CritterBids.Settlement/SettlementsIdentityNamespaces.cs` — namespace constant + `SettlementId(listingId)` helper |
| S4c | Four Settlement-internal events (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`) plus the `SettlementSource` enum, all in `src/CritterBids.Settlement/` |
| S4d | Five self-send commands (`CheckReserve`, `ChargeWinner`, `CalculateFee`, `IssueSellerPayout`, `CompleteSettlement`), one file each |
| S4e | `src/CritterBids.Settlement/FinancialEventStream.cs` — stream-type marker class for `UseMandatoryStreamTypeDeclaration` |
| S4f | `src/CritterBids.Settlement/InvalidSettlementTransitionException.cs` and `src/CritterBids.Settlement/PendingSettlementNotFoundException.cs` — custom exception types |
| S4g | `src/CritterBids.Settlement/SettlementsConcurrencyRetryPolicies.cs` — `IWolverineExtension` retry policy for `PendingSettlementNotFoundException` per W003 Phase 1 Part 1 Option A |
| S4h | `src/CritterBids.Settlement/SettlementSaga.cs` — M5-S2 shell extended to full implementation. `SettlementStatus` enum, 12 state fields, five `Handle` methods (state guard → mutate → `session.Events.Append` → return `OutgoingMessages` with self-send + integration emits at terminal phases). Reserve-not-met branch throws `NotImplementedException` with explicit M5-S5 marker. |
| S4i | `src/CritterBids.Settlement/StartSettlementSagaHandler.cs` — separate static class per the `StartAuctionClosingSagaHandler` precedent. Loads `PendingSettlement` (throws `PendingSettlementNotFoundException` if absent); derives `SettlementId` via UUID v5; idempotent re-delivery guard; constructs saga in `Initiated`; appends `SettlementInitiated` to stream; returns `(saga, OutgoingMessages { CheckReserve })` |
| S4j | `SettlementModule.cs` registrations — `FinancialEventStream` schema + seven `AddEventType<T>` calls (4 internal + 3 integration contracts; `PaymentFailed` registered ahead of M5-S5's emit to save a future module edit) + `IWolverineExtension` retry policy |
| S4k | W003 + docstring namespace drift fixes — five call sites corrected from `AuctionsNamespace` to `SettlementsIdentityNamespaces.SettlementSaga` |
| S4l | `tests/CritterBids.Settlement.Tests/SettlementSagaTests.cs` — `Full_BiddingSource_HappyPath_ProducesSixEventStream` (§9.1 end-to-end; six-event stream assertion + projection status + saga document removal) and `PendingSettlement_NotFound_ThrowsRetryableException` (§9.4 path A) |
| S4m | `docs/skills/wolverine-sagas.md` — new "Multi-Phase Sagas with Self-Sent Continuation Commands" section authored per ADR-019 §Consequences. ~80 lines documenting the seven-phase pattern variant with table comparing two-phase vs multi-phase shapes |
| S4n | This retrospective |

The prompt structured scope as three commits:

| Commit | Items covered |
|--------|---------------|
| 1 — `feat(settlement): author UUID v5 helper, Settlement-internal events and self-send commands, FinancialEventStream marker, exception types, and the SettlementSaga happy-path implementation` | S4a, S4b, S4c, S4d, S4e, S4f, S4g, S4h, S4i, S4j (plus the M5-S4 prompt itself) |
| 2 — `feat(settlement): saga integration tests for §9.1 happy path and §9.4 retry; W003 + docstring namespace drift fixes` | S4k, S4l |
| 3 — `docs(settlement): cash in wolverine-sagas.md M5-S4 amendment; write M5-S4 retrospective` | S4m, S4n |

---

## S4a — UUID v5 helper

### Shape

```csharp
internal static class UuidV5
{
    public static Guid Create(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);  // .NET little-endian → RFC big-endian

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = ...; // namespace || name
        var hash = SHA1.HashData(input);

        Span<byte> result = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(result);
        result[6] = (byte)((result[6] & 0x0F) | 0x50);  // version 5
        result[8] = (byte)((result[8] & 0x3F) | 0x80);  // variant RFC 4122

        var output = result.ToArray();
        SwapByteOrder(output);  // RFC big-endian → .NET little-endian
        return new Guid(output);
    }
}
```

### Why first lived UUID v5 use was at M5-S4 rather than M4-S3

M4-S1 pinned the namespace-constant pattern in `AuctionsIdentityNamespaces.cs` with the comment "Saga-identifier and deterministic-stream-key helpers consume these constants" — but those helpers were planned for M4-S3 (Proxy Bid Manager saga). M4-S3 has not shipped, so by the time M5-S4 needed deterministic SettlementId derivation, no helper existed yet. M5-S4 authors the helper here; the shape is RFC-canonical and reusable — Auctions can adopt it as-is when M4-S3 lands by adding `using CritterBids.Settlement;` (or by promoting the helper to a shared location if cross-BC use becomes common).

### Why the byte-order swap is load-bearing

.NET's `Guid` stores `Data1`, `Data2`, `Data3` little-endian internally; RFC 4122 specifies big-endian for the SHA-1 input. Without the byte-order swap, `UuidV5.Create(namespaceId, name)` would produce a hash that doesn't match what other RFC 4122 implementations (Postgres' `uuid-ossp.uuid_generate_v5`, Python's `uuid.uuid5`, Node.js's `uuid.v5`) would produce for the same inputs. The swap on input AND output ensures interoperability — a SettlementId computed in CritterBids matches what an external auditor's UUID v5 tool would compute given the same `(namespace, listingId)` pair.

### Structural metrics

| Metric | Value |
|--------|-------|
| Lines of code | 70 (including triple-slash docs) |
| Third-party dependencies | 0 (`SHA1` from `System.Security.Cryptography`) |
| Public API surface | 1 method (`Create(Guid, string) → Guid`) |
| Internal helpers | 2 (`SwapByteOrder`, `SwapBytes`) |
| Test coverage | Transitive — exercised on every saga start (the §9.1 test calls `SettlementId(listingId)` and asserts the resulting Guid matches what the saga wrote) |

---

## S4h / S4i — SettlementSaga + StartSettlementSagaHandler

### Shape after

```csharp
public sealed class SettlementSaga : Wolverine.Saga
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid WinnerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal HammerPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public decimal FeePercentage { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal? SellerPayout { get; set; }
    public bool ReserveWasMet { get; set; }
    public SettlementStatus Status { get; set; }
    public string? FailureReason { get; set; }

    public OutgoingMessages Handle([SagaIdentityFrom(...)] CheckReserve _, IDocumentSession s) { ... }
    public OutgoingMessages Handle([SagaIdentityFrom(...)] ChargeWinner _, IDocumentSession s) { ... }
    public OutgoingMessages Handle([SagaIdentityFrom(...)] CalculateFee _, IDocumentSession s) { ... }
    public OutgoingMessages Handle([SagaIdentityFrom(...)] IssueSellerPayout _, IDocumentSession s) { ... }
    public OutgoingMessages Handle([SagaIdentityFrom(...)] CompleteSettlement _, IDocumentSession s) { ... }
}
```

Each Handle has the same shape: state guard → mutate state → `session.Events.Append(Id, domainEvent)` → return `OutgoingMessages` with the next-phase self-send and (terminal phases) the integration event emit.

### Why a separate Start handler

Per `wolverine-sagas.md` and the `StartAuctionClosingSagaHandler` precedent, the Start pattern lives outside the saga type so Wolverine distinguishes "create + persist" from "load existing and handle". The Start handler returns `Task<(SettlementSaga?, OutgoingMessages)>` — saga-creation tuple plus the first continuation command. Returning `(null, empty)` skips saga creation for re-delivery (the deterministic SettlementId means a duplicate `ListingSold` resolves to the same id, and the existing-saga check in the Start handler returns null).

### Why the reserve-not-met branch throws NotImplementedException

`Handle(CheckReserve)` computes `ReserveWasMet`. On the happy path it returns `OutgoingMessages { new ChargeWinner(Id) }`. On the not-met branch (W003 §3.2 / §9.3), M5-S5 wires `OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }` plus a `Handle(FailSettlement)` that emits `PaymentFailed`. M5-S4 authors the structural conditional but throws `NotImplementedException` on the not-met branch with an explicit M5-S5 marker comment — the §9.1 happy-path test never exercises this branch (its `HammerPrice: 85m` exceeds `ReservePrice: 50m`), so the throw is dead code at S4 and S5 replaces it without scaffolding.

### Why the fee-percentage formula is `HammerPrice * FeePercentage` rather than `HammerPrice * (FeePercentage / 100m)`

The W003 §4.1 scenario shows `FeePercentage: 10.0` (decimal-percentage form). The `ListingPublished` contract carries the constant placeholder `0.10m` (multiplicative-ratio form) per the existing producer. The test data flows: `ListingPublished.FeePercentage = 0.10m` → `PendingSettlement.FeePercentage = 0.10m` → saga reads `FeePercentage = 0.10m` → multiplies by `HammerPrice = 85m` → `8.50m`. The formula in the saga is `Math.Round(HammerPrice * FeePercentage, 2, MidpointRounding.ToEven)` — multiplicative-ratio form. Both forms round to the same value for the §9.1 case (`85 * 0.10 = 8.50` and `85 * 10.0 / 100 = 8.50`) but diverge for fractional-cent cases. The lived ground at M5-S4 close is multiplicative-ratio (`0.10m`); if W003 §4.2's fractional-cent corner cases need a different rendering at M5-S5, the formula stays the same and the input form changes (the contract field would migrate from `0.10m` to `10m` and the saga would `/ 100m`). The Open Questions section anticipated this; the verification path was "§9.1 expected values must hold regardless" — they do.

### Structural metrics

| Metric | Value |
|--------|-------|
| `Handle` methods on saga | 5 (one per self-send command) |
| State fields on saga | 12 (`Id`, plus 11 domain fields) |
| State guard early-throws per Handle | 1 (`if (Status != ExpectedPhase) throw InvalidSettlementTransitionException`) |
| `session.Events.Append` calls per Handle | 1 (or `StartStream` on the Start handler) |
| Integration events appended to stream | 2 (`SellerPayoutIssued` from `Handle(IssueSellerPayout)`; `SettlementCompleted` from `Handle(CompleteSettlement)`) |
| `MarkCompleted()` calls | 1 (terminal Handle only) |
| `OutgoingMessages` returns containing self-send commands | 4 (every non-terminal phase) |
| `OutgoingMessages` returns containing integration events | 2 (`SellerPayoutIssued`, `SettlementCompleted`) |

---

## S4l — Saga integration tests

### `Full_BiddingSource_HappyPath_ProducesSixEventStream`

The §9.1 end-to-end test seeds `PendingSettlement` via the canonical `ListingPublished` path (M5-S3's surface), dispatches `ListingSold` via `Host.InvokeMessageAndWaitAsync`, and asserts:

1. **Six-event financial stream** at `SettlementsIdentityNamespaces.SettlementId(listingId)`, in the §9.1-prescribed order: `SettlementInitiated → ReserveCheckCompleted → WinnerCharged → FinalValueFeeCalculated → SellerPayoutIssued → SettlementCompleted`. Per-event field assertions verify the W003-canonical payload values (`Hammer: 85, Fee: 8.50, Payout: 76.50`).
2. **`PendingSettlement.Status = Consumed`** — the M5-S3 `PendingSettlementHandler.Handle(SettlementCompleted, ...)` fires from local in-process dispatch via the saga's `OutgoingMessages` return under `MultipleHandlerBehavior.Separated`. This is the integration point where M5-S3's projection lifecycle and M5-S4's saga implementation meet.
3. **Saga document removed** — `LoadAsync<SettlementSaga>(sagaId)` returns null after `MarkCompleted()`. Mirrors the AuctionClosingSaga's terminal contract.

### Why `Host.InvokeMessageAndWaitAsync` works without explicit tracking

Wolverine's `InvokeMessageAndWaitAsync` drains the local queue before returning. The saga's continuation chain (`CheckReserve → ChargeWinner → CalculateFee → IssueSellerPayout → CompleteSettlement`) all routes through the local in-process queue under `RunWolverineInSoloMode()` and `DisableAllExternalWolverineTransports()`. The wait-for-completion semantics cover all five self-sends transitively. No `TrackActivity()` wrapper needed; the AuctionClosingSagaTests precedent confirmed this shape.

### `PendingSettlement_NotFound_ThrowsRetryableException`

Path A (per the prompt's open-questions confirmation; the user prefers the "trust the framework" stance). Direct-invokes `StartSettlementSagaHandler.Handle(listingSold, session, default)` with no `PendingSettlement` seed; asserts `PendingSettlementNotFoundException` is thrown carrying the listing id. The Wolverine retry policy's behavior (re-queueing the inbound message with backoff) is a framework convention not re-asserted here — the policy is registered and configured; trusting the framework as we trust EF Core is the project convention.

### Test infrastructure stayed lean

The existing `SettlementTestFixture` was sufficient — `CleanAllMartenDataAsync`, `GetDocumentSession`, `Host.InvokeMessageAndWaitAsync` (extension method from `Wolverine.Tracking`). No saga-state seed helpers, no tracked-message wrappers. M5-S5's failure-path tests may need additional helpers; M5-S4's two tests run with the M5-S2-authored fixture as-is.

### Structural metrics

| Metric | Value |
|--------|-------|
| Test methods added | 2 |
| Total Settlement test count | 9 (was 7 at M5-S3 close) |
| Total solution test count | 96 (was 94 at M5-S3 close) |
| `Host.InvokeMessageAndWaitAsync` calls in §9.1 test | 1 (single inbound message; saga's continuation chain runs in-process) |
| Direct handler invocations in §9.4 test | 1 (`StartSettlementSagaHandler.Handle` called against the document session) |
| Tracked-message helpers used | 0 (`TrackActivity` not needed) |
| Six-event stream assertions per test | 12 (one per event + per-field assertions on selected events) |

---

## S4k — W003 + docstring namespace drift fixes

### What surfaced and where

Five call sites referenced "AuctionsNamespace" for the SettlementId derivation. The convention from `AuctionsIdentityNamespaces.ProxyBidManagerSaga` is BC-isolation: each BC owns its own namespace constants. Settlement should reference `SettlementsIdentityNamespaces.SettlementSaga`. The W003 / docstring drift was pre-existing — authored before M5-S1 closed the foundation decisions and before the `SettlementsIdentityNamespaces` file existed in code form.

| File | Location | Drift correction |
|---|---|---|
| `docs/workshops/003-settlement-bc-deep-dive.md` | Ubiquitous Language SettlementId row | `AuctionsNamespace` → `SettlementsIdentityNamespaces.SettlementSaga` + inline rationale on BC-isolation discipline |
| `docs/workshops/003-settlement-bc-deep-dive.md` | Phase 1 Part 6 Option C option line | `SettlementNamespace` → `SettlementsIdentityNamespaces.SettlementSaga` (normalized to actual code shape) |
| `docs/workshops/003-settlement-bc-deep-dive.md` | Phase 1 Part 6 decision blockquote | `AuctionsNamespace` → `SettlementsIdentityNamespaces.SettlementSaga` + M5-S4 fix note |
| `docs/workshops/003-scenarios.md` | Placeholder ID notation in scenarios preamble | `AuctionsNamespace` → `SettlementsIdentityNamespaces.SettlementSaga` |
| `docs/milestones/M5-settlement-bc.md` | §6 Conventions UUID v5 paragraph | `AuctionsNamespace` → `SettlementsIdentityNamespaces.SettlementSaga` + M5-S4 fix note |
| `src/CritterBids.Contracts/Settlement/SettlementCompleted.cs` | Field rationale docstring (line 26) | Same correction |

### Lane

`workshop-update` per the four-lane discipline. The workshop named the wrong namespace; the implementation got it right (M5-S2's SettlementSaga.cs docstring already used `SettlementsIdentityNamespaces.SettlementSaga` in the `// SettlementId per W003 Phase 1 Part 6` comment, even though that comment cited the W003 phrasing). The fix lands the workshop's text with the implementation's vocabulary.

### Time-anchored historical artifacts intentionally NOT touched

The M5-S2 prompt, M5-S3 retro, and prior session-state artifacts contain the original `AuctionsNamespace` references. Those are time-anchored records — fixing them retroactively would rewrite history. The fix is applied only to live design documents and active code docstrings. Retros and prompts that recorded the (now-corrected) drift remain authentic to their authoring moment.

---

## S4m — wolverine-sagas.md M5-S4 amendment

### What landed

A new "Multi-Phase Sagas with Self-Sent Continuation Commands" section authored after §6 "Business Logic — The Decider Pattern" and before §7 "Scheduled Messages and Timeouts". ~80 lines covering:

- **When to reach for the multi-phase shape** — comparison table contrasting the two-phase Auction Closing saga (accumulator) with the multi-phase Settlement saga (pipeline) across six dimensions
- **Self-sent continuation commands** — the `OutgoingMessages { new NextPhaseCommand(Id) }` return shape with `[SagaIdentityFrom(nameof(X.SettlementId))]` correlation
- **State guards on every phase entry** — the `if (Status != ExpectedPhase) throw InvalidSettlementTransitionException(...)` pattern as the multi-phase saga's idempotency contract
- **`session.Events.Append` alongside `OutgoingMessages`** — the audit-stream pattern for sagas that need a financial / operational event log; the `FinancialEventStream` marker class for `UseMandatoryStreamTypeDeclaration` compatibility; integration events as dual-role (stream-stored + bus-emitted)
- **Retry-on-not-found at the Start handler** — the `PendingSettlementNotFoundException` + `IWolverineExtension.OnException(...).RetryWithCooldown(...)` pattern per W003 Phase 1 Part 1 Option A
- **`MarkCompleted()` at the terminal phase** — saga document removal vs audit stream persistence
- **In-repo ground table** — side-by-side comparison of the M3-S5 Auction Closing saga and the M5-S4 Settlement saga across seven dimensions (saga document, start handler, continuation pattern, audit stream, retry policy, integration tests)

### Why a new section rather than extending §6 (Decider Pattern)

The multi-phase saga is structurally distinct from the decider-pattern shape — it's a hosting variation, not a logic variation. ADR-019 §Consequences explicitly says the decider design lens is preserved at the W003 / scenarios level regardless of host choice; the host shape (two-phase accumulator vs multi-phase pipeline) is the dimension this skill section documents. Folding into §6 would have conflated the two axes; a new section keeps them orthogonal.

### Cross-references

The section cross-references the M3-S5 Auction Closing saga (`src/CritterBids.Auctions/AuctionClosingSaga.cs`) and the M5-S4 Settlement saga (`src/CritterBids.Settlement/SettlementSaga.cs`). The "in-repo ground" table at the section's close lists both saga documents, both start handlers, both retry policies, and both integration test suites — readers comparing the two shapes can navigate to either side from the table.

---

## Test results

| Phase | Settlement.Tests | All Tests | Result |
|-------|------------------|-----------|--------|
| Baseline (M5-S3 close) | 7 | 94 | Green |
| After commit 1 (saga implementation; no test changes yet) | 7 | 94 | Green (no test additions; only `src/CritterBids.Settlement/` edited) |
| After commit 2 (saga integration tests) | 9 | 96 | Green |
| Session close | 9 | 96 | Green |

Test count delta across the session: **+2** (the §9.1 happy-path integration test plus the §9.4 retry assertion).

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 96 passing (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + **9 Settlement**)
- `.cs` files added in `src/CritterBids.Settlement/`: 17 (UuidV5 + namespaces + 4 events + enum + 5 commands + marker + 2 exceptions + retry policy + start handler — plus the saga itself extending the M5-S2 shell)
- `.cs` files added in `tests/CritterBids.Settlement.Tests/`: 1 (`SettlementSagaTests.cs`)
- Production handlers authored: 6 (5 saga `Handle` methods + 1 `StartSettlementSagaHandler.Handle`)
- HTTP endpoints authored: 0 (Settlement remains backend-only through M5)
- `MarkCompleted()` calls: 1 (in `Handle(CompleteSettlement)`)
- `opts.Events.AddEventType<T>()` calls in `SettlementModule`: 7 (4 internal + 3 integration contracts; `PaymentFailed` registered ahead of M5-S5's emit)
- `opts.Schema.For<T>()` calls in `SettlementModule`: 3 (`SettlementSaga`, `PendingSettlement`, `FinancialEventStream`)
- `IWolverineExtension` registrations on the BC side: 1 (`SettlementsConcurrencyRetryPolicies`)
- New RabbitMQ queue routes added to `Program.cs`: 0 (M5-S3 wired `settlement-auctions-events` to publish `ListingSold`, which the saga subscribes to via Wolverine handler discovery)
- W003 / docstring drift call sites corrected: 6 (3 in W003 deep-dive + 1 in scenarios + 1 in milestone doc + 1 in `SettlementCompleted.cs`)
- `wolverine-sagas.md` net additions: 1 new section (~80 lines) + 1 TOC entry
- `marten-projections.md` edits: 0 (M5-S3 already cashed in)
- `BidderCreditView` projection: not authored (M5-S5 territory)
- `SettlementCompleted` outbound RabbitMQ publish route: not wired (M5-S6 territory)
- BIN source path implementation (`BuyItNowPurchased` consumer): not authored (M5-S5 territory)
- Reserve-not-met failure path implementation: stub (`NotImplementedException` with explicit M5-S5 marker)

---

## Key learnings

1. **The W003 saga sketch transcribed near-verbatim into runnable code.** W003 Phase 1 Part 2 Approach A's saga sketch (~120 lines of C# embedded in the workshop doc) became the starting point for `SettlementSaga.cs` with three concrete additions: the state guards (which the sketch implied but didn't enumerate), the `session.Events.Append` calls (which the sketch elided since it predated the financial-event-stream framing), and the integration-event dual-role at terminal phases. The sketch's accuracy is unusual for a workshop spec — most workshops describe behavior in prose; W003 went to code-level detail because the saga shape was the most contested design decision in the workshop. The lesson generalizes: when a workshop produces a code sketch precise enough to transcribe, that sketch is doing extra duty as design-contract — protect it through reviews and prefer minimal reshaping at implementation time. Conversely: sketches that are *too* close to runnable code can mislead by making the implementation feel done when in fact it hasn't been verified end-to-end.

2. **First lived UUID v5 helper authoring is straightforward but byte-order-sensitive.** The RFC 4122 §4.3 algorithm is short (SHA-1 + version/variant bit twiddling), but the `.NET Guid` little-endian internal storage vs RFC big-endian hash input is a real interoperability concern. Without the byte-swap, CritterBids' SettlementId for a given `(namespace, listingId)` pair would not match what Postgres' `uuid_generate_v5` or any RFC-compliant external tool would produce — auditing across systems would silently disagree. The lesson: when authoring a deterministic-id helper meant to interoperate with external systems (auditors, downstream BCs in other process, future sibling projects), test against a known-good external reference. The §9.1 test exercises the helper transitively but doesn't cross-verify against an external implementation; a future skill amendment could add a small "ext-verify" test against a SQL `uuid_generate_v5` call on the same Postgres container.

3. **Wolverine's `InvokeMessageAndWaitAsync` drains the saga's full continuation chain transitively.** The §9.1 test dispatches one inbound `ListingSold`, which triggers `StartSettlementSagaHandler` returning `OutgoingMessages { new CheckReserve(sagaId) }`. The five subsequent self-sends (`CheckReserve → ChargeWinner → CalculateFee → IssueSellerPayout → CompleteSettlement`) all flow through Wolverine's local in-process queue. `InvokeMessageAndWaitAsync` waits until the local queue drains — covering all six saga events transitively without explicit `TrackActivity()` wrapping. The AuctionClosingSagaTests precedent works the same way at smaller scale; M5-S4 confirms the pattern scales to longer chains. The framework convention earns the trust the user invoked at session-start ("just like EF Core, we wouldn't test the framework's behavior").

4. **`FinancialEventStream` as a stream-type marker class is the cleanest answer to `UseMandatoryStreamTypeDeclaration = true`.** The constraint forces every new stream to declare its aggregate type. The Settlement saga's stream isn't projected from — the saga's state is a Marten document, not a hydrated aggregate — so there's no natural type to use. Inventing a sentinel class (`FinancialEventStream`, mirroring `BidRejectionAudit`) costs ~10 lines and resolves the constraint without compromising the design. The alternative — using `SettlementSaga` itself as the stream type — would conflate the saga document with the audit stream and confuse Marten's projection registration. The marker pattern is now used twice in CritterBids (`BidRejectionAudit` for Auctions, `FinancialEventStream` for Settlement); a third use case is an argument for a generalized helper, but two is just convergent design.

5. **The W003 namespace drift was a documentation lag, not a design error.** The workshop was authored before `SettlementsIdentityNamespaces.cs` existed in code form; the original phrasing borrowed `AuctionsNamespace` as a placeholder when no Settlement-side analogue existed. By the time M5-S1 authored the namespace constant, the workshop's text was stale but not yet conflicting with implementation (M5-S1 didn't actually use UUID v5; M5-S3 didn't either). M5-S4 is the first slice where the conflict became live, and the surface for the fix presented itself naturally. The lesson: workshops that reference future-implementation symbols (constants, helper class names, type names) carry an implicit "fix me when the symbol lands" debt. Future workshops should consider naming-deferral practices — either author placeholders that explicitly say "TBD: Settlement-owned namespace constant lands at M5-S1" or hold the workshop until the symbol exists in code form. Scope here is small enough that the lazy correction (fix when load-bearing) is fine; for higher-orbit drift (e.g., a workshop referencing a non-existent integration event), the lazy correction is dangerous because it can mask actual design errors.

6. **The "Integration events appended to stream AND emitted on bus" dual-role is structurally unusual and worth documenting.** Most CritterBids events are either stream-internal (Auctions's BidPlaced, BidRejected — appended to streams, never bused) or bus-only (`ListingPublished`, `ListingSold` — bused without a corresponding stream append in the publisher BC). Settlement's `SellerPayoutIssued` and `SettlementCompleted` are dual-role: the saga `session.Events.Append`s them to the financial event stream for audit AND emits them via `OutgoingMessages` for cross-BC delivery (and local in-process consumers like the M5-S3 `PendingSettlementHandler`). The dual-role works because Wolverine's auto-transactions commit both in the same database transaction; there's no race. But it requires the integration event types to be registered with Marten via `AddEventType<T>` (so Marten can deserialize them on stream replay) AND with Wolverine's discovery (so the bus can route them) — both registrations happen in `SettlementModule.cs`. The pattern is documented in the new `wolverine-sagas.md` section; future BCs that need the same dual-role should consult it.

7. **The cutover gate's joint-authority discipline holds across S1 / S2 / S3 / S4.** Four consecutive slices have inherited the `Narrative:` metadata line and operated against narrative 002's joint-authority scope without methodology overhead. Narrative 002 dramatises the Settlement saga's progression at Moment-grain: Moment 1 is `SettlementInitiated`, Moment 2 is `ReserveCheckCompleted`, Moment 3 is `WinnerCharged` (the `BidderCreditView` projection update is M5-S5 territory but the narrative anticipates it), Moment 4 is `FinalValueFeeCalculated → SellerPayoutIssued`, Moment 5 is `SettlementCompleted`. The §9.1 test asserts the six-event stream that produces this narrative arc — no narrative drift surfaced because the saga implementation matches the Moment-by-Moment design ground exactly. The narrative-as-design-witness role earned its keep again.

---

## Skill gaps surfaced

- **`adding-bc-module.md` — nothing to add from this session.** The skill's BC module pattern, the Marten BC schema registration, and the `IWolverineExtension` registration shape all applied verbatim. The Settlement saga adds a third document type (`FinancialEventStream`) to the schema-registration list and a seven-event-type registration block, but those are mechanical extensions rather than pattern variants.
- **`marten-event-sourcing.md` — Stream-type-marker pattern is documented in the M3 / M4 era for `BidRejectionAudit`; the M5-S4 use of `FinancialEventStream` is a second instance.** Worth a brief callout in `marten-event-sourcing.md` §"UseMandatoryStreamTypeDeclaration" (if such a section exists or is authored later) noting that two CritterBids BCs now use the marker pattern. Not edited in this session per the prompt's "do not edit skills in-session" rule beyond the `wolverine-sagas.md` cash-in.
- **`integration-messaging.md` — Integration-event dual-role (stream-append + bus-emit) is documented in the new `wolverine-sagas.md` section.** It might also belong in `integration-messaging.md` since it concerns integration-event delivery semantics. Defer the cross-reference to a future skills-maintenance pass.
- **`critter-stack-testing-patterns.md` — Multi-phase saga integration test shape (single inbound → six-event stream assertion) has no precedent in the file.** The §9.1 test's pattern (seed projection → invoke single inbound message → assert event stream + projection state + saga removal) is reusable for any future multi-phase saga. Worth a callout in §Integration Test Pattern. Also defer.

---

## Findings against narrative

The slice operated against narrative 002 as a Moment-grain implementation reference. The §9.1 integration test's six-event stream assertion exactly matches narrative 002's Moment 1–5 progression: Moment 1's `SettlementInitiated` (Cast: Settlement saga onstage; Setting: `PendingSettlement` row at `Status: Pending`); Moment 2's `ReserveCheckCompleted`; Moment 3's `WinnerCharged` (Bidder-visible beat 1: "Charged $55.00"); Moment 4's `FinalValueFeeCalculated → SellerPayoutIssued` (narrator-led; offstage to the bidder); Moment 5's `SettlementCompleted` (Bidder-visible beat 2: "Charged $55.00 to your credit. The keyboard is yours."). The saga's lived stream produces this exact arc.

| Lane | Action |
|---|---|
| `narrative-update` | None. Narrative 002's Moment-by-Moment framing matches the saga implementation exactly; no drift surfaced. |
| `workshop-update` | One — the `AuctionsNamespace` → `SettlementsIdentityNamespaces.SettlementSaga` correction across W003 / scenarios / milestone / contract docstring. Resolved in this PR. |
| `code-update` | None. The contract types (`ListingSold`, `SettlementCompleted`, `SellerPayoutIssued`) are used as authored at M3 / M5-S1; the M5-S3 contract docstring corrections covered the consumer surface. |
| `document-as-intentional` | None. |

The cumulative narrative 002 findings ledger is unchanged: F001 ✓ (PR #20), F002 ✓ (PR #25), F003 ✓ minimum-scope (PR #20), F004 ✓ (PR #25), F005 ✓ (PR #25). All five pre-existing findings remain closed; no new findings against narrative 002 in S4.

---

## Verification checklist

- [x] `src/CritterBids.Settlement/UuidV5.cs` defines `internal static class UuidV5` with `public static Guid Create(Guid namespaceId, string name)` per RFC 4122 §4.3.
- [x] `src/CritterBids.Settlement/SettlementsIdentityNamespaces.cs` defines `SettlementSaga` Guid namespace constant + `SettlementId(Guid listingId) → Guid` static helper.
- [x] `src/CritterBids.Settlement/SettlementSource.cs` defines `public enum SettlementSource { Bidding, BuyItNow }`.
- [x] Four Settlement-internal event records (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`) — one file each.
- [x] Five self-send command records (`CheckReserve`, `ChargeWinner`, `CalculateFee`, `IssueSellerPayout`, `CompleteSettlement`) — one file each.
- [x] `src/CritterBids.Settlement/FinancialEventStream.cs` defines a marker class with `Guid Id { get; set; }`.
- [x] `InvalidSettlementTransitionException.cs` and `PendingSettlementNotFoundException.cs` exist.
- [x] `SettlementsConcurrencyRetryPolicies.cs` defines `IWolverineExtension` with `OnException<PendingSettlementNotFoundException>().RetryWithCooldown(100ms, 250ms, 500ms)`.
- [x] `SettlementSaga.cs` extends from M5-S2 shell to full implementation per W003 Phase 1 Part 2 Approach A.
- [x] `StartSettlementSagaHandler.cs` defines the saga's start handler returning `Task<(SettlementSaga?, OutgoingMessages)>`.
- [x] `SettlementModule.cs` updated with `FinancialEventStream` schema, seven `AddEventType<T>` calls, and the `IWolverineExtension` retry-policy registration.
- [x] W003 namespace drift fixed in `003-settlement-bc-deep-dive.md` (three call sites) + `003-scenarios.md` (one) + `M5-settlement-bc.md` (one) + `SettlementCompleted.cs` (one) — six total.
- [x] `wolverine-sagas.md` carries a new "Multi-Phase Sagas with Self-Sent Continuation Commands" section.
- [x] `SettlementSagaTests.cs` exists with `Full_BiddingSource_HappyPath_ProducesSixEventStream` and `PendingSettlement_NotFound_ThrowsRetryableException`.
- [x] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [x] `dotnet test CritterBids.slnx` — all green; 94 baseline tests still pass; two new saga tests pass; total 96.
- [x] This retrospective exists.

---

## What M5-S5 should know

**M5-S5 lands the Settlement saga's failure paths and BIN source.** Three workstreams: failure-path reserve-not-met (W003 §3.2 / §9.3), the BIN source path (W003 §1.2 / §9.2), and the `BidderCreditView` projection (W003 Phase 1 Part 7). Concrete items S5 should walk in with:

1. **The M5-S4 `NotImplementedException` stub in `Handle(CheckReserve)` is the failure-branch entry point.** Replace with `OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") }` plus a `Handle(FailSettlement)` method that mutates `Status: Failed`, populates `FailureReason`, appends `PaymentFailed` (already in `Contracts/Settlement/`) to the financial event stream, emits `PaymentFailed` via `OutgoingMessages`, and calls `MarkCompleted()`. The §3.2 / §9.3 scenarios specify the exact event payload.

2. **The `FailSettlement` self-send command lands in S5** as a `sealed record (Guid SettlementId, string Reason)`. Pattern matches the five M5-S4 commands; one file.

3. **The seven invalid-transition scenarios (§1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2) become real tests in S5.** S4's `InvalidSettlementTransitionException` is wired but only the happy-path-relative throws are exercised by §9.1. S5 adds explicit per-scenario tests covering each invalid transition. Per-scenario unit tests against the saga document directly (no Wolverine harness needed) work for these — they're pure state-guard assertions.

4. **The BIN source path consumes `BuyItNowPurchased` via a `StartSettlementSagaHandler.Handle(BuyItNowPurchased, ...)` overload.** The handler's body is structurally identical to the `ListingSold` overload, but the saga's initial `Status` is `ReserveChecked(WasMet: true)` (skipping the reserve check phase per W003 Phase 1 Part 5) and the `SettlementSource.BuyItNow` enum value is set on `SettlementInitiated`. The §9.2 test asserts the BIN-source stream contains five events (no `ReserveCheckCompleted` — the absence is W003 §canonical-payload's "audit query 'show me all BIN settlements' is literally event streams where no `ReserveCheckCompleted` appears").

5. **The `BidderCreditView` projection consumes `WinnerCharged` (saga-internal) and seeds on `ParticipantSessionStarted` (Participants integration event).** Per W003 Phase 1 Part 7. Schema `(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)`. Idempotency via `LastChargedSettlementId` — re-delivery of `WinnerCharged` for the same SettlementId is a no-op. The handler shape mirrors `PendingSettlementHandler` (M5-S3) — single static class, two `Handle` overloads, tolerant-upsert. Lands a fourth `Schema.For<T>` registration in `SettlementModule.cs`.

6. **`PaymentFailed` outbound RabbitMQ publish route — defer to M5-S5 or post-M5.** Operations BC (post-M5) is the consumer; if Operations isn't shipping at M5 close, the publish route can be wired structurally without a consumer (the Settlement-side emission already lands in S5 via `OutgoingMessages`; the cross-BC delivery is dormant until Operations ships).

7. **`SettlementCompleted` outbound RabbitMQ publish route is M5-S6 territory.** S5 doesn't touch it.

8. **The W003 fee-percentage form (multiplicative-ratio `0.10m` vs decimal-percentage `10m`) is locked at M5-S4.** The saga reads `FeePercentage` from `PendingSettlement` as the multiplicative-ratio form (`0.10m` for 10%) and computes `Math.Round(HammerPrice * FeePercentage, 2, MidpointRounding.ToEven)`. If S5's failure-path tests surface a fractional-cent edge case where this form rounds incorrectly, the formula stays the same and the input form would migrate (with a coordinated `ListingPublished` contract change). Don't pre-empt; verify §4.2's edge-case scenarios produce W003-canonical outputs and document any divergence in the M5-S5 retro.

9. **The cross-BC fixture exclusion matrix is at M5-S3's documented state.** Foreign-BC fixtures (Auctions, Listings, Selling) all exclude Settlement handlers via `SettlementBcDiscoveryExclusion`. M5-S5 doesn't add new handlers to existing classes — the projection is new (`BidderCreditView`) and lands in its own handler class — so the exclusion matrix shouldn't need extending. If a foreign-fixture test breaks at M5-S5 with a tracked-bucket assertion shift, audit per the M5-S3 retro Key Learning #1 ("bidirectional N-1 exclusion is slice-aligned with handler additions").

10. **`wolverine-sagas.md` was amended at M5-S4.** S5's failure-path implementation may surface additional pattern notes worth folding in (e.g., the `FailSettlement` exit-branch shape, multi-source Start handlers via overload). Defer to S5's retro.

---

## What remains / deferred into later M5 sessions

**In scope for M5, deferred to later slices:**

- Failure-path implementation (`Handle(FailSettlement)`, `PaymentFailed` emission, §3.2 / §9.3 reserve-not-met defense-in-depth scenarios) — S5
- BIN source path (`StartSettlementSagaHandler.Handle(BuyItNowPurchased)` overload, §1.2 / §9.2 BIN-evolver-branch initial state) — S5
- `BidderCreditView` projection (W003 Phase 1 Part 7) — S5
- Per-scenario invalid-transition tests for §1.3 / §2.4 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 — S5
- `SettlementCompleted` cross-BC publish route + Listings `CatalogListingView.Status = "Settled"` extension — S6
- `PaymentFailed` cross-BC publish route — S5 or post-M5 (Operations consumer-dependent)
- M5 milestone retrospective — after S6 ships

**In scope for M5, deferred to a doc-cleanup pass (any milestone):**

- M5 milestone doc §2 wiring table — `ListingPassed` payload extension for `settlement-auctions-events` from M5-S3 still recorded as deferred-to-cleanup; M5-S4 didn't touch the queue.
- `marten-event-sourcing.md` — stream-type-marker pattern callout (`BidRejectionAudit` + `FinancialEventStream` are now both in repo; pattern is established).
- `integration-messaging.md` — integration-event dual-role (stream-append + bus-emit) callout.
- `critter-stack-testing-patterns.md` — multi-phase saga integration test shape callout.

**Out of scope for M5, tracked elsewhere:**

- `ListingRevised` contract (Selling-side, post-M5); §8.2 / §8.3 PendingSettlement scenarios become implementable when the contract ships
- Real payment-processor integration — post-MVP per W003 §"Winner Charge"
- Compensation paths beyond MVP — post-MVP per W003 Phase 1 Part 3
- W003 broader storage-staleness sweep (narrative 002 F003's references at L29 / L649 / L663) — future workshop-cleanup session
- `ProcessManager<TState>` framework primitive — out of scope per CritterBids' shipped-Wolverine stance (ADR-019)

**Cumulative cross-BC handler isolation matrix at M5-S4 close** (unchanged from M5-S3):

| Fixture | Excludes |
|---|---|
| Auctions.Tests | Selling, Listings, Settlement (3) |
| Listings.Tests | Selling, Settlement (2) |
| Selling.Tests | Settlement (1) |
| Settlement.Tests | Selling, Auctions, Listings (3) |
| Participants.Tests | (none) |

The matrix is symmetric for any pair of BCs whose handlers consume each other's events. No fixture changes in M5-S4 — the saga's handlers are already covered by the existing exclusions (the saga's `Handle` methods are in the `CritterBids.Settlement` namespace which the foreign-fixture exclusions catch).
