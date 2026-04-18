# JasperFx AI Skills — CritterBids Gap Analysis

**Last reviewed:** 2026-04-18 (Wave 6 consolidation)
**Source:** `C:\Code\JasperFx\ai-skills` (public at the time; per Erik, going paid-only)
**Scope:** Cross-reference of JasperFx ai-skills patterns against CritterBids' skill library after the M2.5/M3 preparation, ADR 011 (all-Marten pivot), and Waves 1–5 of the multi-wave skill refresh.

---

## Purpose

This document is the rolling audit of CritterBids' skill library against the JasperFx ai-skills canonical reference. It answers three questions:

1. **Where has CritterBids absorbed ai-skills content?** — so future refreshes don't re-do work.
2. **Where does CritterBids diverge from ai-skills?** — so divergences are intentional, not accidental.
3. **What ai-skills territory remains uncovered in CritterBids?** — so the next skill additions are prioritized.

It is not a to-do list. Gaps are flagged with "relevant when" context; most are deferred to future milestones because CritterBids doesn't need them yet.

Updates happen after each wave. History at the bottom of this file; a summary of every wave's commits lives in `docs/skills/README.md`.

---

## Quick summary

| Aspect | Status |
|---|---|
| Core handler patterns (`[WriteAggregate]`, `[Entity]`, Validate/Before, railway programming) | ✅ Complete — extended in Waves 2–4 |
| Event sourcing + Marten projections | ✅ Complete — comprehensive after Wave 4 |
| Multi-BC / modular-monolith patterns | ✅ Complete — Wave 3 closed the integration-messaging gaps |
| Testing | ✅ Complete — Wave 5 restructured and filled gaps |
| Dynamic Consistency Boundary (DCB) | ✅ Complete — CritterBids' skill is more comprehensive than ai-skills' on several fronts |
| Ancillary stores | 📚 Reference only — Wave 1 set up the reference doc; not currently used |
| Polecat patterns | 📚 Reference only — ADR 011 pivot; skills preserved for sibling projects |
| SignalR real-time | ✅ Complete — Wave 3 refresh; one open question logged with JasperFx |
| Observability (OpenTelemetry, Prometheus/Grafana, `[Audit]` tags) | 🔴 Uncovered — deferred, relevant pre-production |
| Event subscriptions (Marten → Wolverine relay) | 🟡 Partial — mentioned in ancillary-stores and polecat-event-sourcing; no dedicated skill yet |
| Vertical slice architecture / A-Frame naming | 🟡 Implicit — patterns are in use; terminology not adopted |
| Transport integrations (Azure Service Bus, Kafka, NATS, Pulsar, MQTT) | 🔴 Uncovered — deferred, relevant if transport is ever swapped |
| Framework conversion paths (from MediatR, MassTransit, NServiceBus, EventStoreDB) | N/A — CritterBids is greenfield |

---

## Section 1 — What CritterBids has absorbed from ai-skills

This section maps each ai-skills file to how it's reflected in CritterBids' skill library. Files ai-skills has that CritterBids doesn't yet cover are in Section 3.

### Architecture

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `architecture/modular-monolith.md` | `integration-messaging.md`, `adding-bc-module.md`, `wolverine-message-handlers.md` | ✅ Covered |
| `architecture/new-project-wolverine-marten.md` | `marten-event-sourcing.md` (BC Module Pattern, Host-Level Wolverine Settings) | ✅ Covered |
| `architecture/new-project-wolverine-polecat.md` | `polecat-event-sourcing.md` (archived reference) | 📚 Reference |
| `architecture/new-project-wolverine-efcore.md` | — | 🔴 N/A — CritterBids uses Marten as the outbox store |
| `architecture/new-project-wolverine-cosmosdb.md` | — | 🔴 N/A — PostgreSQL-only |
| `architecture/vertical-slice-fundamentals.md` | Implicit — CritterBids' `src/CritterBids.{BC}/` folder organization and `sealed record` command pattern follow this. No file explicitly names the pattern "vertical slices." | 🟡 Implicit |

