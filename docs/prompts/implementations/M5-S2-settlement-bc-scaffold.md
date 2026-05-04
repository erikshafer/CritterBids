# M5-S2: Settlement BC Scaffold

**Milestone:** M5 ([Settlement BC](../../milestones/M5-settlement-bc.md))
**Slice:** S2 of 6 (BC Scaffold)
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
**Agent:** @PSA
**Estimated scope:** one PR; ~7 files added (csproj × 2, SettlementModule.cs, SettlementSaga.cs, fixture × 2, smoke test × 1, plus the retro), ~3 files modified (`CritterBids.slnx`, `CritterBids.Api.csproj`, `Program.cs`)

---

## Goal

Stand up the `CritterBids.Settlement` and `CritterBids.Settlement.Tests` projects and wire them into the solution, the Api host, and the Wolverine / Marten configuration — nothing more. The scaffold must compile, register cleanly, boot cleanly, and contribute zero behavior. No handlers, no projections, no event-type registrations, no RabbitMQ publish / listen lines, no `PendingSettlement` or `BidderCreditView` projections, no integration-event consumers, no saga state-machine logic. S3 lands the `PendingSettlement` projection and the `ListingPublished` consumer; S4 lands the saga's seven-phase implementation; S5 lands the failure paths and BIN source plus `BidderCreditView`; S6 publishes `SellerPayoutIssued`. All S2 is doing is creating the shelf.

S1 closed the workflow-hosting decision (ADR-019 — Wolverine Saga), authored the three integration contract stubs at `src/CritterBids.Contracts/Settlement/` (`SettlementCompleted.cs`, `PaymentFailed.cs`, `SellerPayoutIssued.cs`), and folded the W003 F002 / F004 / F005 amendments into the workshop. S2 walks in with zero vocabulary ambiguity and a known host primitive. If any new design decision surfaces mid-session, stop and flag — do not pivot in-session.

This slice is jointly authoritative with narrative 002 per AUTHORING.md rule 3's cutover-gate clause inherited from M5-S1. The narrative does not dramatise the scaffold itself, but its Cast and Setting establish the financial ground (the seven-phase workflow, the deterministic `SettlementId`, the MVP credit-ledger posture) that this slice's `SettlementSaga` shell will host once S4 fleshes it out.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M5-settlement-bc.md` | Milestone scope — S2 deliverables are §4 (solution layout), §5 (infrastructure), §7 (slice breakdown S2 row) |
| `docs/narratives/002-winner-clears-settlement.md` | Joint-authoritative narrative per AUTHORING.md rule 3; Cast and Setting carry the financial-ground vocabulary the scaffold prepares for |
| `docs/decisions/019-settlement-workflow-hosting.md` | ADR-019 (Wolverine Saga chosen) — informs the `SettlementSaga` shell shape and Marten registration |
| `docs/retrospectives/M5-S1-settlement-bc-foundation-decisions-retrospective.md` | "What remains / next session should verify" section — S2 walks in with the M5-S1 closure |
| `docs/skills/adding-bc-module.md` | Canonical BC scaffold pattern — Marten BC registration, host-level settings, `Program.cs` wiring, checklist |
| `docs/prompts/implementations/M3-S2-auctions-bc-scaffold.md` and its retrospective | Most recent BC-scaffold precedent; the Marten-8 projection-validator note from the retro is anti-pattern context for the saga path chosen here |
| `src/CritterBids.Auctions/AuctionsModule.cs` and `src/CritterBids.Auctions/AuctionClosingSaga.cs` | Reference shapes for the `Schema.For<T>().Identity(x => x.Id).UseNumericRevisions(true)` saga registration and the empty `: Wolverine.Saga` shell |

---

## In scope

- **Create `src/CritterBids.Settlement/` class library project.** Target framework, package references, and SDK style match the other Marten BC projects (`CritterBids.Auctions`, `CritterBids.Listings`, `CritterBids.Selling`). Single `<PackageReference Include="WolverineFx.Http.Marten" />` per the M3-S2 precedent (transitively brings `WolverineFx.Http`, `WolverineFx.Marten`, and `Marten`). Add to `CritterBids.slnx` under the `/src/` folder node, alphabetical position after `CritterBids.Selling`. The project has **no** `<ProjectReference>` to `CritterBids.Contracts` in S2 — S3 adds the reference when the `ListingPublished` consumer needs `CritterBids.Contracts.Selling.ListingPublished` and S6 cashes in the existing `CritterBids.Contracts.Settlement.*` stubs as outbound contracts.

- **Create `tests/CritterBids.Settlement.Tests/` test project.** Sibling to the production project per Layout 2. xUnit + Shouldly + Alba + Testcontainers.PostgreSql + Microsoft.NET.Test.Sdk, package versions inherited from `Directory.Packages.props`. Add to `CritterBids.slnx` under the `/tests/` folder node, alphabetical position after `CritterBids.Selling.Tests`. The project references `CritterBids.Settlement` and `CritterBids.Api` (the latter so `AlbaHost.For<Program>()` can resolve the Api entry point).

- **Author `SettlementModule.cs` in `CritterBids.Settlement`.** Single `public static class SettlementModule` with `AddSettlementModule(this IServiceCollection services) : IServiceCollection`. Internal shape mirrors `AuctionsModule.AddAuctionsModule`'s saga-document branch:

  - Single `services.ConfigureMarten(opts => { ... })` block.
  - Inside the lambda: `opts.Schema.For<SettlementSaga>().DatabaseSchemaName("settlement").Identity(x => x.Id).UseNumericRevisions(true);` — the canonical saga-document registration per the AuctionClosingSaga precedent and ADR-019's Saga path.
  - **No `opts.Events.AddEventType<T>()` calls.** Settlement-internal event types (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`) register at first use — the saga lands them in S4. The first-use rule from M2's silent-`AggregateStreamAsync<T>`-null lesson applies.
  - **No `opts.Projections.*` calls.** `PendingSettlement` registers in S3; `BidderCreditView` registers in S5.
  - **No `IWolverineExtension` registration.** Settlement has no concurrency-retry or scheduled-message policies in S2; if such policies are needed they land with the slice that needs them.

