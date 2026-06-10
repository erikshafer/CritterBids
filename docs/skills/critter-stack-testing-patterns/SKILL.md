---
name: critter-stack-testing-patterns
description: "CritterBids testing posture: Alba + Testcontainers PostgreSQL fixtures, cross-BC handler isolation, auth stubs, direct handler traps, and Wolverine diagnostics. Use when writing or debugging integration tests."
cluster: testing
tags: [testing, alba, testcontainers, marten, wolverine, auth]
---

# Critter Stack Testing Patterns

> CritterBids test-fixture decisions and hard-won integration-test findings.
> Generic Alba, Wolverine tracking, Testcontainers, Marten cleanup, and parallelization mechanics live in ai-skills `wolverine-testing-*` / `marten-integration-testing`; **this skill documents only the CritterBids-specific decisions.**

## When to apply this skill

Use this skill when:

- Adding or fixing a CritterBids BC integration test.
- A test host discovers handlers from another BC whose infra is absent.
- `tracked.Sent.MessagesOf<T>()` is empty, `tracked.NoRoutes` has the message, or a cascaded message vanishes.
- A handler test passes by direct method call but fails through Wolverine/HTTP.
- Authentication must be stable and deterministic in Alba tests.

Do NOT use this skill for: generic Alba scenario syntax (see `wolverine-testing-alba`), generic Testcontainers patterns (see `wolverine-testing-with-testcontainers`), generic Marten cleanup APIs (see `marten-integration-testing`), or generic Wolverine tracking (see `wolverine-testing-integration-marten`).

## Read upstream first

Generic mechanics are covered upstream. Read these ai-skills (license required; install via `npx skills add`) before this skill — they cover ~80% of testing:

1. `wolverine-testing-integration-marten` — Wolverine + Marten test hosts, tracking sessions, outbox assertions.
2. `wolverine-testing-alba` — Alba host/scenario mechanics and HTTP assertions.
3. `wolverine-testing-with-testcontainers` — container lifecycle, wait strategies, reuse posture.
4. `marten-integration-testing` — `CleanAllMartenDataAsync`, seeding, sessions for verification.
5. `wolverine-testing-test-parallelization` — xUnit collection fixtures, shared fixtures, parallel safety.

This skill picks up at CritterBids fixture posture and five project-only findings absent from upstream.

## CritterBids fixture posture

All eight BCs are Marten/PostgreSQL (ADR 011). Test fixtures use:

- Alba host around `Program`.
- Testcontainers PostgreSQL collection fixture.
- Shared primary `IDocumentStore` registered in `ConfigureServices`, not via `ConfigureAppConfiguration`.
- `services.AddXyzModule()` registered in fixture so the BC's `ConfigureMarten()` contributions exist.
- `services.RunWolverineInSoloMode()` and `services.DisableAllExternalWolverineTransports()`.
- Non-generic `Host.CleanAllMartenDataAsync()` / `Host.ResetAllMartenDataAsync()` because CritterBids uses one primary store.
- Sequential execution for DB-heavy BC suites; default baseline is `[assembly: CollectionBehavior(DisableTestParallelization = true)]` unless a fixture proves parallel-safe isolation.

```csharp
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        services.AddMarten(opts =>
        {
            opts.Connection(postgresConnectionString);
            opts.DatabaseSchemaName = "public";
            opts.DisableNpgsqlLogging = true;
        })
        .UseLightweightSessions()
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

        services.AddSellingModule();
        services.RunWolverineInSoloMode();
        services.DisableAllExternalWolverineTransports();
    });
});
```

Use the Alba `Scenario` pattern for JSON responses. `IAlbaHost.GetAsJson<T>()` does not exist in Alba 8.5.2.

## Six CritterBids-only patterns to preserve

### 1. Cross-BC handler isolation via `IWolverineExtension` exclusion

Problem 1: fixtures sometimes discover foreign-BC handlers while the foreign BC's infra is absent. Example: an Auctions test host discovers a Selling handler needing Selling services, but only Auctions infrastructure is registered.

Fix: exclude foreign handlers at Wolverine discovery time, not with fragile IoC stubs.

```csharp
public sealed class AuctionsOnlyWolverineExtension : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.DisableConventionalDiscovery();
        options.Discovery.IncludeAssembly(typeof(Listing).Assembly);
    }
}

services.AddWolverineExtension<AuctionsOnlyWolverineExtension>();
```

Problem 2: cross-BC integration messages can be dropped before `tracked.Sent` records them. With no route, Wolverine records `NoRoutesFor`/`NoRoutes` and never sends through an agent, so `tracked.Sent.MessagesOf<T>()` stays empty.

Fix for a fixture with no real consumer: add a stub local queue route for the integration message.

```csharp
public sealed class SellingTestRoutingExtension : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
            .ToLocalQueue("stub-listing-published");
    }
}
```

Assert on the correct target:

- Need to prove a publish route exists and message was sent -> `tracked.Sent.MessagesOf<T>()`.
- Need to prove no route exists yet / route intentionally absent -> `tracked.NoRoutes`.
- Need handler execution -> add `ListenTo...` and register consumer infra, or invoke through Wolverine inside a host that includes the consumer.

### 2. `ConfigureAppConfiguration` does not reach Program.cs inline guards

`Program.cs` reads `builder.Configuration.GetConnectionString(...)` before Alba's `ConfigureAppConfiguration` callback can affect those inline variables. If the primary store is null-guarded in `Program.cs`, injecting a connection string through `ConfigureAppConfiguration` is too late.

