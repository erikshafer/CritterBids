# SKILL: Querying with Marten

Use this skill whenever writing read-side queries against a Marten `IQuerySession`, including compiled queries, batched queries, raw JSON retrieval, raw/advanced SQL, and JSON streaming to HTTP responses.

---

## Decision Tree: Which Query Approach?

```
Need to serve raw JSON to HTTP response?
  └─ YES → Wolverine HTTP endpoint (new code)?
             └─ YES → Return StreamOne<T> / StreamMany<T> / StreamAggregate<T> from Marten.AspNetCore
           MVC controller, middleware, or raw HttpContext write?
             └─ YES → Use WriteArray() / WriteSingle() / WriteById() / WriteLatest() from Marten.AspNetCore
           └─ High-traffic endpoint either way? Wrap query in ICompiledListQuery / ICompiledQuery
  └─ NO
       │
       ├─ Multiple independent queries in one request?
       │    └─ YES → IBatchedQuery (batched queries)
       │
       ├─ Need exact SQL control or multi-table JOINs?
       │    └─ YES → session.AdvancedSql.QueryAsync<T>()
       │         └─ Simple WHERE clause only → session.QueryAsync<T>("where ...")
       │
       ├─ Need raw JSON string (not deserialized)?
       │    └─ YES → session.Json.FindByIdAsync<T> / ToJsonArray() / ToJsonFirst()
       │
       └─ Standard LINQ is fine → session.Query<T>()
            └─ Stable, frequently called query? → ICompiledQuery / ICompiledListQuery
```

---

## Compiled Queries

Compiled queries pre-parse the LINQ expression once and reuse the SQL + execution plan on every subsequent call — eliminating the runtime cost of Expression tree traversal and string concatenation on hot paths.

### ⚠️ Critical Gotchas (Read Before Writing One)

**DO NOT use async LINQ operators in the expression body.** The expression in `QueryIs()` is parsed statically — async operators (`FirstOrDefaultAsync`, `ToListAsync`, etc.) will break query planning. Use the synchronous equivalents (`FirstOrDefault`, leave list return as `IEnumerable<T>`). This does **not** prevent you from executing compiled queries asynchronously.

```csharp
// WRONG — will break
public Expression<...> QueryIs() =>
    q => q.Where(x => x.IsActive).ToListAsync(); // ❌

// CORRECT — synchronous operators in expression, async at call site
public Expression<...> QueryIs() =>
    q => q.Where(x => x.IsActive); // ✅

var results = await session.QueryAsync(new MyCompiledQuery(), ct); // async here ✅
```

**DO NOT use C# primary constructors.** Marten cannot currently detect or validate this, so the failure will be silent or cryptic at runtime.

```csharp
// WRONG
public class ActiveBidsQuery(Guid auctionId) : ICompiledListQuery<Bid> { ... } // ❌

// CORRECT
public class ActiveBidsQuery : ICompiledListQuery<Bid>
{
    public Guid AuctionId { get; set; }
    ...
}
```

**DO NOT use `ToArray()` or `ToList()` in the expression body.** Use `ICompiledListQuery<TDoc>` (returns `IEnumerable<T>`) instead. Calling `ToList()` in the expression throws from the Relinq library.

**Boolean fields cannot be used as query parameters.** Marten's query planner cannot match bool properties to command arguments. Work around with an enum or integer.

### Interface Reference

| Use case | Interface |
|---|---|
| Return single document (no transform) | `ICompiledQuery<TDoc>` |
| Return single document with result type | `ICompiledQuery<TDoc, TOut>` |
| Return list (no transform) | `ICompiledListQuery<TDoc>` |
| Return list with `Select()` transform | `ICompiledListQuery<TDoc, TOut>` |

### Basic Examples

