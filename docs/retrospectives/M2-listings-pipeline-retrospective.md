# M2 — Listings Pipeline — Milestone Retrospective

**Date:** 2026-04-15
**Milestone:** M2 — Listings Pipeline
**Sessions:** S1–S8 (8 sessions)
**Author:** Claude (PSA mode, explanatory output style)

---

## Exit Criteria Status

Walk of each criterion from `docs/milestones/M2-listings-pipeline.md` §1:

| Exit criterion | Status |
|---|---|
| Solution builds clean with `dotnet build` — 0 errors, 0 warnings | ✅ |
| Selling BC: `CreateDraftListing`, `SubmitListing` (3-event chain), `ListingValidator` (14 rules), `RegisteredSellers` projection, `ISellerRegistrationService` seam | ✅ |
| Listings BC: `CatalogListingView` projection, `GET /api/listings`, `GET /api/listings/{id}` | ✅ |
| `SellerRegistrationCompleted` routed via RabbitMQ (replaces M1 local queue rule) | ✅ |
| `CritterBids.Contracts.Selling.ListingPublished` authored — first Selling BC integration contract | ✅ |
| Cross-BC pipeline verified end-to-end: `SellerRegistrationCompleted` → `RegisteredSellers`; `ListingPublished` → `CatalogListingView` | ✅ |
| All acceptance tests pass | ✅ 42 passing |
| Marten named stores ADR authored (`docs/decisions/008-marten-bc-isolation.md`) | ✅ Authored and superseded by ADR 009 within M2 |
| `docs/skills/adding-bc-module.md` authored | ✅ |
| `docs/skills/domain-event-conventions.md` authored | ✅ Authored this session (M2-S8) |
| M2 retrospective doc written | ✅ This document |

---

## Session-by-Session Summary

| Session | Scope | Outcome | Notable deviations |
|---|---|---|---|
| S1 | Marten BC isolation ADR (ADR 008 — named stores per BC) | ✅ Docs only | ADR 008 superseded by ADR 009 in the very next session |
| S2 | Selling BC scaffold — `AddMartenStore<T>()`, smoke test | ✅ | `WolverineFx.Marten` overload ambiguity required explicit `(StoreOptions opts)` annotation; two-container fixture (postgres + SQL Server) required; `ConfigureAppConfiguration` timing constraint surfaced |
| postS2 | ADR 002 numbering correction; `RegisteredSellers` handler + `ISellerRegistrationService`; Program.cs RabbitMQ routing | ✅ | Unscheduled session; three-digit ADR naming adopted; `adding-bc-module.md` authored |
| S3 | Wolverine dual-store conflict investigation; ADR 010 | ✅ Docs only | Fix deferred (Option C); Aspire startup blocked; Polecat has no ancillary-store API |
| S4 | Architecture pivot — All-Marten (ADR 011); CLAUDE.md + skill doc refresh; M2-S5 prompt | ✅ Docs only | Session count 7→8; S5–S8 renumbered; `polecat-event-sourcing.md` archived |
| S5 | `CreateDraftListing` + `ListingValidator` (14 rules) + `POST /api/listings/draft` | ✅ | Participants BC migration performed out of scope — resolved ADR 010 crash as side effect |
| S6 | `SubmitListing` (3-event chain) + `ListingPublished` integration contract + package upgrades | ✅ | RabbitMQ routing in `Program.cs` not `AddSellingModule()` (no `WolverineOptions` access in module) |
| S7 | Listings BC scaffold + `CatalogListingView` + `GET /api/listings` + `GET /api/listings/{id}` + 4 integration tests | ✅ | `CritterBids.Api.csproj` project reference required but not documented (build failure); `IAlbaHost.GetAsJson<T>()` does not exist in Alba 8.5.2 |
| S8 | `domain-event-conventions.md`; `adding-bc-module.md` updates; M2 milestone retro; `CURRENT-CYCLE.md` | ✅ Docs only | — |

---

## Cross-BC Integration Map

Both integrations established in M2 are verified by integration tests:

```
Participants ──► SellerRegistrationCompleted ──► Selling   (RegisteredSellers projection)  ✅
             (queue: selling-participants-events)

Selling      ──► ListingPublished            ──► Listings  (CatalogListingView projection)  ✅
             (queue: listings-selling-events)
```

Both are Wolverine message handlers consuming from RabbitMQ queues. Neither uses Marten async
projections — the read model data lives in Marten documents, but the trigger is a Wolverine
message arriving from the queue. `ListenToRabbitQueue()` and `PublishMessage()` routing rules
live in `Program.cs` for both integrations (BC modules lack `WolverineOptions` access —
see Technical Debt below).

---

## Test Count at M2 Close

| Project | Count | Type |
|---|---|---|
| `CritterBids.Selling.Tests` | 30 | Mixed (integration + pure-function) |
| `CritterBids.Listings.Tests` | 4 | Integration |
| Existing (`Participants`, `Api`, `Contracts`) | 8 | Integration |
| **Total** | **42** | |

Selling BC breakdown: 14 pure-function validator tests + 5 `DraftListing` aggregate tests +
4 `SubmitListing` aggregate tests + 2 `CreateDraftListing` API gateway tests +
4 `RegisteredSellers` projection tests + 1 smoke test = 30.

---

## Key Decisions Made in M2

