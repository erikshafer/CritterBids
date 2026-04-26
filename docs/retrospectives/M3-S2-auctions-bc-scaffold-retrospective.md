# M3-S2: Auctions BC Scaffold — Retrospective

**Date:** 2026-04-17
**Milestone:** M3 — Auctions BC
**Session:** S2 of 7
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M3-S2-auctions-bc-scaffold.md`
**Baseline:** 44 tests passing · `dotnet build` 0 errors, 0 warnings · M3-S1 complete (SHA `2278417`)

---

## Baseline

- 44 tests passing (1 Api + 1 Contracts + 4 Listings + 6 Participants + 32 Selling)
- `dotnet build` — 0 errors, 0 warnings
- `src/CritterBids.Auctions/` does not exist; `tests/CritterBids.Auctions.Tests/` does not exist
- `CritterBids.slnx` lists 6 src nodes + 5 tests nodes (no Auctions entries)
- `src/CritterBids.Api/CritterBids.Api.csproj` references Listings, Participants, Selling (no Auctions reference)
- `src/CritterBids.Api/Program.cs` — three `IncludeAssembly` lines (Participants, Selling, Listings) and three `Add*Module()` calls in the postgres-guarded block (Participants, Selling, Listings)
- `src/CritterBids.Contracts/Auctions/` contains the nine S1 stubs; referenced by no code

---

## Items completed

| Item | Description |
|------|-------------|
| S2a | `src/CritterBids.Auctions/CritterBids.Auctions.csproj` — class library with `WolverineFx.Http.Marten` package reference; no `ProjectReference` (Contracts reference deferred to S3) |
| S2b | `src/CritterBids.Auctions/AuctionsModule.cs` — `AddAuctionsModule()` extension with `services.ConfigureMarten()` registering `auctions` schema for `Listing` and `LiveStreamAggregation<Listing>()`; zero `AddEventType<T>()` calls |
| S2c | `src/CritterBids.Auctions/Listing.cs` — empty aggregate shell with `Id` property plus an S2-scaffold placeholder record and no-op `Apply(ScaffoldPlaceholder)` method added in response to the Marten-8 validator blocker (see S2-blocker subsection) |
| S2d | `CritterBids.slnx` — one new `/src/` node (`CritterBids.Auctions.csproj`, inserted alphabetically between `Api` and `Contracts`) and one new `/tests/` node (`CritterBids.Auctions.Tests.csproj`, inserted alphabetically between `Api.Tests` and `Contracts.Tests`) |
| S2e | `src/CritterBids.Api/CritterBids.Api.csproj` — new `ProjectReference` to `CritterBids.Auctions.csproj` (first in alphabetical order of project references) |
| S2f | `src/CritterBids.Api/Program.cs` — three edits: `using CritterBids.Auctions;`, `opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);` after the three existing `IncludeAssembly` lines, `builder.Services.AddAuctionsModule();` after `AddListingsModule();` |
| S2g | `tests/CritterBids.Auctions.Tests/` — csproj, GlobalUsings.cs, Fixtures/{AuctionsTestFixture, AuctionsTestCollection}, AuctionsModuleTests.cs. Smoke test `AuctionsModule_BootsClean` asserts `Host` constructed and `IDocumentStore` resolvable |
| S2h | This retrospective |

The prompt structured scope as three commits:

| Commit | Items covered |
|--------|---------------|
| 1 — `feat(auctions): scaffold CritterBids.Auctions project with AuctionsModule and empty Listing aggregate` | S2a, S2b, S2c (pre-blocker) |
| 2 — `feat(auctions): scaffold CritterBids.Auctions.Tests with boot-green smoke test; wire Auctions into Api Program.cs and .slnx` | S2c (post-blocker placeholder), S2d, S2e, S2f, S2g |
| 3 — `docs: write M3-S2 retrospective` | S2h |

The S2c placeholder lived in Commit 2 (not Commit 1) because the blocker surfaced when the smoke test first booted the host — the decision to add the placeholder was integration-time work, not scaffold-time work, and belonged in the same commit as the wiring that exposed it.

---

## S2a — CritterBids.Auctions.csproj

### Shape

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="WolverineFx.Http.Marten" />
  </ItemGroup>
</Project>
```

