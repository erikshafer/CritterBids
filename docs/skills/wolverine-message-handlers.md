# Wolverine Message Handlers

Patterns and conventions for building message handlers and HTTP endpoints with Wolverine in the Critter Stack.

> **Marten vs Polecat BCs:** Most patterns in this file apply equally to both. The key namespace difference:
> `using Wolverine.Marten` for PostgreSQL BCs, `using Wolverine.Polecat` for SQL Server BCs.
> Wherever `MartenOps` appears below, substitute `PolecatOps` for Polecat BCs. See `docs/skills/polecat-event-sourcing.md`.

---

## Table of Contents

1. [Core Principle: The Decider Pattern](#core-principle-the-decider-pattern)
2. [Compound Handler Lifecycle](#compound-handler-lifecycle)
3. [Standard Handler Pattern](#standard-handler-pattern)
4. [Aggregate Handler Workflow](#aggregate-handler-workflow)
5. [Entity and Document Loading](#entity-and-document-loading)
6. [Handler Return Patterns](#handler-return-patterns)
7. [Railway Programming](#railway-programming)
8. [HTTP Endpoints](#http-endpoints)
9. [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
10. [Debugging with the Wolverine Diagnostics CLI](#debugging-with-the-wolverine-diagnostics-cli)
11. [File Organization and Naming](#file-organization-and-naming)

---

## Core Principle: The Decider Pattern

Wolverine's aggregate handler workflow implements the **Decider pattern** — a functional approach to event sourcing credited to Jérémie Chassaing.

**Three concerns, cleanly separated:**

1. **Load** — Fetch current aggregate state (Wolverine handles this)
2. **Decide** — Pure function: `(Command, State) → Events` (your `Handle()` method)
3. **Evolve** — Apply events to update state (Marten/Polecat handles this via `Apply()` methods)

Aggregates are immutable write models — "always valid" in structure. Validation happens in handlers, not aggregates. `Handle()` methods are pure functions focused solely on business logic.

**This is A-Frame Architecture:** infrastructure at the edges (loading, validation, persistence), pure logic in the middle.

```csharp
public class Listing
{
    public Guid Id { get; set; }
    public bool IsOpen { get; private set; }
    public decimal CurrentHighBid { get; private set; }

    public void Apply(BiddingOpened e) => IsOpen = true;
    public void Apply(BidPlaced e) => CurrentHighBid = e.Amount;
    public void Apply(BiddingClosed e) => IsOpen = false;
}

public static class PlaceBidHandler
{
    public static ProblemDetails Before(PlaceBid cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = "Listing not found", Status = 404 };
        if (!listing.IsOpen)
            return new ProblemDetails { Detail = "Listing is not open for bidding", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        PlaceBid cmd,
        [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new BidPlaced(listing.Id, cmd.BidId, cmd.BidderId, cmd.Amount));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.BidPlaced(listing.Id, cmd.BidderId, cmd.Amount));

        return (events, outgoing);
    }
}
```

**Why this matters:** `Handle()` is trivially testable — pure function, no mocks. Business logic is isolated and auditable.

---

## Compound Handler Lifecycle

Wolverine executes handler methods in this order:

| Lifecycle | Method Names | Purpose |
|---|---|---|
| Before Handler | `Before`, `BeforeAsync`, `Load`, `LoadAsync`, `Validate`, `ValidateAsync` | Load data, validate preconditions |
| Handler | `Handle`, `HandleAsync` | Business logic (pure function) |
| After Handler | `After`, `AfterAsync`, `PostProcess`, `PostProcessAsync` | Side effects, notifications |
| Finally | `Finally`, `FinallyAsync` | Cleanup (runs even on failure) |

**Key points:**
- Wolverine discovers these by convention — no interfaces required
- Values returned from early methods become parameters for later ones ("tuple threading")
- Tuple order matters — Wolverine wires by type and position
- Early methods can short-circuit by returning `HandlerContinuation.Stop`, `ProblemDetails`, or `IResult`

**⚠️ CRITICAL: Tuple Order Matters**

Wolverine wires dependencies **by position in the tuple**, not by parameter name. If `Load()` returns `(Listing, Bidder)` but `Handle()` expects `(Bidder bidder, Listing listing)`, Wolverine passes `Listing` as the first parameter — causing runtime errors. Always match tuple order to parameter order across methods.

---

## Standard Handler Pattern

```csharp
// Command — sealed record, immutable
public sealed record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount);

// Validator — optional, FluentValidation
public sealed class PlaceBidValidator : AbstractValidator<PlaceBid>
{
    public PlaceBidValidator()
    {
        RuleFor(x => x.ListingId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

// Handler — static class, suffix with Handler
public static class PlaceBidHandler
{
    public static ProblemDetails Before(PlaceBid cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = "Not found", Status = 404 };
        if (!listing.IsOpen)
            return new ProblemDetails { Detail = "Listing not open", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        PlaceBid cmd,
        [WriteAggregate] Listing listing)
    {
        // business logic only
    }
}
```

Commands, validators, and handlers are colocated in one file. Use instance handlers only when constructor-injected dependencies are too heavy for method injection.

---

## Aggregate Handler Workflow

### Three Attributes

| Attribute | Use Case | Persistence |
|---|---|---|
| `[ReadAggregate]` | Query aggregate state (no modifications) | None — read-only |
| `[WriteAggregate]` | Modify aggregate (append events) | Automatic via return value |
| `[AggregateHandler]` | Class-level attribute for single-stream handlers | Automatic via return value |

Prefer `[WriteAggregate]` — it's parameter-level, supports multi-stream operations, and makes intent explicit.

### `[WriteAggregate]`

```csharp
public static class CloseBiddingHandler
{
    public static ProblemDetails Before(CloseBidding cmd, Listing? listing)
    {
        if (listing is null)
            return new ProblemDetails { Detail = "Not found", Status = 404 };
        if (!listing.IsOpen)
            return new ProblemDetails { Detail = "Already closed", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static (Events, OutgoingMessages) Handle(
        CloseBidding cmd,
        [WriteAggregate] Listing listing)
    {
        var events = new Events();
        events.Add(new BiddingClosed(listing.Id, listing.CurrentHighBid, listing.HighBidderId));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.ListingSold(listing.Id, listing.HighBidderId, listing.CurrentHighBid));

        return (events, outgoing);
    }
}
```

**How Wolverine resolves aggregate ID:**
1. Command property named `{AggregateName}Id` (e.g., `ListingId` for `Listing`)
2. Command property with `[Identity]` attribute
3. HTTP route parameter (e.g., `/listings/{listingId}`)

### Explicit stream-ID property override

When the command carries the stream ID on a property that doesn't follow the `{AggregateName}Id`
convention, pass the property name as a positional constructor argument on `[WriteAggregate]`:

```csharp
public sealed record SubmitListing(Guid ListingId, Guid SellerId);

public static (Events, OutgoingMessages) Handle(
    SubmitListing cmd,
    [WriteAggregate(nameof(SubmitListing.ListingId))] SellerListing listing)
{
    // ...
}
```

Without the override, Wolverine would look for `SellerListingId` on the command (the aggregate is
`SellerListing`) and fail at handler code-gen time with
`InvalidOperationException: Unable to determine an aggregate id for the parameter 'listing'` —
the exception surfaces during `WolverineRuntime.findInvoker()` the first time the message type is
dispatched, not at host startup. The 30 direct-call Selling unit tests in `SubmitListingTests`
never exercised this because they bypass dispatch entirely. Verified via dispatch through
`IMessageBus.InvokeAsync()` in `SubmitListingDispatchTests`.

The attribute is `[WriteAggregate(string routeOrParameterName)]` — a positional string
constructor argument, not a named `AggregateIdMember` property. `nameof()` keeps the reference
refactor-safe.

### `[ReadAggregate]`

Use when you need aggregate state but won't modify it:

```csharp
[WolverineGet("/api/listings/{listingId}")]
public static Listing? GetListing(Guid listingId, [ReadAggregate] Listing? listing) => listing;
```

No events persisted, no optimistic concurrency checks. Ideal for GET endpoints.

### Optimistic Concurrency

Include `Version` on commands to enforce optimistic concurrency:

```csharp
public sealed record PlaceBid(Guid ListingId, Guid BidId, Guid BidderId, decimal Amount, int Version);
```

Wolverine calls `FetchForWriting<Listing>(cmd.ListingId, cmd.Version)`. Mismatch → `ConcurrencyException`. Always include `Version` where concurrent writes matter.

### When NOT to Use `[WriteAggregate]`

Use the `Load()` pattern when the aggregate ID must be computed (e.g., UUID v5 from a string key):

```csharp
public static class OpenBiddingHandler
{
    public static async Task<Listing?> Load(OpenBidding cmd, IDocumentSession session)
    {
        var streamId = Listing.StreamId(cmd.ListingId); // UUID v5
        return await session.Events.AggregateStreamAsync<Listing>(streamId);
    }

    public static ProblemDetails Before(OpenBidding cmd, Listing? listing) { /* ... */ }

    // NO [WriteAggregate] — loaded manually; use session.Events.Append()
    public static OutgoingMessages Handle(OpenBidding cmd, Listing listing, IDocumentSession session)
    {
        var evt = new BiddingOpened(listing.Id, cmd.SellerId, cmd.StartingBid);
        session.Events.Append(listing.Id, evt);

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.BiddingOpened(listing.Id));
        return outgoing; // Return ONLY OutgoingMessages — not Events
    }
}
```

**⚠️ CRITICAL:** When using `Load()` + manual `session.Events.Append()`, return only `OutgoingMessages`. Returning an `Events` collection too will persist events twice.

---

## Entity and Document Loading

Load documents (non-event-sourced) using `[Entity]` in Marten BC handlers:

```csharp
// Basic [Entity] — loads SomeDocument by ID from route/query, returns 404 if missing
public static ProblemDetails Before(UpdateSomething cmd, [Entity] SomeDocument? doc)
{
    if (doc is null)
        return new ProblemDetails { Detail = "Not found", Status = 404 };
    return WolverineContinue.NoProblems;
}
```

ID resolution: property named `{EntityName}Id`, `[Identity]` attribute, or HTTP route parameter.

### `[Entity]` Batch-Query Pattern — Multiple Entities in One Round-Trip

*Confirmed by CritterStackSamples north star analysis (§4.3 — Entity Loading with `[Entity]`)*

When multiple related entities must be loaded to fulfil a request, multiple `[Entity]` parameters
on the same handler method signature trigger a single Marten batch query rather than sequential
`LoadAsync` calls. This is one of the most significant performance patterns Wolverine provides over
manual document loading.

Two variants control what happens when a referenced entity is not found:

- Without `OnMissing`: a missing entity resolves as `null`, and a `Required = true` combination
  returns 404. This is appropriate when a missing entity means the resource doesn't exist.
- With `OnMissing = OnMissing.ProblemDetailsWith400`: a missing entity short-circuits with a 400
  response. This is appropriate when the ID comes from the client payload and a missing entity
  means the client sent an invalid reference — a bad input, not a missing resource.

When both entities are loaded via `[Entity]`, the `Validate` (sync) method receives them as
already-loaded parameters. Business rule checks against their state are synchronous — no async
database call needed in the validation step.

The `Validate`/`ValidateAsync` pair and `[Entity]` combine to produce the cleanest form of the
compound handler lifecycle: load entities declaratively, validate business rules synchronously
against the loaded state, execute the happy path in `Handle()` with no conditional returns.

### `ValidateAsync` and `Validate` — Railway Programming Pre-Handler Pattern

*Confirmed by CritterStackSamples north star analysis (§5 — Validation Patterns)*

Wolverine calls `ValidateAsync` (or `Validate`) before the main handler method. Returning a
populated `ProblemDetails` short-circuits with that status; returning `WolverineContinue.NoProblems`
proceeds to `Handle()`.

**`ValidateAsync` — for database-dependent checks:**

Use when the validation requires an async database query that cannot be covered by `[Entity]`
declarative loading — typically uniqueness checks or existence checks on types that don't have
a corresponding `[Entity]` parameter. The method receives `IQuerySession` as an injected parameter.
Returning a populated `ProblemDetails` stops the pipeline; returning `WolverineContinue.NoProblems`
continues.

**`Validate` (synchronous) — for business rules against already-loaded state:**

When business rules can be evaluated against aggregate state (loaded by `[AggregateHandler]` or
`[WriteAggregate]`) or entity state (loaded by `[Entity]`), use the synchronous `Validate` form.
The aggregate or entity is already in scope as a parameter — no async call is needed. This keeps
validation fast and the happy-path handler completely unconditional.

`WolverineContinue.NoProblems` is the continue sentinel in both variants. The main `Handle()` method
is always the happy path — no conditional returns, no error-state branches, no null checks.
If `Validate` or `ValidateAsync` allowed execution to reach `Handle()`, the command is valid.

Wolverine calls the validation layers in this order: FluentValidation structural rules →
`Validate`/`ValidateAsync` business rules → `Handle()` happy path. The separation means each layer
has a single responsibility and the handler itself is a pure business decision function.

---

## Handler Return Patterns

| Return Type | Wolverine Action |
|---|---|
| `Events` (single or collection) | Append to current event stream |
| `OutgoingMessages` | Publish integration messages via outbox |
| `(Events, OutgoingMessages)` | Append events + publish messages |
| `(IResult, OutgoingMessages)` | HTTP response + publish messages |
| `(IResult, Events, OutgoingMessages)` | HTTP response + append events + publish messages |
| `IStartStream` | Start a new event stream |
| `(CreationResponse, IStartStream)` | HTTP 201 + start stream |
| `void` | No events, no messages |

### Start New Stream

**⚠️ CRITICAL:** Handlers creating new event streams MUST return `IStartStream` from `MartenOps.StartStream()` (Marten BCs) or `PolecatOps.StartStream()` (Polecat BCs). Direct `session.Events.StartStream()` silently discards events. See Anti-Pattern #9.

```csharp
// Marten BC — using Wolverine.Marten
public static IStartStream Handle(PublishListing cmd)
{
    var listingId = Guid.CreateVersion7();
    return MartenOps.StartStream<Listing>(listingId, new ListingPublished(listingId, cmd.SellerId, ...));
}

// Polecat BC — using Wolverine.Polecat (identical pattern)
public static IStartStream Handle(RegisterParticipant cmd)
{
    var participantId = Guid.CreateVersion7();
    return PolecatOps.StartStream<Participant>(participantId, new ParticipantRegistered(participantId, ...));
}
```

HTTP endpoint (same tuple pattern for both):

```csharp
[WolverinePost("/api/listings")]
public static (CreationResponse<Guid>, IStartStream) Handle(PublishListing cmd)
{
    var listingId = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Listing>(listingId, new ListingPublished(...));
    return (new CreationResponse<Guid>($"/api/listings/{listingId}", listingId), stream);
}
```

**⚠️ Tuple Order:** HTTP response MUST be first: `(CreationResponse, IStartStream)` not `(IStartStream, CreationResponse)`.

### Full Triple-Tuple (HTTP + Events + Messages)

Wolverine commits all three atomically — event append and outbox enrollment in a single transaction:

```csharp
[WolverinePost("/api/listings/{listingId}/close")]
public static (IResult, Events, OutgoingMessages) Handle(
    CloseBidding cmd,
    [WriteAggregate] Listing listing)
{
    var closed = new BiddingClosed(listing.Id, listing.CurrentHighBid, listing.HighBidderId);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new Contracts.ListingSold(listing.Id, listing.HighBidderId, listing.CurrentHighBid));

    return (Results.Ok(), new Events(closed), outgoing);
}
```

### Async vs Sync

Handlers querying projections AFTER event appends must be `async Task<T>` to call `await session.SaveChangesAsync()` before the query — otherwise stale data is returned.

---

## Railway Programming

Wolverine supports short-circuiting via `Before/Validate/Load` methods, keeping `Handle()` on the happy path only.

### `ProblemDetails` — HTTP 400 with structured error

```csharp
public static ProblemDetails Before(PlaceBid cmd, Listing? listing)
{
    if (listing is null)
        return new ProblemDetails { Detail = "Not found", Status = 404 };
    if (!listing.IsOpen)
        return new ProblemDetails { Detail = "Listing not open", Status = 400 };
    return WolverineContinue.NoProblems; // Continue
}
```

### `HandlerContinuation.Stop` — for message handlers

```csharp
public static HandlerContinuation Before(ProcessMessage cmd, SomeAggregate? aggregate)
{
    if (aggregate is null) return HandlerContinuation.Stop; // Message discarded
    return HandlerContinuation.Continue;
}
```

### Async External Validation

When `Handle()` needs async external validation, split into two handler classes: one for internal/command use, one for the HTTP endpoint using `ValidateAsync()`.

```csharp
// Internal command handler — assumes caller validated
public static class RegisterBidderHandler
{
    public static ProblemDetails Before(RegisterBidder cmd, ParticipantSession? session) { /* sync checks */ }
    public static (Events, OutgoingMessages) Handle(RegisterBidder cmd, ...) { /* happy path */ }
}

// HTTP endpoint handler — uses ValidateAsync for async checks
public static class RegisterBidderEndpoint
{
    public static async Task<ProblemDetails> ValidateAsync(
        RegisterBidder cmd,
        IExternalVerificationService svc,
        CancellationToken ct)
    {
        var result = await svc.VerifyAsync(cmd.SessionToken, ct);
        if (!result.IsValid)
            return new ProblemDetails { Detail = result.Reason, Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/bidders/register")]
    public static (Events, OutgoingMessages) Handle(RegisterBidder cmd, ...) { /* same happy path */ }
}
```

**Rule:** `Handle()` is always the happy path — never return `ProblemDetails` from `Handle()`.

---

## HTTP Endpoints

### Mixed Route + JSON Body Parameters

**⚠️ CRITICAL:** The compound handler pattern (`Before()/Validate()/Load()/Handle()`) does NOT work when mixing route parameters with a JSON body. The `Before()` method cannot access the deserialized body. Result: 500 errors.

```csharp
// ❌ FAILS — compound handler with mixed params
[WolverinePost("/api/listings/{listingId}/bid")]
public static class PlaceBidCompoundHandler
{
    public static Before(Guid listingId, decimal amount) { ... } // Can't access amount from body
}

// ✅ CORRECT — direct implementation
public sealed record PlaceBidRequest(decimal Amount);

[WolverinePost("/api/listings/{listingId}/bid")]
public static async Task<IResult> Handle(
    Guid listingId,         // Route parameter
    PlaceBidRequest request, // JSON body
    IDocumentSession session,
    CancellationToken ct)
{
    var listing = await session.Events.AggregateStreamAsync<Listing>(listingId, token: ct);
    if (listing is null) return Results.NotFound();
    // ...
}
```

**When to use which:**

| Scenario | Pattern |
|---|---|
| All parameters from JSON body | Compound handler (`Before/Handle`) |
| Route parameters only | Compound handler |
| Mixed route + JSON body | Direct implementation |
| DELETE with no body | Direct implementation |

### Publishing Integration Messages from HTTP Endpoints

Always use `OutgoingMessages` — never `bus.PublishAsync()` in HTTP endpoints. See Anti-Pattern #11.

---

## Anti-Patterns to Avoid

### 1. ❌ Business Logic in `Before/Load` Methods

`Before()` is for precondition checks only — no state mutation, no side effects.

```csharp
// ❌ WRONG
public static ProblemDetails Before(SomeCmd cmd, Aggregate? agg)
{
    agg.Status = Status.Changed; // Mutating state!
    session.Store(agg);          // Side effect!
    return WolverineContinue.NoProblems;
}

// ✅ CORRECT — check only, mutate in Handle()
public static ProblemDetails Before(SomeCmd cmd, Aggregate? agg)
{
    if (agg is null) return new ProblemDetails { Detail = "Not found", Status = 404 };
    if (agg.Status != Status.Expected) return new ProblemDetails { Detail = "Wrong state", Status = 409 };
    return WolverineContinue.NoProblems;
}
```

### 2. ❌ Loading Aggregates Manually Inside `Handle()`

```csharp
// ❌ WRONG — bypasses Wolverine concurrency checks
public static async Task<Events> Handle(SomeCmd cmd, IDocumentSession session)
{
    var agg = await session.Events.AggregateStreamAsync<Aggregate>(cmd.AggregateId);
    session.Events.Append(cmd.AggregateId, new SomeEvent());
    return [];
}

// ✅ CORRECT — Wolverine loads automatically
public static Events Handle(SomeCmd cmd, [WriteAggregate] Aggregate agg)
    => [new SomeEvent(agg.Id)];
```

### 3. ❌ Wrong Tuple Order in Return Values

```csharp
// ❌ WRONG — IStartStream is serialized as the response body
[WolverinePost("/api/listings")]
public static (IStartStream, CreationResponse) Handle(CreateListing cmd) { ... }

// ✅ CORRECT — HTTP response type first
[WolverinePost("/api/listings")]
public static (CreationResponse, IStartStream) Handle(CreateListing cmd) { ... }
```

### 4. ❌ Using `[Aggregate]` When Optimistic Concurrency Is Needed

```csharp
// ❌ WRONG — [Aggregate] class-level doesn't opt into concurrency checks
[Aggregate]
public static class CloseBiddingHandler
{
    public static Events Handle(CloseBidding cmd, Listing listing) { ... }
}

// ✅ CORRECT — parameter-level [WriteAggregate] with concurrency
public static class CloseBiddingHandler
{
    public static Events Handle(CloseBidding cmd, [WriteAggregate] Listing listing) { ... }
}
```

### 5. ❌ Building Mediator-Style Chained Result Flows

```csharp
// ❌ WRONG — chatty, Result-wrapping noise
public static async Task<Result<SomethingHappened>> Handle(DoSomething cmd, IMessageBus bus)
{
    var r1 = await bus.InvokeAsync<Result<A>>(new GetA(cmd.Id));
    if (!r1.IsSuccess) return Result.Failure<SomethingHappened>(r1.Error);
    var r2 = await bus.InvokeAsync<Result<B>>(new CheckB(cmd.Value));
    if (!r2.IsSuccess) return Result.Failure<SomethingHappened>(r2.Error);
    // ...
}

// ✅ CORRECT — use Load() + Validate() for data loading, Handle() for logic
public static class DoSomethingHandler
{
    public static async Task<(A?, B?)> Load(DoSomething cmd, IDocumentSession session)
        => (await session.LoadAsync<A>(cmd.Id), await session.LoadAsync<B>(cmd.Value));

    public static ProblemDetails Validate(DoSomething cmd, A? a, B? b)
    {
        if (a is null) return new ProblemDetails { Detail = "A not found", Status = 404 };
        if (b is null) return new ProblemDetails { Detail = "B not found", Status = 404 };
        return WolverineContinue.NoProblems;
    }

    public static Events Handle(DoSomething cmd, A a, B b)
        => [new SomethingHappened(a.Id, b.Value)];
}
```

### 6. ❌ Injecting `IDocumentSession` for Write Operations in Aggregate Handlers

```csharp
// ❌ WRONG — manual append when [WriteAggregate] handles it
public static Events Handle(SomeCmd cmd, [WriteAggregate] Aggregate agg, IDocumentSession session)
{
    session.Events.Append(agg.Id, new SomeEvent()); // Double persistence
    return [];
}

// ✅ CORRECT — Wolverine appends events returned from Handle()
public static Events Handle(SomeCmd cmd, [WriteAggregate] Aggregate agg)
    => [new SomeEvent(agg.Id)];
```

### 7. ❌ Fat Handlers — Infrastructure Work in `Handle()`

Move loading and validation to `Load()` / `Validate()`. `Handle()` should read like business requirements, not infrastructure code.

### 8. ❌ Returning Tuples When Manually Loading Aggregates ⚠️ CRITICAL

```csharp
// ❌ WRONG — event is silently discarded
public static async Task<(Aggregate, SomeEvent)> Handle(SomeCmd cmd, IDocumentSession session)
{
    var agg = await session.Events.AggregateStreamAsync<Aggregate>(cmd.Id);
    return (agg, new SomeEvent()); // Tuple return only works with [WriteAggregate]
}

// ✅ CORRECT — explicit append when loading manually
public static async Task Handle(SomeCmd cmd, IDocumentSession session)
{
    var agg = await session.Events.AggregateStreamAsync<Aggregate>(cmd.Id);
    // validation...
    session.Events.Append(cmd.Id, new SomeEvent());
}
```

The `(Aggregate, Event)` tuple return pattern only works with `[WriteAggregate]`. Manual loading + tuple return silently discards events with no error. Caused ~30 minutes of debugging in production. Always use `session.Events.Append()` when loading manually.

### 9. ❌ Direct `session.Events.StartStream()` Without Returning `IStartStream` ⚠️ CRITICAL

```csharp
// ❌ WRONG — events silently discarded, handler appears to succeed
[WolverinePost("/api/listings")]
public static CreationResponse Handle(CreateListing cmd, IDocumentSession session)
{
    var id = Guid.CreateVersion7();
    session.Events.StartStream<Listing>(id, new ListingPublished(...)); // NOT persisted
    return new CreationResponse($"/api/listings/{id}");
}

// ✅ CORRECT — Marten BC (using Wolverine.Marten)
[WolverinePost("/api/listings")]
public static (CreationResponse, IStartStream) Handle(CreateListing cmd)
{
    var id = Guid.CreateVersion7();
    var stream = MartenOps.StartStream<Listing>(id, new ListingPublished(...));
    return (new CreationResponse($"/api/listings/{id}"), stream);
}

// ✅ CORRECT — Polecat BC (using Wolverine.Polecat — identical pattern)
[WolverinePost("/api/participants")]
public static (CreationResponse, IStartStream) Handle(RegisterParticipant cmd)
{
    var id = Guid.CreateVersion7();
    var stream = PolecatOps.StartStream<Participant>(id, new ParticipantRegistered(...));
    return (new CreationResponse($"/api/participants/{id}"), stream);
}
```

`IStartStream` is a special return type Wolverine intercepts. Direct `session.Events.StartStream()` produces no return value Wolverine can intercept — this is a silent failure pattern. No errors, no data in the database.

### 10. ❌ Compound Handler with Mixed Route + JSON Body Parameters

The compound handler pattern cannot handle endpoints with both route parameters and a JSON body. `Before()` cannot access the deserialized body. Use direct implementation instead. See [HTTP Endpoints](#http-endpoints) above.

Note: **Route-only** POST endpoints with `[WriteAggregate]` work correctly — the compound handler has no body to deserialize. Also note: DELETE + compound handler fails for similar reasons. Use a single-method async handler for DELETE endpoints with no body and a computed stream ID.

### 11. ❌ Using `bus.PublishAsync()` for Integration Events in HTTP Endpoints ⚠️ CRITICAL

```csharp
// ❌ WRONG — published outside the transaction boundary
await bus.PublishAsync(new SomeIntegrationEvent(...)); // Fires even if DB rolls back

// ✅ CORRECT — enrolled in transactional outbox
var outgoing = new OutgoingMessages();
outgoing.Add(new SomeIntegrationEvent(...));
return (Results.Ok(), outgoing); // Committed with the Marten/Polecat session
```

`OutgoingMessages` is processed within the same Wolverine middleware that commits the session. `bus.PublishAsync()` sends immediately outside this boundary — messages can be published even when the DB transaction fails.

**Exception:** `bus.ScheduleAsync()` remains valid — delayed delivery cannot be expressed via `OutgoingMessages`.

### 12. ❌ Mixing `IMessageBus.InvokeAsync()` with Manual `session.Events.Append()` on the Same Stream

Combining `bus.InvokeAsync()` (which triggers the full Wolverine handler lifecycle + auto-commit) with manual `session.Events.Append()` + `session.SaveChangesAsync()` on the same aggregate creates two competing persistence strategies. The manual path bypasses `Before()` validation, and sessions may commit in unpredictable order.

**Rule:** Don't mix strategies for the same stream. Cascading `bus.InvokeAsync()` to a *different* downstream aggregate is acceptable — that is the inline policy invocation pattern, not a violation of this rule.

### 13. ❌ Expecting Wolverine to Cascade Events Appended via `session.Events.Append()` ⚠️ CRITICAL

```csharp
// ❌ WRONG — downstream handler never fires
public static OutgoingMessages Handle(SomeCmd cmd, IDocumentSession session)
{
    session.Events.StartStream(id, new StreamCreated(...)); // Invisible to Wolverine cascade
    // SomePolicyHandler.Handle(StreamCreated) will NEVER be invoked
    return outgoing;
}

// ✅ CORRECT — invoke the policy inline
public static async Task Handle(SomeCmd cmd, IDocumentSession session, IMessageBus bus)
{
    session.Events.StartStream(id, new StreamCreated(...));
    await SomePolicy.Apply(id, cmd, session, bus); // Invoke inline with bus.InvokeAsync()
}
```

Wolverine cascades from messages **returned** from `Handle()`. Events written via `session.Events.Append()` go directly to the unit-of-work — invisible to the cascade pipeline. No error, no warning — the downstream handler simply never fires.

**Rules for inline cascade:**
- Use `bus.InvokeAsync()` (not `bus.PublishAsync()`) — synchronous within the handler
- `bus.ScheduleAsync()` remains valid for delayed delivery
- Call the inline policy in **every** handler that creates the relevant stream type

### 14. ❌ `OutgoingMessages` Without a Routing Rule — `tracked.Sent.MessagesOf<T>()` Always Returns 0 ⚠️ CRITICAL

```csharp
// ❌ APPEARS TO WORK — no error at runtime, but the outbox assertion always fails
var outgoing = new OutgoingMessages();
outgoing.Add(new SellerRegistrationCompleted(participant.Id, evt.CompletedAt));
return (Results.Ok(), evt, outgoing);

// Test assertion fails even though the message was added to OutgoingMessages:
tracked.Sent.MessagesOf<SellerRegistrationCompleted>().ShouldHaveSingleItem(); // → count: 0
```

**Root cause:** Wolverine's `PublishAsync` calls `Runtime.RoutingFor(type).RouteForPublish(message, options)`.
With no routing rule configured for `SellerRegistrationCompleted`, this returns an empty route array.
`PublishAsync` then calls `Runtime.MessageTracking.NoRoutesFor(envelope)` — recording `MessageEventType.NoRoutes`
— and returns `ValueTask.CompletedTask`. The message never reaches `_outstanding`, never passes through
`FlushOutgoingMessagesAsync`, and never calls any `ISendingAgent.EnqueueOutgoingAsync()`. `tracked.Sent` is
populated by `ISendingAgent` implementations only — with no route, no sender is ever invoked.

**Resolution:** Add a routing rule in `Program.cs` for the message type. This is required before any
`tracked.Sent.MessagesOf<T>()` assertion will succeed:

```csharp
// Program.cs / host Wolverine configuration
opts.Publish(x => x.Message<SellerRegistrationCompleted>()
    .ToLocalQueue("participants-integration-events")); // M1 placeholder — no handler

// When the consuming BC is implemented, replace with the appropriate transport rule:
opts.Publish(x => x.Message<SellerRegistrationCompleted>()
    .ToRabbitExchange("seller-registration")); // M2+
```

With this rule, the flow completes: `PublishAsync` → `PersistOrSendAsync` → `FlushOutgoingMessagesAsync` →
`BufferedLocalQueue.EnqueueOutgoingAsync` → `_messageLogger.Sent(envelope)` → recorded in `tracked.Sent`.

**M1 placeholder pattern:** Routing to a named local queue with no handler is safe. Wolverine's
`NoHandlerContinuation` records `NoHandlers` then `MessageSucceeded` — no exception thrown,
`AssertNoExceptions = true` (default) is not triggered.

**This is a host configuration requirement, not a test-fixture concern.** The routing rule lives in
`Program.cs`, not in the test fixture. Future slice prompts that introduce a new integration event type must
include `Program.cs` in the allowed-file set, or require the routing rule to be pre-configured in a
scaffolding session. See `docs/skills/critter-stack-testing-patterns.md` for the corresponding test prerequisite note.

---

## Debugging with the Wolverine Diagnostics CLI

Wolverine ships three CLI sub-commands specifically designed for diagnosing AI-assisted development
issues. Run them from the project root with `dotnet run --project src/CritterBids.Api -- <command>`:

```bash
# Preview the generated C# handler code for any message type.
# Use this first when IDocumentSession injection fails or a handler behaves unexpectedly.
# Shows exactly what SessionVariableSource resolved, what middleware fired, and what
# return-type interceptors were code-generated.
dotnet run -- wolverine-diagnostics codegen-preview --message SellerRegistrationCompleted

# Or preview by HTTP route:
dotnet run -- wolverine-diagnostics codegen-preview --route "POST /api/listings"

# List every configured routing rule — message type, transport, queue/exchange name.
# Use this when tracked.Sent.MessagesOf<T>() returns 0 (see Anti-Pattern #14).
# If a message type has no entry here, Wolverine calls NoRoutesFor() and drops the message.
dotnet run -- wolverine-diagnostics describe-routing

# Show all error handling and circuit breaker policies per handler.
dotnet run -- wolverine-diagnostics describe-resiliency
```

**When to reach for each command:**

| Symptom | First command to run |
|---|---|
| `IDocumentSession` not injectable / code-gen failure | `codegen-preview --message T` |
| `[Entity]` or `[WriteAggregate]` not loading | `codegen-preview --route "VERB /path"` |
| `tracked.Sent.MessagesOf<T>()` returns 0 | `describe-routing` |
| Handler runs but wrong middleware applied | `codegen-preview` |
| Retry/circuit breaker not triggering | `describe-resiliency` |

`codegen-preview` is particularly valuable for diagnosing `SessionVariableSource`-related failures:
if the generated code shows no session variable being resolved, it confirms that
`IntegrateWithWolverine()` was not called on the store that handler belongs to.

---

## File Organization and Naming

Vertical slice organization: commands, validators, and handlers colocated in one file.

```
Features/
  Auctions/
    PlaceBid.cs          # Command + Validator + Handler
    CloseBidding.cs      # Command + Handler
    BidPlaced.cs         # Domain event (separate file)
```

| Type | Naming | Example |
|---|---|---|
| Command | `{Verb}{Noun}.cs` | `PlaceBid.cs` |
| Domain Event | `{Noun}{PastTenseVerb}.cs` | `BidPlaced.cs` |
| Handler class | Static, suffix `Handler` | `PlaceBidHandler` |
| Validator | Suffix `Validator` | `PlaceBidValidator` |

---

## References

- [Wolverine Message Handlers Guide](https://wolverinefx.net/guide/handlers/)
- [Wolverine + Marten Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/marten/event-sourcing.html)
- [Wolverine + Polecat Aggregate Handler Workflow](https://wolverinefx.net/guide/durability/polecat/event-sourcing)
- [Railway Programming with Wolverine](https://wolverinefx.net/tutorials/railway-programming.html)
- [Functional Event Sourcing Decider](https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider)