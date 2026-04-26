# M3-S5: Auction Closing Saga — Skeleton + Forward Path

**Milestone:** M3 — Auctions BC
**Slice:** S5 of 8 (paired with S5b for close evaluation + terminal paths; pre-emptive split per milestone doc §9 S5 risk note — first in-repo saga AND first scheduled-message cancel-and-reschedule would otherwise land together)
**Agent:** @PSA
**Estimated scope:** one PR; 4 new scenario tests; ~5–7 new/modified files
**Baseline:** 68 tests green · `dotnet build` 0 errors, 0 warnings · M3-S4b closed. At S4b close: `PlaceBidHandler` and `BuyNowHandler` both live using the manual-tag + manual-append DCB shape (NOT canonical `[BoundaryModel]` auto-append); `BidConsistencyState` covers all DCB + BIN terminal scenarios; seven `AddEventType<T>()` registrations; `BidRejectionAudit` audit stream operational; both `ConcurrencyException` and `DcbConcurrencyException` retry policies registered via `AuctionsConcurrencyRetryPolicies : IWolverineExtension`; `Listing` live aggregate picks up DCB-tagged events via the primary stream append path.

---

## Goal

Land the Auction Closing saga's infrastructure and its forward-path handlers — the first Wolverine saga in CritterBids and the first use of scheduled messages with cancel-and-reschedule. Scope covers scenarios 3.1–3.4 from `002-scenarios.md` §3: saga starts on `BiddingOpened` and schedules an initial `CloseAuction`, transitions from `AwaitingBids` to `Active` on first bid, tracks `ReserveMet`, and cancels-and-reschedules on `ExtendedBiddingTriggered`. The close-evaluation handler `Handle(CloseAuction)` is a stub that no-ops for the duration of S5 — a TODO comment references S5b. Terminal paths (`BuyItNowPurchased`, `ListingWithdrawn`) and outcome events (`BiddingClosed`, `ListingSold`, `ListingPassed`) are S5b's scope.

Splitting S5 de-risks two first-use Wolverine patterns in one session. Saga base class, Marten saga document, scheduled-message token API, cancel-and-reschedule pattern, and — if Open Question 4 below needs resolving — stream-event-to-bus forwarding all land here with a narrow blast radius. S5b then applies the established pattern to the outcome events and terminal paths.

## Context to load

- `docs/milestones/M3-auctions-bc.md` — §7 saga test rows 3.1–3.4 only, §6 saga conventions, §9 S5 risk notes
- `docs/workshops/002-scenarios.md` — §3 scenarios 3.1, 3.2, 3.3, 3.4 (3.5–3.11 are S5b scope)
- `docs/skills/wolverine-sagas.md` — primary skill; `Saga` base class, Start pattern, scheduled-message + cancel API, Marten configuration, concurrency retry, idempotency guards
- `docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md` — "What M3-S5 should know" section; DCB events the saga consumes; concurrency retry coverage
- `docs/retrospectives/M3-S4b-buy-now-retrospective.md` — "What M3-S5 should know" section; `BuyItNowPurchased` payload and terminal-signal semantics (consumed in S5b but saga Id convention decided here)
- `src/CritterBids.Auctions/AuctionsModule.cs` and `src/CritterBids.Api/Program.cs` — the seven existing `AddEventType<T>()` registrations, both concurrency policies, `IntegrateWithWolverine()` wiring, absence of explicit event-to-bus forwarding (see Open Question 4)
- `C:\Code\JasperFx\wolverine\` — canonical saga, scheduled-message, and Marten-event-forwarding reference code. Specifically worth checking: saga correlation APIs, `ScheduleAsync` / `CancelScheduledAsync` signatures, any `PublishMartenEventsToMessagingInfrastructure` / `SubscribeToEvents` pattern for stream-to-bus forwarding

## In scope (numbered)

1. `src/CritterBids.Auctions/AuctionClosingSaga.cs` — `sealed class AuctionClosingSaga : Saga` with:
   - `public Guid Id { get; set; }` — required by `Saga` base + Marten identity; populated per Open Question 1 resolution
   - State: `ListingId`, `CurrentHighBidderId` (nullable), `CurrentHighBid` (decimal, default 0), `BidCount` (int), `ReserveHasBeenMet` (bool), `ScheduledCloseAt`, `ScheduledCloseMessageId` (the token returned by `ScheduleAsync`, used for cancellation), `ExtendedBiddingEnabled` (bool, captured from `BiddingOpened`), `Status` (enum)
   - `AuctionClosingStatus` enum: `AwaitingBids`, `Active`, `Extended`, `Closing`, `Resolved`. Full set even though `Closing` and `Resolved` are only entered via S5b handlers — declaring the complete enum avoids a type-level churn in S5b.
   - `Handle(BidPlaced)` — AwaitingBids → Active transition on first bid; always updates `CurrentHighBid` / `CurrentHighBidderId` / `BidCount`; idempotency guard per Open Question 2 resolution
   - `Handle(ReserveMet)` — flips `ReserveHasBeenMet` to true; idempotent (no-op if already true); no state transition beyond the flag
   - `Handle(ExtendedBiddingTriggered, IMessageBus)` — calls `bus.CancelScheduledAsync(ScheduledCloseMessageId)`, then `bus.ScheduleAsync(new CloseAuction(...), message.NewCloseAt)`, stashes the new token id into `ScheduledCloseMessageId`, updates `ScheduledCloseAt` to `message.NewCloseAt`, transitions `Status` to `Extended`
   - `Handle(CloseAuction)` — **stub only**. Returns `new OutgoingMessages()` (no-op). Includes a single-line TODO comment: `// TODO(M3-S5b): real close evaluation lives here`. This is the only stub in S5; every other handler is real.
