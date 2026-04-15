# M2-S7: Listings BC — Scaffold, CatalogListingView, Read Paths

**Milestone:** M2 — Listings Pipeline
**Session:** S7 of 8
**Prompt file:** `docs/prompts/M2-S7-listings-bc-and-read-paths.md`
**Agent:** @PSA
**Baseline:** 38 tests passing (Selling: 30, Participants: 6, Api: 1, Contracts: 1) · `dotnet build` 0 errors, 0 warnings · WolverineFx 5.31.0 · Microsoft.NET.Test.Sdk 18.4.0

---

## Goal

Deliver the Listings BC end-to-end: scaffold the project and test project, wire `CatalogListingView`
as a Marten document, implement the Wolverine handler that consumes `Contracts.Selling.ListingPublished`
off the RabbitMQ queue and writes the catalog view, expose `GET /api/listings` and
`GET /api/listings/{id}` read endpoints, and register everything in `Program.cs`.

At session close the full Selling → Listings integration pipeline is live: a submitted listing flows
from the Selling BC outbox, over `listings-selling-events`, into the Listings BC, and becomes
queryable via the catalog API. This is the last implementation session in M2.

---

## Skills — load before writing any code

Load each skill file and read it fully before writing any code. The patterns in these files are
the only source of truth. Do not guess or improvise.

| Skill file | Why required |
|---|---|
| `docs/skills/adding-bc-module.md` | Canonical pattern for scaffolding a new BC; test fixture shape; Program.cs wiring; anti-patterns to avoid |
| `docs/skills/marten-querying.md` | Read-path endpoint query patterns (`IQuerySession`, LINQ, `LoadAsync`) |
| `docs/skills/critter-stack-testing-patterns.md` | Integration test fixture, `DisableAllExternalWolverineTransports`, Alba usage |
| `docs/skills/integration-messaging.md` | `ListenToRabbitQueue`, consumer handler, transport isolation in tests |
| `docs/skills/wolverine-message-handlers.md` | Handler signatures, `IDocumentSession` injection, endpoint attribute patterns |

---

## Baseline context

### Current state (post-S6)

- `dotnet build` passes with 0 errors, 0 warnings
- 38 tests passing: Selling (30), Participants (6), Api (1), Contracts (1)
- `CritterBids.Selling` and `CritterBids.Selling.Tests` exist and are fully operational
- `CritterBids.Contracts/Selling/ListingPublished.cs` exists — 13-field integration contract
- `Program.cs` declares `opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>().ToRabbitQueue("listings-selling-events")` — publish side wired
- **No** `CritterBids.Listings` or `CritterBids.Listings.Tests` projects exist yet
- **No** `GET /api/listings` or `GET /api/listings/{id}` endpoints exist yet
- **No** `opts.ListenToRabbitQueue("listings-selling-events")` — consume side not yet wired

### Architectural baseline — shared primary Marten store (ADR 009)

CritterBids uses a **single primary `IDocumentStore`** registered once in `Program.cs`.
Each Marten BC contributes its types via `services.ConfigureMarten()` in `AddXyzModule()`.
Named stores (`AddMartenStore<T>()`) are explicitly rejected — see `adding-bc-module.md`
anti-patterns and ADR 009.

Schema isolation per BC is enforced at the document level:
`opts.Schema.For<T>().DatabaseSchemaName("listings")` inside `AddListingsModule()`.

The existing `SellingModule.cs` is the primary pattern reference. Inspect it before writing
any Listings code. The Listings BC must follow the same shape without exception.

---

## Deliverables

| ID | Deliverable |
|----|-------------|
| S7a | `CritterBids.Listings` project scaffold — class library, added to solution |
| S7b | `CritterBids.Listings.Tests` project scaffold — xUnit test project, added to solution |
| S7c | `AddListingsModule()` extension method using `services.ConfigureMarten()` |
| S7d | `CatalogListingView` sealed record — Marten document registered in `listings` schema |
| S7e | `ListingPublishedHandler` — Wolverine handler writing `CatalogListingView` from `Contracts.Selling.ListingPublished` |
| S7f | `GET /api/listings` and `GET /api/listings/{id}` Wolverine.HTTP endpoints |
| S7g | `Program.cs` wiring — `AddListingsModule()`, assembly discovery, `ListenToRabbitQueue` |
| S7h | `ListingsTestFixture.cs`, `ListingsTestCollection.cs`, `CatalogListingViewTests.cs` — 4 integration tests |

All deliverables are single-agent, strictly sequential. Complete each gate check before moving on.

---

## S7a: `CritterBids.Listings` project scaffold