### Why `WolverineFx.Http.Marten` rather than `WolverineFx.Marten`

Matches the package choice of sibling BCs (Selling, Listings). Transitively includes `WolverineFx.Http`, `WolverineFx.Marten`, and `Marten`. Auctions has no HTTP endpoints in S2; they land in M3-S4 with the `PlaceBid`/`BuyNow` slice. Pulling the HTTP variant now keeps the package reference identical across all HTTP-capable Marten BCs.

### Why no ProjectReference to Contracts

Explicit prompt instruction — S3 adds the reference when the `ListingPublished` consumer needs `CritterBids.Contracts.Selling.ListingPublished` and produces `CritterBids.Contracts.Auctions.BiddingOpened`. S2's scaffold is Contracts-independent.

---

## S2b — AuctionsModule.cs

### Handler / structure after

```csharp
public static class AuctionsModule
{
    public static IServiceCollection AddAuctionsModule(this IServiceCollection services)
    {
        services.ConfigureMarten(opts =>
        {
            opts.Schema.For<Listing>().DatabaseSchemaName("auctions");
            opts.Projections.LiveStreamAggregation<Listing>();
        });

        return services;
    }
}
```

### Structural metrics

| Metric | Value |
|--------|-------|
| `opts.Events.AddEventType<T>()` calls | **0** (explicit — first-use rule) |
| `opts.Schema.For<T>()` calls | 1 (`Listing` → `auctions`) |
| `opts.Projections.*` calls | 1 (`LiveStreamAggregation<Listing>`) |
| BC-internal services registered | 0 (no `AddTransient`, `AddSingleton`, etc.) |
| Non-Marten `services.*` calls | 0 |

### Why `LiveStreamAggregation<Listing>` and not `Snapshot<Listing>`

Prompt instruction verbatim: "Internal shape mirrors `SellingModule.AddSellingModule` exactly." But Selling uses `Snapshot<SellerListing>(SnapshotLifecycle.Inline)` at its final shape, not `LiveStreamAggregation`. The session prompt's code block explicitly specified `LiveStreamAggregation<Listing>()`, so that's what landed. S4's DCB author will reconsider lifecycle (live vs snapshot) when real Apply methods arrive; the prompt's Open Question #2 language ("S4 needs the projection registered") signals S4 can switch lifecycle without contract churn.

---

## S2c — Listing.cs + the Marten-8 validator blocker

### Final shape

```csharp
public class Listing
{
    public Guid Id { get; set; }
    // S4 adds bidding state fields (CurrentHighBid, BidderId, BidCount, ReserveStatus, BuyItNowAvailable, ScheduledCloseAt) and Apply() methods per the DCB boundary model.

    // ─── S2 scaffold placeholder — removed in S4 ───────────────────────────────
    // [20 lines of comment explaining the placeholder's purpose and S4 replacement path]
    public sealed record ScaffoldPlaceholder(Guid Id);

    public void Apply(ScaffoldPlaceholder @event)
    {
        // No-op — see placeholder comment above. Removed in S4.
    }
}
```

### Discovery — the verbatim error

When Commit 2's wiring first tried to boot the `AuctionsTestFixture.AlbaHost.For<Program>`, the host startup threw inside `MapWolverineEndpoints` with:

> `JasperFx.Events.Projections.InvalidProjectionException : No matching conventional Apply/Create/ShouldDelete methods for the CritterBids.Auctions.Listing aggregate.`

Stack-relevant frames:

```
at JasperFx.Events.Aggregation.AggregateApplication`2.AssertValidity()
at JasperFx.Events.Aggregation.JasperFxAggregationProjectionBase`4.AssembleAndAssertValidity()
at Marten.Events.Projections.ProjectionOptions.SingleStreamProjection[T](...)
at Marten.Events.Projections.ProjectionOptions.LiveStreamAggregation[T](...)
at CritterBids.Auctions.AuctionsModule.<>c.<AddAuctionsModule>b__0_0(StoreOptions opts)
```

### Root cause

The prompt's Open Question #2 anticipated this exact scenario:

> "`opts.Projections.LiveStreamAggregation<Listing>()` binds to an aggregate type that has no `Apply()` methods yet. Marten tolerates this (live aggregation against zero events returns a default-constructed aggregate). If a Marten 8 change surfaces an error on this shape, it is a genuine blocker — stop and flag rather than dropping the projection line."

The assumption "Marten tolerates this" does not hold in the Marten 8 / JasperFx.Events line on disk. `JasperFxAggregationProjectionBase.AssembleAndAssertValidity()` is an upfront validation pass that requires at least one `Apply`/`Create`/`ShouldDelete` method before it will accept a `SingleStreamProjection` / `LiveStreamAggregation` registration. An aggregate with no event-handler methods fails this validator regardless of whether any events exist in the store.

Why the error surfaces during `MapWolverineEndpoints` rather than at `AddMarten` time: the store isn't built until DI resolution. Selling's HTTP endpoints carry `[WriteAggregate]` parameters, and `WriteAggregateAttribute.Modify` (inside Wolverine HTTP code-gen during `MapWolverineEndpoints`) is the first call path that pulls `IDocumentStore` out of DI. HTTP discovery is not governed by `CustomizeHandlerDiscovery` (only *handler* discovery is), so the Selling HTTP exclusion present in `AuctionsTestFixture` did not prevent the code-gen path from firing — but the blocker is not the exclusion's fault. The failing `ConfigureMarten` lambda belonged to Auctions itself.

### Resolution — Option 2 of a four-option triage

The blocker was escalated mid-session to the prompt author with a four-option triage:

1. Drop `LiveStreamAggregation<Listing>()` — rejected, contradicts stop-and-flag.
2. Add a no-op placeholder Apply — **chosen**.
3. Promote an S3/S4 Apply (e.g., `BiddingOpened`) into S2 — rejected, violates "no `AddEventType<T>()`" and pulls in a Contracts reference early.
4. Revert Commit 2 and stop — rejected in favor of Option 2 after the placeholder shape was agreed.

The chosen placeholder:

- Is a `public sealed record ScaffoldPlaceholder(Guid Id)` declared as a nested type on `Listing`.
- Has a paired `public void Apply(ScaffoldPlaceholder @event) { }` with an empty body.
- Is never appended, never published, never registered via `AddEventType<T>()`.
- Does not cross the Contracts boundary (Contracts does not reference Auctions).

### Why the placeholder is `public` rather than `internal`

Initial attempt: `internal sealed record ScaffoldPlaceholder(Guid Id)` with `public void Apply(...)`. Compiler rejected with:

> `error CS0051: Inconsistent accessibility: parameter type 'Listing.ScaffoldPlaceholder' is less accessible than method 'Listing.Apply(Listing.ScaffoldPlaceholder)'`

Promoted `ScaffoldPlaceholder` to `public`. The placeholder still never leaves the Auctions assembly — `CritterBids.Contracts` has no reference to `CritterBids.Auctions`, so `ScaffoldPlaceholder` is not part of any integration contract surface. Internal-to-BC visibility was the actual isolation goal; `public sealed` inside the Auctions assembly preserves that.

### What S4 must do

S4's prompt needs a line that reads "delete `Listing.ScaffoldPlaceholder` and `Listing.Apply(ScaffoldPlaceholder)` in the same diff that adds the first real `Apply(BiddingOpened)`." The placeholder is the only line of code in Auctions that exists purely to defeat an upstream validator; it should not survive the first real behavior landing.

### Structural metrics

| Metric | After |
|--------|-------|
| Public properties on `Listing` | 1 (`Id`) |
| State fields beyond `Id` | 0 |
| `Apply(T)` methods on `Listing` | 1 (placeholder, no-op) |
| Event types registered via `AddEventType<T>` | 0 |
| Integration-contract types exposed by the aggregate file | 0 (placeholder stays assembly-internal) |

---

## S2d — slnx edits

### Before / After

| Folder | Before | After |
|--------|--------|-------|
| `/src/` | AppHost, Api, Contracts, Listings, Participants, Selling (6) | AppHost, Api, **Auctions**, Contracts, Listings, Participants, Selling (7) |
| `/tests/` | Api.Tests, Contracts.Tests, Listings.Tests, Participants.Tests, Selling.Tests (5) | Api.Tests, **Auctions.Tests**, Contracts.Tests, Listings.Tests, Participants.Tests, Selling.Tests (6) |

