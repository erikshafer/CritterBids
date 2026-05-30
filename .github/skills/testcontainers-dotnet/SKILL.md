---
name: testcontainers-dotnet
description: Write reliable integration tests for CritterBids using Testcontainers to spin up a real PostgreSQL instance for Marten. Use when writing or fixing integration tests for an event-sourced aggregate, projection, saga, or Wolverine handler; when a test needs a real database instead of an in-memory fake; or when integration tests are flaky, slow, or leaking state between runs. Covers the shared-container xUnit collection fixture, wait strategies, wiring the container into Marten, and the deterministic-stream-id isolation trap.
license: MIT
compatibility: Requires Docker and a .NET test runner (xUnit).
metadata:
  author: CritterBids
  version: "0.1"
  status: draft-pending-accepted
  source: "Adapted from the testcontainers-dotnet skill in testcontainers/claude-skills (MIT)"
---

# Testcontainers — CritterBids (.NET / Marten / Postgres)

> Adapted from the MIT-licensed `testcontainers-dotnet` skill in testcontainers/claude-skills. Retargeted from its SQL Server examples to Marten-on-Postgres with xUnit, and extended with the event-sourcing isolation guidance that matters for this codebase.

## Why real Postgres, not in-memory

CritterBids is event-sourced on Marten. In-memory fakes hide exactly the things Marten integration tests exist to catch: real projection rebuilds, stream concurrency, JSONB serialization, `mt_*` schema generation, and SQL the document store actually emits. Test against a real Postgres or you're testing a different system than you ship.

## The load-bearing rule: wait, never sleep

Always attach an explicit wait strategy so the container is ready before tests run. **Never** `Thread.Sleep`/`Task.Delay` as a substitute — it's the #1 source of flaky CI. The `PostgreSqlBuilder` ships a sensible default readiness probe; rely on it.

## Shared container via Alba + xUnit collection fixture

CritterBids uses **Alba** as the HTTP integration harness. The pattern is a shared `IAlbaHost` managed by a collection fixture — one Testcontainers PostgreSQL per test collection, all tests in the collection execute sequentially against it.

```csharp
using Alba;
using Testcontainers.PostgreSql;
using Xunit;
using Marten;

public class AuctionsTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("critterbids_test")
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override the Aspire-guarded Marten registration with Testcontainers
                services.AddMarten(opts =>
                {
                    opts.Connection(connectionString);
                    opts.DatabaseSchemaName = "public";
                    opts.Events.AppendMode = EventAppendMode.Quick;
                    opts.Events.UseMandatoryStreamTypeDeclaration = true;
                    opts.DisableNpgsqlLogging = true;
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true);

                // Register the BC module so its ConfigureMarten() contributions (schema, projections) are present
                services.AddAuctionsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
            });
        });
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await _postgres.StopAsync();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public class AuctionsTestCollection : ICollectionFixture<AuctionsTestFixture>
{
    public const string Name = "Auctions Integration Tests";
}

[Collection(AuctionsTestCollection.Name)]
public class AuctionClosingTests(AuctionsTestFixture fixture)
{
    [Fact]
    public async Task closing_a_timed_auction_emits_listing_sold()
    {
        // Use fixture.Host to invoke handlers and assert side effects
        var result = await fixture.Host.Scenario(s =>
        {
            s.Post.Json(new CloseAuction(/* ... */)).ToUrl("/auctions/close");
        });

        result.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    }
}
```

**Key points:**
- `[CollectionDefinition(..., DisableParallelization = true)]` ensures sequential execution so deterministic stream IDs don't collide mid-test.
- `opts.DatabaseSchemaName = "public"` keeps all tests in the same schema (the isolation comes from sequential execution, not per-test databases).
- `services.AddAuctionsModule()` wires the real BC registrations (same as production `Program.cs`).
- `DisableAllExternalWolverineTransports()` and `RunWolverineInSoloMode()` prevent the app from trying to connect to RabbitMQ or external brokers in tests.
- Use `fixture.Host.Scenario()` to invoke HTTP endpoints and assert responses.

## Wiring the container into Alba

Use the fixture's connection string to override Marten in `ConfigureServices`, then register the BC module so its real document types and projections are present:

```csharp
// In InitializeAsync() of your fixture:
Host = await AlbaHost.For<Program>(builder =>
{
    builder.ConfigureServices(services =>
    {
        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);
            opts.DatabaseSchemaName = "public";
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.UseMandatoryStreamTypeDeclaration = true;
        })
        .UseLightweightSessions()
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

        // Mirror the BC's real registration
        services.AddAuctionsModule();

        services.RunWolverineInSoloMode();
        services.DisableAllExternalWolverineTransports();
    });
});

// In test methods:
[Fact]
public async Task starting_an_auction_opens_bidding()
{
    var result = await fixture.Host.Scenario(s =>
    {
        s.Post.Json(new StartAuction(listingId: /* ... */))
            .ToUrl("/auctions/start");
    });

    result.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
    var projection = await fixture.Host.GetAsJson<LiveAuction>($"/auctions/{listingId}");
    projection.Status.ShouldBe(AuctionStatus.Open);
}
```

Use **Shouldly** for assertions (`.ShouldBe()`, `.ShouldContain()`, etc.) to match the CritterBids test stack.

## The isolation trap: deterministic stream IDs

CritterBids uses UUIDNext v5 **deterministic** stream IDs. That's correct for production, but in a shared-container test suite the same logical input produces the **same** stream id across tests — so two tests writing "auction #1" collide in the same database and corrupt each other's state.

Pick one isolation strategy and apply it consistently:

- **Per-test database or schema** (cleanest for event-sourcing): give each test/class its own database name or Marten `DatabaseSchemaName`, so deterministic ids never share a stream table.
- **Marten tenancy**: run tests under a per-test tenant id.
- **Randomized seed for the id** in tests where determinism isn't what's under test.

Do **not** rely on "delete rows between tests" against a shared schema — it's slow, races parallel tests, and still collides on deterministic ids mid-test.

## Cleanup and resources

- `DisposeAsync` on the fixture stops and removes the container — no manual `docker rm`.
- Don't expose host ports in tests; Testcontainers maps a random free port and `GetConnectionString()` returns it. Hardcoding 5432 fights any locally running Postgres.
- Override the default wait only if you add services the default probe doesn't cover.

## Anti-patterns

- `Thread.Sleep` to "wait for the DB" — use the wait strategy.
- A fresh container per test — share via collection fixture.
- Reusing deterministic stream ids across tests in one shared schema.
- In-memory Marten/document fakes for behavior that depends on real projection or SQL semantics.
- Leaving the prod `postgres` tag unpinned so CI drifts from local.