```csharp
// Single document
public class AuctionById : ICompiledQuery<Auction>
{
    public Guid Id { get; set; }

    public Expression<Func<IMartenQueryable<Auction>, Auction>> QueryIs() =>
        q => q.FirstOrDefault(x => x.Id == Id);
}

// List query
public class ActiveAuctionsByParticipant : ICompiledListQuery<Auction>
{
    public Guid ParticipantId { get; set; }

    public Expression<Func<IMartenQueryable<Auction>, IEnumerable<Auction>>> QueryIs() =>
        q => q.Where(x => x.SellerId == ParticipantId && x.Status == AuctionStatus.Active)
              .OrderByDescending(x => x.EndsAt);
}

// With Select() transform
public class AuctionTitlesByStatus : ICompiledListQuery<Auction, string>
{
    public AuctionStatus Status { get; set; }

    public Expression<Func<IMartenQueryable<Auction>, IEnumerable<string>>> QueryIs() =>
        q => q.Where(x => x.Status == Status).Select(x => x.Title);
}
```

### Pagination with QueryStatistics

When paging, computed properties (like `SkipCount`) break Marten's query planner unless you implement `IQueryPlanning`. This lets Marten set unique values for planning purposes.

```csharp
public class PagedAuctions : ICompiledListQuery<Auction>, IQueryPlanning
{
    public int PageSize { get; set; } = 20;

    [MartenIgnore] // Not a DB parameter — computed from Page
    public int Page { private get; set; } = 1;

    public int SkipCount => (Page - 1) * PageSize;

    public AuctionStatus Status { get; set; }

    // Expose for paging metadata
    public QueryStatistics Statistics { get; } = new();

    public Expression<Func<IMartenQueryable<Auction>, IEnumerable<Auction>>> QueryIs() =>
        q => q.Where(x => x.Status == Status)
              .OrderByDescending(x => x.EndsAt)
              .Stats(out _, Statistics) // wired up automatically via the property
              .Skip(SkipCount)
              .Take(PageSize);

    public void SetUniqueValuesForQueryPlanning()
    {
        // Values must produce unique SkipCount and PageSize for Marten to map params correctly
        Page = 3;
        PageSize = 20;
        Status = AuctionStatus.Active;
    }
}

// Usage
var query = new PagedAuctions { Page = 2, PageSize = 10, Status = AuctionStatus.Active };
var auctions = await session.QueryAsync(query, ct);
var totalCount = query.Statistics.TotalResults;
```

### Include (JOIN) in Compiled Queries

The included result must be a property on the compiled query class — Marten writes the joined documents into it.

```csharp
public class BidWithBidder : ICompiledQuery<Bid>
{
    public Guid BidId { get; set; }
    public IList<Participant> Participants { get; } = new List<Participant>();

    public Expression<Func<IMartenQueryable<Bid>, Bid>> QueryIs() =>
        q => q.Include(x => x.ParticipantId, Participants)
              .Single(x => x.Id == BidId);
}
```

### JSON from a Compiled Query

Use `AsJson()` before a terminal operator for a single document as raw JSON, or `ToJsonArray()` on a list query:

```csharp
// Single document as JSON
public class AuctionJsonById : ICompiledQuery<Auction>
{
    public Guid Id { get; set; }

    Expression<Func<IMartenQueryable<Auction>, Auction>> ICompiledQuery<Auction, Auction>.QueryIs() =>
        q => q.Where(x => x.Id == Id).AsJson().Single();
}

// List as JSON array (use ICompiledListQuery — expression body returns IEnumerable)
public class ActiveAuctionsJson : ICompiledListQuery<Auction>
{
    public Expression<Func<IMartenQueryable<Auction>, IEnumerable<Auction>>> QueryIs() =>
        q => q.Where(x => x.Status == AuctionStatus.Active)
              .OrderBy(x => x.EndsAt)
              .ToJsonArray(); // ← produces a JSON array string result
}
```

### Executing Compiled Queries

```csharp
// Direct execution
var auction = await session.QueryAsync(new AuctionById { Id = auctionId }, ct);
var auctions = await session.QueryAsync(new ActiveAuctionsByParticipant { ParticipantId = userId }, ct);
```

---

## Batched Queries

Combine multiple independent queries into a single round trip to PostgreSQL. Each query registration returns a `Task<T>` that is resolved when `Execute()` is called. Useful when an endpoint or handler needs several unrelated read models at once.

