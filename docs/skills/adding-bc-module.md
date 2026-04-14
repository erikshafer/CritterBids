# Adding a Bounded Context Module

Canonical pattern for registering a new bounded context in CritterBids. Every BC follows this shape without exception. Read this before scaffolding any new BC project.

---

## Table of Contents

1. [Overview](#overview)
2. [Project Structure](#project-structure)
3. [Marten BC Module Registration](#marten-bc-module-registration)
4. [Polecat BC Module Registration](#polecat-bc-module-registration)
5. [Host-Level Wolverine Settings](#host-level-wolverine-settings)
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
- Each BC owns its own database schema

There are two BC flavors based on storage engine:

| Flavor | BCs | Storage | Module pattern |
|---|---|---|---|
| Marten BC | Selling, Listings, Auctions, Obligations, Relay | PostgreSQL | `AddMartenStore<IBcDocumentStore>()` |
| Polecat BC | Participants, Settlement, Operations | SQL Server | `AddPolecat()` + `ConfigurePolecat()` |

---

## Project Structure

Each BC follows Layout 2 — one production project, one test project sibling:

```
src/
  CritterBids.{BcName}/               # BC class library
    CritterBids.{BcName}.csproj
    I{BcName}DocumentStore.cs          # Named store marker interface (Marten BCs only)
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

### 1. Named Store Marker Interface

```csharp
// src/CritterBids.Selling/ISellingDocumentStore.cs
using Marten;

namespace CritterBids.Selling;

/// <summary>
/// Named Marten store for the Selling BC. Injected by handlers via [MartenStore] attribute.
/// There is no default IDocumentStore in CritterBids — all session injection flows through
/// BC-typed named store interfaces (see ADR 0002).
/// </summary>
public interface ISellingDocumentStore : IDocumentStore { }
```

### 2. Module Registration

```csharp
// src/CritterBids.Selling/SellingModule.cs
using Marten;
using Wolverine.Marten;

namespace CritterBids.Selling;

public static class SellingModule
{
    public static IServiceCollection AddSellingModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        var connectionString = config["ConnectionStrings:critterbids-postgres"]
            ?? throw new InvalidOperationException(
                "Missing Selling BC PostgreSQL connection string (ConnectionStrings:critterbids-postgres)");

        services.AddMartenStore<ISellingDocumentStore>(opts =>
        {
            opts.Connection(connectionString);

            // Schema isolation — every Marten BC gets its own lowercase schema name
            opts.DatabaseSchemaName = "selling";

            // Required in every BC's store configuration — without this, handlers do not commit
            opts.Policies.AutoApplyTransactions();

            // ── Recommended greenfield event settings ─────────────────────────
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;

            // ── Aggregate + projection registrations ──────────────────────────
            // Register event types, snapshots, and projections here as they are added.
            // Example (add when SellerListing events are introduced in S4):
            // opts.Projections.Snapshot<SellerListing>(SnapshotLifecycle.Inline);
            // opts.Projections.UseIdentityMapForAggregates = true;

            opts.DisableNpgsqlLogging = true;
        })
        // ⚠️ Chain order matters — all three must be on the builder, not inside the opts lambda
        .UseLightweightSessions()           // No identity map overhead; required for all BCs
        .ApplyAllDatabaseChangesOnStartup() // Creates schema objects at startup; required for test fixtures
        .IntegrateWithWolverine();          // Transactional outbox + Wolverine transaction middleware

        // ⚠️ REQUIRED ON ALL HANDLERS: [MartenStore(typeof(ISellingDocumentStore))]
        // Wolverine does NOT infer the named store from handler parameter types. Every handler
        // in this BC that injects a Marten session must carry this attribute or sessions will
        // route to the wrong store (or fail entirely).

        // ── Wolverine RabbitMQ routing rules (add as integrations are wired) ──
        // Publish: opts.PublishMessage<T>().ToRabbitQueue("queue-name");
        // Subscribe: opts.ListenToRabbitQueue("queue-name").ProcessInline();

        return services;
    }
}
```

### 3. Package Reference

```xml
<PackageReference Include="WolverineFx.Marten" />
```

For BCs with HTTP endpoints, upgrade to:

```xml
<PackageReference Include="WolverineFx.Http.Marten" />
```

`WolverineFx.Http.Marten` transitively includes `WolverineFx.Http`, `WolverineFx.Marten`, and `Marten`. **All `WolverineFx.*` packages in the solution must use the same version.**

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
        IConfiguration config)
    {
        var connectionString = config["ConnectionStrings:critterbids-sqlserver"]
            ?? throw new InvalidOperationException(
                "Missing Participants BC SQL Server connection string");

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

**Test fixture connection string override for Polecat BCs:** Use `services.ConfigurePolecat()`, not `services.AddPolecat()` — `AddPolecat` creates a competing store registration.

```csharp
// In test fixture ConfigureServices:
services.ConfigurePolecat(opts => { opts.ConnectionString = testConnectionString; });
```

---

## Host-Level Wolverine Settings

These three settings are configured **once** in `Program.cs`'s `UseWolverine()` block. They are not per-BC settings and must not appear inside any BC module.

```csharp
builder.Host.UseWolverine(opts =>
{
    // Required for BC isolation in a modular monolith.
    // Without Separated: multiple BC handlers for the same message type share one queue.
    // Without IdAndDestination: inbox deduplication prevents fanout from reaching all BC handlers.
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;

    // Routes all named Marten stores' envelope rows to a shared schema.
    // Without this: each named store creates its own duplicate envelope tables.
    opts.Durability.MessageStorageSchemaName = "wolverine";

    // RabbitMQ transport — Aspire-compatible
    opts.UseRabbitMqUsingNamedConnection("rabbit").AutoProvision();

    // BC-specific routing rules contributed by each AddXyzModule() call appear here
    // after module registration wires them via IServiceCollection extensions.
});
```

---

## Contracts Project

Integration messages crossing BC boundaries are defined in `src/CritterBids.Contracts/`. The Contracts project has no BC dependencies — it is a pure types library.

When adding the first integration event for a new BC:

1. Create `src/CritterBids.Contracts/{BcName}/` directory
2. Author the contract record following `integration-messaging.md` contract design rules
3. Add XML doc comment listing publisher and all known consumers
4. Update `CritterBids.Contracts.csproj` if a new folder is added

```csharp
namespace CritterBids.Contracts.Selling;

/// <summary>
/// Published by Selling BC when a listing completes the submission → approval → publication chain.
///
/// Consumed by:
/// - Listings BC: Build CatalogListingView projection
/// - Settlement BC: Cache reserve price and fee percentage for future settlement
/// - Auctions BC: Load extended bidding configuration when BiddingOpened fires
/// </summary>
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    ListingFormat Format,
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    decimal FeePercentage,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow,
    TimeSpan? ExtendedBiddingExtension,
    DateTimeOffset PublishedAt);
```

---

## Test Fixture for Marten BCs

Every Marten BC test project gets a fixture that:

1. Starts a `PostgreSqlBuilder` Testcontainers container
2. Re-registers the named store with the Testcontainers connection string
3. Calls both `RunWolverineInSoloMode()` and `DisableAllExternalWolverineTransports()`
4. Exposes `CleanAllMartenDataAsync()` and `ResetAllMartenDataAsync()` for test isolation

See `critter-stack-testing-patterns.md` §Marten BC TestFixture Pattern for the canonical fixture code.

**Critical:** `services.AddMartenStore<IBcDocumentStore>()` in the `ConfigureServices` override re-registers the named store, replacing the production connection string. `services.ConfigureMarten()` does NOT work for named stores.

---

## Wiring into Program.cs

Every new BC module is wired into `Program.cs` with exactly two lines — one service registration call and one endpoint mapping:

```csharp
// Service registration (in the services configuration block)
builder.Services.AddSellingModule(builder.Configuration);

// Endpoint mapping (if the BC has HTTP endpoints — after app is built)
// HTTP endpoints are auto-discovered by MapWolverineEndpoints() — no per-BC call needed
```

`MapWolverineEndpoints()` is called once and auto-discovers all `[WolverinePost]`, `[WolverineGet]`, etc. endpoints across all loaded BC assemblies.

**Check these in `Program.cs` when wiring the first Marten BC:**

```csharp
// Required for test projects to reference Program
public partial class Program { }

// Required for db-apply, db-assert, codegen CLI commands
// Replace app.Run() with:
return await app.RunJasperFxCommands(args);
```

---

## Checklist

Use this when adding any new BC to CritterBids:

### New project setup
- [ ] `src/CritterBids.{BcName}/` class library created, added to `CritterBids.sln`
- [ ] `tests/CritterBids.{BcName}.Tests/` test project created, added to `CritterBids.sln`
- [ ] `WolverineFx.Marten` (or `WolverineFx.Http.Marten`) pinned in `Directory.Packages.props`
- [ ] `Testcontainers.PostgreSql` pinned in `Directory.Packages.props` (Marten BCs)

### Marten BC-specific
- [ ] `IBcDocumentStore : IDocumentStore` marker interface created
- [ ] `AddBcModule()` uses `AddMartenStore<IBcDocumentStore>()` — not `AddMarten()`
- [ ] Builder chain: `.UseLightweightSessions().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`
- [ ] `opts.DatabaseSchemaName` set to lowercase BC name
- [ ] `opts.Policies.AutoApplyTransactions()` present
- [ ] Comment in module noting `[MartenStore(typeof(IBcDocumentStore))]` requirement on all handlers

### Host-level (Program.cs) — verify once, not per BC
- [ ] `MultipleHandlerBehavior.Separated` set in `UseWolverine()`
- [ ] `MessageIdentity.IdAndDestination` set in `UseWolverine()`
- [ ] `MessageStorageSchemaName = "wolverine"` set in `UseWolverine()`
- [ ] `UseRabbitMqUsingNamedConnection("rabbit").AutoProvision()` present
- [ ] `return await app.RunJasperFxCommands(args)` (not `app.Run()`)
- [ ] `public partial class Program { }` at bottom of file

### Test fixture (Marten BCs)
- [ ] `{BcName}TestFixture.cs` re-registers named store via `services.AddMartenStore<IBcDocumentStore>()`
- [ ] `services.RunWolverineInSoloMode()` present
- [ ] `services.DisableAllExternalWolverineTransports()` present
- [ ] `CleanAllMartenDataAsync()` helper exposed (from `Marten` namespace on `IAlbaHost`)
- [ ] `ResetAllMartenDataAsync()` helper exposed
- [ ] `{BcName}TestCollection.cs` collection fixture defined
- [ ] Smoke test verifying host boots and named store resolves from DI

### Integration
- [ ] `Program.cs` calls `services.AddBcModule(builder.Configuration)`
- [ ] If first integration event: `CritterBids.Contracts/{BcName}/` directory and record authored
- [ ] `dotnet build` succeeds with 0 errors
- [ ] `dotnet test` reports expected test count passing

---

## Anti-Patterns

### ❌ Calling `AddMarten()` from a BC module

Two `AddMarten()` calls in the same process register competing `IDocumentStore` singletons. The second BC's entire configuration is silently discarded. Always use `AddMartenStore<IBcDocumentStore>()`.

### ❌ Omitting `[MartenStore]` from Wolverine handlers

Handlers in a named-store BC that inject a Marten session without `[MartenStore(typeof(IBcDocumentStore))]` will receive sessions from the wrong store or fail at runtime. No compile-time error. Always add the attribute.

### ❌ Configuring `MultipleHandlerBehavior.Separated` inside a BC module

These are host-level settings. A BC module cannot configure `WolverineOptions` directly. All three modular monolith settings (`Separated`, `IdAndDestination`, `MessageStorageSchemaName`) belong in `Program.cs`'s `UseWolverine()` block.

### ❌ Using `services.ConfigureMarten()` to override named store in test fixtures

`ConfigureMarten()` configures the default `IDocumentStore`. For named stores, re-register via `services.AddMartenStore<IBcDocumentStore>()` in the `ConfigureServices` override. The re-registration replaces the production registration for that named store.

### ❌ Omitting `RunWolverineInSoloMode()` from Marten BC test fixtures

Without this, advisory lock contention during test restarts causes intermittent fixture startup failures — especially on CI. Always include it alongside `DisableAllExternalWolverineTransports()`.

### ❌ Placing RabbitMQ transport configuration inside a BC module

BC modules declare which queues to publish to and subscribe from (`PublishMessage<T>()`, `ListenToRabbitQueue()`). The transport itself (`UseRabbitMqUsingNamedConnection()`) is configured once in `Program.cs`.

---

## References

- `docs/decisions/0002-marten-bc-isolation.md` — named store ADR; why `AddMartenStore<T>()` is required
- `docs/skills/marten-event-sourcing.md` — aggregate patterns, `[MartenStore]` on handlers
- `docs/skills/critter-stack-testing-patterns.md` — Marten BC TestFixture Pattern
- `docs/skills/integration-messaging.md` — RabbitMQ routing, `MultipleHandlerBehavior.Separated`
- `docs/skills/polecat-event-sourcing.md` — Polecat BC module pattern
- `docs/vision/bounded-contexts.md` — BC storage assignments
