# M7-S2: Operations BC Scaffold + First Consumer (Settlement Queue)

**Milestone:** M7 ([Operations BC](../../milestones/M7-operations-bc.md))
**Slice:** S2 of 7 (BC scaffold + first cross-BC consumer)
**Narrative:** `none` — the settlement queue is not a narrative-008 surface (narrative 008 dramatises the dispute/escalation queue, which lands in S4). S2's spec authority is the W006 source-audit field freeze, not a narrative.
**Agent:** @PSA
**Estimated scope:** one PR; ~11–14 files added (Operations source ×5–6: module, `SettlementQueueView`, status enum, the Settlement-family handler, `AssemblyInfo`, csproj; test project ×4–5: csproj, fixture, global-usings/`AssemblyInfo`, the projection test; plus this prompt and the retro), ~5–9 files modified (`Program.cs`, `CritterBids.Api.csproj`, `CritterBids.slnx`, and the existing BC test fixtures that need an `OperationsBcDiscoveryExclusion` — exact set per the Open Question)

---

## Goal

Stand up the `CritterBids.Operations` and `CritterBids.Operations.Tests` projects — the eighth and final MVP BC, and the only one that has never existed in `src/` — wire them into the solution, the Api host, and the Wolverine-Marten configuration, and land the **first behavior**: the **settlement queue** read model. Operations is a **pure consumer** (ADR 014 Path A): it listens to the Settlement BC's integration events off RabbitMQ and folds them into a `SettlementId`-keyed upsert view, with `PaymentFailed` flagged for staff attention. The slice is proven **end-to-end** by a Testcontainers projection test that drives the full `Failed → Completed → PaidOut` status lifecycle, including the status-preservation guard.

M7-S1 closed the foundation decisions this slice executes against: **ADR-024** (the staff-auth scheme — *not applied here*), the **OpenSpec decline** for Operations (so this slice carries **no `/opsx:` tasks** and works under ADR 020 alone), and **W006** — the read-model field freeze. S2 walks in with the settlement queue's field shape already locked by W006 §1 and every field already traced to a real `CritterBids.Contracts.Settlement` payload. If a new design decision surfaces mid-session, stop and flag — do not pivot in-session.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M7-operations-bc.md` | Authoritative for scope: §4 Solution Layout (the two new projects + representative files), §5 Infrastructure (Marten `operations` schema via `services.ConfigureMarten()`, no direct `AddMarten()`; the `operations-settlement-events` listen + `SettlementCompleted`/`SellerPayoutIssued` publish routes; `AutoProvision`; documents-only — no sagas/aggregates), §6 Conventions Pinned, §7 slice table (the **S2 row** — the scope ceiling). Do not re-decide anything the milestone pinned. |
| `docs/workshops/006-operations-source-audit.md` | The **frozen spec**. §1 "Settlement queue — upsert, key `SettlementId`" is exactly what S2 builds: the source events, the full field table, the Status-derivation rule (`PaymentFailed`→`Failed`, `SettlementCompleted`→`Completed`, `SellerPayoutIssued`→`PaidOut`), and the status-preservation guard. This is the binding read-model spec — implement these fields, no more, no fewer. |
| `docs/decisions/014-cross-bc-read-model-extension-shape.md` | ADR 014 **Path A** — sibling-handler-per-source-BC tolerant upsert with status-preservation guards. The settlement queue is one Settlement-family handler. **No** Marten multi-stream event projections (the inbound firehose is not appended to local Operations streams — milestone §3 non-goal). |
| `docs/decisions/024-staff-auth-posture-resumption.md` | The auth posture S2 must **not** disturb. S2 applies **no** `[Authorize]` and no `StaffOnly` (that is S6) — but it must not break the current bare-`AddAuthentication()` / no-default-scheme state. Operations has no HTTP endpoints in S2. |
| `CLAUDE.md` | Routing + global conventions (`sealed record`, `IReadOnlyList<T>`, handlers return events/messages, no "Event" suffix, no "paddle"; the canonical bootstrap sequence; `AddXyzModule()` pattern; no BC-to-BC project refs; integration events via Contracts; `AutoApplyTransactions` lives in `Program.cs`) and the `[AllowAnonymous]`-through-M6 posture that S6 — not S2 — lifts. |
| Skill files: `docs/skills/adding-bc-module/SKILL.md`, `docs/skills/marten-projections/SKILL.md`, `docs/skills/integration-messaging/SKILL.md`, `docs/skills/critter-stack-testing-patterns/SKILL.md` | The *how*: module shape (`ConfigureMarten` inside `AddOperationsModule`, no `AddMarten`); tolerant-upsert + status-preservation-guard projection mechanics; the RabbitMQ listen/publish route shape; and §"Cross-BC Handler Isolation" (`{TargetBc}BcDiscoveryExclusion` + `DisableAllExternalWolverineTransports()`) plus Testcontainers projection testing. Prompts point; the skills own the rules. |
| `src/CritterBids.Settlement/` (`SettlementModule.cs`, `AssemblyInfo.cs`, csproj) + `src/CritterBids.Listings/` (`SettlementStatusHandler.cs` and the sibling Path A handlers) + `src/CritterBids.Contracts/Settlement/` (`PaymentFailed`, `SettlementCompleted`, `SellerPayoutIssued`) | Structural templates: Settlement for the module/project/`InternalsVisibleTo` scaffold; the Listings catalog siblings for the lived Path A tolerant-upsert consumer + guard shape; the three Settlement contracts for the authoritative field source W006 §1 traces to. |

