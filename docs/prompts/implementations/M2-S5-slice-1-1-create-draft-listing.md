# M2-S5: Slice 1.1 — Create Draft Listing

**Milestone:** M2 — Listings Pipeline
**Slice:** S5 — Slice 1.1: CreateDraftListing command, DraftListingCreated event, SellerListing aggregate (draft lifecycle), ListingValidator, POST endpoint with seller gate
**Agent:** @PSA
**Estimated scope:** one PR, ~8 new files + 3 modified files

---

## Goal

Implement Slice 1.1 of the Listings Pipeline: a registered seller can create a draft listing via
`POST /api/listings/draft`. The endpoint gates on seller registration via `ISellerRegistrationService`,
applies the `ListingValidator` pure-function rules, and produces a `DraftListingCreated` domain event
that is appended to the `SellerListing` event stream. At session close: 5 aggregate tests + 14
pure-function validator tests + 2 API gateway tests pass, the solution still builds clean, and total
test count is 13 + 21 = 34 (existing 13 + 21 new).

---

## Context to load

1. `docs/decisions/011-all-marten-pivot.md` — the architecture decision made in S4; all BCs use PostgreSQL via Marten. The uniform bootstrap is confirmed. Read before implementing.
2. `docs/decisions/009-shared-marten-store.md` — the shared primary Marten store pattern and `ConfigureMarten()` per-BC contribution model. The Selling BC module already follows this pattern from S2.
3. `docs/skills/marten-event-sourcing.md` — canonical patterns for event-sourced aggregates, `Apply()` conventions, `MartenOps.StartStream()`, `[WriteAggregate]`, snapshot lifecycle, `ConfigureMarten()` per-BC pattern, and `AutoApplyTransactions()` placement (in `UseWolverine()` globally — not in `ConfigureMarten()`).
4. `docs/skills/wolverine-message-handlers.md` — `[Entity]` batch-loading pattern, `ValidateAsync`/`Validate` railway programming model, `[AggregateHandler]` usage, `CreationResponse<T>` return type, `IStartStream`/`MartenOps.StartStream()` requirement.
5. `docs/milestones/M2-listings-pipeline.md` — §7 for the complete acceptance test method list (authoritative for scope); §6 for the `[AllowAnonymous]` stance and `ISellerRegistrationService` module seam pattern.

---

## In scope

### `SellerListing` aggregate — draft lifecycle

- `DraftListingCreated` domain event (in `CritterBids.Selling`) carrying: `ListingId`, `SellerId`, `Title`, `Format`, `StartingBid`, `ReservePrice?`, `BuyItNow?`, `Duration?`, `ExtendedBiddingEnabled`, `ExtendedBiddingTriggerWindow?`, `ExtendedBiddingExtension?`, `CreatedAt`
- `SellerListing` aggregate class with `Apply(DraftListingCreated)` method setting `Id`, `SellerId`, `Title`, and `Status = ListingStatus.Draft`
- `ListingStatus` enum: `Draft`, `Submitted`, `Published`, `Rejected`, `Withdrawn` (all statuses needed for M2 close; only `Draft` transitions in this slice)
- `ConfigureMarten()` contribution in `AddSellingModule()` to register the `SellerListing` event stream type once `DraftListingCreated` is introduced

### `ListingValidator` — pure-function rules

14 pure-function validation rules extracted from `004-scenarios.md` §5. No framework, no host, no Testcontainers — standalone static class with a single `Validate(CreateDraftListing)` method returning a `ValidationResult`. Rules cover: title required/whitespace/max-length, starting bid positive, reserve vs starting bid ordering, BIN vs reserve ordering, BIN vs starting bid equality, null reserve/BIN combinations, Flash format requires null Duration, Timed format requires non-null Duration, extended bidding trigger window max, extended bidding disabled ignores invalid window. The full test list is in `docs/milestones/M2-listings-pipeline.md` §7 `ListingValidatorTests.cs`.

### `CreateDraftListing` handler and endpoint

