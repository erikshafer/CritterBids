# Event Sourcing with Polecat and Wolverine

> **Status: Complete — filled in from M1 Participants BC implementation (April 2026).**
>
> CritterBids is the first project in this ecosystem to use Polecat. This skill documents confirmed API shapes and known SQL Server differences. Updated with concrete findings from the Participants BC (M1 sessions 4–6).

---

## What Is Polecat?

Polecat is JasperFx's SQL Server-targeting sibling to Marten. It provides event sourcing, projections, and document storage against SQL Server using the same programming model as Marten on PostgreSQL.

As of Polecat 2.0, the goal is near-feature parity with Marten. The aggregate design, `Apply()` method conventions, projection types, Wolverine integration patterns, and `AutoApplyTransactions()` requirement are all shared. Polecat uses **source generators** instead of Marten's runtime code generation, and only supports **System.Text.Json** (no Newtonsoft.Json option).

---

## Why SQL Server for Certain BCs?

CritterBids uses SQL Server via Polecat for **Operations**, **Settlement**, and **Participants** BCs. See `docs/decisions/003-polecat-bcs.md` for the full rationale. The short version:

- **Operations** — projections are directly queryable by Power BI and SQL Server BI tooling
- **Settlement** — financial records belong in SQL Server for audit trail and compliance
- **Participants** — staff-managed data co-located alongside operations

---

## What Transfers Directly from Marten

Treat `docs/skills/marten-event-sourcing.md` as the primary reference. The following patterns transfer directly — **the main change is the namespace: `using Wolverine.Polecat` instead of `using Wolverine.Marten`**:

- Aggregate design — `sealed record`, `Apply()` methods, `with` expressions, status enums
- `@event` parameter naming convention
- Domain event structure — aggregate ID as first property, past-tense naming
- Decider pattern — inline handler logic vs. separate Decider class
- Projection types — inline snapshots, multi-stream projections, async daemon, live aggregation
- `AutoApplyTransactions()` — required in every Polecat BC, identical to Marten
- `Events` class — in `Wolverine.Polecat` namespace (not `Wolverine.Marten`)
- `OutgoingMessages` — unchanged, from core `Wolverine` namespace
- `[WriteAggregate]`, `[ReadAggregate]`, `[AggregateHandler]` — all in `Wolverine.Polecat`
- `[ConsistentAggregateHandler]` — shorthand for `[AggregateHandler]` with `AlwaysEnforceConsistency = true`
- `PolecatOps.StartStream<T>()` / `IStartStream` — direct equivalent of `MartenOps.StartStream<T>()`; all in `Wolverine.Polecat`
- `PolecatOps.Store<T>()`, `Insert<T>()`, `Update<T>()`, `Delete<T>()`, `DeleteWhere<T>()` — full document side-effect set, mirrors `MartenOps`
- `UpdatedAggregate` / `UpdatedAggregate<T>` — return type for HTTP endpoints to respond with post-update state
- `ConcurrencyStyle.Optimistic` / `ConcurrencyStyle.Exclusive` — same enum, same meaning
- `[BoundaryModel]` — in `Wolverine.Polecat`; see DCB section for Polecat-specific details
- Testing strategy — `ExecuteAndWaitAsync`, direct aggregate queries, race condition avoidance
- Event versioning — additive-only changes as default, upcasting for breaking changes
- `opts.Projections.Snapshot<T>(SnapshotLifecycle.Inline)` registration syntax
- `SubscribeToEvents()`, `ProcessEventsWithWolverineHandlersInStrictOrder()`, `PublishEventsToWolverine()` — all present on `PolecatConfigurationExpression`, same as Marten

### HTTP Endpoints

`WolverineFx.Http.Polecat` provides HTTP-specific aliases:

- `[Aggregate]` — alias for `[WriteAggregate]`; use on HTTP endpoint parameters (`Wolverine.Http.Polecat` namespace)
- `[Document]` — equivalent to `[Entity]`; loads a Polecat document by route/parameter ID

---

## Polecat Configuration

### Standard BC Module Pattern