The insertion points honor the alphabetical-by-BC-name rule from `adding-bc-module.md` §Project Structure. No reshuffling of existing nodes.

---

## S2e — CritterBids.Api.csproj ProjectReference

```xml
<ItemGroup>
  <ProjectReference Include="..\CritterBids.Auctions\CritterBids.Auctions.csproj" />
  <ProjectReference Include="..\CritterBids.Listings\CritterBids.Listings.csproj" />
  <ProjectReference Include="..\CritterBids.Participants\CritterBids.Participants.csproj" />
  <ProjectReference Include="..\CritterBids.Selling\CritterBids.Selling.csproj" />
</ItemGroup>
```

Required for `typeof(Listing).Assembly` in `Program.cs` to resolve per the M2-S7 discovery documented in `adding-bc-module.md` §Checklist. Not optional. Placed alphabetically first as the leading alphabetical position.

---

## S2f — Program.cs three-line diff

```diff
+ using CritterBids.Auctions;
  using CritterBids.Contracts;
  using CritterBids.Listings;
  using CritterBids.Participants;
  using CritterBids.Selling;
  ...
      opts.Discovery.IncludeAssembly(typeof(Participant).Assembly);
      opts.Discovery.IncludeAssembly(typeof(SellerListing).Assembly);
      opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly);
+     opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);
  ...
      builder.Services.AddParticipantsModule();
      builder.Services.AddSellingModule();
      builder.Services.AddListingsModule();
+     builder.Services.AddAuctionsModule();
```

### Why `typeof(Listing)` is unambiguous

Pre-session grep for `class Listing\b|record Listing\b` across `src/` returned zero matches — no other BC defines a `Listing` type. Listings BC owns `CatalogListingView`, Selling owns `SellerListing`, Participants owns `Participant`. The unqualified `Listing` in `typeof(Listing).Assembly` resolves to `CritterBids.Auctions.Listing` through the new `using CritterBids.Auctions;` without ambiguity.

### Negative assertion — RabbitMQ routing

| Metric | Value |
|--------|-------|
| `opts.PublishMessage<Auctions*>` lines added | **0** |
| `opts.ListenToRabbitQueue("auctions-*")` lines added | **0** |
| `opts.ListenToRabbitQueue("listings-auctions-events")` lines added | **0** |

Per the out-of-scope list: `auctions-selling-events` wires in S3, `listings-auctions-events` in S5 or S6.

---

## S2g — Smoke test shape

### Structure mirrored after `ListingsTestFixture`

Chose to mirror `ListingsTestFixture` over `SellingTestFixture` because Listings is the more recent of the two Marten-BC fixtures that already incorporates the cross-BC handler exclusion pattern (`IWolverineExtension` via `CustomizeHandlerDiscovery`) needed whenever the fixture does not register `AddSellingModule()`.

| Fixture element | Source pattern | Auctions-specific change |
|-----------------|----------------|--------------------------|
| `PostgreSqlBuilder("postgres:17-alpine")` | Listings | container name prefix `auctions-postgres-test-` |
| `AlbaHost.For<Program>` with `ConfigureServices` override | Listings | registers `AddAuctionsModule()` not `AddListingsModule()` |
| `services.AddMarten(...).UseLightweightSessions().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` | Listings verbatim | unchanged |
| `RunWolverineInSoloMode()` + `DisableAllExternalWolverineTransports()` | Listings verbatim | unchanged |
| `SellingBcDiscoveryExclusion : IWolverineExtension` | Listings verbatim | namespace changed to `CritterBids.Auctions.Tests.Fixtures`, exclusion condition message updated to reference "Auctions fixture" |
| Cleanup helpers (`CleanAllMartenDataAsync`, `ResetAllMartenDataAsync`, `GetDocumentSession`) | Listings verbatim | unchanged |

### Test

```csharp
[Fact]
public void AuctionsModule_BootsClean()
{
    _fixture.Host.ShouldNotBeNull();
    var store = _fixture.Host.Services.GetRequiredService<IDocumentStore>();
    store.ShouldNotBeNull();
}
```

