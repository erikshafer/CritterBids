# M4-S3: Proxy Bid Manager Saga — Skeleton + Registration + Reactive Path

**Milestone:** M4 — Auctions BC Completion
**Slice:** S3 of 7 (no S3b pre-drafted; emergent S3b authorized at S3 retro if the novelty risk surfaces — see Open Questions)
**Narrative:** none — proxy mechanics are routed `separate-narrative` in `docs/narratives/001-bidder-wins-flash-auction.md` Moment 5 cumulative deferred section ("IsProxy flag and proxy bidding journey, slices 5.5 / 5.6"). Scope is scenario-anchored to Workshop 002 §4.
**Agent:** @PSA
**Estimated scope:** one PR; 5 new scenario tests (4 saga + 1 dispatch); ~6–8 new/modified files
**Baseline:** 120 tests passing (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 37 Auctions) · `dotnet build` 0 errors, 24 pre-existing NU1904 NuGet warnings (Marten) · M4-S2 closed at `faee5e9`. At session open: `AuctionsIdentityNamespaces.ProxyBidManagerSaga` Guid pinned by M4-S1; six Auctions contract stubs (`RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`) authored at M4-S1 with full future-consumer payload; extended `Contracts.Selling.ListingWithdrawn` carries `(ListingId, WithdrawnBy, Reason?, WithdrawnAt)`; `AuctionClosingSaga.Handle([SagaIdentityFrom(nameof(BidPlaced.ListingId))] BidPlaced)` is the existing first `BidPlaced` saga subscriber.

---

## Goal

Land the Proxy Bid Manager saga's foundation, the `RegisterProxyBid` start handler, and the reactive-path `Handle(BidPlaced)` for competing-bid auto-bids and own-bid tracking. This is the first in-repo **composite-key** saga (`SagaId = UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")` per M4-D1) and the **second `BidPlaced` saga subscriber** alongside the existing `AuctionClosingSaga`. Scope covers Workshop 002 §4 scenarios 4.1, 4.2, 4.4, 4.5 (four scenarios) plus a single `RegisterProxyBid` dispatch test via `IMessageBus`. Outcome-event emission (`ProxyBidExhausted` — §4.3, §4.9), terminal handlers (§4.6–4.8), the two-proxy bidding war (§4.10), and register-while-outbid timing (§4.11) are S4's scope.

Splitting the Proxy Bid Manager into S3 + S4 de-risks two first-use Wolverine patterns landing simultaneously: composite-key UUID v5 saga correlation **and** two-saga-subscribed-to-`BidPlaced` dispatch topology. S3 establishes the identity wiring, the start handler, and the reactive path with at-most-one PlaceBid outcome per inbound `BidPlaced`. S4 applies the established pattern to the harder scenarios (exhaustion emission, terminal paths, bidding war, register-while-outbid).

## Context to load

- `docs/milestones/M4-auctions-bc-completion.md` — §6 proxy saga idempotency conventions; §7 §4 test row mapping for 4.1 / 4.2 / 4.4 / 4.5; §9 S3 risk notes (two-saga `BidPlaced` dispatch is the headline risk)
- `docs/workshops/002-scenarios.md` — §4.1, 4.2, 4.4, 4.5 only (4.3 + 4.6–4.11 are S4 scope; do not read those sections this session)
- `docs/skills/wolverine-sagas.md` — primary skill; composite-key correlation (gap candidate), Start pattern, idempotency guards, retrospective skill-update gate per M3 / M4-S1 discipline
- `src/CritterBids.Auctions/AuctionClosingSaga.cs` — composite-key precedent's structural opposite: this saga's `[SagaIdentityFrom(nameof(BidPlaced.ListingId))]` correlates against a Guid property *already on the contract*. The proxy saga's identity is derived from two properties; the mechanism that makes that work is Open Question 1.
- `src/CritterBids.Settlement/SettlementSaga.cs` — second precedent for `[SagaIdentityFrom]` shape; same observation as `AuctionClosingSaga` — every shipped correlation reads a single Guid property from the inbound message.
- `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` — M4-S1 pinned namespace constant; S3 authors the helper that consumes it.
- `C:\Code\JasperFx\CritterStackSamples` and `C:\Code\JasperFx\wolverine` — canonical reference for any composite-key saga, derived-identity saga, or multi-saga single-event dispatch precedent the skill file may not cover. Spend single-digit minutes here per Open Question 1's flag protocol.