Do this instead:

```csharp
builder.ConfigureServices(services =>
{
    services.AddMarten(opts => opts.Connection(postgresConnectionString))
        .UseLightweightSessions()
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

    services.AddSellingModule();
});
```

### 3. `AutoApplyTransactions` does not fire on direct handler calls

Direct static method calls bypass Wolverine's generated pipeline. `AutoApplyTransactions()` does not wrap them, `[WriteAggregate]` does not load state, cascading messages are not routed, and `ProblemDetails` continuations do not behave like runtime handlers.

Use direct calls only for pure decision functions. For state-changing handlers, invoke through Alba HTTP or Wolverine inside the host.

```csharp
// Good: pure decision test
var decision = SellerListing.Decide(cmd, aggregate);

// Not equivalent to runtime: bypasses Wolverine pipeline
var result = SubmitListingHandler.Handle(cmd, aggregate);
```

### 4. `ProblemDetails` in non-HTTP handlers stops pipeline without throwing

In message handlers, returning `ProblemDetails` is a Wolverine continuation. It short-circuits the pipeline and marks the message handled; it does not throw, so exception assertions and `AssertNoExceptions` will not catch it.

Test the continuation/result explicitly or route through HTTP when the API contract is what matters.

### 5. `TestAuthHandler` uses stable IDs and `NoResult()` for missing auth

CritterBids auth is deferred through M6, but endpoint tests still need stable principals. Use a deterministic test auth handler:

- Stable `NameIdentifier`, `BidderId`, and relevant claims per test user.
- Missing `Authorization` header returns `AuthenticateResult.NoResult()`, not failure. This preserves anonymous endpoint behavior and lets `[AllowAnonymous]` work.
- Explicit test header selects the user identity.

```csharp
protected override Task<AuthenticateResult> HandleAuthenticateAsync()
{
    if (!Request.Headers.ContainsKey("Authorization"))
        return Task.FromResult(AuthenticateResult.NoResult());

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, StableUserIds.BidderA.ToString()),
        new Claim("bidder_id", StableUserIds.BidderA.ToString())
    };

    var identity = new ClaimsIdentity(claims, Scheme.Name);
    return Task.FromResult(AuthenticateResult.Success(
        new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
}
```

### 6. Fresh-state browser smoke against the live integrated host

Green build + green unit tests + green integration tests have now twice shipped a broken end-to-end
flow (M8-S2: session POST missing its JSON body; M8 Bug #2 fix-up: session bootstrap dropped under
StrictMode, live-feed ghost entries). The class of bug is the same each time: mocked-fetch unit
tests verify *response handling*, not request shape, React effect lifecycle, or push-delivery
semantics — and isolated-BC integration fixtures replace the exact transport behavior under test
(`DisableAllExternalWolverineTransports`). Before declaring a user-facing slice done:

- **Run the SPA against the live Aspire host and drive it in a real browser** (the bugs only exist
  where the layers meet).
- **Start from fresh state**: new browser contexts (fresh `sessionStorage`/`localStorage`) per
  simulated user. Cached session ids masked the StrictMode bootstrap bug across an entire milestone.
- **Use one context per simulated participant** for cross-client behaviors (live feed, outbid push) —
  two tabs in one context share storage and therefore share a session.
- **Watch the console**: React duplicate-key warnings and SignalR negotiation errors are findings,
  not noise.
- Zero-install scripting recipe: `npm i playwright-core` in a throwaway directory +
  `chromium.launch({ channel: "msedge" })` drives the system Edge headless — no browser download, no
  admin rights. See the walkthrough scripts pattern in the M8 Bug #2 fix-up session (PR #90).

## Common pitfalls

- **Registering only `AddMarten()` in a fixture.** The BC module's `ConfigureMarten()` contributions and services are missing; register both `AddMarten()` and `AddXyzModule()` in `ConfigureServices`.
- **Expecting `tracked.Sent` without a route.** Add a real/stub route or assert `tracked.NoRoutes` intentionally.
- **Using direct handler calls for persistence assertions.** Direct calls skip `AutoApplyTransactions` and outbox behavior.
- **Assuming test parallelism is harmless.** Shared PostgreSQL + Marten cleanup creates cross-test interference unless every suite has deterministic stream IDs/data isolation. Default to sequential.
- **Using old Polecat fixture patterns.** CritterBids is all-Marten; Polecat fixture notes are archival only.
- **Declaring a user-facing slice done on green tests alone.** Run the fresh-state browser smoke (pattern 6); request-contract, effect-lifecycle, and push-delivery bugs are invisible to mocked and isolated-fixture tests.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-testing-integration-marten`, `wolverine-testing-alba`, `wolverine-testing-with-testcontainers`, `marten-integration-testing`, `wolverine-testing-test-parallelization`.

**Prerequisites:**

- `marten-event-sourcing` — shared Marten store, UUID v7 streams, `AutoApplyTransactions()` placement.
- `wolverine-message-handlers` — handler shape and the outbox routing rule.
- `adding-bc-module` — module registration and fixture wiring.

**Downstream:**

- `diagnostics` — CLI commands for routing, codegen, schema, and storage debugging.
- `integration-messaging` — route topology and cross-BC contract posture.

**External:**

- ADR 009 (shared primary store), ADR 011 (All-Marten Pivot) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions and Canonical Bootstrap Sequence.
