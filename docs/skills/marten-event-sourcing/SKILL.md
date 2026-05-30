---
name: marten-event-sourcing
description: "Event sourcing with Marten in CritterBids: UUID v7 stream identity, schema-per-BC, the single-AddMarten host pattern, AutoApplyTransactions placement, and the identity-map mutation footgun. Use when designing an event-sourced aggregate or wiring a BC's Marten store."
cluster: marten
tags: [marten, event-sourcing, aggregates, configuration, projections]
---

# Event Sourcing with Marten

> CritterBids event-sourcing conventions on top of the Critter Stack.
> Generic Marten aggregate, projection, and daemon mechanics live in the ai-skills `marten-*` family;
> **this skill documents only the CritterBids-specific stream-identity, configuration, and footgun decisions.**

## When to apply this skill

Use this skill when:

- Designing or wiring an event-sourced aggregate in any CritterBids BC.
- Registering a BC's Marten store (`ConfigureMarten` in an `AddXyzModule()`).
- Choosing a stream-identity scheme (UUID v7 vs a deterministic key).
- A handler returns HTTP 200 but the events table stays empty, or aggregate fields mutate "by themselves".

Do NOT use this skill for: handler/endpoint shape (see `wolverine-message-handlers`),
projection deep-dives (see `marten-projections`), or DCB tagged writes (see `dynamic-consistency-boundary`).

## Read upstream first

Generic Marten mechanics are fully covered upstream. Read these ai-skills (license required; install via
`npx skills add`) before this skill — they cover ~80% of event sourcing:

1. `marten-aggregate-handler-workflow` — `FetchForWriting`, `[WriteAggregate]`, `ProjectLatest`, return tuples.
2. `marten-projections-single-stream` / `marten-projections-multi-stream` — projection authoring.
3. `marten-advanced-cross-stream-operations` — multiple `[WriteAggregate]`, `VersionSource`, `[ConsistentAggregate]`.
4. `marten-advanced-async-daemon-deep-dive` — daemon modes, distribution, error handling, rebuilds.
5. `marten-advanced-optimization` — append modes, snapshot strategy, identity map.

This skill picks up at the CritterBids decisions: stream identity, host wiring, and the anti-patterns this
codebase hit.

## Stream identity — CritterBids convention

All eight BCs are Marten/PostgreSQL (ADR 011 — All-Marten Pivot).

- **Default: UUID v7** (`Guid.CreateVersion7()`), generated at entity creation. No natural business key exists
  in most contexts; UUID v7's Unix-ms prefix gives insert locality (ADR 007). No namespace constant needed —
  stream IDs are minted at creation with no cross-handler coordination.
- **UUID v5** (deterministic, SHA-1 over a BC-specific namespace + natural key) remains available **only** where
  a natural business key lets multiple handlers resolve the same stream without a lookup. Reach for it
  deliberately; it is not the default.
- **One event stream per aggregate instance**; **Stream ID = Aggregate ID**.
- **Schema-per-BC:** each BC claims a PostgreSQL schema (`auctions`, `listings`, …); the shared `mt_events` /
  `mt_streams` tables live in the host root schema.

```csharp
[WolverinePost("/api/listings")]
public static (CreationResponse<Guid>, IStartStream) Handle(PublishListing cmd)
{
    var listingId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Listing>(listingId, new ListingPublished(listingId, ...));
    return (new CreationResponse<Guid>($"/api/listings/{listingId}", listingId), stream);
}
```

## Host wiring — the single-store pattern

CritterBids uses **one** primary `IDocumentStore`, registered **once** in `Program.cs`. Every BC contributes
its types via `services.ConfigureMarten()` inside its `AddXyzModule()`. Never call `AddMarten()` or
`AddMartenStore<T>()` from inside a BC module (ADR 009, extended by ADR 011).

```csharp
// Inside AddAuctionsModule() — no connection string, no store provisioning
public static IServiceCollection AddAuctionsModule(this IServiceCollection services)
{
    services.ConfigureMarten(opts =>
    {
        opts.Schema.For<Listing>().DatabaseSchemaName("auctions");      // document tables isolated
        opts.Schema.For<Listing>().Identity(x => x.Id).Index(x => x.SellerId);
        opts.Projections.Snapshot<Listing>(SnapshotLifecycle.Inline);   // consistent, no daemon
        opts.Projections.Add<CatalogListingViewProjection>(ProjectionLifecycle.Inline);
        opts.Projections.UseIdentityMapForAggregates = true;
    });
    services.AddTransient<IAuctionPricingService, AuctionPricingService>();
    return services;
}
```

```csharp
// Program.cs — exactly one AddMarten(), inside the postgres null guard
builder.Services.AddMarten(opts =>
{
    opts.Connection(postgresConnectionString);
    opts.DatabaseSchemaName = "public";
    opts.Events.AppendMode = EventAppendMode.Quick;                 // CritterBids default
    opts.Events.UseMandatoryStreamTypeDeclaration = true;
    opts.DisableNpgsqlLogging = true;
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();

builder.Services.AddSellingModule();   // all Marten BC modules registered here
```

