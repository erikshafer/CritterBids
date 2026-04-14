# M2-S2: Selling BC Scaffold

**Milestone:** M2 — Listings Pipeline
**Slice:** S2 — Selling BC scaffold
**Agent:** @PSA
**Estimated scope:** one PR, ~8 new files + 3 file modifications + package pins

## Goal

Stand up the `CritterBids.Selling` bounded context as an empty, correctly-wired shell. At session
close: the project exists, the named Marten store (`ISellingDocumentStore`) is registered, the
`SellerListing` aggregate skeleton is in place, `AddSellingModule()` is called from the API host,
the Wolverine host has `MessageStorageSchemaName` configured, and a smoke test confirms the full
host boots cleanly with the new module wired in.

No commands, no handlers, no HTTP endpoints, no RabbitMQ subscriptions, no projections — those
arrive in S3–S5. This session establishes the structural foundation that every subsequent Selling BC
session builds on, and it is the first test of the named-store pattern resolved in ADR 0002.

## Context to load

- `docs/milestones/M2-listings-pipeline.md` — authoritative M2 scope; §4 solution layout, §5
  infrastructure (note: §5 code example is superseded by ADR 0002 — see §8 M2-D1 updated
  disposition), §6 conventions (named stores, UUID v7, AutoApplyTransactions)
- `docs/decisions/0002-marten-bc-isolation.md` — **primary reference for this session.** Consequences
  section governs the module registration pattern, `[MartenStore]` attribute requirement,
  `MessageStorageSchemaName`, and test fixture override. Read in full before writing any code.
- `docs/retrospectives/M2-S1-marten-bc-isolation-adr.md` — API surface verified in S1; key findings
  table (named store Wolverine compatibility, `[MartenStore]` requirement, `MessageStorageSchemaName`
  purpose)
- `docs/retrospectives/M1-S4-participants-bc-scaffold.md` — prior BC scaffold session; structural
  analogue for this session (module extension, empty aggregate, test fixture, smoke test shape)
- `docs/skills/marten-event-sourcing.md` — Marten event stream patterns, `StoreOptions` shape,
  `AutoApplyTransactions` placement
- `docs/skills/critter-stack-testing-patterns.md` — test fixture pattern adapted for named Marten
  stores (note: the Polecat fixture uses `ConfigurePolecat()`; the Marten equivalent re-registers
  the named store in `ConfigureServices` per ADR 0002 Consequences)
- `docs/skills/csharp-coding-standards.md` — `sealed record`, nullability, naming
- `docs/prompts/README.md` — the ten rules this prompt obeys

Additionally, **verify current stable package versions via NuGet or Context7 before pinning.** Do
not silently use stale version assumptions.

## In scope

### `src/CritterBids.Selling/` — new project

**`CritterBids.Selling.csproj`**

New class library targeting .NET 10. References:
- Core Marten package (verify current stable — Marten 8.x family)
- Wolverine-Marten integration package (the package that provides `AddMartenStore<T>()` and
  `IntegrateWithWolverine()` for Marten named stores — verify exact package name via NuGet or
  Context7 before referencing)

No `<PackageReference Version="...">` — central version management via `Directory.Packages.props`.

**`ISellingDocumentStore.cs`**

Public marker interface inheriting from `IDocumentStore`. This is the DI key for the Selling BC's
named Marten store. All components that need a Selling BC session resolve this type, not
`IDocumentStore` directly. Place in the root namespace of the project.

**`SellerListing.cs`**

Empty aggregate class. Properties:
- `public Guid Id { get; set; }` — stream ID, populated by Marten from the event stream
- No `Apply()` methods yet — those arrive in S4 with `DraftListingCreated`
- No constructor — Marten instantiates via default constructor

Stream ID strategy: UUID v7 (`Guid.CreateVersion7()`) per ADR 0002 and M2 §6 conventions. No
namespace constant is needed — unlike Polecat BCs, Marten BC stream IDs are not derived from a
business key. Document this in a brief code comment on the class.

**`SellingModule.cs`**

`AddSellingModule()` extension method on `IServiceCollection`. Configuration steps, in order:

1. Resolve the PostgreSQL connection string from `IConfiguration["ConnectionStrings:critterbids-postgres"]`.
   Throw `InvalidOperationException` if absent — fail fast, same pattern as Participants BC.
