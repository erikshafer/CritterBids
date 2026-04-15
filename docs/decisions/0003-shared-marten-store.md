# 0003 — Shared Primary Marten Store

**Status:** Accepted  
**Date:** 2026-04-14  
**Supersedes:** ADR 0002 — Marten BC Isolation: Named Stores per Bounded Context

---

## Context

ADR 0002 established that each Marten-backed BC would register an independent named store via `AddMartenStore<IBcDocumentStore>()`. The intent was sound: each BC owns a distinct PostgreSQL schema and its event tables must not interleave with other BCs' streams.

The named-store approach was confirmed to work by Context7 research at the time of ADR 0002. What the research did not surface — and what only became apparent during M2-S2 implementation — is that `AddMartenStore<T>()` is an *ancillary* store API. It intentionally omits the registration of `SessionVariableSource`, `MartenPersistenceFrameProvider`, and `MartenOpPolicy` that the primary `AddMarten()` path provides. These registrations are what enable Wolverine's code-generated handler middleware to inject `IDocumentSession`, apply `AutoApplyTransactions()`, honour `[Entity]` attribute loading, and process `IStorageAction<T>` return types.

The practical result: every Marten-backed handler in CritterBids was forced to inject `ISellingDocumentStore` directly, open a `LightweightSession()` manually, call `SaveChangesAsync()` explicitly, and carry a `[MartenStore(typeof(ISellingDocumentStore))]` attribute on every class. `AutoApplyTransactions()` did not fire. `[Entity]` did not work. `IStorageAction<T>` / `MartenOps` did not work. `[WriteAggregate]` / `[ReadAggregate]` did not work.

CritterBids exists to demonstrate idiomatic Critter Stack development. A codebase that cannot use `IDocumentSession`, `AutoApplyTransactions`, or `[Entity]` cannot fulfil that purpose.

### Why named stores seemed necessary

ADR 0002 correctly identified that two independent `AddMarten()` calls in the same `IServiceCollection` register competing `IDocumentStore` singletons — the second call's configuration replaces the first. If BC-A and BC-B each call `AddMarten()`, one BC's projections, document types, and event registrations are silently discarded.

### Why the solution was wrong for this project

The named-store isolation provides per-BC `mt_events` tables — a physical schema guarantee appropriate when BCs must be extractable to separate database instances. CritterBids is a modular monolith running all BCs in a single process against a single Aspire-provisioned PostgreSQL server. Physical event-table isolation is an operational concern. What CritterBids needs to demonstrate is correct *code* isolation: handlers that cannot accidentally read another BC's streams, projections that only subscribe to their own event types, and aggregates that live in their own schema namespace.

None of that requires a separate `mt_events` table. It requires that each BC register its types, projections, and schema assignments through its own module — which `ConfigureMarten()` supports fully.

Additionally, CritterSupply (the companion microservices project) demonstrates the separate-process path. CritterBids' value is in showing the modular monolith path. Named stores are the mechanism you reach for when BCs must connect to *separate PostgreSQL servers*. CritterBids does not have that requirement.

---

## Decision

CritterBids uses a **single primary `IDocumentStore`** registered once in `Program.cs` via `AddMarten()`. Each Marten BC contributes its event types, projections, aggregates, and document registrations by calling `services.ConfigureMarten()` inside its own `AddXyzModule()` extension method.

---

## Architecture

```
Program.cs
  AddMarten(opts => {
      opts.Connection(postgresConnectionString);
      opts.DatabaseSchemaName = "public";      // system tables (mt_events, mt_streams)
      opts.Events.AppendMode = EventAppendMode.Quick;
      ...
  })
  .UseLightweightSessions()
  .ApplyAllDatabaseChangesOnStartup()
  .IntegrateWithWolverine();

SellingModule.cs  (AddSellingModule)
  services.ConfigureMarten(opts => {
      opts.Schema.For<RegisteredSeller>().DatabaseSchemaName("selling");
      // projections, aggregates, and other Selling types here
  });

AuctionsModule.cs  (AddAuctionsModule)
  services.ConfigureMarten(opts => {
      opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);
      opts.Schema.For<Listing>().DatabaseSchemaName("auctions");
      // ...
  });
```

The single `mt_events` and `mt_streams` tables live in the schema defined by `opts.DatabaseSchemaName` (default: `public`). Document tables for each BC live in BC-named schemas via per-type `DatabaseSchemaName` configuration. Stream identity is maintained by aggregate type — each BC's handlers only ever load and append to their own aggregate types.

---

## Consequences

### Restored handler patterns

All Critter Stack idiomatic patterns are restored in Marten-backed handlers:

- `IDocumentSession` injection in handler parameters ✅
- `AutoApplyTransactions()` firing from the Wolverine middleware chain ✅
- `[Entity]` attribute auto-loading ✅
- `IStorageAction<T>` / `MartenOps` return types ✅
- `[WriteAggregate]` / `[ReadAggregate]` attribute-driven aggregate loading ✅
- `[MartenStore]` attribute no longer required on handlers ✅

### Shared `mt_events` table

All Marten BCs share a single `mt_events` and `mt_streams` table in the configured schema. This is the accepted trade-off. It is appropriate because:

- CritterBids is a showcase, not a production system requiring physical BC extraction
- Stream isolation is maintained by aggregate type, not by physical table
- Each BC's handlers, projections, and subscriptions only reference their own aggregate types
- No BC queries another BC's event streams

### `ConfigureMarten()` is BC-encapsulated

Each Marten BC's document types, aggregate registrations, and projections live inside `AddXyzModule()` via `services.ConfigureMarten()`. No BC contributes to any other BC's configuration. The module boundary is preserved; only the physical storage mechanism changes.

### Primary store is conditionally registered

`AddMarten()` in `Program.cs` is guarded by a null check on the postgres connection string. Test fixtures that do not provision PostgreSQL (e.g., Participants-only fixtures) do not register `IDocumentStore`. `SessionVariableSource` is absent in those fixtures, and Marten-backed handlers discovered from the Selling or other Marten BC assemblies must be excluded via `IWolverineExtension`. See `docs/skills/critter-stack-testing-patterns.md` §Cross-BC Handler Isolation.

### Named store marker interfaces removed

`ISellingDocumentStore` and all equivalent BC-typed `IDocumentStore` interfaces are deleted. `[MartenStore]` attributes are removed from all handlers. `CleanAllMartenDataAsync()` and `ResetAllMartenDataAsync()` revert to their non-generic forms.

### `MessageStorageSchemaName` retained

`opts.Durability.MessageStorageSchemaName = "wolverine"` remains in `Program.cs`'s `UseWolverine()` block. With a single store it now controls the schema where Wolverine's envelope tables are created within that store, keeping them separate from application tables. The prior justification (preventing duplicate envelope tables across named stores) no longer applies.

### Test fixture simplification

Marten BC test fixtures no longer require `AddMartenStore<IBcDocumentStore>()` in their `ConfigureServices` override. The test connection string is injected via `ConfigureAppConfiguration` (where it is consumed by `Program.cs`'s `AddMarten()` call) — no `ConfigureServices` store re-registration is needed.

---

## Updated BC Module Pattern

### Marten BC module

```csharp
public static IServiceCollection AddSellingModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.ConfigureMarten(opts =>
    {
        // Document tables live in the "selling" schema
        opts.Schema.For<RegisteredSeller>().DatabaseSchemaName("selling");

        // Add event types, projections, and snapshots here as slices introduce them
    });

    services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

    return services;
}
```

### Marten BC handler

```csharp
// No [MartenStore] attribute required — single primary store
public static class SellerRegistrationCompletedHandler
{
    public static void Handle(
        SellerRegistrationCompleted message,
        IDocumentSession session)
    {
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
        // AutoApplyTransactions commits — no explicit SaveChangesAsync
    }
}
```

---

## `docs/skills/marten-named-stores.md`

The skill created alongside ADR 0002 is archived. It documents a real Marten capability (the ancillary store API), but one that is not appropriate for CritterBids' architecture. It is retained in the repository as reference material under a clearly marked archived status.

---

## References

- ADR 0002 — Marten BC Isolation: Named Stores per Bounded Context (superseded)
- ADR 001 — Modular Monolith Architecture
- `src/CritterBids.Api/Program.cs` — primary `AddMarten()` registration
- `src/CritterBids.Selling/SellingModule.cs` — canonical `ConfigureMarten()` BC contribution
- `docs/skills/critter-stack-testing-patterns.md` — cross-BC handler isolation in fixtures
- Marten docs: [ConfigureMarten](https://martendb.io/configuration/index.html#addmarten-and-configure-marten)
