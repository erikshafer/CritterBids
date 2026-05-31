# M7-S2: Operations BC Scaffold + First Consumer (Settlement Queue) - Retrospective

**Date:** 2026-05-31
**Milestone:** M7 - Operations BC
**Slice:** S2 - BC scaffold + first cross-BC consumer (settlement queue)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M7-S2-operations-bc-scaffold-first-consumer.md`

## Baseline

- Pre-slice: `CritterBids.Operations` did not exist in `src/` — the eighth and final MVP BC, never before scaffolded. Seven BC modules wired in `Program.cs`.
- Full solution test baseline: 203 passing across 9 test assemblies (Contracts 1, Api 1, Participants 6, Selling 36, Listings 20, Settlement 24→25, Auctions 65, Obligations 13, Relay 36). 0 build warnings.
- W006 §1 froze the settlement-queue field set, the Status-derivation rule, and the `PaidOut`-preservation guard before the slice opened; every field already traced to a `CritterBids.Contracts.Settlement` payload.
- `Program.cs` already published `PaymentFailed` to `operations-settlement-events` (M5-S6); `SettlementCompleted`/`SellerPayoutIssued` publish routes and the listen route were not yet wired.
- Operations declined OpenSpec at S1 — no `/opsx:` tasks; ADR 020 alone governs.

## Items completed

| Item | Description |
|------|-------------|
| S2.1 | `CritterBids.Operations` class library — `WolverineFx.Http.Marten`, ProjectRef to Contracts only, `InternalsVisibleTo` the test project; added to `CritterBids.slnx` alphabetically. |
| S2.2 | `SettlementQueueView` `sealed record` keyed by `SettlementId` with the exact W006 §1 field set + `SettlementQueueStatus { Failed, Completed, PaidOut }`. |
| S2.3 | `SettlementQueueHandler` — one Path A sibling class, three tolerant-upsert `Handle` overloads, returns `Task`, no `OutgoingMessages`, no `IMessageBus`. |
| S2.4 | Status-preservation guard (`PaidOut` no-regress) + set-once `ListingId`/`WinnerId`; `SellerId` last-write per W006 §1. |
| S2.5 | `AddOperationsModule()` — documents-only `ConfigureMarten` into the `operations` schema; no `AddMarten()`, no saga/aggregate. |
| S2.6 | `Program.cs` routing-only wiring — `using`, `IncludeAssembly`, `AddOperationsModule()`, the `operations-settlement-events` listen route + the two new publish routes; Api csproj ProjectRef. |
| S2.7 | `OperationsBcDiscoveryExclusion` added to exactly the two fixtures named by a red full-suite run (Obligations ×2 hosts, Settlement). |
| S2.8 | `CritterBids.Operations.Tests` — Testcontainers + Alba fixture (6 foreign-BC exclusions + `DisableAllExternalWolverineTransports()`); boots-clean + schema-mapping module tests; six end-to-end projection tests. |

## S2.3: Settlement-family consumer (Path A tolerant upsert)

**Why this approach.** One static sibling handler class with three `Handle(EventType, IDocumentSession, CancellationToken)` overloads — the lived shape of Listings' `SettlementStatusHandler` and Settlement's `PendingSettlementHandler`. Rejected a Marten multi-stream `EventProjection`: the inbound settlement firehose is not appended to any local Operations stream (milestone §3 non-goal), so events arrive as Wolverine message envelopes, not Marten stream replay. A projection would need a local event store the BC deliberately does not have.

**Handler shape after.**

```csharp
public static async Task Handle(SettlementCompleted message, IDocumentSession session, CancellationToken ct)
{
    var view = await LoadOrCreate(session, message.SettlementId, ct);
    session.Store(view with
    {
        ListingId    = view.ListingId == Guid.Empty ? message.ListingId : view.ListingId, // set-once
        WinnerId     = view.WinnerId  == Guid.Empty ? message.WinnerId  : view.WinnerId,  // set-once
        SellerId     = message.SellerId,                                                   // last-write
        HammerPrice  = message.HammerPrice, FeeAmount = message.FeeAmount, SellerPayout = message.SellerPayout,
        Status       = view.Status == SettlementQueueStatus.PaidOut
            ? SettlementQueueStatus.PaidOut : SettlementQueueStatus.Completed,             // no-regress guard
        LastUpdatedAt = Latest(view.LastUpdatedAt, message.CompletedAt),                   // latest-wins
    });
}
```

**Structural metrics.**

| Metric | Value |
|--------|-------|
| Handler class type | `static` |
| `Handle` overloads | 3 (one per Settlement event) |
| Return type | `Task` (no cascade) |
| `OutgoingMessages` / `IMessageBus` usage | 0 / 0 |
| Injected dependencies | `IDocumentSession` only |

**Edge cases preserved.**

- **`SellerId` is last-write, not set-once.** W006 §1 (line 83) lists `SellerId` as "null until completed" with **no** set-once guard. `SettlementCompleted` and `SellerPayoutIssued` both carry it and assign directly; `PaymentFailed` (no `SellerId` on the payload) leaves it untouched. An early plan draft added a `?? `-preserve guard; it was an unmandated extra and was removed.
- **`SellerPayoutIssued` first-arrival.** When the payout is the first event for a `SettlementId`, the constructed minimal row leaves `ListingId`/`WinnerId` at `Guid.Empty` and `HammerPrice`/`FeeAmount`/`SellerPayout`/`FailureReason` null; only the payout fields and `Status = PaidOut` are set. A later `SettlementCompleted` then enriches the set-once fields without regressing `Status`.
- **No Failed-regression guard.** W006 mandates the preservation guard only for `PaidOut → Completed`. `PaymentFailed` sets `Status = Failed` unconditionally; `Failed` and the success path are mutually exclusive on a real settlement, and a `PaymentFailed` arriving after `PaidOut` (a payout reversal) is intentionally allowed to flip the row to `Failed` for staff attention. This asymmetry is locked by `PaymentFailed_AfterPaidOut_SetsFailed_AndOlderTimestampDoesNotRewind`.
- **`FailureReason` lingers across the (spec-only) `Failed → Completed` transition.** W006 lists no reason-clear, so the field is not nulled by `SettlementCompleted`. Documented caveat: a future staff UI must key "currently failed" on `Status == Failed`, not on `FailureReason != null`.

## S2.7: Cross-BC discovery exclusion — empirical set

**Discovery.** With the Operations handler globally discovered (`Program.cs` `IncludeAssembly` + unconditional `AddOperationsModule()`), a red full-suite run named exactly two fixtures:

- **Obligations.Tests (11/13 red):**
  ```
  Wolverine.Runtime.Handlers.NoHandlerForEndpointException : No handlers for message type
  CritterBids.Contracts.Settlement.SettlementCompleted at this endpoint. This is usually because
  of 'sticky' handler to endpoint configuration.
  ```
  Root cause: under `MultipleHandlerBehavior.Separated`, adding a second `SettlementCompleted` consumer turned the fixture's direct `InvokeMessageAndWaitAsync(SettlementCompleted)` into a sticky-routing miss.
- **Settlement.Tests (1/25 red):** `SellerPayoutIssuedPublishRouteTests` — `payoutEvents should have single item but had 0`. The Operations co-consumer perturbed the outgoing-message tracking the publish-route test asserts.

**Resolution.** `OperationsBcDiscoveryExclusion : IWolverineExtension` excluding `t.Namespace?.StartsWith("CritterBids.Operations")`, registered in `ObligationsTestFixture`, `ObligationsLifecycleTestFixture` (shared exclusion class), and `SettlementTestFixture` — three host registrations, matching the lived `{TargetBc}BcDiscoveryExclusion` pattern.

**Why not also Listings.** Listings.Tests consumes `SettlementCompleted` too but stayed green — its settlement tests do not bus-dispatch the event through a tracked invoke that the extra handler would perturb. Per the prompt's open question, the exclusion was applied to exactly the fixtures a red run named, not by guesswork. **Known brittleness:** a future Listings (or other BC) test that bus-dispatches a shared settlement event may newly require the Operations exclusion; the symptom is the verbatim `NoHandlerForEndpointException` above.

## Test results

| Phase | Operations Tests | Full suite | Result |
|-------|-----------------|-----------|--------|
| Source + 4 initial tests | 4 | — | green |
| Full-suite red run (pre-exclusion) | 4 | Obligations 11 fail, Settlement 1 fail | red |
| After `OperationsBcDiscoveryExclusion` ×3 | 4 | 207 | green |
| After rubber-duck-driven test additions | 8 | 211 | green |

Final: **211 passing, 0 failing** across 10 test assemblies. Net test delta vs baseline: +8 (Operations.Tests is new). No existing test was deleted or weakened; the two transiently-red fixtures were restored by isolation, not by assertion change.

## Build state at session close

- `dotnet build` / `dotnet test`: 0 errors, 0 warnings (unchanged from baseline).
- Operations BC negative-space assertions:
  - `OutgoingMessages` returns in Operations source: 0
  - `IMessageBus` references in Operations source: 0
  - `AddMarten(` calls in `OperationsModule`: 0 (host owns the single one)
  - saga/aggregate registrations in `OperationsModule`: 0 (documents-only)
  - `opts.Events.AddEventType` in `OperationsModule`: 0 (pure message consumer, no local stream)
  - `[Authorize]` / `StaffOnly` / auth-scheme registrations added: 0
  - new `CritterBids.Contracts.*` types: 0
  - "Event"-suffixed type names / "paddle" references: 0
- `tracked.Sent.MessagesOf<…Settlement event…>()` asserted empty in `SetOnceFields_NotOverwritten_ByConflictingEvent_AndHandlerPublishesNothing` — the pure-consumer contract is test-guarded, not just code-reviewed.

## Key learnings

1. **A pure Path A consumer needs no event-graph registration.** Inbound integration events arrive as Wolverine message envelopes; the BC appends to no local stream, so `opts.Events.AddEventType<…>()` is unnecessary and would double-register types Settlement already owns. `ConfigureMarten` registers only the document type + schema.
2. **Adding a second consumer of a shared event silently rewires direct `InvokeMessageAndWaitAsync` under `Separated` mode.** A new globally-discovered handler can turn a previously-passing direct-invoke test in an unrelated BC fixture red with `NoHandlerForEndpointException`. The fix is a discovery exclusion in the affected fixture; the set must be found by a red run, never guessed.
3. **Set-once vs last-write must be read off the freeze field-by-field.** W006 §1 marks `ListingId`/`WinnerId` set-once but `SellerId` last-write; assuming a uniform guard would have introduced an unmandated `SellerId` preservation. Conflict tests (different IDs per event) are what actually prove the distinction — same-ID happy paths do not.
4. **Schema mapping deserves a direct assertion.** Behavior tests pass in any schema, so a silent regression of the `operations` schema to `public` is invisible to them. A one-line `information_schema` query (`table_name = 'mt_doc_settlementqueueview'` → `operations`) closes that gap cheaply.

## Findings against narrative

This slice anchors to **no narrative** (prompt `Narrative: none`). The settlement queue is the W006 §1 source-audit surface, not a narrative-008 moment — narrative 008 dramatises the dispute/escalation queue, which lands in S4. No narrative or workshop Document-History row is owed; W006 is a field freeze, not a behavior narrative. No follow-up narrative is warranted for S2.

## Spec delta - landed?

**Landed as written.** Per the prompt's `## Spec delta` (ADR 020): W006 §1's settlement-queue field freeze gained its first runnable, test-backed implementation — `SettlementQueueView` plus its Settlement-family Path A handler, seeded end-to-end against real Postgres via Testcontainers. The Settlement contracts `PaymentFailed`/`SettlementCompleted`/`SellerPayoutIssued` gained consume→upsert coverage with `Status` derived `Failed`/`Completed`/`PaidOut` and the `PaidOut`-preservation guard proven by a re-delivery test. The other four W006 views, all auth behavior, and every query endpoint remain unimplemented (S3–S6); no `CritterBids.Contracts.*` type was added. No narrative/workshop Document-History row is owed — W006 is a freeze, not a behavior narrative, and the settlement queue anchors to no narrative Moment, exactly as the prompt declared.

## Verification checklist

- [x] `CritterBids.Operations.csproj` exists; `WolverineFx.Http.Marten` reference matches sibling Marten BCs; ProjectRef to `CritterBids.Contracts`; `AssemblyInfo.cs` exposes internals to the test project.
- [x] `SettlementQueueView` is a `sealed record` keyed by `SettlementId` carrying exactly the W006 §1 field set; `SettlementQueueStatus` has `Failed`/`Completed`/`PaidOut`.
- [x] One Settlement-family Path A handler consumes all three events; each is a tolerant upsert; `Status` derived per W006 §1; `PaymentFailed` sets `FailureReason` and flags `Failed`; no `OutgoingMessages`, no `IMessageBus`.
- [x] `PaidOut`-does-not-regress guard implemented and asserted by re-delivering `SettlementCompleted` after `SellerPayoutIssued`.
- [x] `AddOperationsModule()` registers `SettlementQueueView` via `ConfigureMarten` in the `operations` schema; no `AddMarten()` in the module; no saga/aggregate.
- [x] `Program.cs` has the `using`, `Discovery.IncludeAssembly`, `AddOperationsModule()`, the `operations-settlement-events` `ListenToRabbitQueue()`, the two new publish routes, and `AutoProvision()`; Api csproj references the project; `CritterBids.slnx` carries both new nodes.
- [x] No `[Authorize]`/`StaffOnly`/auth-scheme registration anywhere; `Program.cs` auth state otherwise unchanged.
- [x] Each existing fixture that would discover the Operations handler registers an `OperationsBcDiscoveryExclusion`; no cross-BC handler leakage (empirical set: Obligations ×2, Settlement).
- [x] `CritterBids.Operations.Tests` contains a boots-clean test and the end-to-end projection test (real Postgres) covering the full `Failed → Completed → PaidOut` lifecycle and the regression guard — all green.
- [x] `dotnet build` passes (0 errors, 0 warnings); full `dotnet test` green (211) with no regressions.
- [x] No new `CritterBids.Contracts.*` type introduced.
- [x] This retro written with the `**Prompt:**` header and the `## Spec delta - landed?` paragraph.
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

- **S3** — lot board / bid-activity feed (`operations-auctions-events`, `operations-selling-events`).
- **S4** — `OperationsObligationsView` (escalation + dispute queues; narrative 008's surface).
- **S5** — session & participant activity board (`operations-participants-events`).
- **S6** — all auth: `StaffToken` scheme, `StaffOnly` policy, `[Authorize]` resumption, and staff query endpoints over the settlement queue (the view is proven via projection test in S2, not yet an HTTP surface). The no-default-scheme state is intentionally left as-is.
- **S7** — end-to-end cross-BC journey test, `Program.cs` route audit, `bounded-contexts.md` Operations status flip.
- **Watch (deferred, not a defect):** the read model is load-mutate-store without optimistic concurrency. Out-of-order delivery is covered; truly concurrent same-`SettlementId` delivery is not tested. Wolverine durable-inbox processing is serial per endpoint in the current topology, so this is a configuration assumption to re-confirm if the `operations-settlement-events` consumer is ever parallelised — not an S2 gap.
- **Watch (test brittleness):** the `OperationsBcDiscoveryExclusion` set is empirical. A future BC test that bus-dispatches a shared settlement event may newly require the exclusion; symptom is `NoHandlerForEndpointException` for the settlement event type.
