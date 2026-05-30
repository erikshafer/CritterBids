---
name: wolverine-message-handlers
description: "Wolverine message handlers and HTTP endpoints in CritterBids: handler shape, the OutgoingMessages routing-rule footgun, IoC registration shape, and InvokeAsync misuse. Use when authoring or debugging a Wolverine handler or endpoint in any CritterBids BC."
cluster: wolverine
tags: [wolverine, handlers, http, outbox, diagnostics]
---

# Wolverine Message Handlers

> CritterBids handler and endpoint conventions on top of the Critter Stack.
> Generic Wolverine handler mechanics live in the ai-skills `wolverine-handlers-*` family;
> **this skill documents only the CritterBids-specific shape decisions and the footguns this codebase hit.**

## When to apply this skill

Use this skill when:

- Authoring a command/event handler or a Wolverine HTTP endpoint in any CritterBids BC.
- An outbox assertion (`tracked.Sent.MessagesOf<T>()`) returns 0 unexpectedly.
- A handler forces runtime service location instead of code-generated constructor calls.
- Choosing between cascading return values, `bus.PublishAsync`, and `bus.InvokeAsync`.

Do NOT use this skill for: event-sourced aggregate mutation mechanics (see `marten-event-sourcing`),
sagas (see `wolverine-sagas`), or cross-BC integration contracts (see `integration-messaging`).

## Read upstream first

Generic Wolverine handler mechanics are fully covered upstream. Read these ai-skills (license required;
install via `npx skills add`) before this skill — they cover ~80% of handler authoring:

1. `wolverine-handlers-fundamentals` — handler discovery, signatures, cascading messages.
2. `wolverine-handlers-pure-functions` — the Decider pattern (pure decide → return events).
3. `wolverine-handlers-a-frame-architecture` — `Load`/`Validate`/`Handle` compound handlers.
4. `wolverine-handlers-railway-programming` — `ProblemDetails`, `HandlerContinuation`, short-circuiting.
5. `wolverine-handlers-declarative-persistence` — `[WriteAggregate]`/`[ReadAggregate]`/`[Entity]`, return tuples.
6. `wolverine-handlers-middleware` — `Before`/`After`/`OnException`, middleware scoping.
7. `wolverine-handlers-ioc-and-service-optimization` — why "the fastest IoC is no IoC".
8. `wolverine-http-fundamentals` + `wolverine-http-marten-integration` — endpoint authoring.

This skill picks up at the CritterBids decisions and the three anti-patterns this codebase actually hit.

## CritterBids handler shape

All eight BCs are Marten/PostgreSQL (ADR 011 — All-Marten Pivot). There is no Polecat handler shape in
CritterBids today; `MartenOps` is used everywhere.

Project conventions layered on top of the upstream handler model:

- **`sealed record`** for every command, event, query, and read model — no exceptions.
- **Static handler classes**, suffix `Handler`; vertical-slice file layout (command + validator + handler colocated).
- **`IReadOnlyList<T>`**, never `List<T>`, on commands/events/aggregates.
- **Handlers return events/messages** — never call `session.Store()` / `SaveChangesAsync()` directly
  (`AutoApplyTransactions()` commits; see `marten-event-sourcing`).
- **Integration events leave a handler via `OutgoingMessages`** — never `IMessageBus.PublishAsync` inside
  an HTTP endpoint, and never `IMessageBus` at all except `bus.ScheduleAsync()` for delayed delivery.
- **`BidderId`** identifies a participant — never "paddle", anywhere.
- **`[AllowAnonymous]`** on all endpoints through M6; the `[Authorize]` convention resumes at M6.

```csharp
// Features/Auctions/PlaceBid.cs  — command + handler colocated (vertical slice)
public sealed record PlaceBid(Guid ListingId, Guid BidderId, decimal Amount);

public static class PlaceBidHandler
{
    // Validate runs before Handle; returns ProblemDetails to short-circuit with HTTP 400.
    public static ProblemDetails Validate(PlaceBid cmd) =>
        cmd.Amount <= 0
            ? new ProblemDetails { Status = 400, Title = "Bid amount must be positive" }
            : WolverineContinue.NoProblems;

    [WolverinePost("/api/listings/{listingId}/bids")]
    public static (IResult, Events) Handle(PlaceBid cmd, [WriteAggregate] Listing listing) =>
        (Results.Ok(), [new BidPlaced(cmd.ListingId, cmd.BidderId, cmd.Amount)]);
}
```

## CritterBids anti-patterns (hard-won)

These three are the genuinely CritterBids-specific findings. Everything else in the generic anti-pattern
catalog is upstream in `wolverine-handlers-fundamentals` / `-declarative-persistence`.

### ❌ `OutgoingMessages` without a routing rule — `tracked.Sent.MessagesOf<T>()` always returns 0 ⚠️ CRITICAL

```csharp
// APPEARS to work — no runtime error, but the outbox assertion always fails:
var outgoing = new OutgoingMessages();
outgoing.Add(new SellerRegistrationCompleted(participant.Id, evt.CompletedAt));
return (Results.Ok(), evt, outgoing);

tracked.Sent.MessagesOf<SellerRegistrationCompleted>().ShouldHaveSingleItem(); // → count: 0
```

**Root cause:** with no routing rule for the message type, `PublishAsync` →
`Runtime.RoutingFor(type).RouteForPublish(...)` returns an empty route array, records
`MessageEventType.NoRoutes`, and returns. The message never reaches `_outstanding`, never flushes, never
invokes an `ISendingAgent` — and `tracked.Sent` is populated by sending agents only.

**Resolution — a host config requirement, not a test-fixture concern.** Add a routing rule in `Program.cs`:

```csharp
// M1 placeholder — routing to a named local queue with no handler is safe:
opts.Publish(x => x.Message<SellerRegistrationCompleted>()
    .ToLocalQueue("participants-integration-events"));

// M2+ — replace with the real transport once the consuming BC exists:
opts.Publish(x => x.Message<SellerRegistrationCompleted>()
    .ToRabbitExchange("seller-registration"));
```

Wolverine's `NoHandlerContinuation` records `NoHandlers` then `MessageSucceeded` — no exception, so the
default `AssertNoExceptions = true` is not tripped. Any slice prompt that introduces a new integration
event type must include `Program.cs` in its allowed-file set, or have the routing rule pre-scaffolded.
See `critter-stack-testing-patterns` for the corresponding test prerequisite.

### ❌ Lambda factory registrations when a concrete type would work

The registration *shape* decides whether Wolverine's codegen emits a direct constructor call or falls
back to runtime service location (which allocates a scoped container per message).

```csharp
// ❌ opaque to codegen — forces IServiceScopeFactory at runtime
services.AddScoped<IOrderRepository>(sp => new OrderRepository(sp.GetRequiredService<IDocumentSession>()));

// ✅ Wolverine generates a direct `new OrderRepository(...)` call
services.AddScoped<IOrderRepository, OrderRepository>();
```

When the lambda form is unavoidable (Refit proxies, `IHttpClientFactory` clients, decorators), allow-list
the specific type — do **not** flip `ServiceLocationPolicy` to `AlwaysAllowed` globally (it hides future regressions):

```csharp
opts.CodeGeneration.AlwaysUseServiceLocationFor<IRefitClient>();
```

### ❌ `bus.InvokeAsync()` for fire-and-forget work

`InvokeAsync` executes a handler **synchronously** and blocks the caller. It's for request/reply and
in-process command dispatch — not "publish and move on". Using it for fire-and-forget blocks an HTTP or
handler thread on queue-able work.

```csharp
// ❌ HTTP thread blocks until SendWelcomeEmail completes
await bus.InvokeAsync(new SendWelcomeEmail(cmd.BidderId));

// ✅ cascading return value — enrolled in the outbox, non-blocking
var outgoing = new OutgoingMessages();
outgoing.Add(new SendWelcomeEmail(cmd.BidderId));
return (Results.Ok(), new ParticipantRegistered(cmd.BidderId), outgoing);
```

| Caller intent | Use |
|---|---|
| Needs the handler's result | `InvokeAsync<T>` — request/reply, synchronous |
| Needs to know the handler succeeded | `InvokeAsync` (no generic) — sync, no return |
| Publishes and moves on | Cascading return value (preferred in endpoints) OR `bus.PublishAsync` |
| Delayed delivery | `bus.ScheduleAsync` — the one justified `IMessageBus` use in a handler |

## Diagnostics

When a handler misbehaves, preview the generated code before guessing. Run from the repo root against the
single API host:

```bash
# What did codegen actually emit? (session resolution, middleware, return interceptors)
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --handler SellerRegistrationCompleted
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics codegen-preview --route "POST /api/listings"

# Why does tracked.Sent return 0? (see anti-pattern #1 above)
dotnet run --project src/CritterBids.Api -- wolverine-diagnostics describe-routing --all
```

| Symptom | First command |
|---|---|
| `IDocumentSession` not injectable / codegen failure | `codegen-preview --handler T` |
| `tracked.Sent.MessagesOf<T>()` returns 0 | `describe-routing "<Type>"` or `--all` |
| Wrong middleware applied | `codegen-preview --handler T` |
| Retry / circuit breaker not firing | `describe` (Error Handling section) |

If `codegen-preview` shows no session variable resolved, `IntegrateWithWolverine()` was not called on the
store that handler belongs to. Full CLI surface: `diagnostics` skill and upstream
`wolverine-observability-command-line-diagnostics`.

## File organization

Vertical slice — command, validator, and handler colocated; domain events in their own files.

```
Features/Auctions/
  PlaceBid.cs       # Command + Validator + Handler
  CloseBidding.cs   # Command + Handler
  BidPlaced.cs      # Domain event (separate file)
```

| Type | Naming | Example |
|---|---|---|
| Command | `{Verb}{Noun}.cs` | `PlaceBid.cs` |
| Domain event | `{Noun}{PastTenseVerb}.cs` | `BidPlaced.cs` |
| Handler class | static, suffix `Handler` | `PlaceBidHandler` |
| Validator | suffix `Validator` | `PlaceBidValidator` |

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-handlers-fundamentals`, `wolverine-handlers-pure-functions`, `wolverine-handlers-a-frame-architecture`,
  `wolverine-handlers-railway-programming`, `wolverine-handlers-declarative-persistence`,
  `wolverine-handlers-middleware`, `wolverine-handlers-ioc-and-service-optimization`.
- `wolverine-http-fundamentals`, `wolverine-http-marten-integration` — endpoint authoring.
- `wolverine-observability-command-line-diagnostics` — full CLI reference.

**Prerequisites:**

- `marten-event-sourcing` — aggregate handler workflow and `AutoApplyTransactions()`.

**Downstream:**

- `wolverine-sagas` — stateful workflows and `ScheduleAsync` patterns.
- `integration-messaging` — cross-BC contracts and routing topology.
- `diagnostics` — the full Wolverine/Marten CLI surface.

**External:**

- ADR 011 (All-Marten Pivot), ADR 009 (shared primary store) in [`docs/decisions/`](../../decisions/).
- [`CLAUDE.md`](../../../CLAUDE.md) § Core Conventions.