Create `src/CritterBids.Listings/CritterBids.Listings.csproj` as a class library targeting `net10.0`.
Add to `CritterBids.sln`.

**Package references** (use `Directory.Packages.props` — no version attributes in the `.csproj`):

```xml
<PackageReference Include="WolverineFx.Http.Marten" />
```

`WolverineFx.Http.Marten` transitively includes `WolverineFx.Http`, `WolverineFx.Marten`, and
`Marten`. No additional Wolverine or Marten package references needed.

**Project references:**

```xml
<ProjectReference Include="..\..\src\CritterBids.Contracts\CritterBids.Contracts.csproj" />
```

`CritterBids.Listings` must **not** reference `CritterBids.Selling`, `CritterBids.Participants`,
or `CritterBids.Api`. Cross-BC coupling through project references is prohibited.

**Gate:** `dotnet build` → 0 errors, 0 warnings before proceeding to S7b.

---

## S7c: `AddListingsModule()`

**File:** `src/CritterBids.Listings/ListingsModule.cs`

Follow the exact shape of `SellingModule.cs`. Key rules from `adding-bc-module.md`:
- Use `services.ConfigureMarten()` — **not** `AddMarten()`, **not** `AddMartenStore<T>()`
- No `IConfiguration` parameter — the connection string is configured once in `Program.cs`
- No `IntegrateWithWolverine()` — configured once in `Program.cs`
- No `ApplyAllDatabaseChangesOnStartup()` — configured once in `Program.cs`

```csharp
namespace CritterBids.Listings;

public static class ListingsModule
{
    public static IServiceCollection AddListingsModule(this IServiceCollection services)
    {
        services.ConfigureMarten(opts =>
        {
            opts.Schema.For<CatalogListingView>().DatabaseSchemaName("listings");
        });

        return services;
    }
}
```

No BC-internal services are needed at M2. `AddListingsModule()` has a single responsibility:
registering `CatalogListingView` in the shared Marten store under the `listings` schema.

---

## S7d: `CatalogListingView`

**File:** `src/CritterBids.Listings/CatalogListingView.cs`

```csharp
namespace CritterBids.Listings;

public sealed record CatalogListingView
{
    public Guid Id { get; init; }                  // ListingId — Marten document identity
    public Guid SellerId { get; init; }
    public string Title { get; init; } = "";
    public string Format { get; init; } = "";      // "Flash" or "Timed" — string, not enum
    public decimal StartingBid { get; init; }
    public decimal? BuyItNow { get; init; }
    public TimeSpan? Duration { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
}
```

**Field rationale:**

- `Format`, `BuyItNow`, `Duration`: necessary for catalog display — Flash vs Timed differentiation,
  BIN pricing, and duration of the listing.
- `Format` is `string`: `ListingFormat` enum is internal to `CritterBids.Selling` and unavailable
  here. The contract's `string Format` field is the source.
- `ReservePrice`, `FeePercentage`, `ExtendedBidding*`: omitted — not needed for catalog browse.
  These fields remain in the `ListingPublished` contract for Settlement (M5) and Auctions (M3).

Marten resolves the document identity from the `Id` property (`Guid`) by convention. No custom
identity configuration needed.

---

## S7e: `ListingPublishedHandler`

**File:** `src/CritterBids.Listings/ListingPublishedHandler.cs`

```csharp
using CritterBids.Contracts.Selling;

namespace CritterBids.Listings;

public static class ListingPublishedHandler
{
    public static void Handle(
        ListingPublished message,
        IDocumentSession session)
    {
        session.Store(new CatalogListingView
        {
            Id          = message.ListingId,
            SellerId    = message.SellerId,
            Title       = message.Title,
            Format      = message.Format,
            StartingBid = message.StartingBid,
            BuyItNow    = message.BuyItNow,
            Duration    = message.Duration,
            PublishedAt = message.PublishedAt
        });
    }
}
```

**Rules:**

- **No `[MartenStore]` attribute** — single primary store, attribute is not needed or applicable.
  See `adding-bc-module.md` anti-patterns.
- **No `SaveChangesAsync()`** — `AutoApplyTransactions()` (configured in `Program.cs`) means
  Wolverine commits the session after `Handle` returns. An explicit commit is incorrect.
- **No `OutgoingMessages` or `IMessageBus`** — this handler produces no downstream messages in M2.
- Handler is `static void` — no async, no return value. Match the shape of `SellerRegistrationCompletedHandler`.

---

## S7f: Read endpoints

**File:** `src/CritterBids.Listings/Features/Catalog/CatalogEndpoints.cs`
(or match the file layout used by Selling BC endpoints — inspect before creating)

