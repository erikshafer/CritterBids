# M2-S3: RegisteredSellers Consumer

**Milestone:** M2 — Listings Pipeline
**Slice:** S3 — RegisteredSellers consumer + Program.cs routing
**Agent:** @PSA
**Estimated scope:** one PR, ~5 new files + 3 file modifications

## Goal

Wire `SellerRegistrationCompleted` from the Participants BC into the Selling BC. At session close:
the `RegisteredSeller` Marten document exists, `SellerRegistrationCompletedHandler` processes
arriving messages and upserts `RegisteredSeller` rows, `ISellerRegistrationService` exposes a
registration check for S4's API gate, and `Program.cs` replaces the M1 local-queue placeholder
with real RabbitMQ routing. Four projection integration tests confirm the projection writes and
the service query both behave correctly.

No commands, no domain events, no HTTP endpoints, no event stream operations — only a message
handler, a document, a service seam, and routing wiring. This session is the prerequisite for S4.

## Context to load

- `docs/milestones/M2-listings-pipeline.md` — §2 (S3 scope table), §5 (RabbitMQ routing changes
  table), §6 (conventions: `ISellerRegistrationService` seam, UUID strategy, records)
- `docs/retrospectives/M2-S2-selling-bc-scaffold-retrospective.md` — **primary baseline reference.
  Read in full before writing any code.** Establishes the actual `AddSellingModule()` shape and
  three deviations from skill documentation that govern all S3 work (see §Deviations below).
  The "What remains" section is the direct S3 handoff.
- `docs/skills/adding-bc-module.md` — BC module registration; see §Deviations for overrides
- `docs/skills/marten-event-sourcing.md` — `[MartenStore]` on handlers, document registration;
  see §Deviations for `AutoApplyTransactions` override
- `docs/skills/critter-stack-testing-patterns.md` — Marten BC fixture pattern, `ExecuteAndWaitAsync`,
  `GetDocumentSession()`
- `docs/skills/integration-messaging.md` — queue naming convention, `PublishMessage<T>()`,
  `ListenToRabbitQueue()`; see §Deviations for routing placement override
- `docs/skills/wolverine-message-handlers.md` — handler patterns, `[MartenStore]` attribute

## Deviations to carry forward from S2

The S2 retrospective documents four deviations from the skill documentation that apply to all S3
work. These override the skill file examples where they conflict. Reading the retro in full is
mandatory — this section is a summary, not a substitute.

**Deviation 1 — `AddSellingModule()` uses early-return, not throw.**
When the PostgreSQL connection string is absent from `IConfiguration`, `AddSellingModule()` returns
`services` immediately (no-op). The `adding-bc-module.md` skill shows a throw pattern — do not
apply it here. The early-return is intentional and documented inside `SellingModule.cs`. Do not
change this behaviour in S3.

**Deviation 2 — `opts.Policies.AutoApplyTransactions()` does not belong inside the Marten lambda.**
Both `adding-bc-module.md` and `marten-event-sourcing.md` show `opts.Policies.AutoApplyTransactions()`
inside the `AddMartenStore<T>()` `StoreOptions` lambda. This is a factual error in the skill
documentation: `AutoApplyTransactions()` is a `WolverineOptions.Policies` extension method, not a
`Marten.StoreOptions` method. Placing it inside the Marten lambda produces a compiler error.
The global `opts.Policies.AutoApplyTransactions()` in `Program.cs`'s `UseWolverine()` block already
covers all BC handlers. Do not add it to the Marten lambda when extending `AddSellingModule()`.

**Deviation 3 — `AddMartenStore<T>()` overload requires explicit type annotation.**
`WolverineFx.Marten` adds a `Func<IServiceProvider, StoreOptions>` overload that the compiler
prefers over the `Action<StoreOptions>` overload when no explicit type is given. Any lambda
passed to `AddMartenStore<ISellingDocumentStore>()` must annotate its parameter explicitly as
`(StoreOptions opts) =>`. The existing S2 code already uses this form — do not regress it. In
test fixture code where both `Marten` and `Polecat` namespaces are in scope, the fully-qualified
form `(Marten.StoreOptions opts) =>` is required.