- **Author `SettlementSaga.cs` empty shell in `CritterBids.Settlement/SettlementSaga.cs`.** Namespace `CritterBids.Settlement`. Class signature `public sealed class SettlementSaga : Wolverine.Saga`. Body carries only `public Guid Id { get; set; }` plus a one-line forward-looking comment naming what S4 adds (the `SettlementStatus` enum, the per-phase `Handle` methods for `CheckReserve` / `ChargeWinner` / `CalculateFee` / `IssueSellerPayout` / `CompleteSettlement`, and the `MarkCompleted()` calls at terminal state). No state fields beyond `Id`, no `Handle` methods, no `MarkCompleted()` calls, no `OutgoingMessages` returns. The saga document exists solely so `Schema.For<SettlementSaga>()` has a type to bind to.

  > **Marten-8 projection validator does not apply.** Saga documents register via the document-store path (`Schema.For<T>().Identity(...).UseNumericRevisions(true)`), not the projection path (`Projections.LiveStreamAggregation<T>` / `SingleStreamProjection<T>`). The validator that bit M3-S2 (`JasperFxAggregationProjectionBase.AssembleAndAssertValidity`) only fires for projection registrations — saga documents are exempt. No placeholder `Apply` is required, and none should be authored.

- **Wire Settlement into `src/CritterBids.Api/Program.cs`.** Three concrete edits:
  1. Add `using CritterBids.Settlement;` after `using CritterBids.Selling;` (alphabetical).
  2. In the `UseWolverine` block, add `opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);` after the existing `IncludeAssembly` line for `Listing` (preserving the Participants → Selling → Listings → Auctions → Settlement insertion order; this matches the existing convention of "additions land at the end of the list").
  3. In the postgres-guarded modules block, add `builder.Services.AddSettlementModule();` after `AddAuctionsModule();` (preserving the existing module-registration order).

