# M5-S2: Settlement BC Scaffold — Retrospective

**Date:** 2026-05-04
**Milestone:** M5 — Settlement BC
**Slice:** S2 of 6 (BC Scaffold)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M5-S2-settlement-bc-scaffold.md`
**Narrative (joint authority):** `docs/narratives/002-winner-clears-settlement.md`

---

## Baseline

- 87 tests passing (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling); `dotnet build CritterBids.slnx` 0 errors, 0 warnings; M5-S1 closed at PR #25 (SHA `056c3c7`)
- `src/CritterBids.Settlement/` does not exist; `tests/CritterBids.Settlement.Tests/` does not exist
- `CritterBids.slnx` lists 7 src nodes + 6 tests nodes (no Settlement entries)
- `src/CritterBids.Api/CritterBids.Api.csproj` references Auctions / Listings / Participants / Selling (no Settlement reference)
- `src/CritterBids.Api/Program.cs` — four `IncludeAssembly` lines (Participants, Selling, Listings, Auctions) and four `Add*Module()` calls in the postgres-guarded block
- `src/CritterBids.Contracts/Settlement/` carries the three S1 stubs (`SettlementCompleted.cs`, `PaymentFailed.cs`, `SellerPayoutIssued.cs`); referenced by no code
- ADR-019 (Settlement Workflow Hosting) accepted at S1 — Wolverine Saga; CritterBids' shipped-Wolverine stance ruled out the proposed `ProcessManager<TState>` primitive
- W003 Phase 1 carries the M5-S1 amendments (F002 Field Name Convention; F004 payload normalization; F005 BidderCreditView projection in Part 7)

---

## Items completed

| Item | Description |
|------|-------------|
| S2a | `src/CritterBids.Settlement/CritterBids.Settlement.csproj` — class library with `WolverineFx.Http.Marten` package reference; no `ProjectReference` (Contracts deferred to S3) |
| S2b | `src/CritterBids.Settlement/SettlementModule.cs` — `AddSettlementModule()` extension with `services.ConfigureMarten()` registering the `settlement` schema for `SettlementSaga` plus `Identity(x => x.Id).UseNumericRevisions(true)`; zero `AddEventType<T>()` calls; zero `Projections.*` calls |
| S2c | `src/CritterBids.Settlement/SettlementSaga.cs` — empty `: Wolverine.Saga` shell with `Guid Id` only and a forward-looking comment naming what S4 / S5 add |
| S2d | `CritterBids.slnx` — one new `/src/` node (Settlement, alphabetical after Selling) and one new `/tests/` node (Settlement.Tests, alphabetical after Selling.Tests) |
| S2e | `src/CritterBids.Api/CritterBids.Api.csproj` — new `ProjectReference` to `CritterBids.Settlement.csproj`, alphabetical after Selling |
| S2f | `src/CritterBids.Api/Program.cs` — three edits: `using CritterBids.Settlement;`, `opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);`, `builder.Services.AddSettlementModule();` |
| S2g | `tests/CritterBids.Settlement.Tests/` — csproj, GlobalUsings.cs, Fixtures/{SettlementTestFixture, SettlementTestCollection}, SettlementModuleTests.cs. Smoke test `SettlementModule_BootsClean` asserts `Host` constructed and `IDocumentStore` resolvable |
| S2h | This retrospective |

The prompt structured scope as three commits:

| Commit | Items covered |
|--------|---------------|
| 1 — `feat(settlement): scaffold CritterBids.Settlement project with SettlementModule and empty SettlementSaga shell` | S2a, S2b, S2c (plus the M5-S2 prompt itself) |
| 2 — `feat(settlement): scaffold CritterBids.Settlement.Tests with boot-green smoke test; wire Settlement into Api Program.cs and .slnx` | S2d, S2e, S2f, S2g |
| 3 — `docs: write M5-S2 retrospective` | S2h |

The M5-S2 prompt rode in commit 1 alongside the production-side scaffold — same pattern M5-S1's PR used for its prompt (S1's prompt landed with the foundation work in PR #25). The prompt is the durable record of session intent; bundling it with commit 1 keeps the prompt-to-PR traceability mechanical.

---

## S2a — CritterBids.Settlement.csproj

### Shape

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="WolverineFx.Http.Marten" />
  </ItemGroup>
</Project>
```