**Deviation 4 — RabbitMQ routing rules go in `Program.cs`, not in `AddSellingModule()`.**
The `integration-messaging.md` skill shows `PublishMessage<T>()` and `ListenToRabbitQueue()` being
called from inside `AddXyzModule()`. In CritterBids, `AddSellingModule()` receives only
`IServiceCollection` — it has no access to `WolverineOptions`. All RabbitMQ routing rules live
in `Program.cs`'s `UseWolverine()` block, which is the established pattern confirmed by the M1
`SellerRegistrationCompleted` placeholder already present there. S3 follows this pattern.

## In scope

### `src/CritterBids.Selling/` — additions to existing project

**`RegisteredSeller.cs`**

Marten document representing a seller who has completed registration in the Participants BC.
Required property: `public Guid Id { get; set; }` — the document ID, set from the `SellerId`
(i.e. `ParticipantId`) carried by `SellerRegistrationCompleted`.

This is a projection document, not a domain aggregate — it has no `Apply()` methods and no
event stream. Keep it minimal: include only fields that `ISellerRegistrationService` needs to
answer the registration query. If no additional fields beyond `Id` are required, the class can
be minimal; document the decision in the retrospective.

**`SellerRegistrationCompletedHandler.cs`**

Wolverine handler for `CritterBids.Contracts.Participants.SellerRegistrationCompleted`.

This handler must carry `[MartenStore(typeof(ISellingDocumentStore))]`. Without this attribute,
Wolverine does not route the injected session to the Selling BC's named store — the default
`IDocumentStore` is not registered in this process (ADR 008), so the handler will fail at
runtime with no compile-time warning. Verify the attribute is present before committing.

The handler upserts a `RegisteredSeller` document. It must be naturally idempotent — processing
the same `SellerRegistrationCompleted` message a second time must produce no error and no
duplicate document. Use the simplest approach that achieves idempotency.

Do not use `IMessageBus` directly. Do not return `OutgoingMessages` from this handler — there are
no downstream integration events to publish in S3.

**`ISellerRegistrationService.cs`**

Interface with a single method: given a seller ID, return whether that seller is registered.
Signature: `Task<bool> IsRegisteredAsync(Guid sellerId, CancellationToken ct = default)`.

This interface is the module seam described in M2 §6. `CritterBids.Api` will inject it via DI in
S4 to gate the `CreateDraftListing` endpoint. The interface belongs in `CritterBids.Selling`;
it is not a contracts type.

**`SellerRegistrationService.cs`**

Concrete implementation of `ISellerRegistrationService`. Resolves the Selling BC's named store
(`ISellingDocumentStore`) to create a query session on demand. Use `IQuerySession` for the
read-only lookup; `LoadAsync<RegisteredSeller>(sellerId, ct)` is preferable to a LINQ query for
an identity-key lookup.

The implementation must not inject `IDocumentStore` (not registered) or `IDocumentSession`
(write session — incorrect for a read-only service). Inject `ISellingDocumentStore` directly.

### `src/CritterBids.Selling/SellingModule.cs` — additions

Two additions to the body of `AddSellingModule()`. Both preserve all S2 code exactly as-is.

**Document registration inside the `AddMartenStore<ISellingDocumentStore>()` options lambda.**
Register `RegisteredSeller` as a Marten document so that `session.Query<RegisteredSeller>()` is
valid from startup. Consult `marten-event-sourcing.md` for the correct `StoreOptions` API.
Maintain the explicit `(StoreOptions opts) =>` lambda annotation established in S2.

Do not add `opts.Policies.AutoApplyTransactions()` — see Deviation 2 above.

**Service registration after the `AddMartenStore<T>()` chain.** Register
`ISellerRegistrationService` → `SellerRegistrationService` in the `IServiceCollection`.
Choose a lifetime appropriate to a service that creates sessions on demand from a singleton store.

### `src/CritterBids.Api/Program.cs` — three changes