Both endpoints carry `[AllowAnonymous]` — the project-wide M2–M5 stance. No exceptions.

Consult `docs/skills/marten-querying.md` for the correct `IQuerySession` injection pattern and
LINQ/`LoadAsync` query shapes before writing these. The read patterns for Marten documents are
covered there.

### `GET /api/listings`

Returns all published listings ordered by `PublishedAt` descending. Returns an empty array when
no listings exist — do **not** return 404 on empty.

```csharp
[AllowAnonymous]
[WolverineGet("/api/listings")]
public static async Task<IReadOnlyList<CatalogListingView>> GetCatalog(
    IQuerySession session)
{
    return await session.Query<CatalogListingView>()
        .OrderByDescending(x => x.PublishedAt)
        .ToListAsync();
}
```

### `GET /api/listings/{id}`

Returns a single `CatalogListingView` by `ListingId`. Returns 404 when not found.

```csharp
[AllowAnonymous]
[WolverineGet("/api/listings/{id}")]
public static async Task<IResult> GetListingDetail(
    Guid id,
    IQuerySession session)
{
    var view = await session.LoadAsync<CatalogListingView>(id);
    return view is null
        ? Results.NotFound()
        : Results.Ok(view);
}
```

**Endpoint discovery:** Wolverine.HTTP discovers endpoints via assembly scanning.
The Listings assembly is added in S7g. `MapWolverineEndpoints()` in `Program.cs` handles discovery
automatically once the assembly is included — no per-BC mapping call is needed.

---

## S7g: `Program.cs` wiring

Three changes to `Program.cs`:

### 1. `AddListingsModule()` in service registration

Alongside `AddSellingModule()`:

```csharp
builder.Services.AddListingsModule();
```

Note: no `IConfiguration` argument — `AddListingsModule()` takes no parameters.

### 2. Assembly discovery in `UseWolverine()`

Alongside the `opts.Discovery.IncludeAssembly(...)` call for the Selling BC:

```csharp
opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly);
```

This ensures Wolverine discovers both `ListingPublishedHandler` and the read endpoints.

### 3. `ListenToRabbitQueue` in the RabbitMQ-guarded block

Inside the same `UseWolverine()` lambda block that contains the existing
`opts.PublishMessage<...>().ToRabbitQueue("listings-selling-events")` rule:

```csharp
opts.ListenToRabbitQueue("listings-selling-events");
```

**Placement note:** RabbitMQ routing rules (`PublishMessage`, `ListenToRabbitQueue`) belong in
`Program.cs`, not inside `AddListingsModule()`. `AddListingsModule()` receives `IServiceCollection`,
not `WolverineOptions`. This is the established pattern — do not move routing into the module.

---

## S7b + S7h: Test projects and `CatalogListingViewTests.cs`

### `CritterBids.Listings.Tests` project scaffold (S7b)

Create `tests/CritterBids.Listings.Tests/CritterBids.Listings.Tests.csproj` as an xUnit test
project targeting `net10.0`. Add to `CritterBids.sln`.

Inspect `tests/CritterBids.Selling.Tests/CritterBids.Selling.Tests.csproj` and apply the same
package references (xUnit, Shouldly, Testcontainers.PostgreSql, Alba, etc.).

**Project references:**

```xml
<ProjectReference Include="..\..\src\CritterBids.Api\CritterBids.Api.csproj" />
<ProjectReference Include="..\..\src\CritterBids.Listings\CritterBids.Listings.csproj" />
```

If a shared test utilities project exists, reference it here too.

**Gate:** `dotnet build` → 0 errors, 0 warnings before writing any test code.

### `ListingsTestFixture.cs`

**File:** `tests/CritterBids.Listings.Tests/Fixtures/ListingsTestFixture.cs`

Follow the `SellingTestFixture` shape from `adding-bc-module.md` exactly. Substitute:
- `AddSellingModule()` → `AddListingsModule()`
- Connection string container name: `"listings-postgres-test-{Guid.NewGuid():N}"`

Required elements (from `adding-bc-module.md` test fixture checklist):

- `services.AddMarten(...)` registered in `ConfigureServices` with Testcontainers connection string
- `services.AddListingsModule()` called in `ConfigureServices`
- `services.RunWolverineInSoloMode()` present
- `services.DisableAllExternalWolverineTransports()` present
- `CleanAllMartenDataAsync()` uses non-generic `Host.CleanAllMartenDataAsync()`
- `GetDocumentSession()` uses `Host.DocumentStore().LightweightSession()`

Also create `tests/CritterBids.Listings.Tests/Fixtures/ListingsTestCollection.cs` following
the same pattern as `SellingTestCollection.cs`.