### Why `WolverineFx.Http.Marten` rather than `WolverineFx.Marten`

Matches the package choice of every Marten BC (Auctions, Listings, Selling). Transitively includes `WolverineFx.Http`, `WolverineFx.Marten`, and `Marten`. Settlement has no HTTP endpoints in M5 (the BC is backend-only per milestone §3); pulling the HTTP variant now keeps the package shape identical across all HTTP-capable Marten BCs and makes future endpoint additions friction-free.

### Why no ProjectReference to Contracts in S2

Mirrors the M3-S2 / M3-S3 split. M3-S2 deliberately deferred the Contracts reference until S3 introduced the first cross-BC consumer that needed `CritterBids.Contracts.Selling.ListingPublished` as input. M5-S3 will take the same step: when the `ListingPublishedHandler` lands consuming `CritterBids.Contracts.Selling.ListingPublished`, the reference is added in the same diff. S6 will use the existing `CritterBids.Contracts.Settlement.*` outbound stubs once the saga publishes them. The scaffold itself is Contracts-independent.

---

## S2b — SettlementModule.cs

### Handler / structure after

```csharp
public static class SettlementModule
{
    public static IServiceCollection AddSettlementModule(this IServiceCollection services)
    {
        services.ConfigureMarten(opts =>
        {
            opts.Schema.For<SettlementSaga>()
                .DatabaseSchemaName("settlement")
                .Identity(x => x.Id)
                .UseNumericRevisions(true);
        });

        return services;
    }
}
```

### Structural metrics

| Metric | Value |
|--------|-------|
| `opts.Events.AddEventType<T>()` calls | **0** (first-use rule) |
| `opts.Projections.*` calls | **0** (S3 adds `PendingSettlement`; S5 adds `BidderCreditView`) |
| `opts.Schema.For<T>()` calls | 1 (`SettlementSaga` → `settlement` schema) |
| Saga registrations | 1 (`Identity(x => x.Id).UseNumericRevisions(true)`) |
| BC-internal services registered | 0 (no `AddTransient`, `AddSingleton`, etc.) |
| `IWolverineExtension` registrations | 0 (no concurrency-retry policies; deferred until a slice surfaces a `ConcurrencyException` symptom worth catching) |
| Non-Marten `services.*` calls | 0 |

### Why a saga registration and not a projection registration

ADR-019 chose Wolverine Saga as the Settlement workflow host. Wolverine Saga documents register through Marten's saga-store path (`Schema.For<T>().Identity(...).UseNumericRevisions(true)`), not through the projection path (`Projections.LiveStreamAggregation<T>` / `Projections.SingleStreamProjection<T>`). The validator that bit M3-S2 (`JasperFxAggregationProjectionBase.AssembleAndAssertValidity`) only fires for projection registrations — saga documents are exempt. This is why M5-S2's scaffold does not need the `ScaffoldPlaceholder` workaround M3-S2 had to author. The host choice from ADR-019 paid a small dividend at scaffold time.

### Why `UseNumericRevisions(true)` from S2 rather than S4

The AuctionClosingSaga precedent registers numeric revisions at scaffold-equivalent time (M3-S2 actually did not have the Saga type; the saga registration appeared at M3-S5). For Settlement, the saga document's optimistic concurrency policy is part of its persistence contract; deferring it to S4 would mean S4 has to weave it into a half-implemented saga, with a higher chance of a missed `ConcurrencyException` retry path. Registering it at scaffold time pins the contract, even though no slice writes to the document until S4.

---

## S2c — SettlementSaga.cs

### Final shape

```csharp
public sealed class SettlementSaga : Wolverine.Saga
{
    public Guid Id { get; set; }
    // S4 adds the SettlementStatus enum, the per-phase Handle methods (CheckReserve /
    // ChargeWinner / CalculateFee / IssueSellerPayout / CompleteSettlement), the seven
    // Settlement-internal events, and the MarkCompleted() calls at terminal states.
    // S5 adds the FailSettlement / PaymentFailed branch and the BIN-source short-circuit
    // through the reserve-check phase per W003 Phase 1 Part 5.
}
```

