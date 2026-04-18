# Critter Stack Testing Patterns

Patterns for testing Wolverine handlers, Marten aggregates, and Alba HTTP scenarios in CritterBids.

The file is organized in three parts, ordered by typical read progression:

- **Part I — Fundamentals.** Core philosophy, tools, the North Star test lifecycle, test authentication, test isolation, unit-testing pure functions, assertions, and organization. Read first; applies to every test.
- **Part II — Integration Testing.** The Marten BC TestFixture pattern, event-sourcing race conditions, HTTP and state tests, tracked sessions, scheduled messages, async projections, and seed data. Read when authoring or extending an integration test.
- **Part III — Advanced Scenarios.** Cross-BC handler isolation in fixtures, test parallelization strategy, advanced Testcontainers patterns (RabbitMQ vhosts, parallel startup, dynamic DB per fixture), and the archived Polecat BC fixture pattern. Read when Parts I and II aren't enough.

---

## Table of Contents

### Part I — Fundamentals

1. [Core Philosophy](#core-philosophy)
2. [Tools](#tools)
3. [North Star Test Class Lifecycle](#north-star-test-class-lifecycle)
4. [Test Authentication](#test-authentication)
5. [Test Isolation](#test-isolation)
6. [Unit Testing Pure Functions](#unit-testing-pure-functions)
7. [Testing Validators](#testing-validators)
8. [Testing Time-Dependent Handlers](#testing-time-dependent-handlers)
9. [Testing Failure Paths](#testing-failure-paths)
10. [Shouldly Assertions](#shouldly-assertions)
11. [Test Organization](#test-organization)

### Part II — Integration Testing

12. [Marten BC TestFixture Pattern](#marten-bc-testfixture-pattern)
13. [Event Sourcing Race Conditions](#event-sourcing-race-conditions)
14. [Integration Test Pattern](#integration-test-pattern)
15. [TestFixture Helper Methods](#testfixture-helper-methods)
16. [Tracked Session Configuration](#tracked-session-configuration)
17. [Testing Scheduled Messages](#testing-scheduled-messages)
18. [Testing Async Projections](#testing-async-projections)
19. [Seeding with `IInitialData`](#seeding-with-iinitialdata)
20. [Debugging Integration Tests](#debugging-integration-tests)

### Part III — Advanced Scenarios

21. [Cross-BC Handler Isolation in Test Fixtures](#cross-bc-handler-isolation-in-test-fixtures)
22. [Test Parallelization Strategy](#test-parallelization-strategy)
23. [Advanced Testcontainers Patterns](#advanced-testcontainers-patterns)
24. [Polecat BC TestFixture Pattern (Archived)](#polecat-bc-testfixture-pattern-archived)

### Closer

25. [Key Principles](#key-principles)
26. [References](#references)

---

# Part I — Fundamentals

## Core Philosophy

1. **Prefer integration tests over unit tests** — test complete vertical slices
2. **Use real infrastructure** — Testcontainers for PostgreSQL (SQL Server only for sibling projects since ADR 011)
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

For host-once-per-collection scenarios, CritterBids uses the `ICollectionFixture<XyzTestFixture>` pattern (see Test Parallelization Strategy in Part III). The fixture boots the host once; each test class calls `CleanAllMartenDataAsync()` in its own `InitializeAsync()` to reset state. This matches the `AppFixture` + `ICollectionFixture<AppFixture>` pattern from the ProjectManagement CritterStackSample.

`services.DisableAllExternalWolverineTransports()` must appear in every `AlbaHost.For<Program>()` override that uses external transports (RabbitMQ, Azure Service Bus). Without it, Wolverine attempts to connect to the external broker during test startup and fails. The `SellingTestFixture` demonstrates this — see the Marten BC TestFixture Pattern in Part II.

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
- [ ] Test connection string overrides production via `ConfigureServices` (not `ConfigureAppConfiguration`)
- [ ] Collection fixture defined for sequential execution

**Test class:**
- [ ] Implements `IAsyncLifetime`
- [ ] `InitializeAsync()` calls `CleanAllMartenDataAsync()` (or `CleanAllPolecatDataAsync()` for sibling-project Polecat fixtures)
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

# Part II — Integration Testing

## Marten BC TestFixture Pattern

CritterBids uses a single primary `IDocumentStore` registered in `Program.cs` (ADR 009). Each Marten BC contributes its types via `services.ConfigureMarten()` inside its `AddXyzModule()`. Test fixtures provision a PostgreSQL Testcontainers container and register both the primary Marten store AND the BC module directly in `ConfigureServices`.

> **Why ConfigureServices, not ConfigureAppConfiguration?** Program.cs reads connection strings inline via `builder.Configuration.GetConnectionString(...)` before `ConfigureAppConfiguration` callbacks are applied to the `WebApplicationBuilder`. As a result, `ConfigureAppConfiguration` does NOT work for triggering Program.cs null guards on connection strings. Always use `ConfigureServices` to register stores and modules directly.
>
> Full caveat details in [Cross-BC Handler Isolation](#cross-bc-handler-isolation-in-test-fixtures) (Part III).

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
    // Only PostgreSQL is needed for the Selling BC (and all BCs under ADR 011).
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
| Single-store-per-fixture rule | Each fixture provisions only its own storage backend (CritterBids is Marten-only under ADR 011) |

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

**`Task.Delay()` is not a fix** — timing-based solutions fail on loaded CI machines. For complex cases where `ExecuteAndWaitAsync` isn't enough, use [`WaitForConditionAsync`](#testing-async-projections) — polling with a real condition instead of a blind sleep.

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

### Void Endpoint Status Codes

Wolverine HTTP endpoints that return `void` respond with **204 No Content**, not 200. This also applies to `[WriteAggregate]` endpoints that return a tuple with `Results.NoContent()` as the first element (the event is cascaded, not serialized into the response):

```csharp
[WolverinePost("/api/listings/{listingId}/close"), EmptyResponse]
public static (IResult, BiddingClosed) Handle(CloseBidding cmd, [WriteAggregate] Listing listing)
    => (Results.NoContent(), new BiddingClosed(listing.Id, DateTimeOffset.UtcNow, ...));

// In tests:
await _fixture.Host.Scenario(s =>
{
    s.Post.Json(new CloseBidding(listingId)).ToUrl($"/api/listings/{listingId}/close");
    s.StatusCodeShouldBe(204); // Not 200!
});
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
        s.StatusCodeShouldBe(204);
    });

    tracked.Sent.MessagesOf<CritterBids.Contracts.Auctions.ListingSold>()
        .Any().ShouldBeTrue();
}
```

Use `tracked.Sent.SingleMessage<T>()` when exactly one message of a type is expected — it asserts count = 1 and returns the payload in one step:

```csharp
var sold = tracked.Sent.SingleMessage<CritterBids.Contracts.Auctions.ListingSold>();
sold.HammerPrice.ShouldBe(100m);
sold.WinnerId.ShouldBe(winnerId);
```

---

## TestFixture Helper Methods

| Method | Purpose | When to Use |
|---|---|---|
| `GetDocumentSession()` | Direct DB access via the primary store | Seeding event streams, asserting state |
| `GetDocumentStore()` | Primary store reference | Advanced cleanup, schema operations |
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

## Tracked Session Configuration

Tracked sessions accept configuration options for timeout extension, external-transport inclusion, multi-host tracking, and exception handling. Four knobs matter in practice:

```csharp
var session = await Host.InvokeMessageAndWaitAsync(command, opts =>
{
    // Extend timeout for slow operations (default 5s)
    opts.Timeout(30.Seconds());

    // Include messages on external transports (normally excluded from Sent/Received counts)
    opts.IncludeExternalTransports();

    // Track activity on another host in multi-host scenarios
    opts.AlsoTrack(otherHost);

    // Don't fail the test on handler exceptions — inspect yourself
    opts.DoNotAssertOnExceptionsDetected();
});

// When exceptions are suppressed, check them explicitly
if (session.HasExceptions())
{
    var exceptions = session.AllExceptions();
    // ...
}
```

### CritterBids posture

- **`opts.Timeout(30.Seconds())`** in CI where async projections or multi-hop flows exist. The default 5-second timeout is too tight for flows that traverse RabbitMQ or wait on async daemon catch-up. Apply per-test rather than globally.
- **`opts.IncludeExternalTransports()`** when asserting on integration messages routed to RabbitMQ queues that the fixture has disabled. Without it, the messages are dispatched but filtered from `tracked.Sent`.
- **`opts.AlsoTrack(otherHost)`** for multi-BC scenarios with separate fixtures (rare — most CritterBids flows live within one fixture). Relevant when testing Selling → Auctions handoff across fixture boundaries.
- **`opts.DoNotAssertOnExceptionsDetected()`** is an escape hatch. Prefer fixing the handler or explicitly catching the exception in the test. Use when the integration is known to throw under specific conditions you're validating.

---

## Testing Scheduled Messages

Scheduled messages (`bus.ScheduleAsync`, `DelayedFor`, `ScheduledAt`) appear in `tracked.Scheduled` but are NOT executed by the normal `ExecuteAndWaitAsync`. Two patterns for testing them:

### Assert the schedule, don't wait for it

```csharp
[Fact]
public async Task CloseBidding_SchedulesExtendedBiddingTimeout()
{
    await SeedOpenListing(listingId);

    var tracked = await _fixture.ExecuteAndWaitAsync(
        new CloseBidding(listingId));

    // Asserts the scheduled message exists without waiting for its scheduled time
    var scheduled = tracked.Scheduled.SingleMessage<ExtendBiddingTimeout>();
    scheduled.ListingId.ShouldBe(listingId);
}
```

Use this when you want to verify "the schedule was set up correctly" without actually running the scheduled logic — fast, deterministic, no wall-clock waits.

### Play scheduled messages immediately

`PlayScheduledMessagesAsync()` (Wolverine 4.12+) fast-forwards the schedule: it executes all scheduled messages immediately and returns a new tracked session. Use when you want to verify the **downstream effects** of the scheduled message without waiting for the scheduled time:

```csharp
[Fact]
public async Task ExtendedBiddingTimeout_ClosesAuction()
{
    await SeedOpenListing(listingId);

    // Step 1: trigger the scheduling
    var initial = await _fixture.ExecuteAndWaitAsync(new CloseBidding(listingId));
    initial.Scheduled.SingleMessage<ExtendBiddingTimeout>().ShouldNotBeNull();

    // Step 2: fast-forward the schedule
    var played = await initial.PlayScheduledMessagesAsync();

    // Step 3: assert downstream effects of the timeout handler
    played.Sent.SingleMessage<CritterBids.Contracts.Auctions.ListingSold>().ShouldNotBeNull();

    // Or verify aggregate state
    await using var session = _fixture.GetDocumentSession();
    var listing = await session.Events.AggregateStreamAsync<Listing>(listingId);
    listing!.Status.ShouldBe(ListingStatus.Closed);
}
```

This is the canonical pattern for testing auction-closing saga timeouts and any scheduled business logic without `Task.Delay`.

---

## Testing Async Projections

Async projections run on the projection daemon after `SaveChangesAsync` returns — they are NOT updated inline. Two waiting patterns:

### `WaitForNonStaleProjectionDataAsync`

The blanket approach — waits for the daemon to process all pending events across all projections:

```csharp
[Fact]
public async Task BidPlaced_UpdatesCatalogView_Async()
{
    await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, ...));

    // Wait for the async daemon to process the resulting events
    await _fixture.Host.DocumentStore()
        .WaitForNonStaleProjectionDataAsync(5.Seconds());

    await using var session = _fixture.GetDocumentSession();
    var view = await session.LoadAsync<CatalogListingView>(listingId);
    view.ShouldNotBeNull();
    view!.CurrentHighBid.ShouldBe(...);
}
```

### `WaitForConditionAsync`

For complex scenarios where the blanket wait is either too coarse (daemon catches up but a specific projection hasn't) or too broad (other daemon work delays the test), use condition-based waiting:

```csharp
[Fact]
public async Task BidPlaced_UpdatesBidderActivityView()
{
    await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, Guid.NewGuid(), bidderId, 50m));

    // Poll until the specific condition is met — bounded by timeout
    await _fixture.Host.WaitForConditionAsync(async () =>
    {
        await using var session = _fixture.GetDocumentSession();
        var activity = await session.LoadAsync<BidderActivityView>(bidderId);
        return activity?.BidCount == 1;
    }, timeout: 10.Seconds());

    // No further assertion needed — the condition IS the assertion
}
```

`WaitForConditionAsync` is the right replacement for `Task.Delay(500)` patterns. It polls with a bounded timeout and fails the test cleanly if the condition never becomes true.

**For test fixtures with async projections registered**, use `ResetAllMartenDataAsync()` rather than `CleanAllMartenDataAsync()`. The reset variant pauses the daemon, clears data, and restarts — preventing the daemon from processing stale events after cleanup.

---

## Seeding with `IInitialData`

`IInitialData` is Marten's seed-data hook — useful for reference data that every test class needs (canonical bidder accounts, marketplace definitions, staff user identities). Two registration paths:

### Register at host startup (preferred for stable reference data)

```csharp
public class CanonicalBidders : IInitialData
{
    public static readonly Guid AliceId = Guid.Parse("00000000-0000-0000-0000-000000000010");
    public static readonly Guid BobId   = Guid.Parse("00000000-0000-0000-0000-000000000011");

    public async Task Populate(IDocumentStore store, CancellationToken cancellation)
    {
        await using var session = store.LightweightSession();
        session.Store(
            new Bidder { Id = AliceId, DisplayName = "Alice Test" },
            new Bidder { Id = BobId,   DisplayName = "Bob Test"   });
        await session.SaveChangesAsync(cancellation);
    }
}

// In the fixture's AddMarten(...) lambda:
services.AddMarten(opts =>
{
    opts.Connection(postgresConnectionString);
    opts.InitialData.Add(new CanonicalBidders());
})
.UseLightweightSessions()
.ApplyAllDatabaseChangesOnStartup();
```

Seed data runs **after schema creation and before any test code runs**. It survives `CleanAllMartenDataAsync` as long as you call `Populate` again after cleanup — typically via a fixture helper:

```csharp
public async Task CleanAndReseedAsync()
{
    await Host.CleanAllMartenDataAsync();
    var store = Host.DocumentStore();
    await new CanonicalBidders().Populate(store, CancellationToken.None);
}
```

### Apply on demand in `AppFixture.InitializeAsync`

```csharp
public async Task InitializeAsync()
{
    await _postgres.StartAsync();
    Host = await AlbaHost.For<Program>(/* ... */);

    var store = Host.DocumentStore();
    await new CanonicalBidders().Populate(store, CancellationToken.None);
}
```

### When to use which

| Need | Approach |
|---|---|
| Reference data every test expects (staff users, canonical bidders) | `opts.InitialData.Add(...)` in `AddMarten` + reseed helper |
| Test-class-specific seed data | Inline seeding in the test's Arrange step |
| Seed data that must survive all cleanup operations | Custom fixture helper that reseeds after `CleanAllMartenDataAsync` |

**CritterBids status:** `IInitialData` is not currently in use — every test seeds inline. When the first cross-cutting reference-data need appears (e.g., a canonical staff user for Operations dashboard tests), this is the idiomatic pattern to reach for.

---

## Debugging Integration Tests

### `wolverine-diagnostics codegen-preview`

When a test fails unexpectedly — wrong status code, entity not loaded, event not appended, middleware not firing — the CLI's codegen preview shows the exact generated handler code:

```bash
dotnet run -- wolverine-diagnostics codegen-preview --route "POST /api/listings/{listingId}/bids"
```

The preview reveals:

- Whether `[WriteAggregate]` is resolving the aggregate correctly (property name conventions)
- Whether `[EmptyResponse]` is active on the endpoint (without it, the returned event becomes the HTTP body)
- Whether the FluentValidation middleware is in the chain (requires `UseFluentValidationProblemDetailMiddleware()`)
- The full middleware stack: `AutoApplyTransactions`, `Before`/`Validate` hooks, logging

Common issues visible in the preview:

- `[WriteAggregate]` not finding the aggregate ID: the command property is named `ListingGuid` instead of `ListingId`
- Event cascaded into the HTTP response body instead of appended to the stream: missing `[EmptyResponse]` or wrong tuple shape
- Handler not discovered: namespace filter in a BC-scoped discovery rule is excluding it

### `describe-routing`

When `tracked.Sent.MessagesOf<T>()` returns 0 unexpectedly:

```bash
dotnet run -- wolverine-diagnostics describe-routing
```

Lists every configured routing rule in the host. If your integration message type is absent from the output, the `opts.Publish(...)` rule is missing from `Program.cs` (Anti-Pattern #14 in `wolverine-message-handlers.md`) or the BC-specific exclusion in the fixture is filtering it out.

### `describe-resiliency`

For retry-policy and circuit-breaker debugging:

```bash
dotnet run -- wolverine-diagnostics describe-resiliency
```

Shows retry policies, circuit breakers, and DLQ configuration for every handler. Useful when a test expects a retry to fire but the handler fails immediately.

---

# Part III — Advanced Scenarios

## Cross-BC Handler Isolation in Test Fixtures

When a test fixture boots `AlbaHost.For<Program>()`, Wolverine's handler discovery scans all
assemblies referenced by the solution — including BC assemblies that are not provisioned in
that fixture. This causes two distinct failure modes that require explicit suppression.

### Problem 1: Foreign-BC handlers discovered but infrastructure absent

When BC-A's test fixture starts, it loads BC-B's assembly (included via `Program.cs`
`opts.Discovery.IncludeAssembly(...)`) but does not provision BC-B's infrastructure
(no Testcontainers for BC-B's database, even if all CritterBids BCs share PostgreSQL today).
BC-B's handlers are discovered by Wolverine, and when a message matching a BC-B handler type
is dispatched, Wolverine attempts to generate handler code that injects `IDocumentSession`.
If the fixture is scoped to BC-A and BC-B's services aren't registered, this can cause
startup or runtime injection errors.

**Fix: register an `IWolverineExtension` singleton that excludes foreign-BC handlers.**

```csharp
// In the fixture, inside builder.ConfigureServices:
services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

// Defined as an internal sealed class in the fixture file:
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC out of scope for this fixture",
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
        // Exclude the handler
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC out of scope for this fixture",
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
`SellingBcDiscoveryExclusion`, not `AuctionsTestExclusion`. This makes the intent clear
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

The canonical fixture is `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs`
(Marten registered in ConfigureServices). When an Auctions or Listings fixture needs to
exclude sibling-BC handlers, the `SellingBcDiscoveryExclusion` pattern is the template.

---

## Test Parallelization Strategy

Integration tests against a shared database can interfere when they modify overlapping data. The two safe strategies — and the trade-offs between them — shape how the CritterBids test suite scales.

### Two safe strategies

| Strategy | Trade-offs |
|---|---|
| **Unique entity IDs per test** | No cleanup overhead; parallelism-friendly; preferred for new tests |
| **Sequential execution with `CleanAllMartenDataAsync`** | Simpler reasoning; eliminates interference; slower |

**Prefer unique IDs when possible.** `Guid.CreateVersion7()` for Marten aggregate streams and `Guid.NewGuid()` for per-test identities give you parallelism for free — tests don't step on each other because they don't share state.

### xUnit: collection fixture for sequential execution

When tests share seeded data or the setup cost is high, use `[CollectionDefinition]` with `DisableParallelization = true`:

```csharp
[CollectionDefinition(Name, DisableParallelization = true)]
public class AuctionsTestCollection : ICollectionFixture<AuctionsTestFixture>
{
    public const string Name = "Auctions Tests";
}

[Collection(AuctionsTestCollection.Name)]
public class PlaceBidTests : IAsyncLifetime { /* ... */ }

[Collection(AuctionsTestCollection.Name)]
public class CloseBiddingTests : IAsyncLifetime { /* ... */ }
```

All test classes in the collection share one fixture (one host boot) and run one at a time — zero interference, minimal container overhead.

### xUnit: parallel classes with shared fixture

When tests use unique IDs and don't share mutable state, skip the collection definition — each class can get its own fixture and run in parallel:

```csharp
// No [Collection] attribute — xUnit parallelizes classes automatically
public class OrderQueryTests(AuctionsTestFixture fixture) : IClassFixture<AuctionsTestFixture>
{
    [Fact]
    public async Task query_with_unique_ids()
    {
        var orderId = Guid.CreateVersion7(); // unique → no collision
        await fixture.ExecuteAndWaitAsync(new CreateOrder(orderId, ...));
        // ...
    }
}
```

Each test class gets its own `AuctionsTestFixture` → its own Testcontainer → its own database. More expensive per class, fully parallel.

### Baseline safety: disable project-wide parallelism

For CritterBids today (shared-database test suite with mixed parallelization readiness), the safest baseline is project-level sequential execution:

```csharp
// In tests/CritterBids.Auctions.Tests/AssemblyInfo.cs (or any test file in the project root)
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

This runs all collections sequentially but allows tests **within** a collection to parallelize if the fixture supports it. Safe default; opt in to parallelism once unique-ID discipline is verified across the test suite.

### Tracked session timeouts under parallel load

When parallel tests contend for Testcontainer resources or daemon catchup, the default 5-second tracked session timeout can expire mid-test. Extend per-test:

```csharp
var tracked = await Host.InvokeMessageAndWaitAsync(command, opts =>
{
    opts.Timeout(30.Seconds()); // generous for loaded CI machines
});
```

See [Tracked Session Configuration](#tracked-session-configuration) for the full options surface.

### Anti-patterns

**Hard-coded IDs that collide under parallelism:**

```csharp
// ❌ WRONG — two parallel tests using the same ID read/write each other's state
var listingId = Guid.Parse("00000000-0000-0000-0000-000000000001");

// ✅ CORRECT — unique ID per test invocation
var listingId = Guid.CreateVersion7();
```

**Calling `CleanAllMartenDataAsync` mid-suite on a shared host:**

```csharp
// ❌ RISKY — resetting data while async daemon is processing can corrupt state
[Fact]
public async Task mid_test_cleanup()
{
    await Host.CleanAllMartenDataAsync(); // daemon may be mid-catchup
    // ... test body
}

// ✅ CORRECT — call cleanup at the start of the test class (InitializeAsync), not mid-test
```

**Concurrent tracked sessions on one host:**

```csharp
// ❌ WRONG — sessions can capture each other's messages
var t1 = Host.InvokeMessageAndWaitAsync(cmd1);
var t2 = Host.InvokeMessageAndWaitAsync(cmd2);
await Task.WhenAll(t1, t2);

// ✅ CORRECT — run tracked sessions sequentially within a test, or use isolated hosts per test
```

---

## Advanced Testcontainers Patterns

Beyond the standard single-Testcontainer pattern shown in the Marten BC TestFixture Pattern, three patterns are worth knowing when CritterBids grows to need them.

### Parallel container startup

When a fixture needs multiple containers (PostgreSQL + RabbitMQ + Redis), start them in parallel with `Task.WhenAll` — sequential startup adds ~3–5 seconds per container:

```csharp
public async Task InitializeAsync()
{
    _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    _rabbit   = new RabbitMqBuilder("rabbitmq:3-management-alpine").Build();
    _redis    = new RedisBuilder("redis:7-alpine").Build();

    // Parallel startup — saves ~10s vs sequential on a cold machine
    await Task.WhenAll(
        _postgres.StartAsync(),
        _rabbit.StartAsync(),
        _redis.StartAsync()
    );

    // Configure host to use all three
    Host = await AlbaHost.For<Program>(x =>
    {
        x.ConfigureServices(services =>
        {
            services.AddMarten(opts => opts.Connection(_postgres.GetConnectionString()));
            services.AddWolverine(opts => opts.UseRabbitMq(_rabbit.GetConnectionString()));
            // ... redis wiring
        });
    });
}
```

### `PullPolicy.Missing` for CI

Always use `PullPolicy.Missing` in CI pipelines that cache Docker images. Without it, Testcontainers pulls the image on every test run:

```csharp
_postgres = new PostgreSqlBuilder("postgres:17-alpine")
    .WithPullPolicy(PullPolicy.Missing) // use cached image if present
    .Build();
```

Not needed locally (Docker layer cache handles it), but saves substantial CI time on every run.

### Dynamic database per fixture (shared container)

When per-fixture isolation is needed but per-fixture container startup is too expensive, share one container but create a separate database per fixture:

```csharp
public class IsolatedDbFixture : IAsyncLifetime
{
    // One container shared across all fixtures in the collection
    private static readonly PostgreSqlContainer SharedPostgres =
        new PostgreSqlBuilder("postgres:17-alpine")
            .WithPullPolicy(PullPolicy.Missing)
            .Build();

    private string _database = $"testdb_{Guid.NewGuid():N}";
    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Idempotent — Testcontainers handles "already started"
        await SharedPostgres.StartAsync();

        // Create an isolated database for this fixture
        await using var conn = new NpgsqlConnection(SharedPostgres.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_database}\"", conn);
        await cmd.ExecuteNonQueryAsync();

        var dbConnectionString = new NpgsqlConnectionStringBuilder(SharedPostgres.GetConnectionString())
        {
            Database = _database
        }.ConnectionString;

        Host = await AlbaHost.For<Program>(b => b.ConfigureServices(services =>
        {
            services.AddMarten(opts => opts.Connection(dbConnectionString))
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine();
            services.AddSellingModule();
            services.RunWolverineInSoloMode();
            services.DisableAllExternalWolverineTransports();
        }));
    }

    public async Task DisposeAsync()
    {
        if (Host != null) await Host.DisposeAsync();

        // Clean up the isolated database — terminate connections first
        await using var conn = new NpgsqlConnection(SharedPostgres.GetConnectionString());
        await conn.OpenAsync();
        await using var term = new NpgsqlCommand(
            $"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '{_database}'", conn);
        await term.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\"", conn);
        await drop.ExecuteNonQueryAsync();
    }
}
```

One container (fast startup), fully isolated schemas per fixture. The right approach when CritterBids' test suite reaches the scale where per-fixture containers become a bottleneck.

### RabbitMQ integration tests with dynamic virtual hosts

For integration tests that exercise RabbitMQ routing (not just `DisableAllExternalWolverineTransports`), use a dedicated virtual host per fixture so queues don't bleed across runs:

```csharp
private readonly string _vhost = $"test-{Guid.NewGuid():N}";

public async Task InitializeAsync()
{
    _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    _rabbit   = new RabbitMqBuilder("rabbitmq:3-management-alpine").Build();

    await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

    // Create isolated vhost for this fixture
    var mgmt = new RabbitMqManagementClient(_rabbit.GetConnectionString());
    await mgmt.CreateVirtualHostAsync(_vhost);

    Host = await AlbaHost.For<Program>(b => b.ConfigureServices(services =>
    {
        services.AddMarten(opts => opts.Connection(_postgres.GetConnectionString()));
        services.AddWolverine(opts =>
            opts.UseRabbitMq(_rabbit.GetConnectionString())
                .AutoProvision()
                .SetVirtualHost(_vhost));
    }));
}
```

**CritterBids status:** This pattern is forward-looking. Current tests use `DisableAllExternalWolverineTransports()` and stub local queues (see [Cross-BC Handler Isolation](#cross-bc-handler-isolation-in-test-fixtures)). Real RabbitMQ integration tests arrive when a true end-to-end multi-node flow needs verification.

### Trade-offs summary

| Approach | Isolation | Startup cost | Parallelism |
|---|---|---|---|
| Shared AppFixture + `CleanAllMartenDataAsync` | Low (shared DB, sequential) | One startup | Sequential |
| Shared AppFixture + unique IDs | Medium (shared DB) | One startup | Parallel-friendly |
| Testcontainer per test class | High (dedicated DB) | ~3–5s per fixture | Fully parallel |
| Dynamic DB per fixture (shared container) | High (dedicated DB) | ~1s per fixture | Fully parallel |
| Testcontainer per test method | Very high | Too expensive | Not recommended |

### Relevant NuGet packages

```xml
<PackageReference Include="Testcontainers.PostgreSql" Version="3.*" />
<PackageReference Include="Testcontainers.RabbitMq"   Version="3.*" />
<PackageReference Include="Testcontainers.Redis"       Version="3.*" />
```

---

## Polecat BC TestFixture Pattern (Archived)

> **Status: 📚 Reference — not currently used.**
>
> Per [ADR 011 — All-Marten Pivot](../decisions/011-all-marten-pivot.md), CritterBids migrated every BC to Marten/PostgreSQL. No test fixture in CritterBids uses Polecat today. This section is retained for:
>
> - Polecat-based sibling projects outside CritterBids
> - The historical record of the M1 Participants BC test fixture that ran on Polecat before the pivot
> - Reference when evaluating the Polecat test tooling surface
>
> The technical content below is accurate for Polecat 2.x — M1 findings, verified extension-method targets (`Host.Services.*`, not `Host.*`), Testcontainers image pinning — but describes infrastructure that is not provisioned in the current CritterBids test suite.

Polecat BCs use SQL Server rather than PostgreSQL. The fixture shape is identical to Marten BCs — the only differences are the container, connection string override, and cleanup helpers.

```csharp
// Verified from the archived CritterBids.Participants.Tests/Fixtures/ParticipantsTestFixture.cs (M1-S7, pre-ADR 011)
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

### Key differences between Marten and Polecat fixtures

| Concern | Marten BC | Polecat BC |
|---|---|---|
| Container | `PostgreSqlBuilder("postgres:17-alpine")` | `MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04")` |
| Connection override | `services.AddMarten(opts => { opts.Connection(...); ... }).UseLightweightSessions()...` in `ConfigureServices` | `services.AddParticipantsModule(testConnectionString)` in `ConfigureServices` |
| Solo mode | `services.RunWolverineInSoloMode()` required | Not required (Polecat doesn't use advisory locks the same way) |
| `IDocumentStore` retrieval | `Host.DocumentStore()` extension on `IAlbaHost` | `Host.Services.DocumentStore()` extension on `IServiceProvider` |
| Data cleanup | `Host.CleanAllMartenDataAsync()` | `Host.Services.CleanAllPolecatDataAsync()` |
| Daemon catchup | `store.WaitForNonStaleProjectionDataAsync(timeout)` | `Host.Services.ForceAllPolecatDaemonActivityToCatchUpAsync(ct)` |
| Helper extension host | `IAlbaHost` | `IServiceProvider` — use `Host.Services.*` |

### Polecat-specific testing helpers

> ⚠️ **Confirmed from M1 (S7):** These helpers are extension methods on `IServiceProvider`, not `IHost`. Always call on `Host.Services`, not directly on `Host`.

```csharp
await host.Services.CleanAllPolecatDataAsync();      // Most common — use in InitializeAsync()
await host.Services.ResetAllPolecatDataAsync();      // When async projections registered
await host.Services.ForceAllPolecatDaemonActivityToCatchUpAsync(ct); // Better than WaitForNonStale
var store = host.Services.DocumentStore();           // Extension on IServiceProvider
```

---

## Key Principles

1. **Standardized TestFixture across all BCs** — same helper methods, same patterns
2. **Register stores AND BC modules in `ConfigureServices`** — `ConfigureAppConfiguration` does NOT propagate to Program.cs's inline `builder.Configuration.GetConnectionString(...)` calls. All store and module registrations must use `ConfigureServices` directly.
3. **Single storage backend per fixture** — CritterBids is Marten-only under ADR 011. A fixture that tried to provision both Marten and Polecat would register two "main" Wolverine message stores and fail with `InvalidWolverineStorageConfigurationException`.
4. **`RunWolverineInSoloMode()` + `DisableAllExternalWolverineTransports()`** — both required in every Marten BC test fixture
5. **`CleanAllMartenDataAsync()` in `InitializeAsync()`** — not `DisposeAsync()`; ensures clean state before each test class
6. **Use `ResetAllMartenDataAsync()` when async projections are registered** — prevents daemon processing stale data
7. **`ExecuteAndWaitAsync` over HTTP POST + GET** — eliminates race conditions in ES tests; `WaitForConditionAsync` for complex async scenarios
8. **Prefer unique IDs over sequential cleanup** — parallelism-friendly and no per-test cleanup cost
9. **Assert full integration message payloads** — not just that the type was published
10. **Real infrastructure via Testcontainers** — real PostgreSQL, not SQLite or in-memory
11. **`IWolverineExtension` exclusion for cross-BC isolation** — when a fixture doesn't provision a BC's infrastructure, exclude that BC's handlers to prevent Wolverine code-gen failures
12. **`PlayScheduledMessagesAsync` over `Task.Delay`** — fast-forward scheduled messages deterministically instead of wall-clock waiting

---

## References

- `docs/skills/marten-event-sourcing.md` — event sourcing race conditions, projection behavior, async daemon configuration
- `docs/skills/marten-projections.md` — projection type reference; testing native and EF Core projections
- `docs/skills/adding-bc-module.md` — BC module registration, ConfigureMarten pattern, fixture setup
- `docs/skills/wolverine-sagas.md` — saga testing patterns
- `docs/skills/wolverine-message-handlers.md` — handler patterns, ProblemDetails behavior (Anti-Pattern #14)
- `docs/skills/polecat-event-sourcing.md` — 📚 Reference; SQL Server + Polecat specifics (not active under ADR 011)
- `docs/decisions/009-shared-marten-store.md` — shared primary store ADR
- `docs/decisions/011-all-marten-pivot.md` — all-Marten pivot (supersedes ADR 003)
- `tests/CritterBids.Selling.Tests/Fixtures/SellingTestFixture.cs` — canonical Marten BC fixture
- [Alba HTTP Testing](https://jasperfx.github.io/alba/)
- [Testcontainers .NET](https://dotnet.testcontainers.org/)
- [Working and Testing Against Scheduled Messages](https://jeremydmiller.com/2025/09/15/working-and-testing-against-scheduled-messages-with-wolverine/) — Jeremy Miller's blog post on `PlayScheduledMessagesAsync`
- [Faster, More Reliable Integration Testing Against Marten Projections](https://jeremydmiller.com/2025/08/19/faster-more-reliable-integration-testing-against-marten-projections-or-subscriptions/)
