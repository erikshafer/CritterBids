# Agent Handoff — Wolverine upstream fix: single-saga chains must not suppress sticky-handler fan-out under `MultipleHandlerBehavior.Separated`

**Audience:** an autonomous coding agent working in `C:\Code\JasperFx\wolverine` (Erik's fork;
`origin` = `erikshafer/wolverine`, `upstream` = `JasperFx/wolverine`). This document is
self-contained — no CritterBids context is required beyond what is quoted here.
**Deliverable:** a focused PR against `JasperFx/wolverine` `main` (branch on the fork, PR via
`gh pr create --repo JasperFx/wolverine`), ready for Jeremy Miller's review. Include the fix, a
failing-first regression test, and a PR description that quotes the minimal repro below.
**Authored:** 2026-06-09, from the CritterBids Bug #2 investigation
(`CritterBids/docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md` has the full
evidence trail; do not copy it into the PR — the generic repro below is sufficient).

---

## 1. The bug, in one paragraph

In a `MultipleHandlerBehavior.Separated` application, when a message type is handled by **exactly
one saga type** (via instance `Handle`/`[SagaIdentityFrom]` methods — a saga *continue* handler)
**plus one or more plain handlers**, deliveries arriving from an **external transport endpoint**
(RabbitMQ, etc.) execute **only the saga**. The plain handlers — which `Separated` moved onto
their own sticky local queues — never receive the message. There is no error, no dead letter, no
log warning: the envelope reports "Successfully processed." This silently violates the documented
Separated-mode contract (Wolverine modular-monolith tutorial): *"When a message arrives from an
external broker, Wolverine auto-fans it out to all matching handler-specific queues — no special
routing needed."* With **two or more** saga types, or with **zero** saga types, fan-out works
correctly — only the single-saga-plus-others shape is broken.

Verified on Wolverine **6.5.1** (tag `V6.5.1`, commit `d0b5d4038`). Confirm the relevant code is
unchanged on current `upstream/main` before patching (it was as of 2026-06-09; the files below had
no post-6.5.1 commits).

## 2. Root cause (read these in order)

### 2a. `SagaChain.maybeAssignStickyHandlers` — `src/Wolverine/Persistence/Sagas/SagaChain.cs`

```csharp
protected override void maybeAssignStickyHandlers(WolverineOptions options, IGrouping<Type, HandlerCall> grouping)
{
    var notSaga = grouping.Where(x => !x.HandlerType.CanBeCastTo<Saga>());
    foreach (var handlerCall in notSaga)
    {
        tryAssignStickyEndpoints(handlerCall, options);   // under Separated: sticky local queue + REMOVED from Handlers
    }

    var groupedSagas = grouping.Where(x => x.HandlerType.CanBeCastTo<Saga>())
        .GroupBy(x => x.HandlerType).ToArray();

    if (groupedSagas.Length > 1)          // ← THE DEFECT: single saga type falls through,
    {                                     //   its calls stay in the default Handlers collection
        // (throws if not Separated; under Separated, builds one sticky
        //  SagaChain per saga type on a local queue named after the saga type,
        //  removes the calls from Handlers, adds to _byEndpoint)
    }
}
```

Note the contrast with the base `HandlerChain.maybeAssignStickyHandlers`, which calls
`tryAssignStickyEndpoints` for **every** call — a non-saga multi-handler chain ends up all-sticky
with an empty default `Handlers`. Only `SagaChain` leaves a single saga behind as a default.

(Also note: `maybeAssignStickyHandlers` is only invoked from the `HandlerChain` ctor when
`grouping.Count() > 1` — single-handler chains are untouched by design, and must remain so.)

### 2b. `HandlerGraph.HandlerFor(Type, Endpoint)` — `src/Wolverine/Runtime/Handlers/HandlerGraph.cs`

For a delivery at an external endpoint with no endpoint-specific sticky chain:

```csharp
var sticky = chain.ByEndpoint.FirstOrDefault(x => x.Endpoints.Contains(endpoint));
if (sticky == null)
{
    if (!chain.HasDefaultNonStickyHandlers())     // SagaChain override: Handlers.Any()
    {
        // all sticky handlers target local queues → build FanoutMessageHandler
        // relaying the message from the external endpoint to each local queue
        ...
    }
    return HandlerFor(messageType);               // ← executes ONLY the default chain (the saga)
}
```

