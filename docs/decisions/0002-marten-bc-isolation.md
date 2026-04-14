# 0002 — Marten BC Isolation: Named Stores per Bounded Context

**Status:** Accepted
**Date:** 2026-04-14
**Milestone:** M2-S1 — Marten BC isolation decision

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

CritterBids uses one named Marten store per Marten BC, registered via `AddMartenStore<IBcDocumentStore>()` inside each BC's `AddXyzModule()`, with `DatabaseSchemaName` set to the BC name in lowercase.

---

## Consequences

**Module registration pattern.**
Each Marten BC follows a consistent shape inside its `AddXyzModule()` extension method: define a public marker interface inheriting from `IDocumentStore` scoped to the BC; call `AddMartenStore<IBcDocumentStore>()` configured with the connection string from `IConfiguration`, the BC's lowercase schema name set as `DatabaseSchemaName`, `opts.Policies.AutoApplyTransactions()`, and all event stream and projection registrations for that BC; chain `.ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` on the returned builder. The host-level Wolverine configuration in `Program.cs` sets the `MessageStorageSchemaName` durability option to a shared schema name so all named stores write envelope rows to the same table without duplication.

**S2 working assumption corrected.**
`M2-listings-pipeline.md` §5 shows a working assumption where each BC calls `AddMarten()` directly with `DatabaseSchemaName` set to the BC name. That assumption is incorrect for a multi-BC process: two `AddMarten()` calls register two competing `IDocumentStore` singletons in DI, and the container silently discards one BC's configuration. S2 (Selling BC scaffold) must use `AddMartenStore<ISellingDocumentStore>()` instead of `AddMarten()`. The builder chain shape — `.ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` — is unchanged; only the registration method and the introduction of the BC-scoped marker interface differ.

**Wolverine handler injection.**
All Wolverine handlers that operate on a Marten BC's store must carry the `[MartenStore(typeof(IBcDocumentStore))]` attribute to receive an injected session from the correct named store. This attribute is a hard requirement. Handlers that omit it will not resolve to the BC's store and will fail to compile or behave incorrectly at runtime.

**Default `IDocumentStore` intentionally absent.**
No BC in CritterBids registers a primary store via `AddMarten()`. The default `IDocumentStore` is intentionally not registered in the DI container. All session injection flows through the named BC-typed store interfaces. Any component that attempts to resolve `IDocumentStore` directly will fail at startup and must instead resolve the appropriate BC-typed interface.

**Test fixture connection string override.**
Test fixtures for a named-store BC override the Testcontainers-issued connection string by re-registering the named store in the `ConfigureServices` override of the test host builder. A call to `AddMartenStore<IBcDocumentStore>()` in the test host's `ConfigureServices` block with the Testcontainers connection string replaces the production registration. Because each BC's named store is registered independently, overriding one BC's store does not affect another BC's named store registration.

**UUID v7 for Marten BC stream IDs.**
Marten BC stream IDs use `Guid.CreateVersion7()` per the convention established in `docs/decisions/0001-uuid-strategy.md` (Proposed). Unlike Polecat BCs where UUID v5 determinism is load-bearing for idempotent stream creation from a natural business key, Marten BCs in CritterBids generate stream IDs at entity creation time with no cross-handler coordination requirement. UUID v7 provides insert locality via its Unix-ms timestamp prefix and requires no namespace constant per BC. This convention is confirmed for M2; `0001-uuid-strategy.md` remains Proposed pending Marten 8 capability verification and JasperFx team input, re-evaluated at M3.

**Explicitly out of scope for this ADR.**
Marten async daemon configuration (`AddAsyncDaemon()`, `DaemonMode`), EF Core projections, and Marten multi-tenancy (a distinct feature from multi-store — CritterBids does not use Marten multi-tenancy) are not addressed here and are deferred to later sessions. Named Polecat stores are also deferred; only one Polecat BC (Participants) exists through M2.

---

## References

- ADR 003 — Polecat (SQL Server) for Operations, Settlement, and Participants BCs
- `docs/decisions/0001-uuid-strategy.md` — UUID strategy (Proposed); Marten BC stream ID convention cross-referenced above
- `docs/milestones/M2-listings-pipeline.md` §5 — working assumption corrected by this ADR
- `docs/vision/bounded-contexts.md` — BC storage assignments
