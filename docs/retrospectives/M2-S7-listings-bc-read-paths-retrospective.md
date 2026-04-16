# M2-S7: Listings BC — Scaffold, CatalogListingView, Read Paths — Retrospective

**Date:** 2026-04-15
**Milestone:** M2 — Listings Pipeline
**Session:** S7 — Scaffold Listings BC, CatalogListingView document, ListingPublishedHandler, read endpoints, integration tests
**Agent:** @PSA
**Prompt:** `docs/prompts/M2-S7-listings-bc-and-read-paths.md`

---

## Baseline

- 38 tests passing: Selling (30), Participants (6), Api (1), Contracts (1)
- `dotnet build` succeeds with 0 errors, 0 warnings
- `CritterBids.Contracts.Selling.ListingPublished` integration contract exists with 13 fields
- `Program.cs` declared `opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>().ToRabbitQueue("listings-selling-events")` — publish side wired
- No `CritterBids.Listings` or `CritterBids.Listings.Tests` projects existed
- No `GET /api/listings` or `GET /api/listings/{id}` endpoints existed
- No `opts.ListenToRabbitQueue("listings-selling-events")` — consume side not yet wired

---

## Items completed

| Item | Description |
|------|-------------|
| S7a | `CritterBids.Listings` class library project — added to `CritterBids.slnx` |
| S7b | `CritterBids.Listings.Tests` xUnit project — added to `CritterBids.slnx` |
| S7c | `AddListingsModule()` using `services.ConfigureMarten()` — no `AddMarten()`, no `IntegrateWithWolverine()` |
| S7d | `CatalogListingView` sealed record — 8 fields, `listings` schema, `Guid Id` as Marten document identity |
| S7e | `ListingPublishedHandler.Handle(ListingPublished, IDocumentSession)` — static void, no `[MartenStore]`, no `SaveChangesAsync()` |
| S7f | `GET /api/listings` and `GET /api/listings/{id}` Wolverine.HTTP endpoints — both `[AllowAnonymous]` |
| S7g | `Program.cs` — `AddListingsModule()`, `IncludeAssembly(CatalogListingView)`, `ListenToRabbitQueue("listings-selling-events")` |
| S7h | `ListingsTestFixture`, `ListingsTestCollection`, `CatalogListingViewTests` — 4 integration tests (scenarios 1.3 and 1.4) |

---

## S7a/S7b: Project scaffolds

**`CritterBids.Listings.csproj`:** Single package reference `WolverineFx.Http.Marten` (transitively includes `WolverineFx.Http`, `WolverineFx.Marten`, and `Marten`). Single project reference to `CritterBids.Contracts`. No BC-to-BC project references.

**`CritterBids.Listings.Tests.csproj`:** Same test package set as `CritterBids.Selling.Tests` (`Alba`, `Microsoft.NET.Test.Sdk`, `Testcontainers.PostgreSql`, `xunit`, `xunit.runner.visualstudio`, `Shouldly`). Project references to `CritterBids.Api` and `CritterBids.Listings`.

**`CritterBids.slnx` format note:** The solution file is `.slnx` (XML format), not `.sln`. Projects are added as `<Project Path="..."/>` elements inside `<Folder>` containers. `TargetFramework` is set globally in `Directory.Build.props` and does not need to appear in individual `.csproj` files.

**Deviation:** The prompt did not explicitly state that `CritterBids.Api.csproj` needed a project reference to `CritterBids.Listings`. A build error (`CS0234: The type or namespace name 'Listings' does not exist in the namespace 'CritterBids'`) at gate 3 revealed this requirement. The reference was added to `src/CritterBids.Api/CritterBids.Api.csproj` alongside the existing `CritterBids.Participants` and `CritterBids.Selling` references.

---

## S7c: `AddListingsModule()`

Follows the exact shape of `SellingModule.cs`:

```csharp
services.ConfigureMarten(opts =>
{
    opts.Schema.For<CatalogListingView>().DatabaseSchemaName("listings");
});
```

No `IConfiguration` parameter, no `IntegrateWithWolverine()`, no `ApplyAllDatabaseChangesOnStartup()`. `AddListingsModule()` has one responsibility: contributing `CatalogListingView` to the shared Marten store under the `listings` schema. No BC-internal services are needed at M2.

---

## S7d: `CatalogListingView`

Eight fields — `Id`, `SellerId`, `Title`, `Format`, `StartingBid`, `BuyItNow`, `Duration`, `PublishedAt`. `Format` is `string` (not an enum) because `ListingFormat` is internal to `CritterBids.Selling` and unavailable in the Listings BC. The contract's `string Format` field is the source. `ReservePrice`, `FeePercentage`, and `ExtendedBidding*` are omitted — not needed for catalog browse; they remain in the contract for Settlement (M5) and Auctions (M3).

---

## S7e: `ListingPublishedHandler`

Static void handler — no `async`, no return value. Mirrors `SellerRegistrationCompletedHandler`. Key constraints confirmed:

- No `[MartenStore]` attribute — single primary store, no attribute needed (ADR 009)
- No `SaveChangesAsync()` — `AutoApplyTransactions()` in `Program.cs` commits after `Handle()` returns
- No `OutgoingMessages` or `IMessageBus` — this handler produces no downstream messages in M2

---

## S7f: Read endpoints

`GET /api/listings` returns `Task<IReadOnlyList<CatalogListingView>>` from `session.Query<CatalogListingView>().OrderByDescending(x => x.PublishedAt).ToListAsync()`. Returns an empty array when no listings exist — never 404.

`GET /api/listings/{id}` returns `Task<IResult>` via `session.LoadAsync<CatalogListingView>(id)`. Returns `Results.NotFound()` when the document is absent, `Results.Ok(view)` when found.

Both endpoints use `IQuerySession` (read-only) rather than `IDocumentSession`. Both carry `[AllowAnonymous]` per the M2–M5 project-wide stance.

---

## S7g: `Program.cs` wiring

Three changes:

1. `using CritterBids.Listings;` added to using directives
2. `opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly)` added inside `UseWolverine()` alongside the Participants and Selling BC assembly includes
3. `opts.ListenToRabbitQueue("listings-selling-events")` added inside the RabbitMQ-guarded block after the existing publish rule
4. `builder.Services.AddListingsModule()` added inside the postgres-guarded block after `AddSellingModule()`

**Deviation from prompt:** The prompt specified three changes to `Program.cs`. An undocumented fourth change was required — adding `using CritterBids.Listings;` to the using directives so `typeof(CatalogListingView)` can be resolved. This is implicit in any project that uses a new BC's types.

---

## S7h: Test fixture and tests

### `ListingsTestFixture`

Follows the `SellingTestFixture` shape precisely:
- `PostgreSqlBuilder("postgres:17-alpine")` Testcontainers container
- `JasperFxEnvironment.AutoStartHost = true`
- `ConfigureServices` registers `AddMarten(...)` + `AddListingsModule()` + `RunWolverineInSoloMode()` + `DisableAllExternalWolverineTransports()`

**Addition beyond base pattern — `SellingBcDiscoveryExclusion`:**

The Listings fixture does not call `AddSellingModule()`, which means `ISellerRegistrationService` is not registered. `CreateDraftListingHandler.ValidateAsync` injects this service; without the exclusion, Wolverine's handler code-gen would fail at startup. An `IWolverineExtension` singleton excludes all `CritterBids.Selling.*` handlers per the cross-BC isolation pattern documented in `critter-stack-testing-patterns.md` §Cross-BC Handler Isolation:

```csharp
services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
```

The `SellingTestFixture` does **not** require a symmetric `ListingsBcDiscoveryExclusion` because `ListingPublishedHandler.Handle` only injects `IDocumentSession` — available via the primary Marten store registered in the Selling fixture's `ConfigureServices`.

### Test method naming

| Method | Scenario |
|--------|----------|
| `GetCatalog_AfterListingPublished_ReturnsCatalogEntry` | 1.3 — listings appear after publish |
| `GetCatalog_BeforePublish_ReturnsEmptyList` | 1.3 — no listings, empty array |
| `GetListingDetail_PublishedListing_ReturnsDetail` | 1.4 — detail returns 200 + fields |
| `GetListingDetail_UnknownId_Returns404` | 1.4 — unknown ID returns 404 |

### `GetAsJson` deviation

The prompt suggested `_fixture.Host.GetAsJson<List<CatalogListingView>>(url)`. `GetAsJson` does not exist on `IAlbaHost` in Alba 8.5.2. The Scenario-based pattern consistent with the Selling test suite was used instead:

```csharp
var result = await _fixture.Host.Scenario(s =>
{
    s.Get.Url("/api/listings");
    s.StatusCodeShouldBe(200);
});
var response = await result.ReadAsJsonAsync<List<CatalogListingView>>();
```

### Handler invocation in test setup

Per the prompt, the handler is invoked directly (not via the bus). `SaveChangesAsync()` is called explicitly in test setup because `AutoApplyTransactions()` only fires through the Wolverine pipeline:

```csharp
await using var session = _fixture.GetDocumentSession();
ListingPublishedHandler.Handle(message, session);
await session.SaveChangesAsync();
```

---

## Test results

| Phase | Listings | Selling | Participants | Api | Contracts | Total | Result |
|-------|----------|---------|-------------|-----|-----------|-------|--------|
| Baseline (before S7) | — | 30 | 6 | 1 | 1 | 38 | ✅ |
| After S7 implementation | 4 | 30 | 6 | 1 | 1 | **42** | ✅ |

**New tests:**

| Test file | Scenarios covered | Count |
|-----------|-------------------|-------|
| `CatalogListingViewTests.cs` | 1.3, 1.4 | 4 |

---

## Build state at session close

- `dotnet build` exits with 0 errors, 0 warnings
- `dotnet test` 42/42 passing
- `AddListingsModule()` uses `services.ConfigureMarten()` — not `AddMarten()` or `AddMartenStore<T>()`: ✅
- `CatalogListingView.Format` is `string`: ✅
- `ListingPublishedHandler.Handle` — no `[MartenStore]`, no `SaveChangesAsync()`, no `IMessageBus`: ✅
- `GET /api/listings` returns empty array (not 404) on no results: ✅
- `GET /api/listings/{id}` returns 404 on unknown ID: ✅
- Both endpoints carry `[AllowAnonymous]`: ✅
- `opts.ListenToRabbitQueue("listings-selling-events")` in RabbitMQ-guarded block: ✅
- `services.DisableAllExternalWolverineTransports()` in fixture: ✅
- `services.RunWolverineInSoloMode()` in fixture: ✅
- `SellingBcDiscoveryExclusion` in fixture: ✅
- `CleanAllMartenDataAsync()` in `InitializeAsync()`: ✅
- All 6 session prompt deliverables verified against checklist: ✅

---

## Key learnings

1. **`CritterBids.Api.csproj` project reference is an implicit requirement.** Adding a new BC project to the solution and referencing it in `Program.cs` requires a project reference in the Api `.csproj`. The prompt's wiring section documents the using directive and the `AddListingsModule()` call but not the project reference itself — this is discovered at first build. Checklist item to add to `adding-bc-module.md`: "Add project reference to `CritterBids.Api.csproj`."

2. **Cross-BC handler isolation is asymmetric.** The Listings fixture excludes Selling handlers (because `ISellerRegistrationService` is absent), but the Selling fixture does not need to exclude Listings handlers (because `ListingPublishedHandler` only injects `IDocumentSession`, which is always present). The deciding factor is whether the discovered handlers have unresolvable DI dependencies — service injection requirements, not just assembly membership.

3. **`Alba.IAlbaHost.GetAsJson<T>` does not exist in Alba 8.5.2.** The prompt referenced this method; it is not part of the Alba 8.x API surface. The correct pattern is `Host.Scenario(s => { s.Get.Url(url); s.StatusCodeShouldBe(200); })` followed by `result.ReadAsJsonAsync<T>()`, which is consistent with the Selling test suite's existing patterns.

4. **`.slnx` requires XML-aware editing.** The solution file in this project is `CritterBids.slnx` (Visual Studio XML solution format), not the legacy `.sln` text format. Project entries are `<Project Path="..."/>` elements nested inside `<Folder>` containers. The Glob tool's `*.sln` pattern does not match `.slnx` files — use `*.slnx` or look for the file by exact name.

5. **`AutoApplyTransactions()` does not fire on direct handler invocations.** In test setup that calls `ListingPublishedHandler.Handle(message, session)` directly, `SaveChangesAsync()` must be called explicitly. Without it, the document is queued in the unit-of-work but never persisted — the subsequent GET returns an empty result. This is documented in `critter-stack-testing-patterns.md` but is easy to miss when the prompt's handler code comment says "no `SaveChangesAsync()`."

---

## Verification checklist

