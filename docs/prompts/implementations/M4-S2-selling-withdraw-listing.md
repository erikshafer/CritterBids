# M4-S2: Selling `WithdrawListing` Command + Real `ListingWithdrawn` Producer

**Milestone:** M4 — Auctions BC Completion
**Session:** S2 of 7 (plus pre-drafted S4b and S5b split slots)
**Prompt file:** `docs/prompts/implementations/M4-S2-selling-withdraw-listing.md`
**Baseline:** 86 tests passing · `dotnet build` 0 errors, 0 warnings · M4-S1 complete

---

## Goal

Author the Selling-side `WithdrawListing` command and its aggregate handler, then wire the resulting
`ListingWithdrawn` integration event through the existing RabbitMQ topology so Auctions and Listings
each receive it on their own consumer queue. At session close the M3-era test-fixture synthesis of
`ListingWithdrawn` stops being a production-path placeholder — the Auction Closing saga's terminal
scenario 3.10 (already green in M3-S5b) now runs against a real Selling producer in at least one
integration test, and the fixture helper survives only as an isolated unit-test shortcut per the
M4-D6 disposition below.

M4-S2 lands four scenarios — happy path, reject-not-published, reject-already-closed, and a
dispatch test through `IMessageBus`. It is the smallest implementation session in M4 (M4 plan §9:
"smaller than it looks"), but its cross-BC coordination is what justifies its own slot: the same
event now has two distinct transport queues and two consuming BCs, and the end-to-end replacement
of the M3 fixture fiction is the milestone-level gate for M4-S3's proxy saga work. Starting S3
with `ListingWithdrawn` still synthesized by the Auctions test project means the proxy-saga
terminal test (scenario 4.8, scheduled for S4) would repeat the same fiction — not acceptable
per the M4 plan §6 "`ListingWithdrawn` authority" rule.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M4-auctions-bc-completion.md` | Milestone scope — S2 deliverables in §2 Selling table; routing topology in §5; scenarios in §7 `CritterBids.Selling.Tests`; M4-D6 disposition in §8 |
| `docs/skills/wolverine-message-handlers.md` | Handler shape, `[WriteAggregate]` usage, `(Events, OutgoingMessages)` tuple return; mirror `SubmitListingHandler` precedent |
| `docs/skills/integration-messaging.md` | L2 discipline — contract already final in S1; this session is publisher-side wiring + producer authoring |
| `docs/skills/critter-stack-testing-patterns.md` | Dispatch-test fixture shape (Alba + Testcontainers); `ExecuteAndWaitAsync` vs `InvokeMessageAndWaitAsync` semantics |
| `src/CritterBids.Selling/SubmitListing.cs` | Canonical reference for command + handler file pairing, `[WriteAggregate(nameof(...))]`, state-guard exception pattern |
| `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` | The M4-S1 extended contract — final shape, not edited this session |
| `src/CritterBids.Api/Program.cs` | Two new `opts.PublishMessage<ListingWithdrawn>().ToRabbitQueue(...)` lines alongside the existing Selling/Auctions routing block |

Seven entries, all substantive reads. At the README's guidance ceiling.

---

## In scope

- **`WithdrawListing` command record** in `src/CritterBids.Selling/WithdrawListing.cs`. `sealed record`
  shape consistent with `SubmitListing`: carries `ListingId` and `WithdrawnBy` (the seller's
  participant identifier). No `Reason` field on the command — MVP UI does not capture one, and the
  contract event's `Reason` is always null for seller-initiated withdrawal per the `ListingWithdrawn`
  docstring field rationale.

- **`WithdrawListingHandler` static class** — co-located in the same `.cs` file as the command record
  (mirror the `SubmitListing.cs` / `SubmitListingHandler` pairing), or as a separate
  `WithdrawListingHandler.cs` if the session finds a reason to split (the M4 plan §4 layout shows
  both shapes as acceptable). Handler signature:
  `public static (Events, OutgoingMessages) Handle(WithdrawListing cmd, [WriteAggregate(nameof(WithdrawListing.ListingId))] SellerListing listing)`.
  - Happy path: emits one domain event `ListingWithdrawn` (Selling-internal, new type — not the same
    CLR type as the contract; see "Conventions" below) and adds a `CritterBids.Contracts.Selling.ListingWithdrawn`
    to `OutgoingMessages` for RabbitMQ dispatch with `WithdrawnBy` populated from the command,
    `Reason` set to `null`, and `WithdrawnAt` stamped `DateTimeOffset.UtcNow` at handler entry.
  - Reject-not-published: if the listing status is `Draft`, `Submitted`, or `Rejected`, throw
    `InvalidListingStateException` with a message naming the current status. (Same exception type
    as `SubmitListingHandler`; do not invent a parallel exception hierarchy.)
  - Reject-already-closed: if the listing status is already `Withdrawn`, throw the same exception.
    The `Closed` / `Sold` / `Passed` status values do not exist on Selling's `ListingStatus` enum
    — Selling's view of lifecycle ends at `Published` per M2/M3 scope (closure is an Auctions-side
    concept). Reject-already-closed in this session's context means "reject if already withdrawn."
    If session reading surfaces a different interpretation (e.g. listing has already received a
    contract-level `ListingSold` that Selling does not track), stop and flag as an M4-D7 candidate.

- **Domain event `ListingWithdrawn` (Selling-internal)** in
  `src/CritterBids.Selling/ListingWithdrawn.cs`. `sealed record` with the minimum payload Selling's
  aggregate needs for replay (`ListingId`, `WithdrawnAt`) — this is the Selling-stream event,
  distinct from the contract event in `CritterBids.Contracts.Selling`. Pattern mirrors the existing
  split between `CritterBids.Selling.ListingPublished` (domain) and
  `CritterBids.Contracts.Selling.ListingPublished` (contract). If the file or type collides with
  something left over, stop and flag — the two-namespace split is intentional and must be preserved.

- **`SellerListing.Apply(ListingWithdrawn)`** — one-line state transition setting
  `Status = ListingStatus.Withdrawn`. No other field writes; the aggregate does not track
  `WithdrawnAt` or `WithdrawnBy` internally (it is audit data on the event stream, not aggregate
  state).

- **`SellingModule.ConfigureMarten` registration** — add `opts.Events.AddEventType<ListingWithdrawn>()`
  alongside the existing six `AddEventType<T>()` calls. This is the M2 key learning re-applied:
  skipping the registration silently null-returns `AggregateStreamAsync<SellerListing>` when the
  stream contains a `ListingWithdrawn` event under `UseMandatoryStreamTypeDeclaration = true`.

- **Two new publisher-side routing rules** in `src/CritterBids.Api/Program.cs`, inside the existing
  `if (!string.IsNullOrEmpty(rabbitMqUri))` block:
  - `opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>().ToRabbitQueue("auctions-selling-events");`
  - `opts.PublishMessage<CritterBids.Contracts.Selling.ListingWithdrawn>().ToRabbitQueue("listings-selling-events");`
  Both target queues already exist with bound listeners. No new `ListenToRabbitQueue(...)` calls —
  Auctions and Listings each already listen to their respective `*-selling-events` queue from M2/M3.

- **Four `CritterBids.Selling.Tests` additions**:
  - `WithdrawListingTests.WithdrawListing_Published_ProducesListingWithdrawn` — direct handler-call
    test against a pre-built `Published` aggregate; asserts the three-tuple (domain event,
    outgoing contract event with correct field values, `WithdrawnAt` within a sane range).
  - `WithdrawListingTests.WithdrawListing_NotPublished_Rejected` — direct handler-call test against
    a `Draft` aggregate; asserts `InvalidListingStateException`.
  - `WithdrawListingTests.WithdrawListing_AlreadyWithdrawn_Rejected` — direct handler-call test
    against a previously-`Withdrawn` aggregate; asserts `InvalidListingStateException`. Note the
    test method name tracks the real guard (already-withdrawn) rather than the M4 plan §7 row's
    "already-closed" phrasing — Selling has no `Closed` state, and the test name must describe
    what the test actually asserts.
  - `WithdrawListingDispatchTests.WithdrawListing_ViaWolverineDispatch_TransitionsAggregateAndEmitsContractEvent`
    — end-to-end dispatch through `IMessageBus` mirroring `SubmitListingDispatchTests` shape.
    Seeds a `Published` aggregate via `StartStream<SellerListing>`, dispatches
    `WithdrawListing`, then asserts the aggregate status transitioned and the outgoing
    `ListingWithdrawn` contract event landed on the tracked Wolverine session (per
    `tracked.Sent` — see feedback memory "Wolverine outbox tracking requires routing").

- **Auctions-side integration coverage of the real producer**. The M4 plan exit criteria require
  "Auction Closing saga's `Handle(ListingWithdrawn)` is now exercised against the real Selling
  producer (integration test)." The cleanest shape: add one **new** test in
  `CritterBids.Auctions.Tests` (or convert one existing synthesis-based test) that
  (1) dispatches `WithdrawListing` to the Selling aggregate, (2) waits for the RabbitMQ
  fan-out to arrive at Auctions, and (3) asserts the Auction Closing saga transitioned to
  Resolved-by-withdrawal without emitting a closing outcome event. If the shape of the Auctions
  test fixture makes the end-to-end Rabbit path expensive (`DisableAllExternalWolverineTransports`
  in the Auctions fixture blocks it), the acceptable fallback is an in-process Alba test under a
  new test class or a shared composition-root fixture — stop and flag before investing if the
  fixture work exceeds one hour of session budget.
  - **M4-D6 disposition for this session.** The fixture helper
    `AuctionsTestFixture.AppendListingWithdrawnAsync(Guid)` stays in place as a saga-unit-test
    shortcut — rename its docstring only to acknowledge it is "unit-test shortcut for replay
    semantics; real producer integration lives in `<new test class>`." Do not delete the helper
    or the existing `AuctionClosingSagaTests.ListingWithdrawn_TerminatesWithoutEvaluation` test
    (scenario 3.10) — those continue to run against synthesized events for coverage economy.

- **Session retrospective** at
  `docs/retrospectives/M4-S2-selling-withdraw-listing-retrospective.md`.

---

## Explicitly out of scope

- **Ops-staff-initiated withdrawal.** M4 scope is seller-initiated only. `WithdrawnBy` on the
  contract is always a seller participant identifier in M4. Post-M4 ops-staff withdrawal paths
  (abuse, fraud) will add a second producer command; the contract is already shaped for it.
- **`Reason` capture.** No command parameter, no aggregate field, no UI. The contract carries the
  field up-front per M4-S1 but M4 only ever emits `null`.
- **Proxy Bid Manager saga consumption of `ListingWithdrawn`.** Scenario 4.8 is owned by S4.
  S2 does not author the Proxy saga handler; the contract event simply flows onto the
  `auctions-selling-events` queue where S4's handler will subscribe.
- **Listings BC consumption of `ListingWithdrawn`.** `SessionMembershipHandler` (and the
  `CatalogListingView.Status = Withdrawn` transition) lands at S6. S2 wires the publisher side
  only; Listings already has a queue binding on `listings-selling-events` so no listener change
  is required.
- **`ReviseListing`, `EndListingEarly`, `Relist`.** Same deferral as M2–M3. Only `WithdrawListing`
  is authored this session.
- **HTTP endpoint for `WithdrawListing`.** `[AllowAnonymous]` and command-only testing through
  M5 per milestone non-goals. No endpoint; dispatch test uses `IMessageBus`.
- **Selling-side `PublishedListings` projection, `WithdrawnListings` projection, or any
  Selling-owned read model.** Selling is producer-only; consumption-side projections are Auctions
  (S5, M4-D4 option 4) and Listings (S6).
- **Edits to the M4-S1 contract.** `CritterBids.Contracts.Selling.ListingWithdrawn` is final.
  If the session surfaces a missing field, stop and flag — a contract change this session
  re-opens a milestone-level decision (ADR 005 additive still applies, but the S1 acceptance was
  "contracts final at S1 close").
- **Retrospective skill-file edits.** `wolverine-message-handlers.md` and `integration-messaging.md`
  remain untouched this session. No novel pattern is introduced — `WithdrawListing` is a carbon
  copy of `SubmitListing` shape-wise, and no new skill content is earned.
- **New ADR.** No ADR 015 candidate, no ADR 016 candidate. Session is pure implementation inside
  an already-decided vocabulary.
- **Unplanned cross-BC scope creep.** S2 touches `CritterBids.Selling`, `CritterBids.Selling.Tests`,
  `CritterBids.Api/Program.cs` (two new lines in the RabbitMQ block), and at most one new file in
  `CritterBids.Auctions.Tests` for the integration coverage. No changes to
  `CritterBids.Auctions/*.cs`, `CritterBids.Listings/*.cs`, `CritterBids.Contracts/**`,
  `CritterBids.AppHost/*.cs`, or any ADR.

---

## Conventions to pin or follow

- **Domain event vs contract event — two CLR types, same name.** The Selling-internal
  `CritterBids.Selling.ListingWithdrawn` event is stored on the `SellerListing` event stream.
  The outbound contract `CritterBids.Contracts.Selling.ListingWithdrawn` is a separate type with
  the full M4 payload. The handler emits the domain event into `Events` and the contract event
  into `OutgoingMessages`. This two-namespace split is the same shape used by
  `Selling.ListingPublished` (domain) / `Contracts.Selling.ListingPublished` (contract) — rely on
  the compile-time namespace qualification, and use the `using ContractListingWithdrawn = ...`
  alias in the handler file if the un-aliased reference is ambiguous (mirror `SubmitListing.cs`
  line 3's `ContractListingPublished` alias pattern exactly).
- **`[WriteAggregate]` from first commit with explicit `nameof` override.** Command property is
  `ListingId` but the aggregate type is `SellerListing` — the M2 retrospective's fragility finding
  means `[WriteAggregate(nameof(WithdrawListing.ListingId))]` is mandatory; bare `[WriteAggregate]`
  is not equivalent.
- **`WithdrawnAt` is handler-entry timestamp, not outbox dispatch time.** One `DateTimeOffset.UtcNow`
  at handler entry is reused for both the domain event's stamped timestamp (if the domain event
  carries one — it does in the MVP) and the contract event's `WithdrawnAt`. Per the M4-S1
  `ListingWithdrawn` docstring field rationale.
- **State-guard exception is `InvalidListingStateException`**, not a new exception type. Message
  template: `$"Cannot withdraw listing in {listing.Status} state. Only Published listings can be withdrawn."`
  Mirror `SubmitListingHandler` message shape.
- **`AddEventType<ListingWithdrawn>()` in `SellingModule`** — same commit as the domain event type
  itself. The M2 learning (silent `AggregateStreamAsync<T>` null returns on missing registration
  under `UseMandatoryStreamTypeDeclaration`) still applies.
- **Two-queue fan-out is publisher-side only.** Do not add `opts.ListenToRabbitQueue(...)` calls
  — Auctions and Listings already listen to their queues. Adding a second listener on a queue
  already bound in the same Wolverine runtime can cause the sticky-handler failure mode captured
  in the memory "Wolverine ListenToRabbitQueue creates sticky handlers."
- **Dispatch test uses `ExecuteAndWaitAsync` for Selling-only flow; integration test uses
  `InvokeMessageAndWaitAsync` or `TrackActivity` for cross-BC Rabbit-path coverage.** Follow the
  `SubmitListingDispatchTests` shape for the Selling dispatch test; follow the
  `AuctionClosingSagaTests.ListingWithdrawn_TerminatesWithoutEvaluation` shape as a starting
  point for the cross-BC integration test, adjusted to dispatch the real command rather than
  calling the fixture helper.
- **Commit message discipline.** Feature-scoped (`feat(selling):`, `feat(api):`, `test(...):`)
  with concise summaries; no `Co-Authored-By` trailer per CLAUDE.md "Do Not".

---

## Acceptance criteria

- [ ] `src/CritterBids.Selling/WithdrawListing.cs` — new file; `sealed record` command
  `(Guid ListingId, Guid WithdrawnBy)`; `static WithdrawListingHandler` class with a single
  `Handle` method returning `(Events, OutgoingMessages)`, guarded by status checks, emitting
  the domain `ListingWithdrawn` and the `CritterBids.Contracts.Selling.ListingWithdrawn`.
- [ ] `src/CritterBids.Selling/ListingWithdrawn.cs` — new file; Selling-internal domain
  `sealed record` with minimum aggregate-replay payload. Separate CLR type from the contract
  event of the same name.
- [ ] `src/CritterBids.Selling/SellerListing.cs` — new `Apply(ListingWithdrawn)` method setting
  `Status = ListingStatus.Withdrawn`. No other state changes.
- [ ] `src/CritterBids.Selling/SellingModule.cs` — new
  `opts.Events.AddEventType<ListingWithdrawn>()` line added alongside the existing six.
- [ ] `src/CritterBids.Api/Program.cs` — two new `opts.PublishMessage<...>().ToRabbitQueue(...)`
  lines inside the RabbitMQ block routing `CritterBids.Contracts.Selling.ListingWithdrawn` to
  `auctions-selling-events` and `listings-selling-events`. No listener changes. No other
  Program.cs edits.
- [ ] `tests/CritterBids.Selling.Tests/WithdrawListingTests.cs` — three `[Fact]` methods covering
  happy-path, reject-not-published, and reject-already-withdrawn per the list above.
- [ ] `tests/CritterBids.Selling.Tests/WithdrawListingDispatchTests.cs` — one `[Fact]` method
  dispatching via `IMessageBus` through the `SellingTestFixture`, asserting aggregate transition
  and tracked outgoing contract event.
- [ ] `tests/CritterBids.Auctions.Tests/` — one new integration test (or equivalently, one new
  test class) that exercises the Auction Closing saga's `ListingWithdrawn` terminal path driven
  by a real `WithdrawListing` dispatch through Selling — not by `AppendListingWithdrawnAsync`.
  The existing synthesis-based scenario 3.10 test remains in place for coverage economy.
- [ ] `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — docstring of
  `AppendListingWithdrawnAsync` updated to mark it a unit-test shortcut with a pointer to the
  new real-producer integration test. No behaviour change.
- [ ] `dotnet build` — 0 errors, 0 warnings.
- [ ] `dotnet test` — 90 passing (86 baseline + 4 new: 3 Selling + 1 Selling dispatch) — plus
  any new Auctions integration test count (1 expected, so 91 total). No test deletions; no
  regressions elsewhere.
- [ ] `docs/retrospectives/M4-S2-selling-withdraw-listing-retrospective.md` — written; records
  the final handler shape (happy path + two rejects), the domain-vs-contract split decision
  if any friction surfaced, the cross-BC integration test mechanism actually landed (direct
  Rabbit vs in-process vs fallback), any scope deviation, and a one-paragraph "what M4-S3
  should know" note pointing at the Proxy Bid Manager saga authoring.

---

## Open questions

- **Already-closed rejection interpretation.** Selling's `ListingStatus` enum has no `Closed`,
  `Sold`, or `Passed` values — Selling's lifecycle ends at `Published` (or `Withdrawn` after
  this session). The M4 plan §7 row's "reject-already-closed" test name maps to
  "reject-already-withdrawn" in the actual state machine. If session reading surfaces a
  Selling-side need to track Auctions-terminal states (e.g. to reject withdrawal of a
  `ListingSold`), stop and flag as M4-D7 — adding an Auctions→Selling back-channel is a
  milestone-level re-opening, not an S2 decision.
- **Cross-BC integration test transport choice.** Running Selling's `WithdrawListing` through
  a real Rabbit round-trip into Auctions's saga is the cleanest shape but requires either (a) a
  shared fixture that does not disable external Wolverine transports, or (b) an Alba
  composition-root test at the API layer. If the Auctions test fixture's
  `DisableAllExternalWolverineTransports` is load-bearing for isolation of other tests, a
  third option — manually invoking the contract event on the Auctions-side bus to simulate the
  arriving message — is acceptable but loses the routing-path coverage. Stop and flag if the
  first two options both prove expensive; the pick is not an S2 design decision.
- **Domain `ListingWithdrawn` event shape.** The minimum replay payload is `ListingId` and
  `WithdrawnAt`. If session reading suggests the aggregate needs `WithdrawnBy` for any future
  Apply method or query (it does not, in S2 scope), stop and flag — adding fields to an
  event-sourced stream that are not actually replayed is a silent-correctness hazard and
  deserves a deliberate call rather than drift.
- **Fixture-helper fate after M4 close.** The M4 plan §7 "`ListingWithdrawn` authority" rule
  says "any remaining hand-crafted usage in saga tests is a unit-test-only shortcut clearly
  isolated from integration paths." S2's disposition (keep the helper, update docstring only)
  is the MVP-speed path. If the session surfaces a cleaner boundary — e.g. moving the helper
  onto a `SagaReplayShortcuts` static class — record the option for S7 consolidation but do
  not refactor this session.

---

## Commit sequence

Four commits, in this order:

1. `feat(selling): add WithdrawListing command, handler, and domain ListingWithdrawn event`
2. `feat(api): route Selling ListingWithdrawn contract to auctions-selling-events and listings-selling-events`
3. `test(selling): add three WithdrawListing handler tests and one dispatch test`
4. `test(auctions): drive Auction Closing saga terminal path from real Selling producer; write M4-S2 retrospective`

Commit 1 carries the code that shapes the event stream, including the `AddEventType<>` line —
if the test commit landed first, seeded `Published` streams under `UseMandatoryStreamTypeDeclaration`
would fail at test fixture setup. Commit 2 is the routing change that turns the contract event
into an actually-deliverable message; this must land before the Auctions integration test commit
that assumes the fan-out works. Commit 4 bundles the cross-BC integration test with the retro
because the test is the evidence that feeds the retro's "what actually happened cross-BC" section;
splitting them produces two trivial PRs a reviewer evaluates together anyway.