### `AutoApplyTransactions()` lives in `UseWolverine()`, not in any BC

It is a **global** Wolverine policy registered once. Every BC's handlers inherit it; you must **not** repeat
it inside a BC's `ConfigureMarten()`.

```csharp
builder.UseWolverine(opts =>
{
    opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;     // BC isolation
    opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;   // fanout dedup
    opts.Durability.MessageStorageSchemaName = "wolverine";
    opts.Policies.AutoApplyTransactions();                               // ← global, never per-BC
});
```

## CritterBids anti-patterns (hard-won)

### ❌ Missing `AutoApplyTransactions()` ⚠️ CRITICAL

Without it, handlers do not commit Marten changes — **silent** failure. **Diagnosis:** handler returns
HTTP 200, events table empty, no exceptions → confirm `AutoApplyTransactions()` is in `Program.cs`'s
`UseWolverine()` block. Corollary: never call `SaveChangesAsync()` in a handler — it's redundant; remove it.

### ❌ Two `AddMarten()` calls / `AddMartenStore<T>()` per BC

A second `AddMarten()` registers a competing `IDocumentStore` and silently discards the first BC's config.
The fix is **not** `AddMartenStore<T>()` (which loses `IDocumentSession` injection and `AutoApplyTransactions`)
— it's the single-store pattern above (ADR 009).

> `[MartenStore]` attributes are **not** required under the shared-store model — `IDocumentSession` is injected
> by `SessionVariableSource` from the single primary store. The attribute was an ADR 008 (superseded)
> named-store constraint; it returns only if ancillary stores are introduced for multi-server deployments.

### ❌ DCB boundary state missing `Guid Id`

Marten registers boundary-state classes as documents. Without `public Guid Id { get; set; }`,
`CleanAllMartenDataAsync()` throws during test cleanup, cascading failures. See `dynamic-consistency-boundary`.

### ❌ Mutating aggregates cached under `UseIdentityMapForAggregates`

With the identity map on, Marten caches aggregate snapshots in the session. Any mutation to a cached
aggregate is persisted on the next commit — even if the code "just logged" a value.

```csharp
// ❌ mutation persisted silently
var listing = await session.Events.FetchLatest<Listing>(listingId);
listing.Title = listing.Title.ToUpperInvariant();   // someone's "harmless" transform
await session.SaveChangesAsync();                    // title is now uppercase in the DB

// ✅ project to a DTO for ad-hoc reads; leave the aggregate untouched
var display = new ListingDisplay(Title: listing.Title.ToUpperInvariant(), ...);
```

Treat identity-mapped aggregates as **read-only** outside the projection pipeline and aggregate-handler chain.

## Lessons learned

CritterBids-specific findings worth carrying forward:

- **L1 — Verify integration queue wiring end-to-end.** Handler tests aren't enough; a BC can publish an
  event that never reaches its subscriber because the queue binding is missing. Add cross-BC smoke tests.
- **L2 — Design integration events for all known consumers.** Too-thin payloads force a contract expansion
  per new consumer. Document consumers when designing the payload.
- **L3 — HTTP-based testing ignores eventual consistency.** `POST → GET` races flake on CI/under load. Use
  direct command invocation for state-changing operations (see `critter-stack-testing-patterns`).
- **L4 — Sagas must handle every terminal state.** Handling `ReturnCompleted` but not `ReturnRejected`
  leaves dangling state forever. Every terminal path calls `MarkCompleted()`.
- **L5 — Document-based saga vs event-sourced aggregate.** Sagas are write-heavy/read-light → document store
  with numeric revisions. Event-source domain aggregates where history matters.
- **L9 — `MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination` must both be set.** Without
  `Separated`, multiple BC handlers for one message type collapse into one queue (isolation broken); without
  `IdAndDestination`, fanout dedup silently drops some BC handlers.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `marten-aggregate-handler-workflow`, `marten-projections-single-stream`, `marten-projections-multi-stream`,
  `marten-advanced-cross-stream-operations`, `marten-advanced-async-daemon-deep-dive`, `marten-advanced-optimization`.
- `critterstack-arch-new-project-wolverine-marten` — bootstrap reference.

**Prerequisites:**

- `wolverine-message-handlers` — handler shape and the outbox routing rule.

**Downstream:**

- `marten-projections` — read-model and snapshot deep-dive.
- `dynamic-consistency-boundary` — tagged event writes across streams.
- `critter-stack-testing-patterns` — testing event-sourced systems without race conditions.

**External:**

- ADR 007 (UUID v7 stream IDs), ADR 009 (shared primary store), ADR 011 (All-Marten Pivot) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Canonical Bootstrap Sequence.
