# Marten Named Stores

> **⚠️ ARCHIVED — 2026-04-14**
>
> CritterBids no longer uses named/ancillary Marten stores. ADR 008 was superseded by
> ADR 009, which establishes a single primary `IDocumentStore` registered in `Program.cs`
> with per-BC `ConfigureMarten()` contributions. This file is retained as reference material
> for the named-store API, which is a real Marten capability appropriate for multi-server
> deployments where each BC must connect to a separate PostgreSQL instance.
>
> **Do not follow the patterns in this file for CritterBids development.** Use the patterns
> in `docs/skills/adding-bc-module.md` and `docs/skills/critter-stack-testing-patterns.md`.

---

Patterns, constraints, and gotchas for the named-store-only architecture.

> **Why this skill existed:** CritterBids originally had no default `IDocumentStore` — only named stores
> registered via `AddMartenStore<T>()` (one per BC, per ADR 008). That decision was superseded
> because it made `IDocumentSession` injection, `AutoApplyTransactions`, `[Entity]`, and
> `IStorageAction<T>` unavailable in all Marten-backed handlers — eliminating the core Critter
> Stack idioms CritterBids exists to showcase.

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

---

## What Named Stores Register vs. Skip

| Feature | Default Store + `IntegrateWithWolverine()` | Named Store (`AddMartenStore<T>()`) |
|---|---|---|
| `IDocumentStore` registered in DI | ✅ | ❌ Not registered |
| `IDocumentSession` injectable in handlers | ✅ Via `SessionVariableSource` | ❌ Not available |
| `[Entity]` attribute auto-loading | ✅ | ❌ No `SessionVariableSource` |
| `IStorageAction<T>` / `MartenOps` return types | ✅ | ❌ No `MartenPersistenceFrameProvider` |
| `AutoApplyTransactions()` wrapping | ✅ | ❌ No session to wrap |
| `[MartenStore]` attribute for inbox routing | Implicit | ✅ Required on every handler |
| Named store injectable via `IBcDocumentStore` | N/A | ✅ |

---

## Handler Patterns for Named-Store BCs

Because `SessionVariableSource` is absent, inject the typed named-store interface and manage
the session lifecycle manually:

```csharp
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

`[MartenStore(typeof(IBcDocumentStore))]` is required even though it doesn't drive parameter
injection — it sets `chain.AncillaryStoreType` for durable inbox/outbox routing.

---

## Module Registration

```csharp
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

The explicit `(StoreOptions opts)` type annotation is required to pick the correct overload
(the compiler otherwise prefers `Func<IServiceProvider, StoreOptions>`).

---

## Test Fixture Override Rules

`services.AddMartenStore<IBcDocumentStore>(...)` in `ConfigureServices` fully replaces the
production registration — `opts.Schema.For<T>()` declarations are lost unless repeated.
`CleanAllMartenDataAsync()` (non-generic) throws because `IDocumentStore` is not registered;
use `CleanAllMartenDataAsync<IBcDocumentStore>()` instead.

---

## References

- `docs/decisions/008-marten-bc-isolation.md` — original rationale (superseded)
- `docs/decisions/009-shared-marten-store.md` — superseding decision
- Marten docs: [Multiple Document Stores](https://martendb.io/configuration/multiple-databases.html)
