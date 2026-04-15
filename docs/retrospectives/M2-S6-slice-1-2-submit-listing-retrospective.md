# M2-S6: Slice 1.2 — Submit Listing — Retrospective

**Date:** 2026-04-15
**Milestone:** M2 — Listings Pipeline
**Slice:** S6 — Slice 1.2: SubmitListing handler (3-event atomic chain), ListingPublished integration contract, RabbitMQ publish rule, package upgrades
**Agent:** @PSA
**Prompt:** `docs/prompts/M2-S6-slice-1-2-submit-listing.md`

---

## Baseline

- 34 tests passing: Selling (26), Participants (6), Api (1), Contracts (1)
- `dotnet build` succeeds with 0 errors, 0 warnings
- `SellerListing` aggregate had `Apply(DraftListingCreated)` and `Apply(DraftListingUpdated)` — no submit lifecycle methods
- `ListingStatus` enum already had all 5 values (`Draft`, `Submitted`, `Published`, `Rejected`, `Withdrawn`) — defined ahead of use in S5
- `WolverineFx.*` packages at 5.30.0; `Microsoft.NET.Test.Sdk` at 18.3.0
- No `Contracts.Selling/` directory existed — only `SellerRegistrationCompleted.cs` at Contracts root

---

## Items completed

| Item | Description |
|------|-------------|
| S6a | Package upgrades: WolverineFx.* 5.30.0→5.31.0, Microsoft.NET.Test.Sdk 18.3.0→18.4.0 |
| S6b | `CritterBids.Contracts.Selling.ListingPublished` sealed record — full payload for all 3 future consumers |
| S6c | 4 domain events: `ListingSubmitted`, `ListingApproved`, `ListingRejected`, `ListingPublished` in `CritterBids.Selling` |
| S6d | `SellerListing` aggregate — added `Format`, `Duration`, `ExtendedBidding*`, `PublishedAt` fields; `Apply()` for all 4 new events |
| S6e | `ListingValidator.Validate(SellerListing)` overload — delegates to `Validate(CreateDraftListing)` via field mapping |
| S6f | `SubmitListing` command + `SubmitListingHandler` — guard, 3-event happy path, rejection path |
| S6g | `SellingModule.cs` `ConfigureMarten()` — 4 new `AddEventType<T>()` registrations |
| S6h | `Program.cs` — `PublishMessage<Contracts.Selling.ListingPublished>().ToRabbitQueue("listings-selling-events")` |
| S6i | `SubmitListingTests.cs` — 4 aggregate tests covering scenarios 2.1–2.4 |

---

## S6a: Package upgrades

**`Directory.Packages.props` after:**

```xml
<PackageVersion Include="WolverineFx" Version="5.31.0" />
<PackageVersion Include="WolverineFx.Http" Version="5.31.0" />
<PackageVersion Include="WolverineFx.Http.Polecat" Version="5.31.0" />
<PackageVersion Include="WolverineFx.Http.Marten" Version="5.31.0" />
<PackageVersion Include="WolverineFx.Marten" Version="5.31.0" />
<PackageVersion Include="WolverineFx.Polecat" Version="5.31.0" />
<PackageVersion Include="WolverineFx.RabbitMQ" Version="5.31.0" />
<PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
```

`dotnet build` passes immediately after bump — no API breakage between 5.30.0 and 5.31.0.

---

## S6b: `CritterBids.Contracts.Selling.ListingPublished`

**Placement:** `src/CritterBids.Contracts/Selling/ListingPublished.cs`. New `Selling/` subdirectory created.

**Contract fields:**

```csharp
public sealed record ListingPublished(
    Guid ListingId, Guid SellerId, string Title, string Format,
    decimal StartingBid, decimal? ReservePrice, decimal? BuyItNow,
    TimeSpan? Duration, bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow, TimeSpan? ExtendedBiddingExtension,
    decimal FeePercentage, DateTimeOffset PublishedAt);
```

**`Format` field type:** `string` — avoids cross-BC enum coupling. The `ListingFormat` enum is internal to `CritterBids.Selling`. Consumers receive `"Flash"` or `"Timed"` and can define their own enum if needed. Moving `ListingFormat` to Contracts was considered but rejected as out-of-scope churn that would require refactoring existing S5 code.

**`FeePercentage`:** Hardcoded `0.10m` in the handler (placeholder for M5 fee engine). No existing fee abstraction found in the codebase.

**Consumer table satisfied:**