The `FanoutMessageHandler` relay — the only mechanism by which sticky-local handlers receive
externally-delivered messages — is gated on the chain having **no** default handlers. The lone
saga keeps `HasDefaultNonStickyHandlers()` true, so the gate never opens, and the
`return HandlerFor(messageType)` executes the saga alone.

### 2c. Why nobody noticed

- Chains with no saga: all-sticky → fan-out works (the documented path).
- Chains with ≥2 saga types: the `Length > 1` branch separates them → all-sticky → fan-out works
  (covered by `src/Persistence/MartenTests/Saga/multiple_sagas_for_same_message.cs` and its
  Polecat twin).
- Single-handler chains: never grouped, default execution at the receiving endpoint is correct.
- The broken shape executes the saga successfully, so nothing dead-letters and the logs say
  "Successfully processed" — starvation of the other handlers is invisible.

## 3. Minimal reproduction (turn this into the regression test)

One host, `MultipleHandlerBehavior.Separated`, durable or buffered local queues — transport can be
the in-box test transports; the defect needs a delivery at a **non-local endpoint** with no sticky
match (in MartenTests, a RabbitMQ-free approximation: see "test strategy" below).

```csharp
public record OrderPlaced(Guid Id, string ProductName);

public class ShippingSaga : Saga          // ONE saga type, CONTINUE handler
{
    public Guid Id { get; set; }
    public int Updates { get; set; }
    public static ShippingSaga Start(StartShipping cmd) => new() { Id = cmd.Id };
    public void Handle([SagaIdentityFrom(nameof(OrderPlaced.Id))] OrderPlaced msg) => Updates++;
}

public static class AuditHandler          // plain handler, same message type
{
    public static int Received;           // (use a proper recorder in the real test)
    public static void Handle(OrderPlaced msg) => Received++;
}
```

Deliver `OrderPlaced` through an external (non-local) endpoint. **Expected:** saga advances AND
`AuditHandler` runs (fan-out semantics). **Actual on 6.5.1:** only the saga runs; the sticky
`local://audithandler/` queue never receives anything. Replace the saga continue-handler with a
second saga type, or with a static start-only class, and `AuditHandler` starts receiving.

## 4. The fix (primary shape — implement this one)

In `SagaChain.maybeAssignStickyHandlers`: under `MultipleHandlerBehavior.Separated`, apply the
existing per-saga separation to **every** saga grouping, not just `Length > 1` — i.e. a single
saga type also gets its own sticky `SagaChain` on a local queue named after the saga type, its
calls removed from the default `Handlers`. Sketch:

```csharp
if (groupedSagas.Length > 1 && options.MultipleHandlerBehavior != MultipleHandlerBehavior.Separated)
{
    throw new InvalidSagaException(...);   // existing multi-saga-without-Separated error, unchanged
}

if (options.MultipleHandlerBehavior == MultipleHandlerBehavior.Separated)
{
    // existing per-saga separation block, now for Length >= 1
    foreach (var sagaGroup in groupedSagas) { ... }
}
```

Constraints to honor:
- **Scope to `Separated` only.** Classic mode must be byte-for-byte unchanged.
- **Only when the grouping had other handlers** is strictly required to fix the bug, but
  separating the saga whenever `maybeAssignStickyHandlers` runs (`grouping.Count() > 1`) is the
  simpler, more consistent rule — the multi-saga branch already behaves that way. Use the simpler
  rule unless a test breaks.
- Single-handler saga chains (`grouping.Count() == 1`, e.g. a scheduled `CloseAuction`-style
  command handled only by its saga) **must keep current behavior** — the ctor gate already
  guarantees this; add a test asserting it anyway.
- The chain left behind keeps working as the type's chain wrapper exactly as in the multi-saga
  case (calls removed from `Handlers`, sticky `SagaChain` added to `_byEndpoint`).

### Alternative shape (mention in the PR description, do NOT implement unless Jeremy asks)

Generalize `HandlerGraph.HandlerFor(Type, Endpoint)`: when a chain has both sticky-local handlers
AND default handlers and the delivery is at an external endpoint with no sticky match, execute the
default chain AND relay to the sticky local queues. Broader (covers any future mixed
default+sticky chain) but changes dispatch semantics beyond sagas; the primary shape is the
narrower, lower-risk fix that reuses an already-shipped code path.