### `CatalogListingViewTests.cs`

**File:** `tests/CritterBids.Listings.Tests/CatalogListingViewTests.cs`

4 integration tests. Uses `[Collection(ListingsTestCollection.Name)]` and `IAlbaHost` from
the fixture. Follow the exact xUnit collection fixture pattern from the Selling tests.

#### Scenario mapping

| Scenario | Test method | Setup | Assert |
|----------|-------------|-------|--------|
| 1.3 — Catalog browse — listings appear after publish | `GetCatalog_AfterListingPublished_ReturnsCatalogEntry` | Invoke handler → GET | Entry present, key fields match |
| 1.3 — Catalog browse — no listings yet | `GetCatalog_BeforePublish_ReturnsEmptyList` | None | Empty JSON array |
| 1.4 — Listing detail — published listing | `GetListingDetail_PublishedListing_ReturnsDetail` | Invoke handler → GET /{id} | 200, fields match |
| 1.4 — Listing detail — unknown ID | `GetListingDetail_UnknownId_Returns404` | None | 404 |

#### Handler invocation in test setup

For tests that require a `CatalogListingView` to exist, invoke the handler directly —
do not publish a message over the bus.

```csharp
// Arrange: build a representative ListingPublished contract
var listingId = Guid.CreateVersion7();
var message = new ListingPublished(
    ListingId: listingId,
    SellerId: Guid.CreateVersion7(),
    Title: "Mint Condition Foil Black Lotus",
    Format: "Timed",
    StartingBid: 50_000m,
    ReservePrice: 75_000m,
    BuyItNow: 150_000m,
    Duration: TimeSpan.FromDays(7),
    ExtendedBiddingEnabled: false,
    ExtendedBiddingTriggerWindow: null,
    ExtendedBiddingExtension: null,
    FeePercentage: 0.10m,
    PublishedAt: DateTimeOffset.UtcNow);

// Invoke handler with a real Marten session from the fixture
await using var session = _fixture.GetDocumentSession();
ListingPublishedHandler.Handle(message, session);
await session.SaveChangesAsync();   // explicit in test setup — not a handler concern
```

Note: `SaveChangesAsync()` is called explicitly in test setup because there is no Wolverine
pipeline wrapping the direct handler invocation. This is correct — `AutoApplyTransactions()`
applies only when Wolverine dispatches the handler, not in direct test invocations.

#### Assertions

Use Shouldly. For catalog and detail assertions, deserialize the response body and assert
specific fields rather than full-record equality. Example for catalog:

```csharp
// Act
var response = await _fixture.Host.GetAsJson<List<CatalogListingView>>("/api/listings");

// Assert
response.ShouldNotBeNull();
response.Count.ShouldBe(1);
response[0].Id.ShouldBe(listingId);
response[0].Title.ShouldBe("Mint Condition Foil Black Lotus");
response[0].Format.ShouldBe("Timed");
response[0].StartingBid.ShouldBe(50_000m);
```

For the 404 test:

```csharp
var response = await _fixture.Host.Scenario(s =>
{
    s.Get.Url($"/api/listings/{Guid.NewGuid()}");
    s.StatusCodeShouldBe(404);
});
```

Adjust Alba assertion patterns to match what the Selling test suite uses — inspect the existing
tests for the exact `Host.Scenario` / `GetAsJson` / `StatusCodeShouldBe` patterns before writing.

Each test that seeds data should call `await _fixture.CleanAllMartenDataAsync()` in a
`BeforeEach` / `IAsyncLifetime.InitializeAsync()` setup to ensure isolation.

---

## Not in scope

The following items are explicitly out of scope for this session. Do not implement or reference them:

- Any Auctions BC work — M3
- `SubmitListing` HTTP endpoint — no HTTP endpoint for `SubmitListing` exists in M2
- `[WriteAggregate]` stream-ID verification for `SubmitListing` — deferred to the session that adds the endpoint
- `ListingFormat` enum promotion to `CritterBids.Contracts.Selling` — deferred to S8
- Moving RabbitMQ routing rules into BC module extensions — deferred architectural refactor
- `ReviseListing`, `EndListingEarly`, `MarkAsRelisted` and their events — later milestones
- Paging or filtering on `GET /api/listings` — simple full list is M2-sufficient
- Named Polecat stores — still only one Polecat BC
- Any frontend work — M6
- Real authentication — M6; `[AllowAnonymous]` is the intentional M2–M5 stance
- S8 skills documentation (`domain-event-conventions.md`) — S8 scope

---

## Build and test gates