```csharp
var batch = session.CreateBatchQuery();

// Register queries — order doesn't matter
var auctionTask = batch.Load<Auction>(auctionId);
var bidsTask = batch.Query<Bid>()
    .Where(x => x.AuctionId == auctionId)
    .OrderByDescending(x => x.Amount)
    .ToList();
var participantTask = batch.Load<Participant>(currentUserId);
var countTask = batch.Query<Bid>().Count(x => x.AuctionId == auctionId);

// One DB round trip
await batch.Execute();

// Await each Task — data is already in memory
var auction = await auctionTask;
var bids = await bidsTask;
var participant = await participantTask;
var bidCount = await countTask;
```

### Compiled Queries in a Batch

```csharp
var batch = session.CreateBatchQuery();

var flashTask = batch.Query(new ActiveAuctionsByType { Type = AuctionType.Flash });
var timedTask = batch.Query(new ActiveAuctionsByType { Type = AuctionType.Timed });

await batch.Execute();

var flash = await flashTask;
var timed = await timedTask;
```

### Query Plans in a Batch

`QueryListPlan<T>` base class supports both direct `IQuerySession` and `IBatchedQuery` execution. Prefer this pattern when a query needs to be reusable across both contexts without duplicating logic.

```csharp
public class ActiveAuctionsPlan : QueryListPlan<Auction>
{
    public AuctionStatus Status { get; }
    public ActiveAuctionsPlan(AuctionStatus status) => Status = status;

    public override IQueryable<Auction> Query(IQuerySession session) =>
        session.Query<Auction>().Where(x => x.Status == Status).OrderBy(x => x.EndsAt);
}

// Direct usage
var auctions = await session.QueryByPlanAsync(new ActiveAuctionsPlan(AuctionStatus.Active), ct);

// Batch usage
var batch = session.CreateBatchQuery();
var activeTask = batch.QueryByPlan(new ActiveAuctionsPlan(AuctionStatus.Active));
var closedTask = batch.QueryByPlan(new ActiveAuctionsPlan(AuctionStatus.Closed));
await batch.Execute();
```

---

## Raw JSON Queries

Marten stores documents as JSONB. These APIs return the raw JSON strings without deserializing into C# objects — useful when passing data straight through (e.g., to a response body or a Redis cache).

```csharp
// By ID
var json = await session.Json.FindByIdAsync<Auction>(auctionId);

// LINQ-based single results
var json = await session.Query<Auction>()
    .Where(x => x.Id == auctionId)
    .ToJsonFirst();           // throws if not found
var json = await session.Query<Auction>()
    .Where(x => x.Id == auctionId)
    .ToJsonFirstOrDefault();  // null if not found
var json = await session.Query<Auction>()
    .Where(x => x.Id == auctionId)
    .ToJsonSingle();          // throws if not exactly one
var json = await session.Query<Auction>()
    .Where(x => x.Id == auctionId)
    .ToJsonSingleOrDefault(); // null if not found

// Array of results
var jsonArray = await session.Query<Auction>()
    .Where(x => x.Status == AuctionStatus.Active)
    .OrderBy(x => x.EndsAt)
    .ToJsonArray();
```

### AsJson() with Select() Transforms

You can project to a different shape before retrieving as JSON — Marten performs the transform at the SQL level:

```csharp
var json = await session.Query<Auction>()
    .OrderBy(x => x.EndsAt)
    .Select(x => new { x.Id, x.Title, x.CurrentBid })
    .ToJsonFirstOrDefault();
// → {"Id":"...","Title":"...","CurrentBid":...}
```

---

## Raw SQL Queries

Drop down to raw SQL when LINQ is insufficient. Marten's `QueryAsync<T>` assumes a document query unless the SQL starts with `SELECT`.