### Behavioral changes to call out in the PR description (for Jeremy's judgment)

1. The saga now receives externally-delivered messages via the fan-out relay (its own sticky local
   queue) instead of executing inline at the receiving endpoint — same at-least-once semantics,
   one extra local hop.
2. `IMessageBus.InvokeAsync()` of a message type whose only-saga handler becomes sticky may now
   behave like the existing multi-saga/sticky cases (sticky handlers are not invokable at the
   default endpoint). This is already the documented trade of `Separated` + sticky handlers;
   confirm with a test and note the behavior either way.

## 5. Test strategy

- **Primary regression test:** extend `src/Persistence/MartenTests/Saga/multiple_sagas_for_same_message.cs`
  (or add a sibling `single_saga_with_other_handlers_separated.cs` next to it — prefer the sibling;
  the existing file's host setup is the template: `DisableConventionalDiscovery` + `IncludeType`,
  `DurabilityMode.Solo`, Marten + `IntegrateWithWolverine`, `AutoApplyTransactions`,
  `MultipleHandlerBehavior.Separated`). Assert BOTH the saga document advanced AND the plain
  handler executed (use `host.SendMessageAndWaitAsync` + tracked session
  `Executed.MessagesOf<T>()` or a recording service — mirror the idioms already in that folder).
  To exercise the external-endpoint dispatch path without a broker, send via a non-local endpoint
  from the test transports (see how `Bug_2004_separated_handler_stuff.cs` and
  `src/Testing/CoreTests/Acceptance/sticky_message_handlers.cs` drive endpoint-specific delivery);
  if that proves awkward in MartenTests, the RabbitMQ test project
  (`src/Transports/RabbitMQ/Wolverine.RabbitMQ.Tests`) has docker-backed fixtures.
- **Guard tests:** (a) multi-saga case still passes (existing file); (b) single-handler saga chain
  unchanged; (c) Classic mode unchanged for the single-saga-plus-handlers grouping.
- **Polecat twin:** `src/Persistence/PolecatTests/Sagas/multiple_sagas_for_same_message.cs`
  mirrors the Marten file — add the same sibling there if the Marten one lands cleanly (Polecat
  tests need SQL Server; skip if the environment lacks it and say so in the PR).
- MartenTests need PostgreSQL: `docker compose up -d` at the repo root brings up the standard
  test dependencies (`src/Testing/IntegrationTests/Servers.cs` shows the expected connection
  strings).

## 6. Repo practicalities

- Branch from up-to-date `upstream/main` (`git fetch upstream && git checkout -b
  fix/separated-single-saga-sticky-fanout upstream/main`), push to the fork, PR to
  `JasperFx/wolverine:main`.
- Build: `dotnet build wolverine.slnx` (or the solution file at root — check; net8/net9/net10
  multi-target). Run only the test projects you touched plus CoreTests; the full suite needs many
  brokers.
- Commit style: recent history mixes conventional and plain ("Fixing a regression problem with…").
  Use a clear imperative subject mentioning Separated + sagas; reference the GitHub issue if one
  is filed first (optional — a PR with a complete description is acceptable in this repo).
- PR description must include: the one-paragraph bug statement (§1), the minimal repro (§3), the
  root-cause pointer (§2a/§2b), the documented-contract quote, the behavioral-changes notes (§4),
  and "found while building CritterBids, a Critter Stack reference modular monolith; full
  field evidence available on request."
- **Out of scope for this PR** (file as a separate small issue if time permits):
  `wolverine-diagnostics describe-routing <type> --explain` throws `NullReferenceException` at
  `MessageRoute.Describe()` (`src/Wolverine/Runtime/Routing/MessageRoute.cs`, ~line 204) when a
  route's `Sender` is null — hit in an app with RabbitMQ senders while diagnosing this very bug.

## 7. Acceptance checklist

- [ ] New regression test fails on unpatched `main`, passes with the fix.
- [ ] Existing multi-saga tests (Marten + Polecat) pass unmodified.
- [ ] Classic-mode and single-handler-saga behavior covered by guard tests.
- [ ] No changes outside `src/Wolverine` (+ tests) — the fix is `SagaChain` scoped.
- [ ] PR description written per §6; behavioral changes called out explicitly.