(Seven files. The four-deferred-scenarios sections of `002-scenarios.md` are out of scope on purpose — do not load §4.3 or §4.6–4.11 to avoid scope creep into S4 work.)

## In scope (numbered)

1. `src/CritterBids.Auctions/ProxyBidManagerSaga.cs` — `sealed class ProxyBidManagerSaga : Saga` with:
   - `public Guid Id { get; set; }` — required by `Saga` base; populated by the composite-key helper (item 3)
   - State: `ListingId`, `BidderId`, `MaxAmount`, `BidderCreditCeiling`, `LastBidAmount` (decimal, default 0), `Status` (`ProxyBidManagerStatus`)
   - `Handle(BidPlaced)` — reactive path:
     - If `message.BidderId == saga.BidderId`: update `LastBidAmount = message.Amount` and return (own bid; tracking only — covers scenarios 4.4 and 4.5)
     - Else (competing bid): compute `nextBid = competingBid + increment` per Workshop 002 conventions ($1 under $100, $5 at $100+); if `nextBid <= saga.MaxAmount`, emit `PlaceBid(ListingId, BidderId, nextBid, IsProxy: true)` via `OutgoingMessages`. If `nextBid > saga.MaxAmount`, **do not emit `ProxyBidExhausted` in S3** — leave that branch as a TODO comment referencing S4 (the exhaustion-emission handler is S4 scope; S3 only handles the path where the proxy still has headroom).
   - Identity wiring per Open Question 1 resolution
2. `src/CritterBids.Auctions/ProxyBidManagerStatus.cs` — `enum` with three values: `Active`, `Exhausted`, `ListingClosed`. Declare all three at S3 even though only `Active` is reached by S3 handlers — matches the `AuctionClosingStatus` "declare full enum at skeleton" precedent (M3-S5).
3. `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` — extend with a `static Guid ProxyBidManagerSagaId(Guid listingId, Guid bidderId)` helper (or author a sibling helper class — name resolved at item time, but the constant lives on `AuctionsIdentityNamespaces` per M4-S1's "pure constants" shape; a helper extension goes on a new `AuctionsIdentityHelpers` static class if it doesn't fit cleanly). Returns `UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{listingId}:{bidderId}")`. UUID v5 implementation per existing precedent — verify `CritterBids.Participants` has a UUID v5 helper already (it does — used for participant id derivation); reuse the same library/utility rather than authoring a parallel helper.
4. `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs` — separate `public static class` per skill §Starting a Saga. `Handle(RegisterProxyBid, IDocumentSession)` (or whatever signature shape the skill specifies for "start saga + check for existing"):
   - Compute `sagaId = AuctionsIdentityNamespaces.ProxyBidManagerSagaId(message.ListingId, message.BidderId)` via item 3
   - Existence check: if a saga document with that Id already exists, no-op return (idempotent re-registration — the deterministic composite-key Guid makes this natural; the existence check is the "saga already exists for this (listing, bidder)" guard)
   - Otherwise construct the saga (`Status = Active`, populate state from `RegisterProxyBid` + `BidderCreditCeiling` lookup per Open Question 4), emit `ProxyBidRegistered` per Open Question 3 resolution, return the Wolverine-recognized tuple
5. `src/CritterBids.Auctions/AuctionsModule.cs` — additive only:
   - `services.ConfigureMarten(...)`: `.Schema.For<ProxyBidManagerSaga>().Identity(x => x.Id).UseNumericRevisions(true)` per skill §Marten Document Configuration (mirrors AuctionClosingSaga registration shape)
   - No new `AddEventType<T>()` calls — `RegisterProxyBid` is a command (not a stored event), `ProxyBidRegistered` emission shape is Open Question 3, `BidPlaced` is already registered, `PlaceBid` is already registered.
   - No new tag-type registrations; no new RabbitMQ routing.
