# Critter Stack Ancillary Stores

Reference guide for registering ancillary (named) Marten and Polecat stores alongside a primary store. Covers the marker-interface pattern, Wolverine integration, handler routing via `[MartenStore]`, multi-tenancy, and the Polecat parity surface.

> **CritterBids status — not currently used.** Per ADR 009, CritterBids runs a single primary `IDocumentStore` with per-BC `services.ConfigureMarten()` contributions. This skill documents ancillary stores as a **future option** and as reference material for conversations with the JasperFx team where the pattern comes up. Do not introduce `AddMartenStore<T>()` into CritterBids without a superseding ADR.
>
> Read `docs/decisions/009-shared-marten-store.md` and `docs/decisions/011-all-marten-pivot.md` for the current architecture.

---

## Table of Contents

1. [When Ancillary Stores Fit](#when-ancillary-stores-fit)
2. [The Marker Interface Pattern](#the-marker-interface-pattern)
3. [Bootstrap — `AddMartenStore<T>` + `IntegrateWithWolverine`](#bootstrap--addmartenstoret--integratewithwolverine)
4. [The `MessageStorageSchemaName` Requirement](#the-messagestorageschemaname-requirement)
5. [Handler Routing with `[MartenStore]`](#handler-routing-with-martenstore)
6. [Aggregate Handler Workflow in Ancillary Stores](#aggregate-handler-workflow-in-ancillary-stores)
7. [Marten Side Effects in Ancillary Stores](#marten-side-effects-in-ancillary-stores)
8. [Event Subscriptions and Projections Per Store](#event-subscriptions-and-projections-per-store)
9. [Wolverine-Managed Distribution](#wolverine-managed-distribution)
10. [Multi-Tenanted Ancillary Stores](#multi-tenanted-ancillary-stores)
11. [Polecat Ancillary Stores — API Parity](#polecat-ancillary-stores--api-parity)
12. [Capability Matrix](#capability-matrix)
13. [Testing Fixture Patterns](#testing-fixture-patterns)
14. [Anti-Patterns](#anti-patterns)
15. [Decision Guidance](#decision-guidance)
16. [Historical Note — ADR 008 → ADR 009 Context](#historical-note--adr-008--adr-009-context)
17. [References](#references)

---

## When Ancillary Stores Fit

Ancillary stores are the right tool when one of these holds:

| Situation | Why ancillary stores help |
|---|---|
| Modular monolith where BCs must target **separate physical databases** | Each store owns its own connection string; cross-BC queries are physically impossible |
| Multiple logical schemas against the **same PostgreSQL instance**, with per-module projections and daemons | Each ancillary store runs its own async daemon shard with independent projection progress |
| You need per-module **event subscriptions** with distinct advancement cursors | `.SubscribeToEvents()` and `.PublishEventsToWolverine()` scope to the ancillary store |
| You're migrating from a microservice split toward a modular monolith and want to preserve per-service database isolation | Ancillary stores provide schema-level isolation within one process |
| Multi-tenanted scenarios where each tenant has its own database, but envelope tables stay in a shared "control plane" DB | `MainConnectionString` on `IntegrateWithWolverine()` routes inbox/outbox to the control-plane DB |

**Do not** reach for ancillary stores when:

- A single schema per BC (via `opts.Schema.For<T>().DatabaseSchemaName(...)`) would suffice — this is what CritterBids does today.
- You want BC isolation only for code hygiene reasons — per-BC modules with `services.ConfigureMarten()` enforce that without splitting the store.
- You haven't measured the operational cost of multiple async daemons, envelope table sets (if not shared), and projection progress tables. Ancillary stores compound infrastructure.

---

## The Marker Interface Pattern

Each ancillary store is identified by a marker interface extending `IDocumentStore`:

```csharp
// Orders/IOrderStore.cs
public interface IOrderStore : IDocumentStore;

// Inventory/IInventoryStore.cs
public interface IInventoryStore : IDocumentStore;
```

The marker is the DI key for the store instance, the type argument to `[MartenStore(typeof(IOrderStore))]`, and the type parameter for `IConfigureMarten<T>` module contributions. One marker per logical store — naming convention is `I<Module>Store` or `I<Module>DocumentStore`.

---

## Bootstrap — `AddMartenStore<T>` + `IntegrateWithWolverine`

Register each store individually, and call `IntegrateWithWolverine()` on each builder expression:

```csharp
builder.Host.UseWolverine(opts =>
{
    // See §4 — critical for modular monoliths
    opts.Durability.MessageStorageSchemaName = "wolverine";

    opts.Policies.AutoApplyTransactions();
    opts.Durability.Mode = DurabilityMode.Balanced;
});

// Primary store — provides SessionVariableSource, MartenPersistenceFrameProvider,
// and MartenOpPolicy registrations that ancillary stores redirect through.
builder.Services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "public";
    })
    .IntegrateWithWolverine();

builder.Services.AddMartenStore<IOrderStore>(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "orders";      // isolated schema, same physical DB
    })
    .IntegrateWithWolverine();

builder.Services.AddMartenStore<IInventoryStore>(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "inventory";
    })
    .IntegrateWithWolverine();

// Create/migrate all schemas on startup
builder.Services.AddResourceSetupOnStartup();
```

**The primary-store requirement is load-bearing.** `SessionVariableSource` — the mechanism that enables `IDocumentSession` injection into handlers, `[Entity]` attribute loading, `IStorageAction<T>`/`MartenOps` return types, and `AutoApplyTransactions()` — is registered by the primary `AddMarten(...)` call. Ancillary stores redirect through that registration when a handler is tagged with `[MartenStore]` (see §5). **Without a primary store, ancillary handlers lose access to these idioms** and must fall back to injecting the marker interface and calling `store.LightweightSession()` manually — the pattern that ADR 008 codified for CritterBids before ADR 009 corrected course.

### Overload selection

Be explicit with the lambda parameter type to pick the `StoreOptions` overload rather than the `Func<IServiceProvider, StoreOptions>` overload:

```csharp
services.AddMartenStore<IOrderStore>((StoreOptions opts) =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "orders";
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();
```

Without the explicit `StoreOptions` annotation, the compiler can silently select the `IServiceProvider`-taking overload and the configuration lambda won't be invoked at the expected lifecycle stage.

### The `String` vs lambda overloads

`AddMartenStore<T>(Action<StoreOptions>)` is the usual entry point. A second overload takes a connection string directly (`AddMartenStore<T>(string connectionString)`) and is a convenience for the simple case. Prefer the lambda form so `DatabaseSchemaName`, projection registrations, and schema contributions all live in one place.

---

## The `MessageStorageSchemaName` Requirement

Set `opts.Durability.MessageStorageSchemaName` in `UseWolverine(...)` when registering more than one ancillary store against the same physical database. Without it, every store provisions its own `wolverine_*` envelope tables, wasting resources and complicating cleanup.

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.Durability.MessageStorageSchemaName = "wolverine";   // one shared envelope schema
});
```

Set once at the Wolverine host level. Not per-store. Not per-BC.

---

## Handler Routing with `[MartenStore]`

Without `[MartenStore]`, Wolverine routes a handler's `IDocumentSession` parameter to the **primary** store — silently. A handler intended for the orders schema will quietly write to `public`. Always tag the handler class (or individual method) with the target store's marker type.

```csharp
[MartenStore(typeof(IOrderStore))]
public static class PlaceOrderHandler
{
    // IDocumentSession here is a lightweight session from IOrderStore
    public static IMartenOp Handle(PlaceOrder command)
        => MartenOps.Store(new Order { Id = command.OrderId, Items = command.Items });
}

[MartenStore(typeof(IInventoryStore))]
public static class ReserveInventoryHandler
{
    public static IMartenOp Handle(ReserveInventory cmd, IDocumentSession session)
    {
        var product = session.Load<Product>(cmd.ProductId);
        return MartenOps.Store(new Reservation { ProductId = cmd.ProductId, Qty = cmd.Qty });
    }
}
```

### What `[MartenStore]` actually does

The attribute sets `chain.AncillaryStoreType = typeof(IOrderStore)` on the generated handler chain and inserts an `AncillaryOutboxFactoryFrame` at the head of the middleware chain. Wolverine's code generation then resolves `IDocumentSession` from the ancillary store instead of the primary, and outbox messages are enrolled on the ancillary store's inbox/outbox tables.

The attribute is required — Wolverine does **not** infer which store to use based on document types, namespaces, or assembly conventions. Tag every handler class that targets an ancillary store.

### Class-level vs method-level

`AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)` — both work. Class-level is the common case; method-level is appropriate when a single handler class mixes primary-store and ancillary-store handlers. Prefer one handler class per store to keep it simple.

---

## Aggregate Handler Workflow in Ancillary Stores

`[WriteAggregate]` and `[ReadAggregate]` work identically in ancillary stores when the enclosing class carries `[MartenStore]`:

```csharp
[MartenStore(typeof(IOrderStore))]
public static class ShipOrderHandler
{
    public static OrderShipped Handle(
        ShipOrder cmd,
        [WriteAggregate] IEventStream<Order> order)
    {
        if (order.Aggregate.Status != "Placed")
            throw new InvalidOperationException("Order is not in Placed status");

        order.AppendOne(new OrderShipped(cmd.OrderId, DateTime.UtcNow));
        return new OrderShipped(cmd.OrderId, DateTime.UtcNow);
    }
}
```

Optimistic concurrency, `FetchForWriting`, `IEventStream<T>` access, and the `(Events, OutgoingMessages)` tuple return shape all behave as they do against the primary store.

---

## Marten Side Effects in Ancillary Stores

`IMartenOp` return types (`MartenOps.Store`, `MartenOps.Insert`, `MartenOps.StartStream`, etc.) route to the correct store automatically when `[MartenStore]` is set:

```csharp
[MartenStore(typeof(IInventoryStore))]
public static class StockReceivedHandler
{
    public static IMartenOp Handle(StockReceived @event)
        => MartenOps.Store(new StockEntry { ProductId = @event.ProductId, Qty = @event.Qty });
}
```

No per-store `MartenOps` variant exists — the attribute handles the dispatch.

---

## Event Subscriptions and Projections Per Store

Each ancillary store runs its own async daemon with independent projections and subscriptions. Register projections inside the `AddMartenStore<T>()` lambda; register subscriptions on the builder expression:

```csharp
builder.Services.AddMartenStore<IOrderStore>(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "orders";

        opts.Projections.Add<OrderSummaryProjection>(ProjectionLifecycle.Async);
    })
    .IntegrateWithWolverine()
    // Per-store event subscription — your own IWolverineSubscription implementation
    .SubscribeToEvents(new OrderAuditSubscription())
    // Or forward specific event types to Wolverine message handlers
    .PublishEventsToWolverine("OrderEvents", x =>
    {
        x.PublishEvent<OrderPlaced>();
        x.PublishEvent<OrderShipped>();
    });
```

Each ancillary store maintains its own progression table (`mt_event_progression` under that store's schema). Rebuilding projections, pausing subscriptions, and daemon sharding all scope to the store.

---

## Wolverine-Managed Distribution

Load distribution across ancillary stores is handled automatically when the primary store opts in:

```csharp
builder.Services.AddMarten(/* main store */)
    .IntegrateWithWolverine(m =>
    {
        m.UseWolverineManagedEventSubscriptionDistribution = true;
    });

builder.Services.AddMartenStore<IOrderStore>(/* ... */)
    .IntegrateWithWolverine();   // inherits distribution from the main store
```

Ancillary stores inherit the distribution strategy from the primary. Each store's shards are assigned across cluster nodes via Wolverine's leader-election, independent of the other stores.

---

## Multi-Tenanted Ancillary Stores

Ancillary stores support per-tenant databases. When tenant data lives in separate databases but inbox/outbox envelope tables should stay in a "control plane" database, set `MainConnectionString`:

```csharp
builder.Services.AddMartenStore<IThingStore>(opts =>
    {
        opts.MultiTenantedDatabases(tenancy =>
        {
            tenancy.AddSingleTenantDatabase(tenant1ConnectionString, "tenant1");
            tenancy.AddSingleTenantDatabase(tenant2ConnectionString, "tenant2");
        });
        opts.DatabaseSchemaName = "things";
    })
    .IntegrateWithWolverine(x =>
    {
        // Envelope tables live in the main store's database, not in tenant DBs
        x.MainConnectionString = mainConnectionString;
    });
```

Without `MainConnectionString`, Wolverine attempts to provision envelope tables in each tenant database, which is almost never what you want.

---

## Polecat Ancillary Stores — API Parity

Polecat 1.1+ ships the equivalent ancillary-store surface in `Wolverine.Polecat`:

- `AddPolecatStore<T>()` — register a typed ancillary Polecat store
- `IConfigurePolecat<T>` — per-store configuration hook (parallel to Marten's `IConfigureMarten<T>`)
- `PolecatStoreExpression<T>` — builder expression returned by `AddPolecatStore<T>()`
- `.IntegrateWithWolverine()` — enables transactional outbox, aggregate handler workflow, side effects
- `.SubscribeToEvents()`, `.PublishEventsToWolverine()`, `.ProcessEventsWithWolverineHandlersInStrictOrder()` — per-store event distribution

```csharp
public interface IOrderStore : IDocumentStore;

builder.Services.AddPolecatStore<IOrderStore>((StoreOptions opts) =>
{
    opts.Connection(sqlServerConnectionString);
    opts.DatabaseSchemaName = "orders";
    opts.Projections.Add<OrderSummaryProjection>(ProjectionLifecycle.Async);
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();
```

### Handler routing for Polecat ancillary stores — open question

As of Wolverine 5.x, `[MartenStore]` is the Marten-specific routing attribute (`Wolverine.Marten` namespace, inserts `AncillaryOutboxFactoryFrame` from `Wolverine.Marten.Codegen`). There is no corresponding `[PolecatStore]` attribute in `Wolverine.Polecat` at the time of writing. The Polecat ancillary handler story currently appears to rely on direct store injection:

```csharp
public static class PlaceOrderHandler
{
    public static async Task Handle(PlaceOrder cmd, IOrderStore store)
    {
        await using var session = store.LightweightSession();
        session.Store(new Order { Id = cmd.OrderId, Items = cmd.Items });
        await session.SaveChangesAsync();
    }
}
```

This mirrors the pre-ADR-009 CritterBids handler shape — functional, but without `AutoApplyTransactions`, `[Entity]`, or `IStorageAction<T>`. Verify the current Polecat ancillary handler story with the JasperFx team before committing to an architecture that depends on attribute-driven routing for Polecat stores. The ai-skills repo does not yet document this surface.

---

## Capability Matrix

| Feature | Marten ancillary | Polecat ancillary |
|---|---|---|
| Transactional inbox/outbox via `IntegrateWithWolverine()` | ✅ | ✅ |
| `AutoApplyTransactions()` across ancillary handlers | ✅ (with primary store + `[MartenStore]`) | ⚠️ verify — no `[PolecatStore]` attribute |
| `[WriteAggregate]` / `[ReadAggregate]` | ✅ (with `[MartenStore]`) | ⚠️ verify routing |
| `IMartenOp` / `MartenOps.*` return types | ✅ (with `[MartenStore]`) | N/A — Polecat uses the same API under a different namespace; verify routing |
| `IStorageAction<T>` return types | ✅ (with primary store + `[MartenStore]`) | ⚠️ verify |
| `[Entity]` attribute auto-loading | ✅ (with primary store + `[MartenStore]`) | ⚠️ verify |
| Event subscriptions per store | ✅ | ✅ |
| Projections and async daemon per store | ✅ | ✅ |
| Wolverine-managed shard distribution | ✅ | ✅ (inherits from main) |
| Conjoined multi-tenancy | ✅ | ✅ |
| Multi-tenancy via separate databases | ✅ with `MainConnectionString` | ✅ with `MainConnectionString` |
| Multiple ancillary stores per handler | ❌ Not yet | ❌ Not yet |
| Custom `IDocumentSessionListener` per session | ❌ Not yet | ❌ Not yet |
| PostgreSQL/SQL Server messaging transport spanning ancillary DBs | ❌ Not yet (same DB works) | ❌ Not yet |

Entries marked ⚠️ are unverified and should be confirmed with JasperFx team or by reading the Wolverine.Polecat source before committing to a design that depends on them.

---

## Testing Fixture Patterns

`services.AddMartenStore<T>()` calls in a test fixture's `ConfigureServices` **fully replace** the production registration. Any `opts.Schema.For<T>()` or projection registrations declared in the production `AddMartenStore<T>()` are lost unless repeated in the fixture override.

### Cleanup APIs are store-typed

`CleanAllMartenDataAsync()` (non-generic) operates on the primary `IDocumentStore`. If the primary store isn't registered — or you need to clean a specific ancillary store — use the typed form:

```csharp
// Clean every document in the IOrderStore schema
await _host.CleanAllMartenDataAsync<IOrderStore>();

// Reset (clears + restarts projections) for a specific ancillary store
await _host.ResetAllMartenDataAsync<IOrderStore>();
```

`CleanAllMartenDataAsync()` without a type parameter throws if `IDocumentStore` (the primary) isn't registered in the fixture. This is the gotcha that bit CritterBids under ADR 008 — the fixtures had no primary store at all.

### Fixture cross-BC isolation

When a test fixture registers only a subset of Marten BCs (e.g., a Participants-only fixture that excludes the Selling module), handler discovery may surface Marten-backed handlers from other BC assemblies. Either:

1. Register all Marten BC modules in the fixture (slower but simple), or
2. Exclude specific handler assemblies via `IWolverineExtension` in the fixture's `ConfigureServices`.

See `docs/skills/critter-stack-testing-patterns.md` for CritterBids' current cross-BC isolation pattern under the single-primary-store architecture.

---

## Anti-Patterns

### Injecting `IOrderStore` and calling `SaveChangesAsync()` in a Wolverine handler

```csharp
// ❌ BAD: bypasses Wolverine's transactional middleware — outbox is NOT used.
// Messages published during this handler will not be durably outboxed with the write.
[MartenStore(typeof(IOrderStore))]
public static class CreateOrderHandler
{
    public static async Task Handle(CreateOrder cmd, IOrderStore store)
    {
        await using var session = store.LightweightSession();
        session.Store(new Order { Id = cmd.OrderId });
        await session.SaveChangesAsync();  // bypasses outbox
    }
}

// ✅ GOOD: IMartenOp return type — outbox is used, transaction is atomic
[MartenStore(typeof(IOrderStore))]
public static class CreateOrderHandler
{
    public static IMartenOp Handle(CreateOrder cmd)
        => MartenOps.Store(new Order { Id = cmd.OrderId });
}

// ✅ GOOD: injected IDocumentSession — Wolverine owns the lifecycle and outbox
[MartenStore(typeof(IOrderStore))]
public static class CreateOrderHandler
{
    public static void Handle(CreateOrder cmd, IDocumentSession session)
    {
        session.Store(new Order { Id = cmd.OrderId });
        // AutoApplyTransactions commits atomically; do NOT call SaveChangesAsync
    }
}
```

Raw `store.LightweightSession()` + `SaveChangesAsync()` is fine for **query services** and **seeding code** outside the handler pipeline. It is never the right pattern inside a Wolverine handler tagged with `[MartenStore]`.

### Missing `[MartenStore]` — silent routing to the primary store

```csharp
// ❌ BAD: without [MartenStore], Wolverine routes IDocumentSession to the PRIMARY store.
// This handler writes Orders to the "public" schema, not "orders". No error, no warning.
public static class CreateOrderHandler
{
    public static IMartenOp Handle(CreateOrder cmd)
        => MartenOps.Store(new Order { Id = cmd.OrderId });
}

// ✅ GOOD: explicit [MartenStore] on the class
[MartenStore(typeof(IOrderStore))]
public static class CreateOrderHandler
{
    public static IMartenOp Handle(CreateOrder cmd)
        => MartenOps.Store(new Order { Id = cmd.OrderId });
}
```

**Verification:** `dotnet run -- wolverine-diagnostics codegen-preview --message CreateOrder` will show exactly which store the generated session variable resolves to. Use this to diagnose when documents end up in the wrong schema.

### Forgetting `MessageStorageSchemaName` in a modular monolith

```csharp
// ❌ BAD: each store provisions its own wolverine_* envelope tables.
// Wastes connection pool capacity, complicates cleanup, fragments outbox queries.
builder.Services.AddMartenStore<IOrderStore>(opts => { ... }).IntegrateWithWolverine();
builder.Services.AddMartenStore<IInventoryStore>(opts => { ... }).IntegrateWithWolverine();
// → orders.wolverine_*, inventory.wolverine_*, and public.wolverine_* all exist

// ✅ GOOD: one shared envelope schema across all stores
builder.Host.UseWolverine(opts =>
{
    opts.Durability.MessageStorageSchemaName = "wolverine";   // single envelope schema
});
```

### Using `AddMartenStore<T>` without a primary `AddMarten()` in the same process

This is the ADR 008 configuration. Feasible, but eliminates `IDocumentSession` injection, `[Entity]`, `AutoApplyTransactions`, and `IStorageAction<T>` across every handler. Handlers fall back to direct store injection + manual session lifecycle + explicit `SaveChangesAsync()`. If the goal is BC isolation within a single database, prefer `AddMarten()` + per-BC `ConfigureMarten()` (CritterBids' current approach) unless the scenarios in §1 actually apply.

### Registering ancillary stores without `IntegrateWithWolverine()`

The ancillary store will function as a plain Marten store, but:
- No transactional outbox for messages published during handlers targeting that store
- No aggregate handler workflow wired through Wolverine
- No Wolverine-managed daemon distribution

Always chain `IntegrateWithWolverine()` on the `AddMartenStore<T>()` expression if any Wolverine handler will target that store.

---

## Decision Guidance

| Situation | Recommendation |
|---|---|
| Single app, single schema | Primary `AddMarten()` only; no ancillary stores |
| Modular monolith, BCs share one PostgreSQL instance, code hygiene is the goal | Primary `AddMarten()` + per-BC `services.ConfigureMarten()` with `opts.Schema.For<T>().DatabaseSchemaName(...)` per type — CritterBids' current pattern under ADR 009 |
| Modular monolith, BCs need independent async daemons or event subscriptions | Ancillary stores with `MessageStorageSchemaName` set |
| Modular monolith, BCs target separate physical databases | Ancillary stores with per-store connection strings |
| Per-tenant databases with shared control plane | Ancillary stores with `MainConnectionString` set on `IntegrateWithWolverine()` |
| Query-only access to an ancillary store from a service class | Inject the marker interface directly, use `LightweightSession()` |
| Write access from a Wolverine handler | `[MartenStore(typeof(IXyzStore))]` + injected `IDocumentSession` or `IMartenOp` return |
| Polecat ancillary stores for non-trivial Wolverine integration | Verify routing attribute story with JasperFx team before committing (§11) |

---

## Historical Note — ADR 008 → ADR 009 Context

CritterBids originally adopted the ancillary-store pattern per ADR 008 (2026-04-something), registering each Marten BC via `AddMartenStore<IBcDocumentStore>()` with **no primary `AddMarten()` call**. The intent was BC code isolation at the DI type level.

The practical consequence, identified during M2-S2 implementation: without a primary store, `SessionVariableSource`, `MartenPersistenceFrameProvider`, and `MartenOpPolicy` were never registered. Every CritterBids handler was forced to inject the marker store directly, open sessions manually, and commit explicitly. `AutoApplyTransactions()`, `[Entity]`, `IStorageAction<T>`, and `[WriteAggregate]` all became inoperative.

ADR 009 (2026-04-14) superseded ADR 008 by establishing a single primary `IDocumentStore` with per-BC `services.ConfigureMarten()` contributions. All standard Critter Stack idioms were restored.

**What the original archived skill (`marten-named-stores.md`) captured** was the constrained handler shape ADR 008 imposed — injecting the marker store, manual session lifecycle, no `AutoApplyTransactions`. That material is **historically accurate for the ADR 008 era** but does not reflect the current capability of ancillary stores **when used alongside a primary store**. A primary store provides the `SessionVariableSource` registration; the `[MartenStore]` attribute then redirects session resolution to the ancillary store without losing any of the idioms. This is the pattern documented in §5.

The decision to supersede ADR 008 remains correct for CritterBids — even with a primary store added, ancillary stores would add operational complexity (multiple daemon shards, separate projection progress tables, coordination overhead) that CritterBids' scope does not require. This skill exists as forward-looking reference, not as a recommendation.

---

## References

- `docs/decisions/008-marten-bc-isolation.md` — original ancillary-store adoption (superseded)
- `docs/decisions/009-shared-marten-store.md` — superseding decision; current architecture
- `docs/decisions/011-all-marten-pivot.md` — all-Marten BC architecture
- `docs/skills/adding-bc-module.md` — canonical BC module registration under ADR 009
- `docs/skills/critter-stack-testing-patterns.md` — fixture patterns, cross-BC isolation
- Wolverine docs: [Ancillary Marten Stores](https://wolverine.netlify.app/guide/durability/marten/ancillary-stores.html)
- Marten docs: [Multiple Document Stores](https://martendb.io/configuration/multiple-databases.html)
- JasperFx ai-skills: `marten/advanced/ancillary-stores.md`
