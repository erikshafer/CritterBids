# M6-S7: End-to-End Integration + Housekeeping - Retrospective

**Date:** 2026-05-30
**Milestone:** M6 - Obligations BC + Relay BC (post-sale coordination + real-time relay)
**Slice:** S7 - End-to-end fan-out integration + M6 close-out housekeeping
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M6-S7-end-to-end-integration-housekeeping.md`

## Baseline

- `dotnet build CritterBids.slnx`: **0 errors / 0 warnings** at session open.
- `dotnet test CritterBids.slnx`: **202 passed / 0 failed / 0 skipped** (Contracts 1, Api 1,
  Participants 6, Listings 20, Selling 36, Settlement 25, Obligations 13, Relay 35, Auctions 65).
- Docker **available** (server 29.4.3) — Testcontainers tests ran this session (unlike the S6
  session, which recorded a Docker-blocked full suite).
- Structural starting facts the diff is measured against: Relay's three participant-push handlers and
  `NotificationHistoryView` exist (S5/S6); Obligations' `PostSaleCoordinationSaga` +
  `ObligationStatusView` exist (S3); `Program.cs` already wires all seven M6 RabbitMQ routes. No
  composed Obligations+Relay host existed — each BC was only ever tested behind its own
  sibling-exclusion fixture.

## Items completed

| Item | Description |
|------|-------------|
| S7.1 | End-to-end fan-out test: one `SettlementCompleted` → Obligations saga start **and** Relay winner push, asserted in one run |
| S7.2 | Composed host (`PostSaleFanOutTestFixture`) running Settlement-publish + Obligations + Relay together, without weakening any per-BC exclusion fixture |
| S7.3 | `Program.cs` seven-route topology audit against milestone §5 — verification only |
| S7.4 | M6-close test-count baseline recorded (this retro) |
| S7.5 | This slice retrospective |
| S7.6 | Document History rows on narratives 001 and 006 |

## S7.1 / S7.2: Composed fan-out test + fixture

**Why this approach.** The two halves have conflicting host needs that no existing fixture satisfies
at once. The Relay push needs a **real Kestrel** host — SignalR's WebSocket transport cannot run
under Alba's in-memory `TestServer` (the `RelayHubTestFixture` precedent; `wolverine-signalr` SKILL
§9). The Obligations saga + `ObligationStatusView` Inline projection need **Marten**. The
per-BC fixtures deliberately *exclude* the sibling BC, so neither could observe a fan-out. The fixture
is therefore the **inverse** of the exclusion fixtures: a new dedicated host that merges
`RelayHubTestFixture`'s real-Kestrel shape with a Testcontainers Postgres + Marten store, with **both**
`AddObligationsModule()` and `AddRelayModule()` active and only the other five BCs excluded. The
existing exclusion fixtures were left untouched (acceptance criterion 2).

Rejected alternative — putting the test in `CritterBids.Api.Tests` against the full `Program.cs`:
that host boots all eight BCs (every BC's schema, DI, and RabbitMQ wiring) and still cannot host a
`HubConnection` through Alba. A narrow composed host is both SignalR-capable and free of the other
BCs' handler co-consumption.

**Sibling-consumer fidelity.** `MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination`
mirror `Program.cs` so the Obligations and Relay `SettlementCompleted` handlers run as two independent
destinations off one local publish — a true fan-out, not a single co-handled execution. This is the
structural claim the test exists to prove: **siblings off one event, not a chain.** `MessageIdentity`
is the bit that matters — without it the durable inbox would dedupe the fanned-out copies and only one
handler would run.

**Determinism — no real-clock waits.** The publish is driven through
`Host.TrackActivity().Timeout(30s).PublishMessageAndWaitAsync(...)`, which drains both separated
handler queues and commits the Inline `ObligationStatusView` projection before returning; the push is
observed on a `TaskCompletionSource` with a 10s failsafe timeout. Default Production
`ObligationsOptions` durations (days) mean the saga's `bus.ScheduleAsync` reminder/escalation timers
target day-out instants and never fire during the run — so tracking does not wait on them and there is
no `Task.Delay` / clock polling anywhere.

**Deterministic-key assertion without crypto duplication.** The test proves the deterministic
UUID-v5 `ObligationId` *structurally* rather than recomputing the RFC-4122 hash in-test (the
production `UuidV5` / `ObligationsIdentityNamespaces` helpers are `internal`, with `InternalsVisibleTo`
only to Obligations.Tests where the literal derivation stays unit-covered): it asserts the
`ObligationStatusView`, its backing event stream, and the `PostSaleCoordinationStarted` event all
share the one key (`started.ObligationId == view.Id`, `started.ListingId == listingId`). No
second-publish idempotency proof was attempted — saga-start re-dispatch is not the supported duplicate
path, and Relay has no idempotency guard, so a duplicate publish would double-push by design.

**Marten config matches the sibling fixtures** (`EventAppendMode.Quick`,
`UseMandatoryStreamTypeDeclaration = true`, `ApplyAllDatabaseChangesOnStartup`,
`IntegrateWithWolverine(UseFastEventForwarding = true)`). Mandatory stream-type declaration is
satisfied: Obligations declares its stream type on `StartStream`, and Relay's history writer uses
`IDocumentSession.Store` (a document upsert, not an event append), so it is unaffected.

### Resulting fixture shape

```
PostSaleFanOutTestFixture (real Kestrel + Testcontainers Postgres)
  AddMarten(...).UseLightweightSessions().IntegrateWithWolverine(UseFastEventForwarding)
  AddObligationsModule()    // saga + ObligationStatusView projection
  AddRelayModule()          // BiddingHub + SettlementCompletedHandler push
  UseWolverine:
    MultipleHandlerBehavior.Separated
    Durability.MessageIdentity = IdAndDestination
    Durability.MessageStorageSchemaName = "wolverine"
    IncludeAssembly(Obligations) + IncludeAssembly(Relay)
    exclude Selling/Auctions/Listings/Settlement/Participants (reuse existing extensions)
    DisableAllExternalWolverineTransports + RunWolverineInSoloMode
    Policies.AutoApplyTransactions + UseDurableLocalQueues
  MapHub<BiddingHub>("/hub/bidding")