## In scope

1. **`src/CritterBids.Operations` class library** — `WolverineFx.Http.Marten` package reference matching sibling Marten BCs; `<ProjectReference>` to `CritterBids.Contracts` (the handler consumes `CritterBids.Contracts.Settlement.*`); `AssemblyInfo.cs` with `InternalsVisibleTo("CritterBids.Operations.Tests")`. Added to `CritterBids.slnx` under `/src/`, alphabetical (after `CritterBids.Obligations`, before `CritterBids.Participants`).
2. **`SettlementQueueView`** — `sealed record` Marten document keyed by `SettlementId`, carrying exactly the W006 §1 field set (`SettlementId`, `ListingId`, `WinnerId`, `SellerId?`, `HammerPrice?`, `FeeAmount?`, `SellerPayout?`, `PayoutAmount?`, `FeeDeducted?`, `FailureReason?`, `Status`, `LastUpdatedAt`). A `Status` enum with the W006 members (`Failed`, `Completed`, `PaidOut`).
3. **Settlement-family consumer handler** — one Path A sibling handler class consuming `PaymentFailed`, `SettlementCompleted`, and `SellerPayoutIssued`, each a tolerant upsert (load-or-construct by `SettlementId`, mutate via record `with`, store). `Status` is derived from which event arrived per W006 §1; the `PaymentFailed` arrival sets `Status = Failed` and `FailureReason` (staff-attention flag). `SellerPayoutIssued` carries only `SettlementId`/`SellerId`/`PayoutAmount`/`FeeDeducted`/`IssuedAt` — it must **not** invent `ListingId`/`WinnerId` (it preserves whatever a prior `PaymentFailed`/`SettlementCompleted` already set, populating only the payout fields and advancing `Status` to `PaidOut`). If `SellerPayoutIssued` is the first event to arrive for a `SettlementId`, the constructed minimal row leaves the set-once fields unset; record that first-arrival behavior in the retro. The handler returns `void`/`Task` and writes via the injected Marten session — **no** `OutgoingMessages`, **no** `IMessageBus` (Operations publishes nothing).
4. **Status-preservation guard** — `PaidOut` must not regress to `Completed` if `SettlementCompleted` is re-delivered after `SellerPayoutIssued` (W006 §1). Set-once fields (`ListingId`, `WinnerId`) follow the W006 guards.
5. **`AddOperationsModule()`** — contributes the `SettlementQueueView` document type (and any registrations) via `services.ConfigureMarten()` in the dedicated `operations` schema. **No** `AddMarten()` call inside the module — the host owns the single one. Registers no sagas and no event-sourced aggregates (documents only).
6. **`Program.cs` wiring (routing-only)** — `using CritterBids.Operations;`, `opts.Discovery.IncludeAssembly(...)` for Operations, `builder.Services.AddOperationsModule()`, the `operations-settlement-events` `ListenToRabbitQueue()`, and the `SettlementCompleted` / `SellerPayoutIssued` publish-route additions (publish for `PaymentFailed` is pre-wired since M5-S6); `AutoProvision()` declares the new queue. `<ProjectReference>` from `CritterBids.Api.csproj`. **No upstream BC code change** — these are routing additions to existing message types.
7. **Cross-BC discovery exclusions** — add an `OperationsBcDiscoveryExclusion` to each existing BC test fixture whose in-process host would otherwise discover the Operations settlement handler (the `SettlementCompleted` overlap with Listings/Settlement at minimum), following the `{TargetBc}BcDiscoveryExclusion` pattern. Exact set per the Open Question.
8. **`CritterBids.Operations.Tests`** — xUnit + Shouldly + Testcontainers + Alba; fixture excluding the foreign BCs and applying `DisableAllExternalWolverineTransports()`; a boots-clean test; and the **end-to-end settlement-queue projection test** against a real Postgres exercising the consume → upsert path across the full `Failed → Completed → PaidOut` lifecycle, asserting field population per W006 §1 and the `PaidOut`-does-not-regress guard on a re-delivered `SettlementCompleted`.

