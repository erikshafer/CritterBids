---
name: adding-bc-module
description: "CritterBids BC module procedure: AddXyzModule, ConfigureMarten, single host AddMarten, schema-per-BC, Contracts-only integration. Use when scaffolding or wiring a bounded context."
cluster: infrastructure
tags: [modular-monolith, marten, wolverine, bounded-contexts, contracts]
---

# Adding a Bounded Context Module

> CritterBids procedure for adding or wiring a bounded context.
> Generic modular-monolith and new-project mechanics live in ai-skills `critterstack-arch-*`; **this skill documents only the CritterBids-specific module shape.**

## When to apply this skill

Use this skill when:

- Creating a new `src/CritterBids.{BcName}` project or test project.
- Wiring `AddXyzModule()` into `Program.cs`.
- Registering documents, projections, snapshots, or event types for a BC.
- Adding cross-BC contracts under `CritterBids.Contracts`.
- Fixing a fixture whose `Program.cs` inline connection-string guard skipped the primary store.

Do NOT use this skill for: aggregate handler mechanics (see `marten-event-sourcing`), generic modular-monolith theory (see upstream), or generic Testcontainers mechanics (see `critter-stack-testing-patterns`).

## Read upstream first

Generic mechanics are covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of the architecture:

1. `critterstack-arch-modular-monolith` — Wolverine module boundaries, handler isolation, inter-module messaging.
2. `critterstack-arch-new-project-wolverine-marten` — bootstrap shape for Wolverine + Marten projects.

This skill picks up at CritterBids' exact project layout, single-store posture, and contracts rules.

## CritterBids module rules

CritterBids is a modular monolith with all eight BCs on Marten/PostgreSQL (ADR 011): Participants, Selling, Auctions, Listings, Settlement, Obligations, Relay, Operations.

Hard rules:

- No BC project references another BC project.
- Only `CritterBids.Contracts` crosses BC boundaries.
- Each BC exposes `AddXyzModule(this IServiceCollection services)`.
- BC modules use `services.ConfigureMarten(...)`; never `AddMarten()` or `AddMartenStore<T>()`.
- One primary `IDocumentStore` is registered in `src/CritterBids.Api/Program.cs`.
- Each BC owns tables in its schema (`selling`, `auctions`, etc.); Marten system tables live in `public`.
- Host-level Wolverine settings live in `Program.cs`, not in BC modules.

## Project and solution shape

```text
src/
  CritterBids.{BcName}/
    CritterBids.{BcName}.csproj
    {BcName}Module.cs
    Features/
      {SliceName}/
        {Command}.cs
tests/
  CritterBids.{BcName}.Tests/
    CritterBids.{BcName}.Tests.csproj
    Fixtures/
      {BcName}TestFixture.cs
      {BcName}TestCollection.cs
```

CritterBids uses `CritterBids.slnx`, not `.sln`. Add XML `<Project Path="..." />` entries directly. `dotnet sln` does not manage `.slnx` here.

## Module procedure

```csharp
namespace CritterBids.Selling;

public static class SellingModule
{
    public static IServiceCollection AddSellingModule(this IServiceCollection services)
    {
        services.ConfigureMarten(opts =>
        {
            opts.Schema.For<RegisteredSeller>().DatabaseSchemaName("selling");
            opts.Events.AddEventType<SellerRegistered>();
            opts.Projections.Snapshot<SellerListing>(SnapshotLifecycle.Inline);
        });

        services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();
        return services;
    }
}
```

No connection string parameter. No store provisioning. No `IntegrateWithWolverine()` here.

## Host wiring procedure

In `Program.cs`:

```csharp
builder.Services.AddMarten(opts =>
{
    opts.Connection(postgresConnectionString);
    opts.DatabaseSchemaName = "public";
    opts.Events.AppendMode = EventAppendMode.Quick;
    opts.Events.UseMandatoryStreamTypeDeclaration = true;
    opts.DisableNpgsqlLogging = true;
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();

builder.Services.AddSellingModule();
```

In `UseWolverine`:

```csharp
opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
opts.Durability.MessageStorageSchemaName = "wolverine";
opts.Discovery.IncludeAssembly(typeof(SellerListing).Assembly);
opts.Policies.AutoApplyTransactions();
```

These settings are global. Do not repeat them in a BC.

## Contracts procedure

Cross-BC integration messages live in `src/CritterBids.Contracts/{BcName}`. Contracts must be `sealed record`, carry all known consumer data, and list publisher/consumers in XML docs.

```csharp
namespace CritterBids.Contracts.Selling;

/// <summary>
/// Published by Selling when a listing is published.
/// Consumed by: Listings, Auctions, Settlement.
/// </summary>
public sealed record ListingPublished(
    Guid ListingId,
    Guid SellerId,
    string Title,
    decimal StartingBid,
    DateTimeOffset PublishedAt);
```

Domain events stay in the owning BC namespace, even when they share a simple name with an integration contract. Use aliases in handler files when both are needed.

## Test fixture essentials

Program.cs reads connection strings before Alba configuration callbacks can change inline guards. In tests, register the store and BC module directly in `ConfigureServices`:

```csharp
builder.ConfigureServices(services =>
{
    services.AddMarten(opts => opts.Connection(postgresConnectionString))
        .UseLightweightSessions()
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

    services.AddSellingModule();
    services.RunWolverineInSoloMode();
    services.DisableAllExternalWolverineTransports();
});
```

Use non-generic cleanup helpers for the primary store: `Host.CleanAllMartenDataAsync()` and `Host.ResetAllMartenDataAsync()`.

## Checklist

- [ ] Production and test projects created and added to `CritterBids.slnx`.
- [ ] API project references new BC project; no BC references another BC.
- [ ] BC module exposes `AddXyzModule()` and calls `services.ConfigureMarten()`.
- [ ] Documents/projections/event types assigned to the BC schema.
- [ ] `Program.cs` calls `AddXyzModule()` and includes the BC assembly in Wolverine discovery.
- [ ] Integration contracts live only in `CritterBids.Contracts`.
- [ ] Test fixture registers `AddMarten()`, `AddXyzModule()`, solo mode, and disabled external transports.
- [ ] `dotnet build` and relevant tests pass.

## Common pitfalls

- **Calling `AddMarten()` in a BC.** It creates a competing primary store and loses other BC contributions.
- **Calling `AddMartenStore<T>()` for BC isolation.** Named stores lose primary-store handler conveniences unless deliberately using ancillary-store patterns; CritterBids does not.
- **Adding `[MartenStore]` to handlers.** Not needed with the shared primary store.
- **Putting RabbitMQ transport setup in a BC.** Transport is host-level; BCs only contribute routes/contracts through host config.
- **Using stale Polecat framing.** Polecat was eliminated by ADR 011; current BCs are all Marten/PostgreSQL.
- **Relying on `ConfigureAppConfiguration` in tests.** It does not affect Program.cs inline guards; use `ConfigureServices`.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `critterstack-arch-modular-monolith`, `critterstack-arch-new-project-wolverine-marten`.

**Prerequisites:**

- `csharp-coding-standards` — records, collections, naming, GUID/time rules.
- `domain-event-conventions` — domain event shape and registration.

**Downstream:**

- `marten-event-sourcing` — stream identity, aggregate handlers, store configuration.
- `integration-messaging` — cross-BC routes and contracts.
- `critter-stack-testing-patterns` — fixture isolation and cross-BC handler filtering.

**External:**

- ADR 009 (shared primary store), ADR 011 (All-Marten Pivot) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Modular Monolith Rules and Canonical Bootstrap Sequence.