```csharp
// Inside AddXyzModule() extension method — confirmed from Participants BC (M1)
services.AddPolecat(opts =>
{
    opts.ConnectionString = connectionString;                 // ← ✅ confirmed: property form works
    // opts.Connection(connectionString);                     // ← not verified in M1; use ConnectionString property
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate; // ← NOT AutoCreate.All (doesn't exist in Polecat)
    // opts.UseSystemTextJsonForSerialization(...) — ⚠️ absent from Polecat 2.x API; Polecat uses
    // System.Text.Json exclusively and there is no configuration method for serialization format.

    // Schema isolation — one schema per BC
    opts.DatabaseSchemaName = "participants"; // lowercase BC name

    opts.Events.StreamIdentity = StreamIdentity.AsGuid;

    // Inline snapshot projections
    opts.Projections.Snapshot<Participant>(SnapshotLifecycle.Inline);

    // Multi-stream projections
    opts.Projections.Add<ParticipantSummaryProjection>(ProjectionLifecycle.Inline);

    // Async projections — daemon starts automatically, no AddAsyncDaemon() call needed
    opts.Projections.Add<OperationsDashboardProjection>(ProjectionLifecycle.Async);

    // Document indexes for query performance
    opts.Schema.For<Participant>()
        .Identity(x => x.Id)
        .Index(x => x.Email);
})
// ApplyAllDatabaseChangesOnStartup() is on PolecatConfigurationExpression (the builder),
// NOT inside the StoreOptions lambda. Chain it before .IntegrateWithWolverine().
// Required so test fixtures can call CleanAllPolecatDataAsync() before any ORM operation.
.ApplyAllDatabaseChangesOnStartup()  // ← ✅ confirmed; must chain on builder, not inside lambda
.IntegrateWithWolverine();           // ← chains identically to Marten; optional configure callback available
```

```csharp
// In the BC's Wolverine configuration — REQUIRED, same as Marten
opts.Policies.AutoApplyTransactions();        // ← non-negotiable
opts.Policies.UseDurableLocalQueues();
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

### `IntegrateWithWolverine()` — What It Does

```csharp
.IntegrateWithWolverine()
// or with configuration:
.IntegrateWithWolverine(cfg =>
{
    cfg.MessageStorageSchemaName = "wolverine"; // inbox/outbox tables schema; default "wolverine"
    cfg.TransportSchemaName = "wolverine_queues"; // SQL Server transport queues schema; default "wolverine_queues"
})
```

Under the hood this registers a `SqlServerMessageStore` (backed by SQL Server, using the same connection string as the Polecat store) for Wolverine's inbox/outbox. The message storage schema is **separate** from the BC document schema.

### Package References

```bash
# Core Wolverine + Polecat integration
dotnet add package WolverineFx.Polecat

# HTTP endpoint integration (includes WolverineFx.Polecat transitively)
dotnet add package WolverineFx.Http.Polecat

# EF Core projections
dotnet add package Polecat.EntityFrameworkCore
```

### Connection String Differences from Marten

The configuration method is the same (`opts.Connection("...")`), but the connection string format is SQL Server:

```csharp
// Marten (PostgreSQL)
opts.Connection("Host=localhost;Port=5432;Database=critterbids;Username=postgres");

// Polecat (SQL Server)
opts.Connection("Server=localhost;Database=critterbids_participants;User Id=sa;Password=YourStrong!Password;TrustServerCertificate=True");
```

The `ConnectionString` property form (`opts.ConnectionString = connStr`) also works and is used in integration tests.

### Schema Isolation

Polecat defaults to `"dbo"` if `DatabaseSchemaName` is not set. Always set it explicitly per BC:

```csharp
opts.DatabaseSchemaName = "participants"; // lowercase BC name
```

### `AutoCreate` Enum Values

Polecat does **not** have `AutoCreate.All`. Use:

| Value | Behavior |
|---|---|
| `AutoCreate.CreateOrUpdate` | Default — creates new, updates existing (adds columns, never drops) |
| `AutoCreate.CreateOnly` | Only creates, never modifies existing |
| `AutoCreate.None` | No automatic schema management — use in production |

### Async Daemon — No `AddAsyncDaemon()` Chain

Marten requires `.AddAsyncDaemon(DaemonMode.Solo)`. Polecat's daemon **starts automatically** when async projections are registered — there is no `AddAsyncDaemon()` or `DaemonMode` equivalent. Register projections with `ProjectionLifecycle.Async` and the daemon handles itself.

---

## Starting New Streams from Wolverine HTTP Endpoints

`PolecatOps.StartStream<T>()` is a direct equivalent of `MartenOps.StartStream<T>()`. The usage is a straight namespace swap:

```csharp
// Marten
using Wolverine.Marten;
var stream = MartenOps.StartStream<Listing>(listingId, new ListingPublished(...));