Exact shape of `SellingModuleTests.SellingModule_BootsClean`. One fact, two assertions — sufficient as a green guard against future scaffold-breaking edits.

### Why no Wolverine tracking helpers in the fixture

`SellingTestFixture` carries `ExecuteAndWaitAsync` and `TrackedHttpCall` helpers because Selling's tests exercise message-tracking scenarios. `ListingsTestFixture` does not, and S2's one fact does not need them. Auctions fixture mirrors Listings' leaner shape. S3 adds tracking helpers when the first consumer needs them, following the same "add what the next test needs, not ceremonial infrastructure" pattern.

---

## Test results

| Phase | Auctions.Tests | All Tests | Result |
|-------|---------------|-----------|--------|
| Baseline | 0 (project absent) | 44 | Green |
| After Commit 1 | 0 (project absent) | 44 | Green (standalone Auctions dll compiles; nothing else touched) |
| After Commit 2 edits — first test run | 1 | 44 + 1 failing | **Red** — `InvalidProjectionException` (S2c blocker) |
| After Commit 2 placeholder resolution | 1 passing | 45 | Green |
| Session close (after Commit 3 retro) | 1 passing | 45 | Green |

Test count delta across the session: **+1** (the one scaffold smoke test).

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test CritterBids.slnx` — 45 passing (1 Api + 1 Auctions + 1 Contracts + 4 Listings + 6 Participants + 32 Selling)
- `.cs` files created in `src/CritterBids.Auctions/`: 2 (`AuctionsModule.cs`, `Listing.cs`)
- `.csproj` files created: 2 (`CritterBids.Auctions.csproj`, `CritterBids.Auctions.Tests.csproj`)
- Production handlers authored: **0**
- Commands authored: **0**
- HTTP endpoints authored: **0**
- Saga definitions authored: **0**
- DCB artifacts authored: **0** (no `[BoundaryModel]`, no `EventTagQuery`, no `BidConsistencyState`)
- `opts.Events.AddEventType<T>()` calls in AuctionsModule: **0**
- RabbitMQ routing lines added to Program.cs: **0**
- `ProjectReference` from Auctions to Contracts: **absent** (S3 adds it)
- `ProjectReference` from Api to Auctions: **present** (S2e)
- Contracts project edits: **0** (the nine S1 stubs stand as-authored)

---

## Key learnings

1. **Open Questions in a session prompt are load-bearing.** The prompt's Open Question #2 correctly identified the one failure mode that would convert this session from a docs-like scaffold into a real debugging episode, and it pre-committed the session to a stop-and-flag response rather than a silent projection-line drop. Without that anticipation, the mid-session default would likely have been to remove the projection line and quietly push the discovery to S4 — where it would have been a worse blocker because the S4 author would be debugging DCB behavior at the same time. The lesson generalizes: when a prompt author sees a specific upstream-version-dependent assumption, naming it and pre-specifying the failure response is a first-class scope item, not overhead.

2. **JasperFx.Events `AssembleAndAssertValidity` is upfront, not lazy.** The validator runs the moment a `LiveStreamAggregation<T>` / `SingleStreamProjection<T>` is constructed inside a `ConfigureMarten` lambda — which means the first time anything in DI resolves `IDocumentStore`, the lambda runs and the validator fires. There is no "register now, validate on first use" escape hatch. Any future BC scaffolding session that registers a projection ahead of its Apply methods needs to know this and budget for a placeholder or defer the registration.

3. **Wolverine HTTP discovery runs a separate pass from handler discovery.** The `CustomizeHandlerDiscovery` exclusion pattern used in Marten BC test fixtures (the `SellingBcDiscoveryExclusion : IWolverineExtension`) excludes **handler** types only. HTTP endpoints discovered via `[WolverinePost]`, `[WolverineGet]`, etc. pass through `HttpGraph.DiscoverEndpoints` → `HttpChainParameterAttributeStrategy` independently, and attributes like `[WriteAggregate]` there still resolve `IDocumentStore` during code-gen. If a future test fixture needs to exclude an entire BC from code-gen (not just its handlers), it needs a second exclusion on the HTTP path. For S2 the blocker was not the exclusion's failure — the projection lambda belonged to Auctions itself — but the investigation surfaced this structural distinction that `critter-stack-testing-patterns.md` should probably call out.

4. **Cross-BC test fixture patterns do not need to be invented once more per BC.** `AuctionsTestFixture` is a near-verbatim clone of `ListingsTestFixture` with three concrete changes (container name prefix, `AddXxxModule` call, exclusion-class namespace). The Selling-fixture-vs-Listings-fixture divergence (tracking helpers present in Selling, absent in Listings) is a real signal — the choice of which fixture to mirror should be driven by "what does the first test need" rather than "most recent fixture wins." Future BC scaffolds should make the same Listings-minimal vs Selling-rich choice explicitly, not by accident.

5. **Placeholder code that exists to defeat an upstream validator should be marked with its removal trigger in-file.** The `ScaffoldPlaceholder` record and its Apply method carry an explicit block comment naming S4 as the removal-point and listing the Apply methods that will replace it. That comment turns an otherwise surprising code block ("why is there a no-op Apply on an aggregate that's supposed to be empty?") into a self-documenting S4 handoff item. The general rule: scaffolding that isn't real behavior should document when and why it ceases to exist, so the next session has a search term to grep for.

---

## Skill gaps surfaced

- **`adding-bc-module.md` — nothing to add from this session.** The skill's Marten BC module registration, `Program.cs` wiring, and checklist all applied verbatim. The blocker that surfaced was a Marten-8 validator behavior, not a skill gap.
- **`critter-stack-testing-patterns.md` — one potential addition worth considering in a later docs session.** Key learning #3 (Wolverine HTTP discovery vs handler discovery) is a real distinction not currently documented in the skill. If a future session needs to exclude HTTP endpoints from code-gen (not just handlers), the existing `SellingBcDiscoveryExclusion` pattern is not sufficient, and the skill currently implies that it is. A single "HTTP discovery is a separate pass" note alongside the Cross-BC Handler Isolation section would close the gap. **Not fixed in this session** per the prompt's "do not edit skills in-session" rule; flagged here for the next skills-maintenance pass.
- **Session-prompt template — the Open Question #2 pattern is durable.** The shape "state the author's assumption; specify the stop-and-flag behavior if the assumption doesn't hold; name what S{n+k} depends on" produced exactly the right outcome here. Worth keeping as a durable prompt-authoring pattern; no edit needed.

---

## Verification checklist

- [x] `src/CritterBids.Auctions/CritterBids.Auctions.csproj` exists; `WolverineFx.Http.Marten` package reference matches sibling BC projects; no ProjectReference to Contracts
- [x] `tests/CritterBids.Auctions.Tests/CritterBids.Auctions.Tests.csproj` exists; references `CritterBids.Auctions` and `CritterBids.Api`; package references (Alba, Microsoft.NET.Test.Sdk, Testcontainers.PostgreSql, xunit, xunit.runner.visualstudio, Shouldly) match sibling test projects
- [x] `CritterBids.slnx` contains `<Project>` entries for both new projects under the `/src/` and `/tests/` folder nodes respectively
- [x] `src/CritterBids.Auctions/AuctionsModule.cs` defines `AddAuctionsModule(this IServiceCollection services) : IServiceCollection` calling `services.ConfigureMarten(...)` with the `auctions` schema mapping and `LiveStreamAggregation<Listing>()` projection registration
- [x] `src/CritterBids.Auctions/Listing.cs` contains a class with `Id` plus the S4 forward-comment — *and* the Marten-8-validator-required `ScaffoldPlaceholder` record + no-op Apply per the S2c blocker resolution (documented inline and in this retro)
- [x] `AuctionsModule.cs` contains zero `opts.Events.AddEventType<T>()` calls
- [x] `src/CritterBids.Api/CritterBids.Api.csproj` has a `<ProjectReference>` to `CritterBids.Auctions.csproj`
- [x] `src/CritterBids.Api/Program.cs` — `using CritterBids.Auctions;` present; `opts.Discovery.IncludeAssembly(typeof(Listing).Assembly);` present after the existing `IncludeAssembly` lines; `builder.Services.AddAuctionsModule();` present after `AddListingsModule();`
- [x] `Program.cs` `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks contain zero Auctions-related lines
- [x] At least one test in `CritterBids.Auctions.Tests` asserts the Auctions module registers cleanly alongside the other BC modules and the Api boots green (`AuctionsModule_BootsClean` — one fact, two assertions)
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] `dotnet test` — all green; baseline 44 tests still pass; new smoke test passes; total 45
- [x] This retrospective exists; records the solution layout delta, the Program.cs edit diff (three lines added), the smoke-test shape chosen, the skill gap discovered, and a one-paragraph "what M3-S3 should know" note (below)