### Wolverine handlers

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `wolverine/handlers/building-handlers.md` | `wolverine-message-handlers.md` | ✅ Covered |
| `wolverine/handlers/pure-functions.md` | `wolverine-message-handlers.md` §1 (Decide pattern), `csharp-coding-standards.md` | ✅ Covered (without the "A-Frame Architecture" name) |
| `wolverine/handlers/a-frame-architecture.md` | Decider pattern in `marten-event-sourcing.md`; Load/Validate/Handle implicit throughout `wolverine-message-handlers.md` | 🟡 Implicit — pattern is used but not named |
| `wolverine/handlers/declarative-persistence.md` | `wolverine-message-handlers.md` (`[Entity]`, `IStorageAction<T>`), `marten-event-sourcing.md` (`MartenOps.StartStream`) | ✅ Covered |
| `wolverine/handlers/middleware.md` | `wolverine-message-handlers.md` (Compound Handler Lifecycle, OnException, Configure(HandlerChain)) | ✅ Covered — extended in Wave 2 |
| `wolverine/handlers/railway-programming.md` | `wolverine-message-handlers.md` (Railway Programming section, inline IEnumerable<string> Validate) | ✅ Covered — extended in Wave 2 |
| `wolverine/handlers/efcore-handlers.md` | — | 🔴 N/A — CritterBids uses Marten |
| `wolverine/handlers/ioc-and-service-optimization.md` | `wolverine-message-handlers.md` §9 (IoC and Service Optimization, ServiceLocationPolicy, AlwaysUseServiceLocationFor, ILogger rule) | ✅ Covered — Wave 2 |

### Wolverine HTTP

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `wolverine/http/wolverine-http-fundamentals.md` | `wolverine-message-handlers.md` (HTTP Endpoints section) | ✅ Covered |
| `wolverine/http/http-marten-integration.md` | `marten-event-sourcing.md` (Wolverine Integration Patterns, Marten.AspNetCore streaming) | ✅ Covered — extended in Wave 4 |
| `wolverine/http/hybrid-handlers.md` | `wolverine-message-handlers.md` (MiddlewareScoping for hybrid HTTP+messaging) | ✅ Covered — Wave 2 |

### Wolverine messaging

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `wolverine/messaging/message-routing.md` | `integration-messaging.md` (Publishing Integration Messages, Scheduling, DeliverWithin) | ✅ Covered — Wave 3 |
| `wolverine/messaging/resiliency-policies.md` | `integration-messaging.md` (Resiliency Policies section — 150 lines) | ✅ Covered — Wave 3 |

### Wolverine testing

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `wolverine/testing/integration-testing.md` | `critter-stack-testing-patterns.md` Parts II + III | ✅ Covered — Wave 5 |
| `wolverine/testing/integration-testing-wolverine-marten.md` | `critter-stack-testing-patterns.md` Parts II + III | ✅ Covered — Wave 5 |
| `wolverine/testing/test-parallelization.md` | `critter-stack-testing-patterns.md` Part III (Test Parallelization Strategy) | ✅ Covered — Wave 5 |
| `wolverine/testing/testing-with-testcontainers.md` | `critter-stack-testing-patterns.md` Part III (Advanced Testcontainers Patterns) | ✅ Covered — Wave 5 |
| `wolverine/testing/alba.md` | Implicit — Alba patterns throughout `critter-stack-testing-patterns.md` Part II | ✅ Covered |
| `wolverine/testing/testing-with-nunit.md` | — | 🔴 N/A — CritterBids is xUnit-only |
| `wolverine/testing/testing-with-mstest.md` | — | 🔴 N/A |
| `wolverine/testing/testing-with-tunit.md` | — | 🔴 N/A |

