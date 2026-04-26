# CritterBids: Structural Constraints (Layer 1 Rules)

These rules encode the architectural non-negotiables from accepted ADRs and the operative patterns from skill files. They apply to all implementation sessions, across all bounded contexts. If a situation appears to require deviating from a rule marked **Guardrail**, surface it in the session retrospective rather than silently departing from it. Conventions (un-marked rules) tolerate justified deviation captured in the same place.

---

## Modular Monolith Boundaries (ADRs 001, 008, 011)

**Never add a project reference from one BC project to another BC project.** *(Guardrail.)*
The only shared dependency between BCs is `CritterBids.Contracts`. Cross-BC dependencies are expressed as integration-event types in that project, never as direct .NET project references. This is the structural defining property of the modular monolith per ADR 001.

**Never call another BC's handler in-process.** *(Guardrail.)*
Cross-BC communication is exclusively via Wolverine message dispatch. Direct method invocation across BC boundaries (calling a public service method from another BC) is not permitted. The only shared surface is the message contract itself.

**Each BC registers itself via `AddXyzModule()`; `Program.cs` calls these and nothing else for BC composition.**
The bootstrap layer (`CritterBids.Api/Program.cs`) wires modules but does not configure their internals. Per-BC document types, projections, aggregate registrations, and Wolverine handler discovery happen inside the BC's own `AddXyzModule()` extension method via `services.ConfigureMarten()` and `opts.IncludeAssembly(...)`. See `docs/skills/adding-bc-module.md`.

**Cross-BC handler isolation in test fixtures requires an explicit `*BcDiscoveryExclusion`.**
When BC A's tests share an event type with BC B and both BCs have a handler for that type, BC A's test fixture must declare a `BBcDiscoveryExclusion` to prevent BC B's handler from being discovered during BC A's tests. `IncludeAssembly` scans regardless of which `AddXyzModule()` was called. See `docs/skills/critter-stack-testing-patterns.md`.

---

## Marten Event Store and Bootstrap (ADRs 009, 011)

**The Critter Stack runs with exactly one primary Marten store.** *(Guardrail.)*
`Program.cs` contains exactly one `AddMarten(...).IntegrateWithWolverine().UseLightweightSessions()` chain. ADR 011 made this universal across all eight BCs; ADR 009 established the pattern for the Marten BCs that existed at that time. Adding a second `AddMarten()` (or `AddPolecat()`) violates the bootstrap and re-introduces the dual-store conflict ADR 010 was created to document.

**Each BC contributes its document types, projections, and aggregates via `services.ConfigureMarten()` inside its `AddXyzModule()`.**
The shared store is composed cooperatively; each BC owns its own type registrations. A BC must not register its types from `Program.cs`, from another BC, or via a global startup hook.

**`opts.Policies.AutoApplyTransactions()` lives in `UseWolverine(...)` in `Program.cs`, not inside any BC's `ConfigureMarten()` call.** *(Guardrail.)*
This applies the transaction policy globally for all message handlers. Per-BC application would create gaps in transactional coverage and is incorrect. See `docs/skills/wolverine-message-handlers.md`.

---

## Transport Posture (ADR 002)

**RabbitMQ is the cross-BC transport for the MVP.** *(Guardrail for "no other transports without an ADR".)*
The default integration-event transport is RabbitMQ via Wolverine. Adding a second transport (Azure Service Bus, Kafka) requires its own ADR; the planned ASB swap is itself a future ADR-grade event per ADR 002.

**Integration events cross BC boundaries via `OutgoingMessages` returned from a handler.** *(Guardrail.)*
A handler signals an outbound integration event by returning a `CritterBids.Contracts.<BC>.<Event>` instance (or an `OutgoingMessages` collection containing it). The handler never calls `IMessageBus.PublishAsync` to emit an integration event. See `docs/skills/integration-messaging.md`.