---

## What M3-S3 should know

**M3-S3 is the first handler session for Auctions** — the `ListingPublished` consumer that produces `BiddingOpened`. S3 walks in with the Auctions module registered, the `Listing` aggregate type registered for `LiveStreamAggregation` via a one-line `ScaffoldPlaceholder`-backed placeholder Apply, and no event types registered. Concrete items S3 should walk in with:

1. **The `ScaffoldPlaceholder` stays until S4.** S3 does not remove it. S3's work produces `BiddingOpened` but the first real `Apply(BiddingOpened)` lands in S4 as part of the DCB boundary model (`BiddingOpened` establishes the stream that `PlaceBid` writes to). Removing the placeholder in S3 would recreate the exact blocker this session resolved.
2. **`CritterBids.Auctions` gains its first `<ProjectReference Include="...CritterBids.Contracts.csproj" />` in S3.** The `ListingPublished` consumer needs `CritterBids.Contracts.Selling.ListingPublished` as input and produces `CritterBids.Contracts.Auctions.BiddingOpened`. This is a scaffold edit, not a discovery.
3. **`auctions-selling-events` is the S3 queue wiring.** Added to `Program.cs` inside the existing RabbitMQ-guarded block. `opts.PublishMessage<SellerRegistrationCompleted>().ToRabbitQueue("selling-participants-events")` is the shape to mirror.
4. **`opts.Events.AddEventType<BiddingOpened>()` lands in S3's `ConfigureMarten` callback.** This is the first event-type registration for Auctions. The rest of the nine contracts stay unregistered until S4 (bid-and-friends batch) and S5 (closing-outcome batch).
5. **`AuctionsTestFixture` is ready for S3.** The fixture is a clone of `ListingsTestFixture` with the `SellingBcDiscoveryExclusion` in place. If S3's handler test needs Wolverine tracking helpers (`ExecuteAndWaitAsync`, `TrackedHttpCall`), add them following the `SellingTestFixture` shape. If S3's test just needs a document session and a `Host.Scenario(...)` call, the existing fixture shape is sufficient as-is.
6. **The Marten-8 projection validator landmine is now a known quantity.** When S4 finally removes `ScaffoldPlaceholder` and wires real `Apply(BiddingOpened)`, the first `dotnet test` after the removal will surface the same validator behavior if any of the new Apply methods has a signature defect. S4 should expect-and-check for the same `InvalidProjectionException` class.

---

## What remains / deferred into later M3 sessions

**In scope for M3, deferred to later sessions:**

- `ListingPublished` → `BiddingOpened` consumer + its handler test (S3)
- DCB `BidConsistencyState` boundary model, `PlaceBid` + `BuyNow` handlers, `EventTagQuery` (S4)
- Auction Closing saga with cancellable scheduled close (S5)
- Listings catalog auction-status field additions to `CatalogListingView` (S6)
- ADR 007 Gate 4 re-evaluation before M3-S4 prompt draft (M3 trigger, unchanged)
- M3 milestone retrospective (S7)
- `ScaffoldPlaceholder` removal (S4, specifically; paired with the first real Apply method)

**Out of scope for M3, tracked elsewhere:**

- Proxy Bid Manager saga, Session aggregate (M4)
- Settlement BC consuming `ListingSold` / `BuyItNowPurchased` (M5)
- Auth across Auctions endpoints (M6; `[AllowAnonymous]` stance unchanged through M5)

**Still deferred from M2:**

- RabbitMQ routing in BC modules vs `Program.cs` (threading `WolverineOptions` into module methods) — deferred again in M3 per `M3-auctions-bc.md` §8