Run these in order. Do not proceed past a failing gate.

1. After S7a: `dotnet build` → 0 errors, 0 warnings
2. After S7b: `dotnet build` → 0 errors, 0 warnings
3. After S7c–S7g: `dotnet build` → 0 errors, 0 warnings
4. After S7h: `dotnet test` → **42 tests passing**, 0 failures (38 existing + 4 new)

---

## Atomic commit sequence

One commit per deliverable block. Commit message format: `feat: [one-line description]` for
production code, `test: [one-line description]` for test files.

1. `feat: scaffold CritterBids.Listings and CritterBids.Listings.Tests projects` (S7a + S7b)
2. `feat: add AddListingsModule with CatalogListingView in listings schema` (S7c + S7d)
3. `feat: add ListingPublishedHandler writing CatalogListingView document` (S7e)
4. `feat: add GET /api/listings and GET /api/listings/{id} endpoints` (S7f)
5. `feat: wire AddListingsModule, assembly discovery, and ListenToRabbitQueue in Program.cs` (S7g)
6. `test: add CatalogListingViewTests covering scenarios 1.3 and 1.4` (S7h)

---

## Session close checklist

Verify every item before marking the session complete.

**Project structure:**
- [ ] `CritterBids.Listings.csproj` exists and is added to `CritterBids.sln`
- [ ] `CritterBids.Listings.Tests.csproj` exists and is added to `CritterBids.sln`
- [ ] `CritterBids.Listings` does not reference any other BC project

**Module and Marten registration:**
- [ ] `AddListingsModule()` takes no parameters
- [ ] `AddListingsModule()` calls `services.ConfigureMarten()` — not `AddMarten()` or `AddMartenStore<T>()`
- [ ] `opts.Schema.For<CatalogListingView>().DatabaseSchemaName("listings")` present
- [ ] No `IntegrateWithWolverine()` or `ApplyAllDatabaseChangesOnStartup()` inside `AddListingsModule()`

**CatalogListingView:**
- [ ] `sealed record` with `Guid Id` as the Marten document identity
- [ ] `Format` is `string`, not an enum

**Handler:**
- [ ] `ListingPublishedHandler.Handle` has signature `(ListingPublished message, IDocumentSession session)`
- [ ] No `[MartenStore]` attribute on the handler
- [ ] No `SaveChangesAsync()` inside the handler
- [ ] No `IMessageBus` or `OutgoingMessages` in `ListingPublishedHandler`

**Endpoints:**
- [ ] `GET /api/listings` returns `IReadOnlyList<CatalogListingView>`, empty array when no results
- [ ] `GET /api/listings/{id}` returns 200 + view when found, 404 when not found
- [ ] Both endpoints carry `[AllowAnonymous]`

**Program.cs:**
- [ ] `builder.Services.AddListingsModule()` present (no arguments)
- [ ] `opts.Discovery.IncludeAssembly(typeof(CatalogListingView).Assembly)` present in `UseWolverine()`
- [ ] `opts.ListenToRabbitQueue("listings-selling-events")` present in the RabbitMQ-guarded block

**Tests:**
- [ ] `ListingsTestFixture.cs` registers `AddMarten()` + `AddListingsModule()` in `ConfigureServices`
- [ ] `services.RunWolverineInSoloMode()` present in fixture
- [ ] `services.DisableAllExternalWolverineTransports()` present in fixture
- [ ] `CatalogListingViewTests.cs` contains all 4 tests with correct method names
- [ ] Each test that seeds data calls `CleanAllMartenDataAsync()` for isolation

**Final gates:**
- [ ] `dotnet build` → 0 errors, 0 warnings
- [ ] `dotnet test` → 42 tests passing, 0 failures
- [ ] All 6 commits made atomically per the commit sequence above

---

## Required session close artifacts

1. **Retrospective:** author `docs/retrospectives/M2-S7-listings-bc-read-paths-retrospective.md`
   following the same structure as `M2-S6-slice-1-2-submit-listing-retrospective.md`. Required
   sections: Baseline, Items completed (table), per-deliverable notes (document any deviations
   from this prompt), test results table, build state at session close, key learnings, verification
   checklist, files changed, what remains for S8.

---

## What S8 will cover (for your awareness — not in scope here)

S8 is documentation-only. No code changes. It covers:
- `docs/skills/domain-event-conventions.md` — authored retrospectively from S5–S6 domain event patterns
- `docs/skills/adding-bc-module.md` — any updates from S7 learnings
- M2 milestone retrospective at `docs/retrospectives/M2-listings-pipeline-retrospective.md`

The implementation phase of M2 closes at the end of this session (S7).
