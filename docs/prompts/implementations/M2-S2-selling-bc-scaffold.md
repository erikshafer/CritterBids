# M2-S2: Selling BC Scaffold

**Milestone:** M2 — Listings Pipeline
**Slice:** S2 — Selling BC scaffold
**Agent:** @PSA
**Estimated scope:** one PR, ~8 new files + 3 file modifications + package pins

## Goal

Stand up the `CritterBids.Selling` bounded context as an empty, correctly-wired shell. At session
close: the project exists, the named Marten store (`ISellingDocumentStore`) is registered, the
`SellerListing` aggregate skeleton is in place, `AddSellingModule()` is called from the API host,
the Wolverine host has `MultipleHandlerBehavior.Separated`, `MessageIdentity.IdAndDestination`, and
`MessageStorageSchemaName` configured, and a smoke test confirms the full host boots cleanly with
the new module wired in.

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
- `WolverineFx.Marten` — the Wolverine-Marten integration package. Provides `AddMartenStore<T>()`,
  `.IntegrateWithWolverine()`, `[MartenStore]`, and transitively brings in `Marten` itself. For S2
  (no HTTP endpoints), this single package is sufficient. S4 will upgrade to `WolverineFx.Http.Marten`
  when the first HTTP endpoint is added — that package transitively includes `WolverineFx.Http` and
  `WolverineFx.Marten`. **All `WolverineFx.*` packages in the solution must use the same version.**

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
   - Minimal stream/document registration: confirm the correct registration against
     `marten-event-sourcing.md` before writing. In S2 there are no event types yet; leave stream
     registration minimal or absent until S4 adds `DraftListingCreated`.
3. Chain `.UseLightweightSessions()` on the builder returned by `AddMartenStore<T>()`. This
   disables identity map overhead and is the recommended session type for all Marten BC modules.
4. Chain `.ApplyAllDatabaseChangesOnStartup()` on the same builder.
5. Chain `.IntegrateWithWolverine()` on the same builder.

The complete chain: `AddMartenStore<ISellingDocumentStore>(...).UseLightweightSessions().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`.

No RabbitMQ `opts.ListenToRabbitQueue()` or `opts.PublishMessage<T>()` calls — those arrive in S3
when the `RegisteredSellers` consumer is wired.

No `services.AddSingleton<ISellerRegistrationService, SellerRegistrationService>()` — that arrives
in S3 alongside the service's implementation.

### `src/CritterBids.Api/Program.cs` — four changes

**`AddSellingModule()` call.** Add `services.AddSellingModule(builder.Configuration)` alongside
`services.AddParticipantsModule(builder.Configuration)`.

**Wolverine modular monolith settings.** The host's `UseWolverine()` block must include three
settings that belong at the host level, not in any BC module:

```
opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
opts.Durability.MessageStorageSchemaName = "wolverine";
```

- `Separated` — each BC handler for the same message type gets its own dedicated queue with its own
  transaction and retry policy. In the default combined mode, multiple BC handlers for `ListingPublished`
  (Listings, Settlement, Auctions) would run in one combined handler, breaking BC isolation.
- `MessageIdentity.IdAndDestination` — prevents the durable inbox from deduplicating messages that
  arrive on different queues but share the same message ID (fanout deduplication bug without this).
- `MessageStorageSchemaName` — routes all named Marten stores' envelope rows to a shared schema
  instead of each store creating its own envelope tables.

If any of these properties do not exist at the paths shown, verify the correct API via Context7 and
flag the discrepancy in the retrospective.

**`RunJasperFxCommands`.** Check whether `Program.cs` currently ends with `app.Run()` or
`return await app.RunJasperFxCommands(args)`. The canonical Wolverine bootstrap requires
`RunJasperFxCommands` — it enables the `db-apply`, `db-assert`, `db-dump`, and `codegen` CLI
commands. If `app.Run()` is present, replace it with `return await app.RunJasperFxCommands(args)`.

**`public partial class Program`.** Check whether `Program.cs` ends with
`public partial class Program { }`. This declaration is required for test projects to reference
`Program` via `AlbaHost.For<Program>()`. If absent, add it.

### `CritterBids.sln` — add new projects

Add both new projects to the solution file:
- `src/CritterBids.Selling/CritterBids.Selling.csproj`
- `tests/CritterBids.Selling.Tests/CritterBids.Selling.Tests.csproj`