```

## S7.3: Program.cs route-topology audit

Verification only — **no defect found, no `Program.cs` change.** All seven M6 §5 routes are wired
with the correct direction:

| Queue | Publish | Listen | Program.cs |
|-------|---------|--------|-----------|
| `obligations-settlement-events` | `SettlementCompleted` | yes | L188–190 |
| `relay-obligations-events` | 5 Obligations contracts | yes | L198–216 |
| `relay-participants-events` | `ParticipantSessionStarted` + `SellerRegistrationCompleted` | yes | L49–53 |
| `relay-selling-events` | `ListingPublished`/`Revised`/`EndedEarly` | yes | L62–68 |
| `relay-auctions-events` | 14 Auctions/Selling contracts | yes | L225–253 |
| `relay-settlement-events` | `SellerPayoutIssued` + `SettlementCompleted` | yes | L172, L261–263 |
| `relay-listings-events` | `LotWatchAdded`/`Removed` | yes | L265–269 |

The dual `SettlementCompleted` + `SellerPayoutIssued` publish to `relay-settlement-events` is the
**accepted** state per the in-file M6-S5 comment (L255–260) and the M6 exit criteria — **not** flagged
as drift against the §5 table's `SellerPayoutIssued`-only row. The audit verifies required
listen/publish sides exist, not event-exhaustiveness.

## Test results

| Phase | Relay Tests | Full solution | Result |
|-------|-------------|---------------|--------|
| Baseline (session open) | 35 | 202 | green |
| After `PostSaleFanOutTests` added | 36 | 203 | green |

The new fan-out test ran green in isolation (1/1, ~1s) and within the full Relay suite (36/36) and the
full solution (203/203). **Test count delta from S6 baseline: +1** (the single composed fan-out test).
No regressions. Docker was available, so the full Testcontainers suite executed — no blocked command
to record this session.

## Build state at session close

- `dotnet build CritterBids.slnx`: **0 errors / 0 warnings** (unchanged from baseline).
- Negative-space assertions (the slice added **no** new behaviour):
  - New production BC source files: **0**.
  - `Program.cs` lines changed: **0** (audit found no defect).
  - New saga transitions / commands / contracts / internal events / projections / hub handlers: **0**.
  - Existing `{TargetBc}BcDiscoveryExclusion` fixtures weakened or removed: **0**.
  - New files are test-only: `PostSaleFanOutTestFixture.cs`, `PostSaleFanOutTestCollection.cs`,
    `PostSaleFanOutTests.cs` (+ one `ProjectReference` to `CritterBids.Obligations` in
    `CritterBids.Relay.Tests.csproj`).
  - In-test UUID-v5 recomputation: **0** (deterministic key proven structurally).
  - `Task.Delay` / real-clock polling in the new test: **0**.

## Key learnings

1. **The composed-host fixture is the structural inverse of the sibling-exclusion fixture.** To prove
   a fan-out you include exactly the two consuming BCs and exclude the rest; to prove a single BC in
   isolation you exclude all siblings. Both reuse the same `{Bc}BcDiscoveryExclusion` extension set —
   the difference is only which BCs you leave out.
2. **`MessageIdentity.IdAndDestination` is load-bearing for fan-out tests, not just production.** A
   composed test host that omits it will silently dedupe the fanned-out message and observe only one
   of the two sibling handlers — a green-looking single-consumer test masquerading as a fan-out.
3. **Prove deterministic stream identity by shared key, not by recomputation.** When the derivation
   helper is `internal`, asserting that view / stream / start-event share one id is stronger and
   cheaper than duplicating ~30 lines of RFC-4122 hashing in the test, and it keeps the literal
   derivation's single source of truth in the owning BC's unit tests.
4. **A document-writing consumer is compatible with `UseMandatoryStreamTypeDeclaration`.** Relay's
   `IDocumentSession.Store` history writer needs no stream-type declaration; only event appenders do.
   Composing a document-only BC alongside an event-sourced BC under mandatory declaration is safe.

## Findings against narrative

Anchored to narratives 001 (winner `BiddingHub` settlement push) and 006 (Obligations saga start).
**No drift surfaced** — the fan-out behaved exactly as both narratives describe, so no
`narrative-update`, `workshop-update`, or `code-update` finding was raised.

- **Narrative 001** — `document-as-intentional`: the winner-facing `SettlementCompleted` push (Moment
  8), previously *partially lived* after S5, is now end-to-end journey-covered as one half of the
  fan-out. Recorded as a **v0.4** Document History row.
- **Narrative 006** — `document-as-intentional`: the `SettlementCompleted → PostSaleCoordinationStarted`
  saga-start join (Moment 1) is now end-to-end integration-covered as the other half of the fan-out.
  Recorded as a **2026-05-30** Document History row. The S3-flagged carrier-on-tracking drift is
  unrelated to S7 and remains an S4-scope additive contract item — untouched here.

## Spec delta - landed?

Landed as written, no divergence. Per ADR 020 and the S7 prompt's `## Spec delta` section, this slice
carried **no new requirement and no new OpenSpec change** — the `add-obligation-lifecycle` change
remains complete and archived (2026-05-29). The only spec-shaped delta was Document History coverage
in two narratives, and **both rows landed**: narrative 001 gained a v0.4 row recording the winner
settlement push is now covered end-to-end as part of the fan-out (completing the partial S5 landing),
and narrative 006 gained a 2026-05-30 row recording the `SettlementCompleted → PostSaleCoordinationStarted`
join is now end-to-end test-covered (see each file's § Document History). The fan-out test is **green**
and deterministic; the `Program.cs` route audit **passed with no correction** (all seven §5 routes
correctly wired; the accepted dual `relay-settlement-events` publish was confirmed, not flagged). Still
tracked but explicitly **out of this slice**: the M6-S6 prompt backfill, the owed
`docs/skills/wolverine-signalr/SKILL.md` lived-Relay update, the Relay OpenSpec ledger row, and the M6
milestone retrospective (which lands in a dedicated M6-close session per the M2/M3/M4 precedent).

## Verification checklist

- [x] End-to-end fan-out test asserts **both** the Obligations saga start (`PostSaleCoordinationStarted`
  for the deterministic `ObligationId`; `ObligationStatusView` = `AwaitingShipment`) **and** the Relay
  `SettlementCompletedNotification` push to the winner's `BiddingHub` group — green, deterministic, no
  real-clock waits.
- [x] Test composes Settlement-publish + Obligations + Relay in one real-SignalR-capable host without
  removing or weakening any `{TargetBc}BcDiscoveryExclusion` fixture.
- [x] `Program.cs` has `ListenToRabbitQueue(...)` for each Relay inbound queue +
  `obligations-settlement-events`, each with a matching publish route; the dual
  `SettlementCompleted` + `SellerPayoutIssued` publish to `relay-settlement-events` recorded as
  accepted, not drift.
- [x] No production BC source file changed; `Program.cs` unchanged (audit found no defect). No new
  saga transition, command, contract, internal event, projection, or hub handler (negative-space
  assertions recorded above).
- [x] `dotnet build` passes (0/0). Full `dotnet test CritterBids.slnx` green (203/203), no regressions;
  Docker available so the full Testcontainers suite ran — no blocked command to record.
- [x] Narratives 001 and 006 each gained a Document History row recording the end-to-end fan-out
  coverage.
- [x] This retro written with `## Spec delta - landed?`, `## Findings against narrative` (001/006), the
  M6-close test-count baseline, and the explicit still-tracked note (S6-prompt backfill, owed
  `wolverine-signalr` skill update, Relay OpenSpec ledger row, M6 milestone retro).
- [x] No commit to `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

**Out of scope, tracked elsewhere (confirmed still open at M6 close):**

- **M6 milestone retrospective + skills extraction** — lands in a dedicated `retrospective-skills-m6-close`
  session per the M2/M3/M4 precedent.
- **M6-S6 prompt backfill** — the S6 retro references a prompt authored in a companion worktree that
  never landed in `main`. Real gap, tracked separately.
- **`docs/skills/wolverine-signalr/SKILL.md` lived-Relay update** — owed since S5, reaffirmed in the
  S6 retro and again here; still owed.
- **Relay OpenSpec ledger row** — tracked separately; not authored this slice.
- **`OperationsObligationsView` / Operations BC operator read models** — deferred to M7 per the
  archived `add-obligation-lifecycle` task 8.3.
- **Targeted/relay pushes not yet covered** — `Outbid`, `ReserveMet`, `ExtendedBiddingTriggered`
  targeted pushes and connection-lifecycle/group-subscription semantics remain post-M6.