## Explicitly out of scope

- **Lot board / bid-activity feed** (`operations-auctions-events`, `operations-selling-events`) — **S3**.
- **`OperationsObligationsView`** (escalation + dispute queues, `operations-obligations-events`) — **S4** (narrative 008's surface).
- **Session & participant activity board** (`operations-participants-events`, the session events) — **S5**.
- **All auth work** — the `StaffToken` scheme registration, the `StaffOnly` policy, any `[Authorize]`/`[AllowAnonymous]` attribute, and any staff query endpoint — **S6**. S2 adds **no** HTTP endpoint and **no** authorization attribute, and must not register the auth scheme. The no-default-scheme state stays as-is (ADR-024 records the fix lands in S6).
- **Staff query endpoints over the settlement queue** — even read-only — **S6**. S2 proves the view via the projection test, not an HTTP surface.
- **End-to-end cross-BC journey test, `Program.cs` route audit, `bounded-contexts.md` status flip** — **S7**.
- **OpenSpec / `/opsx:` anything** — Operations declined OpenSpec at S1; there is no `openspec/changes/<operations-slug>/` folder and none is created here. Do not edit OpenSpec-managed files under `.github/prompts/` or `.github/skills/`.
- **Any upstream BC behavior change** — no Settlement / Listings / Auctions / Selling / Obligations / Participants code edits beyond `Program.cs` route additions. No new `CritterBids.Contracts.*` type (Operations is a pure consumer).
- **SignalR / `OperationsHub`** — the hub stays in Relay (ADR 023); no hub edit this slice.
- **Skill-file edits.** If S2 surfaces a skill gap, record it in the retro — do not edit in-session (AUTHORING rule 4).

## Conventions to pin or follow

- **Read-model strategy — ADR 014 Path A.** One view per logical entity, one sibling handler per source BC, tolerant upsert, status-preservation guards. `docs/skills/marten-projections/SKILL.md` owns the mechanics; the prompt points, it does not restate.
- **Operations is a pure consumer.** Consumer handlers return `void`/`Task` and write via the injected Marten session — they do **not** return `OutgoingMessages` and make **no** `IMessageBus` calls. `bounded-contexts.md` §Operations "Integration out: None" is enforced structurally.
- **No-default-scheme constraint.** S2 introduces no authorization attribute and registers no auth scheme; it leaves `Program.cs`'s auth state untouched so nothing 500s and nothing is silently gated (ADR-024: auth lands whole in S6).
- **Module shape** per `docs/skills/adding-bc-module/SKILL.md`: `services.ConfigureMarten()` inside `AddOperationsModule()`; no `AddMarten()` inside the module; `operations` schema.
- **Canonical bootstrap + `AutoApplyTransactions`** stay in `Program.cs` (CLAUDE.md); the module only contributes types and routes.
- **Cross-BC fixture exclusion naming:** `OperationsBcDiscoveryExclusion` per `critter-stack-testing-patterns/SKILL.md`.
- `sealed record`; `IReadOnlyList<T>` for any collection; no "Event" suffix on type names; `WinnerId`/`BidderId` never "paddle".

## Spec delta

Per ADR 020:

- W006 §1 (the settlement-queue field freeze) gains its first runnable, test-backed implementation: `SettlementQueueView` plus its Settlement-family Path A handler.
- The Settlement contracts `PaymentFailed` / `SettlementCompleted` / `SellerPayoutIssued` gain consume→upsert coverage, with `Status` derived `Failed`/`Completed`/`PaidOut` and the `PaidOut`-preservation guard proven.
- The other four W006 views (lot board, bid-activity feed, `OperationsObligationsView`, session/participant board), all auth behavior, and any query endpoint remain unimplemented (S3–S6); no contract type is added.

(No narrative or workshop Document-History row is owed — W006 is a freeze, not a behavior narrative, and the settlement queue anchors to no narrative Moment.) The retro's `## Spec delta — landed?` paragraph confirms the settlement queue is seeded end-to-end against real Postgres and the W006 §1 fields/guard are exercised.

## Acceptance criteria

- [ ] `src/CritterBids.Operations/CritterBids.Operations.csproj` exists; `WolverineFx.Http.Marten` reference matches sibling Marten BCs; `<ProjectReference>` to `CritterBids.Contracts` present; `AssemblyInfo.cs` exposes internals to `CritterBids.Operations.Tests`.
- [ ] `SettlementQueueView` is a `sealed record` keyed by `SettlementId` carrying exactly the W006 §1 field set; the `Status` enum has `Failed`/`Completed`/`PaidOut`.
- [ ] One Settlement-family Path A handler consumes `PaymentFailed`, `SettlementCompleted`, `SellerPayoutIssued`; each is a tolerant upsert; `Status` is derived per W006 §1; `PaymentFailed` sets `FailureReason` and flags `Failed`; the handler returns no `OutgoingMessages` and makes no `IMessageBus` call.
- [ ] The `PaidOut`-does-not-regress-to-`Completed` guard is implemented and asserted by a test re-delivering `SettlementCompleted` after `SellerPayoutIssued`.
- [ ] `AddOperationsModule()` registers `SettlementQueueView` via `ConfigureMarten` in the `operations` schema; no `AddMarten()` call inside the module; no saga or aggregate registered.
- [ ] `Program.cs` has the `using`, the `Discovery.IncludeAssembly`, `AddOperationsModule()`, the `operations-settlement-events` `ListenToRabbitQueue()`, the `SettlementCompleted`/`SellerPayoutIssued` publish routes, and `AutoProvision()` covering the new queue; `CritterBids.Api.csproj` references the project; `CritterBids.slnx` carries both new project nodes.
- [ ] No `[Authorize]`/`StaffOnly`/auth-scheme registration anywhere in the slice; `Program.cs` auth state is otherwise unchanged.
- [ ] Each existing BC test fixture that would discover the Operations settlement handler registers an `OperationsBcDiscoveryExclusion`; no cross-BC handler leakage.
- [ ] `CritterBids.Operations.Tests` contains a boots-clean test and the end-to-end settlement-queue projection test (real Postgres via Testcontainers) covering the full `Failed → Completed → PaidOut` lifecycle and the regression guard — all green.
- [ ] `dotnet build` passes (0 errors, 0 warnings); full `dotnet test` green with no regressions across any BC.
- [ ] No new `CritterBids.Contracts.*` type introduced.
- [ ] `docs/retrospectives/M7-S2-operations-bc-scaffold-first-consumer-retrospective.md` written with the `**Prompt:**` header and the `## Spec delta — landed?` paragraph.
- [ ] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer.

## Open questions

- **Discovery-exclusion fixture set.** Which existing BC test fixtures need an `OperationsBcDiscoveryExclusion` is determined by which in-process hosts would discover the Operations settlement handler (the `SettlementCompleted` overlap reaches Listings and Settlement at minimum; the M6-S2 precedent excluded four fixtures for the Obligations scaffold). Apply the exclusion to every fixture whose host discovery surfaces the leak, verified by a green full-suite run; do not under- or over-apply by guesswork — let a red test name the set.
- **Settlement outbound publish routes.** Confirm whether `operations-settlement-events` pairs with publish routes that already exist for `SettlementCompleted`/`SellerPayoutIssued` (to `relay-*`/`listings-*` consumers) versus needing new `operations-*` publish-route additions. Add only the `operations-*` routing the settlement queue needs; do not alter Settlement-side publish wiring beyond the route topology M5 established.
- **`SettlementQueueView` storage shape.** W006 §1 freezes the fields, not the storage mechanics; the expected shape is **one** `SettlementId`-keyed upsert document in the `operations` schema. Marten indexing (e.g. on the failure flag) is fine if a query needs it, but introducing any **additional document or view** is out of S2 scope — stop and flag rather than splitting the view. Record any indexing choice in the retro.
- **Status enum home and member spelling.** W006 §1 fixes the three derived values (`Failed`/`Completed`/`PaidOut`); the enum's namespace/home is an implementation choice. Keep it Operations-internal (no Contracts type).
