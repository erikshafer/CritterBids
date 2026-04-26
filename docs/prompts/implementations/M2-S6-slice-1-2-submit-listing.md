# M2-S6: Slice 1.2 — Submit Listing + Package Upgrades

**Milestone:** M2 — Listings Pipeline
**Slice:** S6 — Slice 1.2: SubmitListing handler (3-event atomic chain), ListingPublished integration contract, RabbitMQ publish rule, package upgrades
**Agent:** @PSA
**Estimated scope:** one PR, ~5 new files + 4 modified files

---

## Goal

Implement Slice 1.2 of the Listings Pipeline: a seller can submit a draft listing for publication.
`SubmitListing` produces a 3-event atomic chain — `ListingSubmitted + ListingApproved + ListingPublished`
— in a single Marten transaction. `ListingPublished` is the first Selling BC integration contract;
it is published to RabbitMQ via Wolverine's outbox so that the Listings BC (S7) can project
`CatalogListingView` entries. Additionally, bump WolverineFx packages from 5.30.0 to 5.31.0 and
`Microsoft.NET.Test.Sdk` from 18.3.0 to 18.4.0. At session close: 4 new aggregate tests pass,
build is clean, and total test count is 34 + 4 = 38.

---

## Context to load

1. `docs/milestones/M2-listings-pipeline.md` — §7 for the complete `SubmitListingTests.cs`
   test method list; §6 for `ListingPublished` contract payload requirements (all future consumers
   must be represented at first commit); §5 for the RabbitMQ routing convention (`listings-selling-events`).
2. `docs/decisions/011-all-marten-pivot.md` — all BCs use PostgreSQL via Marten; shapes the
   module bootstrap and store topology for this session.
3. `docs/skills/marten-event-sourcing.md` — canonical `Apply()` patterns, `MartenOps`, stream
   type registration in `ConfigureMarten()`, `[WriteAggregate]` usage.
4. `docs/skills/wolverine-message-handlers.md` — `(Events, OutgoingMessages)` return tuple,
   `OutgoingMessages` for integration event publishing, `[WriteAggregate]` in handler signature.
5. `docs/skills/integration-messaging.md` — `ListenToRabbitQueue` / `PublishMessage` routing
   convention, queue naming (`<consumer>-<publisher>-<category>`), `OutgoingMessages` requirement,
   contract payload completeness rule (L2 consumer table).
6. `docs/retrospectives/M2-S5-slice-1-1-create-draft-listing-retrospective.md` — build state,
   test baseline, and open items carried into this session.

---

## In scope

### Package upgrades — `Directory.Packages.props`

Bump the following entries. Do this first, run `dotnet build` to verify clean, then proceed.

- All `WolverineFx.*` entries: `5.30.0` → `5.31.0`
  - `WolverineFx`, `WolverineFx.Http`, `WolverineFx.Http.Marten`, `WolverineFx.Http.Polecat`,
    `WolverineFx.Marten`, `WolverineFx.Polecat`, `WolverineFx.RabbitMQ`
- `Microsoft.NET.Test.Sdk`: `18.3.0` → `18.4.0`

### `SubmitListing` domain events

Four new sealed records in `CritterBids.Selling`:

- `ListingSubmitted` — carries `ListingId`, `SellerId`, `SubmittedAt`
- `ListingApproved` — carries `ListingId`, `ApprovedAt`
- `ListingRejected` — carries `ListingId`, `RejectionReason`, `RejectedAt`
- `ListingPublished` — carries `ListingId`, `SellerId`, `Title`, `Format`, `StartingBid`,
  `ReservePrice?`, `BuyItNow?`, `Duration?`, `ExtendedBiddingEnabled`,
  `ExtendedBiddingTriggerWindow?`, `ExtendedBiddingExtension?`, `FeePercentage`, `PublishedAt`

`FeePercentage` is required for the Settlement BC (future consumer, M5). All fields in the
`ListingPublished` contract must be present at first commit — do not slim the payload to
what Listings BC consumes today. See `docs/milestones/M2-listings-pipeline.md` §6 for the
full field list and consumer table.

### `SellerListing` aggregate — submit lifecycle