2. Call `AddMartenStore<ISellingDocumentStore>()` with a `StoreOptions` lambda that sets:
   - `opts.Connection(connectionString)`
   - `opts.DatabaseSchemaName = "selling"`
   - `opts.Policies.AutoApplyTransactions()` — required in every BC per M2 §6
   - Register `SellerListing` with the event stream: `opts.Events.AddEventType<SellerListing>()` is
     not correct — register the aggregate stream identity with
     `opts.Schema.For<SellerListing>()` if document storage is needed, or leave stream registration
     minimal until S4 adds actual event types. Confirm the correct minimal registration against
     `marten-event-sourcing.md` before writing.
3. Chain `.ApplyAllDatabaseChangesOnStartup()` on the builder returned by `AddMartenStore<T>()`.
4. Chain `.IntegrateWithWolverine()` on the same builder.

No RabbitMQ `opts.ListenToRabbitQueue()` or `opts.PublishMessage<T>()` calls — those arrive in S3
when the `RegisteredSellers` consumer is wired.

No `services.AddSingleton<ISellerRegistrationService, SellerRegistrationService>()` — that arrives
in S3 alongside the service's implementation.

### `src/CritterBids.Api/Program.cs` — two changes

**`AddSellingModule()` call.** Add `services.AddSellingModule(builder.Configuration)` alongside
`services.AddParticipantsModule(builder.Configuration)`.

**`MessageStorageSchemaName` in Wolverine host configuration.** The host's `UseWolverine()` call
(or equivalent) in `Program.cs` must set `opts.Durability.MessageStorageSchemaName = "wolverine"`
so all named Marten stores write envelope rows to a shared PostgreSQL schema rather than each
creating their own envelope tables. This setting belongs in the host-level Wolverine configuration,
not inside any BC module. If the existing Wolverine host configuration does not expose a
`Durability.MessageStorageSchemaName` option, verify the correct API path against ADR 0002 and the
Wolverine documentation; flag in the retro if the API shape differs from what the ADR describes.

### `CritterBids.sln` — add new projects

Add both new projects to the solution file:
- `src/CritterBids.Selling/CritterBids.Selling.csproj`
- `tests/CritterBids.Selling.Tests/CritterBids.Selling.Tests.csproj`

### `Directory.Packages.props` — new package pins

Pin the packages required by this session. Verify current stable versions at session time.
Packages to add:
- Core Marten package (Marten 8.x)
- Wolverine-Marten integration package
- `Testcontainers.PostgreSql` — PostgreSQL container for test fixtures

Do not add `Version=` to any `<PackageReference>` anywhere in the solution.

### `tests/CritterBids.Selling.Tests/` — new project

**`CritterBids.Selling.Tests.csproj`**

Test project. References:
- `<ProjectReference>` to `src/CritterBids.Selling/`
- `<ProjectReference>` to `src/CritterBids.Api/` — required for `AlbaHost.For<Program>`
- `Alba` (already pinned from M1-S5)
- `Testcontainers.PostgreSql`
- xUnit and Shouldly (already pinned)

**`Fixtures/SellingTestFixture.cs`**

Follows the same structural shape as `ParticipantsTestFixture.cs` adapted for a named Marten store:
- Starts a PostgreSQL Testcontainers container (`PostgreSqlBuilder`, verify constructor pattern —
  use same style as `MsSqlBuilder` in M1 Participants fixture)
- Builds `AlbaHost.For<Program>` with a `ConfigureServices` override that calls
  `AddMartenStore<ISellingDocumentStore>()` with `opts.Connection(pgContainer.ConnectionString)` and
  `opts.DatabaseSchemaName = "selling"` — this re-registers the named store, replacing the
  production connection string with the Testcontainers-issued one. Per ADR 0002 Consequences:
  overriding a named store re-registers it; the production registration is replaced.
- Calls `DisableAllExternalWolverineTransports()` in the `UseWolverine` configuration to suppress
  RabbitMQ during tests.
- Exposes `Host` and any helper methods used by tests in this project (minimal in S2 — a
  `CleanAllMartenDataAsync()` or equivalent for future test isolation, if Marten provides one).

Note: confirm the correct Marten extension method for test data cleanup (equivalent to Polecat's
`CleanAllPolecatDataAsync()`). Check `marten-event-sourcing.md` or Context7 for the Marten API.
Document the finding in the retrospective regardless of whether a direct equivalent exists.