6. `tests/CritterBids.Auctions.Tests/ProxyBidManagerSagaTests.cs` — 4 integration tests, method names exactly per milestone doc §7 §4:
   - `RegisterProxyBid_StartsSaga_ProducesProxyBidRegistered` (scenario 4.1)
   - `CompetingBid_ProxyAutoBidsOneIncrementAbove` (scenario 4.2)
   - `OwnProxyBid_TracksNoReact` (scenario 4.4)
   - `OwnManualBid_TracksNoReact` (scenario 4.5)
   Test shape follows skill §Testing Sagas — Alba + Testcontainers via `AuctionsTestFixture`; `ExecuteAndWaitAsync` or `InvokeMessageAndWaitAsync`; saga document loaded via the existing `LoadSaga<T>` helper (from M3-S5); Shouldly assertions on saga state and `tracked.Sent` / `tracked.NoRoutes` per Open Question 3 resolution.
7. `tests/CritterBids.Auctions.Tests/RegisterProxyBidDispatchTests.cs` — one `[Fact]` dispatching via `IMessageBus`; asserts saga document exists post-dispatch with `Status = Active` and the appropriate `tracked.NoRoutes` / `tracked.Sent` assertion shape per Open Question 3.
8. *(Verify only, not necessarily a change)* `tests/CritterBids.Auctions.Tests/Fixtures/AuctionsTestFixture.cs` — no new discovery exclusions expected since `ProxyBidManagerSaga` lives in the same BC as `AuctionClosingSaga`. Verify the existing fixture covers the two-saga `BidPlaced` dispatch case without modification. If a within-BC two-saga dispatch surprise surfaces (per Open Question 2), the fix may require a fixture amendment — record in retro.
9. *(Optional)* `docs/skills/wolverine-sagas.md` — append an "M4-S3 learnings" subsection if and only if first-use surfaces something the skill does not predict: composite-key identity wiring mechanism (Open Question 1), two-saga `BidPlaced` dispatch (Open Question 2), `ProxyBidRegistered` emission shape (Open Question 3), or `BidderCreditCeiling` lookup pattern (Open Question 4). If nothing new surfaces, record "nothing new surfaced beyond what the skill already covers" in the retro per M3-S4b / M4-S2 precedent.
10. `docs/retrospectives/M4-S3-proxy-bid-manager-saga-skeleton-retrospective.md` — written last. Gate below.

## Explicitly out of scope

- `ProxyBidExhausted` event emission and the exhaustion code branch — S4 (scenarios 4.3 and 4.9). The Handle(BidPlaced) exhaustion branch is a single-line TODO in S3.
- Terminal handlers: `Handle(ListingSold)`, `Handle(ListingPassed)`, `Handle(ListingWithdrawn)` — S4 (scenarios 4.6 / 4.7 / 4.8). The `ListingClosed` enum value is declared in S3 but not reached by any handler.
- Credit-ceiling cap interaction in the exhaustion calc — S4 (scenario 4.9). The `BidderCreditCeiling` field is populated on the saga at S3 per Open Question 4 but not consulted by S3's reactive handler.
- Two-proxy bidding war (scenario 4.10) — S4.
- Register-while-outbid timing (scenario 4.11) — S4.
- Session aggregate, `CreateSession` / `AttachListingToSession` / `StartSession`, `SessionStarted → BiddingOpened` fan-out — S5.
- Listings BC catalog extension and `SessionMembershipHandler` — S6.
- Proxy cancellation or modification after registration — not in Workshop 002 §4 per milestone doc §3.
- HTTP endpoint for `RegisterProxyBid` — M6.
- Any modification to `AuctionClosingSaga`, `PlaceBidHandler`, `BuyNowHandler`, `Listing`, `BidConsistencyState`, `SellerListing`, or any test file outside `CritterBids.Auctions.Tests` for the two new test files (byte-level diff limited to whitespace at most).
- Any modification to M4-S1 contract stubs (`RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted` payloads are final; touching them indicates Open Question 1 resolved to Path B and must be flagged in the retro).
- Any new RabbitMQ routing rule. `RegisterProxyBid` is a command (no cross-BC publish path). `ProxyBidRegistered`'s cross-BC consumer is post-M5 Relay per M4-S1 retro — route deferred. `tracked.NoRoutes` is the expected fixture-stance assertion shape if Open Question 3 resolves to bus emission.
- Any change to `Program.cs` except what Open Question 1 might require (and that change must be called out explicitly in the retro with rationale).
- Rewriting existing sections of `wolverine-sagas.md` — skill updates are append-only at retro time per M3 / M4-S1 discipline.

