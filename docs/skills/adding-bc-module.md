# Adding a Bounded Context Module

Canonical pattern for registering a new bounded context in CritterBids. Every BC follows this shape without exception. Read this before scaffolding any new BC project.

---

## Table of Contents

1. [Overview](#overview)
2. [Project Structure](#project-structure)
3. [Marten BC Module Registration](#marten-bc-module-registration)
4. [Polecat BC Module Registration](#polecat-bc-module-registration)
5. [Host-Level Wolverine and Marten Settings](#host-level-wolverine-and-marten-settings)
6. [Contracts Project](#contracts-project)
7. [Test Fixture for Marten BCs](#test-fixture-for-marten-bcs)
8. [Wiring into Program.cs](#wiring-into-programcs)
9. [Checklist](#checklist)
10. [Anti-Patterns](#anti-patterns)

---

## Overview

CritterBids is a modular monolith. All BCs run in one process but are structurally isolated:

- No BC project references another BC project
- Cross-BC communication is exclusively via `CritterBids.Contracts` types over RabbitMQ
- Each BC registers itself via `AddXyzModule()` on `IServiceCollection`
- Each BC owns its own document schema within the shared Marten store

There are two BC flavors based on storage engine:

| Flavor | BCs | Storage | Module pattern |
|---|---|---|---|
| Marten BC | Selling, Listings, Auctions, Obligations, Relay | PostgreSQL | `services.ConfigureMarten()` |
| Polecat BC | Participants, Settlement, Operations | SQL Server | `AddPolecat()` + `ConfigurePolecat()` |

---

## Project Structure

Each BC follows Layout 2 — one production project, one test project sibling:

```
src/
  CritterBids.{BcName}/               # BC class library
    CritterBids.{BcName}.csproj
    {AggregateOrDocument}.cs           # Aggregate / domain types
    {BcName}Module.cs                  # AddXyzModule() extension method
    Features/
      {SliceName}/
        {Command}.cs                   # Command + handler + endpoint colocated
tests/
  CritterBids.{BcName}.Tests/         # One test project per production project
    CritterBids.{BcName}.Tests.csproj
    Fixtures/
      {BcName}TestFixture.cs
      {BcName}TestCollection.cs
    {FeatureName}/
      {TestClass}.cs
```

Both new projects must be added to `CritterBids.sln` in the same PR that creates them.

---

## Marten BC Module Registration

Marten BCs contribute their document types, aggregates, and projections to the **single primary
`IDocumentStore`** registered in `Program.cs`. Each BC uses `services.ConfigureMarten()` inside
its `AddXyzModule()` call — no named store, no separate connection string, no `IntegrateWithWolverine()`
per BC. See ADR 0003.

### Module Registration

```csharp
// src/CritterBids.Selling/SellingModule.cs
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Selling;

public static class SellingModule
{
    public static IServiceCollection AddSellingModule(this IServiceCollection services)
    {
        // Contribute Selling BC types to the shared primary IDocumentStore.
        // Connection string, UseLightweightSessions, ApplyAllDatabaseChangesOnStartup,
        // and IntegrateWithWolverine are all configured once in Program.cs.
        services.ConfigureMarten(opts =>
        {
            // Document tables live in the "selling" schema — isolated from other BCs
            // while sharing the same PostgreSQL database and mt_events table.
            opts.Schema.For<RegisteredSeller>().DatabaseSchemaName("selling");

            // Register event types, aggregates, and projections here as slices introduce them:
            // opts.Projections.Snapshot<SellerListing>(SnapshotLifecycle.Inline);
        });

        // Register BC-internal services
        services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

        return services;
    }
}
```

### Handler Pattern

Because the primary store is registered in `Program.cs`, `SessionVariableSource` is available
and all standard Critter Stack handler patterns work:

```csharp
// No [MartenStore] attribute — single primary store, no attribute needed
public static class SellerRegistrationCompletedHandler
{
    // IDocumentSession injected by Wolverine's SessionVariableSource
    // AutoApplyTransactions() commits — no explicit SaveChangesAsync needed
    public static void Handle(
        SellerRegistrationCompleted message,
        IDocumentSession session)
    {
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
    }
}
```

All declarative patterns are available: `[Entity]`, `[WriteAggregate]`, `[ReadAggregate]`,
`IStorageAction<T>`, `MartenOps`, and `AutoApplyTransactions()`.

### Package Reference

```xml
<PackageReference Include="WolverineFx.Marten" />
```

For BCs with HTTP endpoints:

```xml
<PackageReference Include="WolverineFx.Http.Marten" />
```

`WolverineFx.Http.Marten` transitively includes `WolverineFx.Http`, `WolverineFx.Marten`, and
`Marten`. **All `WolverineFx.*` packages in the solution must use the same version.**

---

## Polecat BC Module Registration

```csharp
// src/CritterBids.Participants/ParticipantsModule.cs
using Wolverine.Polecat;

namespace CritterBids.Participants;

public static class ParticipantsModule
{
    public static IServiceCollection AddParticipantsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPolecat(opts =>
        {
            opts.ConnectionString = connectionString;
            opts.DatabaseSchemaName = "participants";
            opts.Policies.AutoApplyTransactions();

            // Register event types, snapshots, and projections here
        })
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

        return services;
    }
}
```

**Test fixture connection string override for Polecat BCs:** Use `services.ConfigurePolecat()`,
not `services.AddPolecat()` — `AddPolecat` creates a competing store registration.

```csharp
// In test fixture ConfigureServices:
services.ConfigurePolecat(opts => { opts.ConnectionString = testConnectionString; });
```

---

## Host-Level Wolverine and Marten Settings

These settings are configured **once** in `Program.cs`. They are not per-BC settings and must
not appear inside any BC module.

### Wolverine settings (`UseWolverine`)

```csharp
builder.UseWolverine(opts =>
{
    // Required for BC isolation in a modular monolith.
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // Keeps Wolverine's envelope tables in their own schema.
    opts.Durability.MessageStorageSchemaName = "wolverine";

    // Discover handlers from each BC assembly
    opts.Discovery.IncludeAssembly(typeof(SomeBcType).Assembly);

    // RabbitMQ transport
    opts.UseRabbitMq(new Uri(rabbitMqUri));

    opts.Policies.AutoApplyTransactions();
});
```

### Primary Marten store (`Program.cs`, outside UseWolverine)

```csharp
// Guarded so test fixtures without PostgreSQL skip Marten entirely
var postgresConnectionString = builder.Configuration.GetConnectionString("postgres");
if (!string.IsNullOrEmpty(postgresConnectionString))
{
    builder.Services.AddMarten(opts =>
    {
        opts.Connection(postgresConnectionString);
        opts.DatabaseSchemaName = "public";   // mt_events, mt_streams system tables
        opts.Events.AppendMode = EventAppendMode.Quick;
        opts.Events.UseMandatoryStreamTypeDeclaration = true;
        opts.DisableNpgsqlLogging = true;
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();
}
```

Each Marten BC's `AddXyzModule()` contributes to this store via `services.ConfigureMarten()`.

---

## Contracts Project

Integration messages crossing BC boundaries are defined in `src/CritterBids.Contracts/`. The
Contracts project has no BC dependencies — it is a pure types library.

When adding the first integration event for a new BC:

1. Create `src/CritterBids.Contracts/{BcName}/` directory
2. Author the contract record following `integration-messaging.md` contract design rules
3. Add XML doc comment listing publisher and all known consumers

```csharp
namespace CritterBids.Contracts.Selling;

/// <summary>Published by Selling BC when a listing completes publication.
/// Consumed by: Listings BC, Settlement BC, Auctions BC.</summary>
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    decimal StartingBid,
    DateTimeOffset PublishedAt);
```

---

## Test Fixture for Marten BCs

Marten BC test fixtures register both the primary Marten store AND the BC module directly
in `ConfigureServices`. Program.cs's `AddMarten()` is null-guarded on the Aspire connection string,
which is absent in tests — `ConfigureAppConfiguration` does NOT propagate to Program.cs inline
guards (see `critter-stack-testing-patterns.md`). Both the store and module must be registered
in `ConfigureServices`, which runs after Program.cs and wins for `IDocumentStore` resolution.

```csharp
public class SellingTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithName($"selling-postgres-test-{Guid.NewGuid():N}")
            .WithCleanUp(true).Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register the primary Marten store with the Testcontainers connection.
                // Program.cs's AddMarten() is null-guarded on the Aspire connection string,
                // which is absent in tests. ConfigureServices runs after Program.cs, so
                // this registration is always present and wins for IDocumentStore resolution.
                services.AddMarten(opts =>
                {
                    opts.Connection(postgresConnectionString);
                    opts.DatabaseSchemaName = "public";
                    opts.DisableNpgsqlLogging = true;
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine();

                // Register this BC's module so its services (e.g. ISellerRegistrationService)
                // and ConfigureMarten contributions are present. Program.cs guards the module
                // call inside its postgres null check, which ConfigureServices bypasses.
                services.AddSellingModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    // Use non-generic overloads — primary IDocumentStore is registered
    public Task CleanAllMartenDataAsync() => Host.CleanAllMartenDataAsync();
    public Task ResetAllMartenDataAsync() => Host.ResetAllMartenDataAsync();

    public Marten.IDocumentSession GetDocumentSession() =>
        Host.DocumentStore().LightweightSession();
}
```

See `critter-stack-testing-patterns.md` for the full fixture code and cross-BC handler isolation
patterns (needed when a fixture hosts BCs whose infrastructure is not provisioned).

---

## Wiring into Program.cs

Every new BC module is wired into `Program.cs` with one line:

```csharp
builder.services.AddSellingModule();
```

`MapWolverineEndpoints()` auto-discovers all `[WolverinePost]`, `[WolverineGet]`, etc. endpoints
across all loaded BC assemblies — no per-BC mapping call needed.

Also add the BC assembly to Wolverine discovery inside `UseWolverine()`:

```csharp
opts.Discovery.IncludeAssembly(typeof(SellerListing).Assembly);
```

---

## Checklist

### New project setup
- [ ] `src/CritterBids.{BcName}/` class library created, added to `CritterBids.sln`
- [ ] `tests/CritterBids.{BcName}.Tests/` test project created, added to `CritterBids.sln`
- [ ] `WolverineFx.Marten` (or `WolverineFx.Http.Marten`) pinned in `Directory.Packages.props`
- [ ] `Testcontainers.PostgreSql` pinned in `Directory.Packages.props` (Marten BCs)

### Marten BC-specific
- [ ] `AddBcModule()` calls `services.ConfigureMarten()` — **not** `AddMarten()` or `AddMartenStore<T>()`
- [ ] Document types registered: `opts.Schema.For<T>().DatabaseSchemaName("bcname")`
- [ ] Projections, snapshots, event types added to `ConfigureMarten()` as slices introduce them
- [ ] No `[MartenStore]` attribute on handlers — not needed with single primary store
- [ ] BC assembly added to `opts.Discovery.IncludeAssembly()` in `Program.cs`

### Host-level (Program.cs) — verify once, not per BC
- [ ] `MultipleHandlerBehavior.Separated` set
- [ ] `MessageIdentity.IdAndDestination` set
- [ ] `MessageStorageSchemaName = "wolverine"` set
- [ ] Primary `AddMarten()` null-guarded on postgres connection string
- [ ] `return await app.RunJasperFxCommands(args)` (not `app.Run()`)
- [ ] `public partial class Program { }` at bottom

### Test fixture (Marten BCs)
- [ ] `services.AddMarten(...)` registered in `ConfigureServices` with Testcontainers connection string
- [ ] `services.RunWolverineInSoloMode()` present
- [ ] `services.DisableAllExternalWolverineTransports()` present
- [ ] `CleanAllMartenDataAsync()` uses non-generic `Host.CleanAllMartenDataAsync()`
- [ ] `GetDocumentSession()` uses `Host.DocumentStore().LightweightSession()`
- [ ] Collection fixture defined for sequential test execution
- [ ] If fixture co-hosts Polecat BCs: `services.ConfigurePolecat(...)` override present
- [ ] If fixture lacks another BC's infrastructure: `IWolverineExtension` exclusion registered

### Integration
- [ ] `Program.cs` calls `services.AddBcModule()` — no IConfiguration parameter needed
- [ ] If first integration event: `CritterBids.Contracts/{BcName}/` directory and record authored
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` reports expected test count passing

---

## Anti-Patterns

### ❌ Calling `AddMarten()` from a BC module

`AddMarten()` registers `IDocumentStore` as a singleton. A second call registers a competing
singleton — the second BC's configuration silently overwrites the first. Use `ConfigureMarten()`
to contribute to the existing store.

### ❌ Calling `AddMartenStore<T>()` from a BC module

Named/ancillary stores omit `SessionVariableSource`, `MartenPersistenceFrameProvider`, and
`MartenOpPolicy`. This makes `IDocumentSession` injection, `AutoApplyTransactions`, `[Entity]`,
and `IStorageAction<T>` unavailable in handlers — eliminating the core Critter Stack idioms.
Use `ConfigureMarten()` instead. See ADR 0003.

### ❌ Adding `[MartenStore]` to handlers

`[MartenStore]` is for named/ancillary stores and is not needed with the single primary store.
Handlers in Marten BCs inject `IDocumentSession` directly.

### ❌ Configuring `MultipleHandlerBehavior.Separated` inside a BC module

Host-level settings belong in `Program.cs`'s `UseWolverine()` block. A BC module receives
`IServiceCollection`, not `WolverineOptions`.

### ❌ Using `ConfigureAppConfiguration` to inject the test connection string

`ConfigureAppConfiguration` does **not** propagate to Program.cs's inline
`builder.Configuration.GetConnectionString(...)` reads — those evaluate before the factory's
callbacks apply. Register the primary Marten store and BC module directly in `ConfigureServices`
instead. See `critter-stack-testing-patterns.md`.

### ❌ Omitting `RunWolverineInSoloMode()` from Marten BC test fixtures

Without this, advisory lock contention during test restarts causes intermittent startup
failures — especially on CI. Always include it alongside `DisableAllExternalWolverineTransports()`.

### ❌ Placing RabbitMQ transport configuration inside a BC module

BC modules declare routing rules (`PublishMessage<T>()`, `ListenToRabbitQueue()`). The transport
itself (`UseRabbitMq(...)`) is configured once in `Program.cs`.

---

## References

- `docs/decisions/0003-shared-marten-store.md` — shared primary store ADR (current)
- `docs/decisions/0002-marten-bc-isolation.md` — named store ADR (superseded)
- `docs/skills/marten-event-sourcing.md` — aggregate patterns, handler conventions
- `docs/skills/critter-stack-testing-patterns.md` — Marten BC TestFixture Pattern, cross-BC isolation
- `docs/skills/integration-messaging.md` — RabbitMQ routing, `MultipleHandlerBehavior.Separated`
- `docs/skills/polecat-event-sourcing.md` — Polecat BC module pattern
- `docs/vision/bounded-contexts.md` — BC storage assignments
