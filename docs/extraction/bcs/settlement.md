# Settlement BC Dossier

**Maturity:** Implemented (25 cs files in `src/CritterBids.Settlement/`; 3 contracts in `src/CritterBids.Contracts/Settlement/`; 7 test classes in `tests/CritterBids.Settlement.Tests/`).

Source of truth: `src/CritterBids.Settlement/`, `src/CritterBids.Contracts/Settlement/`, `tests/CritterBids.Settlement.Tests/`. Cross-cuts read at `src/CritterBids.Api/Program.cs`.

---

## Purpose

Owns the seven-phase financial workflow that begins when a listing terminates with a winning outcome and ends with seller-payout-issued and settlement-completed. Drives the platform fee deduction, the winner's credit-ledger debit, and the seller's credit-ledger entry. Per ADR-019, the workflow is implemented as a Wolverine Saga with a per-settlement financial event stream as audit ground.

## Module bootstrap (`SettlementModule.cs`)

`services.AddSettlementModule()` contributes to the shared `IDocumentStore` via `services.ConfigureMarten(...)`:

- **Documents (schema `settlement`):**
  - `SettlementSaga` — `Identity(x => x.Id)`, `UseNumericRevisions(true)` (Wolverine `Saga`; document key = deterministic UUID v5 SettlementId)
  - `PendingSettlement` — natural-key-as-id (`Id == ListingId`); tolerant-upsert handler; no numeric revisions
  - `FinancialEventStream` — stream-type marker class for the per-settlement audit stream (required by `opts.Events.UseMandatoryStreamTypeDeclaration = true` in Program.cs)
  - `BidderCreditView` — natural-key-as-id (`Id == BidderId`); tolerant-upsert handler; no numeric revisions
- **Event types registered:** `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`, `SellerPayoutIssued`, `SettlementCompleted`, `PaymentFailed`. Settlement-internal events and integration contracts are both registered (the integration contracts are appended to the financial event stream AND emitted on the bus — per `SettlementModule.cs:54-58`).
- **Retry policy (`SettlementsConcurrencyRetryPolicies`, `SettlementsConcurrencyRetryPolicies.cs:20-27`):**
  - `PendingSettlementNotFoundException` → cooldown 100ms, 250ms, 500ms (W003 Phase 1 Part 1 Option A — projection-lag race; ~850ms cumulative budget; in practice the race rarely fires because `ListingPublished` precedes `ListingSold` by hours/days).

## Aggregates and projections

### `SettlementSaga` (Wolverine `Saga`, `SettlementSaga.cs`)

Single mutable document; persists via Marten under a deterministic UUID v5 `Id`. Owns the seven-phase progression as one state machine with `Status` field guards. State (`SettlementStatus`): `Initiated`, `ReserveChecked`, `WinnerCharged`, `FeeCalculated`, `PayoutIssued`, `Completed`, `Failed`.

Fields: `Id`, `ListingId`, `WinnerId`, `SellerId`, `HammerPrice`, `ReservePrice`, `FeePercentage`, `FeeAmount`, `SellerPayout`, `ReserveWasMet`, `Status`, `FailureReason`.

Each phase appends one event to the financial event stream at `Id` and either self-sends a continuation command or emits a terminal integration event via `OutgoingMessages`. `MarkCompleted()` at `Completed` and `Failed` removes the saga document; the financial event stream persists as audit (W003 §"Financial Event Stream").

### `PendingSettlement` projection (`PendingSettlement.cs` + handler)

Settlement-local cache of the listing data the saga needs at workflow-start time — reserve price, BIN price, fee percentage, seller identity — without crossing the Settlement / Selling boundary. **Status (`PendingSettlementStatus`):** `Pending`, `Consumed`, `Expired`, `Failed`. Terminal statuses are absorbing — a handler only transitions when current status is `Pending`.