Add `Apply()` methods for all four new events:

- `Apply(ListingSubmitted)` → `Status = ListingStatus.Submitted`
- `Apply(ListingApproved)` → `Status = ListingStatus.Published`
- `Apply(ListingRejected)` → `Status = ListingStatus.Rejected`
- `Apply(ListingPublished)` → `Status = ListingStatus.Published` (idempotent if already set by `ListingApproved`)

Add any fields to `SellerListing` required by the `Apply()` implementations (e.g., `PublishedAt`).
No fields are required beyond what the tests exercise.

### `SubmitListingHandler`

- `SubmitListing` command (sealed record) carrying `ListingId`, `SellerId`
- `SubmitListingHandler.Handle(SubmitListing cmd, [WriteAggregate] SellerListing listing)` →
  return type `(Events, OutgoingMessages)`
- Guard: throw `InvalidListingStateException` if `listing.Status` is not `Draft` or `Rejected`
  (scenario 2.3 requires resubmission from `Rejected` state)
- Happy path (validation passes): append `ListingSubmitted + ListingApproved + ListingPublished`
  as the `Events` element; add `Contracts.Selling.ListingPublished` to `OutgoingMessages`
- Rejection path (validation fails, scenario 2.2): append `ListingSubmitted + ListingRejected`
  only; nothing added to `OutgoingMessages`
- Not-in-scope state guard (scenario 2.4): throw `InvalidListingStateException` if
  `listing.Status` is anything other than `Draft` or `Rejected`

### `CritterBids.Contracts.Selling.ListingPublished`

Create `src/CritterBids.Contracts/Selling/ListingPublished.cs`. Sealed record. Fields per
`docs/milestones/M2-listings-pipeline.md` §6. Namespace: `CritterBids.Contracts.Selling`.
This is the integration contract consumed by Listings BC (M2-S7) and in future by Settlement
(M5) and Auctions (M3). Full payload required at first commit.

No new test file is needed in `CritterBids.Contracts.Tests` for this contract — the contract
is a pure data shape exercised through the Selling BC handler tests.

### `ConfigureMarten()` event stream registrations

Add to `AddSellingModule()`'s `ConfigureMarten()` call:

- `AddEventType<ListingSubmitted>()`
- `AddEventType<ListingApproved>()`
- `AddEventType<ListingRejected>()`
- `AddEventType<ListingPublished>()`

These are required before `UseMandatoryStreamTypeDeclaration` can be enabled in a future session.

### RabbitMQ publish rule

In `AddSellingModule()`, add:

```
opts.PublishMessage<Contracts.Selling.ListingPublished>()
    .ToRabbitQueue("listings-selling-events")
```

Queue name follows the `<consumer>-<publisher>-<category>` convention from
`docs/skills/integration-messaging.md`. The Listings BC's `ListenToRabbitQueue` declaration
is deferred to S7.

### Tests in `CritterBids.Selling.Tests`

Implement `SubmitListingTests.cs` with the four test methods listed in
`docs/milestones/M2-listings-pipeline.md` §7. All four are Marten aggregate tests (no HTTP
boundary). See the test-method-to-scenario mapping table in §7.

---

## Explicitly out of scope

- `UpdateDraftListing` HTTP endpoint (`PATCH /api/listings/draft/{id}`) — deferred beyond M2
- `POST /api/listings/{id}/submit` HTTP endpoint — no HTTP boundary for `SubmitListing` in M2;
  the handler is tested as an aggregate handler only
- `SubmitListingApiTests.cs` — §7.3–7.6 scenarios are deferred per M2 §3 non-goals
- Listings BC (`CritterBids.Listings`) — S7
- `CatalogListingView` projection — S7
- `ListenToRabbitQueue("listings-selling-events")` in Listings BC — S7
- `docs/skills/domain-event-conventions.md` authoring — deferred to S8 retrospective pass
- `docs/skills/adding-bc-module.md` authoring — deferred to S8
- CLAUDE.md edits
- Any changes to `CritterBids.Participants`, `CritterBids.AppHost`, or `CritterBids.Api/Program.cs`
- Polecat version bump — `Polecat` 2.0.1 is unchanged in this session

