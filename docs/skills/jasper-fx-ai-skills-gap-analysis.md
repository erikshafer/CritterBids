# JasperFx AI Skills — CritterBids Gap Analysis

**Date:** 2026-04-14
**Source:** `C:\Code\JasperFx\ai-skills` (Jeremy Miller's public skill repo)
**Scope:** Comparison of JasperFx canonical patterns against CritterBids skill files and M2 planning decisions

---

## Summary

The JasperFx ai-skills repo confirms the majority of CritterBids' established patterns but surfaces several gaps — some of which must be addressed before M2-S2 runs, and others that should be folded into skill file updates during M2-S7 or later. Nothing in the ai-skills contradicts a hard architectural decision, but several patterns CritterBids has not documented are load-bearing for correctness in a modular monolith.

---

## 1. Must Address Before M2-S2 Runs

These items directly affect the code S2 will write. The S2 prompt should be amended or the agent should receive a brief addendum.

### 1a. `MultipleHandlerBehavior.Separated` + `MessageIdentity.IdAndDestination` — Missing from Program.cs

**Source:** `architecture/modular-monolith.md` — the three critical settings

In a modular monolith where multiple BCs handle the same integration event type (e.g., `ListingPublished` will eventually be consumed by Listings, Settlement, and Auctions BCs — all in the same process), Wolverine's default `ClassicCombineIntoOneLogicalHandler` behaviour combines all handlers for the same message type into one logical handler queue. This is wrong for CritterBids. The correct setting is:

```
opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination;
```

`MessageIdentity.IdAndDestination` is critical: without it, the durable inbox deduplicates messages by ID alone, which means when the same message fans out to multiple handler-specific queues, only the first handler runs.

**Action:** S2's `Program.cs` changes section already adds `MessageStorageSchemaName`. Add these two settings to the same Wolverine configuration block. They belong at the host level, not in any BC module.

### 1b. `UseLightweightSessions()` — Missing from Marten Module Pattern

**Source:** `architecture/new-project-wolverine-marten.md`, `architecture/modular-monolith.md`

`.UseLightweightSessions()` chains on the `AddMartenStore<T>()` builder. It disables identity map overhead, which is the recommended session type for all Marten BC modules. Not setting this means all sessions use the heavier identity-mapped session by default.

**Action:** The `AddSellingModule()` pattern in S2 should chain `.UseLightweightSessions()` between `AddMartenStore<T>()` and `.ApplyAllDatabaseChangesOnStartup()`.

### 1c. `app.RunJasperFxCommands(args)` vs `app.Run()` — Check Program.cs

**Source:** All `architecture/new-project-*.md` files

The canonical Wolverine bootstrap ends with `return await app.RunJasperFxCommands(args)` instead of `app.Run()`. This enables the `db-apply`, `db-assert`, `db-dump`, and `codegen` CLI commands. If CritterBids currently uses `app.Run()`, S2 should correct it. Without `RunJasperFxCommands`, schema management commands don't work.

**Action:** S2 prompt should instruct the agent to check and correct if needed.

### 1d. `public partial class Program { }` — Check Program.cs

**Source:** `architecture/new-project-wolverine-marten.md`

Required at the bottom of `Program.cs` so test projects can reference `Program` for `AlbaHost.For<Program>()`. May already be present from M1; should be verified.

### 1e. `services.RunWolverineInSoloMode()` — Missing from Test Fixture Pattern

**Source:** `wolverine/testing/integration-testing-wolverine-marten.md`

The canonical test fixture for Wolverine + Marten calls both:
- `services.DisableAllExternalWolverineTransports()` (already in CritterBids Participants fixture)
- `services.RunWolverineInSoloMode()` (missing)

`RunWolverineInSoloMode()` prevents advisory lock contention during test restarts. The S2 `SellingTestFixture` should include both.

### 1f. `CleanAllMartenDataAsync()` API Confirmed

**Source:** `wolverine/testing/integration-testing-wolverine-marten.md`

This directly answers S2's open question. The Marten cleanup API is:
- `await _host.CleanAllMartenDataAsync()` — deletes all documents and event streams (simple test cleanup between test classes)
- `await _host.ResetAllMartenDataAsync()` — disables async projections, clears data, restarts projections (use when testing async projections)

Both are extension methods on `IAlbaHost` from the `Marten` namespace. S2 fixture should expose `CleanAllMartenDataAsync()`.

### 1g. Package Name Confirmed: `WolverineFx.Marten` for S2, `WolverineFx.Http.Marten` for S4

**Source:** `architecture/new-project-wolverine-marten.md`

`WolverineFx.Http.Marten` is a single package that transitively brings in `WolverineFx.Http`, `WolverineFx.Marten`, `WolverineFx`, and `Marten`. For S2 (scaffold only, no HTTP endpoints), reference `WolverineFx.Marten`. For S4 (first HTTP endpoints), upgrade the reference to `WolverineFx.Http.Marten` — this single change brings in the HTTP integration.

> **All `WolverineFx.*` packages must use the same version.** Mixing versions causes restore failures and runtime errors.

---

## 2. Address During M2 Sessions (S3–S6)

### 2a. RabbitMQ Aspire Connection Pattern

**Source:** `integrations/wolverine-rabbitmq.md`

Aspire provides a URI, not a traditional connection string. The Aspire-compatible RabbitMQ connection is:

```csharp
opts.UseRabbitMqUsingNamedConnection("rabbit").AutoProvision();
```

Our `integration-messaging.md` shows a manual factory configuration. S3 (first RabbitMQ wiring) should use `UseRabbitMqUsingNamedConnection`. The Aspire resource name is `"rabbit"` (or whatever name was used in `AppHost/Program.cs` — verify against `CritterBids.AppHost`).

### 2b. `IWolverineExtension` for BC Wolverine Configuration

**Source:** `architecture/modular-monolith.md`

The official modular monolith pattern uses `IWolverineExtension` for Wolverine-specific BC configuration (queue routing, handler behavior), registered via `opts.Include<XyzModuleExtension>()` inside `UseWolverine()`. Our `AddXyzModule()` extension method handles `IServiceCollection` registration (Marten, DI services) — the Wolverine-specific parts (publish/subscribe rules) could live in an `IWolverineExtension` for cleaner separation.

This is a pattern refinement, not a hard error. Our current approach works. However, the `adding-bc-module.md` skill being written in S7 should document both approaches and explain why each concern lives where it does.

### 2c. `[Aggregate]` vs `[WriteAggregate]` in HTTP+Marten Handlers

**Source:** `wolverine/http/http-marten-integration.md`

For S4 (first Marten HTTP endpoints), the correct HTTP+Marten patterns are:
- `MartenOps.StartStream<T>()` + `CreationResponse` — creating new aggregate (Slice 1.1)
- `[Aggregate]` parameter + event return + `[EmptyResponse]` — mutating existing aggregate (Slice 1.2+)
- `[WriteAggregate]` + `IEventStream<T>` — lower-level mutation with explicit `AppendOne()` 

The key difference: `[Aggregate]` on a parameter loads the aggregate for mutation. `[WriteAggregate]` loads via `IEventStream<T>` and requires explicit `AppendOne()`. `[Aggregate]` + event return is simpler and preferred.

**Important:** `[EmptyResponse]` suppresses the event type from becoming the HTTP response body. Without it, the returned domain event is serialized as the HTTP response — almost never what you want.

---

## 3. M2-S7 Skills Pass — Update/Author

### 3a. `marten-event-sourcing.md` — Missing Performance Patterns

Patterns not yet in the CritterBids skill file (from `architecture/new-project-wolverine-marten.md`):
- `opts.Events.AppendMode = EventAppendMode.Quick` — ~50% throughput improvement, recommended for greenfield
- `opts.Events.UseMandatoryStreamTypeDeclaration = true` — future-proofs for rebuild optimizations; requires aggregate type registration
- `opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true` — allows marking problematic events for projection skipping
- `opts.Projections.UseIdentityMapForAggregates = true` — eliminates extra DB round trip with `FetchForWriting`
- `opts.Projections.EnableAdvancedAsyncTracking = true` — improved async projection progress tracking
- `.UseLightweightSessions()` on the Marten builder

### 3b. `critter-stack-testing-patterns.md` — Missing Marten Cleanup APIs

- `CleanAllMartenDataAsync()` vs `ResetAllMartenDataAsync()` (distinction documented above in §1f)
- `services.RunWolverineInSoloMode()` required alongside `DisableAllExternalWolverineTransports()`
- `services.MartenDaemonModeIsSolo()` — for async projection tests
- `store.WaitForNonStaleProjectionDataAsync(TimeSpan)` — for waiting on async projections
- `IDocumentStore.LightweightSession()` for seeding (NOT `GetRequiredService<IDocumentSession>()`)

### 3c. `wolverine-message-handlers.md` — Missing Patterns

- `Validate` / `ValidateAsync` method pattern (business rule validation separate from `Handle`)
- `Configure(HandlerChain chain)` method for per-handler error handling configuration
- `[Entity]` attribute for auto-loading entities by ID
- Prefer `ILogger` over `ILogger<T>` in handlers

### 3d. `integration-messaging.md` — Missing Modular Monolith Settings

- `MultipleHandlerBehavior.Separated` — required for multiple BCs handling same message type
- `MessageIdentity.IdAndDestination` — required for correct fanout deduplication
- RabbitMQ Aspire connection pattern: `UseRabbitMqUsingNamedConnection("rabbit")`

### 3e. `adding-bc-module.md` — New Skill (Written in S7)

This skill should cover:
- `AddMartenStore<IBcDocumentStore>()` with `.UseLightweightSessions().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()`
- `[MartenStore(typeof(IBcDocumentStore))]` on all handlers
- `IWolverineExtension` as the optional Wolverine-config companion
- `MessageStorageSchemaName` — once in Program.cs, not per BC
- `MultipleHandlerBehavior.Separated` and `MessageIdentity.IdAndDestination` — once in Program.cs
- Test fixture override for named Marten stores
- `RunWolverineInSoloMode()` in all Marten BC test fixtures

---

## 4. Confirmed / No Change Needed

| Pattern | Verdict |
|---|---|
| `opts.Policies.AutoApplyTransactions()` in every BC | ✅ Confirmed by all ai-skills |
| `[AllowAnonymous]` through M5, real auth at M6 | ✅ Not contradicted |
| `OutgoingMessages` for integration event publishing | ✅ Confirmed |
| `[WriteAggregate]` attribute — same API for Marten and Polecat | ✅ Confirmed by polecat skill |
| `sealed record` for all commands/events/queries | ✅ Confirmed |
| ADR 008 named stores + `[MartenStore]` attribute | ✅ Confirmed by modular-monolith.md |
| `MessageStorageSchemaName = "wolverine"` | ✅ Confirmed by modular-monolith.md |
| `DisableAllExternalWolverineTransports()` in test fixtures | ✅ Confirmed (supplemented by `RunWolverineInSoloMode()`) |
| UUID v7 for Marten BC stream IDs | ✅ Not contradicted |
| Queue naming `<consumer>-<publisher>-<category>` | ✅ Not contradicted |
| `AutoProvision()` on RabbitMQ | ✅ Confirmed (with development caveat) |

---

## 5. Deferred / Future Milestones

| Item | When Relevant |
|---|---|
| `CritterStackDefaults()` for dev vs production code gen / schema modes | Before production deploy — M_deploy or post-MVP |
| `opts.Durability.EnableInboxPartitioning = true` | Before high-load testing |
| `UseWolverineManagedEventSubscriptionDistribution` replaces `AddAsyncDaemon(HotCold)` | M3+ (async projections) |
| `store.WaitForNonStaleProjectionDataAsync()` for async projection tests | M3+ (first async projections) |
| `wolverine-diagnostics codegen-preview` and `describe` commands | Ongoing (add to observability/debugging notes) |
| `UpdatedAggregate` return type for HTTP responses | M3+ (Auctions BC slices) |
| Quorum queues for production RabbitMQ | Pre-deploy |
| `opts.Events.AppendMode = EventAppendMode.Quick` | Can add in S2 or S3 when first Marten module is configured |

---

## 6. Action Summary for S2

The S2 prompt should be amended (or the agent given an addendum note) covering:

1. Add to Program.cs `UseWolverine()` block:
   - `opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated`
   - `opts.Durability.MessageIdentity = MessageIdentity.IdAndDestination`
2. Chain `.UseLightweightSessions()` on `AddMartenStore<T>()` in `AddSellingModule()`
3. Check/add `return await app.RunJasperFxCommands(args)` in Program.cs
4. Check/add `public partial class Program { }` at bottom of Program.cs
5. Add `services.RunWolverineInSoloMode()` to `SellingTestFixture`
6. Fixture cleanup method: use `CleanAllMartenDataAsync()` (from `Marten` namespace on `IAlbaHost`)
7. Reference `WolverineFx.Marten` package (not `WolverineFx.Http.Marten` — that's S4)
