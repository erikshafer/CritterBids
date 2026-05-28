# Selling BC

**Maturity:** Implemented (with named gaps against the vision doc).

**Evidence for the call:** `src/CritterBids.Selling` exists with the `SellerListing` event-sourced aggregate (`SellerListing.cs`), four commands with handlers (`CreateDraftListing`, `UpdateDraftListing`, `SubmitListing`, `WithdrawListing`), the `ListingValidator` pure function (14 rules), the `RegisteredSeller` projection with its `SellerRegistrationCompletedHandler` consumer, and the `ISellerRegistrationService` API-layer seam. The module is registered in `Program.cs` line 199 and is covered by ten test files (`tests/CritterBids.Selling.Tests/*Tests.cs`). The vision-doc surface for **`ListingRevised`**, **`ListingEndedEarly`**, and **`ListingRelisted`** has no implementation — those events have no internal CLR types, no contract types, no commands, no handlers; see capability table below.

## Business purpose

Self-service seller flow. A registered seller creates a draft listing, optionally updates it, submits it for approval, and on success the listing is automatically approved and published in the same transaction. A published listing can be withdrawn by the seller before close. The validator enforces the listing-configuration invariants that downstream BCs assume.

## Project layout

Flat vertical-slice layout — aggregates, events, commands/handlers, validators, and module registration sit as sibling files directly under `src/CritterBids.Selling/`.

## Aggregates

| Aggregate | File | Stream lifecycle |
|---|---|---|
| `SellerListing` | `SellerListing.cs` | Stream starts on `DraftListingCreated` with a UUID v7 id (`CreateDraftListingHandler.Handle` line 78). Stream advances through `DraftListingUpdated*` (0..n), `ListingSubmitted`, then either `ListingRejected` (resubmittable) or `ListingApproved + ListingPublished` (atomic pair from `SubmitListingHandler.Handle` lines 55–56). Optionally terminated by `ListingWithdrawn` while `Status == Published`. No terminal close event lives in Selling — close/sold/passed are Auctions concerns. |

## Domain events (internal — `CritterBids.Selling` namespace)

| Event | File | Notes |
|---|---|---|
| `DraftListingCreated` | `DraftListingCreated.cs` | Full listing config payload — same 11 fields as `CreateDraftListing`. |
| `DraftListingUpdated` | `DraftListingUpdated.cs` | Nullable fields — only `Title`, `ReservePrice`, `BuyItNowPrice` are mutable in Draft. |
| `ListingSubmitted` | `ListingSubmitted.cs` | First event in every submit sequence (success or failure). |
| `ListingApproved` | `ListingApproved.cs` | Followed by `ListingPublished` in the same transaction (`SubmitListingHandler.cs` lines 55–56). |
| `ListingRejected` | `ListingRejected.cs` | Carries `RejectionReason`. Resubmittable. |
| `ListingPublished` (internal) | `ListingPublished.cs` | Carries only `ListingId` and `PublishedAt` — the rich payload lives in the contract event of the same name (`src/CritterBids.Contracts/Selling/ListingPublished.cs`). |
| `ListingWithdrawn` (internal) | `ListingWithdrawn.cs` | Carries only `ListingId` and `WithdrawnAt`. |

## Commands and handlers

| Command | File | Endpoint | Notes |
|---|---|---|---|
| `CreateDraftListing(11 fields)` | `CreateDraftListing.cs` | `POST /api/listings/draft`, `[AllowAnonymous]` | `ValidateAsync` checks `ISellerRegistrationService.IsRegisteredAsync` and returns HTTP 403 if not registered (`SellerNotRegisteredException` exists but is documented for the "race-condition retry" Wolverine path, not thrown by `ValidateAsync`). `Handle` generates a UUID v7 stream id and starts the stream with `DraftListingCreated`. |
| `UpdateDraftListing(ListingId, Title?, ReservePrice?, BuyItNowPrice?)` | `UpdateDraftListing.cs` | No HTTP endpoint (XML doc lines 6–8: "deferred to a later session"). | `[WriteAggregate]`. Guards `Status == Draft`. Recomputes effective BIN ≥ Reserve invariant. Emits `DraftListingUpdated`. |
| `SubmitListing(ListingId, SellerId)` | `SubmitListing.cs` | No HTTP endpoint (XML doc line 11: "No HTTP endpoint in M2 — tested as an aggregate handler only"). | `[WriteAggregate]`. Guards `Status ∈ {Draft, Rejected}`. Emits `ListingSubmitted`, then either `ListingRejected` or `ListingApproved + ListingPublished + Contracts.Selling.ListingPublished` (via `OutgoingMessages`). Automated approval — no human review. |
| `WithdrawListing(ListingId, WithdrawnBy)` | `WithdrawListing.cs` | No HTTP endpoint (XML doc lines 15–16: "tested through `IMessageBus` dispatch only per the M5-through-M6 backend-only posture"). | `[WriteAggregate]`. Guards `Status == Published`. Emits internal `ListingWithdrawn` + contract `ListingWithdrawn` via `OutgoingMessages`. |

The "Approved" state is transient in MVP — a single `SubmitListingHandler` chain produces `ListingSubmitted + ListingApproved + ListingPublished` atomically (`SubmitListingHandler.cs` lines 54–56). It is modeled as a real state on the enum (`ListingStatus.cs` line 13) so that a post-MVP migration to manual approval is a handler split, not an event-vocabulary change.

