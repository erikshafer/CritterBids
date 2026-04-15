# Critter Stack Testing Patterns

Patterns for testing Wolverine handlers, Marten aggregates, and Alba HTTP scenarios in CritterBids.

---

## Table of Contents

1. [Core Philosophy](#core-philosophy)
2. [Marten BC TestFixture Pattern](#marten-bc-testfixture-pattern)
3. [Test Authentication](#test-authentication)
4. [Test Isolation](#test-isolation)
5. [Event Sourcing Race Conditions](#event-sourcing-race-conditions)
6. [Integration Test Pattern](#integration-test-pattern)
7. [TestFixture Helper Methods](#testfixture-helper-methods)
8. [Unit Testing Pure Functions](#unit-testing-pure-functions)
9. [Testing Validators](#testing-validators)
10. [Testing Time-Dependent Handlers](#testing-time-dependent-handlers)
11. [Testing Failure Paths](#testing-failure-paths)
12. [Shouldly Assertions](#shouldly-assertions)
13. [Test Organization](#test-organization)
14. [Key Principles](#key-principles)
15. [Polecat BC TestFixture Pattern](#polecat-bc-testfixture-pattern)
16. [Cross-BC Handler Isolation in Test Fixtures](#cross-bc-handler-isolation-in-test-fixtures)

---

## Core Philosophy

1. **Prefer integration tests over unit tests** — test complete vertical slices
2. **Use real infrastructure** — Testcontainers for PostgreSQL, SQL Server
3. **Pure functions are easy to unit test** — thanks to A-Frame architecture
4. **BDD-style for integration tests** — focus on behavior, not implementation

## Tools

| Tool | Purpose |
|---|---|
| **xUnit** | Test framework |
| **Shouldly** | Readable assertions |
| **Alba** | HTTP integration testing via ASP.NET Core TestServer |
| **Testcontainers** | Real PostgreSQL/SQL Server in Docker |
| **NSubstitute** | Mocking (only when necessary) |

---

## North Star Test Class Lifecycle

*Confirmed by CritterStackSamples north star analysis (§11 — Alba Integration Testing). All 12 reference samples use this identical structure.*

Every test class that exercises the host follows the same lifecycle contract:

- Implement `IAsyncLifetime` — xUnit's async setup/teardown hook.
- In `InitializeAsync()`: boot the host (or receive it from the fixture) and call `CleanAllMartenDataAsync()` to reset all Marten documents and event streams before each class.
- In `DisposeAsync()`: return `Task.CompletedTask` (or dispose the host if the class owns it).

`CleanAllMartenDataAsync()` is the canonical cleanup call in every sample — it is an extension method on `IAlbaHost` from the `Marten` namespace and wipes all documents, event streams, and snapshot rows in a single operation. It is always called in `InitializeAsync()`, never in `DisposeAsync()`, so the database is clean before the test class starts rather than after it ends (xUnit does not guarantee class execution order, so cleaning on exit does not protect the next class).

The `_host.DocumentStore().LightweightSession()` pattern provides direct Marten access for both seeding test data and making post-request assertions. It bypasses the HTTP layer and is the correct tool when you need to seed an event stream before a scenario or verify persisted state after `ExecuteAndWaitAsync()`.

For host-once-per-collection scenarios, CritterBids uses the `ICollectionFixture<XyzTestFixture>` pattern (see Collection Fixture for Sequential Execution below). The fixture boots the host once; each test class calls `CleanAllMartenDataAsync()` in its own `InitializeAsync()` to reset state. This matches the `AppFixture` + `ICollectionFixture<AppFixture>` pattern from the ProjectManagement CritterStackSample.

`services.DisableAllExternalWolverineTransports()` must appear in every `AlbaHost.For<Program>()` override that uses external transports (RabbitMQ, Azure Service Bus). Without it, Wolverine attempts to connect to the external broker during test startup and fails. The `SellingTestFixture` demonstrates this — see the Marten BC TestFixture Pattern below.

---

## Marten BC TestFixture Pattern

CritterBids uses a single primary `IDocumentStore` registered in `Program.cs` (ADR 009). Each Marten BC contributes its types via `services.ConfigureMarten()` inside its `AddXyzModule()`. Test fixtures provision a PostgreSQL Testcontainers container and register both the primary Marten store AND the BC module directly in `ConfigureServices`.

> **Why ConfigureServices, not ConfigureAppConfiguration?** Program.cs reads connection strings inline via `builder.Configuration.GetConnectionString(...)` before `ConfigureAppConfiguration` callbacks are applied to the `WebApplicationBuilder`. As a result, `ConfigureAppConfiguration` does NOT work for triggering Program.cs null guards on connection strings. Always use `ConfigureServices` to register stores and modules directly.
>
> **Key difference from Polecat:** Polecat fixtures call `services.AddParticipantsModule(connectionString)` directly in `ConfigureServices`. Marten BC fixtures call `services.AddMarten(...)` plus `services.AddBcModule()` in `ConfigureServices`.

```csharp
// Verified from CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs (ADR 009)
using Alba;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;
using Testcontainers.PostgreSql;

namespace CritterBids.Selling.Tests.Fixtures;

public class SellingTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Selling BC.
    // Program.cs null-guards AddParticipantsModule on the sqlserver connection string;
    // without it, Polecat is never registered — no two-main-stores conflict.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"selling-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register the primary Marten store with the Testcontainers connection string.
                // Program.cs's AddMarten() is null-guarded on the Aspire postgres connection
                // string, which is absent in tests. ConfigureServices runs after Program.cs, so
                // this registration is always present and wins for IDocumentStore resolution.
                services.AddMarten(opts =>
                {
                    opts.Connection(postgresConnectionString);
                    opts.DatabaseSchemaName = "public";
                    opts.Events.AppendMode = EventAppendMode.Quick;
                    opts.Events.UseMandatoryStreamTypeDeclaration = true;
                    opts.DisableNpgsqlLogging = true;
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine();

                // Register the BC module so its services (e.g. ISellerRegistrationService)
                // and ConfigureMarten contributions are present. Program.cs null-guards this
                // call inside the postgres block, which ConfigureServices bypasses.
                services.AddSellingModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    public Task CleanAllMartenDataAsync() => Host.CleanAllMartenDataAsync();
    public Task ResetAllMartenDataAsync() => Host.ResetAllMartenDataAsync();

    public Marten.IDocumentSession GetDocumentSession() =>
        Host.DocumentStore().LightweightSession();

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.ExecuteAndWaitAsync(
            async ctx => await ctx.InvokeAsync(message),
            timeoutSeconds * 1000);
    }

    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration)
    {
        IScenarioResult result = null!;
        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });
        return (tracked, result);
    }
}
```

### Marten Cleanup API Reference

| Method | When to Use |
|---|---|
| `Host.CleanAllMartenDataAsync()` | Standard test isolation — deletes all docs and event streams. Call in `InitializeAsync()` of each test class. |
| `Host.ResetAllMartenDataAsync()` | When async projections are registered — pauses daemon, clears data, restarts. Prevents daemon processing stale events. |
| `store.WaitForNonStaleProjectionDataAsync(timeout)` | After seeding events, before asserting async projection state. |

Both `CleanAllMartenDataAsync()` and `ResetAllMartenDataAsync()` are extension methods on `IAlbaHost` from the `Marten` namespace (non-generic overloads — `IDocumentStore` is registered in every Marten BC fixture). Always add `using Marten;` at the top of the fixture file.

### Marten BC Fixture — Key Points (ADR 009)

| Concern | Pattern |
|---|---|
| Store registration | `services.AddMarten(...).UseLightweightSessions().ApplyAllDatabaseChangesOnStartup().IntegrateWithWolverine()` in `ConfigureServices` |
| BC module registration | `services.AddBcModule()` in `ConfigureServices` — required even if Program.cs normally handles it (guards bypass ConfigureServices) |
| Session access | `Host.DocumentStore().LightweightSession()` — `IDocumentStore` is always registered in Marten BC fixtures |
| ConfigureAppConfiguration | Does NOT work for Program.cs inline connection string guards — use `ConfigureServices` instead |
| Single-store-per-fixture rule | Each fixture provisions only its own storage backend — Marten-only or Polecat-only, never both |

---

## Test Authentication

### TestAuthHandler — Stable User IDs

Multi-request tests need stable user IDs. Random IDs on every request cause 403s when request 1 creates a resource and request 2 tries to access it with a different identity.

```csharp
public interface ITestAuthContext { Guid UserId { get; } }

public class TestAuthContext : ITestAuthContext
{
    // Stable well-known test IDs
    public static readonly Guid TestStaffUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid UserId => TestStaffUserId;
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITestAuthContext _authContext;

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ITestAuthContext authContext)
        : base(options, logger, encoder) { _authContext = authContext; }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // ⚠️ CRITICAL: Check for Authorization header — without this, [Authorize]
        // endpoints always succeed in tests even without credentials.
        if (!Context.Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _authContext.UserId.ToString()),
            new Claim(ClaimTypes.Name, "Test Staff"),
            new Claim(ClaimTypes.Role, "Staff")
        };

        var identity = new ClaimsIdentity(claims, "Test");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

**After creating the AlbaHost, call `Host.AddDefaultAuthHeader()`** — this injects `Authorization: Bearer test-token` into every Alba scenario. Without it, all HTTP requests to `[Authorize]` endpoints fail with 401.

Note: CritterBids uses `[AllowAnonymous]` on all endpoints through M5. Auth setup in fixtures is needed from M6 onward.

### Authorization Policy Bypass

```csharp
services.AddAuthorization(opts =>
{
    opts.AddPolicy("StaffOnly", p => p.RequireAssertion(_ => true));
});
```

---

## Test Isolation

### Checklist

**Fixture setup:**
- [ ] `RunWolverineInSoloMode()` called (Marten BCs)
- [ ] `DisableAllExternalWolverineTransports()` called
- [ ] Test connection string overrides production via named store re-registration
- [ ] Collection fixture defined for sequential execution

**Test class:**
- [ ] Implements `IAsyncLifetime`
- [ ] `InitializeAsync()` calls `CleanAllMartenDataAsync()` (or `CleanAllPolecatDataAsync()` for Polecat BCs)
- [ ] `DisposeAsync()` returns `Task.CompletedTask`
- [ ] `[Collection(XyzTestCollection.Name)]` attribute present

**Test methods:**
- [ ] Each test seeds its own data inline
- [ ] Tests do not rely on data from other tests
- [ ] Tests can run in any order

### Collection Fixture for Sequential Execution

```csharp
[CollectionDefinition(Name)]
public class AuctionsTestCollection : ICollectionFixture<AuctionsTestFixture>
{
    public const string Name = "Auctions Tests";
}

[Collection(AuctionsTestCollection.Name)]
public class BidPlacementTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public BidPlacementTests(AuctionsTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.CleanAllMartenDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}
```

### Seed Data Isolation

Test classes that call `CleanAllMartenDataAsync()` in `DisposeAsync()` can wipe seed data before verification tests run — xUnit does not guarantee class execution order.

Seed data verification classes must reseed in `InitializeAsync()` and must NOT clean in `DisposeAsync()`:

```csharp
[Collection(AuctionsTestCollection.Name)]
public class SeedDataTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;
    public SeedDataTests(AuctionsTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ReseedAsync(); // Reseed before verifying
    public Task DisposeAsync() => Task.CompletedTask; // Do NOT clean — leave seed data
}
```

---

## Event Sourcing Race Conditions

HTTP-based tests for event-sourced aggregates contain a race condition: Wolverine's transaction middleware commits asynchronously — the HTTP response returns *before* the commit completes. The subsequent query reads stale state.

```csharp
// ❌ RACE CONDITION
await _fixture.Host.Scenario(s =>
{
    s.Post.Json(new PlaceBid(listingId, ...)).ToUrl($"/api/listings/{listingId}/bids");
    s.StatusCodeShouldBe(200);
});
// Transaction may not be committed yet!
await _fixture.Host.Scenario(s => s.Get.Url($"/api/listings/{listingId}")); // Stale data!

// ✅ CORRECT — direct invocation waits for all side effects
await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, bidId, bidderId, 75m));

await using var session = _fixture.GetDocumentSession();
var listing = await session.Events.AggregateStreamAsync<Listing>(listingId);
listing.ShouldNotBeNull();
listing.CurrentHighBid.ShouldBe(75m);
listing.HighBidderId.ShouldBe(bidderId);
```

**`Task.Delay()` is not a fix** — timing-based solutions fail on loaded CI machines.

### When to Use Each Approach

| Scenario | Approach |
|---|---|
| Aggregate state transitions | `ExecuteAndWaitAsync` + query event store directly |
| HTTP contract (status codes, serialization, validation errors) | HTTP via Alba |
| Integration flows (events published to message bus) | `ExecuteAndWaitAsync` + `TrackedHttpCall` |
| E2E tests | Playwright / full HTTP |

### `ProblemDetails` in Non-HTTP Handlers

When `Before()` returns `ProblemDetails` in a message handler (not HTTP), Wolverine stops the pipeline **without throwing an exception**. Assert that state is unchanged — don't expect an exception.

```csharp
// ❌ WRONG — no exception thrown in message handler context
await Should.ThrowAsync<InvalidOperationException>(async () =>
    await _fixture.ExecuteAndWaitAsync(new PlaceBid(closedListingId, ...)));

// ✅ CORRECT — verify aggregate state is unchanged
await _fixture.ExecuteAndWaitAsync(new PlaceBid(closedListingId, ...));
using var session = _fixture.GetDocumentSession();
var listing = await session.Events.AggregateStreamAsync<Listing>(closedListingId);
listing!.CurrentHighBid.ShouldBe(previousHighBid); // Before() rejected the command — unchanged
```

---

## Integration Test Pattern

### HTTP Tests (Alba)

```csharp
[Collection(AuctionsTestCollection.Name)]
public class PlaceBidTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;
    public PlaceBidTests(AuctionsTestFixture fixture) => _fixture = fixture;
    public async Task InitializeAsync() => await _fixture.CleanAllMartenDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PlaceBid_ValidBid_Returns200()
    {
        // Arrange — seed listing via event stream
        var listingId = Guid.CreateVersion7();
        using (var session = _fixture.GetDocumentSession())
        {
            session.Events.StartStream<Listing>(listingId,
                new ListingPublished(listingId, sellerId, "Test Item", 10m, ...),
                new BiddingOpened(listingId, DateTimeOffset.UtcNow.AddDays(1)));
            await session.SaveChangesAsync();
        }

        var cmd = new PlaceBid(listingId, Guid.NewGuid(), bidderId, 25m);

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(cmd).ToUrl($"/api/listings/{listingId}/bids");
            s.StatusCodeShouldBe(200);
        });
    }
}
```

### Event-Sourced Aggregate State Tests

```csharp
[Fact]
public async Task PlaceBid_UpdatesListingHighBid()
{
    // Arrange
    var listingId = Guid.CreateVersion7();
    using (var session = _fixture.GetDocumentSession())
    {
        session.Events.StartStream<Listing>(listingId,
            new BiddingOpened(listingId, DateTimeOffset.UtcNow.AddHours(1)));
        await session.SaveChangesAsync();
    }

    // Act — direct invocation, no race condition
    await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, Guid.NewGuid(), bidderId, 75m));

    // Assert — query event store directly
    await using var session = _fixture.GetDocumentSession();
    var listing = await session.Events.AggregateStreamAsync<Listing>(listingId);

    listing.ShouldNotBeNull();
    listing.CurrentHighBid.ShouldBe(75m);
    listing.HighBidderId.ShouldBe(bidderId);
}
```

### Testing Integration Message Publishing

> ⚠️ **Outbox Assertion Prerequisite:** `tracked.Sent.MessagesOf<T>()` requires a Wolverine routing rule
> to be configured for the message type in the host's Wolverine options (`Program.cs`). Without it,
> `PublishAsync` calls `NoRoutesFor()` and returns immediately — the message never reaches any
> `ISendingAgent` and `tracked.Sent` always returns 0 regardless of what was added to `OutgoingMessages`.
> See **Anti-Pattern #14** in `docs/skills/wolverine-message-handlers.md` for the full root cause and
> the `opts.Publish(...)` resolution. This is a host configuration requirement, not a fixture concern.
>
> **Diagnostic:** `dotnet run -- wolverine-diagnostics describe-routing` lists every configured routing
> rule. If the message type is absent from the output, the `opts.Publish(...)` rule is missing from
> `Program.cs`. See the Debugging section in `docs/skills/wolverine-message-handlers.md`.

```csharp
[Fact]
public async Task CloseBidding_WithWinner_PublishesListingSold()
{
    await SeedOpenListingWithBid(listingId, winnerId, hammerPrice: 100m);

    var (tracked, result) = await _fixture.TrackedHttpCall(s =>
    {
        s.Post.Json(new CloseBidding(listingId)).ToUrl($"/api/listings/{listingId}/close");
        s.StatusCodeShouldBe(200);
    });

    tracked.Sent.MessagesOf<CritterBids.Contracts.Auctions.ListingSold>()
        .Any().ShouldBeTrue();
}
```

---

## TestFixture Helper Methods

| Method | Purpose | When to Use |
|---|---|---|
| `GetDocumentSession()` | Direct DB access via named store | Seeding event streams, asserting state |
| `GetDocumentStore()` | Named store reference | Advanced cleanup, schema operations |
| `CleanAllMartenDataAsync()` | Clear all docs + event streams | `InitializeAsync()` of every test class (no async projections) |
| `ResetAllMartenDataAsync()` | Pause daemon, clear, restart | `InitializeAsync()` when async projections are registered |
| `WaitForNonStaleProjectionDataAsync()` | Sync async projection state | After seeding events, before asserting async projection output |
| `ExecuteAndWaitAsync<T>()` | Invoke via Wolverine pipeline | Testing state-changing commands |
| `TrackedHttpCall()` | HTTP + message tracking | Asserting integration messages published |

### ⚠️ AutoApplyTransactions Does Not Fire on Direct Handler Calls

`AutoApplyTransactions()` only fires through the Wolverine pipeline — not when you call a handler method directly in a test. Use `ExecuteAndWaitAsync()` to avoid this entirely.

```csharp
// ❌ WRONG — changes silently discarded
var events = PlaceBidHandler.Handle(cmd, listing);

// ✅ PREFERRED — use the pipeline
await _fixture.ExecuteAndWaitAsync(cmd);
```

---

## Unit Testing Pure Functions

A-Frame architecture makes handler unit testing trivial — no infrastructure:

```csharp
public class PlaceBidHandlerTests
{
    [Fact]
    public void Before_WithClosedListing_Returns400()
    {
        var listing = CreateListing(status: ListingStatus.Closed);
        var cmd = new PlaceBid(listing.Id, Guid.NewGuid(), bidderId, 50m);

        var result = PlaceBidHandler.Before(cmd, listing);

        result.Status.ShouldBe(400);
    }

    [Fact]
    public void Handle_ValidBid_ReturnsBidPlacedEvent()
    {
        var listing = CreateListing(currentHighBid: 25m);
        var cmd = new PlaceBid(listing.Id, Guid.NewGuid(), bidderId, 50m);

        var (events, messages) = PlaceBidHandler.Handle(cmd, listing);

        events.ShouldContain(e => e is BidPlaced);
        messages.ShouldContain(m => m is CritterBids.Contracts.Auctions.BidPlaced);
    }
}
```

---

## Testing Validators

```csharp
public class ListingValidatorTests
{
    [Fact]
    public void ValidDraft_Passes()
    {
        var draft = CreateValidDraft();
        var result = ListingValidator.Validate(draft);
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Title_Empty_IsRejected()
    {
        var draft = CreateValidDraft() with { Title = "" };
        var result = ListingValidator.Validate(draft);
        result.IsValid.ShouldBeFalse();
        result.Reason.ShouldBe("Title is required");
    }
}
```

---

## Testing Time-Dependent Handlers

Handlers that check elapsed time must use an injectable clock — never `DateTimeOffset.UtcNow` directly.

```csharp
public interface ISystemClock { DateTimeOffset UtcNow { get; } }
public class SystemClock : ISystemClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public class FrozenSystemClock : ISystemClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
```

Expose on fixture and override in tests:

```csharp
public FrozenSystemClock Clock { get; private set; } = new();

// During host init:
services.RemoveAll<ISystemClock>();
services.AddSingleton<ISystemClock>(Clock);
```

**Always reset the clock in `InitializeAsync()` of each test class.** The clock is a singleton shared across the xUnit collection — one class advancing it affects subsequent classes.

---

## Testing Failure Paths

When the default test fixture uses a stub that always succeeds, testing failure paths requires a dedicated fixture with a failing implementation.

```csharp
[CollectionDefinition(Name)]
public class TrackingFailureTestCollection : ICollectionFixture<TrackingFailureTestFixture>
{
    public const string Name = "Tracking Failure Tests";
}
```

A separate `[CollectionDefinition]` creates a fully isolated fixture with its own Testcontainers instance. The cost is a second container startup — acceptable for a small set of targeted failure-path tests.

---

## Shouldly Assertions

```csharp
result.ShouldNotBeNull();
result.Status.ShouldBe(ListingStatus.Open);
result.CurrentHighBid.ShouldBe(75m);
result.BidCount.ShouldBeGreaterThan(0);
events.ShouldNotBeEmpty();
events.ShouldContain(e => e is BidPlaced);
Should.Throw<InvalidOperationException>(() => /* ... */);
await Should.ThrowAsync<InvalidOperationException>(async () => /* ... */);
```

---

## Test Organization

```
tests/
  CritterBids.Auctions.Tests/
    Fixtures/
      AuctionsTestFixture.cs
      AuctionsTestCollection.cs
    Bidding/
      PlaceBidTests.cs
      BidRejectionTests.cs
    AuctionClosing/
      AuctionClosingTests.cs
  CritterBids.Selling.Tests/
    Fixtures/
      SellingTestFixture.cs
      SellingTestCollection.cs
    Listings/
      DraftListingTests.cs
      SubmitListingTests.cs
```

---

## Key Principles

1. **Standardized TestFixture across all BCs** — same helper methods, same patterns
2. **Register stores AND BC modules in `ConfigureServices`** — `ConfigureAppConfiguration` does NOT propagate to Program.cs's inline `builder.Configuration.GetConnectionString(...)` calls. All store and module registrations must use `ConfigureServices` directly.
3. **Single storage backend per fixture** — each fixture provisions either Marten (PostgreSQL) or Polecat (SQL Server), never both simultaneously. Running both registers two "main" Wolverine message stores and causes `InvalidWolverineStorageConfigurationException`.
4. **`RunWolverineInSoloMode()` + `DisableAllExternalWolverineTransports()`** — both required in every Marten BC test fixture; `RunWolverineInSoloMode()` not required for Polecat BC fixtures
5. **`CleanAllMartenDataAsync()` in `InitializeAsync()`** — not `DisposeAsync()`; ensures clean state before each test class
6. **Use `ResetAllMartenDataAsync()` when async projections are registered** — prevents daemon processing stale data
7. **`ExecuteAndWaitAsync` over HTTP POST + GET** — eliminates race conditions in ES tests
8. **Assert full integration message payloads** — not just that the type was published
9. **Real infrastructure via Testcontainers** — real PostgreSQL/SQL Server, not SQLite or in-memory
10. **`IWolverineExtension` exclusion for cross-BC isolation** — when a fixture doesn't provision a BC's infrastructure, exclude that BC's handlers via `IWolverineExtension` to prevent Wolverine code-gen failures

---

## Polecat BC TestFixture Pattern

Polecat BCs (Participants, Operations, Settlement) use SQL Server rather than PostgreSQL. The fixture shape is identical to Marten BCs — the only differences are the container, connection string override, and cleanup helpers.

```csharp
// Verified from actual CritterBids.Participants.Tests/Fixtures/ParticipantsTestFixture.cs (M1-S7)
namespace CritterBids.Participants.Tests.Fixtures;

public class ParticipantsTestFixture : IAsyncLifetime
{
    // Pass image tag directly to constructor — .WithImage() is obsolete in Testcontainers 4.x.
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04")
        .WithPassword("CritterBids#Test2025!")
        .WithName($"participants-sqlserver-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        var connectionString = _sqlServer.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register the Participants BC module directly with the Testcontainers connection.
                // Program.cs's AddParticipantsModule() is null-guarded on the sqlserver connection
                // string, which is absent in tests (ConfigureAppConfiguration does not propagate
                // to Program.cs inline guards). ConfigureServices runs after Program.cs, so this
                // is the only Polecat registration — no two-main-stores conflict.
                services.AddParticipantsModule(connectionString);

                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _sqlServer.DisposeAsync();
    }

    // Note: DocumentStore(), CleanAllPolecatDataAsync(), TrackActivity(), and ExecuteAndWaitAsync()
    // are extension methods on IServiceProvider, not on IHost. Always use Host.Services.
    public IDocumentSession GetDocumentSession() =>
        Host.Services.DocumentStore().LightweightSession();

    public IDocumentStore GetDocumentStore() =>
        Host.Services.DocumentStore();

    public Task CleanAllPolecatDataAsync() =>
        Host.Services.CleanAllPolecatDataAsync();

    public Task ResetAllPolecatDataAsync() =>
        Host.Services.ResetAllPolecatDataAsync();

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.Services
            .TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host.Services)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx => await ctx.InvokeAsync(message)));
    }

    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration)
    {
        IScenarioResult result = null!;
        var tracked = await Host.Services.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });
        return (tracked, result);
    }
}
```

### Key Differences Between Marten and Polecat Fixtures

| Concern | Marten BC | Polecat BC |
|---|---|---|
| Container | `PostgreSqlBuilder("postgres:17-alpine")` | `MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04")` |
| Connection override | `services.AddMarten(opts => { opts.Connection(...); ... }).UseLightweightSessions()...` in `ConfigureServices` | `services.AddParticipantsModule(testConnectionString)` in `ConfigureServices` |
| Solo mode | `services.RunWolverineInSoloMode()` required | Not required (Polecat doesn't use advisory locks the same way) |
| `IDocumentStore` available? | No — use `GetRequiredService<IBcDocumentStore>()` | Yes via `Host.Services.DocumentStore()` extension |
| Data cleanup | `Host.CleanAllMartenDataAsync()` | `Host.Services.CleanAllPolecatDataAsync()` |
| Daemon catchup | `store.WaitForNonStaleProjectionDataAsync(timeout)` | `Host.Services.ForceAllPolecatDaemonActivityToCatchUpAsync(ct)` |
| Helper extension host | `IAlbaHost` | `IServiceProvider` — use `Host.Services.*` |

### Polecat-Specific Testing Helpers

> ⚠️ **Confirmed from M1 (S7):** These helpers are extension methods on `IServiceProvider`, not `IHost`. Always call on `Host.Services`, not directly on `Host`.

```csharp
await host.Services.CleanAllPolecatDataAsync();      // Most common — use in InitializeAsync()
await host.Services.ResetAllPolecatDataAsync();      // When async projections registered
await host.Services.ForceAllPolecatDaemonActivityToCatchUpAsync(ct); // Better than WaitForNonStale
var store = host.Services.DocumentStore();           // Extension on IServiceProvider
```

---

## Cross-BC Handler Isolation in Test Fixtures

When a test fixture boots `AlbaHost.For<Program>()`, Wolverine's handler discovery scans all
assemblies referenced by the solution — including BC assemblies that are not provisioned in
that fixture. This causes two distinct failure modes that require explicit suppression.

### Problem 1: Foreign-BC handlers discovered but infrastructure absent

When BC-A's test fixture starts, it loads BC-B's assembly (included via `Program.cs`
`opts.Discovery.IncludeAssembly(...)`) but does not provision BC-B's infrastructure
(no Testcontainers for BC-B's database). BC-B's handlers are discovered by Wolverine,
and when a message matching a BC-B handler type is dispatched, Wolverine attempts to
generate handler code that injects IDocumentSession. Because Program.cs's primary
AddMarten() call is null-guarded on the postgres connection string, IDocumentStore is
not registered in this fixture — SessionVariableSource is absent and Wolverine's code-gen
fails. This causes a startup or runtime injection error.

**Fix: register an `IWolverineExtension` singleton that excludes foreign-BC handlers.**

```csharp
// In ParticipantsTestFixture.cs, inside builder.ConfigureServices:
services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

// Defined as an internal sealed class in the fixture file:
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — Marten not configured (no postgres in Participants fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });
    }
}
```

The `WithCondition` overload that accepts a label string is preferred — it appears in
Wolverine diagnostic output and makes the exclusion reason visible in test run logs.

### Problem 2: Cross-BC integration messages dropped before `tracked.Sent` records them

When a handler publishes an integration event via `OutgoingMessages` and the event's routing
rule points at a foreign-BC transport that is disabled in the fixture
(`DisableAllExternalWolverineTransports()`), Wolverine calls `NoRoutesFor(envelope)` before
any `ISendingAgent` is invoked. `tracked.Sent.MessagesOf<T>()` returns 0, even though the
message was correctly added to `OutgoingMessages`.

This is distinct from Anti-Pattern #14 in `docs/skills/wolverine-message-handlers.md`, which
covers a message type with **no** routing rule in production `Program.cs`. This pattern applies
to message types that **have** a production rule pointing at a transport not available in tests.

**Fix: add a stub local queue routing rule in the same `IWolverineExtension`.**

```csharp
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        // Exclude the handler (no IDocumentStore — postgres absent in Participants fixture)
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — Marten not configured (no postgres in Participants fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });

        // Route SellerRegistrationCompleted to a stub local queue so tracked.Sent captures it.
        // Without a routing rule, Wolverine calls NoRoutesFor() before any ISendingAgent fires.
        // The handler is excluded above, so the message is delivered and silently dropped —
        // matching pre-registration placeholder behavior.
        options.PublishMessage<SellerRegistrationCompleted>()
            .ToLocalQueue("selling-participants-stub");
    }
}
```

The stub queue needs no handler. Wolverine's `NoHandlerContinuation` records `NoHandlers` then
`MessageSucceeded` — no exception thrown, `DoNotAssertOnExceptionsDetected` is not needed.

### Naming convention

Name the `IWolverineExtension` after the BC being excluded, not the fixture registering it:
`SellingBcDiscoveryExclusion`, not `ParticipantsTestExclusion`. This makes the intent clear
when multiple fixtures use the same exclusion class.

### `ConfigureAppConfiguration` timing caveat ⚠️ DOES NOT WORK FOR PROGRAM.CS INLINE GUARDS

`ConfigureAppConfiguration` in test fixtures does **NOT** propagate to inline
`builder.Configuration.GetConnectionString(...)` reads in `Program.cs`. When Program.cs
reads connection strings immediately during builder setup:

```csharp
// In Program.cs — this runs BEFORE ConfigureAppConfiguration callbacks apply
var postgresConnectionString = builder.Configuration.GetConnectionString("postgres");
if (!string.IsNullOrEmpty(postgresConnectionString))
{
    builder.Services.AddMarten(...); // never reached in test fixtures via ConfigureAppConfiguration
}
```

...the value is evaluated from the initial `WebApplicationBuilder` configuration, not from the
factory's injected configuration. The factory's `ConfigureAppConfiguration` callbacks run too
late to affect these inline reads.

**Correct approach:** Register stores and modules directly in `ConfigureServices`. This always
runs after Program.cs and its registrations either add to or replace (last-wins) what Program.cs
registered:

```csharp
builder.ConfigureServices(services =>
{
    // Always correct — runs after Program.cs
    services.AddMarten(opts => { opts.Connection(testConnectionString); /* ... */ })
        .UseLightweightSessions()
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

    services.AddSellingModule(); // BC module must also be registered here
});
```

**To suppress a BC's registration** (exclude it entirely because infrastructure is absent):
omit that BC's `ConfigureServices` registration and add an `IWolverineExtension` exclusion
to prevent handler discovery from finding its handlers (see Problem 1 above).

The canonical fixtures are `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs`
(Marten registered in ConfigureServices, no SQL Server) and
`tests/CritterBids.Participants.Tests/Fixtures/ParticipantsTestFixture.cs` (Polecat registered
in ConfigureServices via `AddParticipantsModule`, no PostgreSQL, with `SellingBcDiscoveryExclusion`).

---

## References

- `docs/skills/marten-event-sourcing.md` — event sourcing race conditions, projection behavior
- `docs/skills/adding-bc-module.md` — BC module registration, ConfigureMarten pattern, fixture setup
- `docs/skills/wolverine-sagas.md` — saga testing patterns
- `docs/skills/wolverine-message-handlers.md` — handler patterns, ProblemDetails behavior (see Anti-Pattern #14)
- `docs/decisions/009-shared-marten-store.md` — shared primary store ADR (current)
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs` — canonical Marten BC fixture
- `tests/CritterBids.Participants.Tests/Fixtures/ParticipantsTestFixture.cs` — canonical Polecat BC fixture with `IWolverineExtension` exclusion
- [Alba HTTP Testing](https://jasperfx.github.io/alba/)
- [Testcontainers .NET](https://dotnet.testcontainers.org/)
