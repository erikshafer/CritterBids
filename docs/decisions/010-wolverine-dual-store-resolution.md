# ADR 010 — Wolverine Dual-Store Resolution

**Status:** Proposed — Pending JasperFx input
**Date:** 2026-04-15
**References:** ADR 009 — Shared Primary Marten Store (§Consequences, "Production multi-store conflict is unresolved")

---

## Context

CritterBids is a .NET modular monolith with eight bounded contexts. Five BCs (Selling, Auctions,
Listings, Obligations, Relay) use PostgreSQL via Marten. Three BCs (Participants, Settlement,
Operations) use SQL Server via Polecat. `src/CritterBids.Api/Program.cs` registers both storage
backends when the corresponding connection strings are present.

### The error

When both `ConnectionStrings:postgres` and `ConnectionStrings:sqlserver` are configured — which is
the production state under .NET Aspire — startup throws:

```
InvalidWolverineStorageConfigurationException: There must be exactly one message store tagged as
the 'main' store. Found multiples:
  wolverinedb://sqlserver/127.0.0.1/master/wolverine,
  wolverinedb://postgresql/127.0.0.1/postgres/wolverine
```

### Root cause

`Program.cs` makes two calls that each register a Wolverine "main" message store:

```csharp
// Guard 1 — postgres present
builder.Services.AddMarten(opts => { ... })
    .IntegrateWithWolverine();   // ← registers PostgreSQL as the Wolverine main store

// Guard 2 — sqlserver present
builder.Services.AddParticipantsModule(sqlServerConnectionString);
// AddParticipantsModule() internally calls:
//   AddPolecat(...).IntegrateWithWolverine();  ← registers SQL Server as a second Wolverine main store
```

Wolverine requires exactly one main store. Both guards fire in production; neither fires alone in
a single-BC test fixture. The test suite is unaffected (fixtures provision exactly one backend), but
`dotnet run --project src/CritterBids.AppHost` fails at host startup.

---

## Options Considered

### Option A — Mark Polecat as ancillary via `IntegrateWithWolverine()` callback

Hypothesis: `AddPolecat().IntegrateWithWolverine(cfg => cfg.IsMain = false)` or a
`MessageStoreRole` equivalent may exist on `PolecatIntegration`.

**Investigation (Wolverine.Polecat 5.30.0, `Wolverine.Polecat.xml`):**

`PolecatIntegration` exposes:
- `UseFastEventForwarding` (bool)
- `UseWolverineManagedEventSubscriptionDistribution` (bool)
- `MainDatabaseConnectionString` (string — for multi-tenant database-per-tenant scenarios)
- `TransportSchemaName` (string — SQL Server transport queues schema)
- `MessageStorageSchemaName` (string — inbox/outbox tables schema)

No `IsMain`, `IsAncillary`, `MessageStoreRole`, or equivalent property exists. The single
`IntegrateWithWolverine()` overload on `PolecatConfigurationExpression` unconditionally registers
Polecat as the Wolverine main message store.

**Contrast with Marten:** Wolverine.Marten 5.30.0 provides a second overload:
`AncillaryWolverineOptionsMartenExtensions.IntegrateWithWolverine<T>(MartenStoreExpression<T>, Action<AncillaryMartenIntegration>)`.
This overload is called on `AddMartenStore<T>()` expressions (the named/ancillary Marten store path)
and registers the store as ancillary. No equivalent exists for Polecat.

**Decision: rejected.** The API does not exist.

---

### Option B — Standalone Wolverine message persistence

Hypothesis: calling `opts.PersistMessagesWithSqlServer(connectionString)` or
`opts.PersistMessagesWithPostgresql(connectionString)` in the `UseWolverine()` block registers a
standalone Wolverine inbox/outbox independently of both Marten and Polecat. Both
`AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()` would then be
ancillary to this standalone store.

**Investigation (Wolverine.SqlServer 5.30.0 and Wolverine.Postgresql 5.30.0, XML docs):**

Both APIs exist and have a `MessageStoreRole` parameter:

```csharp
// Wolverine.SqlServer
opts.PersistMessagesWithSqlServer(connectionString, schemaName, MessageStoreRole role);

// Wolverine.Postgresql
opts.PersistMessagesWithPostgresql(connectionString, schemaName, MessageStoreRole role);
```

`MessageStoreRole.Ancillary` exists as an enum value (confirmed via `Wolverine.xml`).

Jeremy Miller's "Wolverine 5 and Modular Monoliths" blog (2025-10-27) confirms this pattern for
**EF Core** alongside Marten:

```csharp
// Marten as main store
opts.Services.AddMarten(m => { m.Connection(postgresql); }).IntegrateWithWolverine();

// SQL Server as ancillary — EF Core integration path
opts.Services.AddDbContextWithWolverineIntegration<SampleDbContext>(x => x.UseSqlServer(sqlserver1));
opts.PersistMessagesWithSqlServer(sqlserver1, role: MessageStoreRole.Ancillary)
    .Enroll<SampleDbContext>();
```

**Why this does NOT apply to Polecat as-is:**