- `CreateDraftListing` command (sealed record) carrying `SellerId`, `Title`, `Format`, `StartingBid`, `ReservePrice?`, `BuyItNow?`, `Duration?`, `ExtendedBiddingEnabled`, `ExtendedBiddingTriggerWindow?`, `ExtendedBiddingExtension?`
- `POST /api/listings/draft` endpoint — `[AllowAnonymous]` (M2 stance per `docs/milestones/M2-listings-pipeline.md` §6)
- `ISellerRegistrationService.IsRegisteredAsync(sellerId)` gate — return 403 if not registered
- On success: create new `SellerListing` stream via `MartenOps.StartStream<SellerListing>()`, return 201 with `Location: /api/listings/{listingId}` via `CreationResponse<Guid>`
- Stream ID: `Guid.CreateVersion7()` per the Marten BC UUID v7 convention (M2 milestone §6)

### Tests in `CritterBids.Selling.Tests`

All test files listed in `docs/milestones/M2-listings-pipeline.md` §7:

- `DraftListingTests.cs` — 5 aggregate tests (scenarios 1.1–1.5 from `004-scenarios.md` §1). Note: 1.3 `UpdateDraft` and 1.4/1.5 state-guard tests require that the aggregate and status transitions already exist from this slice even though UpdateDraft is not an endpoint in S5.
- `ListingValidatorTests.cs` — 14 pure-function tests (scenarios 5.1–5.14)
- `CreateDraftListingApiTests.cs` — 2 HTTP gateway tests (scenarios 7.1–7.2)

---

## Explicitly out of scope

- `UpdateDraftListing` command and endpoint — deferred to a later session
- `SubmitListing` handler and 3-event chain — S6
- `ListingSubmitted`, `ListingApproved`, `ListingPublished` events — S6
- `RegisteredSellersProjectionTests.cs` — authored in S3, unchanged
- `SubmitListingTests.cs` — S6
- `CritterBids.Listings` project — S7
- `ListingPublished` integration contract — S6
- RabbitMQ publish rule for `ListingPublished` — S6
- Participants BC migration from Polecat to Marten — separate dedicated session (open dependency, see Open Questions)
- Any changes to `Program.cs`, `CritterBids.AppHost`, or infrastructure configuration
- Any changes to `CritterBids.Contracts` in this slice — `DraftListingCreated` is a domain event, not an integration contract

---

## Conventions to follow

- `[AllowAnonymous]` on all new endpoints — project-wide stance through M5 per `docs/milestones/M2-listings-pipeline.md` §6
- `sealed record` for `CreateDraftListing` command and `DraftListingCreated` event
- No "Event" suffix on `DraftListingCreated`
- `Guid.CreateVersion7()` for the `SellerListing` stream ID
- `MartenOps.StartStream<SellerListing>(listingId, new DraftListingCreated(...))` — never `session.Events.StartStream()` directly
- `ConfigureMarten()` contribution in `AddSellingModule()` to register the event stream type — not a new `AddMarten()` call
- `AutoApplyTransactions()` is already in `UseWolverine()` in `Program.cs` — do not add it to `ConfigureMarten()` in the module

---

## Acceptance criteria

- [ ] `DraftListingCreated` sealed record exists in `CritterBids.Selling`
- [ ] `SellerListing` aggregate has `Apply(DraftListingCreated)` and `ListingStatus.Draft` state
- [ ] `AddSellingModule()` registers `SellerListing` event stream type via `ConfigureMarten()`
- [ ] `ListingValidator.Validate(CreateDraftListing)` exists as a pure static function with 14 rules
- [ ] `POST /api/listings/draft` endpoint exists, returns 201 for registered sellers, 403 for unregistered
- [ ] `ListingValidatorTests.cs`: 14 tests pass (pure-function, no host, no Testcontainers)
- [ ] `DraftListingTests.cs`: 5 aggregate tests pass
- [ ] `CreateDraftListingApiTests.cs`: 2 API gateway tests pass
- [ ] `dotnet build` passes with 0 errors, 0 warnings
- [ ] `dotnet test` passes with 34/34 (existing 13 + 21 new)
- [ ] No Polecat packages, `AddPolecat()` calls, or `PolecatOps` references introduced

---

## Open questions

**Participants BC migration dependency.** The `ISellerRegistrationService` gate in scenario 7.1/7.2 queries the `RegisteredSellers` Marten projection, which was established in S3 and works correctly. The Participants BC currently has a Polecat implementation (ADR 011 §Consequences). The S5 slice does not require the Participants migration — `ISellerRegistrationService` is a Selling BC concern backed by Marten. The migration of the Participants BC from Polecat to Marten is a separate dedicated session to be prompted after S5 (or in parallel if the session ordering permits). Flag as an open dependency in this session's retrospective.
