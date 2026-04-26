# M2-S2: Selling BC Scaffold — Retrospective

**Date:** 2026-04-14
**Milestone:** M2 — Listings Pipeline
**Slice:** S2 — Selling BC scaffold
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M2-S2-selling-bc-scaffold.md`

## Baseline

- `dotnet build`: 0 errors, 0 warnings across 8 projects (no Selling BC yet).
- `dotnet test`: 8 tests passing — Participants 6, Api.Tests 1, Contracts.Tests 1.
- `src/CritterBids.Selling/` did not exist.
- `tests/CritterBids.Selling.Tests/` did not exist.
- `CritterBids.Api/Program.cs` ended with `app.Run()` (no `RunJasperFxCommands`). `public partial class Program { }` was absent.
- No `WolverineFx.Marten` or `Testcontainers.PostgreSql` pin in `Directory.Packages.props`.

## Items completed

| Item | Description |
|------|-------------|
| S2a | `CritterBids.Selling` project — csproj, `ISellingDocumentStore`, `SellerListing`, `SellingModule` |
| S2b | `Program.cs` — four changes: `AddSellingModule()`, three Wolverine modular monolith settings, `RunJasperFxCommands`, `public partial class Program` |
| S2c | `Directory.Packages.props` — `WolverineFx.Marten` and `Testcontainers.PostgreSql` pins |
| S2d | `CritterBids.slnx` — both new projects added |
| S2e | `CritterBids.Selling.Tests` project — fixture, collection, smoke test |
| S2f | Open questions answered: `SellerListing` stream registration (absent), API paths verified, `PostgreSqlBuilder` constructor pattern confirmed |

## S2a: Selling BC project

### `ISellingDocumentStore` and `SellerListing`

No API surprises. `ISellingDocumentStore : IDocumentStore` is the DI key; all components that need a Selling session resolve this type, not `IDocumentStore` directly. `IDocumentStore` is not registered in the process (no `AddMarten()` call anywhere) — resolving it directly fails at startup by design.

`SellerListing` carries `public Guid Id { get; set; }` and a code comment. Open question resolved: no stream registration is needed in `StoreOptions` for S2. Marten discovers event types at append time; the `SellerListing` aggregate needs no explicit registration until `DraftListingCreated` is appended in S4.

### `SellingModule.cs` — two deviations from acceptance criteria

**Deviation 1: `opts.Policies.AutoApplyTransactions()` is a Wolverine API, not Marten.**

The session prompt and acceptance criterion both state `opts.Policies.AutoApplyTransactions()` should appear inside the `AddMartenStore<T>()` lambda. It is not available there. The signature mismatch:

- `StoreOptions.Policies` is a Marten type (`PoliciesExpression`) with document and event configuration — no `AutoApplyTransactions()` method.
- `AutoApplyTransactions()` is an extension on `WolverineOptions.Policies` — the Wolverine host-level policy collection.

The global `opts.Policies.AutoApplyTransactions()` already present in Program.cs `UseWolverine()` applies to all BC handlers including Marten-backed ones. Adding it inside the Marten lambda would have produced a compiler error. The acceptance criterion is factually incorrect about API placement; the correct placement was already in the host.

**Deviation 2: Early-return instead of throw when connection string absent.**

The prompt requires `InvalidOperationException` when the postgres connection string is absent. The Participants test fixture (`ParticipantsTestFixture`) calls `AlbaHost.For<Program>` — which runs all of `Program.cs` including `AddSellingModule()` — without provisioning PostgreSQL. A throw would fail all 6 Participants tests.

Because the Participants test project is explicitly out of scope (prompt §Explicitly out of scope), modifying `ParticipantsTestFixture` was not an option. The resolution was an early-return (no-op) that silently skips Selling BC registration when the connection string is absent. The `SellingTestFixture` injects the connection string via `ConfigureAppConfiguration` before Program.cs reads it, ensuring full registration in Selling-specific tests.

**Why early-return is safe in production.** Aspire always injects `ConnectionStrings:critterbids-postgres` via `WithReference()`. The early-return branch is never taken outside of test fixtures that deliberately omit the string. This is documented inside `SellingModule.cs`.

**`AddMartenStore<T>()` overload resolution — explicit type annotation required.**

`WolverineFx.Marten` exposes a second overload of `AddMartenStore<T>()` taking `Func<IServiceProvider, StoreOptions>` in addition to Marten's `Action<StoreOptions>`. Without an explicit lambda parameter type, the compiler selects the Wolverine overload:

```
// Fails: compiler selects Func<IServiceProvider, StoreOptions>
services.AddMartenStore<ISellingDocumentStore>(opts =>
{
    opts.Connection(...);   // IServiceProvider has no Connection() method
});
```

Fix — explicit annotation resolves the correct overload:

```csharp
services.AddMartenStore<ISellingDocumentStore>((StoreOptions opts) =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "selling";
})
```

In the test fixture, where both `using Marten;` and `using Polecat;` are in scope, `StoreOptions` is ambiguous. The fully-qualified form `(Marten.StoreOptions opts) =>` is required there.

**Resulting `AddSellingModule` shape:**

```csharp
public static IServiceCollection AddSellingModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var connectionString = configuration["ConnectionStrings:critterbids-postgres"];
    if (string.IsNullOrEmpty(connectionString))
        return services;   // no-op — see comment in file

    services.AddMartenStore<ISellingDocumentStore>((StoreOptions opts) =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "selling";
        // ⚠️ All handlers must carry [MartenStore(typeof(ISellingDocumentStore))]
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();

    return services;
}
```

**Structural metrics:**

| Metric | Value |
|--------|-------|
| `AddMarten()` calls | 0 |
| `AddMartenStore<T>()` calls | 1 |
| `opts.Policies.AutoApplyTransactions()` in Marten lambda | 0 |
| `[MartenStore]` attribute placements | 0 (no handlers yet) |
| `IDocumentStore` registrations | 0 (by design) |

## S2b: Program.cs — four changes

### Wolverine modular monolith settings — all confirmed at documented API paths

All three properties exist at the paths the prompt specified:

```csharp
opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
opts.Durability.MessageStorageSchemaName = "wolverine";
```

No path corrections required. These resolve the open question in the prompt.

### `RunJasperFxCommands` — namespace is `JasperFx`, not `JasperFx.CommandLine`

`RunJasperFxCommands` is an extension on `WebApplication` in the root `JasperFx` namespace. `JasperFx.CommandLine` contains `JasperFxEnvironment` (used in test fixtures) but not `RunJasperFxCommands`. Using `using JasperFx.CommandLine;` alone produces a compiler error; the correct using is `using JasperFx;`.

`public partial class Program { }` was absent at session start and was added. No other change required.

## S2e: Selling test fixture — two-container requirement

The prompt specifies one PostgreSQL container. At runtime, the fixture discovered a second infrastructure requirement:

```
DocumentStoreException: A connection string must be configured. Set StoreOptions.ConnectionString.
```

Root cause: `AlbaHost.For<Program>` runs all of Program.cs. `AddParticipantsModule()` registers Polecat, which requires a SQL Server connection string. The `Program.cs` guard (`?? string.Empty`) passes an empty string to Polecat; Polecat then throws during `DocumentStore` initialization because an empty string is not a valid connection string.

Resolution: the Selling fixture provisions both containers:

- **PostgreSQL** (`postgres:17-alpine`) — Marten named store for the Selling BC
- **SQL Server** (`mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04`) — Polecat for the Participants BC and Wolverine inbox/outbox

Both are started in parallel:

```csharp
await Task.WhenAll(_postgres.StartAsync(), _sqlServer.StartAsync());
```

`ConfigureServices` overrides both:

```csharp
services.ConfigurePolecat(opts => { opts.ConnectionString = sqlServerConnectionString; });