```csharp
// WHERE clause only — Marten adds "select data from mt_doc_auction where ..."
var auctions = await session.QueryAsync<Auction>(
    "where data ->> 'Status' = ?", "Active");

// Full SELECT for scalar aggregates
var count = (await session.QueryAsync<int>(
    "select count(*) from mt_doc_auction")).First();

// Custom placeholder character
var auctions = await session.QueryAsync<Auction>('$',
    "where data ->> 'SellerId' = $", sellerId.ToString());

// Casting JSONB properties to PostgreSQL types for scalar returns
var closedAt = (await session.QueryAsync<DateTimeOffset>(
    "SELECT (data ->> 'ClosedAt')::timestamptz FROM mt_doc_auction WHERE id = ?",
    auctionId)).FirstOrDefault();
```

**Rules:**
- `?` is the default parameter placeholder; you can override with any single character.
- If `T` is an npgsql-mapped type (`int`, `Guid`, `string`, etc.), Marten reads the first column.
- If `T` is not a mapped type, Marten deserializes the first column as JSON.
- SQL starting with `SELECT` is used verbatim.
- SQL not starting with `SELECT` is treated as a WHERE clause appended to the document table query.
- `WHERE` keyword is optional — Marten adds it if missing.

---

## Advanced SQL Queries

`session.AdvancedSql.QueryAsync<T>()` gives you full SQL control with Marten's result mapping. You provide the entire query — no magic additions. Supports returning up to three types as a tuple using `ROW()` wrapping.

### Schema Resolution

Always use the schema resolver for table names to avoid hardcoding:

```csharp
var schema = session.DocumentStore.Options.Schema;

// Resolves to e.g. "public.mt_doc_auction"
var tableName = schema.For<Auction>();

// Without schema prefix
var bare = schema.For<Auction>(qualified: false); // "mt_doc_auction"
```

### Column Order Requirements

For document types, the `SELECT` must return columns in this exact order:
1. `id` (required, except QuerySession)
2. `data` (required)
3. `mt_doc_type` (only for document hierarchies)
4. `mt_version` (only if versioning is enabled)
5. `mt_last_modified`, `mt_created_at`, `correlation_id`, `causation_id`, `last_modified_by` (only if those metadata fields are mapped)
6. `mt_deleted`, `mt_deleted_at` (only if soft-delete metadata is mapped)

**Tip:** Inspect the correct column order with:
```csharp
var cmd = session.Query<Auction>().ToCommand();
Console.WriteLine(cmd.CommandText);
```

### Examples

```csharp
var schema = session.DocumentStore.Options.Schema;

// Single scalar
var count = (await session.AdvancedSql.QueryAsync<long>(
    $"select count(*) from {schema.For<Auction>()} where data ->> 'Status' = 'Active'",
    CancellationToken.None)).First();

// Multiple scalars as tuple
var (total, active) = (await session.AdvancedSql.QueryAsync<long, long>(
    "select row(count(*) filter (where true)), row(count(*) filter (where data->>'Status'='Active')) from mt_doc_auction",
    CancellationToken.None)).First();

// Document query (no metadata)
var auctions = await session.AdvancedSql.QueryAsync<Auction>(
    $"select id, data from {schema.For<Auction>()} order by data ->> 'EndsAt'",
    CancellationToken.None);

// Document with metadata (mt_version must follow data)
var auction = (await session.AdvancedSql.QueryAsync<Auction>(
    $"select id, data, mt_version from {schema.For<Auction>()} where id = $1",
    CancellationToken.None,
    auctionId)).FirstOrDefault();

// Multi-document JOIN with paging scalar — use ROW() for each type
var results = await session.AdvancedSql.QueryAsync<Auction, Bid, long>(
    $"""
    select
      row(a.id, a.data, a.mt_version),
      row(b.id, b.data, b.mt_version),
      row(count(*) over())
    from
      {schema.For<Auction>()} a
    join
      {schema.For<Bid>()} b on (b.data ->> 'AuctionId')::uuid = a.id
    where
      a.id = $1
    order by
      (b.data ->> 'Amount')::numeric desc
    limit $2 offset $3
    """,
    CancellationToken.None,
    auctionId, pageSize, skip);

// Large dataset streaming (avoids loading all results into memory)
var stream = session.AdvancedSql.StreamAsync<Bid>(
    $"select id, data from {schema.For<Bid>()} where data ->> 'AuctionId' = $1",
    CancellationToken.None,
    auctionId.ToString());

await foreach (var bid in stream)
{
    // process each bid without buffering the full result set
}
```