- **Add `<ProjectReference>` from `CritterBids.Api.csproj` to `CritterBids.Settlement.csproj`.** Required for the `typeof(SettlementSaga).Assembly` reference in `Program.cs` to resolve. Insert alphabetically — after the existing Auctions / Contracts / Listings / Participants references, after Selling. Per `adding-bc-module.md` §Checklist.

- **No RabbitMQ wiring.** `Program.cs`'s `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks gain zero Settlement-related lines in S2. M5 §2's three new queue routes (`settlement-selling-events`, `settlement-auctions-events`, `listings-settlement-events`) wire when their consumers / publishers come online: S3 wires `settlement-selling-events` for the `ListingPublished` consumer; S4 wires `settlement-auctions-events` for the `ListingSold` and `BuyItNowPurchased` consumers; S6 wires `listings-settlement-events` for the `SettlementCompleted` publisher. The milestone doc's S2 row mentions "RabbitMQ routing setup" in its scope summary; the precedent set by M3-S2 (which also said "Marten config" but landed zero RabbitMQ lines in S2) is to interpret "setup" as the structural readiness — the queue topology documented and the wiring deferred to the slice that produces or consumes each event. This slice produces and consumes nothing.

- **Test fixture in `tests/CritterBids.Settlement.Tests/Fixtures/`.** `SettlementTestFixture.cs` and `SettlementTestCollection.cs`, mirroring the AuctionsTestFixture / AuctionsTestCollection shape:
  - PostgreSQL Testcontainer with name prefix `settlement-postgres-test-` and the `postgres:17-alpine` image.
  - `AlbaHost.For<Program>` with a `ConfigureServices` override that registers the primary Marten store with the Testcontainers connection string (per the `ConfigureAppConfiguration` timing caveat in `critter-stack-testing-patterns.md` §Cross-BC Handler Isolation), calls `services.AddSettlementModule()`, calls `RunWolverineInSoloMode()` and `DisableAllExternalWolverineTransports()`.
  - **Three cross-BC discovery exclusions** — `SellingBcDiscoveryExclusion`, `AuctionsBcDiscoveryExclusion`, `ListingsBcDiscoveryExclusion`. The Settlement fixture does not register `AddSellingModule()`, `AddAuctionsModule()`, or `AddListingsModule()`, so any handler from those BCs that Wolverine discovers via `IncludeAssembly` would either fail DI validation (Selling: `ISellerRegistrationService`) or fail Marten code-gen (Auctions: handlers operate on the `Listing` aggregate whose schema isn't configured; Listings: handlers operate on `CatalogListingView` whose schema isn't configured). Per memory `project_cross_bc_handler_isolation.md` and the `critter-stack-testing-patterns.md` §Cross-BC Handler Isolation pattern. Naming follows the `{TargetBc}BcDiscoveryExclusion` convention.
  - **Participants is not excluded** — Participants handlers do not depend on services that the Settlement fixture omits, matching the AuctionsTestFixture / ListingsTestFixture precedent.
  - Cleanup helpers `CleanAllMartenDataAsync()` and `ResetAllMartenDataAsync()` and a `GetDocumentSession()` helper, all per the AuctionsTestFixture verbatim shape.
  - **No saga seed helpers, no event-stream seed helpers, no Wolverine tracking helpers.** Those land with the slice that produces the first scenario needing them. S2 has only one smoke test which needs none of them.

- **Smoke test `SettlementModuleTests.cs` in `tests/CritterBids.Settlement.Tests/`.** Single `[Fact]` named `SettlementModule_BootsClean`, exact shape of `AuctionsModuleTests.AuctionsModule_BootsClean`: assert `_fixture.Host.ShouldNotBeNull()` and `_fixture.Host.Services.GetRequiredService<IDocumentStore>().ShouldNotBeNull()`. The test verifies (a) `AddSettlementModule()` registers cleanly alongside the primary Marten store, (b) `Program.cs`'s Wolverine assembly discovery for the Settlement assembly does not surface a code-gen failure, (c) the cross-BC discovery exclusions correctly suppress Selling / Auctions / Listings handlers without breaking host startup. One fact, two assertions — sufficient as a green guard.