| Consumer | Fields | Status |
|----------|--------|--------|
| Listings BC (M2) | `ListingId`, `SellerId`, `Title`, `Format`, `StartingBid`, `PublishedAt` | ✅ present |
| Auctions BC (M3) | `ExtendedBiddingEnabled`, `ExtendedBiddingTriggerWindow`, `ExtendedBiddingExtension`, `Duration` | ✅ present |
| Settlement BC (M5) | `ReservePrice`, `FeePercentage` | ✅ present |

---

## S6c: Domain events

Four sealed records in `CritterBids.Selling`:

| Event | Fields |
|-------|--------|
| `ListingSubmitted` | `ListingId`, `SellerId`, `SubmittedAt` |
| `ListingApproved` | `ListingId`, `ApprovedAt` |
| `ListingRejected` | `ListingId`, `RejectionReason`, `RejectedAt` |
| `ListingPublished` | `ListingId`, `PublishedAt` |

`ListingPublished` (domain event) is intentionally slim — only `ListingId` + `PublishedAt`. The full listing payload lives in `Contracts.Selling.ListingPublished`. This avoids payload duplication and keeps the domain event stream compact.

**Naming collision handling:** Both `CritterBids.Selling.ListingPublished` and `CritterBids.Contracts.Selling.ListingPublished` exist. The handler file uses:
```csharp
using ContractListingPublished = CritterBids.Contracts.Selling.ListingPublished;
```
`Program.cs` uses the fully qualified name to avoid ambiguity with `using CritterBids.Selling;`.

---

## S6d: `SellerListing` aggregate additions

**Fields added:**

| Field | Type | Set by |
|-------|------|--------|
| `Format` | `ListingFormat` | `Apply(DraftListingCreated)` |
| `Duration` | `TimeSpan?` | `Apply(DraftListingCreated)` |
| `ExtendedBiddingEnabled` | `bool` | `Apply(DraftListingCreated)` |
| `ExtendedBiddingTriggerWindow` | `TimeSpan?` | `Apply(DraftListingCreated)` |
| `ExtendedBiddingExtension` | `TimeSpan?` | `Apply(DraftListingCreated)` |
| `PublishedAt` | `DateTimeOffset?` | `Apply(ListingPublished)` |

These fields are required both for the validator adapter (S6e) and for constructing the outgoing integration contract.

**New `Apply()` methods:**

```csharp
public void Apply(ListingSubmitted @event) => Status = ListingStatus.Submitted;
public void Apply(ListingApproved @event) => Status = ListingStatus.Published;
public void Apply(ListingRejected @event) => Status = ListingStatus.Rejected;
public void Apply(ListingPublished @event) { Status = ListingStatus.Published; PublishedAt = @event.PublishedAt; }
```

`Apply(ListingApproved)` and `Apply(ListingPublished)` both set `Status = Published` — idempotent as required by the prompt. `Apply(ListingPublished)` additionally records `PublishedAt`.

---

## S6e: `ListingValidator.Validate(SellerListing)` overload

**Why this approach:** The existing `Validate(CreateDraftListing)` is the single source of truth for all 14 validation rules. Rather than duplicating logic or modifying the validator's signature, an overload bridges the `SellerListing` → `CreateDraftListing` mapping:

```csharp
public static ValidationResult Validate(SellerListing listing) =>
    Validate(new CreateDraftListing(
        listing.SellerId, listing.Title, listing.Format, listing.StartingBid,
        listing.ReservePrice, listing.BuyItNowPrice, listing.Duration,
        listing.ExtendedBiddingEnabled, listing.ExtendedBiddingTriggerWindow,
        listing.ExtendedBiddingExtension));
```

This was required because the aggregate previously stored only 7 fields (Id, SellerId, Title, StartingBid, ReservePrice, BuyItNowPrice, Status) — 5 fields needed for validation (Format, Duration, ExtendedBidding*) were absent. Adding those to the aggregate unblocked both the validator adapter and the outgoing contract construction.

---

## S6f: `SubmitListingHandler`

**Return type:** `(Events, OutgoingMessages)` — `Events` first, `OutgoingMessages` second. Position is load-bearing for Wolverine dispatch.

**Guard:**
```csharp
if (listing.Status != ListingStatus.Draft && listing.Status != ListingStatus.Rejected)
    throw new InvalidListingStateException(
        $"Cannot submit listing in {listing.Status} state. Only Draft or Rejected listings can be submitted.");
```

Throws on anything other than `Draft` or `Rejected` — allows resubmission after correction (scenario 2.3).