---

## JSON Streaming to HTTP (Marten.AspNetCore)

The `Marten.AspNetCore` package streams raw JSONB directly from PostgreSQL to the HTTP response — no C# deserialization, no serializer, no GC pressure. Install via:

```
dotnet add package Marten.AspNetCore
```

### WriteById — Single Document by ID

```csharp
// MVC Controller
[HttpGet("/auctions/{id:guid}")]
public Task Get(Guid id, [FromServices] IQuerySession session) =>
    session.Json.WriteById<Auction>(id, HttpContext);

// Returns 200 with JSON body, or 404 if not found
```

### WriteSingle / WriteArray — LINQ-Based

```csharp
// Single document via LINQ
[HttpGet("/auctions/{id:guid}/detail")]
public Task GetDetail(Guid id, [FromServices] IQuerySession session) =>
    session.Query<Auction>()
        .Where(x => x.Id == id)
        .WriteSingle(HttpContext);

// Array of documents via LINQ
[HttpGet("/auctions/active")]
public Task GetActive([FromServices] IQuerySession session) =>
    session.Query<Auction>()
        .Where(x => x.Status == AuctionStatus.Active)
        .OrderBy(x => x.EndsAt)
        .WriteArray(HttpContext);
```

### Maximum Throughput: Compiled Query + WriteArray

This is the highest-performance combination. The SQL plan is compiled once; JSON never leaves the database as a C# string.

```csharp
public class ActiveAuctionsQuery : ICompiledListQuery<Auction>
{
    public Expression<Func<IMartenQueryable<Auction>, IEnumerable<Auction>>> QueryIs() =>
        q => q.Where(x => x.Status == AuctionStatus.Active)
              .OrderBy(x => x.EndsAt);
}

// Wolverine endpoint
public static class GetActiveAuctionsEndpoint
{
    [WolverineGet("/api/auctions/active")]
    [ProducesResponseType<Auction[]>(200, "application/json")]
    public static Task Get(IQuerySession session, HttpContext context) =>
        session.WriteArray(new ActiveAuctionsQuery(), context);
}
```

### WriteLatest — Event-Sourced Aggregates (IDocumentSession required)

For inline or caught-up async projections, streams the stored JSONB directly. Falls back to live rebuild only when the async daemon is behind.

```csharp
// Requires IDocumentSession (not IQuerySession)
[HttpGet("/auctions/{id:guid}/state")]
public Task GetState(Guid id, [FromServices] IDocumentSession session) =>
    session.Events.WriteLatest<AuctionState>(id, HttpContext);
```

**Important:** `WriteLatest<T>()` requires `IDocumentSession`. Inject it only in write-side handlers or endpoints that already hold a document session; avoid creating a write session purely for reads.

### Return-Type API — `StreamOne<T>` / `StreamMany<T>` / `StreamAggregate<T>` (Wolverine 5.32+)

