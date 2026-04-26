# M2 — Listings Pipeline

**Status:** Active
**Scope:** Tier 1 foundation — first cross-BC integration, Selling BC + Listings BC
**Companion docs:** [`../workshops/001-flash-session-demo-day-journey.md`](../workshops/001-flash-session-demo-day-journey.md) Tier 1 · [`../workshops/001-scenarios.md`](../workshops/001-scenarios.md) slices 1.3, 1.4 · [`../workshops/004-selling-bc-deep-dive.md`](../workshops/004-selling-bc-deep-dive.md) · [`../workshops/004-scenarios.md`](../workshops/004-scenarios.md) · [`../skills/README.md`](../skills/README.md) · [`../workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md)

---

## 1. Goal & Exit Criteria

### Goal

Extend the M1 skeleton into a functional listings pipeline. A registered seller can create a draft listing, configure it, submit it for publication, and any participant can browse published listings via the catalog API. This milestone delivers the first cross-BC integration: Participants → Selling → Listings. At M2 close, a contributor can exercise the full pipeline: start a session → register as seller → create a draft → submit → browse the catalog.

### Exit criteria

- [ ] Solution builds clean with `dotnet build` — 0 errors, 0 warnings
- [ ] Selling BC implemented: `CreateDraftListing`, `SubmitListing` (3-event chain), `ListingValidator` (14 pure-function rules), `RegisteredSellers` projection, `ISellerRegistrationService` module seam
- [ ] Listings BC implemented: `CatalogListingView` projection, `GET /api/listings`, `GET /api/listings/{id}`
- [ ] `SellerRegistrationCompleted` now routed via RabbitMQ (replaces M1 local queue rule in `Program.cs`)
- [ ] `CritterBids.Contracts.Selling.ListingPublished` authored — first Selling BC integration contract
- [ ] Cross-BC pipeline verified end-to-end: `SellerRegistrationCompleted` → `RegisteredSellers` projection; `ListingPublished` → `CatalogListingView` projection
- [ ] All acceptance tests pass (see §7)
- [ ] Marten named stores ADR authored (`docs/decisions/008-marten-bc-isolation.md`)
- [ ] `docs/skills/adding-bc-module.md` authored (retrospectively from S2–S3)
- [ ] `docs/skills/domain-event-conventions.md` authored (retrospectively from S4–S5)
- [ ] M2 retrospective doc written

---

## 2. In Scope

### Prerequisite (M1 carryover obligation)

Before any Tier 1 slice can be tested end-to-end, the `SellerRegistrationCompleted` plumbing must be promoted from its M1 local-queue placeholder to real RabbitMQ routing. This is not a slice — it is a cleanup obligation inherited from M1 and is addressed in S3.

### Tier 1 slices

| Slice | Name | Scenarios |
|---|---|---|
| Pre-req | `SellerRegistrationCompleted` consumer → `RegisteredSellers` projection | `004-scenarios.md` §6 |
| 1.1 | Create draft listing | `004-scenarios.md` §1 + §5 (validator) |
| 1.2 | Submit and publish listing | `004-scenarios.md` §2 |
| 1.3 | Catalog browse (read path) | `001-scenarios.md` §1.3 |
| 1.4 | Listing detail (read path) | `001-scenarios.md` §1.4 |

Slice 1.1 encompasses all draft-lifecycle scenarios from `004-scenarios.md` §1 (including the seller-not-registered rejection path). The `ListingValidator` pure-function test suite (§5) is authored alongside 1.1 — the validator is exercised during `SubmitListing`, but the test suite is independent of the framework.

Slice 1.2 encompasses the full `SubmitListing` → `ListingSubmitted + ListingApproved + ListingPublished` atomic chain. `ListingPublished` is the integration event that feeds the Listings BC; its routing to RabbitMQ and the Listings BC's handler are part of this slice.

### M1 carryover cleanups

| Item | Where resolved |
|---|---|
| `[AllowAnonymous]` M1-override language removed — now an intentional project-wide stance through M5 (see §6) | S2 (Selling BC scaffold — new endpoints carry the stance explicitly) |
| Local queue rule for `SellerRegistrationCompleted` replaced with RabbitMQ routing | S3 |

---

## 3. Explicit Non-Goals

Hard line — if you catch yourself building any of these in M2, stop and flag it:

- Post-publication revision (`ReviseListing`) — deferred to later milestone
- End early / relist (`EndListingEarly`, `MarkAsRelisted`) — deferred
- `004-scenarios.md` §3 and §4 scenarios — not in M2 scope
- `004-scenarios.md` §7.3–7.6 (end-early API gateway checks) — deferred with end-early
- `ListingRevised`, `ListingEndedEarly`, `ListingRelisted` integration events — not authored in M2
- Any Auctions BC work — M3
- Listings BC watchlist (slice 8.1), participant bid history (slice 8.2) — M3+
- `ListingAttachedToSession`, `SessionStarted`, `BiddingOpened` consumers in Listings BC — M3
- Any Settlement, Obligations, Relay, or Operations BC work
- Frontend (`critterbids-web`, `critterbids-ops`) — M6
- Real authentication scheme — M6
- EF Core projections (`marten-projections.md`) — not needed in M2; `CatalogListingView` uses a native Marten document
- Named Polecat stores — only one Polecat BC exists (Participants); deferred until Settlement or Operations arrives

---

## 4. Solution Layout

### New projects added in M2

```
src/
  CritterBids.Selling/          # Selling BC class library — SellerListing aggregate, validator
  CritterBids.Listings/         # Listings BC class library — CatalogListingView projection, read paths
tests/
  CritterBids.Selling.Tests/    # sibling of CritterBids.Selling
  CritterBids.Listings.Tests/   # sibling of CritterBids.Listings
```

### Full solution layout at M2 close

```
CritterBids/
├── CritterBids.sln
├── Directory.Packages.props
├── src/
│   ├── CritterBids.AppHost/              # .NET Aspire orchestration (unchanged)
│   ├── CritterBids.Api/                  # API host — gains AddSellingModule(), AddListingsModule()
│   ├── CritterBids.Contracts/            # gains Selling/ListingPublished.cs
│   ├── CritterBids.Participants/         # unchanged
│   ├── CritterBids.Selling/              # NEW — Selling BC
│   └── CritterBids.Listings/             # NEW — Listings BC
└── tests/
    ├── CritterBids.Api.Tests/            # unchanged
    ├── CritterBids.Contracts.Tests/      # unchanged
    ├── CritterBids.Participants.Tests/   # unchanged
    ├── CritterBids.Selling.Tests/        # NEW
    └── CritterBids.Listings.Tests/       # NEW
```

The Layout 2 rule (one test project per production project) established in M1-S1 applies: adding `CritterBids.Selling` requires adding `CritterBids.Selling.Tests` in the same PR. Same for Listings.

---

## 5. Infrastructure

### First Marten BCs

Selling and Listings are the first BCs to use PostgreSQL via Marten. Both share the same PostgreSQL server instance (already provisioned by `CritterBids.AppHost` in M1). Schema isolation is enforced at the Marten configuration level — each BC owns its own PostgreSQL schema.

> **Decision:** CritterBids uses per-BC Marten configuration with explicit `DatabaseSchemaName` set to the BC name in lowercase. Each Marten BC is isolated to its own schema. Selling BC schema: `selling`. Listings BC schema: `listings`. Full ADR: `docs/decisions/008-marten-bc-isolation.md` (authored in S1).

### Marten module pattern

Each BC follows the same module pattern established for Polecat in M1, adapted for Marten:

```csharp
public static IServiceCollection AddSellingModule(
    this IServiceCollection services,
    IConfiguration config)
{
    var connectionString = config["ConnectionStrings:critterbids-postgres"]
        ?? throw new InvalidOperationException("Missing Selling BC PostgreSQL connection string");

    services.AddMarten(opts =>
    {
        opts.Connection(connectionString);
        opts.DatabaseSchemaName = "selling";
        opts.Policies.AutoApplyTransactions();
        // event stream and projection registrations
    })
    .ApplyAllDatabaseChangesOnStartup()
    .IntegrateWithWolverine();

    // Wolverine routing rules for this BC's integration events
    // RabbitMQ queue subscriptions for incoming integration events

    return services;
}
```

`opts.Policies.AutoApplyTransactions()` is required in every Marten BC's configuration — same as the Polecat convention. `ApplyAllDatabaseChangesOnStartup()` chains on the Marten builder, not inside the options lambda (same placement rule as Polecat's `ApplyAllDatabaseChangesOnStartup()`).

> **Note on named stores:** The S1 ADR resolves whether multiple Marten BCs in the same process require `AddMartenStore<T>()` (named stores) or whether schema-per-BC within separate `AddMarten()` calls is sufficient. The pattern above is the working assumption; the ADR either confirms it or corrects it. The module shape is stable regardless.

### AppHost

No changes needed. PostgreSQL was provisioned in M1-S3. Both Selling and Listings BC modules resolve their connection strings from the same `"ConnectionStrings:critterbids-postgres"` key injected by Aspire. Schema isolation happens at the Marten configuration level, not at the infrastructure level.

### RabbitMQ routing changes in M2

M1 left `SellerRegistrationCompleted` routed to a local Wolverine queue in `Program.cs` with no consumer. M2 replaces this with real RabbitMQ exchange routing. `Program.cs` changes required in S3:

| Change | Session |
|---|---|
| Remove local queue fallback rule for `SellerRegistrationCompleted` from `Program.cs` | S3 |
| Add `opts.PublishMessage<SellerRegistrationCompleted>().ToRabbitQueue("selling-participants-events")` in Participants BC module | S3 |
| Add `opts.ListenToRabbitQueue("selling-participants-events")` in Selling BC module | S3 |
| Add `opts.PublishMessage<ListingPublished>().ToRabbitQueue("listings-selling-events")` in Selling BC module | S5 |
| Add `opts.ListenToRabbitQueue("listings-selling-events")` in Listings BC module | S6 |

Queue names follow `<consumer>-<publisher>-<category>` per `docs/skills/integration-messaging.md`.

---

## 6. Conventions Pinned

Conventions inherit from `CLAUDE.md` unless overridden below.

### Authentication — Project-Wide Stance Through M5

**`[AllowAnonymous]` on all endpoints through M5. This is the intentional project stance, not a temporary override.**

The M1 milestone characterized `[AllowAnonymous]` as a "M1-only override" of the `CLAUDE.md` convention of `[Authorize]` on all non-auth endpoints. That framing is retired as of M2. The decision is: real authentication is deferred to M6. From M2 forward, `[AllowAnonymous]` on all endpoints is the intentional choice — it is not an exception to a rule, it is the rule until M6.

New endpoints added in M2 carry `[AllowAnonymous]` explicitly. Existing Participants endpoints retain their existing `[AllowAnonymous]` attributes unchanged. The `CLAUDE.md` convention of `[Authorize]` on all non-auth endpoints is suspended until M6 re-addresses it with full deliberate coverage.

### Marten BC Isolation

True schema isolation per BC. Each Marten BC owns exactly one PostgreSQL schema. Schema names match the BC name in lowercase:

| BC | Schema |
|---|---|
| Selling | `selling` |
| Listings | `listings` |

No BC may reference another BC's Marten tables directly. Cross-BC data flows exclusively through integration events over RabbitMQ.

Detailed pattern confirmed by ADR `008-marten-bc-isolation.md` authored in S1.

### ISellerRegistrationService — Module Seam Pattern

The API gateway check for seller registration (scenarios 7.1/7.2 in `004-scenarios.md`) requires the API layer to verify that a seller is registered before routing `CreateDraftListing` to the Selling BC. The check is done through a module-exposed seam, not by the API layer directly querying Selling's Marten store.

```csharp
// Exposed by Selling BC — registered in AddSellingModule()
public interface ISellerRegistrationService
{
    Task<bool> IsRegisteredAsync(Guid sellerId, CancellationToken ct = default);
}
```

`CritterBids.Api` injects `ISellerRegistrationService` into endpoint handlers that require seller registration checks. The concrete implementation queries `RegisteredSellers` inside Selling's own Marten store. The API layer never touches Selling's `IDocumentSession` directly.

This is the canonical pattern for API-layer cross-BC state checks in CritterBids. When a second BC requires a similar seam, follow this shape.

### UUID v7 for Marten BC Stream IDs

Under the scope of `docs/decisions/007-uuid-strategy.md` (Proposed), Marten BC stream IDs use UUID v7 (`Guid.CreateVersion7()`). Marten BCs do not have the same determinism requirement as Polecat BCs — `SellerListing` streams have no natural business key that a v5 derivation would meaningfully encode. UUID v7 provides insert locality via its Unix-ms prefix and aligns with the forward-looking standard.

UUID v5 remains the convention for stream IDs in Polecat BCs (Participants) where determinism from a business key is load-bearing for idempotent stream creation. The split convention is:

| BC type | Stream ID strategy |
|---|---|
| Polecat BCs (Participants, Settlement, Operations) | UUID v5 with BC-specific namespace constant |
| Marten BCs (Selling, Listings, Auctions, Obligations, Relay) | UUID v7 |

This convention is confirmed for M2 but the underlying ADR (`007-uuid-strategy.md`) remains **Proposed** pending Marten 8 / Polecat 2 capability verification and JasperFx team input. ADR promotion gates are re-evaluated at M3.

### Integration Event Placement

`CritterBids.Contracts.Selling.ListingPublished` is the first Selling BC integration contract. Namespace and folder structure follows the `integration-messaging.md` convention:

```
src/CritterBids.Contracts/
├── Participants/
│   └── SellerRegistrationCompleted.cs    # M1
└── Selling/
    └── ListingPublished.cs                # M2
```

`ListingPublished` is both a domain event in Selling's Marten event stream and the integration contract published to downstream BCs. The `SubmitListing` handler returns it as part of the `(Events, OutgoingMessages)` tuple — the domain event goes to the event stream, the integration contract goes to the Wolverine outbox.

**The `ListingPublished` contract must carry payload for all future consumers at first commit**, even though only Listings BC consumes it in M2. Per `integration-messaging.md` L2, design contracts for all consumers before publishing the first message. Future consumers include:

| Consumer | Fields required |
|---|---|
| Listings BC (M2) | `ListingId`, `SellerId`, `Title`, `Format`, `StartingBid`, `PublishedAt` |
| Settlement BC (M5) | `ReservePrice`, `FeePercentage` |
| Auctions BC (M3) | `ExtendedBiddingEnabled`, `ExtendedBiddingTriggerWindow`, `ExtendedBiddingExtension`, `Duration` |

This means the M2 `ListingPublished` contract carries all of these fields despite Settlement and Auctions not subscribing until later milestones.

### AutoApplyTransactions — BC-Engine-Agnostic Convention

`opts.Policies.AutoApplyTransactions()` is required in every BC's Marten or Polecat configuration. This convention is now stated in engine-agnostic terms. S7 updates CLAUDE.md to reflect this.

### Records, Collections, Naming

Unchanged from M1 and `CLAUDE.md`:

- `sealed record` for all commands, events, queries, and read models — no exceptions
- `IReadOnlyList<T>` not `List<T>` for collections
- No "Event" suffix on domain event type names
- `OutgoingMessages` for all integration event publishing — never `IMessageBus` directly

---

## 7. Acceptance Tests

Tests are organized by project. All integration tests use xUnit + Shouldly + Testcontainers + Alba per `docs/skills/critter-stack-testing-patterns.md`.

### `CritterBids.Selling.Tests`

#### `RegisteredSellersProjectionTests.cs`

Mapping from `004-scenarios.md` §6. Integration tests against Marten with Testcontainers.

| Scenario | Test method |
|---|---|
| 6.1 — `SellerRegistrationCompleted` creates projection row | `SellerRegistrationCompleted_CreatesRegisteredSellerRow` |
| 6.2 — Idempotent replay | `SellerRegistrationCompleted_Duplicate_IsIdempotent` |
| 6.3 — Query by SellerId — found | `IsRegistered_WithKnownSeller_ReturnsTrue` |
| 6.4 — Query by SellerId — not found | `IsRegistered_WithUnknownSeller_ReturnsFalse` |

#### `ListingValidatorTests.cs`

Mapping from `004-scenarios.md` §5. **Pure-function tests — no framework, no host, no Testcontainers.**

| Scenario | Test method |
|---|---|
| 5.1 — Valid draft passes | `ValidDraft_Passes` |
| 5.2 — Title required | `Title_Empty_IsRejected` |
| 5.3 — Title whitespace-only | `Title_Whitespace_IsRejected` |
| 5.4 — Title length limit (201 chars) | `Title_ExceedsMaxLength_IsRejected` |
| 5.5 — StartingBid must be positive | `StartingBid_Zero_IsRejected` |
| 5.6 — Reserve below starting bid | `Reserve_BelowStartingBid_IsRejected` |
| 5.7 — BIN below reserve | `BuyItNow_BelowReserve_IsRejected` |
| 5.8 — BIN equals starting bid | `BuyItNow_EqualsStartingBid_IsRejected` |
| 5.9 — No reserve (null) is valid | `Reserve_Null_WithBuyItNow_IsValid` |
| 5.10 — No BIN (null) is valid | `BuyItNow_Null_IsValid` |
| 5.11 — Flash format requires null Duration | `Flash_WithDuration_IsRejected` |
| 5.12 — Timed format requires non-null Duration | `Timed_WithoutDuration_IsRejected` |
| 5.13 — Extended bidding TriggerWindow exceeds max | `ExtendedBidding_TriggerWindowExceedsMax_IsRejected` |
| 5.14 — Extended bidding disabled ignores invalid window | `ExtendedBidding_Disabled_IgnoresInvalidWindow_IsValid` |

#### `DraftListingTests.cs`

Mapping from `004-scenarios.md` §1. Marten aggregate tests.

| Scenario | Test method |
|---|---|
| 1.1 — Create draft — happy path | `CreateDraft_WithRegisteredSeller_ProducesDraftListingCreated` |
| 1.2 — Create draft — seller not registered | `CreateDraft_WithUnregisteredSeller_ThrowsSellerNotRegisteredException` |
| 1.3 — Update draft — valid change | `UpdateDraft_ValidChange_ProducesDraftListingUpdated` |
| 1.4 — Update draft — violates invariant | `UpdateDraft_BinBelowReserve_ThrowsValidationException` |
| 1.5 — Update draft — not in Draft state | `UpdateDraft_WhenPublished_ThrowsInvalidStateException` |

#### `SubmitListingTests.cs`

Mapping from `004-scenarios.md` §2. Marten aggregate tests.

| Scenario | Test method |
|---|---|
| 2.1 — Submit happy path — 3-event atomic chain | `SubmitListing_ValidDraft_ProducesThreeEventsAtomically` |
| 2.2 — Submit — validation fails | `SubmitListing_InvalidDraft_ProducesSubmittedAndRejected` |
| 2.3 — Submit — from Rejected state after fix | `SubmitListing_FromRejectedStateAfterFix_ProducesThreeEvents` |
| 2.4 — Submit — not in Draft state | `SubmitListing_WhenAlreadyPublished_ThrowsInvalidStateException` |

#### `CreateDraftListingApiTests.cs`

Mapping from `004-scenarios.md` §7 (API gateway checks, M2-relevant subset only). HTTP-level tests against the API host.

| Scenario | Test method |
|---|---|
| 7.1 — Create draft — seller registered | `CreateDraftListing_WithRegisteredSeller_Returns201` |
| 7.2 — Create draft — seller not registered | `CreateDraftListing_WithUnregisteredSeller_Returns403` |

### `CritterBids.Listings.Tests`

#### `CatalogListingViewTests.cs`

Mapping from `001-scenarios.md` slices 1.3 and 1.4. Integration tests verifying the `CatalogListingView` projection and read-path endpoints.

| Slice | Test method |
|---|---|
| 1.3 — Catalog browse — listings appear after publish | `GetCatalog_AfterListingPublished_ReturnsCatalogEntry` |
| 1.3 — Catalog browse — unpublished listings not shown | `GetCatalog_BeforePublish_ReturnsEmptyList` |
| 1.4 — Listing detail — published listing | `GetListingDetail_PublishedListing_ReturnsDetail` |
| 1.4 — Listing detail — unknown ID returns 404 | `GetListingDetail_UnknownId_Returns404` |

### Test count summary at M2 close

| Project | Tests | Type |
|---|---|---|
| `CritterBids.Selling.Tests` | 4 + 14 + 5 + 4 + 2 = **29** | Mixed (integration + pure-function) |
| `CritterBids.Listings.Tests` | **4** | Integration |
| Existing (`Participants`, `Api`, `Contracts`) | **8** | Integration |
| **Total** | **41** | |

---

## 8. Open Questions / Decisions

| ID | Question | Disposition |
|---|---|---|
| M2-D1 | Named Marten stores: `AddMartenStore<T>()` vs schema-per-BC within separate `AddMarten()` calls | **Resolved in S1 (ADR 008). Named stores required; working assumption corrected.** Separate `AddMarten()` calls per BC conflict in DI — the second call registers a competing `IDocumentStore` singleton and silently discards the first BC's configuration. Each Marten BC uses `AddMartenStore<IBcDocumentStore>()` with a BC-scoped marker interface and its own lowercase schema name. See `docs/decisions/008-marten-bc-isolation.md`. |
| M2-D2 | `ListingPublished` contract payload completeness | **Resolved in S5.** Walk `integration-messaging.md` L2 consumer table before finalizing. All three downstream consumers (Listings, Settlement, Auctions) must be represented in the payload even though only Listings subscribes in M2 (see §6 Integration Event Placement). |
| M2-D3 | Is `RegisteredSellers` the only Selling BC projection? | **Confirm during M2 coding** (W004-P2-9). Expected: yes, for M2 scope. |
| M1-deferred: S4-F2 | Named Polecat stores | **Still deferred.** Only one Polecat BC (Participants) exists in M2. Address when Settlement or Operations arrives. |
| M1-deferred: ADR 007 | UUID v7 promotion gates — Marten 8 / Polecat 2 capability check + JasperFx team input | **Stays Proposed through M2.** Re-evaluate at M3 (Auctions BC — the high-write motivation for v7 insert locality). |
| M1-deferred: CLAUDE.md | `AutoApplyTransactions` Marten-specific wording | **Resolved in S7.** Update CLAUDE.md to BC-engine-agnostic phrasing as part of the skills pass. |

---

## 9. Session Breakdown

Seven sessions, matching M1's shape. S1 is a pure ADR / documentation session — no code, no tests. S7 is retrospective-only. Every implementation session corresponds to a PR and a retrospective.

| # | Prompt file | Scope summary |
|---|---|---|
| 1 | `docs/prompts/implementations/M2-S1-marten-bc-isolation-adr.md` | Marten BC isolation ADR — named stores vs schema-per-BC decision, schema naming convention, module pattern sketch, UUID v7 in Marten BCs confirmed. Documentation only; no code, no projects created. Authors `docs/decisions/008-marten-bc-isolation.md`. |
| 2 | `docs/prompts/implementations/M2-S2-selling-bc-scaffold.md` | Selling BC scaffold — `CritterBids.Selling` and `CritterBids.Selling.Tests` projects, `SellerListing` aggregate (empty shell), `AddSellingModule()` with Marten config + `IntegrateWithWolverine()`, smoke test. No handlers, no slices. |
| 3 | `docs/prompts/implementations/M2-S3-registered-sellers-consumer.md` | `RegisteredSellers` consumer — `SellerRegistrationCompleted` Wolverine handler, `RegisteredSeller` Marten document, `ISellerRegistrationService` interface + implementation registered in `AddSellingModule()`. `Program.cs` routing update: remove local queue fallback rule, add real RabbitMQ routing (`selling-participants-events`). 4 projection integration tests. |
| 4 | `docs/prompts/implementations/M2-S4-north-star-alignment.md` | Architecture pivot — evaluates Options A/B/C for resolving the Wolverine dual-store conflict; decides All-Marten (Option A); authors ADR 011; updates CLAUDE.md, three skill docs, and skills README; renumbers M2 session table; authors M2-S5 prompt. Documentation only; no code changes. |
| 5 | `docs/prompts/implementations/M2-S5-slice-1-1-create-draft-listing.md` | Slice 1.1 — `CreateDraftListing` command, `DraftListingCreated` event, `SellerListing` aggregate (draft lifecycle), `ListingValidator` pure-function rules, `POST /api/listings/draft` endpoint with `ISellerRegistrationService` gate. 5 aggregate tests + 14 pure-function tests + 2 API gateway tests. |
| 6 | `docs/prompts/implementations/M2-S6-slice-1-2-submit-listing.md` | Slice 1.2 — `SubmitListing` handler (3-event atomic chain: `ListingSubmitted + ListingApproved + ListingPublished`), `CritterBids.Contracts.Selling.ListingPublished` integration contract (full payload for all future consumers), `AddSellingModule()` RabbitMQ publish rule for `ListingPublished` (`listings-selling-events`). 4 aggregate tests. |
| 7 | `docs/prompts/implementations/M2-S7-listings-bc-and-read-paths.md` | Listings BC scaffold + `CatalogListingView` + read paths — `CritterBids.Listings` and `CritterBids.Listings.Tests` projects, `AddListingsModule()` with Marten config, `CatalogListingView` Marten document, Wolverine handler consuming `Contracts.Selling.ListingPublished` (RabbitMQ subscription: `listings-selling-events`), `GET /api/listings`, `GET /api/listings/{id}`. 4 integration tests. |
| 8 | `docs/prompts/implementations/M2-S8-retrospective-skills-m2-close.md` | Retrospective skills + M2 close — `docs/skills/adding-bc-module.md` authored retrospectively from S2–S3, `docs/skills/domain-event-conventions.md` authored retrospectively from S5–S6, M2 retrospective written. |

### Session dependency graph

```
S1 (Marten ADR — docs only)
 └── S2 (Selling scaffold)
      └── S3 (RegisteredSellers consumer + Program.cs routing)
           └── S4 (architecture pivot — docs only)
                └── S5 (CreateDraftListing + ListingValidator)
                     └── S6 (SubmitListing → ListingPublished)
                          └── S7 (Listings BC + CatalogListingView + read paths)
                               └── S8 (skills + retro + M2 close)
```

Sessions are strictly sequential — each depends on the prior.

### Session sizing notes

- **S4 is the largest session.** It combines the aggregate (5 tests), the validator (14 pure-function tests), and the API gateway check (2 tests). If it runs long during execution, the API gateway check (scenarios 7.1/7.2) can be deferred into S5 without breaking the dependency chain — S5 adds the `POST /api/listings/draft` → `ISellerRegistrationService` → handler flow anyway.
- **S6 is the second-largest.** Listings BC scaffold + projection handler + two read endpoints is three concerns. The scaffold is lightweight (Listings is projection-first in M2, no domain aggregate), so this should stay within bounds.
- **S3 has a cross-project footprint.** It touches `CritterBids.Participants` (routing rule migration), `CritterBids.Selling` (handler + projection), and `CritterBids.Api/Program.cs`. Agent prompts must explicitly list all three in the allowed-file set.

---

## Appendix: Cross-BC Integration Map at M2 Close

The two new integration connections established in M2:

```
Participants ─── SellerRegistrationCompleted ────────────► Selling
             (queue: selling-participants-events)          (RegisteredSellers projection)

Selling ─────── ListingPublished ────────────────────────► Listings
             (queue: listings-selling-events)              (CatalogListingView projection)
```

Both are Wolverine message handlers consuming from RabbitMQ. Neither uses Marten async projections — the projection data lives in Marten documents, but the trigger is a Wolverine message arriving from the RabbitMQ queue.

At M2 close, `ListingPublished` reaches only Listings BC. The contract carries fields for Settlement and Auctions (future consumers in M3/M5) but no additional `ListenToRabbitQueue()` declarations are made for them yet.