// Polecat — exact same pattern, different namespace
using Wolverine.Polecat;
var stream = PolecatOps.StartStream<Participant>(participantId, new ParticipantRegistered(...));
```

Full HTTP endpoint example:

```csharp
[WolverinePost("/api/participants")]
public static (CreationResponse<Guid>, IStartStream) Handle(RegisterParticipant cmd)
{
    var participantId = Guid.CreateVersion7();
    var stream = PolecatOps.StartStream<Participant>(
        participantId,
        new ParticipantRegistered(participantId, cmd.Email, cmd.DisplayName));

    return (new CreationResponse<Guid>($"/api/participants/{participantId}", participantId), stream);
}
```

`PolecatOps` also provides the full document side-effect set via `IPolecatOp` (the Polecat equivalent of `IMartenOp`): `Store<T>`, `Insert<T>`, `Update<T>`, `Delete<T>`, `DeleteWhere<T>`, `Nothing()`.

For existing streams, `[WriteAggregate]` (or the HTTP alias `[Aggregate]`) works identically to Marten.

### Single-Event Return Type — No `Events()` Wrapper ⚠️

**Confirmed from M1 (S6).** When a `[WriteAggregate]` handler appends exactly one domain event, return it
directly as a tuple element — **do not wrap it in `new Events(singleEvent)`**:

```csharp
// ✅ CORRECT — return the domain event type directly in the tuple
public static (IResult, SellerRegistered, OutgoingMessages) Handle(
    RegisterAsSeller cmd,
    [WriteAggregate] Participant participant)
{
    var evt = new SellerRegistered(participant.Id, DateTimeOffset.UtcNow);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new SellerRegistrationCompleted(participant.Id, evt.CompletedAt));
    return (Results.Ok(), evt, outgoing);
}

// ❌ WRONG — CS1503: cannot convert 'SellerRegistered' to 'IEnumerable<object>'
// Events() constructor requires IEnumerable<object>; a single domain event is not enumerable.
return (Results.Ok(), new Events(evt), outgoing);
```

Wolverine/Polecat recognizes the domain event type in the tuple and appends it automatically. The
`Events` collection class is needed only when multiple events are appended from one handler call.

### `[WriteAggregate]` `OnMissing.Simple404` Fires Before `Before()` ⚠️

**Confirmed from M1 (S6).** When no event stream exists for the aggregate ID, Wolverine returns a 404
response **without calling `Before()`**. Consequence: `Before()` is only invoked when the stream
exists — the aggregate parameter is always non-null when `Before()` runs. Declare it non-nullable:

```csharp
// ✅ CORRECT — Before() only runs when aggregate is loaded; non-nullable declaration is safe
public static ProblemDetails Before(RegisterAsSeller cmd, Participant participant) { ... }

// ❌ UNNECESSARY — the null check is unreachable when OnMissing.Simple404 is active
public static ProblemDetails Before(RegisterAsSeller cmd, Participant? participant)
{
    if (participant is null) return new ProblemDetails { Status = 404 }; // Never reached
}
```

The two rejection paths for a `[WriteAggregate]` endpoint are therefore:
1. **Stream missing** → `OnMissing.Simple404` returns 404, `Before()` is never called
2. **Stream exists, precondition fails** → `Before()` returns `ProblemDetails` (e.g., 409)

For multi-event handlers:

```csharp
public static (Events, OutgoingMessages) Handle(
    UpdateParticipant cmd,
    [WriteAggregate] Participant participant)
{
    var events = new Events();
    events.Add(new ParticipantUpdated(participant.Id, cmd.DisplayName));
    var outgoing = new OutgoingMessages();
    outgoing.Add(new Contracts.ParticipantUpdated(participant.Id));
    return (events, outgoing);
}
```

---

## DCB with Polecat

DCB is **confirmed available** in Polecat 2.x. `[BoundaryModel]` is in `Wolverine.Polecat`. The API differs slightly from Marten:

| Operation | Marten | Polecat |
|---|---|---|
| Attribute | `[BoundaryModel]` (`Wolverine.Marten`) | `[BoundaryModel]` (`Wolverine.Polecat`) |
| Tag an event | `wrapped.AddTag(new FooId(id))` | `wrapped.WithTag(fooId, barId)` |
| Read by tags | `QueryByTagsAsync(query)` | `QueryByTagsAsync(query)` — same |
| Fetch for writing | stream-based `FetchForWriting` | `FetchForWritingByTags<T>(query)` |
| Concurrency exception | `EventStreamUnexpectedMaxEventIdException` | `DcbConcurrencyException` |

**`[BoundaryModel]` requires a `Load()` or `Before()` method** returning `EventTagQuery` on the handler class — this is the same requirement as Marten's `[BoundaryModel]`:

```csharp
public static EventTagQuery Load(MyCommand cmd)
    => new EventTagQuery().Or<ParticipantId>(new ParticipantId(cmd.ParticipantId));