**`IMessageBus` injection in a handler is justified only for `ScheduleAsync()`.**
Schedule-message scenarios (the auction-closing saga's scheduled `CloseAuction` is the canonical case) need `IMessageBus.ScheduleAsync(...)` because there is no equivalent return-shape API at this Wolverine version. All other publication shapes are `OutgoingMessages` returns.

**Wolverine outbox tracking requires an explicit routing rule.**
`tracked.Sent` only captures messages that have an `opts.PublishMessage<T>().ToRabbitQueue(...)` (or equivalent) routing rule registered. Messages without a routing rule land in `tracked.NoRoutes`, and tests asserting on `tracked.Sent` will fail. See `docs/skills/integration-messaging.md`.

---

## Wolverine Handler Conventions (skill: `docs/skills/wolverine-message-handlers.md`)

**Handlers return events or messages; they do not call `session.Store(...)` directly.**
Aggregate-stream handlers declare their target via `[WriteAggregate]` and return the events to append. The transaction wrapping is applied by the global `AutoApplyTransactions` policy.

**Aggregate-targeting handlers declare `[WriteAggregate]`.** *(Guardrail.)*
Handlers that mutate an aggregate stream are decorated with `[WriteAggregate("<stream-id-arg>")]`. The Wolverine source generator depends on this decoration; absent it, the handler is treated as a plain message handler and stream operations silently fail.

**Cross-aggregate reads in a handler use `[Entity]` parameter loading.**
When a handler needs to read an aggregate it does not write to (e.g., DCB scenarios where one aggregate's decision depends on another's state), the parameter is decorated with `[Entity]` and Wolverine resolves it via `IDocumentSession`. Manual `session.LoadAsync<T>()` calls in handler bodies are a smell. See `docs/skills/dynamic-consistency-boundary.md`.

**Handler discovery and HTTP endpoint discovery are separate passes.**
`opts.Discovery.CustomizeHandlerDiscovery()` only scopes handler classes; HTTP endpoint code-generation runs independently and still triggers `[WriteAggregate]` store resolution. A test fixture that disables external transports for handlers will still resolve HTTP endpoints if they are scanned. See `docs/skills/critter-stack-testing-patterns.md`.

---

## Wolverine Saga Conventions (skill: `docs/skills/wolverine-sagas.md`)

**Every terminal path of a saga calls `MarkCompleted()`.** *(Guardrail.)*
A saga that closes without `MarkCompleted()` leaks state and times out under recovery. Each handler that resolves the saga (success, failure, cancellation) ends with `MarkCompleted()`.

**Sagas never call `IMessageBus.PublishAsync` in a handler body.** *(Guardrail.)*
Outbound messages are produced via `OutgoingMessages` returns. Direct `IMessageBus.PublishAsync` calls bypass the outbox and the transactional envelope, which the saga's persistence depends on for correctness.

**Scheduled messages in a saga use `IMessageBus.ScheduleAsync(...)`.**
This is the one justified `IMessageBus` injection in a saga (or any handler). The `CloseAuction` scheduling in the auction-closing saga is the canonical example.

---

## Domain Event Conventions (skill: `docs/skills/domain-event-conventions.md`)

**Domain event type names never carry an "Event" suffix.** *(Guardrail.)*
The type is `BiddingOpened`, not `BiddingOpenedEvent`. Wolverine source generation, Marten projection registration, and project-wide readability rely on this convention being uniform.

**Every domain or integration event type registers via `opts.AddEventType<T>()` in its owning BC's `AddXyzModule()`.** *(Guardrail.)*
Marten 8's projection validator and Wolverine's handler discovery both depend on the registration. An unregistered event type silently fails to project or dispatch.

**Domain events live in the owning BC's namespace; integration events live in `CritterBids.Contracts.<BC>`.** *(Guardrail.)*
A type in `CritterBids.Contracts` is by definition cross-BC. A type in `CritterBids.Auctions` (the BC project) is internal to that BC. The two are distinct types even when they share a name (`ListingPublished` exists in both shapes). Care is required to not conflate them in code or in narratives.

**Records on domain events use `IReadOnlyList<T>` for collection properties, not `List<T>`.**
The collection mutability of `List<T>` permits accidental in-place mutation that breaks event-sourcing's immutability assumption. `IReadOnlyList<T>` makes the immutability structural.

---

## Sealed-record Posture (skill: `docs/skills/csharp-coding-standards.md`)

**Commands, events, queries, and read models are `sealed record` types.**
Sealed prevents accidental inheritance; record provides value semantics. Both are required for the Critter Stack patterns to work as expected (Wolverine message identity, Marten document equality).

---

## UUID Strategy (ADR 007)

**Stream IDs are UUID v7 (`Guid.CreateVersion7()`) for all Marten BCs.** *(Guardrail for stream IDs.)*
ADR 007 accepted UUID v7 for stream IDs across the Marten BCs (extended to all eight by ADR 011). The Unix-millisecond prefix gives insert locality. Stream IDs are not natural business keys; UUID v5 with a BC-specific namespace constant remains an option only when a deterministic stream-creation case has a natural business key.

**Event row IDs use the Marten engine default unless a specific BC re-evaluates and adopts a strategy.**
The event-row-ID strategy is re-deferred at M4-S1 with M5-S1 (Settlement BC foundation decisions) as the next trigger and Erik as the named owner of the JasperFx follow-up nudge. M3 shipped on the engine default without incident.

---

## Frontend Stack Posture (ADRs 012, 013)

**React frontends ship as static Vite SPAs.** *(Guardrail per ADR 012.)*
No Next.js, no Remix framework mode, no TanStack Start. The backend owns all contracts; the frontend is a static SPA. Choosing a meta-framework re-introduces the contract-ownership ambiguity ADR 012 closed.

**The frontend core stack is TypeScript strict + Zod + TanStack Query + Tailwind v4 + shadcn/ui + react-hook-form + `@microsoft/signalr` + Vitest + Playwright; PWA from day one.** *(ADR 013 still Proposed.)*
Routing and the auth client pattern are deferred. Adding a library outside this stack requires ADR 013 amendment or supersession.

---

## Spec-Anchored Development (ADR 016)

**Load relevant narratives before generating implementation.**
The narrative at `docs/narratives/` is the architectural reference for the session. Code implements what the narrative specifies, not what seems reasonable at the time. Before authoring a slice, check whether a narrative covers it.

**When generated code diverges from the narrative, surface the divergence in the retrospective.**
Either the code is wrong (correct it) or the narrative is wrong (update it). The retrospective is where this resolution is recorded and committed. Do not silently resolve the conflict.

**The retrospective is part of every session's definition of done.** *(Guardrail.)*
A session that closes without a retrospective leaves the spec ↔ code relationship unaudited. The PR that contains the implementation contains the retrospective.

**The first narrative session (foundation refresh Phase 2) audits lived code for drift via the four-lane findings discipline.**
The lanes are `narrative-update`, `workshop-update`, `code-update`, `document-as-intentional`. `code-update` findings become follow-up implementation slices in Phase 2.5; the others resolve in the narrative session's PR.

---

## Design-Phase Workflow Sequence (ADR 017)

**For new BCs (Settlement, Obligations, Relay, Operations) and substantial feature work, follow the six-step sequence: Context Mapping → Domain Storytelling → Event Modeling → Narratives → Prompts → Implementation + Retrospective.**
Steps 1 and 2 are available, not mandatory; the per-BC decision is made at the BC's design opening session and recorded in its first workshop artifact.

**For lived BCs (Participants, Selling, Auctions, Listings), Steps 1 and 2 are not retroactively performed.**
Phase 3 of the foundation refresh adds Cast/Setting/Ubiquitous-Language sections to the existing workshops as a partial substitute. The workshops authored before ADR 017 are the substantive design artifacts for those BCs.

**Each step's output is a committed artifact.**
Context maps and Domain Storytelling outputs in `docs/workshops/`; Event Models in `docs/workshops/`; narratives in `docs/narratives/`; prompts in `docs/prompts/implementations/`; retrospectives in `docs/retrospectives/`. A step is finished enough to move forward when its artifact is committed.

---

## Project-wide writing-style and commit conventions

**No em dashes in committed prose.**
Hyphens (`-`) and en dashes (Unicode `U+2013`) are fine. The em dash (Unicode `U+2014`) is excluded. Existing files predating this convention are not retroactively swept; new files and new edits adhere.

**No "paddle" reference anywhere in domain or application code.** *(Guardrail per CLAUDE.md.)*
Bidders are identified by `BidderId`. The "paddle" auction-house metaphor is rejected as project vocabulary.

**`[AllowAnonymous]` on all endpoints through M6.**
Real authentication lands at M6; until then, every endpoint carries `[AllowAnonymous]` as the intentional project stance, not a temporary override. The `[Authorize]` convention resumes at M6.

**Commit messages do not include a `Co-Authored-By` trailer.** *(Guardrail per CLAUDE.md.)*

**Commits do not skip hooks (`--no-verify`) or bypass signing unless the user explicitly requests it.** *(Guardrail per CLAUDE.md.)*

**Commit directly to `main` is prohibited; branch and PR.** *(Guardrail per CLAUDE.md.)*

---

## Document history

- **v0.1** (2026-04-26): Authored as foundation-refresh Phase 1 Item 4. Sections sourced from ADRs 001, 002, 007, 008, 009, 010, 011, 012, 013, 016, 017 and the skill files `domain-event-conventions.md`, `wolverine-message-handlers.md`, `wolverine-sagas.md`, `integration-messaging.md`, `critter-stack-testing-patterns.md`, `dynamic-consistency-boundary.md`, `adding-bc-module.md`, `csharp-coding-standards.md`. Layer 2 (per-BC ubiquitous language) and Layer 3 (code conventions) deferred per the rules README.