## Conventions to pin or follow

Inherit all conventions from CLAUDE.md and prior milestones (M3-S5 saga conventions, M4-S1 / M4-S2 contract and fixture conventions). New conventions introduced or pinned in this slice:

- **Composite-key saga id format is `UuidV5(namespace, $"{ListingId}:{BidderId}")` — M4-D1, pinned by M4-S1.** The colon-delimited string form matches Workshop 002 §4.1 verbatim; do not byte-concatenate or reorder.
- **Composite-key namespace constant lives on `AuctionsIdentityNamespaces`; helper method shape per item 3.** S1's "pure constants" file gains a helper sibling rather than methods on the constants class.
- **`IsProxy: true` on proxy-emitted `PlaceBid` is load-bearing.** Scenarios 4.4 (`OwnProxyBid_TracksNoReact`) and 4.5 (`OwnManualBid_TracksNoReact`) differ only on the inbound `BidPlaced.IsProxy` flag — both must be tracked-only on the saga (no PlaceBid emission). Note: the M3-era hardcoded `IsProxy: false` in `PlaceBidHandler` (still in place at session open) does not block S3 — the saga reads `BidPlaced.IsProxy` from the contract; nothing changes in `PlaceBidHandler`.
- **Bid increment math is inline in S3, not extracted.** $1 under $100, $5 at $100+ per Workshop 002 conventions. Three similar lines of inline computation, per CLAUDE.md's "premature abstraction" rule. S4 retro decides extraction if the exhaustion + bidding-war scenarios warrant.
- **Saga existence check at start uses the deterministic composite-key Guid.** Idempotent re-registration is natural: if `RegisterProxyBid` arrives twice for the same (listing, bidder), the second start handler hits the existence guard and no-ops. No `HashSet<Guid> ProcessedRegistrations` pattern; the saga document itself is the existence record.
- **Reactive-path idempotency for own bids: monotone `LastBidAmount` updates.** If an own `BidPlaced` arrives for an amount already at or below `LastBidAmount`, no state change (idempotent on redelivery). Competing-bid path has no built-in monotonicity — S4 retro may revisit if the bidding-war scenario surfaces re-delivery issues.
- **Concurrency: existing `AuctionsConcurrencyRetryPolicies.OnException<ConcurrencyException>` policy is global per M3-S5.** Verify it covers `ProxyBidManagerSaga` document writes under numeric revisions; do not duplicate.
- **`Saga.NotFound(BidPlaced)` static method:** AuctionClosingSaga has `static OutgoingMessages NotFound(CloseAuction) => new();` at line 146 for the saga-already-removed path. If two-saga `BidPlaced` dispatch causes Wolverine to try loading a `ProxyBidManagerSaga` for a `(ListingId, BidderId)` pair that never had a proxy registered, the equivalent static `NotFound(BidPlaced)` on `ProxyBidManagerSaga` is the safety net (Open Question 2 names the conditions under which this is required).
- **No changes to existing event-type registrations.** `BidPlaced` was registered at M3-S4; `RegisterProxyBid` is a command and not stored on a stream; `ProxyBidRegistered` storage shape depends on Open Question 3 (if it's bus-only, no AddEventType needed; if it's appended to the saga's audit stream, AddEventType<ProxyBidRegistered>() is added at item 5).

## Commit sequence (proposed)

1. `feat(auctions): add ProxyBidManagerStatus enum and ProxyBidManagerSaga state class` — items 1 and 2 (type + state properties only, no handlers; `Status` defaults to `Active`)
2. `feat(auctions): UUID v5 composite-key helper for ProxyBidManagerSaga` — item 3
3. `feat(auctions): register ProxyBidManagerSaga Marten schema with numeric revisions` — item 5 Marten-schema portion
4. `feat(auctions): start saga on RegisterProxyBid with ProxyBidRegistered emission` — item 4 + scenario 4.1 test (item 6 method 1)
5. `feat(auctions): reactive Handle(BidPlaced) for competing-bid auto-bid` — item 1 competing-bid handler branch + scenario 4.2 test
6. `feat(auctions): own-bid tracking (proxy + manual) no-react paths` — item 1 own-bid branches + scenarios 4.4 and 4.5 tests
7. `test(auctions): RegisterProxyBid dispatch via IMessageBus` — item 7
8. *(optional)* `docs(skills): append M4-S3 learnings to wolverine-sagas.md` — item 9, only if something new surfaced
9. `docs: write M4-S3 retrospective` — item 10

## Acceptance criteria

- [ ] `dotnet build` — 0 errors, 0 warnings beyond the pre-existing 24 NU1904 NuGet warnings (Marten) — no new warnings introduced
- [ ] `dotnet test` — 120-test baseline preserved; +5 new (4 saga + 1 dispatch) tests green; zero skipped, zero failing; **total 125** (1 Api + 1 Contracts + 6 Participants + 14 Listings + 36 Selling + 25 Settlement + 42 Auctions)
- [ ] `src/CritterBids.Auctions/ProxyBidManagerSaga.cs` exists; inherits `Saga`; has `public Guid Id { get; set; }`; `ProxyBidManagerStatus` enum has all three values (`Active`, `Exhausted`, `ListingClosed`); `Handle(BidPlaced)` has the own-bid + competing-bid branches with the exhaustion TODO comment referencing S4
- [ ] `src/CritterBids.Auctions/ProxyBidManagerStatus.cs` — three-value enum, declared in its own file (matches M3-S5 `AuctionClosingStatus` pattern)
- [ ] `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` (or sibling helper class) — `ProxyBidManagerSagaId(Guid listingId, Guid bidderId)` returns deterministic UUID v5; helper consumes the M4-S1 pinned namespace Guid; existing `ProxyBidManagerSaga` Guid constant unchanged (byte-level)
- [ ] `src/CritterBids.Auctions/StartProxyBidManagerSagaHandler.cs` — separate `public static class`; `Handle(RegisterProxyBid, ...)` returns the Wolverine-recognized saga-start tuple; existence check guards against re-registration; emits `ProxyBidRegistered` per Open Question 3 resolution
- [ ] `src/CritterBids.Auctions/AuctionsModule.cs` — `ProxyBidManagerSaga` Marten schema registered with `.UseNumericRevisions(true)`; `AddEventType<T>()` count unchanged unless Open Question 3 resolves to audit-stream append (in which case +1 for `ProxyBidRegistered`)
- [ ] All 4 test methods in `ProxyBidManagerSagaTests.cs` named exactly per milestone doc §7 §4 and green; scenario 4.1 asserts saga document exists with `Status = Active`, composite-key Id matches `ProxyBidManagerSagaId(...)`, and `ProxyBidRegistered` reaches the appropriate tracked bucket (Open Question 3); scenarios 4.4 and 4.5 assert no `PlaceBid` emission and `LastBidAmount` updated; scenario 4.2 asserts a single `PlaceBid` emission at `competingBid + increment` with `IsProxy: true`
- [ ] `RegisterProxyBidDispatchTests.cs` — one `[Fact]` through `IMessageBus`; asserts saga existence and the outbound `ProxyBidRegistered` per Open Question 3
- [ ] `src/CritterBids.Auctions/AuctionClosingSaga.cs` — unchanged from M4-S2 close (byte-level diff limited to whitespace at most)
- [ ] `src/CritterBids.Auctions/PlaceBidHandler.cs`, `BuyNowHandler.cs`, `BidConsistencyState.cs`, `Listing.cs` — unchanged
- [ ] `src/CritterBids.Contracts/Auctions/RegisterProxyBid.cs`, `ProxyBidRegistered.cs`, `ProxyBidExhausted.cs` — unchanged (byte-level — touching these indicates Open Question 1 resolved to Path B, which must be flagged in the retro)
- [ ] `src/CritterBids.Api/Program.cs` — unchanged unless Open Question 1 requires routing-side wiring (any change is called out explicitly in the retro)
- [ ] No `[Obsolete]`, no `#pragma warning disable`, no `throw new NotImplementedException()` in production code. The exhaustion TODO in `Handle(BidPlaced)` is a single-line comment referencing S4, not a thrown exception or `NotImplementedException`.
- [ ] `docs/retrospectives/M4-S3-proxy-bid-manager-saga-skeleton-retrospective.md` exists and meets the retrospective content requirements below

## Retrospective gate (REQUIRED)

The retrospective is the last commit of the PR. Gate condition: retrospective commits **only after** `dotnet test` shows 125 passing and `dotnet build` shows no new warnings beyond the pre-existing 24 NU1904.

Retrospective content requirements:

- Baseline numbers (120 before, 125 after) with a phase table matching M4-S2 retro shape
- Per-item status table mirroring the "In scope (numbered)" list with commit hashes
- Each of the four Open Questions answered with which path was taken and why; for Open Question 1 (composite-key identity wiring), a citation to the Wolverine source, the CritterStackSamples precedent, or the skill file passage that grounded the decision
- Whether the skill append in item 9 was written; if so, the appended sections listed; if not, an explicit "nothing new surfaced beyond what the skill already covers" observation
- Any blocker encountered — verbatim error message, root cause, fix path — with particular attention to:
  - Composite-key `[SagaIdentityFrom]` semantics (Open Question 1)
  - Two-saga single-event dispatch (`AuctionClosingSaga` + `ProxyBidManagerSaga` both subscribed to `BidPlaced`)
  - `BidderCreditCeiling` lookup mechanism on saga start (Open Question 4)
  - Any Marten saga-document storage surprises specific to composite-key identity
- A **"What M4-S4 should know"** section covering at minimum:
  - Identity wiring chosen (Open Question 1 outcome) — the exact mechanism S4 reuses for `Handle(ListingSold)`, `Handle(ListingPassed)`, `Handle(ListingWithdrawn)` and for the exhaustion path
  - Idempotency convention chosen (Open Question 2 / convention pinning outcome) — applied uniformly in S4's terminal handlers
  - `ProxyBidRegistered` emission shape (Open Question 3 outcome) — confirms the analogous shape S4 uses for `ProxyBidExhausted`
  - `BidderCreditCeiling` lookup mechanism (Open Question 4 outcome) — S4's exhaustion calc (scenario 4.9 — credit-ceiling cap) needs this value at competing-bid time
  - Whether two-saga dispatch surfaced any handler-discovery or fixture surprise that S4's terminal-handler additions might re-trigger
  - Bid increment helper — whether inline math should be extracted in S4 given the bidding-war scenario's repeated use

## Open questions (pre-mortems — flag, do not guess)

1. **Composite-key saga identity wiring.** The two shipped sagas (`AuctionClosingSaga`, `SettlementSaga`) correlate via `[SagaIdentityFrom(nameof(X.PropertyName))]` against a single Guid property already present on the inbound contract (`ListingId`, `SettlementId`). The Proxy Bid Manager saga's identity is `UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")` — a *derived* Guid that no contract property carries. Three paths to investigate:

   - **Path A (preferred if available):** Wolverine supports a delegate-based, expression-based, or method-based saga identity resolution at the handler level. Candidate mechanisms to grep for in `C:\Code\JasperFx\wolverine\` and `C:\Code\JasperFx\CritterStackSamples\`: a `[SagaIdentity]` attribute taking a resolver expression, a per-handler convention method on the saga class (`static Guid IdentifyFor(BidPlaced m) => ...`), or a Wolverine extension hook on the saga registration that names the id-derivation function. Verify before any other path.

   - **Path B (last resort — costs contract churn):** Add a precomputed `ProxyBidManagerSagaId: Guid` field to `RegisterProxyBid` and `BidPlaced`. Both records were finalized at M4-S1 and M3-S4 respectively; adding a field forces dual-source coordination (the producer must compute the v5 hash before emission). Avoid unless Paths A and C are both unavailable. Touching `src/CritterBids.Contracts/Auctions/*.cs` from S3 is the unambiguous signal that Path B was taken — flag in retro with the exact reason A and C failed.

   - **Path C:** Introduce a saga-side message-routing intermediary — a small `BidPlacedForProxyDispatcher` handler that receives `BidPlaced`, looks up any registered `ProxyBidManagerSaga` for `(ListingId, BidderId)` via direct document query, and forwards an Auctions-internal command `BidPlacedForProxy(SagaId, OriginalBidPlaced)` to the saga. Adds one indirection layer but keeps both contracts and the saga's `[SagaIdentityFrom(nameof(BidPlacedForProxy.SagaId))]` shape clean. Cost: the dispatcher itself becomes a non-trivial moving part with its own tests.

   Flag blocking only if Path A is documented nowhere and Path C requires authoring patterns the skill doesn't cover. Whichever path lands, note the exact mechanism and the Wolverine version in the retro. **Important:** if Path B is forced, the contract change is limited to adding the single `ProxyBidManagerSagaId` field and MUST NOT touch any other field — the integration-messaging L2 payload completeness from M4-S1 stays intact.

2. **Two-saga `BidPlaced` dispatch — within-BC topology.** `AuctionClosingSaga.Handle([SagaIdentityFrom(nameof(BidPlaced.ListingId))] BidPlaced)` exists at `AuctionClosingSaga.cs:47`. Adding `ProxyBidManagerSaga.Handle(BidPlaced)` (with whatever identity wiring Open Question 1 lands on) creates a within-BC multi-saga subscription on the same event. Three possible surprises to verify with a session-time first run:

   - **(a) Additive without exclusion:** Both sagas dispatch on every inbound `BidPlaced`; Wolverine resolves each saga's id independently from the same message. Expected default per skill file. Verify in scenario 4.2 test run.
   - **(b) Saga-document lookup explosion:** If Wolverine attempts to load a `ProxyBidManagerSaga` document for every `(ListingId, BidderId)` combination on every inbound `BidPlaced` — even pairs with no proxy registration — the `NotFound` branch may fire frequently. Mitigated by the same `static OutgoingMessages NotFound(BidPlaced) => new();` pattern from `AuctionClosingSaga.cs:146`. Verify the count of `NotFound` invocations in scenario 4.2 is bounded (one per inbound, not one per (listing, bidder) combinatorial).
   - **(c) Handler-discovery shadowing:** The M4-S2 retro Key Learning #1 documented cross-BC handler discovery as fixture-stance, not test-stance. The within-BC equivalent: if Wolverine's `[SagaIdentityFrom]` resolution iterates registered sagas and somehow picks the wrong one (or both ambiguously), the fix shape is closer to the M3-S6 `*DiscoveryExclusion` precedent. Flag if scenario 4.2 or the dispatch test hits an unexpected handler resolution.

   Flag blocking if first-run surfaces an `UnknownSagaException` or a dispatch ambiguity. The expected resolution path for (b) and (c) is the static `NotFound(BidPlaced)` pattern; for unforeseen surprises beyond that, halt and consult.

3. **`ProxyBidRegistered` emission shape.** `ProxyBidRegistered` is in `CritterBids.Contracts.Auctions` (integration event) with payload `(ListingId, BidderId, MaxAmount, RegisteredAt)`. M4-S1 retro names Relay BC (post-M5) as the primary consumer; at M4-S3 there is no cross-BC consumer. Two paths:

   - **(a) Emit via `OutgoingMessages` at saga start** — bus-published. Scenario 4.1 test asserts the event reaches `tracked.NoRoutes.MessagesOf<ProxyBidRegistered>()` (the M4-S2 fixture-stance pattern from M5-S6 retro Key Learning #1). Aligned with the integration-messaging L2 "complete payload at first commit" discipline — payload is Relay-ready months before Relay lands.
   - **(b) Append to the saga's own Marten audit stream** — `session.Events.Append(saga.Id, new ProxyBidRegistered(...))`. The saga document is the audit; no bus traffic. Requires `AddEventType<ProxyBidRegistered>()` in `AuctionsModule.ConfigureMarten` (item 5 delta). Skips the dormant-route situation but loses the "ready for Relay" property — Relay would later need to consume from a different source.

   Recommended: **(a)** — matches M5-S3's pre-wiring discipline (Settlement consumed `ListingWithdrawn` routes months before the Selling producer existed). Test assertion shape is `tracked.NoRoutes.MessagesOf<ProxyBidRegistered>().ShouldHaveSingleItem()`. Flag if the saga's own-stream audit becomes more compelling for a reason that surfaces at session time.

4. **`BidderCreditCeiling` lookup at saga start.** The Proxy Bid Manager saga state carries `BidderCreditCeiling` (the saga must enforce the credit-ceiling cap on auto-bid amounts at S4 scenario 4.9). `RegisterProxyBid` does not carry the credit ceiling — Workshop 002 §4.1 shows the command as `{ ListingId, BidderId, MaxAmount }`. Where does the saga get the credit ceiling? Two options:

   - **(a) Query the Participants BC's read side at saga start.** Reach across BC boundaries from the Auctions saga's start handler to load the participant's credit ceiling. This violates modular-monolith BC isolation (the same constraint that drove M4-D4 to option 4 — Auctions-side duplicate projection — rather than cross-BC `CatalogListingView` query). Almost certainly the wrong path.
   - **(b) Auctions-side duplicate `ParticipantCreditCeiling` projection** — analogous to the M4-D4 `PublishedListings` projection. Auctions subscribes to `ParticipantSessionStarted` (which carries the credit ceiling per `Contracts.Participants.ParticipantSessionStarted` shape from M5-S5) on the existing `auctions-participants-events` queue (verify queue exists; may need wiring), maintains a small Marten document projection keyed by `ParticipantId` (or `BidderId` — verify the identity mapping at session open), and the start handler loads from it.
   - **(c) Defer the credit-ceiling field to S4** — S3's saga doesn't enforce the cap (S3 scenarios 4.1/4.2/4.4/4.5 don't reach the cap). The field can be added in S3 as default-zero and populated in S4 alongside the cap-enforcing handler. Lowest-cost path but pushes a foundational concern down.

   Recommended: investigate **(b)** first — same shape as M4-D4 option 4, no new ADR triggered (duplicate projection is a named pattern). If `auctions-participants-events` queue or `ParticipantSessionStarted` projection wiring is more work than S3's session can absorb, fall back to **(c)** with explicit S4-residual call-out. Path (a) is rejected upfront on BC isolation grounds; do not investigate it.

   **Cross-reference:** the Auctions-side `PublishedListings` projection (M4-D4 resolution, S5 implementation per milestone doc §6) is the parallel pattern. If S3 implements a `ParticipantCreditCeiling` projection here, S5's scope reduces by the precedent-establishing portion — flag in retro as a positive scope ripple.