public static (Events, OutgoingMessages) Handle(
    MyCommand cmd,
    [BoundaryModel] MyAggregate aggregate)
{
    // aggregate is loaded via FetchForWritingByTags under the hood
}
```

See `docs/skills/dynamic-consistency-boundary.md` for the full DCB implementation checklist.

> **Status:** Settlement, Operations, and Participants BCs do not currently require DCB, so this is not an immediate blocker for MVP.

---

## EF Core Projections with Polecat

The same `EfCoreSingleStreamProjection<T>`, `EfCoreMultiStreamProjection<T>`, and `EfCoreEventProjection<T>` patterns work identically with Polecat. Only the DbContext provider changes:

```csharp
// PostgreSQL (Marten BCs)
services.AddDbContext<ListingsProjectionDbContext>(opts =>
    opts.UseNpgsql(connectionString));

// SQL Server (Polecat BCs)
services.AddDbContext<OperationsProjectionDbContext>(opts =>
    opts.UseSqlServer(connectionString));
```

The projection class itself is **identical** between Marten and Polecat versions.

Optionally use Polecat's Weasel pipeline to manage EF Core entity tables alongside Polecat schema objects:

```csharp
opts.AddEntityTablesFromDbContext<OperationsProjectionDbContext>();
```

---

## SQL Server-Specific Gotchas

### String Collation

SQL Server's default collation (`SQL_Latin1_General_CP1_CI_AS`) is **case-insensitive**. PostgreSQL defaults to case-sensitive. This affects:

- Document ID lookups — `"DOC-001"` and `"doc-001"` are the same key on SQL Server
- LINQ `.Where()` string comparisons
- Unique indexes on string columns

**CritterBids mitigation:** Use GUIDs for all aggregate IDs in Polecat BCs. Normalize any string keys with `.ToUpperInvariant()` or `.ToLowerInvariant()` before storing or querying.

### SQL Server 2025 Native JSON Type

Polecat uses SQL Server 2025's native `json` data type by default. Fall back to `nvarchar(max)` for pre-2025 instances:

```csharp
opts.UseNativeJsonType = false;
```

### Table Naming

Polecat tables use `pc_` prefix in the configured schema:
- `pc_events` — event log
- `pc_streams` — stream metadata and snapshots
- `pc_event_progression` — async daemon progress
- `pc_doc_{typename}` — document tables

### ⚠️ StronglyTypedId Codegen Gap

A known code generation gap exists with `[Entity]` attribute and saga identifiers using StronglyTypedId wrappers. The generated code emits `LoadAsync<T>(strongId)` without unwrapping to the underlying `Guid`.

**CritterBids is not affected** because Polecat BCs use plain `Guid` for all aggregate IDs. If you add a Polecat BC that uses StronglyTypedId on `[Entity]` or saga IDs, verify at runtime.

---

## Testing Event-Sourced Polecat BCs

The same race-condition problem applies: use `ExecuteAndWaitAsync` + direct event store query, not HTTP POST → GET.

### Preferred Wolverine Testing Helpers

`Wolverine.Polecat` provides rich test helpers on `IHost`:

```csharp
// One-liner to clean all Polecat data (documents + events)
await _host.CleanAllPolecatDataAsync();

// Safer reset: pauses async daemon, cleans everything, resumes
await _host.ResetAllPolecatDataAsync();

// Force async projections to catch up immediately (better than WaitForNonStaleProjectionDataAsync for tests)
await _host.ForceAllPolecatDaemonActivityToCatchUpAsync(cancellationToken);

// Get the document store
var store = _host.DocumentStore();
// or typed: var store = _host.DocumentStore<IDocumentStore>();

// Save to Polecat and wait for all outgoing messages to flush
await _host.SaveInPolecatAndWaitForOutgoingMessagesAsync(session =>
{
    session.Events.Append(streamId, new SomeEvent());
});
```

For `TrackedSessionConfiguration` (Wolverine tracked sessions):

```csharp
// Wait for non-stale daemon data after execution
configuration.WaitForNonStaleDaemonDataAfterExecution(TimeSpan.FromSeconds(10));

