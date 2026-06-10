---
name: message-flow-diagnosis
description: "CritterBids live message-flow tracing: the publish-side vs consume-side decision tree, the PreviewSubscriptions dev probe, Wolverine debug-log signatures, envelope-table semantics under Separated fan-out, and dead-letter classes. Use when a message is produced but a consumer never sees it."
cluster: observability
tags: [diagnostics, wolverine, rabbitmq, routing, fan-out, separated]
---

# Message-Flow Diagnosis — Tracing a Delivery Through the Live Runtime

> CritterBids methodology for answering "this message was produced — why did consumer X never see it?"
> The sibling [`diagnostics`](../diagnostics/SKILL.md) skill answers *"what is configured?"* (CLI, config
> inspection, schema drift); **this skill answers "where did my message actually go at runtime?"**
> Distilled from the M8 Bug #2 investigation, where eight publish-side experiments failed before one
> instrumented run found a consume-side dispatch defect
> ([`docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md`](../../research/jasperfx-escalation-bidplaced-cross-bc-delivery.md)).

## When to apply this skill

Use this skill when:

- An integration event persists / is published, but a cross-BC read model, Relay push, or Operations
  feed never updates in the **integrated host** (isolated BC tests are green).
- Two sibling message types behave differently despite apparently identical routing.
- You are about to "try another publish mechanism" because the last one didn't work — **stop; run the
  decision tree first.**
- You need to verify what the live runtime will actually do with a message before shipping.

Do NOT use this skill for: config/schema/codegen inspection (see `diagnostics`), test-fixture tracking
assertions (`tracked.Sent` etc. — see `critter-stack-testing-patterns`), or designing routes
(see `integration-messaging`).

## Read upstream first

1. `wolverine-observability-command-line-diagnostics` — the CLI surface (`describe`,
   `wolverine-diagnostics describe-routing`).
2. `critterstack-arch-modular-monolith` — what `Separated` mode and broker fan-out are *supposed* to do.

This skill picks up at the live-runtime instruments and the interpretation traps this codebase hit.

## The decision tree — publish side or consume side?

The single most expensive mistake in the Bug #2 investigation was iterating publish mechanisms
(forwarding → explicit `PublishAsync` → `OutgoingMessages` → async-202 …) without ever establishing
**which half of the pipeline was failing**. One debug-log read answers it:

```bash
# run the API standalone against the live Aspire containers, with Wolverine debug logging
ConnectionStrings__postgres="..." ConnectionStrings__rabbitmq="..." \
Logging__LogLevel__Wolverine=Debug Logging__LogLevel__Microsoft=Warning \
dotnet run --project src/CritterBids.Api --no-launch-profile > /tmp/api-debug.log 2>&1
```

Trigger the scenario once, then grep for the message type and read the signatures **in order**:

| Log signature | Meaning |
|---|---|
| `Enqueued for sending <Type>#<id> to rabbitmq://queue/<q>` | routing resolved + envelope handed to the sender — **publish side works** |
| `Received <Type>#<id> at rabbitmq://queue/<q> from ...` | the broker round trip completed — transport works |
| `Successfully processed message <Type>#<id> from rabbitmq://queue/<q>` | *a* handler chain ran at that endpoint — **but not necessarily the one you care about** |
| `Enqueued for sending <Type>#<id'> to local://<handlertype>/` | the Separated fan-out relayed the delivery to that sticky handler's queue — this consumer WILL run |
| *(no `local://` relays after a queue receipt)* | the fan-out did not fire — a **default (non-sticky) handler consumed the delivery alone**; every sticky consumer starved (the Bug #2 signature) |

Rules of thumb:

- **"Successfully processed" is not proof of delivery to your consumer.** It logs once per
  endpoint-level execution; under `Separated`, the executed chain may be a single default handler
  (e.g. a saga) while six sticky handlers received nothing — silently, with zero dead letters.
- If `Enqueued for sending … rabbitmq://` never appears → genuinely publish-side: check routing
  (below) and whether the producing session is outbox-enrolled.
- If it appears and comes back `Received` but your consumer's `local://<handler>/` relay is absent →
  consume-side dispatch: inspect the handler graph shape (saga vs plain, sticky vs default).

## The live routing probe (`PreviewSubscriptions`)

The static routing table (`describe` / `describe-routing`) shows *subscriptions*; it told the Bug #2
investigation the routes existed and thereby **deepened** the wrong conclusion. The authoritative
question is what `RoutingFor(message.GetType()).RouteForPublish(...)` resolves at runtime — exactly
what `IMessageBus.PreviewSubscriptions(object)` executes.

CritterBids keeps a dev-only endpoint for this: [`src/CritterBids.Api/Dev/RoutingProbeEndpoint.cs`](../../../src/CritterBids.Api/Dev/RoutingProbeEndpoint.cs)
(`GET /api/dev/routing-probe`, `IsDevelopment()`-gated). It previews both the **raw contract types**
and the **`Event<T>` wrappers** that `UseFastEventForwarding` actually publishes (forwarding publishes
the `IEvent` wrapper pre-commit; Wolverine's `MartenEventRouter` unwraps it onto the raw type's
subscriptions). Extend the probe's dictionary when investigating a new type.

Why not `wolverine-diagnostics describe-routing <type> --explain`? It runs in description mode and, as
of Wolverine 6.5.1, NREs at `MessageRoute.Describe()` on description-mode routes (their members are
assigned null-tolerantly; the dereference that actually throws is the route's `Serializer`) — and it
cannot reach the `Event<T>` wrapper types at all. The live probe has neither limitation.

## Envelope-table semantics (the interpretation trap)

`wolverine.wolverine_incoming_envelopes` rows are **not** a delivery log. What the Bug #2
investigation misread for a full session:

- **Inline/buffered broker listeners leave NO incoming rows.** CritterBids' RabbitMQ listeners run
  inline — a consumed-and-processed message writes nothing. "Zero envelopes for type X" does **not**
  mean X was never delivered.
- The rows you DO see for integration events are **durable local-queue copies created by the
  Separated fan-out** (`UseDurableLocalQueues` + relay to `local://<handler>/`). Their presence proves
  the fan-out fired; their absence proves it didn't — that is the useful signal.
- `wolverine_outgoing_envelopes` is transient: healthy sends drain it in milliseconds. An empty
  outgoing table proves nothing either way.
- Dead-letter rows for saga-START types (`DocumentAlreadyExistsException`) are the expected
  at-least-once × fan-out noise: one start per consuming queue races, first wins. They indicate the
  pipeline IS flowing, not that it's broken. A *new* dead-letter class after a change is the real
  signal.

## Escalation discipline

When live behavior contradicts a documented framework contract, **stop iterating workarounds and go
to ground truth** — in this order:

1. **Framework source at the pinned version.** The JasperFx repos live at `C:\Code\JasperFx\*`; read
   the exact tag without touching the working tree:
   `git fetch https://github.com/JasperFx/wolverine.git V<version>` then `git show FETCH_HEAD:<path>`.
2. **One instrumented live run** (debug log + probe) to confirm or kill the source-derived hypothesis.
3. **Then** write the escalation/fix. A root-caused report with file/line and a minimal repro
   (the Bug #2 pattern: [`wolverine-upstream-saga-sticky-separation-handoff.md`](../../research/wolverine-upstream-saga-sticky-separation-handoff.md))
   beats any number of falsified experiments.

## Common pitfalls

- **Varying the publish mechanism to fix a consume-side problem.** Eight experiments, one outcome —
  run the decision tree first; the failing half is identifiable in one log read.
- **Trusting "Successfully processed".** Under `Separated`, it can mean "the default handler ate the
  delivery and everyone else starved."
- **Treating zero `wolverine_incoming_envelopes` rows as zero deliveries.** Inline listeners don't
  write rows; only the fan-out's durable local copies do.
- **Trusting the static routing table over the runtime.** `describe` shows subscriptions, not what
  `RouteForPublish` resolves for the concrete published type (wrappers included).
- **Diagnosing through the test fixtures.** `DisableAllExternalWolverineTransports()` replaces the
  exact transport behavior under investigation; integrated-host bugs need the integrated host.

## See also

**Upstream (ai-skills)** — generic mechanics this skill defers to. License required; install via `npx skills add`:

- `wolverine-observability-command-line-diagnostics`, `critterstack-arch-modular-monolith`.

**Prerequisites:**

- [`diagnostics`](../diagnostics/SKILL.md) — the static half: CLI invocation, config/schema inspection.
- [`integration-messaging`](../integration-messaging/SKILL.md) — the per-BC queue topology being traced.

**Downstream:**

- [`wolverine-sagas`](../wolverine-sagas/SKILL.md) § "Separated-mode rule" — the dispatch defect this
  methodology uncovered, and the dispatcher-bridge that fixes it.

**External:**

- [`docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md`](../../research/jasperfx-escalation-bidplaced-cross-bc-delivery.md) — the worked example, end to end.
- [`docs/research/dcb-marten-blog-series-research.md`](../../research/dcb-marten-blog-series-research.md) §5 — the full experiment trail this skill exists to make unnecessary.