- [x] `CritterBids.Listings.csproj` exists and is added to `CritterBids.slnx`
- [x] `CritterBids.Listings.Tests.csproj` exists and is added to `CritterBids.slnx`
- [x] `CritterBids.Listings` does not reference any other BC project
- [x] `AddListingsModule()` takes no parameters
- [x] `AddListingsModule()` calls `services.ConfigureMarten()` — not `AddMarten()` or `AddMartenStore<T>()`
- [x] `opts.Schema.For<CatalogListingView>().DatabaseSchemaName("listings")` present
- [x] No `IntegrateWithWolverine()` or `ApplyAllDatabaseChangesOnStartup()` inside `AddListingsModule()`
- [x] `CatalogListingView` is a `sealed record` with `Guid Id` as Marten document identity
- [x] `CatalogListingView.Format` is `string`, not an enum
- [x] `ListingPublishedHandler.Handle` signature: `(ListingPublished message, IDocumentSession session)`
- [x] No `[MartenStore]` attribute on `ListingPublishedHandler`
- [x] No `SaveChangesAsync()` inside `ListingPublishedHandler.Handle`
- [x] No `IMessageBus` or `OutgoingMessages` in `ListingPublishedHandler`
- [x] `GET /api/listings` returns `IReadOnlyList<CatalogListingView>`, empty array when no results
- [x] `GET /api/listings/{id}` returns 200 + view when found, 404 when not found
- [x] Both endpoints carry `[AllowAnonymous]`
- [x] `builder.Services.AddListingsModule()` present in `Program.cs` (no arguments)
- [x] `opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly)` present in `UseWolverine()`
- [x] `opts.ListenToRabbitQueue("listings-selling-events")` present in the RabbitMQ-guarded block
- [x] `ListingsTestFixture.cs` registers `AddMarten()` + `AddListingsModule()` in `ConfigureServices`
- [x] `services.RunWolverineInSoloMode()` present in fixture
- [x] `services.DisableAllExternalWolverineTransports()` present in fixture
- [x] `SellingBcDiscoveryExclusion` registered as `IWolverineExtension` in fixture
- [x] `CatalogListingViewTests.cs` contains all 4 tests with correct method names
- [x] Each test that seeds data calls `CleanAllMartenDataAsync()` for isolation (via `InitializeAsync()`)
- [x] `dotnet build` → 0 errors, 0 warnings
- [x] `dotnet test` → 42 tests passing, 0 failures

---

## Files changed

**New — Listings BC:**
- `src/CritterBids.Listings/CritterBids.Listings.csproj` — class library, `WolverineFx.Http.Marten` + Contracts ref
- `src/CritterBids.Listings/ListingsModule.cs` — `AddListingsModule()` extension method
- `src/CritterBids.Listings/CatalogListingView.cs` — Marten document, 8 fields, `listings` schema
- `src/CritterBids.Listings/ListingPublishedHandler.cs` — static void handler, stores `CatalogListingView`
- `src/CritterBids.Listings/Features/Catalog/CatalogEndpoints.cs` — `GET /api/listings` and `GET /api/listings/{id}`

**New — Tests:**
- `tests/CritterBids.Listings.Tests/CritterBids.Listings.Tests.csproj`
- `tests/CritterBids.Listings.Tests/GlobalUsings.cs`
- `tests/CritterBids.Listings.Tests/Fixtures/ListingsTestFixture.cs` — includes `SellingBcDiscoveryExclusion`
- `tests/CritterBids.Listings.Tests/Fixtures/ListingsTestCollection.cs`
- `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs` — 4 integration tests

**Modified — Infrastructure:**
- `CritterBids.slnx` — added `CritterBids.Listings` and `CritterBids.Listings.Tests`
- `src/CritterBids.Api/CritterBids.Api.csproj` — added project reference to `CritterBids.Listings`
- `src/CritterBids.Api/Program.cs` — `using CritterBids.Listings`, `IncludeAssembly(CatalogListingView)`, `AddListingsModule()`, `ListenToRabbitQueue("listings-selling-events")`

---

## What remains for S8

S8 is documentation-only (no code changes). It covers:

- `docs/skills/domain-event-conventions.md` — authored retrospectively from S5–S6 domain event patterns
- `docs/skills/adding-bc-module.md` — updates from S7 learnings (notably: Api project reference requirement, `GetAsJson` absence in Alba 8.x)
- `docs/retrospectives/M2-listings-pipeline-retrospective.md` — M2 milestone retrospective

The implementation phase of M2 is complete as of this session (S7). The full Selling → Listings integration pipeline is live: a submitted listing flows from the Selling BC outbox, over `listings-selling-events`, into the Listings BC, and becomes queryable via `GET /api/listings` and `GET /api/listings/{id}`.
