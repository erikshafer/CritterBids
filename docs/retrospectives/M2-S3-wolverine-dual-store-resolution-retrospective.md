# M2-S3: Wolverine Dual-Store Resolution — Retrospective

**Date:** 2026-04-15
**Milestone:** M2 — Listings Pipeline
**Slice:** S3 — Production Aspire unblock; ADR 010; S3 close
**Agent:** Claude (interactive session, explanatory output style)
**Prompt:** `docs/prompts/implementations/M2-S3-wolverine-dual-store-resolution.md`

> **Session note:** The deliverables originally scoped to S3 (RegisteredSellers handler, projection,
> `ISellerRegistrationService`, `Program.cs` routing, 4 projection tests) were completed in the
> unscheduled post-S2 architectural correction session. That session is fully documented in
> `docs/retrospectives/M2-postS2-adr0002-correction.md`, which is the authoritative record for
> that work. This session addresses the remaining S3 deliverables: the production Aspire crash,
> ADR 010, and the formal S3 close.

---

## Baseline

- `dotnet build`: 0 errors, 0 warnings.
- `dotnet test`: 13 passing (Selling 5, Participants 6, Api.Tests 1, Contracts.Tests 1).
- `dotnet run --project src/CritterBids.AppHost`: throws `InvalidWolverineStorageConfigurationException`
  when both postgres and sqlserver connection strings are present.