**`Fixtures/SellingTestCollection.cs`**

`[CollectionDefinition]` + `ICollectionFixture<SellingTestFixture>` — same pattern as
`ParticipantsTestCollection.cs`. Enables sequential test execution sharing one fixture instance.

**`SellingModuleTests.cs`** — smoke test

One test method: `SellingModule_BootsClean`. Verifies that:
1. The test host starts without throwing (AlbaHost construction succeeds)
2. `ISellingDocumentStore` is resolvable from the DI container (`Host.Services.GetRequiredService<ISellingDocumentStore>()` does not throw)

No HTTP calls, no event stream operations. Boot and DI resolution only.

### `docs/milestones/M2-listings-pipeline.md` — doc fix

Update §9 S2 row from the prompt filename placeholder to
`docs/prompts/M2-S2-selling-bc-scaffold.md` (this file), if it is not already correct.

## Explicitly out of scope

- **`RegisteredSellers` projection, `ISellerRegistrationService`** — S3.
- **RabbitMQ wiring** (`ListenToRabbitQueue`, `PublishMessage`) — S3 and S5.
- **`SellerListing` `Apply()` methods, `DraftListingCreated` event** — S4.
- **`ListingValidator`** — S4.
- **HTTP endpoints** (`POST /api/listings/draft`) — S4.
- **`CritterBids.Contracts.Selling.ListingPublished`** — S5.
- **Listings BC** — S6.
- **`AddListingsModule()`** — S6.
- **`ISellerRegistrationService` registration** — S3.
- **Any Settlement, Obligations, Relay, or Operations BC work.**
- **Frontend.**
- **No CI workflow changes.**
- **No changes to `CritterBids.Participants` or its test project** — Participants BC is unchanged
  in S2.
- **No changes to `CritterBids.Contracts`** — no new integration events in S2.

## Conventions to pin or follow

- **Named store registration via `AddMartenStore<ISellingDocumentStore>()`** — the ADR corrects the
  §5 working assumption. Do not call `AddMarten()` from any BC module. See ADR 0002 Decision and
  Consequences sections.
- **`[MartenStore(typeof(ISellingDocumentStore))]` on handlers** — S2 has no handlers, so no
  attribute placements are needed today. However, establish this requirement in a code comment
  inside `SellingModule.cs` (e.g., a comment on the `AddMartenStore<T>()` call noting that all
  Wolverine handlers in this BC must carry the attribute). This convention is established now so
  that S3+ agents see it at the call site.
- **`opts.Policies.AutoApplyTransactions()`** — required in every BC's store configuration per M2
  §6 (BC-engine-agnostic convention).
- **UUID v7 stream IDs** — `Guid.CreateVersion7()` at creation time; no namespace constant. Add a
  brief comment to `SellerListing.cs` noting this per ADR 0002.
- **`MessageStorageSchemaName = "wolverine"`** — set once in host-level Wolverine configuration;
  not in any BC module.
- **`[AllowAnonymous]` everywhere through M5** — no endpoints in S2, so no action needed. No
  `[Authorize]` or `[AllowAnonymous]` attributes introduced in this session.
- **`sealed record` for all records** — no records in S2; no action needed.
- **No `Version=` on any `<PackageReference>` anywhere in the solution.**

## Acceptance criteria

- [ ] `src/CritterBids.Selling/CritterBids.Selling.csproj` exists and references the core Marten
      package and Wolverine-Marten integration package.
- [ ] `ISellingDocumentStore` interface exists, is public, and inherits from `IDocumentStore`.
- [ ] `SellerListing` class exists with `public Guid Id { get; set; }` and a code comment noting
      UUID v7 stream ID strategy.
- [ ] `AddSellingModule()` extension method exists on `IServiceCollection`, calls
      `AddMartenStore<ISellingDocumentStore>()` with `DatabaseSchemaName = "selling"` and
      `opts.Policies.AutoApplyTransactions()`, and chains `.ApplyAllDatabaseChangesOnStartup()` and
      `.IntegrateWithWolverine()`.
- [ ] `AddSellingModule()` throws `InvalidOperationException` if the PostgreSQL connection string
      is absent from `IConfiguration`.