**Add Selling BC assembly to Wolverine handler discovery.**
`UseWolverine()` currently includes `opts.Discovery.IncludeAssembly(typeof(Participant).Assembly)`
for the Participants BC. Without an equivalent call for `CritterBids.Selling`,
`SellerRegistrationCompletedHandler` will not be discovered by Wolverine at startup. Add
`opts.Discovery.IncludeAssembly()` for the Selling assembly using any stable exported type from
`CritterBids.Selling` as the anchor. Place it alongside the existing Participants discovery call.

**Remove the M1 local-queue placeholder.**
The `UseWolverine()` block contains a `opts.Publish(...)` call routing `SellerRegistrationCompleted`
to a local queue, with a comment marking it as a temporary M1 placeholder. Remove this call and
its accompanying comment entirely.

**Add real RabbitMQ routing inside the existing guard block.**
The existing guard checks for the `rabbitmq` connection string before calling `opts.UseRabbitMq(...)`.
Both new routing declarations must live inside this same guard so they are only applied when the
transport is configured:

- Publish `SellerRegistrationCompleted` to the queue `selling-participants-events`
  (queue name follows `<consumer>-<publisher>-<category>` from `integration-messaging.md`)
- Subscribe to `selling-participants-events` with inline processing

Use the `PublishMessage<T>().ToRabbitQueue(...)` and `ListenToRabbitQueue(...).ProcessInline()`
forms documented in `integration-messaging.md`. If the API signatures differ from what is shown
there, verify via Context7 and flag in the retrospective.

The `SellerRegistrationCompleted` type lives in `CritterBids.Contracts.Participants` — confirm
the using directive is present.

### `tests/CritterBids.Selling.Tests/` — new test file

**`RegisteredSellersProjectionTests.cs`**

New test class inside the existing `CritterBids.Selling.Tests` project. Uses the `SellingTestFixture`
and `SellingTestCollection` established in S2. The test class implements `IAsyncLifetime` and calls
`_fixture.CleanAllMartenDataAsync()` in `InitializeAsync()`.

The `SellingTestFixture` already provisions both PostgreSQL and SQL Server containers — do not
modify the fixture. The two-container baseline is intentional and documented in the S2 retrospective.

All four tests invoke `SellerRegistrationCompleted` via `_fixture.ExecuteAndWaitAsync(...)`.
Do not use HTTP for projection tests — see `critter-stack-testing-patterns.md` §Event Sourcing
Race Conditions. Query state via `_fixture.GetDocumentSession()` or by resolving
`ISellerRegistrationService` from `_fixture.Host.Services`.

| Test method | Scenario | What to assert |
|---|---|---|
| `SellerRegistrationCompleted_CreatesRegisteredSellerRow` | Happy path | `RegisteredSeller` document exists with correct ID after invoke |
| `SellerRegistrationCompleted_Duplicate_IsIdempotent` | Same message twice | No exception; exactly one document row; no data corruption |
| `IsRegistered_WithKnownSeller_ReturnsTrue` | Known seller → service returns true | Invoke first, resolve service, assert `IsRegisteredAsync` returns `true` |
| `IsRegistered_WithUnknownSeller_ReturnsFalse` | Unknown seller → service returns false | Resolve service with random ID, assert `IsRegisteredAsync` returns `false` |

Use `Guid.CreateVersion7()` for seller IDs in test data.

## Explicitly out of scope

- **`SellerListing` `Apply()` methods, `DraftListingCreated` event** — S4
- **`ListingValidator`** — S4
- **HTTP endpoints (`POST /api/listings/draft`)** — S4
- **`CritterBids.Contracts.Selling.ListingPublished`** — S5
- **`SubmitListing` handler** — S5
- **Listings BC, `CritterBids.Listings`, `CritterBids.Listings.Tests` projects** — S6
- **`opts.PublishMessage<ListingPublished>()`** — S5
- **`opts.ListenToRabbitQueue("listings-selling-events")`** — S6
- **No changes to `CritterBids.Participants` or `CritterBids.Participants.Tests`** — do not modify
  any source file in the Participants project or its test project
- **No changes to `CritterBids.Contracts`** — `SellerRegistrationCompleted` already exists;
  no new contract types are introduced in S3
