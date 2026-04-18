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
9. [IoC and Service Optimization](#ioc-and-service-optimization)
10. [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
11. [Debugging with the Wolverine Diagnostics CLI](#debugging-with-the-wolverine-diagnostics-cli)
12. [File Organization and Naming](#file-organization-and-naming)

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
| Exception | `OnException`, `OnExceptionAsync` | Inline exception handling (see below) |

**Key points:**
- Wolverine discovers these by convention — no interfaces required
- Values returned from early methods become parameters for later ones ("tuple threading")
- Tuple order matters — Wolverine wires by type and position
- Early methods can short-circuit by returning `HandlerContinuation.Stop`, `ProblemDetails`, or `IResult`

**⚠️ CRITICAL: Tuple Order Matters**

Wolverine wires dependencies **by position in the tuple**, not by parameter name. If `Load()` returns `(Listing, Bidder)` but `Handle()` expects `(Bidder bidder, Listing listing)`, Wolverine passes `Listing` as the first parameter — causing runtime errors. Always match tuple order to parameter order across methods.

### `OnException` / `OnExceptionAsync` Convention

Define an `OnException` or `OnExceptionAsync` method on the handler class to catch exceptions inline. The first parameter is the exception type; additional parameters inject the triggering message and any other services. Wolverine generates a `try`/`catch` around the handler invocation that dispatches to the matching method by exception specificity (most derived first).

**The exception is swallowed after `OnException` returns** — no re-throw. This is by design: `OnException` is a *recovery* hook, not a logging hook. If you want the framework's retry/DLQ pipeline to see the exception, don't define `OnException` for that type; let it propagate.

**For compensation patterns** — the canonical CritterBids use case is payment-failure compensation in the Settlement saga, or obligation handling when external calls throw. Return `OutgoingMessages` to publish compensating events as part of the recovery:

```csharp
public static class ProcessPaymentHandler
{
    public static (Events, OutgoingMessages) Handle(
        ProcessPayment cmd,
        [WriteAggregate] SettlementSaga saga,
        IPaymentGateway gateway)
    {
        // may throw PaymentGatewayException
        var result = gateway.Charge(cmd.Amount, cmd.PaymentMethodId);

        var events = new Events();
        events.Add(new PaymentSucceeded(saga.Id, result.TransactionId));

        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.Settlement.PaymentConfirmed(saga.ListingId, cmd.Amount));

        return (events, outgoing);
    }

    // Catches PaymentGatewayException inline, emits compensation, swallows the exception.
    // Note: no rethrow — the saga stays alive to handle PaymentFailed next.
    public static OutgoingMessages OnException(
        PaymentGatewayException ex,
        ProcessPayment cmd)
    {
        var outgoing = new OutgoingMessages();
        outgoing.Add(new Contracts.Settlement.PaymentFailed(cmd.SagaId, ex.Message));
        return outgoing;
    }
}
```

**Supported return types** mirror handler returns: `void`, `ProblemDetails` / `IResult` (HTTP), `HandlerContinuation`, `OutgoingMessages`. For HTTP endpoints, returning `ProblemDetails` from `OnException` yields a structured error response without touching middleware retry policies.

**Multiple `OnException` methods** for different exception types are supported — Wolverine orders them by specificity. A `catch (Exception ex)` overload at the bottom of the class acts as a default handler.

**Versus framework retry policies:** `OnException` is the right tool when the handler itself can **decide** what compensating action to take. For transient retry-then-fail patterns (e.g., `SqlException` → retry 3x — DLQ), keep using `opts.OnException<T>()` global policies or `[RetryNow]`/`[MaximumAttempts]` attributes. See `wolverine-sagas.md` and the Critter Stack resiliency patterns.

### MiddlewareScoping — HTTP-only vs Messaging-only Lifecycle Methods

When a single handler class serves **both** an HTTP endpoint and a message-bus invocation (the "hybrid handler" pattern), some lifecycle methods should only run in one context. Use `[WolverineBefore(MiddlewareScoping.HttpEndpoints)]` or `[WolverineBefore(MiddlewareScoping.MessageHandlers)]` (and the same for `After` / `Finally` variants) to scope a method to one path.

```csharp
public static class ProcessOrderHandler
{
    [WolverineBefore(MiddlewareScoping.HttpEndpoints)]
    public static void SetTenantFromHeader(HttpContext context, Envelope envelope)
    {
        envelope.TenantId = context.Request.Headers["X-Tenant-Id"].ToString();
    }

    [WolverineBefore(MiddlewareScoping.MessageHandlers)]
    public static void CheckEnvelopeMetadata(Envelope envelope)
    {
        // Message-bus-only concern — HttpContext is not available here
    }

    [WolverinePost("/api/orders/process")]
    public static OrderProcessed Handle(ProcessOrder cmd) { /* ... */ }
}
```

**Two CritterBids-relevant footguns:**

1. **`HttpContext` in a hybrid handler's `Before`/`Load` without scoping** throws `NullReferenceException` when invoked via `IMessageBus.InvokeAsync(...)`. Always scope HTTP-only concerns with `MiddlewareScoping.HttpEndpoints`.
2. **Classes suffixed `Endpoint` / `Endpoints` are HTTP-only** by Wolverine's discovery convention. A class suffixed `Handler` can serve both paths. If a hybrid pattern is intended, name the class `Handler`, not `Endpoint`.

CritterBids has no hybrid handlers through M2.5. The pattern becomes relevant when a single command surface needs to accept both direct `InvokeAsync` from other BCs (via the integration bus) and external HTTP traffic (e.g. a public API for `SubmitListing` once seller tooling lands).

### Applying Middleware Globally and Per-Handler

Middleware — custom `Before`/`After`/`Finally`/`OnException` logic defined on a separate class — can be applied in several scopes:

| Scope | Mechanism | When to use |
|---|---|---|
| Per-handler class | `[Middleware(typeof(MyMiddleware))]` on the handler class or method | Narrow cross-cutting concern for a specific handler |
| By message interface | `opts.Policies.ForMessagesOfType<IMyInterface>().AddMiddleware(typeof(MyMiddleware))` | All handlers for messages implementing a marker interface |
| Filtered global | `opts.Policies.AddMiddleware<MyMiddleware>(chain => chain.MessageType.IsInNamespace("..."))` | Cross-cutting concern for a namespace or subset |
| Unconditional global | `opts.Policies.AddMiddleware<MyMiddleware>()` | True cross-cutting concern (e.g., audit logging for every handler) |
| Per-handler customization | `public static void Configure(HandlerChain chain)` method on the handler class | Per-handler error policies, timeouts, audited members — full chain access |
| Custom policy | `IHandlerPolicy` / `IHttpPolicy` implementations registered via `opts.Policies.Add<MyPolicy>()` | Cross-cutting policy with complex conditional application |

CritterBids currently uses none of these. The conventional `Before`/`Validate`/`Load`/`Handle`/`After` methods on the handler class itself cover every pattern through M2.5. The table exists so that when a cross-cutting need arises (bid-throttling, flash-session rate limits, audit trails for regulated operations), the available options are clear and we can choose the narrowest scope that fits.

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

**How Wolverine resolves aggregate ID for `[WriteAggregate]`:**

1. Positional constructor argument on the attribute (`[WriteAggregate(nameof(Cmd.SomeProperty))]`) —
   the explicit override, always wins.
2. Command property named `{camelCaseParameterName}Id` (from the handler parameter, e.g. `listing` →
   `listingId`) or, failing that, a property named `id`.
3. HTTP route parameter on the enclosing endpoint (e.g. `/listings/{listingId}`) — handled by
   Wolverine.Http during endpoint chain assembly.
4. Strong-typed ID fallback — a property whose type derives from `IStronglyTypedId` whose
   underlying value matches the aggregate's stream type.

Source: `WriteAggregateAttribute.FindIdentity()` in
`C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\WriteAggregateAttribute.cs` (lines
164–215). Note the resolver walks command properties by name — it does **not** inspect
`[Identity]` attributes (see "Two resolution paths" below).

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

### Two resolution paths — `[WriteAggregate]` vs `[AggregateHandler]`

Wolverine has **two separate code paths** for figuring out the stream ID, and they look at
different things on your command. Knowing which is in play prevents a whole category of "why
isn't this working" confusion.

| Path | Used by | Resolver | Honours `[Identity]`? |
|---|---|---|---|
| Parameter-level | `[WriteAggregate]` (primary), `[ReadAggregate]` | `WriteAggregateAttribute.FindIdentity()` | **No** |
| Class-level | `[AggregateHandler]` | `AggregateHandling.DetermineAggregateIdMember()` | **Yes** |

`AggregateHandling.DetermineAggregateIdMember()` (in
`C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\AggregateHandling.cs`, lines 191–212)
checks the Marten `[Identity]` attribute *first*, then falls back to the `{AggregateName}Id`/`Id`
convention, then strong-typed IDs. `WriteAggregateAttribute.FindIdentity()` does **not** check
`[Identity]` at all.

**Practical consequence:** `[Identity]` annotations on a command property are only load-bearing if
the handler uses the class-level `[AggregateHandler]` attribute. On a `[WriteAggregate]` handler,
`[Identity]` is a no-op — the override has to come from the positional `nameof(...)` argument on
the attribute itself. CritterBids standardises on `[WriteAggregate]` (per the "Three Attributes"
table above), so `[Identity]` is not part of our aggregate-handler toolkit.

### Custom identity sources (Wolverine 5.25+)

For HTTP handlers where the identity does not come from the command body or route, Wolverine 5.25
introduced four attributes that plug custom identity resolution into both `[WriteAggregate]` and
`[ReadAggregate]`:

| Attribute | Identity source |
|---|---|
| `[FromHeader("X-Tenant-Id")]` | Named HTTP header |
| `[FromClaim(ClaimTypes.NameIdentifier)]` | Named claim on `HttpContext.User` |
| `[FromRoute("tenantId")]` | Named route segment (explicit — useful when the default name-match doesn't apply) |
| `[FromMethod(typeof(TenantResolver), nameof(TenantResolver.Resolve))]` | Static method — receives `HttpContext`, returns `Guid`/`string`/strongly-typed ID |

These let aggregate identity flow from authentication context (e.g. seller ID from a JWT claim)
without polluting the command record with a property the client doesn't set. CritterBids does not
use these yet — `[AllowAnonymous]` is the project stance through M6 — but the option is on the
table once M6 introduces real authentication. Source: Wolverine docs at
`C:\Code\JasperFx\wolverine\docs\guide\http\marten.md` under "Custom Aggregate Identity".

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

**Overriding the version property name.** By default Wolverine looks for a command property named
`Version`. When the command uses a different name — for instance an `ETag` surfaced through an
`If-Match` header, or a separate `ExpectedVersion` field — set `VersionSource` on the attribute:

```csharp
public sealed record UpdateReserve(Guid ListingId, decimal NewReserve, long ExpectedVersion);

public static Events Handle(
    UpdateReserve cmd,
    [WriteAggregate(nameof(UpdateReserve.ListingId), VersionSource = nameof(UpdateReserve.ExpectedVersion))]
    SellerListing listing)
{
    // ...
}
```

`VersionSource` is a named property (unlike the positional `routeOrParameterName` argument), so
keep the positional argument first and the named property after. `nameof()` keeps both refactor-
safe. Source: `WriteAggregateAttribute.VersionSource` in
`C:\Code\JasperFx\wolverine\src\Persistence\Wolverine.Marten\WriteAggregateAttribute.cs:68`.

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
| `UpdatedAggregate` (marker type) | Re-aggregate the stream after appends and write the rebuilt state to the HTTP response body |
| `void` | No events, no messages |

### `UpdatedAggregate` — return rebuilt aggregate state

On a `[WriteAggregate]` HTTP endpoint, returning `UpdatedAggregate` (alongside `Events`) tells
Wolverine to re-aggregate the stream after the new events are applied and serialise the rebuilt
aggregate to the response body. The alternative — hand-building the post-update state in the
handler — duplicates the `Apply()` logic the aggregate already owns and drifts over time.

```csharp
[WolverinePut("/api/listings/{listingId}/draft")]
public static (UpdatedAggregate, Events) Handle(
    UpdateDraftListing cmd,
    [WriteAggregate(nameof(UpdateDraftListing.ListingId))] SellerListing listing)
{
    // business checks, then:
    return (new UpdatedAggregate(),
            [new DraftListingUpdated(listing.Id, cmd.Title, cmd.ReservePrice, cmd.BuyItNowPrice, DateTimeOffset.UtcNow)]);
}
```

The client receives the post-update `SellerListing` document without any round-trip rebuild in
the handler. CritterBids has no HTTP endpoint using this yet — `UpdateDraftListing` is dispatched
via `IMessageBus` and has no endpoint through M2.5 — but this is the idiomatic shape when the
endpoint arrives. Source: `C:\Code\JasperFx\wolverine\docs\guide\http\marten.md` under
"Returning Updated Aggregate".

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

### Inline String Validation — Lightweight Alternative to `ProblemDetails`

For simple error-string validation that doesn't need HTTP status-code control, return `IEnumerable<string>` from a `Validate` method. Any yielded strings abort the handler pipeline; an empty enumeration allows execution to proceed.

```csharp
public static class PlaceBidHandler
{
    public static IEnumerable<string> Validate(PlaceBid cmd, Listing? listing)
    {
        if (listing is null)
            yield return "Listing not found";
        else if (!listing.IsOpen)
            yield return "Listing is not open for bidding";

        if (cmd.Amount <= 0)
            yield return "Bid amount must be positive";
    }

    public static (Events, OutgoingMessages) Handle(PlaceBid cmd, [WriteAggregate] Listing listing)
    {
        // happy path — only reached when Validate yielded nothing
    }
}
```

**Behaviour split by context:**
- **Message handlers:** yielded strings are logged as warnings; the handler is skipped. No response flows back to a synchronous caller.
- **HTTP endpoints:** Wolverine synthesises a `ProblemDetails` with status 400 containing the error strings in the `errors` dictionary, and writes it as the response body.

**When to prefer over `ProblemDetails`:**
- The error is informational and status 400 is fine — no need for custom status codes
- You want to collect multiple errors per invocation (a `Validate` returning `ProblemDetails` typically short-circuits on first failure)
- The handler is a message handler where `ProblemDetails` would be wasted (there's no HTTP response to attach it to)

**When to stick with `ProblemDetails`:**
- HTTP endpoints that need specific status codes (`404`, `409`, `412`) — string validation always yields 400
- Endpoints that need a structured error payload beyond a string list

For M3+ bid validation in CritterBids, string validation is a clean fit for `Validate`-level checks where the errors are informational. Reserve `ProblemDetails` for endpoints where the HTTP status code carries semantic meaning (Listing not found → 404; Listing already closed → 409).

### `WolverineContinue.NoProblems` Is Reference-Equality

Wolverine's code generation checks the result of a `Validate` method with `ReferenceEquals(result, WolverineContinue.NoProblems)`, not value equality. Always return the static `WolverineContinue.NoProblems` singleton to signal "continue" — do not construct a new empty `ProblemDetails` with the thought that it's equivalent. A new instance is **not** the same reference, so the pipeline short-circuits on your empty problem-details as though validation failed.

```csharp
// ❌ WRONG — new instance, not the NoProblems singleton
public static ProblemDetails Validate(PlaceBid cmd) =>
    new ProblemDetails();   // pipeline aborts with status-0 ProblemDetails

// ✅ CORRECT
public static ProblemDetails Validate(PlaceBid cmd) => WolverineContinue.NoProblems;
```

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

### `[EmptyResponse]` vs `Results.NoContent()` — The Aggregate Endpoint Footgun

This is the single most consequential HTTP-endpoint mistake on aggregate handlers. It is silent — the endpoint appears to work, returns `200 OK`, and no events are persisted.

**The failure mode:**

```csharp
// ❌ FAILS SILENTLY — event is serialized as HTTP response body, NOT appended to stream
[WolverinePost("/api/listings/{listingId}/close")]
public static BiddingClosed Close([WriteAggregate] Listing listing)
{
    return new BiddingClosed(listing.Id, listing.CurrentHighBid, listing.HighBidderId);
}
```

On an aggregate-handler endpoint, a single return value is treated as the **HTTP response body**. The `BiddingClosed` event is written to the response (as JSON), **never appended to the event stream**. No exception, no warning. The next `FetchForWriting<Listing>` shows the pre-close state.

**Two correct shapes:**

**Shape 1 — `[EmptyResponse]` attribute.** Suppresses the HTTP body; the returned event is appended to the stream. Response is `204 No Content`.

```csharp
[WolverinePost("/api/listings/{listingId}/close"), EmptyResponse]
public static BiddingClosed Close([WriteAggregate] Listing listing) =>
    new BiddingClosed(listing.Id, listing.CurrentHighBid, listing.HighBidderId);
```

**Shape 2 — explicit tuple with `Results.NoContent()`.** The first element is the HTTP response, subsequent elements are events/messages. Preferred when you also want to return `OutgoingMessages`.

```csharp
[WolverinePost("/api/listings/{listingId}/close")]
public static (IResult, Events, OutgoingMessages) Close(
    [WriteAggregate] Listing listing)
{
    var closed = new BiddingClosed(listing.Id, listing.CurrentHighBid, listing.HighBidderId);
    var outgoing = new OutgoingMessages();
    outgoing.Add(new Contracts.Auctions.ListingSold(listing.Id, listing.SellerId, listing.HighBidderId!.Value, listing.CurrentHighBid, DateTimeOffset.UtcNow));
    return (Results.NoContent(), new Events(closed), outgoing);
}
```

**When to pick which:**

| Returning | Use |
|---|---|
| Just an event to append, 204 response | `[EmptyResponse]` — minimal ceremony |
| Event(s) + `OutgoingMessages` | Explicit tuple with `Results.NoContent()` first |
| Updated aggregate state in the HTTP body | `(UpdatedAggregate, Events)` tuple — see Handler Return Patterns section |
| A proper creation response (`201 Created`) | `(CreationResponse<T>, IStartStream)` — see Handler Return Patterns section |

**CritterBids rule:** never return a bare event type from an aggregate-handler HTTP endpoint. Always one of: `[EmptyResponse]` + event, tuple with explicit HTTP result first, or `UpdatedAggregate` + events when state is needed.

This is a different failure from Anti-Pattern #3 (wrong tuple order) and Anti-Pattern #9 (direct `session.Events.StartStream()` instead of `IStartStream` return). It is specific to bare single-value returns on aggregate-handler HTTP endpoints, where Wolverine's default "return value is the HTTP body" rule collides with the aggregate-handler "return value is the event to append" expectation.

### Prefer Concrete Return Types Over `IResult`

`IResult` is opaque to Wolverine's OpenAPI metadata generation. When an endpoint returns a concrete type (`Listing`, `BidPlaced`, `CreationResponse<Guid>`, `ProblemDetails`), Wolverine infers the status code, response schema, and content type automatically — OpenAPI docs and generated clients get accurate information with zero boilerplate.

```csharp
// ❌ OPAQUE — OpenAPI shows no response schema; requires manual [ProducesResponseType]
[WolverineGet("/api/listings/{listingId}")]
public static IResult Get(Guid listingId, IDocumentSession session) =>
    /* ... */ Results.Ok(listing);

// ✅ CONCRETE — OpenAPI infers 200 + Listing schema automatically
[WolverineGet("/api/listings/{listingId}")]
public static Task<Listing?> Get(Guid listingId, IDocumentSession session) =>
    session.LoadAsync<Listing>(listingId);   // nullable → 200 or 404 automatically
```

**Reserve `IResult` for:**
- Endpoints that genuinely have runtime-variable response shapes (e.g. conditional redirects, content negotiation)
- The `Results.NoContent()` first element of tuple returns on aggregate handlers (covered above)

**Return type → OpenAPI mapping cheat-sheet:**

| Return type | Inferred status codes | Body |
|---|---|---|
| `T` (concrete) | 200 | JSON of `T` |
| `T?` (nullable) | 200 or 404 | JSON of `T`, or empty on 404 |
| `void` / `Task` | 200 | empty |
| `CreationResponse<T>` | 201 | JSON of `T`, `Location` header |
| `AcceptResponse` | 202 | empty, `Location` header |
| A `Validate` returning `ProblemDetails` | 400 added automatically | `application/problem+json` on failure |
| `IResult` | opaque | manual `[ProducesResponseType]` required |

Let Wolverine do the OpenAPI work whenever the endpoint's response shape is known at compile time.

---

## IoC and Service Optimization

Wolverine's handler pipeline bypasses the IoC container at runtime wherever it can. Understanding the mechanics is rarely important day-to-day, but becomes critical when diagnosing slow cold starts, service-location warnings, or "service not resolved" failures in production.

### The principle: the fastest IoC is no IoC

At startup Wolverine inspects every service registration and generates adapter code per handler that calls constructors directly rather than resolving through `IServiceProvider`. Singletons are captured once and inlined on the adapter class. Scoped and transient services with public constructors get `new` calls generated into the handler pipeline. Disposable services get `using` / `await using` statements generated automatically.

**Practical consequence:** a handler with `IDocumentSession` and `ILogger` parameters produces generated C# that looks roughly like `new LightweightSession(...)` and a captured `ILogger` field on the adapter — no `serviceProvider.GetRequiredService<T>()` call at runtime. Allocation pressure is minimal, and generated stack traces on exceptions stay short and readable.

### Prefer concrete-type registrations over lambda factories

The registration shape determines whether Wolverine can generate a direct constructor call or has to fall back to runtime service location via `IServiceScopeFactory`. Runtime service location is always a performance regression.

```csharp
// ✅ GOOD — Wolverine generates a direct `new OrderRepository(...)` call
services.AddScoped<IOrderRepository, OrderRepository>();

// ❌ FORCES SERVICE LOCATION — Wolverine cannot see through the lambda
services.AddScoped<IOrderRepository>(sp =>
    new OrderRepository(sp.GetRequiredService<IDocumentSession>()));
```

The lambda form is sometimes unavoidable (Refit proxies, `IHttpClientFactory`-backed clients, factory-only third-party registrations). For those, see the escape hatch below.

### `ServiceLocationPolicy` — three modes

Control how Wolverine treats service-location fallbacks:

```csharp
opts.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;   // default — log warning
opts.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;    // silent fallback
opts.ServiceLocationPolicy = ServiceLocationPolicy.NotAllowed;       // throws at host startup
```

**CritterBids recommendation:** keep the default `AllowedButWarn` in development (fallbacks visible in the log but don't block startup), and consider `NotAllowed` in CI to catch regressions before deploy. Production posture is a judgment call — `NotAllowed` fails hard but gives you maximum information if a reg regresses.

### Opt-in escape hatch for unavoidable service location

When a specific service cannot be registered as a concrete type (Refit proxy, dynamic decorator, factory-only registration), allow-list it so the warning stops firing but the policy remains active for other services:

```csharp
opts.CodeGeneration.AlwaysUseServiceLocationFor<IRefitClient>();
```

This is the correct escape hatch — don't suppress the warning by flipping the policy to `AlwaysAllowed` globally.

### Pre-generate code for production

By default Wolverine generates handler adapter code at host startup (`TypeLoadMode.Dynamic`). That's fine for development but adds startup latency in production proportional to the handler count. Pre-generate for production:

```csharp
// In Program.cs, before UseWolverine(...)
opts.Services.CritterStackDefaults(x =>
{
    x.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;
    x.Production.GeneratedCodeMode = TypeLoadMode.Static;
    x.Production.AssertAllPreGeneratedTypesExist = true;   // fail fast if a type is missing at startup
});
```

Write the generated code to disk as part of the build:

```bash
dotnet run --project src/CritterBids.Api -- codegen write
```

The generated files land in `src/CritterBids.Api/Internal/Generated/WolverineHandlers/`. Commit them to source control, or — better — regenerate them in CI before the Docker build step, since they're dependent on the current handler set and drift silently when handlers change.

**`AssertAllPreGeneratedTypesExist = true` is the safety net:** if a handler shipped without its generated adapter on disk, host startup fails immediately rather than falling back to dynamic generation and masking the problem.

### `ILogger` over `ILogger<T>`

Inject `ILogger` (not `ILogger<T>`) in Wolverine handlers. Wolverine already tags log output with the handler type context, so the generic variant is redundant noise.

```csharp
// ✅ GOOD
public static void Handle(PlaceBid cmd, IDocumentSession session, ILogger logger)

// ❌ Redundant — Wolverine has already captured the handler type
public static void Handle(PlaceBid cmd, IDocumentSession session, ILogger<PlaceBidHandler> logger)
```

### Diagnosing service-location fallbacks

```bash
# Preview generated adapter code. Look for IServiceScopeFactory usage — that's service location.
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --message PlaceBid

# Full diagnostic report
dotnet run --project src/CritterBids.Api -- describe
```

If the generated code for a handler shows `IServiceScopeFactory` where you expected a direct constructor call, check the registration — it's almost always a lambda factory or an internal type that Wolverine couldn't see through.

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

### 15. ❌ Lambda Factory Registrations When a Concrete Type Would Work

The registration shape determines whether Wolverine's code generation can emit a direct constructor call or must fall back to runtime service location. Lambda factories force service location, which allocates a scoped container per message and prevents pipeline optimisation.

```csharp
// ❌ WRONG — opaque to codegen; forces IServiceScopeFactory at runtime
services.AddScoped<IOrderRepository>(sp =>
    new OrderRepository(sp.GetRequiredService<IDocumentSession>()));

// ✅ CORRECT — Wolverine generates a direct `new OrderRepository(...)` call
services.AddScoped<IOrderRepository, OrderRepository>();
```

**When the lambda form is unavoidable** (Refit proxies, `IHttpClientFactory`-backed clients, decorators), allow-list the specific type:

```csharp
opts.CodeGeneration.AlwaysUseServiceLocationFor<IRefitClient>();
```

Do not suppress the warning globally by flipping `ServiceLocationPolicy` to `AlwaysAllowed` — that hides future regressions. See `IoC and Service Optimization` above.

### 16. ❌ `bus.InvokeAsync()` for Fire-and-Forget Work

`InvokeAsync` executes a handler **synchronously** and blocks the caller until completion. It's for request/reply and local in-process command dispatch. Using it for fire-and-forget work ("publish the event, don't wait") blocks an HTTP or handler thread on work that belongs on a queue.

```csharp
// ❌ WRONG — HTTP thread blocks until SendWelcomeEmail handler completes
[WolverinePost("/api/participants/register")]
public static async Task<IResult> Register(RegisterBidder cmd, IMessageBus bus)
{
    // ... create participant ...
    await bus.InvokeAsync(new SendWelcomeEmail(cmd.BidderId));   // sync wait on email send
    return Results.Ok();
}

// ✅ CORRECT — cascading return value, enrolled in outbox, non-blocking
[WolverinePost("/api/participants/register")]
public static (IResult, ParticipantRegistered, OutgoingMessages) Register(RegisterBidder cmd, ...)
{
    // ... create participant ...
    var outgoing = new OutgoingMessages();
    outgoing.Add(new SendWelcomeEmail(cmd.BidderId));   // queued, not awaited
    return (Results.Ok(), new ParticipantRegistered(cmd.BidderId), outgoing);
}
```

**Quick reference:**

| Caller intent | Use |
|---|---|
| Caller needs the handler's result | `InvokeAsync<T>` — request/reply, synchronous |
| Caller needs to know the handler succeeded | `InvokeAsync` (no generic) — sync, no return |
| Caller publishes and moves on | Cascading return value OR `bus.PublishAsync` — async, outboxed |
| Caller is inside an HTTP endpoint | Always prefer the cascading return value (participates in the transactional outbox) |

`bus.ScheduleAsync` remains the correct tool for delayed delivery — it is neither sync nor outbox-bypassing in the problematic way. See `wolverine-sagas.md` for scheduling patterns.

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