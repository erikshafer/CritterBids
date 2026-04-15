# ADR 008 — Marten BC Isolation: Named Stores per Bounded Context

**Status:** Superseded by ADR 009 — Shared Primary Marten Store  
**Date:** 2026-04-14  
**Superseded:** 2026-04-14  
**Milestone:** M2-S1 — Marten BC isolation decision

> **Why superseded:** The named-store approach (`AddMartenStore<T>()`) omits the
> `SessionVariableSource`, `MartenPersistenceFrameProvider`, and `MartenOpPolicy`
> registrations that the primary `AddMarten()` path provides. This made `IDocumentSession`
> injection, `AutoApplyTransactions()`, `[Entity]`, and `IStorageAction<T>` unavailable in
> all Marten-backed handlers — eliminating the core Critter Stack idioms CritterBids exists
> to showcase. ADR 009 supersedes this decision with a single primary `IDocumentStore` and
> per-BC `ConfigureMarten()` contributions. The named-store API is a real Marten capability
> but is appropriate for multi-server deployments, not a single-server modular monolith.

---

## Context

Selling and Listings are the first BCs to use PostgreSQL via Marten, both arriving in M2 and sharing the same PostgreSQL server provisioned by `CritterBids.AppHost`. CritterBids is a modular monolith: all bounded contexts run in a single process, each registering itself via `AddXyzModule()` on `IServiceCollection`. No BC project references another BC project directly. Every BC owns its own schema; no direct cross-BC data access is permitted.

ADR 003 established this isolation model for Polecat BCs (Participants, Settlement, Operations): each BC owns its own SQL Server schema, fully configured within its own `AddXyzModule()` call. Marten BCs need the same guarantee — each BC's event tables, document tables, and projections must be confined to a schema owned by that BC and invisible to all other BCs.

The question this ADR resolves: how do two (and eventually five) Marten BCs coexist in the same process while each owning a distinct schema with no shared event table?

---

## Options Considered

**Option A — Shared `AddMarten()` with per-BC schema contribution via `ConfigureMarten()`.**
A single `AddMarten()` call establishes one primary `IDocumentStore`. Each BC module contributes its event types, projections, and document registrations by calling `ConfigureMarten()` inside its own `AddXyzModule()`. Schema isolation is attempted by setting `opts.DatabaseSchemaName` in the primary call, with individual document-type schema overrides applied in each BC's contribution.

This option does not satisfy CritterBids' isolation requirement. `DatabaseSchemaName` is a store-level property — it applies to one `StoreOptions` instance and determines the schema for that store's system tables, including the single `mt_events` table created per store. If two BCs contribute to the same store, their event streams share that table regardless of document-level schema overrides. True per-BC event-stream isolation is not achievable within a single store.

The alternative variant of Option A — each BC calling `AddMarten()` independently rather than contributing via `ConfigureMarten()` — is worse: `AddMarten()` registers `IDocumentStore` as a singleton in DI, and a second call registers a competing singleton. The container resolves one and silently discards the other BC's configuration. Neither variant of Option A provides schema isolation without breaking module encapsulation or losing BC configuration entirely.

**Option B — Named stores via `AddMartenStore<T>()`.**
Marten provides an "ancillary store" API for exactly this pattern. Each BC defines a public marker interface inheriting from `IDocumentStore` (for example, `ISellingDocumentStore`) and registers it via `AddMartenStore<ISellingDocumentStore>()` inside `AddSellingModule()`. Each named store receives its own `StoreOptions`, its own `DatabaseSchemaName`, and therefore its own `mt_events`, `mt_streams`, and document tables in a distinct PostgreSQL schema.

Context7 research against the Marten and Wolverine documentation confirmed the following for this option:

- `.ApplyAllDatabaseChangesOnStartup()` chains on the `AddMartenStore<T>()` builder exactly as it does on the `AddMarten()` builder. Schema objects for each named store are applied at startup independently.
- `.IntegrateWithWolverine()` chains on `AddMartenStore<T>()` and each named store participates in Wolverine's transactional inbox and outbox. The outbox pattern CritterBids requires (`OutgoingMessages` returned from handlers) works correctly with named stores.
- Wolverine handlers that require a Marten session from a named store must carry the `[MartenStore(typeof(ISellingDocumentStore))]` attribute. Without this attribute, Wolverine will not route the injected session to the correct store. This is an explicit requirement; Wolverine does not infer the store from the parameter type alone.
- When multiple named stores target the same PostgreSQL server, Wolverine's `MessageStorageSchemaName` durability option (set once in the host-level Wolverine configuration) directs all stores to write envelope rows to a shared schema, preventing duplicate envelope tables across named stores.
- Named stores are resolved from DI via their BC-typed marker interface. The default `IDocumentStore` interface registered by `AddMarten()` is not involved.

Option B is the only approach that provides true per-BC event-stream isolation while keeping each BC's Marten configuration fully enclosed in its own `AddXyzModule()` call — matching the Polecat isolation precedent from ADR 003.

---

## Decision

*(Superseded — see ADR 009)*

CritterBids uses one named Marten store per Marten BC, registered via `AddMartenStore<IBcDocumentStore>()` inside each BC's `AddXyzModule()`, with `DatabaseSchemaName` set to the BC name in lowercase.

---

## References

- ADR 009 — Shared Primary Marten Store (supersedes this ADR)
- ADR 003 — Polecat (SQL Server) for Operations, Settlement, and Participants BCs
- `docs/decisions/007-uuid-strategy.md` — UUID strategy
- `docs/vision/bounded-contexts.md` — BC storage assignments