(The actual file carries the longer triple-slash `/// <summary>` form; the prose above is the body of that summary block.)

### Why no validator workaround was needed

Sagas are registered via `Schema.For<SettlementSaga>().Identity(...).UseNumericRevisions(true)`. That path doesn't run through `JasperFxAggregationProjectionBase.AssembleAndAssertValidity()`. The Marten 8 validator that surfaced as `InvalidProjectionException : No matching conventional Apply/Create/ShouldDelete methods for the CritterBids.Auctions.Listing aggregate` during M3-S2 (per that retrospective's S2c subsection) cannot fire for a saga document. The empty `SettlementSaga` shell registered cleanly on the first build with zero `Apply` methods and zero placeholder records.

This is the cleanest dividend ADR-019's Saga-over-Handlers decision delivered: M3-S2's lived blocker was structurally impossible at M5-S2.

### Structural metrics

| Metric | After |
|--------|-------|
| Public properties on `SettlementSaga` | 1 (`Id`) |
| State fields beyond `Id` | 0 |
| `Handle(T)` methods | 0 |
| `MarkCompleted()` calls | 0 |
| `OutgoingMessages` returns | 0 |
| Event types declared | 0 |
| Placeholder Apply / record needed | **0** (saga document path bypasses the projection validator) |

---

## S2d — slnx edits

### Before / After

| Folder | Before | After |
|--------|--------|-------|
| `/src/` | AppHost, Api, Auctions, Contracts, Listings, Participants, Selling (7) | AppHost, Api, Auctions, Contracts, Listings, Participants, Selling, **Settlement** (8) |
| `/tests/` | Api.Tests, Auctions.Tests, Contracts.Tests, Listings.Tests, Participants.Tests, Selling.Tests (6) | Api.Tests, Auctions.Tests, Contracts.Tests, Listings.Tests, Participants.Tests, Selling.Tests, **Settlement.Tests** (7) |

Insertion points honor the alphabetical-by-BC-name rule from `adding-bc-module.md` §Project Structure. No reshuffling of existing nodes.

---

## S2e — CritterBids.Api.csproj ProjectReference

```xml
<ItemGroup>
  <ProjectReference Include="..\CritterBids.Auctions\CritterBids.Auctions.csproj" />
  <ProjectReference Include="..\CritterBids.Listings\CritterBids.Listings.csproj" />
  <ProjectReference Include="..\CritterBids.Participants\CritterBids.Participants.csproj" />
  <ProjectReference Include="..\CritterBids.Selling\CritterBids.Selling.csproj" />
  <ProjectReference Include="..\CritterBids.Settlement\CritterBids.Settlement.csproj" />
</ItemGroup>
```

Required for `typeof(SettlementSaga).Assembly` in `Program.cs` to resolve per the M2-S7 discovery documented in `adding-bc-module.md` §Checklist. Alphabetical position: last in the existing list.

---

## S2f — Program.cs three-line diff

```diff
  using CritterBids.Selling;
+ using CritterBids.Settlement;
  using JasperFx;
  ...
      opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly);
      opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);
+     opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);
  ...
      builder.Services.AddListingsModule();
      builder.Services.AddAuctionsModule();
+     builder.Services.AddSettlementModule();
```

### Why `typeof(SettlementSaga)` is unambiguous

Pre-session grep for `class SettlementSaga\b|record SettlementSaga\b` across `src/` returned zero matches (the project did not exist). No other type named `SettlementSaga` exists anywhere in the solution. The unqualified `SettlementSaga` in `typeof(SettlementSaga).Assembly` resolves to `CritterBids.Settlement.SettlementSaga` through the new `using CritterBids.Settlement;` without ambiguity.

### Negative assertion — RabbitMQ routing

| Metric | Value |
|--------|-------|
| `opts.PublishMessage<Settlement*>` lines added | **0** |
| `opts.ListenToRabbitQueue("settlement-*")` lines added | **0** |
| `opts.ListenToRabbitQueue("listings-settlement-events")` lines added | **0** |