- **No changes to `CritterBids.Api.Tests` or `CritterBids.Contracts.Tests`**
- **No skill file updates, no CLAUDE.md changes** — documentation corrections for
  `AutoApplyTransactions` placement are deferred to S7
- **No CI workflow changes**

## Acceptance criteria

- [ ] `RegisteredSeller.cs` exists in `src/CritterBids.Selling/` with `public Guid Id { get; set; }`
- [ ] `SellerRegistrationCompletedHandler` exists and carries `[MartenStore(typeof(ISellingDocumentStore))]`
- [ ] `SellerRegistrationCompletedHandler` is idempotent — processing the same message twice produces no error
- [ ] `ISellerRegistrationService` interface exists with `IsRegisteredAsync(Guid sellerId, CancellationToken ct = default)` returning `Task<bool>`
- [ ] `SellerRegistrationService` implements `ISellerRegistrationService` using `ISellingDocumentStore`, not `IDocumentStore`
- [ ] `ISellerRegistrationService` is registered in DI inside `AddSellingModule()`
- [ ] `RegisteredSeller` is registered as a Marten document inside the `AddMartenStore<ISellingDocumentStore>()` options lambda
- [ ] `AddSellingModule()` still uses early-return (not throw) when connection string is absent — no regression on Deviation 1
- [ ] `AddMartenStore<ISellingDocumentStore>()` lambda still uses explicit `(StoreOptions opts) =>` annotation — no regression on Deviation 3
- [ ] No `opts.Policies.AutoApplyTransactions()` call inside any Marten `StoreOptions` lambda — no regression on Deviation 2
- [ ] `Program.cs` `UseWolverine()` includes `opts.Discovery.IncludeAssembly()` for the `CritterBids.Selling` assembly
- [ ] `Program.cs` no longer contains the M1 local-queue `opts.Publish(...)` call for `SellerRegistrationCompleted`
- [ ] `Program.cs` RabbitMQ guard block includes publish rule routing `SellerRegistrationCompleted` to `selling-participants-events`
- [ ] `Program.cs` RabbitMQ guard block includes listen rule for `selling-participants-events` with inline processing
- [ ] `RegisteredSellersProjectionTests.cs` exists with all four test methods named exactly as listed in §In scope
- [ ] All four projection tests pass
- [ ] `dotnet test` reports 13 passing tests, zero failing (9 existing + 4 new)
- [ ] `dotnet build` succeeds with zero errors and zero warnings across all projects
- [ ] No files created or modified outside: `src/CritterBids.Selling/`, `src/CritterBids.Api/Program.cs`,
      `tests/CritterBids.Selling.Tests/`, and this session's retrospective
- [ ] No `DraftListingCreated`, `SellerListing` Apply methods, HTTP endpoints, `ListingPublished`,
      or Listings BC work introduced

## Open questions

**`RegisteredSeller` fields beyond `Id`.** The handler receives `SellerRegistrationCompleted`.
Determine whether any fields beyond `Id` (the seller's GUID) are needed on the document. S4's
API gate only calls `IsRegisteredAsync(sellerId)` — it does not query name, email, or timestamp.
If no additional fields are required, keep the class minimal and document the decision in the
retrospective.

**`ISellerRegistrationService` DI lifetime.** `SellerRegistrationService` takes
`ISellingDocumentStore` as a constructor dependency and opens a new `IQuerySession` per call.
Confirm the appropriate lifetime given that `ISellingDocumentStore` is a singleton. Document in
the retrospective.

**`ListenToRabbitQueue` return value and `.ProcessInline()`.** Verify the chaining API — some
Wolverine versions return `IListenerConfiguration` from `ListenToRabbitQueue()`, making
`.ProcessInline()` the appropriate next call. Others differ. Confirm the actual signature via
Context7 or local source and note any deviation from what this prompt shows.

**If any `Program.cs` API surface differs from what this prompt describes** — for example, if
`opts.PublishMessage<T>().ToRabbitQueue(...)` is not the correct form, or if `ListenToRabbitQueue`
behaves differently — verify via Context7 and flag the actual API in the retrospective so the S7
skills update captures it.