## Validation

`ListingValidator` is a pure function (no DI, no host, no DB). Two overloads share the same 14 rules: one for `CreateDraftListing` commands and one for the loaded `SellerListing` aggregate (`ListingValidator.cs` lines 22–88).

Rules enforced (`ListingValidator.cs`):

- Title: required, non-whitespace, ≤ 200 chars (5.2–5.4)
- `StartingBid > 0` (5.5)
- `ReservePrice >= StartingBid` (5.6)
- `BuyItNowPrice >= ReservePrice` (5.7)
- `BuyItNowPrice > StartingBid` (5.8)
- BIN-vs-reserve vacuously valid when reserve is null (5.9)
- Flash listings: `Duration == null` (5.11)
- Timed listings: `Duration != null` (5.12)
- Extended-bidding trigger window ≤ 2 minutes when enabled (5.13–5.14)

## Integration events (out — `CritterBids.Contracts.Selling` namespace)

| Event | Contract file | Trigger | Payload |
|---|---|---|---|
| `ListingPublished` | `src/CritterBids.Contracts/Selling/ListingPublished.cs` | `SubmitListingHandler.Handle` line 58 via `OutgoingMessages`. | `ListingId`, `SellerId`, `Title`, `Format` (string), `StartingBid`, `ReservePrice?`, `BuyItNow?`, `Duration?`, three extended-bidding fields, `FeePercentage`, `PublishedAt`. |
| `ListingWithdrawn` | `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` | `WithdrawListingHandler.Handle` line 53 via `OutgoingMessages`. | `ListingId`, `WithdrawnBy`, `Reason?` (null in M4), `WithdrawnAt`. |

## Integration events (in)

| Event | Handler file | Effect |
|---|---|---|
| `SellerRegistrationCompleted` | `SellerRegistrationCompletedHandler.cs` | Upserts a `RegisteredSeller` document into the Selling BC's `selling` schema. Wolverine's at-least-once delivery + Marten upsert provides idempotency. |

## Projections / read models

| Projection | File | Source | Purpose |
|---|---|---|---|
| `RegisteredSeller` | `RegisteredSeller.cs` (document), `SellerRegistrationCompletedHandler.cs` (writer), `SellerRegistrationService.cs` (reader) | `SellerRegistrationCompleted` from Participants. | Single field (`Id`). Queried via `ISellerRegistrationService.IsRegisteredAsync` by the API layer to gate `CreateDraftListing`. Lives in the `selling` schema (`SellingModule.cs` line 15). |

## Vision-doc capabilities NOT implemented

| Vision element | Source | Status in `src/` |
|---|---|---|
| `ListingRevised` integration event | `bounded-contexts.md` line 59, `domain-events.md` line 35 | No CLR type in `Contracts/Selling/` or `Selling/`. No emitter. No consumer registration. |
| `ListingEndedEarly` integration event | `bounded-contexts.md` line 59, `domain-events.md` line 36 | No CLR type. No emitter. No consumer. |
| `ListingRelisted` integration event | `bounded-contexts.md` line 59, `domain-events.md` line 37 | No CLR type. No emitter. No consumer. |
| Seller-configurable fee | vision implies seller knows their FVF at publish time | `FeePercentage` is hardcoded to `0.10m` in `SubmitListingHandler.cs` line 70 (comment: "M5 placeholder — no fee engine exists yet"). The contract event still carries the field. |
| HTTP endpoints for update/submit/withdraw | implicit | Only `POST /api/listings/draft` exists. The other three commands are dispatched in tests via Wolverine `IMessageBus`. |

## Storage

PostgreSQL via Marten. `RegisteredSeller` document lives in the `selling` schema (`SellingModule.cs` line 15). The aggregate's event stream lives in the global event-store schema set in `Program.cs` line 186 (`public`). Seven event types are explicitly registered for `UseMandatoryStreamTypeDeclaration` (`SellingModule.cs` lines 20–26).

## Identity strategy

UUID v7 for new listing streams (`CreateDraftListingHandler.Handle` line 78). No UUID v5 namespace constant — Selling has no deterministic-id use case.

## Test-evidenced behaviors

From `tests/CritterBids.Selling.Tests/`:

- `CreateDraftListingApiTests` — the `POST /api/listings/draft` endpoint round-trip.
- `DraftListingTests` — aggregate-level draft creation and replay.
- `ListingValidatorTests` — all 14 validator rules.
- `RegisteredSellersProjectionTests` — `SellerRegistrationCompleted` arrival builds the projection.
- `SellingModuleTests` — module registration.
- `SubmitListingTests` + `SubmitListingDispatchTests` — submit happy path, rejection, state guards, atomic `Submitted + Approved + Published` chain, outbound `Contracts.Selling.ListingPublished`.
- `UpdateDraftListingDispatchTests` — update guards and event emission.
- `WithdrawListingTests` + `WithdrawListingDispatchTests` — withdraw happy path, state guards, contract event emission.

## Open questions

- The vision-doc fee story names a `FeePercentage` captured from "platform config at publish time and fixed for the life of the listing" (`domain-events.md` line 34). In code that capture is a literal `0.10m`. There is no platform-config seam — this is a known gap recorded above; not an open question.
- No question raised by the BC's code that the BC's code cannot resolve.