Starting with Wolverine 5.32 (Marten.AspNetCore sibling release), three return types let Wolverine HTTP endpoints stream raw JSONB without a method-call shape. Each implements `IResult` (Wolverine's existing `ResultWriterPolicy` dispatches them with no pipeline change) and `IEndpointMetadataProvider` (OpenAPI `Produces<T>` / `Produces(404)` metadata is generated automatically — no `[ProducesResponseType]` attributes required). Reference: <https://wolverine.netlify.app/guide/http/marten.html#streaming-json-responses>.

```csharp
using Marten.AspNetCore;

// Single document by LINQ predicate — 404 on miss
[WolverineGet("/auctions/{id}")]
public static StreamOne<Auction> Get(Guid id, IQuerySession session)
    => new(session.Query<Auction>().Where(x => x.Id == id));

// Array from an IQueryable — returns empty array, never 404
[WolverineGet("/auctions/active")]
public static StreamMany<Auction> Active(IQuerySession session)
    => new(session.Query<Auction>().Where(x => x.Status == AuctionStatus.Active));

// Event-sourced aggregate current state — 404 on miss
[WolverineGet("/sessions/{id}/state")]
public static StreamAggregate<SessionState> GetState(Guid id, IDocumentSession session)
    => new(session, id);
```

**404 semantics split by type.** `StreamOne<T>` and `StreamAggregate<T>` return 404 when the document/aggregate does not exist. `StreamMany<T>` returns an empty JSON array and never 404s — matches REST-array idiom, prevents "empty result" ambiguity.

**Init-only customization properties.** Both types expose `OnFoundStatus` (override the 200 default) and `ContentType` (override `application/json`):

```csharp
return new StreamOne<Auction>(query) { OnFoundStatus = 200, ContentType = "application/json" };
```

**When to prefer the return-type API over `WriteArray`/`WriteById`/`WriteLatest`:**

| Context | Preferred |
|---|---|
| New Wolverine HTTP endpoint | **Return-type API** — typed return, auto OpenAPI, no `HttpContext` parameter |
| Existing MVC controller | Extension methods — MVC actions don't consume `IResult` the same way |
| Middleware or raw `HttpContext` write | Extension methods — no endpoint result pipeline involved |
| Compiled query composition | Either — `new StreamMany<T>(compiledQuery.Queryable(session))` composes cleanly |

Both API shapes live in the same `Marten.AspNetCore` package and can coexist in the same project.

### Gotchas

- `WriteArray()` / `WriteSingle()` / `WriteById()` serve exactly what is persisted — no anti-corruption/mapping layer. If the persisted shape must differ from what clients receive, introduce a mapping projection or a `Select()` transform before calling these methods.
- Make sure Marten's JSON serialization configuration (camelCase, enum-as-string, etc.) matches what your HTTP clients expect. Configure once in `Program.cs` and it applies everywhere.
- `[ProducesResponseType]` is required when using `WriteArray()`/`WriteSingle()` with Wolverine for OpenAPI metadata generation, since the method returns `Task` rather than a typed response.

---

## Quick Reference

| Scenario | API |
|---|---|
| Standard LINQ query | `session.Query<T>().Where(...).ToListAsync(ct)` |
| Reusable, high-frequency LINQ query | `ICompiledListQuery<T>` / `ICompiledQuery<T>` |
| Multiple queries, one DB round trip | `session.CreateBatchQuery()` |
| Reusable query usable in both batch and direct | `QueryListPlan<T>` |
| Raw JSON string by ID | `session.Json.FindByIdAsync<T>(id)` |
| Raw JSON array from LINQ | `.ToJsonArray()` |
| Raw JSON single from LINQ | `.ToJsonFirstOrDefault()` |
| Simple WHERE clause SQL | `session.QueryAsync<T>("where ...")` |
| Full SQL with multi-type results | `session.AdvancedSql.QueryAsync<T1, T2, T3>(...)` |
| Stream large SQL result sets | `session.AdvancedSql.StreamAsync<T>(...)` |
| Stream JSON array to HTTP (extension API) | `query.WriteArray(HttpContext)` |
| Stream single doc JSON to HTTP (extension API) | `session.Json.WriteById<T>(id, HttpContext)` |
| Stream compiled query JSON to HTTP (extension API) | `session.WriteArray(new MyCompiledQuery(), HttpContext)` |
| Stream event-sourced aggregate to HTTP (extension API) | `session.Events.WriteLatest<T>(id, HttpContext)` |
| Stream single doc from Wolverine endpoint (return-type API, 5.32+) | `return new StreamOne<T>(session.Query<T>().Where(...))` |
| Stream array from Wolverine endpoint (return-type API, 5.32+) | `return new StreamMany<T>(session.Query<T>().Where(...))` |
| Stream aggregate state from Wolverine endpoint (return-type API, 5.32+) | `return new StreamAggregate<T>(session, id)` |