### Marten

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `marten/aggregate-handler-workflow.md` | `marten-event-sourcing.md` (Wolverine Integration Patterns) | ✅ Covered |
| `marten/event-subscriptions.md` | `critter-stack-ancillary-stores.md` (brief), `polecat-event-sourcing.md` (API list) | 🟡 Partial — see Section 3.1 |
| `marten/integration-testing-with-marten.md` | `critter-stack-testing-patterns.md` Part II | ✅ Covered — Wave 5 |
| `marten/advanced/ancillary-stores.md` | `critter-stack-ancillary-stores.md` | ✅ Covered — Wave 1, reference only per ADR 009 |
| `marten/advanced/async-daemon-deep-dive.md` | `marten-event-sourcing.md` (Async Daemon Configuration section) | ✅ Covered — Wave 4 |
| `marten/advanced/cross-stream-operations.md` | `marten-event-sourcing.md` (Cross-Stream Aggregate Handlers section) | ✅ Covered — Wave 4 |
| `marten/advanced/dynamic-consistency-boundary.md` | `dynamic-consistency-boundary.md` | ✅ Covered — CritterBids version is more comprehensive; see Section 2.1 |
| `marten/advanced/optimization.md` | `marten-event-sourcing.md` (EventAppendMode.Quick, UseIdentityMapForAggregates, Marten.AspNetCore) | ✅ Covered — Wave 4 |
| `marten/advanced/indexes-and-query-optimization.md` | `marten-querying.md` (computed indexes, duplicated fields, compiled queries) | ✅ Covered |
| `marten/advanced/load-distribution.md` | `marten-event-sourcing.md` (Async Daemon Configuration → Wolverine-managed distribution) | ✅ Covered — Wave 4 |
| `marten/advanced/multi-tenancy.md` | `marten-projections.md` (RollUpByTenant with note that CritterBids is single-tenant) | 🟡 Partial — multi-tenant patterns documented as "not currently used" |
| `marten/projections/single-stream-projections.md` | `marten-event-sourcing.md` §5 (Projections) | ✅ Covered — Wave 4 |
| `marten/projections/multi-stream-projections.md` | `marten-projections.md` (Multi-Stream Projection Routing Patterns) | ✅ Covered — Wave 4 |
| `marten/projections/composite-projections.md` | `marten-projections.md` (Composite Projections with `Updated<T>` synthetic events) | ✅ Covered — Wave 4 |
| `marten/projections/event-enrichment.md` | `marten-projections.md` (Event Enrichment with `EnrichEventsAsync`) | ✅ Covered — Wave 4 |
| `marten/projections/flat-table-projections.md` | `marten-projections.md` (Flat Table Projections) | ✅ Covered — Wave 4 |
| `marten/projections/raise-side-effects.md` | `projection-side-effects-for-broadcast-live-views.md` | ✅ Covered — Wave 1 extended |

### Polecat

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `polecat/polecat-setup-and-decision-guide.md` | `polecat-event-sourcing.md` (archived reference) | 📚 Reference — ADR 011 |

### Transports

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `integrations/wolverine-rabbitmq.md` | `integration-messaging.md` (RabbitMQ Transport Configuration with CritterBids posture) | ✅ Covered |
| `integrations/wolverine-signalr.md` | `wolverine-signalr.md` | ✅ Covered |
| `integrations/wolverine-azure-service-bus.md` | — | 🔴 Uncovered — relevant if a future conference talk demos transport swap |
| `integrations/wolverine-kafka.md` | — | 🔴 Uncovered — not planned |
| `integrations/wolverine-aws-sqs-sns.md` | — | 🔴 Uncovered — not planned |
| `integrations/wolverine-nats.md` | — | 🔴 Uncovered — not planned |
| `integrations/wolverine-redis.md` | — | 🔴 Uncovered — not planned |
| `integrations/wolverine-pulsar.md` | — | 🔴 Uncovered — not planned |
| `integrations/wolverine-mqtt.md` | — | 🔴 Uncovered — not planned |

### Observability

| ai-skills file | CritterBids coverage | Status |
|---|---|---|
| `wolverine/observability/opentelemetry-setup.md` | — | 🔴 Uncovered — deferred to pre-production milestone |
| `wolverine/observability/command-line-diagnostics.md` | `wolverine-message-handlers.md` + `critter-stack-testing-patterns.md` (codegen-preview, describe-routing, describe-resiliency) | ✅ Partial coverage — diagnostic commands in context; no dedicated skill |
| `wolverine/observability/code-generation.md` | `wolverine-message-handlers.md` (IoC codegen section) | 🟡 Partial — dynamic vs static modes and `codegen write` for production covered briefly |
| `wolverine/observability/metrics-and-auditing.md` | — | 🔴 Uncovered |
| `wolverine/observability/grafana-dashboard-templates.md` | — | 🔴 Uncovered |

### Converting from other frameworks

CritterBids is greenfield — no conversion skills are relevant. All `wolverine/converting-from/*` files are N/A.

---