2. `src/CritterBids.Auctions/CloseAuction.cs` — internal saga command record. Not a contract. Property shape resolved per Open Question 1 (`AuctionClosingSagaId` vs `ListingId` or both). Lives in `CritterBids.Auctions` only — no reference from `CritterBids.Contracts`.
3. `src/CritterBids.Auctions/StartAuctionClosingSagaHandler.cs` — separate `public static class` per skill §Starting a Saga. Contains `Handle(BiddingOpened, IMessageBus)` that constructs a new `AuctionClosingSaga`, calls `bus.ScheduleAsync(new CloseAuction(...), message.ScheduledCloseAt)`, stashes the token id into the saga, and returns the tuple `(AuctionClosingSaga, ...)` that Wolverine recognizes as a saga start. Idempotency: if a saga for the listing already exists, the handler no-ops (guard per Open Question 2).
4. `src/CritterBids.Auctions/AuctionsModule.cs` — additive changes:
   - `services.ConfigureMarten(...)`: `.Schema.For<AuctionClosingSaga>().Identity(x => x.Id).UseNumericRevisions(true)` per skill §Marten Document Configuration
   - No new `AddEventType<T>()` calls — the four events the saga consumes (`BiddingOpened`, `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`) are already registered from S4
   - No new tag-type registrations
   - Whatever Marten→Wolverine event-forwarding mechanism Open Question 4 lands on gets wired here (or in `Program.cs` if that is the correct seam — see that question's discussion)
   - `AuctionsConcurrencyRetryPolicies` stays as-is; the existing `ConcurrencyException` retry policy is global and already covers saga document writes under numeric revisions (verify, do not duplicate)
5. `src/CritterBids.Auctions/AssemblyAttributes.cs` — add `[assembly: WolverineModule]` if not already present. Per skill: saga discovery requires this **and** `Discovery.IncludeAssembly(typeof(Listing).Assembly)` in `Program.cs`. `Program.cs` already has that include from M3-S2; verify it is there and no action is needed beyond adding the assembly attribute.
6. `tests/CritterBids.Auctions.Tests/AuctionClosingSagaTests.cs` — 4 integration tests, method names **exactly** per milestone doc §7 §3 rows:
   - `BiddingOpened_StartsSaga_SchedulesClose`
   - `FirstBid_TransitionsToActive`
   - `ReserveMet_UpdatesSagaState`
   - `ExtendedBidding_CancelsAndReschedules`
   Test shape follows skill §Testing Sagas: Alba + Testcontainers; `ExecuteAndWaitAsync` (or equivalent on the fixture) for dispatch; saga document loaded from the Marten session and asserted with Shouldly. Scheduled-message assertions on 3.1 and 3.4 verify via Wolverine's durability storage that a `CloseAuction` is pending at the expected time.
7. `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — additive only:
   - Helper: `Task<T?> LoadSaga<T>(Guid id) where T : Saga` that opens a session and queries the saga document
   - If scenario 3.4's cancel-confirmed + reschedule-confirmed assertion requires a Wolverine-specific scheduled-message query API, add a helper here rather than duplicating per-test. Per S4 precedent, the fixture grows on demand only.
   No changes to existing fixture behaviour; S4/S4b test files remain byte-level unchanged.
8. *(Optional)* `docs/skills/wolverine-sagas.md` — append a "CritterBids M3-S5 learnings" subsection if and only if first-use surfaces something the skill did not predict (saga Id convention wrinkle, scheduled-message token lifetime, Marten saga identity under 8.x, Marten-to-Wolverine event forwarding seam, etc.). If the session runs clean, record "nothing new surfaced" in the retro instead.
9. `docs/retrospectives/M3-S5-auction-closing-saga-skeleton-retrospective.md` — written last. Gate below.

## Explicitly out of scope

- `Handle(CloseAuction)` real implementation and outcome-event emission — S5b
- `BiddingClosed`, `ListingSold`, `ListingPassed` handler emission — S5b (the contract record stubs from S1 are untouched)
- `Handle(BuyItNowPurchased)` terminal path on the saga — S5b
- `Handle(ListingWithdrawn)` terminal path on the saga, plus the synthetic `ListingWithdrawn` event in `AuctionsTestFixture` for scenario 3.10 — S5b
- `listings-auctions-events` RabbitMQ queue — S6
- `CatalogListingView` auction-status fields — S6
- Proxy Bid Manager saga — M4
- Session aggregate — M4
- Any change to `PlaceBidHandler`, `BuyNowHandler`, `BidConsistencyState`, `Listing`, `BidRejectionAudit`, or S4/S4b test files (byte-level diff limited to whitespace at most)
- Any change to `BiddingOpened`, `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`, `BuyItNowOptionRemoved`, `BidRejected`, or any other existing contract payload — Open Question 1 is the only path under which a contract property is added, and it is flagged as "last resort"
- Modifying the DCB handlers to return `OutgoingMessages` or publish via `IMessageBus` — even if Open Question 4 requires per-event bus delivery, the mechanism must be wired externally (Marten subscription, event forwarding, or similar), not by touching S4/S4b production code
- Any `Program.cs` change except what Open Questions 1 and/or 4 require; any such change must be called out in the retro with rationale
- Rewriting existing sections of `wolverine-sagas.md` or `dynamic-consistency-boundary.md`. Skill updates are append-only at retro time.

## Conventions to pin or follow

Inherit all conventions from prior milestones and S4/S4b. New conventions introduced in this slice:

- **Sagas live in their BC project, not in Contracts.** `AuctionClosingSaga` lives in `CritterBids.Auctions`.
- **Scheduled-message commands are saga-internal, not contracts.** `CloseAuction` is dispatched only within the Auctions BC.
- **Saga discovery is assembly-based.** `Discovery.IncludeAssembly(typeof(Listing).Assembly)` in `Program.cs` + `[assembly: WolverineModule]` in the BC project — per skill. `IncludeType<T>()` does not find saga sibling handlers.
- **Saga concurrency = numeric revisions + `ConcurrencyException` retry.** The existing `AuctionsConcurrencyRetryPolicies.OnException<ConcurrencyException>()` registration covers saga writes globally; verify rather than duplicate.
- **Idempotency guards mandatory on every saga handler.** Pattern per Open Question 2 resolution — consistent across `Handle(BidPlaced)`, `Handle(ReserveMet)`, `Handle(ExtendedBiddingTriggered)`, and the Start handler's "saga already exists" check.
- **Decider extraction is optional in S5.** The skill recommends a static `AuctionClosingDecider`. S5's four handlers are simple enough that inline logic may be acceptable. If any handler has branching beyond two conditions, extract. Record the call in the retro.
- **`IsProxy: false` is still hardcoded on `BidPlaced`** per S4 precedent — the saga observes every `BidPlaced` regardless of `IsProxy` and does not branch on it in M3.
- **`OriginalCloseAt + MaxDuration` is the saga's hard cap on rescheduling.** Per S4 retro, `Listing` preserves `OriginalCloseAt`. S5's reschedule handler does not need to consult this — extension math already validated the candidate `NewCloseAt` against MaxDuration in `PlaceBidHandler` before producing `ExtendedBiddingTriggered`. The saga trusts the event.

## Commit sequence (proposed)

1. `feat(auctions): add AuctionClosingStatus enum and AuctionClosingSaga state class` — item 1 (type + state properties only, no handlers; Status default `AwaitingBids`)
2. `feat(auctions): add CloseAuction saga command and WolverineModule assembly attribute` — items 2 and 5
3. `feat(auctions): register AuctionClosingSaga Marten schema with numeric revisions` — item 4 Marten-schema portion
4. *(if Open Question 4 requires new wiring)* `feat(auctions): wire Marten stream-event forwarding to Wolverine bus` — item 4 forwarding portion, naming the mechanism
5. `feat(auctions): start saga on BiddingOpened with initial scheduled CloseAuction` — item 3 + saga fixture helper + scenario 3.1 test
6. `feat(auctions): saga handlers for BidPlaced and ReserveMet transitions` — item 1 BidPlaced/ReserveMet handlers + scenarios 3.2 and 3.3 tests
7. `feat(auctions): saga cancels and reschedules on ExtendedBiddingTriggered` — item 1 reschedule handler + scenario 3.4 test
8. `feat(auctions): stub Handle(CloseAuction) pending S5b` — item 1 stub with TODO comment
9. *(optional)* `docs(skills): append M3-S5 learnings to wolverine-sagas.md` — item 8, only if something new surfaced
10. `docs: write M3-S5 retrospective` — item 9

## Acceptance criteria

- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- [ ] `dotnet test CritterBids.slnx` — 68-test baseline preserved; +4 new tests green; zero skipped, zero failing; **total 72**
- [ ] `src/CritterBids.Auctions/AuctionClosingSaga.cs` exists; inherits `Saga`; has `public Guid Id { get; set; }`; `AuctionClosingStatus` enum has all five values (`AwaitingBids`, `Active`, `Extended`, `Closing`, `Resolved`); real handlers exist for `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`; `CloseAuction` handler is a stub with a single-line TODO comment referencing S5b
- [ ] `src/CritterBids.Auctions/CloseAuction.cs` — internal record; not referenced from `CritterBids.Contracts`
- [ ] `src/CritterBids.Auctions/StartAuctionClosingSagaHandler.cs` — separate `public static class`; its `Handle(BiddingOpened, IMessageBus)` returns a tuple with the saga and calls `ScheduleAsync` for the initial `CloseAuction`; token id stashed into `saga.ScheduledCloseMessageId`
- [ ] `src/CritterBids.Auctions/AssemblyAttributes.cs` — contains `[assembly: WolverineModule]` (newly added or pre-existing, verified)
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — saga schema registered with `.UseNumericRevisions(true)`; `AddEventType<T>()` count unchanged at 7; whatever Open Question 4 mechanism is wired is traceable in the module or Program.cs diff
- [ ] All 4 test methods in `AuctionClosingSagaTests.cs` named exactly per milestone doc §7 §3 and green
- [ ] Scenario 3.4 (`ExtendedBidding_CancelsAndReschedules`) asserts **both** that the prior scheduled `CloseAuction` is cancelled (no longer in the pending set) AND the new `CloseAuction` is scheduled at `NewCloseAt`. A single-sided assertion is insufficient.
- [ ] Scenario 3.1 (`BiddingOpened_StartsSaga_SchedulesClose`) asserts the saga document exists with `Status = AwaitingBids`, `BidCount = 0`, `ReserveHasBeenMet = false`, and an initial `CloseAuction` is scheduled at the listing's `ScheduledCloseAt`
- [ ] `src/CritterBids.Auctions/` contains zero `IMessageBus` references **outside** saga handlers and the Start handler (the scheduling / cancel calls are the only legitimate uses)
- [ ] `src/CritterBids.Api/Program.cs` diff limited to what Open Questions 1 and/or 4 require; any change is called out in the retro
- [ ] `CritterBids.Auctions.csproj` `ProjectReference` count is 1 (Contracts only)
- [ ] `PlaceBidHandler.cs`, `PlaceBidHandlerTests.cs`, `PlaceBidDispatchTests.cs`, `BuyNowHandler.cs`, `BuyNowHandlerTests.cs`, `BuyNowDispatchTests.cs`, `BidConsistencyState.cs`, `Listing.cs` all unchanged from S4b close (byte-level diff limited to whitespace at most)
- [ ] No `[Obsolete]`, no `#pragma warning disable`, no `throw new NotImplementedException()` in production code. The `CloseAuction` stub returns `new OutgoingMessages()`, not an exception.
- [ ] `docs/retrospectives/M3-S5-auction-closing-saga-skeleton-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is **not optional** and is **not a footnote**. It is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test CritterBids.slnx` shows all tests green and `dotnet build` shows 0 errors + 0 warnings. If any test fails or any warning lands, fix the code first, then write the retro.

Retrospective content requirements:
- Baseline numbers (68 tests before, 72 after) with a phase table matching the S4/S4b retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit hashes
- Each of the four Open Questions answered with which path was taken and why, and — for Open Questions 1 and 4 — a citation to the Wolverine source or docs that grounded the decision
- Whether the skill append in item 8 was written; if so, the appended sections listed; if not, an explicit "nothing new surfaced beyond what the skill already covers" observation with rationale (S4b precedent)
- Any blocker encountered: verbatim error message, root cause, fix path — with particular attention to first-use surprises around `Saga` base class behaviour, `ScheduleAsync` / `CancelScheduledAsync` token semantics, Marten saga document identity under 8.x, Marten event forwarding to Wolverine, and any saga-discovery edge cases
- A **"What M3-S5b should know"** section covering at minimum:
  - Saga Id convention chosen (Open Question 1 outcome) — property name for correlation on the remaining integration events S5b will observe
  - Idempotency convention chosen (Open Question 2 outcome) — applied consistently in S5b's `Handle(BuyItNowPurchased)` and `Handle(ListingWithdrawn)`
  - Event-forwarding mechanism (Open Question 4 outcome) — whether S5b's terminal events need any additional wiring, or whether S5's mechanism is listing-stream-wide and covers the remaining events for free
  - Whether `Handle(CloseAuction)`'s real implementation should cancel any pending schedule when fired after a BuyItNow or Withdrawn (scenarios 3.8 + 3.9, 3.10)
  - Whether the fixture's synthetic `ListingWithdrawn` approach is constrained by any decision made in S5 (e.g., event type registration, projection subscription)

## Open questions (pre-mortems — flag, do not guess)

1. **Saga correlation — `Id` value and correlation property on integration events.** Skill file §Message Correlation states Wolverine looks for `{SagaTypeName}Id` on messages by convention — which would imply `AuctionClosingSagaId` on `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, and (for S5b) `BuyItNowPurchased`. But `002-scenarios.md` §3 explicitly models `AuctionClosingSaga.Id = listing-A`, suggesting a 1:1 saga-per-listing mapping that should not force a contract change. Three paths:

   - **Path A (preferred if available):** `Saga.Id = ListingId` with an attribute-based correlation override on the saga's handler methods — something like `[SagaIdentity("ListingId")]` or Wolverine's equivalent — so existing `ListingId`-bearing contracts correlate without a property addition. **Verify by grepping `C:\Code\JasperFx\wolverine\` for `SagaIdentity`, `IdentityAttribute`, or any override mechanism.** If supported, this is the cleanest path — zero contract touch, saga Id matches listing Id as scenarios specify.
   - **Path B (last resort):** `Saga.Id = Guid.CreateVersion7()` with `AuctionClosingSagaId` property added to `BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`. This forces a contract change. Requires the Start handler to seed the saga Id and populate the correlation property on every outgoing event. **Avoid unless A and C are both unavailable** — contract churn at this stage is costly.
   - **Path C:** `Saga.Id = ListingId` with explicit per-handler routing configured in `Program.cs` that maps the message's `ListingId` to saga id. Viable if Wolverine's routing API exposes this at the message-registration level.

   Flag blocking only if none of A/C are available and the retro ends up documenting B. Whichever path is taken, note the exact mechanism (attribute name, method name, or config API) and the Wolverine version it is documented in. Important: if Path B is forced, the contract change is limited to adding `AuctionClosingSagaId` and MUST NOT touch any other field.

2. **Idempotency guard storage.** The skill recommends `HashSet<Guid> ProcessedBidIds`. In CritterBids, `BidPlaced` does not carry a bid id — it carries `ListingId`, `BidderId`, `Amount`, `BidCount`, `IsProxy`, `PlacedAt`. Three options:

   - **(a) Derive an idempotency key** from `(ListingId, BidderId, BidCount)` and store a `HashSet<(Guid, Guid, int)>` or a `HashSet<Guid>` of hash-derived ids
   - **(b) Use `BidCount` monotonicity as the guard** — ignore any `BidPlaced` whose `BidCount <= saga.BidCount`. The DCB rejects sub-increment bids, so `BidCount` is monotonically increasing per listing. Stale deliveries are dropped without an explicit set.
   - **(c) Accept at-most-once for S5** and skip the guard, flagging it as a known gap for S5b or a follow-up session

   Recommended: **(b)** — simpler, no allocation growth, aligned with existing DCB guarantees. Flag if verifying the DCB's monotonicity guarantee surfaces a counterexample (e.g., concurrent accepts racing to the same `BidCount`) — that would be an S4 finding worth a small follow-up rather than a workaround in S5. Apply the chosen convention to `Handle(ReserveMet)` (trivially idempotent: set-to-true is idempotent) and the Start handler ("saga already exists for this listing" check).

3. **Does the existing `ConcurrencyException` retry policy cover saga writes?** `AuctionsConcurrencyRetryPolicies` registers `OnException<ConcurrencyException>().RetryWithCooldown(100ms, 250ms)` globally. Saga document writes under `.UseNumericRevisions(true)` raise `ConcurrencyException` on optimistic-concurrency conflicts. The existing registration should cover both paths, but the cooldown values were tuned for DCB aggregate writes in S4, not saga retries. Verify the retry fires for saga conflicts during a test run; if the existing values are inappropriate (too aggressive, too loose), **flag rather than tune speculatively** — retro notes the finding and S5b or a follow-up adjusts.

4. **How do DCB-produced events reach the saga's message handlers?** `PlaceBidHandler` and `BuyNowHandler` (S4/S4b) append events via `session.Events.Append` — they do NOT return `OutgoingMessages` and do NOT call `IMessageBus.PublishAsync`. `Program.cs` has `IntegrateWithWolverine()` on the Marten registration but no explicit Marten-stream-to-Wolverine-bus forwarding seam. For the saga's `Handle(BidPlaced)`, `Handle(ReserveMet)`, `Handle(ExtendedBiddingTriggered)`, and (S5b) `Handle(BuyItNowPurchased)` to fire, these events must reach Wolverine's message pipeline.

   Known candidate mechanisms to investigate in `C:\Code\JasperFx\wolverine\`:

   - **Marten event subscription / relay:** Wolverine's Marten integration may provide a `PublishEventsToWolverineMessaging()` or `SubscribeToEvents` seam that forwards stream events post-commit. If so, wire it on the `AddMarten()` chain in `Program.cs` or on the `ConfigureMarten` block in `AuctionsModule.cs`.
   - **Marten `IProjection`-style subscription:** a durable projection whose sole purpose is to publish stream events to the bus. Less idiomatic but functional.
   - **`Wolverine.Marten.IAggregateHandlerCascader`-style pattern:** aggregate handlers return events that Wolverine both appends and publishes. Does NOT apply here without modifying the DCB handlers — out of scope by the "unchanged from S4 close" rule.

   **The DCB handlers must not change.** Whatever mechanism is chosen lives entirely in module or `Program.cs` wiring. If the forwarding mechanism is listing-stream-wide (i.e., all events on the listing's primary stream get republished), that is preferred — it means S5b's `BuyItNowPurchased` handler automatically works without additional wiring. If the mechanism is per-event-type, S5 wires the four events the saga needs in S5 + S5b's events will be added in S5b.

   Flag blocking if no mechanism is found in a single-digit-minutes search of the Wolverine repo. The fallback is a small Marten subscription class in `CritterBids.Auctions` that receives stream events and calls `IMessageBus.PublishAsync` — functional, and aligned with the "no production handler change" constraint.