### `Directory.Packages.props` — new package pins

Pin the packages required by this session. Verify current stable versions at session time.
Packages to add (if not already present):
- `WolverineFx.Marten` — same version family as all other `WolverineFx.*` packages in the solution
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
- Starts a PostgreSQL Testcontainers container (`PostgreSqlBuilder` — verify constructor pattern;
  Testcontainers 4.x uses `new PostgreSqlBuilder("image:tag")` constructor form matching the
  `MsSqlBuilder` pattern from M1-S7 finding #3)
- Builds `AlbaHost.For<Program>` with a `ConfigureServices` override that:
  - Calls `AddMartenStore<ISellingDocumentStore>()` with `opts.Connection(pgContainer.ConnectionString)`
    and `opts.DatabaseSchemaName = "selling"` — re-registers the named store, replacing the production
    connection string with the Testcontainers-issued one
  - Calls `services.RunWolverineInSoloMode()` — prevents advisory lock contention during test
    restarts (required alongside `DisableAllExternalWolverineTransports()`)
  - Calls `services.DisableAllExternalWolverineTransports()` — suppresses RabbitMQ during tests
- Exposes `Host` for test access
- Exposes `CleanAllMartenDataAsync()` as an async helper for future test isolation. The canonical
  Marten cleanup API is `await _host.CleanAllMartenDataAsync()` — an extension method on `IAlbaHost`
  from the `Marten` namespace. This deletes all documents and event streams. A second method
  `ResetAllMartenDataAsync()` exists for async projection tests (disables/clears/restarts
  projections) — expose it too for completeness, even if no test calls it in S2.

**`Fixtures/SellingTestCollection.cs`**

`[CollectionDefinition]` + `ICollectionFixture<SellingTestFixture>` — same pattern as
`ParticipantsTestCollection.cs`. Enables sequential test execution sharing one fixture instance.

**`SellingModuleTests.cs`** — smoke test

One test method: `SellingModule_BootsClean`. Verifies that:
1. The test host starts without throwing (AlbaHost construction succeeds)
2. `ISellingDocumentStore` is resolvable from the DI container
   (`Host.Services.GetRequiredService<ISellingDocumentStore>()` does not throw)

No HTTP calls, no event stream operations. Boot and DI resolution only.

### `docs/milestones/M2-listings-pipeline.md` — doc fix

Update §9 S2 row from the prompt filename placeholder to
`docs/prompts/implementations/M2-S2-selling-bc-scaffold.md` (this file), if it is not already correct.

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
  §5 working assumption. Do not call `AddMarten()` from any BC module. See ADR 0002.
- **`.UseLightweightSessions()`** — chain on `AddMartenStore<T>()` in every Marten BC module.
  Disables identity map overhead. Required for the canonical Marten module pattern.
- **`[MartenStore(typeof(ISellingDocumentStore))]` on handlers** — S2 has no handlers, so no
  attribute placements are needed today. Establish this requirement in a code comment inside
  `SellingModule.cs` so that S3+ agents see it at the call site.
- **`opts.Policies.AutoApplyTransactions()`** — required in every BC's store configuration per M2 §6.
- **UUID v7 stream IDs** — `Guid.CreateVersion7()` at creation time; no namespace constant. Add a
  brief comment to `SellerListing.cs` noting this per ADR 0002.
- **`MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination`** — host-level Wolverine
  settings; not in any BC module. Both must be set in the same `UseWolverine()` block as
  `MessageStorageSchemaName`.
- **`RunJasperFxCommands`** — all Wolverine projects use `return await app.RunJasperFxCommands(args)`
  instead of `app.Run()`.
- **`RunWolverineInSoloMode()` + `DisableAllExternalWolverineTransports()`** — both required in every
  Marten BC test fixture `ConfigureServices` override.
- **No `Version=` on any `<PackageReference>` anywhere in the solution.**

## Acceptance criteria

- [ ] `src/CritterBids.Selling/CritterBids.Selling.csproj` exists and references `WolverineFx.Marten`.
- [ ] `ISellingDocumentStore` interface exists, is public, and inherits from `IDocumentStore`.
- [ ] `SellerListing` class exists with `public Guid Id { get; set; }` and a code comment noting
      UUID v7 stream ID strategy.
- [ ] `AddSellingModule()` extension method exists on `IServiceCollection`, calls
      `AddMartenStore<ISellingDocumentStore>()` with `DatabaseSchemaName = "selling"` and
      `opts.Policies.AutoApplyTransactions()`, and chains `.UseLightweightSessions()`,
      `.ApplyAllDatabaseChangesOnStartup()`, and `.IntegrateWithWolverine()`.
- [ ] `AddSellingModule()` throws `InvalidOperationException` if the PostgreSQL connection string
      is absent from `IConfiguration`.
- [ ] `AddSellingModule()` does **not** call `AddMarten()`.
- [ ] `SellingModule.cs` contains a comment noting that all Wolverine handlers in this BC require
      `[MartenStore(typeof(ISellingDocumentStore))]`.
- [ ] `CritterBids.Api/Program.cs` calls `services.AddSellingModule(builder.Configuration)`.
- [ ] `CritterBids.Api/Program.cs` `UseWolverine()` block includes `MultipleHandlerBehavior.Separated`,
      `MessageIdentity.IdAndDestination`, and `MessageStorageSchemaName = "wolverine"`.
- [ ] `CritterBids.Api/Program.cs` ends with `return await app.RunJasperFxCommands(args)`, not
      `app.Run()`.
- [ ] `CritterBids.Api/Program.cs` contains `public partial class Program { }`.
- [ ] Both new projects are added to `CritterBids.sln`.
- [ ] `Directory.Packages.props` contains a pin for `WolverineFx.Marten` and `Testcontainers.PostgreSql`.
      No `Version=` on any `<PackageReference>`.
- [ ] `SellingTestFixture` exists, starts a PostgreSQL Testcontainers container, bootstraps
      `AlbaHost.For<Program>` with a `ConfigureServices` override that re-registers
      `AddMartenStore<ISellingDocumentStore>()` with the Testcontainers connection string, calls
      `services.RunWolverineInSoloMode()`, and calls `DisableAllExternalWolverineTransports()`.
- [ ] `SellingTestFixture` exposes `CleanAllMartenDataAsync()` and `ResetAllMartenDataAsync()` helpers.
- [ ] `SellingTestCollection` defines the xUnit collection fixture.
- [ ] `SellingModule_BootsClean` test passes: host starts, `ISellingDocumentStore` is resolvable from DI.
- [ ] `dotnet test` reports 9 passing tests, zero failing (8 existing + 1 new smoke test).
- [ ] `dotnet build` succeeds with zero errors and zero warnings across all projects.
- [ ] No files created or modified outside: `src/CritterBids.Selling/`, `src/CritterBids.Api/Program.cs`,
      `tests/CritterBids.Selling.Tests/`, `CritterBids.sln`, `Directory.Packages.props`,
      `docs/milestones/M2-listings-pipeline.md`, and this session's retrospective.
- [ ] No `RegisteredSellers`, `ISellerRegistrationService`, `DraftListingCreated`, HTTP endpoints,
      or RabbitMQ wiring introduced.

## Open questions

- **Minimal `SellerListing` stream registration in `StoreOptions`.** In S2 there are no event types
  yet. Confirm whether any stream-identity or aggregate registration is needed in `StoreOptions` at
  scaffold time, or whether the stream simply does not need configuration until S4 introduces
  `DraftListingCreated`. Document the approach in the retrospective.

- **`MultipleHandlerBehavior`, `MessageIdentity`, and `MessageStorageSchemaName` exact API paths.**
  The prompt shows the expected paths based on Wolverine 5.x documentation. If any property does not
  exist at the path shown, verify the correct path via Context7 and document the actual API shape in
  the retrospective so the gap analysis document and skill files can be updated at M2-S7.

- **`PostgreSqlBuilder` constructor pattern.** Testcontainers 4.x changed `MsSqlBuilder` to use a
  constructor-with-image-tag pattern (M1-S7 finding). Verify whether `PostgreSqlBuilder` follows the
  same pattern or uses a different API shape. Use whatever the current API requires; document in the
  retrospective.

- **`RunJasperFxCommands` and `public partial class Program` — already present?** These are likely
  already in `Program.cs` if they were established during M1. Check before modifying; if already
  present, note that in the retrospective rather than re-adding.

- **If any root configuration file conflicts with this prompt's scope, flag and stop before
  editing.** Carried forward from prior sessions.