// Pause daemon before, catch up after
configuration.PauseThenCatchUpOnPolecatDaemonActivity();
```

### Manual Cleanup (if needed directly)

```csharp
// Direct store cleanup — use the host helpers above instead when possible
await store.Advanced.CleanAllDocumentsAsync();
await store.Advanced.CleanAllEventDataAsync();
await store.Advanced.CleanAsync<ParticipantView>(); // specific type
```

---

## Implementation Checklist

Filled in from M1 Participants BC (sessions 4–6). Items not reached in M1 are marked N/A (M1).

- [x] First end-to-end handler with `[WriteAggregate]` + `AutoApplyTransactions()` — ✅ confirmed.
      `RegisterAsSellerHandler` uses `[WriteAggregate]` on the `Participant` aggregate. `AutoApplyTransactions()`
      is configured at host level in `Program.cs`; it applies to all BC handlers.

- [x] `PolecatOps.StartStream<T>()` from Wolverine HTTP endpoint — ✅ confirmed identical to Marten pattern.
      `StartParticipantSessionHandler` uses `PolecatOps.StartStream<Participant>(participantId, evt)` and
      returns `(CreationResponse<Guid>, IStartStream)`. Namespace swap only: `using Wolverine.Polecat`.

- [ ] Inline snapshot registration and `session.LoadAsync<T>()` query verification — N/A (M1).
      Participants has no snapshots. Arrives when a BC with snapshot projections is implemented.

- [ ] Multi-stream projection registration and query verification — N/A (M1).
      Participants has no projections in M1. Arrives with Listings or Operations BC.

- [ ] Async projection + `host.ForceAllPolecatDaemonActivityToCatchUpAsync()` in tests — N/A (M1).

- [x] Test cleanup with `host.CleanAllPolecatDataAsync()` / `host.ResetAllPolecatDataAsync()` — ✅ confirmed.
      `ParticipantsTestFixture` uses `Host.Services.CleanAllPolecatDataAsync()` (note: extension is on
      `IServiceProvider`, not `IHost`). `ResetAllPolecatDataAsync()` is reserved for BCs with async projections;
      not used in M1 because Participants has none.

- [x] `DatabaseSchemaName` isolation — ✅ confirmed via direct SQL query (M1-S7 S4-F4 verification).
      Polecat tables (`pc_events`, `pc_streams`, `pc_event_progression`) land in the `participants` schema.
      Wolverine inbox/outbox tables (`wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`,
      `wolverine_dead_letters`, `wolverine_nodes`, etc.) land in the `wolverine` schema — never `participants`.
      Schema creation DDL is logged by `Wolverine.SqlServer.Persistence.SqlServerMessageStore` at startup,
      which is a useful diagnostic signal.

- [ ] `AddEntityTablesFromDbContext` + `EfCoreSingleStreamProjection` with `UseSqlServer` — N/A (M1).

- [ ] SQL Server collation behavior with any string lookups — N/A (M1).
      Participants uses GUID IDs for all aggregates. Collation behavior is relevant when string columns
      are used for lookups; not encountered in M1.

- [ ] DCB `WithTag()` tagging and `FetchForWritingByTags` if needed — N/A (M1).

- [x] Document any anti-patterns discovered — ✅ see "Single-Event Return Type" and "`OnMissing.Simple404`"
      sections above, and Anti-Pattern #14 in `wolverine-message-handlers.md` (outbox routing rule requirement).

- [x] Update this file with a "Known Gotchas" section — ✅ incorporated inline into relevant sections above
      rather than as a separate section; keeps findings co-located with the patterns they correct.

---

## References

- [Wolverine + Polecat integration](https://wolverinefx.net/guide/durability/polecat/) — primary Wolverine docs
- [Wolverine + Polecat event sourcing](https://wolverinefx.net/guide/durability/polecat/event-sourcing)
- [Event Sourcing and CQRS with Polecat tutorial](https://wolverinefx.net/tutorials/cqrs-with-polecat) — PolecatIncidentService sample
- [Polecat event store](https://polecat.netlify.app/events/) — canonical Polecat docs
- [Polecat bootstrapping](https://polecat.netlify.app/configuration/hostbuilder)
- [Polecat DCB](https://polecat.netlify.app/events/dcb)
- [Polecat EF Core projections](https://polecat.netlify.app/events/projections/efcore)
- [Polecat on NuGet](https://www.nuget.org/packages/Polecat)
- [Polecat.EntityFrameworkCore NuGet](https://www.nuget.org/packages/Polecat.EntityFrameworkCore/)
- `docs/skills/marten-event-sourcing.md` — primary reference for patterns that transfer directly
- `docs/skills/marten-projections.md` — EF Core projection patterns
- `docs/skills/dynamic-consistency-boundary.md` — full DCB implementation guide
- `docs/decisions/003-polecat-bcs.md` — which BCs use Polecat and why