**Happy path (validation passes):**
1. `events.Add(new ListingSubmitted(...))`
2. `events.Add(new ListingApproved(...))`
3. `events.Add(new ListingPublished(...))`
4. `outgoing.Add(new ContractListingPublished(..., FeePercentage: 0.10m, ...))`

**Rejection path (validation fails):**
1. `events.Add(new ListingSubmitted(...))`
2. `events.Add(new ListingRejected(..., validation.Reason!, ...))`
3. `outgoing` remains empty

---

## S6g: `SellingModule.cs` event registrations

```csharp
opts.Events.AddEventType<ListingSubmitted>();
opts.Events.AddEventType<ListingApproved>();
opts.Events.AddEventType<ListingRejected>();
opts.Events.AddEventType<ListingPublished>();
```

Required for `UseMandatoryStreamTypeDeclaration` (set in `Program.cs`) and to prevent silent `null` returns from `AggregateStreamAsync<T>` when replaying streams containing these event types (anti-pattern #9 in `marten-event-sourcing.md`).

---

## S6h: RabbitMQ publish rule placement

**Note on prompt vs implementation:** The prompt instructs "In `AddSellingModule()`, add: `opts.PublishMessage<...>()`". However, `AddSellingModule()` returns `IServiceCollection` and has no `WolverineOptions` access. The established M2-S3 pattern puts all RabbitMQ routing rules in `Program.cs` inside `UseWolverine()`. This session follows that pattern:

```csharp
// In Program.cs UseWolverine() RabbitMQ-guarded block:
opts.PublishMessage<CritterBids.Contracts.Selling.ListingPublished>()
    .ToRabbitQueue("listings-selling-events");
```

Moving BC-level routing into the BC module itself would require either extending `AddSellingModule()` to accept `WolverineOptions` or using `services.Configure<WolverineOptions>()`. That refactor is deferred — the routing works correctly in `Program.cs` and is consistent with the existing `SellerRegistrationCompleted` rule.

---

## Test results

| Phase | Selling | Participants | Api | Contracts | Total | Result |
|-------|---------|-------------|-----|-----------|-------|--------|
| Baseline (before S6) | 26 | 6 | 1 | 1 | 34 | ✅ |
| After S6 implementation | 30 | 6 | 1 | 1 | **38** | ✅ |

**New tests:**

| Test file | Scenarios covered | Count |
|-----------|-------------------|-------|
| `SubmitListingTests.cs` | 2.1–2.4 | 4 |

---

## Build state at session close

- `dotnet build` exits with 0 errors, 0 warnings
- `dotnet test` 38/38 passing
- `(Events, OutgoingMessages)` tuple — `Events` first: ✅
- `OutgoingMessages` usage — never `IMessageBus` directly: ✅
- `[WriteAggregate]` on handler parameter: ✅
- `AddEventType<T>()` calls in `ConfigureMarten()`: 4 new (6 total in Selling BC)
- `AddPolecat()` calls in production code: 0
- `PublishMessage<T>()` routing rules in `Program.cs`: 2 (SellerRegistrationCompleted + ListingPublished)
- Domain `ListingPublished` events registered in `ConfigureMarten()`: ✅
- `FeePercentage` hardcoded `0.10m`: ✅

---

## Key learnings

1. **`SellerListing` aggregate field coverage must match all integration contract fields.** The S5 aggregate stored only 7 fields — enough for the draft lifecycle but not for submission. `ListingPublished` integration contract requires `Format`, `Duration`, and `ExtendedBidding*`. Identifying this gap before implementation (by checking `Apply()` method needs against contract fields) prevents mid-implementation rework.

2. **Validator adapter pattern: prefer overload over inline construction.** When the validator accepts a command type but the handler has an aggregate, adding a `Validate(SellerListing)` overload that delegates to `Validate(CreateDraftListing)` keeps the rule-set single-sourced and keeps the handler code clean. The alternative — inline command construction in the handler — works but pollutes handler logic with mapping concerns.

3. **`using` alias disambiguates same-name types across namespaces.** `CritterBids.Selling.ListingPublished` (domain) and `CritterBids.Contracts.Selling.ListingPublished` (integration) coexist without ambiguity via `using ContractListingPublished = CritterBids.Contracts.Selling.ListingPublished;` in the handler file, and fully qualified names in `Program.cs`. This is a common pattern when domain events double as integration event bases.

4. **Slim domain events vs rich integration contracts.** The domain `ListingPublished` carries only `ListingId` + `PublishedAt` — the minimum needed to update aggregate state. The integration contract carries 13 fields for all downstream consumers. This separation keeps the event stream compact while satisfying the `integration-messaging.md` L2 rule (design contracts for all consumers before first publish).

5. **RabbitMQ routing belongs in `Program.cs` given current `AddSellingModule()` signature.** The skill files intend routing to live in BC modules, but without `WolverineOptions` access in `AddSellingModule()`, `Program.cs` is the only viable location. A future refactor could thread `WolverineOptions` into module extensions, but that's a separate architectural investment.

---

## Verification checklist

- [x] `Directory.Packages.props`: all `WolverineFx.*` entries read `5.31.0`
- [x] `Directory.Packages.props`: `Microsoft.NET.Test.Sdk` reads `18.4.0`
- [x] `dotnet build` passes with 0 errors, 0 warnings immediately after package version bumps
- [x] `ListingSubmitted`, `ListingApproved`, `ListingRejected`, `ListingPublished` sealed records exist in `CritterBids.Selling`
- [x] `SellerListing` has `Apply()` methods for all four new events with correct status transitions
- [x] `SubmitListingHandler.Handle` returns `(Events, OutgoingMessages)` and appends the 3-event chain on the happy path
- [x] `SubmitListingHandler` calls `ListingValidator.Validate()` and produces `ListingSubmitted + ListingRejected` on validation failure (scenario 2.2)
- [x] `SubmitListingHandler` allows resubmission from `Rejected` state (scenario 2.3)
- [x] `CritterBids.Contracts/Selling/ListingPublished.cs` exists with full payload (all future-consumer fields present)
- [x] `AddSellingModule()` registers all four new event types via `AddEventType<T>()` in `ConfigureMarten()`
- [x] `Program.cs` declares `PublishMessage<Contracts.Selling.ListingPublished>().ToRabbitQueue("listings-selling-events")` (see S6h note on placement)
- [x] `SubmitListingTests.cs`: all 4 tests pass
- [x] `dotnet build` passes with 0 errors, 0 warnings at session close
- [x] `dotnet test` passes with 38/38 (existing 34 + 4 new)

---

## Files changed

**New — Contracts:**
- `src/CritterBids.Contracts/Selling/ListingPublished.cs` — integration contract, 13 fields

**New — Selling BC:**
- `src/CritterBids.Selling/ListingSubmitted.cs` — domain event
- `src/CritterBids.Selling/ListingApproved.cs` — domain event
- `src/CritterBids.Selling/ListingRejected.cs` — domain event
- `src/CritterBids.Selling/ListingPublished.cs` — domain event (slim)
- `src/CritterBids.Selling/SubmitListing.cs` — command + handler

**New — Tests:**
- `tests/CritterBids.Selling.Tests/SubmitListingTests.cs` — 4 aggregate tests

**Modified — Selling BC:**
- `src/CritterBids.Selling/SellerListing.cs` — 6 new fields; 4 new `Apply()` methods; `Apply(DraftListingCreated)` stores Format/Duration/ExtendedBidding*
- `src/CritterBids.Selling/ListingValidator.cs` — `Validate(SellerListing)` overload added
- `src/CritterBids.Selling/SellingModule.cs` — 4 `AddEventType<T>()` calls added

**Modified — Infrastructure:**
- `Directory.Packages.props` — WolverineFx 5.31.0, Test.Sdk 18.4.0
- `src/CritterBids.Api/Program.cs` — `PublishMessage<ListingPublished>().ToRabbitQueue("listings-selling-events")`

---

## What remains / next session should verify

- **`[WriteAggregate]` stream-ID convention** — `SubmitListing` has `ListingId`; Wolverine looks for `SellerListingId` by convention for aggregate type `SellerListing`. This has never been exercised through the full Wolverine pipeline because there is no HTTP endpoint for `SubmitListing` in M2. If a future session adds the endpoint, verify that Wolverine correctly resolves the stream ID or add a `[WriteAggregate("ListingId")]` attribute override.
- **`ListenToRabbitQueue("listings-selling-events")` in Listings BC** — deferred to S7 per prompt. The publish side is wired; the consume side is S7 scope.
- **`CatalogListingView` projection** — S7 scope.
- **`ListingFormat` enum placement** — currently in `CritterBids.Selling`; integration contract uses `string Format`. Moving the enum to `CritterBids.Contracts.Selling` would enable typed sharing across BCs. Deferred to S8 skills/retro pass.
- **RabbitMQ routing in BC modules** — `AddSellingModule()` currently has no `WolverineOptions` access. `Program.cs` owns all routing rules. A future refactor to thread `WolverineOptions` into module extensions would move routing to the owning BC module. Deferred.