services.AddMartenStore<ISellingDocumentStore>((Marten.StoreOptions opts) =>
{
    opts.Connection(postgresConnectionString);
    opts.DatabaseSchemaName = "selling";
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();

services.RunWolverineInSoloMode();
services.DisableAllExternalWolverineTransports();
```

**`ConfigureAppConfiguration` timing — required for `AddSellingModule` to register.**

`ConfigureAppConfiguration` runs before Program.cs reads `IConfiguration`. Because `AddSellingModule()` uses an early-return when the connection string is absent, the Testcontainers postgres connection string must be injected via `ConfigureAppConfiguration` — not `ConfigureServices` — so it is visible when Program.cs calls `AddSellingModule()`. `ConfigureServices` runs after Program.cs and is too late.

```csharp
builder.ConfigureAppConfiguration((_, config) =>
{
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:critterbids-postgres"] = postgresConnectionString
    });
});
```

The `ConfigureServices` override then re-registers `ISellingDocumentStore` with the Testcontainers connection, replacing the production registration established by Program.cs.

### `ConfigureAppConfiguration` delegate signature

The correct delegate is `Action<WebHostBuilderContext, IConfigurationBuilder>`. A single-parameter form fails:

```
Error CS1593: Delegate 'Action<WebHostBuilderContext, IConfigurationBuilder>' does not take 1 arguments
```

Fix: `(_, config) =>` (discard the `WebHostBuilderContext`).

## API surface explored

| Question | Finding |
|---|---|
| `opts.Policies.AutoApplyTransactions()` inside Marten `StoreOptions` lambda? | Not available. `StoreOptions.Policies` is `PoliciesExpression` — no `AutoApplyTransactions()`. It is a `WolverineOptions.Policies` extension; the global host-level call in `UseWolverine()` covers all BCs. |
| `AddMartenStore<T>()` overload when `WolverineFx.Marten` is referenced? | Two overloads: `Action<StoreOptions>` and `Func<IServiceProvider, StoreOptions>`. Compiler prefers the Wolverine overload without explicit typing. Fix: `(StoreOptions opts) =>`. |
| `RunJasperFxCommands` namespace? | Root `JasperFx` namespace (not `JasperFx.CommandLine`). |
| `PostgreSqlBuilder` constructor pattern in Testcontainers 4.x? | Same as `MsSqlBuilder`: constructor-with-image-tag form `new PostgreSqlBuilder("postgres:17-alpine")`. Parameterless constructor is deprecated (CS0618). |
| Wolverine modular monolith settings API paths? | All three confirmed at documented paths: `opts.MultipleHandlerBehavior`, `opts.Durability.MessageIdentity`, `opts.Durability.MessageStorageSchemaName`. |
| `SellerListing` stream registration needed at scaffold time? | No. Marten discovers event types at append time. No `opts.Events.AddEventType<T>()` call needed until `DraftListingCreated` is introduced in S4. |
| `public partial class Program` — already present? | Absent at session start. Added. |
| `RunJasperFxCommands` — already present? | Absent at session start (`app.Run()` was used). Replaced. |

## Test results

| Phase | Selling Tests | All Tests | Result |
|-------|--------------|-----------|--------|
| Session open (baseline) | 0 | 8 | Pass |
| After `AddSellingModule()` throw-if-absent (first attempt) | — | 2 | Participants 6 failing (no postgres) |
| After early-return fix | 0 | 8 | Pass |
| After fixture single-container (postgres only) | — | — | Runtime throw: Polecat missing connection string |
| After two-container fixture (postgres + SQL Server) | 1 | 9 | Pass |

## Build state at session close

- Errors: 0
- Warnings: 0
- Projects added: 2 (`CritterBids.Selling`, `CritterBids.Selling.Tests`)
- `AddMarten()` calls in solution: 0
- `AddMartenStore<T>()` calls in production code: 1
- `AddMartenStore<T>()` calls in test fixtures: 1 (override)
- `opts.Policies.AutoApplyTransactions()` calls in Marten lambdas: 0
- `[MartenStore]` attribute placements: 0 (no handlers — S3+)
- `IDocumentStore` registrations: 0 (by design; any direct resolve fails at startup)
- `session.Store()` calls: 0
- `IMessageBus` usages outside `ScheduleAsync()`: 0
- `app.Run()` calls: 0 (replaced with `RunJasperFxCommands`)

## Key learnings

1. **`WolverineFx.Marten` introduces a competing `AddMartenStore<T>()` overload.** The `Func<IServiceProvider, StoreOptions>` overload is preferred by the compiler over Marten's `Action<StoreOptions>`. Every call to `AddMartenStore<T>()` — in both module registration and test fixture overrides — requires an explicit lambda parameter type annotation. When `Marten` and `Polecat` namespaces are both in scope, use `Marten.StoreOptions` (fully qualified).

2. **`opts.Policies.AutoApplyTransactions()` belongs in `UseWolverine()`, not in a Marten `StoreOptions` lambda.** The skill file and this session's prompt both placed it in the wrong location. The global host-level call in `UseWolverine()` covers all BC handlers. This finding should propagate to `docs/skills/marten-event-sourcing.md` at M2-S7.

3. **Any `AlbaHost.For<Program>` fixture must satisfy every BC module's infrastructure requirements.** Program.cs registers all BCs. The Selling test fixture requires PostgreSQL for its own store AND SQL Server for the Participants BC's Polecat store. Every future BC test fixture that introduces a new infrastructure dependency will propagate that requirement to all sibling test fixtures.

4. **`ConfigureAppConfiguration` is the correct injection point when module registration reads `IConfiguration` during `Program.cs` execution.** `ConfigureServices` runs too late — the module's early-return guard has already fired by then. The pattern: use `ConfigureAppConfiguration` to inject connection strings that govern whether a module registers, then use `ConfigureServices` to override the registered stores with Testcontainers connections.

5. **Early-return (no-op) is safer than throw-if-absent when any sibling test fixture shares the same `AlbaHost.For<Program>`.** Throw-if-absent would require all sibling test fixtures to provision every BC's infrastructure or explicitly null out the connection string — both approaches contaminate out-of-scope test fixtures. Early-return restricts the impact to the specific BC's functionality being absent in those tests.

6. **`RunJasperFxCommands` is in the root `JasperFx` namespace; `JasperFxEnvironment` is in `JasperFx.CommandLine`.** These are different types in different namespaces despite being in the same package family. Production code needs `using JasperFx;`; test fixtures additionally need `using JasperFx.CommandLine;` for `JasperFxEnvironment.AutoStartHost`.

## Verification checklist

- [x] `src/CritterBids.Selling/CritterBids.Selling.csproj` exists and references `WolverineFx.Marten`
- [x] `ISellingDocumentStore` interface exists, is public, and inherits from `IDocumentStore`
- [x] `SellerListing` class exists with `public Guid Id { get; set; }` and a code comment noting UUID v7 stream ID strategy
- [x] `AddSellingModule()` extension method exists on `IServiceCollection`, calls `AddMartenStore<ISellingDocumentStore>()` with `DatabaseSchemaName = "selling"` and chains `.UseLightweightSessions()`, `.ApplyAllDatabaseChangesOnStartup()`, and `.IntegrateWithWolverine()`
- [ ] `AddSellingModule()` calls `opts.Policies.AutoApplyTransactions()` inside the Marten lambda — **CRITERION INCORRECT**: `AutoApplyTransactions()` is not available on `StoreOptions.Policies`; the global `UseWolverine()` call already covers all BCs
- [x] `AddSellingModule()` does **not** call `AddMarten()`
- [x] `SellingModule.cs` contains a comment noting all Wolverine handlers require `[MartenStore(typeof(ISellingDocumentStore))]`
- [ ] `AddSellingModule()` throws `InvalidOperationException` if the PostgreSQL connection string is absent — **DEVIATED**: early-return (no-op) instead; throw would break 6 Participants integration tests
- [x] `CritterBids.Api/Program.cs` calls `services.AddSellingModule(builder.Configuration)`
- [x] `CritterBids.Api/Program.cs` `UseWolverine()` block includes `MultipleHandlerBehavior.Separated`, `MessageIdentity.IdAndDestination`, and `MessageStorageSchemaName = "wolverine"`
- [x] `CritterBids.Api/Program.cs` ends with `return await app.RunJasperFxCommands(args)`, not `app.Run()`
- [x] `CritterBids.Api/Program.cs` contains `public partial class Program { }`
- [x] Both new projects are added to `CritterBids.slnx`
- [x] `Directory.Packages.props` contains a pin for `WolverineFx.Marten` and `Testcontainers.PostgreSql`; no `Version=` on any `<PackageReference>`
- [x] `SellingTestFixture` exists, starts PostgreSQL and SQL Server Testcontainers containers, bootstraps `AlbaHost.For<Program>` with `ConfigureAppConfiguration` (postgres connection string) and `ConfigureServices` override (re-registers `ISellingDocumentStore`, overrides Polecat, calls `RunWolverineInSoloMode()` and `DisableAllExternalWolverineTransports()`)
- [x] `SellingTestFixture` exposes `CleanAllMartenDataAsync()` and `ResetAllMartenDataAsync()` helpers
- [x] `SellingTestCollection` defines the xUnit collection fixture
- [x] `SellingModule_BootsClean` test passes: host starts, `ISellingDocumentStore` is resolvable from DI
- [x] `dotnet test` reports 9 passing tests, zero failing (8 existing + 1 new smoke test)
- [x] `dotnet build` succeeds with zero errors and zero warnings across all projects
- [x] No files created or modified outside permitted scope
- [x] No `RegisteredSellers`, `ISellerRegistrationService`, `DraftListingCreated`, HTTP endpoints, or RabbitMQ wiring introduced

## Files changed

**New — source**
- `src/CritterBids.Selling/CritterBids.Selling.csproj`
- `src/CritterBids.Selling/ISellingDocumentStore.cs`
- `src/CritterBids.Selling/SellerListing.cs`
- `src/CritterBids.Selling/SellingModule.cs`

**New — tests**
- `tests/CritterBids.Selling.Tests/CritterBids.Selling.Tests.csproj`
- `tests/CritterBids.Selling.Tests/GlobalUsings.cs`
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs`
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestCollection.cs`
- `tests/CritterBids.Selling.Tests/SellingModuleTests.cs`

**Modified**
- `src/CritterBids.Api/Program.cs` — `AddSellingModule()`, three Wolverine modular monolith settings, `RunJasperFxCommands`, `public partial class Program`
- `src/CritterBids.Api/CritterBids.Api.csproj` — project reference to `CritterBids.Selling`
- `Directory.Packages.props` — `WolverineFx.Marten` and `Testcontainers.PostgreSql` pins
- `CritterBids.slnx` — both new projects

**New — docs**
- `docs/retrospectives/M2-S2-selling-bc-scaffold-retrospective.md` (this file)

## What remains / next session should verify

- **`[MartenStore(typeof(ISellingDocumentStore))]` on every Selling BC handler.** S2 has no handlers. S3 introduces the first handler — verify the attribute is present before committing. The requirement is documented in `SellingModule.cs`.
- **`docs/skills/marten-event-sourcing.md` contains incorrect `AutoApplyTransactions()` placement.** The skill shows `opts.Policies.AutoApplyTransactions()` inside a Marten `StoreOptions` lambda. This is factually incorrect — update at M2-S7 skills pass.
- **`AddSellingModule()` throw-if-absent deviation.** S3 or the M2-S7 skills pass should evaluate whether a `ConfigureAppConfiguration` injection requirement should be added to the Participants test fixture to allow the throw-if-absent pattern. This is a mild structural debt: early-return hides misconfiguration that throw-if-absent would surface immediately.
- **Two-container requirement is now the baseline for all future BC test fixtures** that use `AlbaHost.For<Program>`. Any new BC module added to Program.cs propagates its infrastructure requirement to all existing test fixtures. This should be documented in `docs/skills/critter-stack-testing-patterns.md` at M2-S7.
- **`SellerListing` `Apply()` methods and `DraftListingCreated` event** — S4.
- **`ISellerRegistrationService` and `RegisteredSellers` projection** — S3.
- **HTTP endpoints (`POST /api/listings/draft`)** — S4.
