# Critter Stack Testing Patterns

Patterns for testing Wolverine handlers, Marten aggregates, and Alba HTTP scenarios in CritterBids.

---

## Table of Contents

1. [Core Philosophy](#core-philosophy)
2. [TestFixture Pattern](#testfixture-pattern)
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

---

## Core Philosophy

1. **Prefer integration tests over unit tests** — test complete vertical slices
2. **Use real infrastructure** — Testcontainers for PostgreSQL, RabbitMQ
3. **Pure functions are easy to unit test** — thanks to A-Frame architecture
4. **BDD-style for integration tests** — focus on behavior, not implementation

## Tools

| Tool | Purpose |
|---|---|
| **xUnit** | Test framework |
| **Shouldly** | Readable assertions |
| **Alba** | HTTP integration testing via ASP.NET Core TestServer |
| **Testcontainers** | Real PostgreSQL/RabbitMQ in Docker |
| **NSubstitute** | Mocking (only when necessary) |

---

## TestFixture Pattern

One standardized fixture per BC. All BCs share the same shape.

```csharp
namespace CritterBids.Auctions.IntegrationTests;

public class AuctionsTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("auctions_test_db")
        .WithName($"auctions-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.ConfigureMarten(opts => opts.Connection(connectionString));
                services.DisableAllExternalWolverineTransports();

                // Auth setup — see Test Authentication section
                services.AddSingleton<ITestAuthContext, TestAuthContext>();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                // Bypass all authorization policies
                services.AddAuthorization(opts =>
                {
                    opts.AddPolicy("StaffOnly", p => p.RequireAssertion(_ => true));
                });
            });
        });

        Host.AddDefaultAuthHeader(); // Inject Authorization header into all Alba scenarios
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

    // Core helper methods — every fixture exposes these
    public IDocumentSession GetDocumentSession() =>
        Host.Services.GetRequiredService<IDocumentStore>().LightweightSession();

    public IDocumentStore GetDocumentStore() =>
        Host.Services.GetRequiredService<IDocumentStore>();

    public async Task CleanAllDocumentsAsync() =>
        await GetDocumentStore().Advanced.Clean.DeleteAllDocumentsAsync();

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host)
            .ExecuteAndWaitAsync(async ctx => await ctx.InvokeAsync(message));
    }

    protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
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

**After creating the AlbaHost, always call `Host.AddDefaultAuthHeader()`** — this injects `Authorization: Bearer test-token` into every Alba scenario. Without it, all HTTP requests fail with 401.

### Authorization Policy Bypass

Bypass all authorization policies in the test fixture. Missing a policy causes silent 403s:

```csharp
services.AddAuthorization(opts =>
{
    // Bypass every policy used in this BC
    opts.AddPolicy("StaffOnly", p => p.RequireAssertion(_ => true));
    opts.AddPolicy("AnotherPolicy", p => p.RequireAssertion(_ => true));
});
```

Find all policies in a BC:
```bash
grep -r "Authorize(Policy" src/CritterBids.<BcName>/
```

---

## Test Isolation

### Checklist

**Fixture setup:**
- [ ] `DisableAllExternalWolverineTransports()` called
- [ ] Test connection string overrides production
- [ ] Collection fixture defined for sequential execution

**Test class:**
- [ ] Implements `IAsyncLifetime`
- [ ] `InitializeAsync()` calls `_fixture.CleanAllDocumentsAsync()`
- [ ] `DisposeAsync()` returns `Task.CompletedTask`
- [ ] `[Collection(IntegrationTestCollection.Name)]` attribute present

**Test methods:**
- [ ] Each test seeds its own data inline
- [ ] Tests do not rely on data from other tests
- [ ] Tests can run in any order

### Collection Fixture for Sequential Execution

```csharp
[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<AuctionsTestFixture>
{
    public const string Name = "Auctions Integration Tests";
}

[Collection(IntegrationTestCollection.Name)]
public class BidPlacementTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public BidPlacementTests(AuctionsTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.CleanAllDocumentsAsync();
    public Task DisposeAsync() => Task.CompletedTask;
}
```

### Seed Data Isolation

Test classes that call `CleanAllDocumentsAsync()` in `DisposeAsync()` can wipe seed data before verification tests run — xUnit does not guarantee class execution order.

Seed data verification classes must reseed in `InitializeAsync()` and must NOT clean in `DisposeAsync()`:

```csharp
[Collection(IntegrationTestCollection.Name)]
public class SeedDataTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;
    public SeedDataTests(AuctionsTestFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ReseedAsync(); // Reseed before verifying
    public Task DisposeAsync() => Task.CompletedTask; // Do NOT clean — leave seed data

    [Fact]
    public async Task Should_have_seeded_demo_listings() { /* verify */ }
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
[Collection(IntegrationTestCollection.Name)]
public class PlaceBidTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;
    public PlaceBidTests(AuctionsTestFixture fixture) => _fixture = fixture;
    public async Task InitializeAsync() => await _fixture.CleanAllDocumentsAsync();
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

    [Fact]
    public async Task PlaceBid_BelowCurrentHigh_Returns400()
    {
        // Arrange — listing with existing bid
        await _fixture.ExecuteAndWaitAsync(new PlaceBid(listingId, ..., amount: 50m));

        var cmd = new PlaceBid(listingId, Guid.NewGuid(), bidderId, 30m); // Below current high

        await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(cmd).ToUrl($"/api/listings/{listingId}/bids");
            s.StatusCodeShouldBe(400);
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
| `GetDocumentSession()` | Direct DB access | Seeding event streams, asserting state |
| `GetDocumentStore()` | Advanced operations | Cleanup |
| `CleanAllDocumentsAsync()` | Clear all data | `InitializeAsync()` of every test class |
| `ExecuteAndWaitAsync<T>()` | Invoke via Wolverine pipeline | Testing state-changing commands |
| `TrackedHttpCall()` | HTTP + message tracking | Asserting integration messages published |

### ⚠️ AutoApplyTransactions Does Not Fire on Direct Handler Calls

`AutoApplyTransactions()` only fires through the Wolverine pipeline — not when you call a handler method directly in a test. If you call a handler directly, you must `await session.SaveChangesAsync()` explicitly. Use `ExecuteAndWaitAsync()` to avoid this entirely.

```csharp
// ❌ WRONG — changes silently discarded
var events = PlaceBidHandler.Handle(cmd, listing);
// No SaveChangesAsync call — nothing persisted!

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

Test Decider pure functions in the same way:

```csharp
[Fact]
public void AuctionClosingDecider_WhenBidInExtensionWindow_ExtendsCloseTime()
{
    var saga = new AuctionClosingSaga
    {
        ExtendedBiddingEnabled = true,
        ExtensionWindowMinutes = 2,
        ScheduledCloseAt = DateTimeOffset.UtcNow.AddSeconds(90) // within window
    };

    var decision = AuctionClosingDecider.HandleBidPlaced(saga, bid, DateTimeOffset.UtcNow);

    decision.NewCloseAt.ShouldNotBeNull();
}
```

---

## Testing Validators

```csharp
public class PlaceBidValidatorTests
{
    private readonly PlaceBidValidator _validator = new();

    [Fact]
    public void Validate_WithZeroAmount_Fails()
    {
        var cmd = new PlaceBid(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Amount: 0m);
        var result = _validator.Validate(cmd);
        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == "Amount");
    }

    [Fact]
    public void Validate_WithValidCommand_Passes()
    {
        var cmd = new PlaceBid(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Amount: 50m);
        _validator.Validate(cmd).IsValid.ShouldBeTrue();
    }
}
```

---

## Testing Time-Dependent Handlers

Handlers that check elapsed time must use an injectable clock — never `DateTimeOffset.UtcNow` directly.

### Infrastructure

```csharp
// Domain project (production code)
public interface ISystemClock { DateTimeOffset UtcNow { get; } }
public class SystemClock : ISystemClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

// Test project only
public class FrozenSystemClock : ISystemClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
```

Register in production: `services.AddSingleton<ISystemClock, SystemClock>()`

Expose on fixture and override in tests:

```csharp
// In TestFixture
public FrozenSystemClock Clock { get; private set; } = new();

// During host init:
services.RemoveAll<ISystemClock>();
services.AddSingleton<ISystemClock>(Clock); // Share fixture's instance
```

**Always reset the clock in `InitializeAsync()` of each test class.** The clock is a singleton shared across the xUnit collection — one class advancing it affects subsequent classes.

```csharp
public async Task InitializeAsync()
{
    await _fixture.CleanAllDocumentsAsync();
    _fixture.Clock.UtcNow = DateTimeOffset.UtcNow; // Reset every time
}

[Fact]
public async Task ObligationSaga_After3Days_EscalatesDeadline()
{
    await SeedActiveObligation(obligationId);

    _fixture.Clock.UtcNow = DateTimeOffset.UtcNow.AddDays(3); // Advance past deadline
    await _fixture.ExecuteAndWaitAsync(new CheckObligationDeadlines());

    using var session = _fixture.GetDocumentSession();
    var saga = await session.LoadAsync<ObligationsSaga>(obligationId);
    saga!.Status.ShouldBe(ObligationStatus.Escalated);
}
```

### Handler Pattern

```csharp
public static async Task Handle(
    CheckObligationDeadlines message,
    IDocumentSession session,
    ISystemClock clock) // Injected — never DateTimeOffset.UtcNow directly
{
    var daysSinceShipBy = (clock.UtcNow - deadline).TotalDays;
    if (daysSinceShipBy < 3) return;
    // escalate...
}
```

---

## Testing Failure Paths

When the default test fixture uses a stub that always succeeds, testing failure paths requires a dedicated fixture with a failing implementation — to avoid breaking every happy-path test in the main collection.

```csharp
// Production stub (always succeeds in tests)
public class StubCarrierTrackingService : ICarrierTrackingService
{
    public Task<TrackingResult> GetStatusAsync(string trackingNumber, CancellationToken ct)
        => Task.FromResult(new TrackingResult("DELIVERED", DateTimeOffset.UtcNow, true));
}

// Failure test stub (always fails)
public class AlwaysFailingCarrierTrackingService : ICarrierTrackingService
{
    public Task<TrackingResult> GetStatusAsync(string trackingNumber, CancellationToken ct)
        => Task.FromResult(new TrackingResult(null, null, false, "Carrier API unavailable (test stub)"));
}
```

Separate collection for failure tests:

```csharp
[CollectionDefinition(Name)]
public class TrackingFailureTestCollection : ICollectionFixture<TrackingFailureTestFixture>
{
    public const string Name = "Tracking Failure Tests";
}

public class TrackingFailureTestFixture : IAsyncLifetime
{
    // Identical to main fixture but swaps the stub:
    // services.RemoveAll<ICarrierTrackingService>();
    // services.AddSingleton<ICarrierTrackingService, AlwaysFailingCarrierTrackingService>();
}

[Collection(TrackingFailureTestCollection.Name)]
public class TrackingFailureTests : IAsyncLifetime
{
    [Fact]
    public async Task WhenCarrierUnavailable_ObligationFlagsForManualReview() { /* ... */ }
}
```

A separate `[CollectionDefinition]` creates a fully isolated fixture with its own PostgreSQL container. The cost is a second container startup — acceptable for a small set of targeted failure-path tests.

### Decision Guide

| Situation | Approach |
|---|---|
| Default stub "never fails"; need failure coverage | Separate fixture + separate xUnit collection |
| Stub has a conditional failure mode | `RemoveAll + AddSingleton` per test class in main fixture |
| Time-dependent behavior | `FrozenSystemClock` on main fixture; reset in `InitializeAsync()` |

---

## Shouldly Assertions

```csharp
// Null / existence
result.ShouldNotBeNull();
result.ShouldBeNull();

// Value equality
result.Status.ShouldBe(ListingStatus.Open);
result.CurrentHighBid.ShouldBe(75m);

// Numeric comparisons
result.BidCount.ShouldBeGreaterThan(0);
result.Amount.ShouldBeInRange(10m, 500m);

// Collections
events.ShouldNotBeEmpty();
events.Count.ShouldBe(1);
events.ShouldContain(e => e is BidPlaced);

// Exceptions
Should.Throw<InvalidOperationException>(() => /* ... */);
await Should.ThrowAsync<InvalidOperationException>(async () => /* ... */);
```

---

## Test Organization

```
tests/
  Auctions/
    CritterBids.Auctions.IntegrationTests/
      Fixtures/
        AuctionsTestFixture.cs
        IntegrationTestCollection.cs
      Bidding/
        PlaceBidTests.cs
        BidRejectionTests.cs
      AuctionClosing/
        AuctionClosingTests.cs
        ExtendedBiddingTests.cs
    CritterBids.Auctions.UnitTests/
      Handlers/
        PlaceBidHandlerTests.cs
      Deciders/
        AuctionClosingDeciderTests.cs
      Validators/
        PlaceBidValidatorTests.cs
  Settlement/
    CritterBids.Settlement.IntegrationTests/
    CritterBids.Settlement.UnitTests/
  Obligations/
    ...
```

---

## Key Principles

1. **Standardized TestFixture across all BCs** — same helper methods, same patterns
2. **Integration tests cover vertical slices** — HTTP request through to database assertion
3. **Unit tests cover pure functions** — `Before()`, `Handle()`, `Apply()`, Decider methods
4. **Use stubs for external services** — tests must not depend on third-party APIs
5. **Real infrastructure via Testcontainers** — real PostgreSQL, not SQLite or in-memory
6. **Collection fixtures enforce sequential execution** — prevents DDL concurrency errors
7. **`ExecuteAndWaitAsync` over HTTP POST + GET** — eliminates race conditions in ES tests
8. **Assert full integration message payloads** — not just that the type was published

---

## References

- `docs/skills/marten-event-sourcing.md` — event sourcing race conditions, projection behavior
- `docs/skills/wolverine-sagas.md` — saga testing patterns
- `docs/skills/wolverine-message-handlers.md` — handler patterns, ProblemDetails behavior
- [Alba HTTP Testing](https://jasperfx.github.io/alba/)
- [Testcontainers .NET](https://dotnet.testcontainers.org/)


---

## Polecat BC TestFixture Pattern

Polecat BCs (Participants, Operations, Settlement) use SQL Server rather than PostgreSQL. The fixture shape is identical to Marten BCs — the only differences are the container, connection string, and cleanup helpers.

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
                // Use ConfigurePolecat (IOptions override), NOT AddPolecat (competing store registration).
                // ConfigurePolecat adds to the IOptions<PolecatOptions> chain and correctly overrides
                // the connection string that AddParticipantsModule registered, while preserving
                // DatabaseSchemaName, AutoCreateSchemaObjects, and all other module settings.
                services.ConfigurePolecat(opts =>
                {
                    opts.ConnectionString = connectionString;
                });

                // Disable RabbitMQ and any external Wolverine transports.
                // Wolverine inbox/outbox (backed by SQL Server) remains active.
                services.DisableAllExternalWolverineTransports();

                // Auth setup — add when BC has [Authorize] endpoints (M2+).
                // M1 Participants uses [AllowAnonymous] on all endpoints, so no auth wiring needed here.
            });
        });

        // Call Host.AddDefaultAuthHeader() here if BC has [Authorize] endpoints.
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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    // Note: DocumentStore(), CleanAllPolecatDataAsync(), TrackActivity(), and ExecuteAndWaitAsync()
    // are extension methods on IServiceProvider, not on IHost. Always use Host.Services.
    public IDocumentSession GetDocumentSession() =>
        Host.Services.DocumentStore().LightweightSession();

    public IDocumentStore GetDocumentStore() =>
        Host.Services.DocumentStore();

    /// <summary>
    /// Cleans all Polecat documents AND event data in one call.
    /// Call in InitializeAsync() of each test class to ensure test isolation.
    /// </summary>
    public Task CleanAllPolecatDataAsync() =>
        Host.Services.CleanAllPolecatDataAsync();

    /// <summary>
    /// Pauses async daemon, cleans all data, then resumes the daemon.
    /// Use this when async projections are registered in the BC.
    /// </summary>
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

### Key Differences from Marten Fixtures

| Concern | Marten BC | Polecat BC |
|---|---|---|
| Container | `PostgreSqlBuilder` (Testcontainers) | `MsSqlBuilder` (Testcontainers) |
| Image | `postgres:18-alpine` | `mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04` |
| MsSqlBuilder ctor | — | Pass image tag in constructor: `new MsSqlBuilder("image:tag")` not `.WithImage()` (obsolete) |
| Connection override | `services.ConfigureMarten(opts => opts.Connection(...))` | `services.ConfigurePolecat(opts => { opts.ConnectionString = ...; })` — NOT `AddPolecat` |
| Data cleanup | `store.Advanced.Clean.DeleteAllDocumentsAsync()` | `host.Services.CleanAllPolecatDataAsync()` |
| Daemon catchup | `store.WaitForNonStaleProjectionDataAsync(timeout)` | `host.Services.ForceAllPolecatDaemonActivityToCatchUpAsync(ct)` |
| Extension method host | `IHost` / `IServiceProvider` (mixed) | All Polecat helpers are on `IServiceProvider` — use `Host.Services.*` |

### Polecat-Specific Testing Helpers

`Wolverine.Polecat` provides first-class test helpers — prefer these over calling the raw store methods.

> ⚠️ **Confirmed from M1 (S5):** These helpers are extension methods on `IServiceProvider`, not `IHost`.
> Always call them on `host.Services`, not directly on `host`:

```csharp
// Clean docs + events atomically (most common — use in InitializeAsync)
await host.Services.CleanAllPolecatDataAsync();

// Pause daemon, clean, resume — use when async projections are registered
await host.Services.ResetAllPolecatDataAsync();

// Force all async projections to catch up immediately
// Better than WaitForNonStaleProjectionDataAsync for test reliability
await host.Services.ForceAllPolecatDaemonActivityToCatchUpAsync(cancellationToken);

// Get the document store (extension on IServiceProvider)
var store = host.Services.DocumentStore();

// Save to Polecat + wait for outgoing messages to flush
await host.Services.SaveInPolecatAndWaitForOutgoingMessagesAsync(session =>
{
    session.Events.Append(streamId, new SomeEvent());
});
```

For `TrackedSessionConfiguration` (Wolverine tracked sessions):

```csharp
// After execution, wait for non-stale daemon data
configuration.WaitForNonStaleDaemonDataAfterExecution(TimeSpan.FromSeconds(10));

// Pause daemon before, force catch-up after — cleanest option for async projection tests
configuration.PauseThenCatchUpOnPolecatDaemonActivity();
```

### Test Isolation for Polecat BCs

Same rules as Marten BCs. In `InitializeAsync()` of each test class:

```csharp
public async Task InitializeAsync() => await _fixture.CleanAllPolecatDataAsync();
```

If the BC has async projections, prefer `ResetAllPolecatDataAsync()` to ensure the daemon is paused before
data is wiped (avoids race conditions between the daemon catching up on stale event data and the test writing
fresh data). Participants BC has no async projections in M1, so `CleanAllPolecatDataAsync()` is sufficient.