Lifecycle (5 cross-BC events, all on the tolerant-upsert pattern from `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert"):

| Event | Source BC | Effect (workshop §) |
| --- | --- | --- |
| `ListingPublished` | Selling | Creates Pending row (§8.1); idempotent on re-delivery (§8.8) |
| `ListingPassed` | Auctions | Pending → Expired (§8.4) |
| `ListingWithdrawn` | Selling | Pending → Expired (§8.5) |
| `SettlementCompleted` | Settlement self-publish | Pending → Consumed (§8.6) |
| `PaymentFailed` | Settlement self-publish | Pending → Failed (§8.7) |

Field-name drift: source `ListingPublished.BuyItNow` renames to `BuyItNowPrice` on this projection to match W003 Phase 1 Part 1 schema sketch and §8 scenario vocabulary (`PendingSettlement.cs:15-18`).

### `BidderCreditView` projection (`BidderCreditView.cs` + handler)

Per-bidder credit balance, surfaces remaining credit for the post-M5 Relay broadcast on `SettlementCompleted`. Fields: `BidderId`, `RemainingCredit`, `LastChargedSettlementId`, `UpdatedAt`. Marten document key is `BidderId` (via the `Id => BidderId` expression-bodied alias).

Lifecycle (2 events):

- `ParticipantSessionStarted` (Participants integration, queue `settlement-participants-events`) — seeds `RemainingCredit = CreditCeiling`. Idempotent: if `LastChargedSettlementId is not null` the existing row is preserved (re-seeding would erase debits).
- `WinnerCharged` (Settlement-internal, saga-emitted) — debits `RemainingCredit -= Amount`, sets `LastChargedSettlementId = SettlementId`. Idempotent via `LastChargedSettlementId == message.SettlementId` early-return. **Lazy-init:** if no prior row exists for the winner, creates one with `RemainingCredit = -Amount` — negative sentinel marks "no prior session-started seed" as data (`BidderCreditView.cs:17-22`, `BidderCreditViewHandler.cs:65-74`).

**M4-D4 duplicate-projection pattern, first lived application** (Auctions `ParticipantCreditCeiling` followed at M4-S4). Each BC consumes `ParticipantSessionStarted` on a BC-specific queue.

### `FinancialEventStream` (`FinancialEventStream.cs`)

Stream-type marker class only — empty contract beyond `public Guid Id { get; set; }`. Required by Marten's mandatory-stream-type-declaration rule (`UseMandatoryStreamTypeDeclaration = true` in Program.cs:188). Sole purpose: satisfying `session.Events.StartStream<FinancialEventStream>(sagaId, ...)` at the saga's first event. Not projected to a live aggregate; persists as audit and is never deleted (per docstring).

## Self-send continuation commands

All carry a single `Guid SettlementId` field; `FailSettlement` adds `string Reason`. Used as the saga's internal step chain (one `Handle(...)` per phase):

| Command | Emitted by | Effect (advances to) |
| --- | --- | --- |
| `CheckReserve` | `StartSettlementSagaHandler` (Bidding source) | `ReserveChecked` |
| `ChargeWinner` | `Handle(CheckReserve)` happy path / `StartSettlementSagaHandler` (BIN source) | `WinnerCharged` |
| `CalculateFee` | `Handle(ChargeWinner)` | `FeeCalculated` |
| `IssueSellerPayout` | `Handle(CalculateFee)` | `PayoutIssued` |
| `CompleteSettlement` | `Handle(IssueSellerPayout)` | `Completed` (terminal happy path; `MarkCompleted()`) |
| `FailSettlement` | `Handle(CheckReserve)` reserve-not-met branch | `Failed` (terminal failure path; `MarkCompleted()`) |

## Integration events published (`CritterBids.Contracts.Settlement.*`)

The integration-out set is exactly three events per W003 §"Integration in/out":

| Event | Emitted from phase | Published to (per Program.cs) |
| --- | --- | --- |
| `SellerPayoutIssued` | `Handle(IssueSellerPayout)` | RabbitMQ queue `relay-settlement-events` (Program.cs:155-156; consumer post-M5) |
| `SettlementCompleted` | `Handle(CompleteSettlement)` | RabbitMQ queue `listings-settlement-events` (Program.cs:145-147; consumer at M5-S6) |
| `PaymentFailed` | `Handle(FailSettlement)` | RabbitMQ queue `operations-settlement-events` (Program.cs:162-163; consumer post-M5 — no `ListenToRabbitQueue` wired yet) |

All three are also appended to the financial event stream at `Id` for audit (`SettlementSaga.cs:165-167`, `199-201`, `225-228`).

## Stream-internal events (not in Contracts)

Per W003 §"Integration in/out" and individual event docstrings, these events live entirely inside the financial event stream and have no cross-BC consumer:

- `SettlementInitiated` — first event in every stream (8-field W003-canonical payload + `InitiatedAt`); carries `SettlementSource` (`Bidding` / `BuyItNow`)
- `ReserveCheckCompleted` — appended at `Handle(CheckReserve)`; **absent for BIN-source settlements** (the absence itself is the §9.2 audit signal "this was a BIN settlement", `StartSettlementSagaHandler.cs:137-143`)
- `WinnerCharged` — appended at `Handle(ChargeWinner)`; also consumed by `BidderCreditViewHandler` for debiting
- `FinalValueFeeCalculated` — appended at `Handle(CalculateFee)`

## Source overloads — Bidding vs BIN

`StartSettlementSagaHandler` has two overloads (`StartSettlementSagaHandler.cs:41` and `:100`):

| Aspect | `Handle(ListingSold)` — bidding | `Handle(BuyItNowPurchased)` — BIN |
| --- | --- | --- |
| Initial `Status` | `Initiated` | `ReserveChecked` (directly) |
| Initial `ReserveWasMet` | `false` (set by `Handle(CheckReserve)`) | `true` |
| Appends `ReserveCheckCompleted` | Yes (later, at `Handle(CheckReserve)`) | **No** — absence is the canonical audit signal (W003 §9.2) |
| First self-send | `CheckReserve` | `ChargeWinner` |
| `SettlementInitiated.Source` | `SettlementSource.Bidding` | `SettlementSource.BuyItNow` |
| `SettlementInitiated.Price` | `message.HammerPrice` | `message.Price` |

Both overloads share:
- `PendingSettlement` load (throws `PendingSettlementNotFoundException` on absent — retry policy re-queues).
- Deterministic `SettlementId` via `SettlementsIdentityNamespaces.SettlementId(listingId)`.
- Existing-saga idempotency check via `LoadAsync<SettlementSaga>` — early-return `(null, empty)` if re-delivery.
- Same financial-event-stream initiation via `session.Events.StartStream<FinancialEventStream>`.

The same listing can only settle once across sources — Auctions enforces "BIN removes after first bid" per M3 lived ground, so existing-saga collisions are always re-deliveries of the same source event (`StartSettlementSagaHandler.cs:34-37`).

## Failure path (reserve-not-met)

Failure-path event stream has exactly three events (W003 §9.3): `SettlementInitiated`, `ReserveCheckCompleted(WasMet: false)`, `PaymentFailed`.

- `Handle(CheckReserve)` reserve-not-met branch self-sends `FailSettlement(Id, "ReserveNotMet")` (`SettlementSaga.cs:86-94`).
- `Handle(FailSettlement)` appends `PaymentFailed` to the stream, emits the integration event, sets `FailureReason = command.Reason`, transitions to `Failed`, `MarkCompleted()` (`SettlementSaga.cs:185-206`).
- `Reason` vocabulary is open-ended free-form string; M5 produces only `"ReserveNotMet"`. Post-MVP failure modes (insufficient credit, payment-provider rejection, ledger divergence) extend the set without contract change (`FailSettlement.cs:11-15`, `PaymentFailed.cs:33-36`).

## State guards

Every saga handler (except `Handle(FailSettlement)`) checks the saga's current `Status` matches the expected pre-phase value; throws `InvalidSettlementTransitionException(Id, Status, phaseName)` on mismatch. `Handle(FailSettlement)` guards only the two terminal states (Completed, Failed) — post-MVP failure modes may dispatch from later phases. Re-delivery of a continuation command after the saga has advanced past the corresponding phase throws (`SettlementSaga.cs:24-30`).

## Fee math

Banker's rounding per W003 §4.2 MVP convention (`SettlementSaga.cs:131-138`):

```
feeAmount = Math.Round(HammerPrice * FeePercentage, 2, MidpointRounding.ToEven)
sellerPayout = HammerPrice - feeAmount
```

`FeePercentage` is carried as the multiplicative ratio (e.g. `0.10m` for 10%) per the `ListingPublished` contract's placeholder; multiply directly without dividing by 100. The placeholder source is hardcoded in Selling — see `selling.md` for the Finding 001 drift.

## Identifiers

- **`SettlementSaga.Id` == `SettlementsIdentityNamespaces.SettlementId(listingId)` == UUID v5(namespace, `$"settlement:{listingId}"`)** (`SettlementsIdentityNamespaces.cs:30-31`). Same `ListingId` always derives the same id — duplicate event consumption deduplicates against the same document key.
- **First lived UUID v5 in CritterBids** (`SettlementsIdentityNamespaces.cs:18-21`). Auctions' Proxy Bid Manager saga's composite-key UUID v5 followed at M4-S3.
- **W003 Phase 1 Part 6 referenced "AuctionsNamespace"** — corrected at M5-S4 to use Settlement's own namespace per BC-isolation discipline (`SettlementsIdentityNamespaces.cs:10-13`). Workshop-doc drift, fixed in code.

## Tests (`tests/CritterBids.Settlement.Tests/`)

`BidderCreditViewTests`, `PendingSettlementHandlerTests`, `SellerPayoutIssuedPublishRouteTests`, `SettlementModuleTests`, `SettlementSagaBinSourceTests`, `SettlementSagaFailurePathsTests`, `SettlementSagaTests`.

## Notable internal conventions

- Field-name convention from W003 Phase 1 Part 2 (M5-S1 F002 amendment): `Price` is source-agnostic at initiation (`SettlementInitiated`); `HammerPrice` is the post-initiation runtime field name on the saga and downstream events. `WinnerCharged.Amount` uses payment-domain vocabulary at the moment money moves (distinct from `HammerPrice`'s auction-domain vocabulary) (`WinnerCharged.cs:16-19`).
- `ReserveCheckCompleted` deliberately omits a `SettlementId` field per W003's stream-internal scoping rule — the event is scoped to its stream and downstream consumers don't need the correlation field (`ReserveCheckCompleted.cs:15-18`).
- The `BidderCreditView.RemainingCredit` negative sentinel is the M5-S5 lazy-init posture: charges arriving before sessions are still recorded as debits; the absent prior state is preserved as data, not normalized away (`BidderCreditView.cs:17-22`).
- `PendingSettlementHandler` and `BidderCreditViewHandler` both follow the tolerant-upsert pattern: `LoadAsync` → branch on present/absent → mutate via record `with` → `session.Store`. No `OutgoingMessages`, no `IMessageBus`. `AutoApplyTransactions()` commits after `Handle` returns.

## Drift items captured for `gaps-and-drift.md`

- W003 Phase 1 Part 6 specified using `AuctionsNamespace` for the SettlementId derivation; M5-S4 corrected to a Settlement-owned namespace per BC isolation. Workshop-doc drift, fixed in code (`SettlementsIdentityNamespaces.cs:10-13`).
- M5-S3 milestone doc §2 lists `ListingSold` + `BuyItNowPurchased` only on the `settlement-auctions-events` queue; the actual implementation extends with `ListingPassed` (queue-payload extension recorded at M5-S3 scoping, Program.cs:110-119).
- `PaymentFailed` has a `PublishMessage` route to `operations-settlement-events` but no `ListenToRabbitQueue` (Operations BC not yet shipped); the publish-side is structural completeness only (Program.cs:158-163).
- `SellerPayoutIssued` has a `PublishMessage` route to `relay-settlement-events` but no `ListenToRabbitQueue` (Relay BC not yet shipped); same shape as PaymentFailed (Program.cs:149-156).

## Open questions / fixture-stance items captured

- W003 Phase 1 Part 3 compensation design is deferred — only the reserve-not-met failure path is implemented at M5. Real-payment-processor failure modes (which require compensation for already-emitted `SellerPayoutIssued` events) are post-MVP.
- `Reason` field on `FailSettlement` / `PaymentFailed` is free-form string; vocabulary is open-ended. M5 produces only `"ReserveNotMet"` (`FailSettlement.cs:11-15`).