## Section 2 — Where CritterBids diverges from ai-skills

Places where CritterBids' skill content is broader, narrower, or differently framed than the ai-skills equivalent. Each divergence is intentional; the reasoning is preserved.

### 2.1. `dynamic-consistency-boundary.md` — CritterBids' version is superior

CritterBids' DCB skill (29.4 KB) is more comprehensive than ai-skills' (7.1 KB) on several fronts the ai-skills version doesn't cover:

- **Two patterns, one preferred** — canonical `[BoundaryModel]` vs manual `FetchForWritingByTags` distinction with guidance on when each fits
- **`EventTagQuery` fluent vs imperative construction styles** — with gotchas (`.AndEventsOfType` is required, not optional)
- **Tagging gotchas** — `StartStream` drops tags; `AddTag` vs `WithTag` equivalence; per-store `DcbConcurrencyException` namespace differences; tag-table opt-in for non-DCB write paths
- **Implementation checklist** — six-step adoption path including strong-typed tag ID records (avoiding the .NET 10 `Variant`/`Version` issue), concurrency retry policy registration for both exception types, and the boundary state class rules
- **`Before()` / `Validate()` gotcha** — `[BoundaryModel]` on pipeline hook state parameters causes Wolverine codegen error CS0128 (documented nowhere else)
- **Boundary model `Guid Id` property under test teardown** — CritterSupply finding about `InvalidDocumentException` during cleanup
- **CritterBids-specific `BidRejected` stream placement decision** — the dedicated-per-listing-stream vs global-audit-stream decision for Auctions BC

**ai-skills has one thing CritterBids doesn't:** the full SubscriptionState/University example. This is retained implicitly because the CritterBids skill points readers at `boundary_model_workflow_tests.cs` in the Wolverine repo for the canonical sample.

