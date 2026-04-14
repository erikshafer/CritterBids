# Marten Named Stores

Patterns, constraints, and gotchas for the named-store-only architecture used in CritterBids.

> **Why this skill exists:** CritterBids has no default `IDocumentStore` — only named stores
> registered via `AddMartenStore<T>()` (one per BC, per ADR 0002). This is an intentional
> architectural choice for BC isolation, but it means many patterns described in upstream
> Critter Stack docs — `[Entity]`, `IStorageAction<T>`, injectable `IDocumentSession`,
> `AutoApplyTransactions` — are **unavailable** in Marten-backed BCs without a main store.
> This skill is the single authoritative reference for working within those constraints.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [What Named Stores Register vs. Skip](#what-named-stores-register-vs-skip)
3. [Handler Patterns for Named-Store BCs](#handler-patterns-for-named-store-bcs)
4. [Module Registration](#module-registration)
5. [Test Fixture Override Rules](#test-fixture-override-rules)
6. [Cleanup API Reference](#cleanup-api-reference)
7. [Common Errors and Root Causes](#common-errors-and-root-causes)

---

## Architecture Overview

Each Marten-backed BC defines a named store interface that extends `IDocumentStore`:

```csharp
// e.g. CritterBids.Selling/ISellingDocumentStore.cs
public interface ISellingDocumentStore : IDocumentStore { }
```

This interface is the BC's DI key. Registered via `AddMartenStore<ISellingDocumentStore>()` in
the BC's module class, it provides schema isolation (each BC owns a dedicated PostgreSQL schema)
and prevents cross-BC data access at the type level.

**Key implication:** `IDocumentStore` is **not** registered in the DI container. Any code that
resolves `IDocumentStore` will throw `InvalidOperationException` at runtime.

See `docs/decisions/0002-marten-bc-isolation.md` for the full architectural rationale.

---

## What Named Stores Register vs. Skip

This table shows what `AddMarten().IntegrateWithWolverine()` (the default store path) provides
versus what `AddMartenStore<T>().IntegrateWithWolverine()` (the named store path) provides.

| Feature | Default Store + `IntegrateWithWolverine()` | Named Store (`AddMartenStore<T>()`) |
|---|---|---|
| `IDocumentStore` registered in DI | ✅ | ❌ Not registered |
| `IDocumentSession` injectable in handlers | ✅ Via `SessionVariableSource` | ❌ Not available |
| `[Entity]` attribute auto-loading | ✅ | ❌ No `SessionVariableSource` |
| `IStorageAction<T>` / `MartenOps` return types | ✅ | ❌ No `MartenPersistenceFrameProvider` |
| `IMartenOp` return type in handlers | ✅ | ❌ |
| `AutoApplyTransactions()` wrapping | ✅ (hooks into `IDocumentSession`) | ❌ No session to wrap |
| `[MartenStore]` attribute for inbox routing | ✅ (on main store, implicit) | ✅ **Required** — sets `AncillaryStoreType` |
| Named store injectable via `IBcDocumentStore` | N/A | ✅ |
| `ApplyAllDatabaseChangesOnStartup()` | ✅ | ✅ Must be chained explicitly |
| `UseLightweightSessions()` | Recommended | ✅ Must be chained explicitly |

**The declarative persistence patterns described in `docs/skills/wolverine-message-handlers.md`
assume a default store with `IntegrateWithWolverine()`.** In CritterBids' named-store setup,
use the patterns in the [Handler Patterns](#handler-patterns-for-named-store-bcs) section below.

---

## Handler Patterns for Named-Store BCs

### The correct pattern: inject `IBcDocumentStore`, open session explicitly

Because `SessionVariableSource` is absent, Wolverine cannot inject `IDocumentSession` as a
handler parameter. The generated handler code has no way to satisfy that dependency.

Instead, inject the typed named-store interface and manage the session lifecycle manually:

```csharp
// ❌ WRONG — IDocumentSession is NOT injectable in a named-store-only setup.
// Wolverine will throw a code-gen error at startup: no variable source for IDocumentSession.
[MartenStore(typeof(ISellingDocumentStore))]
public static class SellerRegistrationCompletedHandler
{
    public static async Task Handle(
        SellerRegistrationCompleted message,
        IDocumentSession session)  // ← throws at code-gen time
    {
        // ...
    }
}

// ✅ CORRECT — inject the named store, open a lightweight session, commit explicitly.
[MartenStore(typeof(ISellingDocumentStore))]
public static class SellerRegistrationCompletedHandler
{
    public static async Task Handle(
        SellerRegistrationCompleted message,
        ISellingDocumentStore store)
    {
        await using var session = store.LightweightSession();
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
        await session.SaveChangesAsync();
    }
}
```

### Why `[MartenStore]` is still required

Even though `[MartenStore]` does not drive parameter injection in named-store-only setups, it
is still required on every handler class. It sets `chain.AncillaryStoreType` on the Wolverine
handler chain, which ensures that durable inbox/outbox routing uses this BC's named PostgreSQL
schema rather than the main store's schema.

Drop it and you'll silently route inbox records to the wrong schema — no compile-time error,
no obvious runtime error, just incorrect transactional atomicity. The comment in
`SellerRegistrationCompletedHandler.cs` documents this fully.

**Rule: Every handler class in a named-store BC must carry `[MartenStore(typeof(IBcDocumentStore))]`.**

### Event-sourced aggregates in named-store handlers

For handlers that append events to an event stream, the same pattern applies — no `[WriteAggregate]`
or automatic session injection:

```csharp
[MartenStore(typeof(IAuctionsDocumentStore))]
public static class SomeEventHandler
{
    public static async Task Handle(
        SomeCommand cmd,
        IAuctionsDocumentStore store)
    {
        await using var session = store.LightweightSession();

        var aggregate = await session.Events.AggregateStreamAsync<Listing>(cmd.ListingId);
        if (aggregate is null) return;

        // Business logic...
        session.Events.Append(cmd.ListingId, new SomeEvent(cmd.ListingId));
        await session.SaveChangesAsync();
    }
}
```

> **Note:** The `[WriteAggregate]` attribute and the `Events` return type both require
> `MartenPersistenceFrameProvider`, which is only registered on the default store path.
> Manual `session.Events.Append()` + `session.SaveChangesAsync()` is the correct pattern here.
> See Anti-Pattern #8 in `docs/skills/wolverine-message-handlers.md` for why returning a tuple
> when loading manually is a silent failure.

### Why `AutoApplyTransactions()` doesn't help

`AutoApplyTransactions()` (configured in `Program.cs` via `opts.Policies.AutoApplyTransactions()`)
hooks into the Wolverine middleware chain by detecting `IDocumentSession` or `ILightweightSession`
dependencies in the handler's generated code. With no `SessionVariableSource` registered,
the middleware chain never fires for named-store handlers. Sessions opened manually inside the
handler body are also outside the middleware scope — `SaveChangesAsync()` must be called explicitly.

---

## Module Registration

### Overload resolution gotcha

`AddMartenStore<T>()` has two overloads: `Action<StoreOptions>` and `Func<IServiceProvider, StoreOptions>`.
The compiler prefers the `Func<>` overload in some contexts, causing cryptic registration errors.
Always annotate the lambda parameter type explicitly:

```csharp
// ❌ Can cause the wrong overload to be selected
services.AddMartenStore<ISellingDocumentStore>(opts =>
{
    opts.Connection(connectionString);
});

// ✅ Explicit type annotation picks the correct Action<StoreOptions> overload
services.AddMartenStore<ISellingDocumentStore>((StoreOptions opts) =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "selling";
    opts.Schema.For<RegisteredSeller>();
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();
```

### Full module registration example

```csharp
// SellingModule.cs
public static IServiceCollection AddSellingModule(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var connectionString = configuration["ConnectionStrings:critterbids-postgres"];

    if (string.IsNullOrEmpty(connectionString))
    {
        // No PostgreSQL present — skip registration.
        // This guards test fixtures that don't provision this BC's infrastructure.
        return services;
    }

    services.AddMartenStore<ISellingDocumentStore>((StoreOptions opts) =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "selling";     // BC owns this schema exclusively
        opts.Schema.For<RegisteredSeller>();     // Register all document types here
    })
    .UseLightweightSessions()
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();

    return services;
}
```

---

## Test Fixture Override Rules

### `AddMartenStore<T>()` replaces the production registration entirely

When a test fixture calls `services.AddMartenStore<ISellingDocumentStore>(...)` inside
`ConfigureServices`, it **fully replaces** the production registration made in `AddSellingModule()`.
This means:

- The connection string is overridden ✅ (intended)
- All `opts.Schema.For<T>()` registrations are **lost** ❌ (unless repeated)
- `ApplyAllDatabaseChangesOnStartup()` will not create tables for unregistered document types
- `CleanAllMartenDataAsync<T>()` will not clean unregistered document types

**Rule: Repeat all `opts.Schema.For<T>()` declarations in the test fixture's `AddMartenStore<T>()` call.**

```csharp
// In SellingTestFixture.cs ConfigureServices:
services.AddMartenStore<ISellingDocumentStore>((Marten.StoreOptions opts) =>
{
    opts.Connection(postgresConnectionString);
    opts.DatabaseSchemaName = "selling";

    // ⚠️ Must repeat ALL document type registrations from SellingModule.cs.
    // The production registration is replaced — these are not inherited.
    opts.Schema.For<RegisteredSeller>();
    // Add any other document types introduced in future slices here.
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup()
.IntegrateWithWolverine();
```

### The `StoreOptions` disambiguation

Test fixtures that have both `Marten` and `Polecat` namespaces in scope must qualify
`StoreOptions` to avoid ambiguity (both Marten and Polecat define a `StoreOptions` type):

```csharp
using Marten;
using Polecat;  // also in scope in fixtures that test both

// ✅ Fully qualified to avoid CS0229 ambiguity
services.AddMartenStore<ISellingDocumentStore>((Marten.StoreOptions opts) =>
{
    opts.Connection(postgresConnectionString);
    // ...
});
```

### `ConfigureAppConfiguration` timing caveat

`ConfigureAppConfiguration` in test fixtures runs during `WebApplication.CreateBuilder()` —
**before** any module registration that reads `IConfiguration`. This is the correct place
to inject connection strings that gate module registration:

```csharp
// ✅ Correct: module registration reads this connection string during Program.cs execution
builder.ConfigureAppConfiguration((_, config) =>
{
    config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:critterbids-postgres"] = postgresConnectionString
    });
});
```

However, `ConfigureAppConfiguration` runs **before** `ConfigureServices`. If you also override
the Marten store in `ConfigureServices`, the store registered by `AddSellingModule()` (via
`ConfigureAppConfiguration`) will be replaced by the `ConfigureServices` override — which is
the intended behavior. The `ConfigureAppConfiguration` step is still required to get
`AddSellingModule()` past its null-connection-string guard.

---

## Cleanup API Reference

### `CleanAllMartenDataAsync<T>()` — typed overload required

The non-generic `IAlbaHost.CleanAllMartenDataAsync()` resolves `IDocumentStore` internally.
Because CritterBids has no default `IDocumentStore`, this throws:

```
InvalidOperationException: No service for type 'Marten.IDocumentStore' has been registered.
```

Always use the typed overload:

```csharp
// ❌ THROWS — resolves IDocumentStore, which is not registered
await Host.CleanAllMartenDataAsync();

// ✅ CORRECT — targets the named store directly
await Host.CleanAllMartenDataAsync<ISellingDocumentStore>();
```

The same rule applies to `ResetAllMartenDataAsync<T>()`.

Encapsulate in the fixture helper so test classes never call the wrong overload:

```csharp
// In SellingTestFixture.cs
public Task CleanAllMartenDataAsync() =>
    Host.CleanAllMartenDataAsync<ISellingDocumentStore>();

public Task ResetAllMartenDataAsync() =>
    Host.ResetAllMartenDataAsync<ISellingDocumentStore>();
```

### Full cleanup API reference

| Method | Use | Named-store overload |
|---|---|---|
| `Host.CleanAllMartenDataAsync<T>()` | Delete all docs + event streams | Required |
| `Host.ResetAllMartenDataAsync<T>()` | Pause daemon, clear, restart | Required |
| `store.WaitForNonStaleProjectionDataAsync(timeout)` | Wait for async projections | Resolved via `GetRequiredService<IBcDocumentStore>()` |

---

## Common Errors and Root Causes

### "No service for type 'Marten.IDocumentStore'"

You called a method (cleanup helper, seeding helper, extension method) that internally
resolves `IDocumentStore`. Use the typed overload or resolve `IBcDocumentStore` directly.

### Wolverine code-gen error: "no variable source for IDocumentSession"

A handler parameter of type `IDocumentSession` was declared in a named-store handler.
`SessionVariableSource` is only registered by `AddMarten().IntegrateWithWolverine()`, which
is never called in CritterBids. Replace with `IBcDocumentStore` injection.

### Inbox records routed to wrong schema / durable messaging inconsistency

`[MartenStore(typeof(IBcDocumentStore))]` is missing from a handler class. Without it,
Wolverine doesn't know which store's inbox/outbox schema to use. Add the attribute to every
handler class in named-store BCs.

### Document table not found / `CleanAllMartenDataAsync` doesn't clean a type

A document type was registered in the production `AddMartenStore<T>()` call in the module
class but not in the test fixture's `AddMartenStore<T>()` override. The test override replaces
the production registration entirely. Repeat `opts.Schema.For<T>()` for every document type
in the fixture.

---

## References

- `docs/decisions/0002-marten-bc-isolation.md` — architectural rationale for named stores
- `docs/skills/wolverine-message-handlers.md` — handler patterns (see Anti-Pattern #15 for named-store-only)
- `docs/skills/critter-stack-testing-patterns.md` — fixture override patterns, cleanup helpers
- `src/CritterBids.Selling/SellerRegistrationCompletedHandler.cs` — canonical named-store handler example
- `src/CritterBids.Selling/SellingModule.cs` — canonical module registration example
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs` — canonical test fixture override
- Marten docs: [Multiple Document Stores](https://martendb.io/configuration/multiple-databases.html)