The EF Core pattern works because `AddDbContextWithWolverineIntegration<T>()` does **not** call
`IntegrateWithWolverine()` separately — `PersistMessagesWithSqlServer(Ancillary).Enroll<T>()` is
the entire Wolverine integration for that DbContext. The EF Core DbContext does not claim "main"
status on its own.

Polecat's integration is different. `AddParticipantsModule()` calls
`AddPolecat().IntegrateWithWolverine()`, which unconditionally registers Polecat as a Wolverine
main message store. Calling `opts.PersistMessagesWithSqlServer(..., MessageStoreRole.Ancillary)` in
`UseWolverine()` would add a second SQL Server store (ancillary), while Polecat's
`IntegrateWithWolverine()` remains registered as main. This produces two SQL Server registrations
and does not resolve the original conflict.

Resolving this via Option B would require either:
- Changing `AddParticipantsModule()` to call `AddPolecat()` without `.IntegrateWithWolverine()`,
  then enrolling the Polecat store via `PersistMessagesWithSqlServer(Ancillary).Enroll<IDocumentStore>()`.
  But this approach is not documented, and modifying BC module files is outside this session's scope.
- A Polecat-specific `AddDbContextWithWolverineIntegration<T>()`-equivalent that registers Polecat
  for Wolverine without claiming main status. This API does not exist.

**Decision: rejected.** Option B's API is confirmed for EF Core but is not directly applicable to
Polecat without either a BC module change or a Polecat API gap being filled.

---

### Option C — Deferred: file a JasperFx GitHub discussion

Document the research, add prominent comments to `Program.cs`, and await JasperFx input on how to
configure Polecat as an ancillary Wolverine message store in a mixed Marten + Polecat application.

**Decision: accepted for this session.**

---

## Decision

Defer. The production multi-store conflict cannot be resolved within current API constraints without
modifying BC module files. ADR 010 is in "Proposed — Pending JasperFx input" state.

Prominent comments have been added to both `Program.cs` guard blocks referencing this ADR.

---

## Open Question for JasperFx

**Can `AddPolecat().IntegrateWithWolverine()` be configured to register as an ancillary Wolverine
message store rather than as main?**

Specifically, CritterBids needs the equivalent of Marten's ancillary store path
(`AncillaryWolverineOptionsMartenExtensions.IntegrateWithWolverine<T>()`) for Polecat, or a
`MessageStoreRole` parameter on `PolecatIntegration` that marks the SQL Server store as ancillary
to a primary Marten/PostgreSQL store.

The desired production configuration:

```csharp
// Main Wolverine message store — PostgreSQL via Marten
builder.Services.AddMarten(opts => { ... })
    .IntegrateWithWolverine();   // ← main

// Ancillary Wolverine message store — SQL Server via Polecat
builder.Services.AddParticipantsModule(sqlServerConnectionString);
// which calls:
//   AddPolecat(...).IntegrateWithWolverine(cfg => { cfg.MessageStoreRole = Ancillary; });
//   // or some equivalent — currently this API does not exist
```

---

## Consequences

### Current impact

- `dotnet run --project src/CritterBids.AppHost` still throws `InvalidWolverineStorageConfigurationException`
  when both postgres and sqlserver connection strings are present.
- `dotnet test` is unaffected: each test fixture provisions exactly one backend (ADR 009 §Consequences),
  so both guard conditions are never simultaneously satisfied.
- All 13 tests pass. The development workflow is unblocked for further slice work.

### When resolved

- No BC handler or module files change — only `Program.cs` is modified.
- The `SellingTestFixture` (postgres-only) and `ParticipantsTestFixture` (sqlserver-only) remain
  correct: each fixture provisions exactly one backend and registers exactly one Wolverine store.
- Future Polecat BCs (Settlement, Operations) follow the same pattern as Participants — they will
  work correctly once the ancillary-store resolution is in place.
- If Option B (standalone `PersistMessagesWithSqlServer`) is confirmed, the Wolverine main store
  will be PostgreSQL (matching Marten) and Polecat BCs will use the ancillary SQL Server store.
  All Polecat BC message outboxes will be backed by SQL Server; all Marten BC outboxes by PostgreSQL.
- If an alternative approach is recommended by JasperFx, this ADR will be updated with the chosen
  mechanism and its fixture implications before the fix is implemented.

---

## References

- ADR 009 — Shared Primary Marten Store (§Consequences "Production multi-store conflict is unresolved")
- `src/CritterBids.Api/Program.cs` — both `IntegrateWithWolverine()` guard blocks
- `docs/retrospectives/M2-postS2-adr0002-correction.md` — §What remains: first description of this conflict
- `docs/retrospectives/M2-S3-wolverine-dual-store-resolution-retrospective.md` — research log for this session
- [Wolverine: "Separate" or Ancillary Stores](https://wolverinefx.net/guide/durability/marten/ancillary-stores) — Marten ancillary store API
- [Wolverine 5 and Modular Monoliths](https://jeremydmiller.com/2025/10/27/wolverine-5-and-modular-monoliths/) — EF Core ancillary store pattern
- [Wolverine: SQL Server Integration](https://wolverinefx.net/guide/durability/sqlserver) — `PersistMessagesWithSqlServer` API
- [Wolverine: Polecat Integration](https://wolverinefx.net/guide/durability/polecat/) — Polecat `IntegrateWithWolverine` API surface