- `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md`: already deleted in commit `1550907`
  (replaced by the current session's prompt as part of the post-S2 correction PR).
- ADR index: next available number confirmed as 010.

---

## Items completed

| Item | Description |
|------|-------------|
| S1 | Researched all three fix options via NuGet XML docs, Wolverine source, and web |
| S2 | Authored ADR 010 (`docs/decisions/010-wolverine-dual-store-resolution.md`) |
| S3 | Added prominent ADR-010 comments to both `Program.cs` guard blocks |
| S4 | Updated `docs/decisions/README.md` — ADR 010 row added |
| S5 | `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md` — already deleted; acceptance criterion satisfied |

---

## S1: Research — API surface investigation

### What was checked

**NuGet XML documentation (local cache)**

- `WolverineFx.Polecat 5.30.0` → `Wolverine.Polecat.xml`
  `PolecatIntegration` properties: `UseFastEventForwarding`, `UseWolverineManagedEventSubscriptionDistribution`,
  `MainDatabaseConnectionString`, `TransportSchemaName`, `MessageStorageSchemaName`.
  No `IsMain`, `IsAncillary`, `MessageStoreRole`, or equivalent. One `IntegrateWithWolverine()` overload exists on
  `PolecatConfigurationExpression`; it always registers Polecat as main. **Option A confirmed unavailable.**

- `WolverineFx.Marten 5.30.0` → `Wolverine.Marten.xml`
  Two `IntegrateWithWolverine()` overloads exist:
  1. `WolverineOptionsMartenExtensions.IntegrateWithWolverine(MartenConfigurationExpression, Action<MartenIntegration>)` — primary store (main)
  2. `AncillaryWolverineOptionsMartenExtensions.IntegrateWithWolverine<T>(MartenStoreExpression<T>, Action<AncillaryMartenIntegration>)` — ancillary store
  Polecat has no equivalent ancillary overload. **Gap confirmed.**

- `WolverineFx.SqlServer 5.30.0` → `Wolverine.SqlServer.xml`
  `SqlServerConfigurationExtensions.PersistMessagesWithSqlServer(WolverineOptions, string, string, MessageStoreRole)` confirmed.
  `MessageStoreRole.Ancillary` enum value confirmed in `Wolverine.xml`.

- `WolverineFx.Postgresql 5.30.0` → `Wolverine.Postgresql.xml`
  `PostgresqlConfigurationExtensions.PersistMessagesWithPostgresql(WolverineOptions, string, string, MessageStoreRole)` confirmed.

**Web research**

- Jeremy Miller blog "Wolverine 5 and Modular Monoliths" (2025-10-27): confirms Option B pattern for **EF Core**:
  `opts.PersistMessagesWithSqlServer(sqlserver1, role: MessageStoreRole.Ancillary).Enroll<SampleDbContext>()`.
  This works because EF Core uses `AddDbContextWithWolverineIntegration<T>()` — which does NOT call
  `IntegrateWithWolverine()` separately and does NOT claim main status.
- Wolverine docs (wolverinefx.net): Polecat integration page has no mention of ancillary stores or
  mixed Marten + Polecat configuration.

### Conclusions per option

**Option A — Mark Polecat as ancillary via `IntegrateWithWolverine()` callback**
`PolecatIntegration` has no `MessageStoreRole` or ancillary property. **Not available.**

**Option B — Standalone `PersistMessagesWithSqlServer(Ancillary)` + `Enroll<T>()`**
API confirmed for EF Core. For Polecat: `AddParticipantsModule()` calls `AddPolecat().IntegrateWithWolverine()`,
which unconditionally claims main status regardless of any standalone `PersistMessagesWithSqlServer(Ancillary)` call
in `UseWolverine()`. Two separate SQL Server registrations would result — not a resolution. Applying Option B
correctly would require either removing `.IntegrateWithWolverine()` from `AddParticipantsModule()` (out of scope —
no BC file changes) or a Polecat-specific `AddPolecat...WithWolverineIntegration<T>()` API (does not exist).
**Not applicable within current constraints.**

**Option C — Deferred**
Both options exhausted. **Selected.**

---

## S2: ADR 010

`docs/decisions/010-wolverine-dual-store-resolution.md` — new file.

Status: **Proposed — Pending JasperFx input**

Sections: Context (exact error verbatim, two guard blocks quoted), Options Considered (A and B with
full API surface examined for each and explicit rejection rationale), Decision (Option C), Open
Question for JasperFx (desired configuration quoted), Consequences (current impact, what changes
when resolved), References (8 links + internal cross-references).

`docs/decisions/README.md` updated — ADR 010 row added to status ledger.

---

## S3: Program.cs comments

Two prominent comment blocks added — one at each guard block — referencing ADR 010 and quoting
the exception message verbatim. Both blocks now state clearly that the conflict is unresolved and
pending JasperFx input.

No functional code changes. All null guards and module registrations are unchanged.

---

## Test results

| Phase | All Tests | Result |
|-------|-----------|--------|
| Session open (baseline) | 13 | Pass |
| After Program.cs comment additions | 13 | Pass |

No code changes were made to handlers, modules, test fixtures, or configuration logic.

---

## Build state at session close

- Errors: 0
- Warnings: 0
- Tests: 13/13 passing (Selling 5, Participants 6, Api.Tests 1, Contracts.Tests 1)
- `InvalidWolverineStorageConfigurationException` at Aspire startup: **still present** — no fix implemented (Option C)
- `IntegrateWithWolverine()` calls claiming "main": 2 (unchanged — `AddMarten()` and `AddPolecat()`)
- ADR 010 status: Proposed — Pending JasperFx input
- `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md`: absent (deleted in commit `1550907` before session start)

---

## Key learnings

1. **Marten and Polecat have asymmetric ancillary-store APIs.** Wolverine.Marten 5.30.0 provides two
   `IntegrateWithWolverine()` overloads: one for the primary store, one for ancillary named stores.
   Wolverine.Polecat 5.30.0 provides only one overload, which always registers as main. A CritterBids-style
   mixed Marten + Polecat modular monolith cannot run under Aspire until this gap is filled.

2. **`PersistMessagesWithSqlServer(Ancillary).Enroll<T>()` is the correct pattern for EF Core, not Polecat.**
   The EF Core ancillary pattern works because EF Core's Wolverine registration path does not call
   `IntegrateWithWolverine()`. Polecat's registration path does. These are different integration models.
   Do not conflate them when researching the fix.

3. **The `MessageStoreRole` enum (`Main` / `Ancillary`) controls which standalone persistence store is primary.**
   This API lives on `WolverineOptions` (inside `UseWolverine()`), not on `PolecatConfigurationExpression` or
   `MartenConfigurationExpression`. It is for standalone `PersistMessagesWithSqlServer`/`PersistMessagesWithPostgresql`
   registrations, not for the store integrations called via `IntegrateWithWolverine()`.

4. **XML documentation files in the NuGet cache are a reliable API surface reference.** For each package
   at `~/.nuget/packages/{package}/{version}/lib/{tfm}/{Package}.xml`, the `<members>` section documents
   all public types, methods, and properties. This is sufficient to confirm whether an API exists without
   decompiling or running code.

5. **`docs/prompts/implementations/M2-S3-registered-sellers-consumer.md` was already deleted before this session started.**
   Commit `1550907` replaced it with the current session's prompt. The deletion acceptance criterion was
   satisfied before the session began; it required no action in this session.

---

## Verification checklist

- [x] `dotnet build`: 0 errors, 0 warnings
- [x] `dotnet test`: 13/13 passing — no regressions
- [x] ADR 010 in "Proposed / Pending JasperFx input" state — confirmed
- [x] `docs/decisions/010-wolverine-dual-store-resolution.md` exists
- [x] `docs/decisions/README.md` includes the ADR 010 row in the status ledger
- [x] `docs/retrospectives/M2-S3-wolverine-dual-store-resolution-retrospective.md` exists
      and references `M2-postS2-adr0002-correction.md`
- [x] `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md` deleted — already absent at session start
- [x] Files modified: `src/CritterBids.Api/Program.cs`, `docs/decisions/README.md`,
      `docs/decisions/010-wolverine-dual-store-resolution.md`,
      `docs/retrospectives/M2-S3-wolverine-dual-store-resolution-retrospective.md`
- [x] No `ISellingDocumentStore`, `AddMartenStore<T>()`, or `[MartenStore]` references introduced

---

## What remains / next session should verify

- **Production Aspire startup is still blocked.** `dotnet run --project src/CritterBids.AppHost`
  still throws `InvalidWolverineStorageConfigurationException` when both postgres and sqlserver
  connection strings are present. This is the open item from ADR 010. Resolution depends on
  JasperFx adding an ancillary-store path for Polecat's `IntegrateWithWolverine()`, or providing
  an alternative configuration approach.

- **File a JasperFx GitHub discussion or issue** before or during S4. The open question for JasperFx
  is documented verbatim in ADR 010 §Open Question. A JasperFx response may arrive before Settlement
  or Operations BCs are scaffolded (those BCs also use Polecat and will face the same conflict).

- **S4 deliverables** (`DraftListingCreated`, `SellerListing.Apply()`, `ListingValidator`,
  `POST /api/listings/draft`) are unblocked — they do not require the Aspire startup to succeed.