- [ ] `AddSellingModule()` does **not** call `AddMarten()`.
- [ ] `SellingModule.cs` contains a comment noting that all Wolverine handlers in this BC require
      `[MartenStore(typeof(ISellingDocumentStore))]`.
- [ ] `CritterBids.Api/Program.cs` calls `services.AddSellingModule(builder.Configuration)`.
- [ ] `CritterBids.Api/Program.cs` sets `opts.Durability.MessageStorageSchemaName = "wolverine"`
      (or equivalent verified API path) in the host Wolverine configuration.
- [ ] Both new projects are added to `CritterBids.sln`.
- [ ] `Directory.Packages.props` contains pins for the core Marten package, the Wolverine-Marten
      integration package, and `Testcontainers.PostgreSql`. No `Version=` on any `<PackageReference>`.
- [ ] `SellingTestFixture` exists, starts a PostgreSQL Testcontainers container, bootstraps
      `AlbaHost.For<Program>` with a `ConfigureServices` override that re-registers
      `AddMartenStore<ISellingDocumentStore>()` with the Testcontainers connection string, and calls
      `DisableAllExternalWolverineTransports()`.
- [ ] `SellingTestCollection` defines the xUnit collection fixture.
- [ ] `SellingModule_BootsClean` test passes: host starts, `ISellingDocumentStore` is resolvable
      from DI.
- [ ] `dotnet test` reports 9 passing tests, zero failing (8 existing + 1 new smoke test).
- [ ] `dotnet build` succeeds with zero errors and zero warnings across all projects.
- [ ] No files created or modified outside: `src/CritterBids.Selling/`, `src/CritterBids.Api/Program.cs`,
      `tests/CritterBids.Selling.Tests/`, `CritterBids.sln`, `Directory.Packages.props`,
      `docs/milestones/M2-listings-pipeline.md`, and this session's retrospective.
- [ ] No `RegisteredSellers`, `ISellerRegistrationService`, `DraftListingCreated`, HTTP endpoints,
      or RabbitMQ wiring introduced.

## Open questions

- **Exact Wolverine-Marten integration package name.** The ADR and skills doc refer to the
  integration capability but do not specify the NuGet package name. Likely `WolverineFx.Marten` or
  a sub-package — verify via NuGet before referencing. If the package that provides
  `AddMartenStore<T>()` with `.IntegrateWithWolverine()` differs from what `marten-event-sourcing.md`
  implies, document the correct name in the retrospective so future sessions and skill files can be
  updated.

- **Minimal `SellerListing` stream registration in `StoreOptions`.** Marten requires event types
  to be registered before they can be appended to a stream. In S2 there are no event types yet.
  Confirm whether any stream-identity or aggregate registration is needed in `StoreOptions` at
  scaffold time, or whether the stream simply does not need configuration until S4 introduces
  `DraftListingCreated`. Document the approach in the retrospective.

- **Marten data-cleanup API for test fixtures.** Polecat provides `CleanAllPolecatDataAsync()` and
  `ResetAllPolecatDataAsync()` as extension methods on `IServiceProvider`. Verify whether Marten
  provides equivalent helpers (e.g. `IDocumentStore.Advanced.Clean.DeleteAllDocumentsAsync()` or
  similar) for use in future Selling BC integration tests. The S2 fixture should expose the cleanup
  method even if no test calls it yet. Document the API found in the retrospective.

- **`MessageStorageSchemaName` exact API path.** ADR 0002 describes this as
  `opts.Durability.MessageStorageSchemaName`. Verify this path exists in the actual Wolverine 5.x
  `WolverineOptions` object. If the property lives elsewhere (e.g. `opts.Node`, `opts.Storage`, or
  requires a different builder chain), use the correct path and flag the discrepancy in the
  retrospective so the ADR can be updated.

- **`PostgreSqlBuilder` constructor pattern.** Testcontainers 4.x changed `MsSqlBuilder` to use a
  constructor-with-image-tag pattern (M1-S7 finding, key learning #3 in that retro). Verify whether
  `PostgreSqlBuilder` follows the same pattern or uses a different API shape. Use whatever the
  current Testcontainers.PostgreSql API requires; document in the retrospective.

- **If any root configuration file conflicts with this prompt's scope, flag and stop before
  editing.** Carried forward from prior sessions.
