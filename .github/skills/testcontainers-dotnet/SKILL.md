---
name: testcontainers-dotnet
description: Write reliable integration tests for CritterBids using Testcontainers to spin up a real PostgreSQL instance for Marten. Use when writing or fixing integration tests for an event-sourced aggregate, projection, saga, or Wolverine handler; when a test needs a real database instead of an in-memory fake; or when integration tests are flaky, slow, or leaking state between runs. Covers the shared-container xUnit collection fixture, wait strategies, wiring the container into Marten, and the deterministic-stream-id isolation trap.
license: MIT
compatibility: Requires Docker and a .NET test runner (xUnit).
metadata:
  author: CritterBids
  version: "0.1"
  status: draft-pending-review
  source: "Adapted from the testcontainers-dotnet skill in testcontainers/claude-skills (MIT)"
---

# Testcontainers — CritterBids (.NET / Marten / Postgres)

> Adapted from the MIT-licensed `testcontainers-dotnet` skill in testcontainers/claude-skills. Retargeted from its SQL Server examples to Marten-on-Postgres with xUnit, and extended with the event-sourcing isolation guidance that matters for this codebase.

## Why real Postgres, not in-memory

CritterBids is event-sourced on Marten. In-memory fakes hide exactly the things Marten integration tests exist to catch: real projection rebuilds, stream concurrency, JSONB serialization, `mt_*` schema generation, and SQL the document store actually emits. Test against a real Postgres or you're testing a different system than you ship.

## The load-bearing rule: wait, never sleep

Always attach an explicit wait strategy so the container is ready before tests run. **Never** `Thread.Sleep`/`Task.Delay` as a substitute — it's the #1 source of flaky CI. The `PostgreSqlBuilder` ships a sensible default readiness probe; rely on it.

## Shared container via a collection fixture

Spin up **one** Postgres container per test collection and reuse it — starting a container per test is slow and unnecessary. Use `IAsyncLifetime` for setup/teardown.

```csharp
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } =
        new PostgreSqlBuilder()
            .WithImage("postgres:17")          // pin to match prod
            .WithDatabase("critterbids_test")
            .Build();

    public string ConnectionString => Container.GetConnectionString();

    public Task InitializeAsync() => Container.StartAsync();   // waits for readiness
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

## Wiring the container into Marten

Build a store against the container's connection string. Configure it the way the app does (same `ConfigureMarten` registrations per BC) so the test exercises the real schema.

```csharp
[Collection(nameof(PostgresCollection))]
public class AuctionClosingTests(PostgresFixture pg)
{
    private DocumentStore NewStore() =>
        DocumentStore.For(opts =>
        {
            opts.Connection(pg.ConnectionString);
            // mirror the BC's real registration: events, projections, etc.
            opts.Projections.Add<AuctionStatusProjection>(ProjectionLifecycle.Inline);
        });

    [Fact]
    public async Task closing_a_timed_auction_emits_listing_sold()
    {
        await using var store = NewStore();
        await using var session = store.LightweightSession();

        // Arrange — append the events that set up the scenario
        var auctionId = /* isolated id, see below */;
        session.Events.StartStream<Auction>(auctionId, new AuctionStarted(...), new BidPlaced(...));
        await session.SaveChangesAsync();

        // Act — run the decider / handler under test
        // ...

        // Assert — rebuild the aggregate or read the projection
        var auction = await session.Events.AggregateStreamAsync<Auction>(auctionId);
        Assert.Equal(AuctionStatus.Sold, auction!.Status);
    }
}
```

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