---

## Conventions to follow

- `sealed record` for all new commands, domain events, and the integration contract
- No "Event" suffix on domain event type names (`ListingSubmitted`, not `ListingSubmittedEvent`)
- `OutgoingMessages` for publishing `Contracts.Selling.ListingPublished` — never `IMessageBus`
  directly in a handler
- `(Events, OutgoingMessages)` return tuple from `SubmitListingHandler.Handle` — `Events` first,
  `OutgoingMessages` second; tuple ordering is load-bearing for Wolverine dispatch
- `[WriteAggregate]` on the `SellerListing` parameter in `Handle` — Marten loads and locks the
  stream before the handler runs
- `FeePercentage` placeholder value: use `0.10m` (10%) — no fee engine exists yet; M5 scope
- `[AllowAnonymous]` stance is unchanged — no new HTTP endpoints in this session
- All four new domain events registered via `AddEventType<T>()` in `ConfigureMarten()`

---

## Acceptance criteria

- [ ] `Directory.Packages.props`: all `WolverineFx.*` entries read `5.31.0`
- [ ] `Directory.Packages.props`: `Microsoft.NET.Test.Sdk` reads `18.4.0`
- [ ] `dotnet build` passes with 0 errors, 0 warnings immediately after package version bumps
- [ ] `ListingSubmitted`, `ListingApproved`, `ListingRejected`, `ListingPublished` sealed records exist in `CritterBids.Selling`
- [ ] `SellerListing` has `Apply()` methods for all four new events with correct status transitions
- [ ] `SubmitListingHandler.Handle` returns `(Events, OutgoingMessages)` and appends the 3-event chain on the happy path
- [ ] `SubmitListingHandler` calls `ListingValidator.Validate()` and produces `ListingSubmitted + ListingRejected` on validation failure (scenario 2.2)
- [ ] `SubmitListingHandler` allows resubmission from `Rejected` state (scenario 2.3)
- [ ] `CritterBids.Contracts/Selling/ListingPublished.cs` exists with full payload (all future-consumer fields present)
- [ ] `AddSellingModule()` registers all four new event types via `AddEventType<T>()` in `ConfigureMarten()`
- [ ] `AddSellingModule()` declares `PublishMessage<Contracts.Selling.ListingPublished>().ToRabbitQueue("listings-selling-events")`
- [ ] `SubmitListingTests.cs`: all 4 tests pass
- [ ] `dotnet build` passes with 0 errors, 0 warnings at session close
- [ ] `dotnet test` passes with 38/38 (existing 34 + 4 new)
- [ ] Session retrospective written to `docs/retrospectives/M2-S6-slice-1-2-submit-listing-retrospective.md`

---

## Session retrospective — required last act

Writing the session retrospective is a **required deliverable**. It is the final commit of the
session, made after all tests pass and the build is clean. Do not skip it.

Write `docs/retrospectives/M2-S6-slice-1-2-submit-listing-retrospective.md` following the
same structure as `docs/retrospectives/M2-S5-slice-1-1-create-draft-listing-retrospective.md`:

- Header block: `Date`, `Milestone`, `Slice`, `Agent`, `Prompt`
- Baseline: test count, build state, package versions, relevant context at session open
- Items completed table (one row per deliverable from this prompt's in-scope list)
- Per-item sections with structural metrics and rationale notes
- Test results table (before / after)
- Build state at session close (same metric format as the M2-S5 retro)
- Key learnings
- Verification checklist (mirrors the acceptance criteria above, checked off)
- Files changed: new and modified, organised by project
- What remains / next session should verify

---

## Open questions

- **`FeePercentage` calculation:** No fee engine exists in M2. Use a hardcoded `0.10m` placeholder
  in the handler. If anything in the codebase suggests a different value or an existing fee
  abstraction, flag it rather than guess.
- **`ListingValidator.Validate()` call site in `SubmitListingHandler`:** The validator was authored
  in S5 as a pure static function on `CreateDraftListing`. If the `SubmitListing` command carries
  different fields than `CreateDraftListing`, an overload or adapter may be needed. Flag and stop
  if the validator signature does not map cleanly — do not silently skip validation.