- **`GlobalUsings.cs`** with `global using Shouldly;` and `global using Xunit;` per the precedent.

- **Session retrospective** at `docs/retrospectives/M5-S2-settlement-bc-scaffold-retrospective.md`.

---

## Explicitly out of scope

- **Any Settlement-internal event types.** `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated` stay unauthored in S2. They land in `src/CritterBids.Settlement/` when the saga first emits them in S4. Per M5 milestone doc §4 and S1 retro's "deferred to S4" section.
- **Any projection.** `PendingSettlement` lands in S3; `BidderCreditView` lands in S5. No `services.ConfigureMarten` projection registrations in this slice.
- **Any handler, command, endpoint, or saga-state evolution.** No `InitiateSettlement` / `CheckReserve` / `ChargeWinner` / `CalculateFee` / `IssueSellerPayout` / `CompleteSettlement` commands; no per-phase `Handle` methods on the saga; no HTTP endpoints; no `OutgoingMessages` returns. Saga remains an empty shell with `Id` only.
- **`SettlementSaga` state fields beyond `Id`.** No `SettlementStatus` enum; no `HammerPrice`, `FeePercentage`, `FeeAmount`, `SellerPayout`, `WinnerId`, `SellerId`, `ListingId`. S4 adds them per W003 Phase 1 Part 2's "phased progression with evolving state" framing and ADR-019's Saga shape.
- **`MarkCompleted()` calls.** No terminal-state handling in S2 — the saga has no handlers to terminate. S4 / S5 adds terminal calls per the seven-phase progression.
- **RabbitMQ queue wiring in `Program.cs`.** The three new queue routes from M5 §2 wire with their consuming / publishing slices (S3, S4, S6 respectively). S2's `Program.cs` `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks gain zero Settlement-related lines.
- **`<ProjectReference>` from `CritterBids.Settlement` to `CritterBids.Contracts`.** S3 adds it when the first cross-BC consumer (`ListingPublishedHandler`) needs `CritterBids.Contracts.Selling.ListingPublished` as input. S6 cashes in the outbound `CritterBids.Contracts.Settlement.*` stubs when the saga publishes them.
- **Settlement-side `IWolverineExtension`.** No retry policies, no message-partitioning, no custom Wolverine extensions. If S4 / S5 surfaces a need (analogue of `AuctionsConcurrencyRetryPolicies`), they land with the slice that needs them.
- **The `SettlementId.cs` strongly-typed identifier and `SettlementsIdentityNamespaces.cs`.** M5 §4 lists these as M5 deliverables but they cash in when the saga first computes `UuidV5(AuctionsNamespace, $"settlement:{ListingId}")` per W003 Phase 1 Part 6 — that is S4 territory (the saga's `InitiateSettlement` handler is where the deterministic id is first derived). S2 uses `Guid` directly on the saga shell.
- **Skill-file edits.** `wolverine-sagas.md` gains the Settlement amendment after S4 ships per ADR-019 §Consequences. `marten-projections.md` carries the M5-S1 "Pending: M5-S3" flag already; S3 cashes that in. S2 edits no skill files. If S2 surfaces a skill gap (e.g. `adding-bc-module.md` missing a step that would have helped), note it in the retrospective — do not edit in-session per AUTHORING.md rule 4.
- **W003 / narrative 002 edits.** S1 closed the W003 amendments. No further W003 edits in S2. No narrative 002 cite-and-edit work in S2 — the scaffold is below any Moment-grain narrative drift surface.
- **Listings-side projection extension for `SettlementCompleted`.** M5-S6 territory; the `CatalogListingView.Status = "Settled"` path is wired with the publisher slice.

---

## Conventions to pin or follow

- **Layout 2 one-prod-one-test-sibling.** Both projects land in the same PR; the `CritterBids.slnx` edit adds both nodes in the same commit as the `.csproj` files.
- **Module shape per `adding-bc-module.md`.** `services.ConfigureMarten()` inside a static `AddSettlementModule` extension returning `IServiceCollection`. No `AddMarten()` call inside the module (the primary store is owned by `Program.cs` per ADR 009).
- **Schema name `settlement`.** Per `opts.Schema.For<SettlementSaga>().DatabaseSchemaName("settlement")` inside the `ConfigureMarten` callback. The primary store's `DatabaseSchemaName = "public"` remains the default; per-type schema overrides are the isolation mechanism.
- **Saga registration shape per AuctionClosingSaga.** `Schema.For<SettlementSaga>().DatabaseSchemaName("settlement").Identity(x => x.Id).UseNumericRevisions(true)`. `UseNumericRevisions(true)` provides optimistic concurrency for saga document writes; ADR-019 §Decision and the AuctionClosingSaga precedent both pin this. No projection registration on the saga document.
- **`Program.cs` edit order matches module-add order.** Settlement registrations come after Auctions and before the ASP.NET / Wolverine HTTP block, preserving the existing Participants → Selling → Listings → Auctions → Settlement sequence for both `IncludeAssembly` and `Add*Module` calls.
- **`slnx` alphabetical-by-BC-name rule.** Per `adding-bc-module.md` §Project Structure. Settlement's `<Project>` entries land alphabetically after Selling under both `/src/` and `/tests/` folder nodes. No reshuffling of existing nodes.
- **UUID v5 deterministic SettlementId is a S4 convention.** S2's saga shell uses `Guid` for `Id` without committing to a derivation; S4's `InitiateSettlement` handler is where `UuidV5(AuctionsNamespace, $"settlement:{ListingId}")` cashes in per W003 Phase 1 Part 6.
- **`[AllowAnonymous]` posture unchanged.** Settlement has no HTTP endpoints in S2. If S6 introduces any (unlikely; Settlement is backend-only per M5 §3), `[AllowAnonymous]` applies through M5 per `CLAUDE.md`'s M1-through-M6 stance.
- **No "Event" suffix on domain event type names** — applies to S4 onwards when the Settlement-internal events land. S2 authors no events.
- **Fixture exclusion class naming.** `{TargetBc}BcDiscoveryExclusion` per the `critter-stack-testing-patterns.md` §Naming convention rule. Three classes in the Settlement fixture: `SellingBcDiscoveryExclusion`, `AuctionsBcDiscoveryExclusion`, `ListingsBcDiscoveryExclusion`.

---

## Acceptance criteria

- [ ] `src/CritterBids.Settlement/CritterBids.Settlement.csproj` exists; `WolverineFx.Http.Marten` package reference matches sibling BC projects; **no `<ProjectReference>` to `CritterBids.Contracts`** (deferred to S3).
- [ ] `tests/CritterBids.Settlement.Tests/CritterBids.Settlement.Tests.csproj` exists; references `CritterBids.Settlement` and `CritterBids.Api`; package references (`Alba`, `Microsoft.NET.Test.Sdk`, `Testcontainers.PostgreSql`, `xunit`, `xunit.runner.visualstudio`, `Shouldly`) match sibling test projects.
- [ ] `CritterBids.slnx` contains `<Project>` entries for both new projects under the `/src/` and `/tests/` folder nodes respectively; both inserted alphabetically after their Selling siblings.
- [ ] `src/CritterBids.Settlement/SettlementModule.cs` defines `AddSettlementModule(this IServiceCollection services) : IServiceCollection` calling `services.ConfigureMarten(...)` with the `settlement` schema mapping for `SettlementSaga` and `Identity(x => x.Id).UseNumericRevisions(true)` saga registration; nothing else.
- [ ] `src/CritterBids.Settlement/SettlementSaga.cs` defines `public sealed class SettlementSaga : Wolverine.Saga` with a single `public Guid Id { get; set; }` property and a single forward-looking comment naming what S4 / S5 add. No state fields beyond `Id`, no `Handle` methods, no `MarkCompleted()` calls.
- [ ] `SettlementModule.cs` contains zero `opts.Events.AddEventType<T>()` calls and zero `opts.Projections.*` calls.
- [ ] `src/CritterBids.Api/CritterBids.Api.csproj` has a `<ProjectReference>` to `CritterBids.Settlement.csproj`.
- [ ] `src/CritterBids.Api/Program.cs` — `using CritterBids.Settlement;` present; `opts.Discovery.IncludeAssembly(typeof(SettlementSaga).Assembly);` present after the existing `IncludeAssembly` line for `Listing`; `builder.Services.AddSettlementModule();` present after `AddAuctionsModule();`.
- [ ] `Program.cs` `opts.PublishMessage` / `opts.ListenToRabbitQueue` blocks contain zero Settlement-related lines.
- [ ] `tests/CritterBids.Settlement.Tests/Fixtures/SettlementTestFixture.cs` and `SettlementTestCollection.cs` exist; the fixture mirrors `AuctionsTestFixture` with three concrete differences (container name prefix, `AddSettlementModule()` call, three discovery exclusion classes for Selling / Auctions / Listings).
- [ ] Three `IWolverineExtension` exclusion classes present in the Settlement fixture: `SellingBcDiscoveryExclusion`, `AuctionsBcDiscoveryExclusion`, `ListingsBcDiscoveryExclusion`. Each excludes types under its respective `CritterBids.{TargetBc}` namespace via `Discovery.CustomizeHandlerDiscovery`.
- [ ] At least one test in `CritterBids.Settlement.Tests` (`SettlementModuleTests.SettlementModule_BootsClean`) asserts the Settlement module registers cleanly alongside the primary Marten store and `IDocumentStore` is resolvable from DI.
- [ ] `dotnet build` — 0 errors, 0 warnings.
- [ ] `dotnet test` — all green; baseline test count from the M5-S1 close still pass; the new smoke test passes.
- [ ] `docs/retrospectives/M5-S2-settlement-bc-scaffold-retrospective.md` exists; mirrors the M3-S2 retrospective shape; records the solution-layout delta, the `Program.cs` edit diff, the smoke-test shape chosen, any skill gap discovered, and a one-paragraph "what M5-S3 should know" note.

---

## Open questions

- **Saga document concurrency on a never-written aggregate.** `Schema.For<SettlementSaga>().UseNumericRevisions(true)` registers numeric-revision optimistic concurrency for an aggregate type that no slice yet writes to. Marten 8's saga-store registration path is documented to tolerate this — the schema is created at `ApplyAllDatabaseChangesOnStartup()` time without requiring a write. The AuctionClosingSaga precedent confirms the shape. If a Marten 8 change surfaces an error on registering a never-written saga aggregate, it is a genuine blocker — stop and flag rather than dropping the `UseNumericRevisions` line. S4 needs the saga registered with concurrency control and will not want to discover a hole here.
- **Cross-BC discovery exclusion completeness.** The S2 fixture excludes Selling, Auctions, Listings. Participants stays included (per the AuctionsTestFixture / ListingsTestFixture precedent — Participants handlers don't trip on missing services in foreign-BC fixtures). If the smoke test surfaces a Participants-handler code-gen failure, add a `ParticipantsBcDiscoveryExclusion` and document it in the retrospective. The expected outcome at S2 is that three exclusions suffice; a fourth is an unexpected discovery worth recording.
- **`Wolverine.Saga` namespace import.** The `SettlementSaga : Wolverine.Saga` declaration may resolve via either an explicit `using Wolverine;` import or the fully-qualified `Wolverine.Saga` base type. The AuctionClosingSaga precedent uses the fully-qualified form. Match that convention to keep the file's using list narrow at the scaffold stage.

---

## Commit sequence

Three commits, in this order:

1. `feat(settlement): scaffold CritterBids.Settlement project with SettlementModule and empty SettlementSaga shell`
2. `feat(settlement): scaffold CritterBids.Settlement.Tests with boot-green smoke test; wire Settlement into Api Program.cs and .slnx`
3. `docs: write M5-S2 retrospective`

The scaffold and wiring are split across commits 1 and 2 so that a reviewer reading commit 1 sees a self-contained new project and commit 2 sees the integration diff (`Program.cs` + `.csproj` + `.slnx` + tests + fixture). The smoke test lives in commit 2 because it only makes sense once `Program.cs` references the module. Commit 3 is the retrospective and lands after the green build.