| ADR | Decision |
|---|---|
| [008](../decisions/008-marten-bc-isolation.md) | Named Marten stores per BC via `AddMartenStore<T>()` — one `mt_events` table per BC. Superseded by ADR 009 when the ancillary-store API was found to omit critical Wolverine registrations. |
| [009](../decisions/009-shared-marten-store.md) | Single shared primary `IDocumentStore` in `Program.cs`; each BC contributes via `services.ConfigureMarten()` inside `AddXyzModule()`. Current and accepted. |
| [010](../decisions/010-wolverine-dual-store-resolution.md) | Wolverine dual-store conflict (Marten + Polecat both claiming "main" store) documented; Polecat has no ancillary-store API; fix deferred pending JasperFx input. Resolved by ADR 011 (scenario eliminated). |
| [011](../decisions/011-all-marten-pivot.md) | All-Marten Pivot — all 8 BCs migrate from any Polecat/SQL Server use to Marten/PostgreSQL; eliminates dual-store conflict; uniform bootstrap pattern across all BCs. Supersedes ADR 003. |

---

## Key Learnings — Cross-Session Patterns

These patterns are generalizable across milestones and future BCs. Session-local learnings are
in individual session retros.

1. **Three rapid ADR pivots (008→009→011) signal a design space that wasn't fully explored before
   the milestone started.** S1 (ADR 008), S3 (ADR 010), and S4 (ADR 011) were three consecutive
   documentation sessions forced by architecture uncertainty. The root cause was that the storage
   topology (named stores vs shared store vs All-Marten) was unresolved at M2 start. Future
   milestones introducing new storage patterns should resolve topology ADRs before the first
   scaffolding session.

2. **Slim domain events / rich integration contracts is an explicit discipline, not a natural
   default.** The instinct is to put all useful data in the domain event. The pattern — slim event
   in the stream, rich contract in `Contracts/` — prevents downstream BCs from coupling to
   aggregate internals and keeps event streams compact. Document the contract's consumer table
   before committing the first integration event (per `integration-messaging.md` L2).

3. **`AddEventType<T>()` registration must happen at the same commit as the event itself.** The
   failure mode (`AggregateStreamAsync<T>` silently returning `null`) is discovered at test time,
   not startup. Treating event registration as a per-BC checklist item eliminates this entire
   category of runtime surprise.

4. **`CritterBids.Api.csproj` needs a project reference to every new BC.** When `Program.cs` uses
   `typeof(SomeNewBcType)`, the Api project must have a `<ProjectReference>` to the new BC's
   `.csproj`. This is implicit and was discovered as build failure `CS0234` in S7. It is now in the
   `adding-bc-module.md` checklist.

5. **Cross-BC handler isolation in test fixtures is asymmetric.** The deciding factor is whether
   a discovered handler has unresolvable DI dependencies — not whether the handler belongs to a
   different BC. `ListingPublishedHandler` injects only `IDocumentSession` (always present in any
   Marten fixture); it does not need to be excluded from Selling fixtures. `CreateDraftListingHandler`
   injects `ISellerRegistrationService` (only present when Selling module is registered); it must
   be excluded from Listings fixtures.

6. **Out-of-scope deviations in 2 of 4 implementation sessions.** S5 performed the Participants BC
   migration (out of scope, resolved the Aspire startup crash as a side effect). S6 performed
   package upgrades. Both were net-positive but blurred the PR boundaries. Prompts should either
   explicitly include known preconditions as scope items or add a "permitted tactical work" note
   to contain the blast radius.

7. **`IAlbaHost.GetAsJson<T>()` does not exist in Alba 8.5.2.** Prompts that reference this method
   cause test implementations to fail with a compiler error. The correct pattern is
   `Host.Scenario(...) + result.ReadAsJsonAsync<T>()`. This is now documented in both
   `adding-bc-module.md` and in each session retro that encountered it.

---

## Technical Debt and Deferred Items

| Item | Deferred in | Target |
|---|---|---|
| `[WriteAggregate]` stream-ID convention for `SubmitListing` — `ListingId` vs Wolverine's convention expecting `SellerListingId` | S6 | When `POST /api/listings/submit` HTTP endpoint is added |
| RabbitMQ routing in BC modules (vs `Program.cs`) — `AddSellingModule()` has no `WolverineOptions` access | S5, S6 | Deferred — requires threading `WolverineOptions` into module extension methods |
| `ListingFormat` enum placement — currently in `CritterBids.Selling`; integration contract uses `string Format` | S6 | Documented as the correct pattern in `domain-event-conventions.md` §8 — no code change needed |
| UUID v7 ADR 007 promotion gates — Marten 8 / Polecat 2 capability check + JasperFx team input | M1, M2 | M3 re-evaluation (Auctions BC — high-write motivation for v7 insert locality) |
| Named Polecat stores — deferred while only one Polecat BC (Participants) existed; Participants now migrated to Marten | M1, M2 | No longer applicable — ADR 011 eliminates Polecat from CritterBids |
| `UpdateDraftListing` HTTP endpoint — handler exists and is tested; `PATCH /api/listings/draft/{id}` deferred | S5 | Future Selling BC endpoint session |

---

## What M3 Should Know

At M2 close the solution has 42 tests passing across 5 test projects (Participants, Selling,
Listings, Api, Contracts). Three production BCs are implemented: Participants (event-sourced
aggregate), Selling (event-sourced aggregate + state machine), and Listings (Marten document +
read endpoints). Two integration event flows are live end-to-end: `SellerRegistrationCompleted`
(Participants→Selling, `RegisteredSellers` projection) and `ListingPublished` (Selling→Listings,
`CatalogListingView` projection). The most significant known fragility is the `[WriteAggregate]`
stream-ID naming convention for `SubmitListing`: the handler uses `ListingId` as the command's
stream-ID parameter, but Wolverine's convention for aggregate type `SellerListing` would look for
`SellerListingId` — this has never been exercised through an HTTP endpoint and must be verified
when the endpoint is added. M3 inherits the Auctions BC as its primary deliverable, which introduces
the Dynamic Consistency Boundary (DCB) pattern and the first saga.