Per the out-of-scope list: `settlement-selling-events` wires in S3, `settlement-auctions-events` in S4, `listings-settlement-events` in S6. The "RabbitMQ routing setup" mention in M5 §7 S2 row resolved as scaffold-readiness — the topology documented and the wiring deferred to the slice that produces or consumes each event. M3-S2 set this precedent verbatim.

---

## S2g — Smoke test shape

### Structure mirrored after `AuctionsTestFixture`

The Settlement fixture is a near-verbatim clone of `AuctionsTestFixture` with four concrete differences:

| Fixture element | Source pattern | Settlement-specific change |
|-----------------|----------------|----------------------------|
| `PostgreSqlBuilder("postgres:17-alpine")` | Auctions | container name prefix `settlement-postgres-test-` |
| `services.AddXxxModule()` call inside `ConfigureServices` | Auctions | `services.AddSettlementModule()` |
| Cross-BC discovery exclusions | Auctions excludes Selling + Listings | Settlement excludes Selling + **Auctions** + Listings (three classes vs Auctions' two) |
| `ConfigureServices` registration shape | Auctions verbatim | unchanged |
| `RunWolverineInSoloMode()` + `DisableAllExternalWolverineTransports()` | Auctions verbatim | unchanged |
| Cleanup helpers (`CleanAllMartenDataAsync`, `ResetAllMartenDataAsync`, `GetDocumentSession`) | Auctions verbatim | unchanged |
| Saga / event-stream seed helpers | Auctions: 3 helpers (`SeedAuctionClosingSagaAsync`, `SeedListingStreamAsync`, `AppendListingWithdrawnAsync`) | None — S2 has no scenarios that need seeded state |

### Test

```csharp
[Fact]
public void SettlementModule_BootsClean()
{
    _fixture.Host.ShouldNotBeNull();
    var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
    store.ShouldNotBeNull();
}
```

Exact shape of `AuctionsModuleTests.AuctionsModule_BootsClean`. One fact, two assertions — sufficient as a green guard.

### Why three exclusions and not two

`AuctionsTestFixture` excludes Selling and Listings: Selling because `ISellerRegistrationService` isn't registered without `AddSellingModule()`; Listings because `AuctionStatusHandler` (M3-S6) handles the same five auction events the Auctions saga starts on, and `MultipleHandlerBehavior.Separated` would shadow them.

For Settlement, the foreign-BC set widens by one. The Settlement fixture does not register `AddAuctionsModule()`, so Auctions handlers — which operate on the `Listing` aggregate (live-stream-aggregation registration at the `auctions` schema) and the `AuctionClosingSaga` document — would fail Marten code-gen at host startup if discovered. The third exclusion (`AuctionsBcDiscoveryExclusion`) is the structural fix.

Participants stays included for the same reason it stays included in `AuctionsTestFixture`: its handlers don't depend on services or schema mappings the foreign-BC fixture omits. Verified empirically — the smoke test boots green with Participants handlers in discovery and Selling / Auctions / Listings excluded.

### Discovery — the JasperFxEnvironment namespace correction

First test run failed at compile-time with:

> `error CS0103: The name 'JasperFxEnvironment' does not exist in the current context`

Root cause: the AuctionsTestFixture uses `using JasperFx.CommandLine;` to resolve `JasperFxEnvironment.AutoStartHost = true;`. The Settlement fixture's first cut imported `JasperFx;` (the parent namespace). The two are not related — `JasperFxEnvironment` lives specifically in `JasperFx.CommandLine`. Fix: change the using directive to `JasperFx.CommandLine;` and rebuild. Subsequent build green.

This is a one-line copy-paste discovery, not a structural pattern. Recording it here so any future BC-fixture authoring session that imports the wrong using statement has a search hit.

### Why no Wolverine tracking helpers in the fixture

`AuctionsTestFixture` carries `LoadSaga<T>` plus three saga-state seed helpers because Auctions tests exercise saga-state scenarios. Settlement's S2 smoke test asserts boot-green only — no saga state, no event-stream seeding, no message tracking. The fixture is intentionally lean. S3 will add what the first projection consumer needs (likely a `LoadAsync<PendingSettlement>` helper); S4 will add saga-seed helpers analogous to `SeedAuctionClosingSagaAsync` when the seven-phase scenarios need pre-terminal-state seeding. Add what the next test needs, not ceremonial infrastructure.

---

## Test results

| Phase | Settlement.Tests | All Tests | Result |
|-------|------------------|-----------|--------|
| Baseline | 0 (project absent) | 87 | Green |
| After Commit 1 | 0 (project absent) | 87 | Green (Settlement dll compiles standalone; nothing else touched) |
| After Commit 2 — first dotnet build | 0 (compile error) | n/a | **Red** — `JasperFxEnvironment` namespace mismatch |
| After namespace fix | 1 passing | 88 | Green |
| Session close (after Commit 3 retro) | 1 passing | 88 | Green |

Test count delta across the session: **+1** (the one scaffold smoke test).

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 88 passing (1 Api + 36 Auctions + 1 Contracts + 11 Listings + 6 Participants + 32 Selling + **1 Settlement**)
- `.cs` files created in `src/CritterBids.Settlement/`: 2 (`SettlementModule.cs`, `SettlementSaga.cs`)
- `.csproj` files created: 2 (`CritterBids.Settlement.csproj`, `CritterBids.Settlement.Tests.csproj`)
- Production handlers authored: **0**
- Commands authored: **0**
- HTTP endpoints authored: **0**
- Saga `Handle` methods authored: **0**
- Saga state fields beyond `Id`: **0**
- `MarkCompleted()` calls: **0**
- `opts.Events.AddEventType<T>()` calls in SettlementModule: **0**
- `opts.Projections.*` calls in SettlementModule: **0**
- `IWolverineExtension` registrations in SettlementModule: **0** (the three in the test fixture are exclusions, not BC-side policies)
- RabbitMQ routing lines added to Program.cs: **0**
- `ProjectReference` from Settlement to Contracts: **absent** (S3 adds it)
- `ProjectReference` from Api to Settlement: **present** (S2e)
- `Contracts/Settlement/` files: 3 (the M5-S1 stubs); referenced by no Settlement-side code yet
- Settlement-internal event types in `src/CritterBids.Settlement/`: 0 (S4 territory)

---

## Key learnings

1. **The Saga-vs-Handlers decision in ADR-019 retired a known scaffolding hazard.** M3-S2's `ScaffoldPlaceholder` workaround existed solely because `Projections.LiveStreamAggregation<T>` triggers `JasperFxAggregationProjectionBase.AssembleAndAssertValidity` — and an aggregate with zero `Apply` methods fails that validator. Wolverine Saga documents register via `Schema.For<T>().Identity(...).UseNumericRevisions(true)`, which is a different code path that bypasses the projection validator entirely. M5-S2's scaffold registered cleanly on first build with zero placeholder code. The lesson generalizes: when a host-choice ADR is between two patterns whose registration paths differ, the scaffold-time validator behavior is a non-trivial input. Future ADRs comparing host primitives should call out the registration-path difference if both paths exist.

2. **Cross-BC handler isolation needs N-1 exclusions, where N is the number of BCs in the solution.** M3-S2 needed 1 (Selling) at the time. By M3-S6, Auctions's fixture needed 2 (Selling + Listings). M5-S2 needs 3 (Selling + Auctions + Listings). The pattern is structural: any BC fixture that registers only its own module needs to exclude every other BC whose handlers the IncludeAssembly path would discover. As BCs are added (Obligations, Relay, Operations remain to ship), every existing fixture needs the new BC's exclusion appended. This is a maintenance load worth tracking in the milestone retros for M6 onward — by M8, eight BCs in scope means a fixture for any BC carries seven exclusion classes. The ergonomics may justify a shared `ForeignBcDiscoveryExclusion` extension method or a `params Type[]` signature in a future skill-file amendment to `critter-stack-testing-patterns.md`. Recorded here as a deferred-item observation, not a S2 problem.

3. **The cutover gate's joint-authority discipline holds without ceremony — second consecutive slice.** M5-S1's retro Key Learning #1 named this. M5-S2 confirms it: the `Narrative:` line in the prompt's metadata block is the only structural difference from a pre-cutover prompt. The narrative did not dramatise the scaffold itself (the scaffold is below Moment grain), but its Cast and Setting framing — the seven-phase workflow, the deterministic SettlementId, the MVP credit-ledger posture — was the design ground that informed the saga shell's shape and the registration choice. No new methodology overhead required; the citation is enough to anchor the slice's work to a narrative-as-design-witness role.

4. **Namespace traps from copy-pasting fixture code are predictable, not structural.** The `JasperFxEnvironment` import lived in `JasperFx.CommandLine`, not `JasperFx`. The first cut of the Settlement fixture imported `JasperFx;` (the parent namespace) because that's the most-used JasperFx import elsewhere in the codebase. Compile failed; namespace corrected; build green. The lesson is small but durable: when copy-pasting an unfamiliar test-fixture pattern, the using directives are part of the pattern — don't trim them. A future skill-file amendment to `critter-stack-testing-patterns.md` could surface the canonical fixture's full using list explicitly, but the cost-benefit of that amendment is marginal at the current scale.

5. **Three-line `Program.cs` diffs are a stable BC-scaffold signature.** M3-S2's diff was three lines (using + IncludeAssembly + AddXyzModule); M5-S2's diff is three lines. Each new BC contributes the same shape to `Program.cs`. The ergonomics suggest the Program.cs's BC-wiring block could eventually be replaced by a single `services.AddAllCritterBidsModules()` call that loops over a list, but at the current scale (8 BCs target) the explicit list reads better as documentation. If the project ships nine BCs, the one-line aggregator looks worth it; at five accumulated BCs (Participants / Selling / Listings / Auctions / Settlement) it does not.

6. **The retrospective's "Items completed" table doubles as a cross-reference to the milestone doc's slice scope.** S2a-S2h mirror the prompt's deliverable list (csproj, module, saga shell, slnx, Api csproj, Program.cs, fixture+test, retro). Anyone reading the milestone doc's §7 S2 row alongside this retro can cross-check completion line by line. The discipline from M3-S2's retro carried over verbatim and remains the right shape.

---

## Skill gaps surfaced

- **`adding-bc-module.md` — nothing to add from this session.** The skill's Marten BC module registration, `Program.cs` wiring, and checklist all applied verbatim. The Saga registration shape (`Schema.For<T>().Identity(...).UseNumericRevisions(true)`) is documented in `wolverine-sagas.md` already, and `adding-bc-module.md` correctly defers there for saga-specific shapes.
- **`critter-stack-testing-patterns.md` — one observation worth queuing for a future amendment.** Key Learning #2 (the N-1 exclusions scaling) is a real distinction worth surfacing in the §Cross-BC Handler Isolation section once enough BCs ship to make the cost legible. At M5-S2 the friction is mild (three explicit exclusion classes per fixture); by M8 it will be an obvious documentation gap. Not fixed in this session per the prompt's "do not edit skills in-session" rule; flagged here for the next skills-maintenance pass.
- **Session-prompt template — the M3-S2 / M5-S2 shape is durable.** Three commits, four-section in-scope list (production project + test project + Api wiring + retro), explicit out-of-scope clauses for every deferred slice, and an Open Questions section that pre-commits the response if a known assumption fails. Worth keeping as the canonical BC-scaffold prompt shape; no template edit needed.
- **`wolverine-sagas.md` Settlement-side amendment** remains queued for M5-S4 per ADR-019 §Consequences. M5-S2 did not touch it; the saga shell is too minimal to inform the skill file.

---

## Findings against narrative

The slice did not dramatise any narrative 002 Moment (the scaffold is below Moment grain), so no `narrative-update` / `workshop-update` / `code-update` / `document-as-intentional` findings surfaced against narrative 002. The narrative's role at this slice was as design witness for the saga's shape and the deterministic SettlementId convention — both pinned at S1 and unchanged at S2.

The cutover-gate's joint-authority discipline operated without surfacing any drift to route. Future M5 slices that touch Moment-grain behavior (S3 onwards: PendingSettlement projection lifecycle is Moment 1; the seven saga phases are Moments 1-5) will exercise the four-lane findings discipline as designed.

The cumulative narrative 002 findings ledger is unchanged from the M5-S1 close: F001 ✓ (PR #20), F002 ✓ (PR #25), F003 ✓ minimum-scope (PR #20), F004 ✓ (PR #25), F005 ✓ (PR #25). All five findings remain closed.

---

## Verification checklist

- [x] `src/CritterBids.Settlement/CritterBids.Settlement.csproj` exists; `WolverineFx.Http.Marten` package reference matches sibling BC projects; **no `<ProjectReference>` to `CritterBids.Contracts`** (deferred to S3).
- [x] `tests/CritterBids.Settlement.Tests/CritterBids.Settlement.Tests.csproj` exists; references `CritterBids.Settlement` and `CritterBids.Api`; package references (Alba, Microsoft.NET.Test.Sdk, Testcontainers.PostgreSql, xunit, xunit.runner.visualstudio, Shouldly) match sibling test projects.
- [x] `CritterBids.slnx` contains `<Project>` entries for both new projects under the `/src/` and `/tests/` folder nodes respectively; both inserted alphabetically after their Selling siblings.
- [x] `src/CritterBids.Settlement/SettlementModule.cs` defines `AddSettlementModule(this IServiceCollection services) : IServiceCollection` calling `services.ConfigureMarten(...)` with the `settlement` schema mapping for `SettlementSaga` and `Identity(x => x.Id).UseNumericRevisions(true)` saga registration; nothing else.
- [x] `src/CritterBids.Settlement/SettlementSaga.cs` defines `public sealed class SettlementSaga : Wolverine.Saga` with a single `public Guid Id { get; set; }` property and a single forward-looking comment naming what S4 / S5 add. No state fields beyond `Id`, no `Handle` methods, no `MarkCompleted()` calls.
- [x] `SettlementModule.cs` contains zero `opts.Events.AddEventType<T>()` calls and zero `opts.Projections.*` calls.
- [x] `src/CritterBids.Api/CritterBids.Api.csproj` has a `<ProjectReference>` to `CritterBids.Settlement.csproj`.
- [x] `src/CritterBids.Api/Program.cs` — `using CritterBids.Settlement;` present; `opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);` present after the existing `IncludeAssembly` line for `Listing`; `builder.Services.AddSettlementModule();` present after `AddAuctionsModule();`.
- [x] `Program.cs` `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks contain zero Settlement-related lines.
- [x] `tests/CritterBids.Settlement.Tests/Fixtures/SettlementTestFixture.cs` and `SettlementTestCollection.cs` exist; the fixture mirrors `AuctionsTestFixture` with three concrete differences (container name prefix, `AddSettlementModule()` call, three discovery exclusion classes for Selling / Auctions / Listings).
- [x] Three `IWolverineExtension` exclusion classes present in the Settlement fixture: `SellingBcDiscoveryExclusion`, `AuctionsBcDiscoveryExclusion`, `ListingsBcDiscoveryExclusion`. Each excludes types under its respective `CritterBids.{TargetBc}` namespace via `Discovery.CustomizeHandlerDiscovery`.
- [x] At least one test in `CritterBids.Settlement.Tests` (`SettlementModuleTests.SettlementModule_BootsClean`) asserts the Settlement module registers cleanly alongside the primary Marten store and `IDocumentStore` is resolvable from DI.
- [x] `dotnet build` — 0 errors, 0 warnings.
- [x] `dotnet test` — all green; baseline 87 tests still pass; new smoke test passes; total 88.
- [x] This retrospective exists; mirrors the M3-S2 retrospective shape; records the solution-layout delta, the `Program.cs` edit diff, the smoke-test shape chosen, the namespace-trap discovery (recorded under S2g), and a "what M5-S3 should know" note (below).

---

## What M5-S3 should know

**M5-S3 is the first projection session for Settlement** — the `PendingSettlement` document projection seeded from `CritterBids.Contracts.Selling.ListingPublished`. It also lands the first cross-BC consumer for the Settlement BC. Concrete items S3 should walk in with:

1. **`CritterBids.Settlement` gains its first `<ProjectReference Include="...CritterBids.Contracts.csproj" />` in S3.** The `ListingPublishedHandler` (or whatever the handler shape resolves to) needs `CritterBids.Contracts.Selling.ListingPublished` as input. This is a scaffold edit, not a discovery — the M3-S2/S3 precedent is verbatim.
2. **`settlement-selling-events` is the new RabbitMQ queue route.** Added to `Program.cs` inside the existing RabbitMQ-guarded block. The shape to mirror is the `auctions-selling-events` route already wired for Auctions: `opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>().ToRabbitQueue("settlement-selling-events"); opts.ListenToRabbitQueue("settlement-selling-events");`. This adds a third publish route for the same `ListingPublished` contract — Selling's existing routes (`listings-selling-events`, `auctions-selling-events`) stay unchanged.
3. **`opts.Events.AddEventType<ListingPublished>()`** is **not** added in `SettlementModule.cs` — `ListingPublished` is a Selling-side event type already registered elsewhere; cross-BC consumption does not require Settlement to re-register the type. The first `AddEventType<T>()` calls in `SettlementModule.cs` will land at S4 with the Settlement-internal events (`SettlementInitiated`, etc.) when the saga first emits them.
4. **`PendingSettlement` is the first projection registration for Settlement.** Per W003 Phase 1 Part 1's framing, lifecycle states are `Pending` → `Consumed` / `Expired`. Marten document projection (not `LiveStreamAggregation` — the projection materializes from a cross-BC integration event, not from a same-BC stream). `marten-projections.md` carries the M5-S1 "Pending: M5-S3 amendment" flag — the cross-BC-event-seeded projection pattern's full skill-file documentation lands at S3's retro.
5. **The Settlement test fixture is ready for S3.** The fixture mirrors `AuctionsTestFixture` and excludes Selling / Auctions / Listings handlers. If S3's test needs Wolverine tracking helpers (`InvokeMessageAndWaitAsync`, `ExecuteAndWaitAsync`), add them following the `SellingTestFixture` shape. The current fixture's lean shape (cleanup helpers + `GetDocumentSession`) is sufficient for projection-load assertions; tracking helpers come when message-flow assertions need them.
6. **The Settlement saga shell stays untouched in S3.** `PendingSettlement` is a separate Marten document projection from the saga; the saga shell's empty body is correct until S4 lands the seven-phase implementation. S3 adds a new file (`PendingSettlement.cs`), not edits to `SettlementSaga.cs`.

---

## What remains / deferred into later M5 sessions

**In scope for M5, deferred to later slices:**

- `PendingSettlement` projection + `ListingPublishedHandler` consumer + lifecycle states (S3)
- Settlement-internal event types (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`) authored in `src/CritterBids.Settlement/` (S4)
- Settlement saga's seven-phase happy path; `ListingSold` consumer; `SettlementId` strongly-typed identifier; UUID v5 namespace constant (S4)
- Failure-path scenarios (`PaymentFailed`); BIN source path (`BuyItNowPurchased` consumer); `BidderCreditView` projection (S5)
- `SellerPayoutIssued` integration-event publishing; `SettlementCompleted` publishing; `listings-settlement-events` queue route; Listings-side `CatalogListingView.Status = "Settled"` extension (S6)
- `wolverine-sagas.md` skill-file amendment with the Settlement-side example (S4 retro)
- `marten-projections.md` skill-file full pattern documentation for the cross-BC-event-seeded projection pattern (S3 retro)
- M5 milestone retrospective (after S6 ships)

**Out of scope for M5, tracked elsewhere:**

- Real payment-processor integration — post-MVP per W003 §"Winner Charge"
- Compensation paths beyond MVP — post-MVP per W003 Phase 1 Part 3
- W003 broader storage-staleness sweep (narrative 002 F003's references at L29 / L649 / L663) — future workshop-cleanup session, not M5
- `ProcessManager<TState>` framework primitive — out of scope per CritterBids' shipped-Wolverine stance (ADR-019)
- M6 frontend MVP design — `[AllowAnonymous]` posture unchanged through M5

**Deferred from earlier milestones, still relevant:**

- RabbitMQ routing in BC modules vs `Program.cs` (threading `WolverineOptions` into module methods) — deferred again per M5 milestone doc; M3 § 8 carried this forward and the M5 milestone doc inherits it.