**JasperFx open question (#2):** ai-skills' DCB doc claims "DCB is currently implemented in Polecat. The `[BoundaryModel]` attribute and `EventTagQuery` are Polecat-specific." Source-verified to be false — both Marten and Polecat have full DCB implementations. See `docs/jasperfx-open-questions.md`. CritterBids' skill documents DCB as store-agnostic, which matches the source tree.

### 2.2. `critter-stack-ancillary-stores.md` — Reference doc, not a recipe

The CritterBids ancillary-stores skill is 📚 Reference status per ADR 009 (shared primary store, no ancillary stores in use). It reflects the ai-skills capability matrix but frames it as *"what CritterBids would need if ancillary stores were introduced"* rather than *"how to use ancillary stores today."*

**JasperFx open question (#1):** the ai-skills file documents `[MartenStore]` handler-routing but does not address whether Polecat has an equivalent handler-routing attribute. Source verified: `[PolecatStore]` does not exist as of the last review. The ancillary-stores skill flags this as an asymmetry.

### 2.3. `integration-messaging.md` — CritterBids-posture tables

ai-skills' `wolverine-rabbitmq.md` and `messaging/message-routing.md` cover capability; CritterBids' `integration-messaging.md` adds posture tables that map capabilities to specific BCs. Examples:

- Per-queue durability posture for each CritterBids BC queue pattern
- Circuit breakers scoped to Settlement and Relay (external-facing BCs), not applied globally
- "Domain events never expire; operational signals may" as the `DeliverWithin` rule

This is deliberate — the skill's job is to tell a reader which pattern applies to their current BC work, not just describe what's possible.

### 2.4. `wolverine-signalr.md` — FQN vs kebab-case, marker interfaces

CritterBids resolved an ambiguity in ai-skills' SignalR doc about CloudEvents `type` field format. Source verification (`WolverineMessageNaming.cs`) showed that marker-interface-routed messages produce FQN `type` fields, while `WebSocketMessage`-based messages produce kebab-case. CritterBids uses marker interfaces, so the JavaScript client does `cloudEvent.type.split(".").pop()` to recover the short name. Documented with a table in Wave 3.

CritterBids also added `Request/Reply to Calling WebSocket`, `JSON Serialization Override`, and `Client-Driven Group Changes` subsections — all ai-skills-inspired but with the CritterBids posture ("doesn't use this today; when it would be wanted").

### 2.5. `marten-projections.md` — Combined native + EF Core

ai-skills has five separate projection files (`single-stream-projections`, `multi-stream-projections`, `composite-projections`, `event-enrichment`, `flat-table-projections`) plus the separately maintained EF Core patterns. CritterBids combines native and EF Core coverage in one file with a clear H1 division (Part 1 — Native Marten Projections, Part 2 — EF Core Projections from Marten Events).

Rationale documented in the file's scope note: same mechanism, different storage backend; splitting once growth or maintenance churn demands it, not preemptively.

### 2.6. `critter-stack-testing-patterns.md` — CritterBids-only content absent from ai-skills

Five patterns that CritterBids documents but ai-skills doesn't:

- **Cross-BC handler isolation via `IWolverineExtension` exclusion** — Problem 1 (handlers discovered, infrastructure absent) and Problem 2 (stub-local-queue for `NoRoutesFor`-dropped messages). Not in ai-skills.
- **`ConfigureAppConfiguration` doesn't propagate to Program.cs inline guards** — CritterBids-specific finding; significant for anyone with null-guarded primary-store registration patterns.
- **`AutoApplyTransactions` doesn't fire on direct handler calls** — documented in CritterBids, not in ai-skills.
- **`ProblemDetails` in non-HTTP handlers stops pipeline without throwing** — Wolverine-specific behavior; ai-skills covers `ProblemDetails` in HTTP contexts but not this message-handler behavior.
- **`TestAuthHandler` with stable IDs and `NoResult()` for missing Authorization header** — CritterBids' idiomatic auth-stub pattern; ai-skills covers Alba auth stubs generally but not this shape.

These patterns will stay CritterBids-specific unless JasperFx's ai-skills absorb them.

### 2.7. `wolverine-message-handlers.md` — The outbox routing rule (AP#14)

CritterBids' Anti-Pattern #14 documents that `tracked.Sent.MessagesOf<T>()` returns 0 when no routing rule exists for the message type, because `PublishAsync` calls `NoRoutesFor()` and returns before any `ISendingAgent` is invoked. The `opts.Publish(...)` rule is required. This is documented nowhere in ai-skills; it was a CritterBids finding during M2.

### 2.8. `wolverine-message-handlers.md` — Anti-patterns #15 and #16

Wave 2 added two CritterBids-specific anti-patterns that ai-skills doesn't cover:

- **#15 (lambda factory registrations)** — opaque to Wolverine codegen, forces `IServiceScopeFactory` at runtime
- **#16 (`bus.InvokeAsync` for fire-and-forget)** — blocks the calling thread for the full handler duration

---

## Section 3 — Uncovered territory

ai-skills material with no CritterBids equivalent. Each entry notes when it would become relevant.

### 3.1. Event subscriptions — Marten → Wolverine relay (🟡 Partial)

**ai-skills:** `marten/event-subscriptions.md` covers the full story:
- `.EventForwardingToWolverine()` — immediate, no ordering guarantee, no daemon required
- `.PublishEventsToWolverine()` — publishes as messages, loose order, daemon required
- `.ProcessEventsWithWolverineHandlersInStrictOrder()` — strict sequential processing through Wolverine handlers
- Custom `BatchSubscription` — direct `IMessageBus` access for batch processing
- Raw Marten `ISubscription` — when Wolverine messaging isn't needed
- Subscription filtering, starting position, error handling

**CritterBids:** mentioned briefly in `critter-stack-ancillary-stores.md` and the archived `polecat-event-sourcing.md`. No dedicated skill file.

**Relevant when:** CritterBids reaches a use case where events must trigger Wolverine handlers outside the same transaction as the event append. Candidate moments:

- Operations BC dashboards consuming Auctions BC events via a strict-order subscription for deterministic replay
- Relay BC forwarding domain events to external webhook subscribers
- Audit/compliance trails that must process every event exactly once in sequence

**Why deferred:** CritterBids through M3 uses the synchronous `OutgoingMessages` integration pattern for cross-BC communication. Event subscriptions add a daemon-managed async layer that isn't needed yet. Adding the skill ahead of first use would be writing against hypothetical requirements.

### 3.2. Observability — OpenTelemetry, Prometheus/Grafana, `[Audit]` tags (🔴 Uncovered)

**ai-skills:** five skills (`opentelemetry-setup`, `metrics-and-auditing`, `grafana-dashboard-templates`, `code-generation`, `command-line-diagnostics`).

**CritterBids:** none. `aspire.md` mentions "Traces (OpenTelemetry)" in passing. CLI diagnostic commands (`codegen-preview`, `describe-routing`, `describe-resiliency`) are documented in context where they're useful (`wolverine-message-handlers.md`, `critter-stack-testing-patterns.md`) but there's no dedicated observability skill.

**Relevant when:** pre-production deployment of CritterBids. The Hetzner VPS target needs at least traces + metrics wired for the live conference demo dashboard. Grafana dashboards become valuable when operational questions start getting asked ("why is the auction-close saga slow on this listing?").

**Why deferred:** M2.5 / M3 are feature-development milestones. Observability tuning without production load is speculative. The observability skill should be authored after the first deployment when concrete telemetry needs are known.

### 3.3. Additional transport integrations (🔴 Uncovered)

**ai-skills:** Azure Service Bus, Kafka, AWS SQS/SNS, NATS, Redis, Pulsar, MQTT.

**CritterBids:** RabbitMQ only, via `integration-messaging.md`.

**Relevant when:** the "Swapping the Bus" live demo proposal lands — the blog-post idea is a live swap from RabbitMQ to Azure Service Bus using CritterBids as the vehicle. At that point the Azure Service Bus skill would need to exist, at minimum reframed from ai-skills' version.

**Why deferred:** conference/blog-post work, not MVP work. When the proposal is accepted and the talk is scheduled, authoring the minimum-viable skill for the target transport is the right preparation step.

### 3.4. Vertical slice architecture / A-Frame naming (🟡 Implicit)

**ai-skills:** `architecture/vertical-slice-fundamentals.md` and `wolverine/handlers/a-frame-architecture.md` name the pattern explicitly. Vertical slice = one file contains command + events + endpoint + handler for a feature. A-Frame = the compound-handler Load/Validate/Handle separation that keeps business logic pure.

**CritterBids:** uses the patterns but doesn't name them. `src/CritterBids.{BC}/` folders are organized vertically by feature. Compound handlers (`Before`/`Handle`) are the default in `wolverine-message-handlers.md`. The Decider pattern section in `marten-event-sourcing.md` is the A-Frame pattern in disguise.

**Relevant when:** the skill library becomes a reference for newcomers to the Critter Stack, not just to CritterBids. The current skills teach the patterns correctly without teaching the names. A newcomer reading both ai-skills and CritterBids skills will figure it out, but explicit name-tagging would reduce the friction.

**Why deferred:** a stylistic refinement, not a correctness gap. Can be folded into a future refresh that touches both files.

### 3.5. Multi-tenancy deep dive (🟡 Partial)

**ai-skills:** `marten/advanced/multi-tenancy.md` covers conjoined, database-per-tenant, dynamic provisioning, and Wolverine's per-tenant shard distribution.

**CritterBids:** single-tenant by design. `RollUpByTenant` mentioned in `marten-projections.md` as "not currently used." No coverage of the fuller multi-tenancy story.

**Relevant when:** a CritterBids variant is introduced that's genuinely multi-tenant (e.g., a SaaS-style auction platform hosting multiple sellers' auctions). No concrete plan.

**Why deferred:** speculative. The abstraction cost of writing multi-tenant patterns into every skill without a use case outweighs the benefit.

### 3.6. Spec-driven development / Event Modeling integration (🔴 Forward-looking)

**ai-skills:** mentioned as a Phase 3 item (`Spec-Driven Development / Event Modeling (Gherkin/markdown → feature scaffold)`). Not yet implemented in ai-skills.

**CritterBids:** Event Modeling workshop skill exists at `event-modeling/SKILL.md` (shared). Gherkin/markdown-to-scaffold tooling doesn't exist.

**Relevant when:** JasperFx ships CritterSpecs (Jeremy's planned tool for Gherkin-based spec-driven dev). At that point, CritterBids becomes a testbed and the skill would need to be authored from first use.

**Why deferred:** the tooling doesn't exist. Can't write the skill.

---

## Section 4 — Wave history

Five waves of skill refresh completed between 2026-04-17 and 2026-04-18. Each wave's detailed commit notes live in `docs/skills/README.md`. Summary:

| Wave | Focus | Primary changes |
|---|---|---|
| 1 (2026-04-17) | Ancillary stores + side-effects | New `critter-stack-ancillary-stores.md` (📚 Reference); extended `projection-side-effects-for-broadcast-live-views.md`; deleted archived `marten-named-stores.md` |
| 2 (2026-04-17) | Handler surface expansion | `wolverine-message-handlers.md` extended 42→65 KB: IoC/service optimization, compound handler lifecycle (`OnException`, `MiddlewareScoping`), railway programming, HTTP endpoints (`[EmptyResponse]` vs `Results.NoContent()`), anti-patterns #15 and #16 |
| 3 (2026-04-17) | Modular monolith + messaging + SignalR | `integration-messaging.md` extended 20→38 KB with resiliency policies and production RabbitMQ; `adding-bc-module.md` gained `IWolverineExtension` alternatives; `wolverine-signalr.md` extended 23→26 KB with FQN-vs-kebab-case, request/reply, JSON override, client-driven group changes |
| 4 (2026-04-18) | Event sourcing + projections + DCB | `marten-event-sourcing.md` extended 30→52 KB with cross-stream handlers, `FetchLatest`/`Evolve`/`DetermineAction`, async daemon configuration, `EventAppendMode.Quick`, `UseIdentityMapForAggregates`, `Marten.AspNetCore` streaming, AP#14; `marten-projections.md` extended 20→44 KB with native Marten patterns (composite, enrichment, flat-table, multi-stream routing); `dynamic-consistency-boundary.md` touchups; `polecat-event-sourcing.md` reframed as 📚 Reference |
| 5 (2026-04-18) | Testing skills refactor | `critter-stack-testing-patterns.md` restructured into three H1 parts (Fundamentals / Integration / Advanced), extended 40.8→64.6 KB with `PlayScheduledMessagesAsync`, `WaitForConditionAsync`, `IInitialData`, tracked session configuration, debugging CLI commands, test parallelization, advanced Testcontainers, Polecat section reframed as archived |
| 6 (2026-04-18) | Gap analysis consolidation | This file refreshed from its 2026-04-14 pre-M2 state to the post-Wave-5 present; sections reorganized as "absorbed / diverges / uncovered" rather than the old "must do / should do / deferred" which was keyed to M2-S2 planning and no longer relevant |

---

## Section 5 — Open questions logged with JasperFx

Two questions live in `docs/jasperfx-open-questions.md` at the time of this review:

1. **[Polecat][Wolverine] Handler routing attribute for ancillary Polecat stores** — raised 2026-04-17. Polecat has registration parity with Marten for ancillary stores but appears to lack the `[PolecatStore]` equivalent of `[MartenStore]`. Source-verified. CritterBids' position: reference-only skill with the asymmetry flagged.
2. **[Docs][Marten] ai-skills DCB documentation claims DCB is Polecat-only** — raised 2026-04-18. Source-verified contradiction: `BoundaryModelAttribute` exists in both `Wolverine.Marten` and `Wolverine.Polecat`, `EventTagQuery` lives in shared `JasperFx.Events`, `MartenTests/Dcb/` has full workflow coverage. CritterBids' position: skill documents DCB as store-agnostic matching the source tree, not the ai-skills claim.

Both questions are low-severity for CritterBids directly (our skills are correct) but would help any other project treating ai-skills as primary reference.

---

## Section 6 — What to do with this document going forward

- **After each future wave**, update Section 1 to reflect new coverage and shift any 🟡/🔴 rows to ✅ if the gap was closed.
- **Before a major CritterBids milestone** (first production deploy, first conference demo), review Section 3 uncovered items and promote any that are now in-scope to an actual skill-writing task.
- **When an ai-skills file is updated** (Jeremy pushes changes to `C:\Code\JasperFx\ai-skills`), diff against the corresponding CritterBids skill and add any new patterns worth absorbing.
- **When CritterBids finds something ai-skills is wrong about**, log it in `docs/jasperfx-open-questions.md` and note it in Section 2 (divergences) or Section 5 (open questions) here.

This doc is the cheap audit. Re-running a full wave-style cross-reference is expensive; this is the lightweight equivalent that catches drift between refreshes.
