# CritterBids Skills Index

Skills are implementation pattern documents. Load the relevant skill file **before** starting any implementation task. They encode hard-won patterns and prevent rediscovering known solutions.

## How to Use Skills

1. Identify your task from the table below
2. Load the skill file(s) into your context
3. Follow the patterns — don't improvise unless you have a specific reason to deviate
4. If you deviate, document why in a comment or ADR

Skills are living documents. When a new pattern is established or an existing one is refined during implementation, update the relevant skill file.

---

## Skill Status

| Skill | File | Status | Source |
|---|---|---|---|
| Wolverine message handlers | `wolverine-message-handlers.md` | ✅ Complete | Extracted from CritterSupply + M2 (routing rule AP#14) |
| Wolverine sagas | `wolverine-sagas.md` | ✅ Complete | Extracted from CritterSupply |
| Marten event sourcing | `marten-event-sourcing.md` | ✅ Complete | Extracted from CritterSupply + updated M2 (named stores, perf settings) + Wave 4 2026-04-18 (cross-stream handlers, `FetchLatest`/`Evolve`/`DetermineAction`, async daemon configuration, `EventAppendMode.Quick`, `UseIdentityMapForAggregates`, `Marten.AspNetCore` streaming, AP#14) |
| Critter Stack ancillary stores | `critter-stack-ancillary-stores.md` | 📚 Reference — not currently used | Rewritten 2026-04-17 from JasperFx ai-skills `marten/advanced/ancillary-stores.md` (replaces archived `marten-named-stores.md`) |
| Marten projections (native + EF Core) | `marten-projections.md` | ✅ Complete | Extended 2026-04-18 (Wave 4) with native Marten projection patterns — composite, enrichment, flat-table, multi-stream routing. EF Core half unchanged. |
| Marten querying | `marten-querying.md` | ✅ Complete | Authored from Marten docs + Jeremy Miller's blog |
| Polecat event sourcing | `polecat-event-sourcing.md` | 📚 Reference — not currently used | Reframed 2026-04-18 (Wave 4). ADR 011 moved all BCs to Marten. Technical content intact; status banner updated. |
| .NET Aspire orchestration | `aspire.md` | ✅ Complete | Authored from M1 (S1–S4) experience |
| Dynamic Consistency Boundary | `dynamic-consistency-boundary.md` | ✅ Complete | Extracted from CritterSupply + Wave 4 2026-04-18 touchups (known-vs-dynamic decision-guide split, explicit store-coverage note) |
| Integration messaging | `integration-messaging.md` | ✅ Complete | Extracted from CritterSupply + updated M2 (Aspire RabbitMQ, Separated mode) |
| SignalR real-time | `wolverine-signalr.md` | ✅ Complete | Extracted from CritterSupply |
| Projection side effects for broadcast live views | `projection-side-effects-for-broadcast-live-views.md` | ✅ Complete | New — authored M2 from vision doc + Jeremy clarification |
| Testing patterns | `critter-stack-testing-patterns.md` | ✅ Complete | Extracted from CritterSupply + updated M2 (named store fixtures, cross-BC isolation) + Wave 5 2026-04-18 (restructured into Fundamentals/Integration/Advanced parts; added `PlayScheduledMessagesAsync`, `WaitForConditionAsync`, `IInitialData`, tracked session configuration, `codegen-preview` debugging, parallelization strategy, advanced Testcontainers patterns; Polecat section reframed as 📚 Archived) |
| C# coding standards | `csharp-coding-standards.md` | ✅ Complete | Extracted from CritterSupply |
| Event Modeling workshop | `event-modeling/SKILL.md` | ✅ Complete | Shared |
| Adding a BC module | `adding-bc-module.md` | ✅ Complete | New — authored M2 pre-S2 from ADR 008 + JasperFx ai-skills |
| React frontend | `react-frontend.md` | 🔴 Not yet written | New |
| Domain event conventions | `domain-event-conventions.md` | ✅ Complete | New — authored M2-S8 from S5–S6 patterns |

**Status key:**
- ✅ Complete and ready to use
- 📚 Reference — documents a capability CritterBids does not currently use; consult when the capability becomes relevant
- 🟡 Placeholder — useful stub exists, fill in during first real use
- 🔴 Not yet written — create when first needed
- ⚠️ Archived — superseded; retained only if historical context matters (prefer deletion + rename)

---

## Skills by Task

### Implementation

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| Wolverine command handler | `wolverine-message-handlers.md` | `csharp-coding-standards.md` |
| Wolverine HTTP endpoint | `wolverine-message-handlers.md` | — |
| Saga (multi-step workflow) | `wolverine-sagas.md` | `integration-messaging.md` |
| Scheduled messages / timeouts | `wolverine-sagas.md` | — |
| Event-sourced aggregate (Marten) | `marten-event-sourcing.md` | `csharp-coding-standards.md` |
| Marten native projection | `marten-event-sourcing.md` | — |
| EF Core projection (Marten) | `marten-projections.md` | `marten-event-sourcing.md` |
| Read model query (LINQ / compiled / batched) | `marten-querying.md` | `csharp-coding-standards.md` |
| JSON streaming to HTTP response | `marten-querying.md` | `wolverine-message-handlers.md` |
| Raw SQL / advanced SQL query | `marten-querying.md` | — |
| DCB boundary model | `dynamic-consistency-boundary.md` | `marten-event-sourcing.md` |
| Integration event (cross-BC) | `integration-messaging.md` | `domain-event-conventions.md` |
| SignalR hub + real-time push | `wolverine-signalr.md` | — |
| Broadcast live view via projection side effect | `projection-side-effects-for-broadcast-live-views.md` | `wolverine-signalr.md` |
| Derived domain event from projection state | `projection-side-effects-for-broadcast-live-views.md` | `marten-event-sourcing.md` |
| New BC module registration | `adding-bc-module.md` | `marten-event-sourcing.md` |

### Testing

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| Integration test (Alba + Testcontainers) | `critter-stack-testing-patterns.md` | — |
| Unit test (pure handler logic) | `critter-stack-testing-patterns.md` | — |
| Marten BC test fixture | `critter-stack-testing-patterns.md` | `adding-bc-module.md` |
| **Cross-BC handler isolation** | **`critter-stack-testing-patterns.md`** | — |
| Polecat BC test fixture (📚 archived — see ADR 011) | `critter-stack-testing-patterns.md` | `polecat-event-sourcing.md` |
| Saga test | `wolverine-sagas.md` | `critter-stack-testing-patterns.md` |
| EF Core projection test | `marten-projections.md` | `critter-stack-testing-patterns.md` |
| Compiled query correctness | `marten-querying.md` | `critter-stack-testing-patterns.md` |
| SignalR integration test | `wolverine-signalr.md` | `critter-stack-testing-patterns.md` |
| Projection side-effect integration test | `projection-side-effects-for-broadcast-live-views.md` | `critter-stack-testing-patterns.md` |

### Frontend

| Task | Primary Skill | Secondary Skill |
|---|---|---|
| React component | `react-frontend.md` | — |
| SignalR client connection | `react-frontend.md` | `wolverine-signalr.md` |
| Real-time bid feed | `react-frontend.md` | `wolverine-signalr.md` |

### Design & Architecture

| Task | Skill |
|---|---|
| Event Modeling workshop | `event-modeling/SKILL.md` |
| Naming domain events | `domain-event-conventions.md` |
| Personas for workshop | `../personas/README.md` |
| Reactive / live-view architecture | `../vision/live-queries-and-streaming.md` |

---

## Writing New Skills

When writing a 🔴 skill for the first time:

1. Implement the feature first — let the code reveal the real patterns
2. Document what you learned, including what didn't work
3. Follow the density principle: every line earns its place
4. Reference related skills at the bottom
5. Update this README status from 🔴 to ✅

### Skills Still Needed

**Write fresh for CritterBids (no direct CritterSupply equivalent):**

- `react-frontend.md` — React + TypeScript conventions, SignalR hook patterns, bid feed state management, ops dashboard patterns. CritterBids-specific.
- `domain-event-conventions.md` — past-tense naming, no "Event" suffix, aggregate ID as first property, `DateTimeOffset` timestamps, `IReadOnlyList<T>` for collections, CritterBids vocabulary reference. Write in M2-S7 when first domain events for Selling BC are authored.

---

## Relationship to CritterSupply Skills and JasperFx AI Skills

Skills marked "Extracted from CritterSupply" have direct equivalents in CritterSupply's `docs/skills/` directory. The extraction process:

1. Keep all domain-agnostic content verbatim or near-verbatim
2. Replace CritterSupply BC names and examples with CritterBids equivalents
3. Strip milestone markers, retrospective references, and `src/` file paths
4. Keep every anti-pattern and lesson learned — these transfer wholesale

CritterBids also maintains a gap analysis against the public JasperFx AI skills repo at `docs/skills/jasper-fx-ai-skills-gap-analysis.md`. Consult it when the canonical Critter Stack patterns have changed or when a skill seems incomplete.

The gap analysis was last reviewed 2026-04-14 as part of a post-M2-S2 skills pass that:
- `marten-named-stores.md` archived (superseded by ADR 009 — shared primary store)
- Removed Anti-Pattern #15 from `wolverine-message-handlers.md` (constraint no longer exists)
- Updated cross-BC handler isolation section in `critter-stack-testing-patterns.md` (rationale updated: Marten not configured, not named store absent)

A subsequent refresh on 2026-04-17 (Wave 1 of a multi-wave cross-reference against the JasperFx ai-skills repo) applied:
- **Renamed and rewrote** the archived `marten-named-stores.md` as `critter-stack-ancillary-stores.md` (status: 📚 Reference). The new file covers current ancillary-store capability on top of a primary store (Marten + Polecat), reflects API capability added since ADR 008 (handlers can use `[MartenStore]` + `IDocumentSession` injection + `AutoApplyTransactions` when a primary store is registered), and preserves the ADR 008 → ADR 009 historical framing.
- Extended `projection-side-effects-for-broadcast-live-views.md` with: a `slice`/`operations` API reference table, a "Writing Auxiliary Documents" section with `ops.LoadAsync` + `ops.Store` example, a fourth testing layer using a `StubEventSlice<T>` test double for fast-feedback coverage, and three new pitfalls (null-dereference on `Snapshot`, external I/O in the hook, infinite `AppendEvent` loop).
- Deleted the archived `marten-named-stores.md` (rename-by-delete).

A fourth refresh on 2026-04-18 (Wave 4 of the cross-reference against the JasperFx ai-skills repo) applied:
- **Extended `marten-event-sourcing.md`** (30 KB → 52 KB, +22 KB): new Cross-Stream Aggregate Handlers section (multiple `[WriteAggregate]`, `VersionSource`, `[ConsistentAggregate]`, `[ConsistentAggregateHandler]`); extended Projections section with `FetchLatest` preference over `AggregateStreamAsync`, `Evolve(IEvent)` alternative, `DetermineAction` for soft-delete lifecycles, `RebuildSingleStreamAsync`, performance knobs (`IncludeType`, `CacheLimitPerTenant`, `BatchSize`); new Async Daemon Configuration section (Solo / HotCold / Wolverine-managed distribution, don't-combine rule, error-handling defaults with CritterBids posture, `WaitForNonStaleProjectionDataAsync`); extended Marten Configuration with `EventAppendMode.Quick` rationale, `UseIdentityMapForAggregates` with mutation-safety warning, `Marten.AspNetCore` `WriteArray`/`WriteById` streaming endpoints; new Anti-Pattern #14 (mutating identity-mapped aggregates).
- **Combined `marten-projections.md`** (20 KB → 44 KB, +24 KB): kept existing EF Core content intact; added new Native Marten Projections half covering multi-stream routing patterns (common-interface, `Identities<T>` fan-out, time-based segmentation, `FanOut<TParent, TChild>`, custom groupers with the no-self-read constraint, `RollUpByTenant`), composite projections with `Updated<T>` / `ProjectionDeleted<TDoc, TId>` / `References<T>` synthetic events, event enrichment with `EnrichEventsAsync` and the `FetchLatest` doesn't-run-enrichment gotcha, flat-table projections (`FlatTableProjection` and raw-SQL `EventProjection`); new Decision Guide table covering all projection-type choices; EF Core sub-section anchors renamed to avoid TOC collisions with native sub-sections.
- **Touchups to `dynamic-consistency-boundary.md`**: Quick Decision Guide split into known-IDs (→ `marten-event-sourcing.md` §9) vs dynamic-selection (→ DCB), with a paragraph explaining the split; registration step gained an explicit store-coverage note citing the Wolverine repo's `MartenTests/Dcb/` coverage and the shared `JasperFx.Events.Tags.EventTagQuery` location. The DCB skill was already more comprehensive than ai-skills' version on the tagging gotchas and the `BidRejected` stream-placement decision — no structural changes made.
- **Reframed `polecat-event-sourcing.md`** from "✅ Complete" (M1 findings) to "📚 Reference — not currently used" to match ADR 011. Status banner rewritten; technical content preserved intact (accurate for Polecat 2.x, valuable for sibling projects). "Why SQL Server for Certain BCs?" section marked Superseded, retaining the rationale for evaluators.
- **New JasperFx open-question (#2):** ai-skills' `marten/advanced/dynamic-consistency-boundary.md` claims "DCB is currently implemented in Polecat" — source-verified to be false (Marten's DCB implementation is production, with full `MartenTests/Dcb/` coverage). Logged in `docs/jasperfx-open-questions.md` for Jeremy Miller's next cycle. Severity low for CritterBids (our skills are already correct); higher for anyone treating ai-skills as primary reference.

A fifth refresh on 2026-04-18 (Wave 5 of the cross-reference against the JasperFx ai-skills repo) applied:
- **Restructured `critter-stack-testing-patterns.md`** (40.8 KB → 66 KB, +25 KB) into three explicit parts: **Part I — Fundamentals** (philosophy, North Star lifecycle, test authentication, test isolation, unit-testing pure functions, validators, time-dependent handlers, failure paths, Shouldly, test organization), **Part II — Integration Testing** (Marten BC TestFixture pattern, event-sourcing race conditions, integration test patterns, fixture helper methods, tracked session configuration, scheduled messages, async projections, `IInitialData`, debugging with `codegen-preview`), and **Part III — Advanced Scenarios** (cross-BC handler isolation, parallelization strategy, advanced Testcontainers patterns, Polecat fixture as archived). The three-part structure keeps the single-file entry point while making the progression explicit; new contributors read Part I, fixture authors read Part II, multi-BC and parallelization cases live in Part III.
- **New Part II content drawn from ai-skills:** `Tracked Session Configuration` (Timeout, IncludeExternalTransports, AlsoTrack, DoNotAssertOnExceptionsDetected with CritterBids posture); `Testing Scheduled Messages` with `PlayScheduledMessagesAsync` as the canonical saga-timeout testing pattern; `Testing Async Projections` with both `WaitForNonStaleProjectionDataAsync` and `WaitForConditionAsync` (the proper replacement for `Task.Delay`); `Seeding with IInitialData` with registration and reseed-helper patterns (not currently used in CritterBids, documented for the first cross-cutting reference-data need); `Debugging Integration Tests` with `codegen-preview`, `describe-routing`, and `describe-resiliency` CLI commands; void-endpoint 204 status explicitly called out in the integration test patterns.
- **New Part III content:** `Test Parallelization Strategy` with the unique-IDs-vs-sequential trade-off, collection fixture patterns, `[assembly: CollectionBehavior(DisableTestParallelization = true)]` as the baseline safety net, parallelization anti-patterns (hard-coded IDs, mid-suite cleanup, concurrent tracked sessions); `Advanced Testcontainers Patterns` with parallel container startup via `Task.WhenAll`, `PullPolicy.Missing` for CI, dynamic database per fixture (shared container), RabbitMQ with dynamic virtual hosts, and a trade-offs table covering five isolation approaches.
- **Polecat fixture section reframed** as `📚 Archived` with the same framing used in Wave 4 for `polecat-event-sourcing.md` and `polecat-event-sourcing.md`. Technical content preserved intact (accurate for Polecat 2.x, valuable for sibling projects).
- **All existing content preserved.** Cross-BC handler isolation, `ConfigureAppConfiguration` caveat, `AutoApplyTransactions` gotcha, `ProblemDetails` in non-HTTP handlers, `TestAuthHandler` pattern, `TrackedHttpCall` helper, test organization tree — all kept verbatim where nothing needed to change, moved into the appropriate Part for findability.
- **External references stable.** Keeping the single-file structure means the ≈20 inbound references from other skill files and CLAUDE.md continue to resolve. Deep links to specific sections (e.g., `#cross-bc-handler-isolation-in-test-fixtures`) retain their anchors.

Wave 6 remains: a refreshed gap analysis dated 2026-04-18 summarizing all five wave outcomes.
