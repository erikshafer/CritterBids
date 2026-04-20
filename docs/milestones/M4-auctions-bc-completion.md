# M4 — Auctions BC Completion

**Status:** Planning
**Scope:** Complete the Auctions BC — Proxy Bid Manager saga, Session aggregate with Flash format, and the Selling-side `WithdrawListing` producer. Trigger ADR 014 via the second application of the M3-D2 Path A read-model extension pattern.
**Companion docs:** [`../workshops/002-auctions-bc-deep-dive.md`](../workshops/002-auctions-bc-deep-dive.md) · [`../workshops/002-scenarios.md`](../workshops/002-scenarios.md) · [`../workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md) · [`../skills/README.md`](../skills/README.md) · [`../decisions/007-uuid-strategy.md`](../decisions/007-uuid-strategy.md) · [`../retrospectives/M3-auctions-bc-retrospective.md`](../retrospectives/M3-auctions-bc-retrospective.md)

---

## 1. Goal & Exit Criteria

### Goal

Finish the Auctions BC. M3 landed the DCB boundary model and the first saga (Auction Closing); M4 lands the **second saga with a composite correlation key** (Proxy Bid Manager, one instance per `ListingId + BidderId`) and the **Session aggregate** that makes the Flash auction format work. At M4 close, an Operations staff member can create a Flash Session, attach published listings to it, and start the session — at which point all attached listings open for bidding simultaneously through the `SessionStarted → BiddingOpened` fan-out handler. Participants with registered proxy bids have their maximums defended automatically through the full bidding cycle, including two-proxy bidding wars that escalate to the weaker proxy's exhaustion. A seller can withdraw a live listing through a real Selling-side command, replacing M3's hand-crafted `ListingWithdrawn` test fixture with a real producer.

This milestone lands three firsts for the CritterBids codebase: the first saga with a composite correlation key, the first aggregate in Auctions that is not a `Listing`, and the first time the M3-D2 Path A extension pattern is applied a second time — which triggers the ADR-014 authoring per the M3 retrospective's ADR candidate review.

### Exit criteria

- [ ] Solution builds clean with `dotnet build` — 0 errors, 0 warnings
- [ ] Proxy Bid Manager saga implemented: Marten-document-backed `Saga` subclass with composite UUID v5 correlation on `ListingId + BidderId`; all 11 `002-scenarios.md` §4 scenarios green
- [ ] Session aggregate implemented: `Session` event-sourced aggregate on Auctions, `CreateSession` / `AttachListingToSession` / `StartSession` command handlers; all 7 `002-scenarios.md` §5 scenarios green
- [ ] `SessionStarted → BiddingOpened` fan-out handler implemented per Workshop 002 Phase 1 Option B — one `BiddingOpened` produced per attached listing
- [ ] `CritterBids.Contracts.Auctions.*` extended — `RegisterProxyBid` (command), `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`
- [ ] `CritterBids.Contracts.Selling.ListingWithdrawn` authored; Selling BC produces it through a new `WithdrawListing` command + `SellerListing.Apply(ListingWithdrawn)` handler; 4 scenarios green
- [ ] Auction Closing saga's `Handle(ListingWithdrawn)` is now exercised against the real Selling producer (integration test); the M3 test-fixture `ListingWithdrawn` synthesis is removed or reduced to a unit-test-only shortcut
- [ ] `[WriteAggregate]` with explicit `nameof` override on every M4 aggregate command (Session aggregate) and Selling command (`WithdrawListing`) from first commit
- [ ] Listings BC catalog extended: `CatalogListingView` gains Session-membership fields and a `Withdrawn` status transition; implemented as a new sibling handler class per M3-D2 Path A (second application of the pattern)
- [ ] ADR 014 — Cross-BC read-model extension shape — authored, documenting the pattern with the two lived applications (M3-S6 auction status + M4-S6 session and withdrawn status) as evidence
- [ ] At least one dispatch test per new command (`RegisterProxyBid`, `CreateSession`, `AttachListingToSession`, `StartSession`, `WithdrawListing`) exercising the Wolverine routing path
- [ ] ADR 007 Gate 4 re-evaluated — closed with JasperFx input, or re-deferred with a new dated rationale that names the specific blocker
- [ ] Aspire RabbitMQ management UI port exposed (per M3-S7 smoke-test observation); low-priority infrastructure fix bundled into S1
- [ ] `docs/skills/wolverine-sagas.md` updated retrospectively with the first in-repo composite-key saga example (from S4)
- [ ] `docs/skills/marten-projections.md` §7 reinforced with the second Path A application as a concrete example (from S6)
- [ ] M4 retrospective doc written

---

## 2. In Scope

### Auctions BC — remaining components

| Component | What it owns | Scenario source |
|---|---|---|
| `Session` aggregate | Flash-format auction container: title, duration, attached listings, started-at timestamp | `002-scenarios.md` §5 (7 scenarios) |
| `SessionStarted → BiddingOpened` fan-out handler | Reacts to `SessionStarted` and produces one `BiddingOpened` per listing in the session — the Option B fan-out from Workshop 002 Phase 1 | Implicit in §5.5; new integration test in M4-S5 |
| Proxy Bid Manager saga | One instance per bidder per listing; composite UUID v5 key from `ListingId + BidderId`; reacts to competing `BidPlaced`, fires back `PlaceBid` up to `min(nextBid, maxAmount, creditCeiling)`, terminates on exhaustion or listing close | `002-scenarios.md` §4 (11 scenarios) |

Total aggregate + saga scenarios in scope: **18** (7 + 11).

### Selling BC — `WithdrawListing` command

Not a full scope addition to Selling — just the narrow slice needed to replace M3's test-fixture fiction.

| Component | What it owns |
|---|---|
| `WithdrawListing` command | Accepts a `ListingId`, rejects if the listing is not in a withdraw-eligible state (not-yet-published, or already closed), otherwise appends `ListingWithdrawn` |
| `ListingWithdrawn` integration event | Published to RabbitMQ for Auctions consumption (Auction Closing saga termination) and Listings consumption (catalog status transition) |
| `SellerListing.Apply(ListingWithdrawn)` | Sets `Status = Withdrawn` on the aggregate |

Scenarios in scope: **4** (happy path, reject-not-published, reject-already-closed, dispatch test).

### Cross-BC wiring

**No new queues.** The existing M3 queues cover all M4 traffic:

| Queue | Existing routes (M3) | New routes (M4) |
|---|---|---|
| `auctions-selling-events` | `ListingPublished` | `ListingWithdrawn` |
| `listings-auctions-events` | `BiddingOpened`, `BidPlaced`, `BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased` | `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`, `ListingWithdrawn` |
| `listings-selling-events` | `ListingPublished` | `ListingWithdrawn` |

One new queue binding may be required on the Listings side to consume from `listings-selling-events` for `ListingWithdrawn` — confirmed in S2 when the Selling producer lands.

### Integration contracts authored in M4

All new events go in `src/CritterBids.Contracts/`:

**Auctions:**
- `RegisterProxyBid` — command carrying `ListingId`, `BidderId`, `MaxAmount`
- `ProxyBidRegistered` — audit event; proxy saga started
- `ProxyBidExhausted` — carries `ListingId`, `BidderId`, `MaxAmount`, `ExhaustedAt`; consumed by Relay (post-M5) for the distinct "your proxy has been exceeded" notification per Workshop 002 Phase 1 (W001 Parked #3 resolution)
- `SessionCreated` — `SessionId`, `Title`, `DurationMinutes`, `CreatedAt`
- `ListingAttachedToSession` — `SessionId`, `ListingId`
- `SessionStarted` — `SessionId`, `ListingIds[]`, `StartedAt`

**Selling:**
- `ListingWithdrawn` — `ListingId`, `WithdrawnBy` (participant or ops staff identifier), `Reason` (optional), `WithdrawnAt`

Contracts carry complete payload for all future consumers at first commit, per `integration-messaging.md` L2 and the discipline re-confirmed across M2–M3.

### ADR 007 Gate 4 — final disposition

Gate 4 has been deferred twice now (ADR-007 original, M3-S1 amendment). M4-S1 is the final scheduled re-evaluation before the Auctions BC is closed. Two outcomes acceptable:

- **Close with a decision** — JasperFx input received, event-row-ID strategy selected (v7 or engine-default), amended into ADR 007 with the date and the source of the guidance.
- **Re-defer with new rationale** — if JasperFx input is still pending, the deferral moves to a specific downstream trigger (e.g. "re-evaluate at M5-S1 when Settlement BC lands, the last Marten BC") with an owner named for the nudge.

Letting Gate 4 drift past M4 without a decision or a re-triggered deferral is not acceptable — it becomes stale governance.

### ADR 014 authoring

Per the M3-S7 retrospective's ADR candidate review, the M3-D2 Path A pattern earns an ADR on its second application. M4-S6 is that second application (Session-membership fields + Withdrawn status transition added to `CatalogListingView` via a new sibling handler class). ADR 014 is authored in S6 alongside the code that justifies it, not in S1 or S7 — the lived second application is the evidence.

Decision shape (draft):
- **Title:** Cross-BC Read-Model Extension Shape
- **Status:** Proposed at S6 open; Accepted at S6 close when tests are green
- **Decision:** Path A — one `CatalogListingView` per logical entity, sibling handler classes, fields added additively
- **Key sub-question surfaced at S6 — sibling handler scoping:** Through M3 each sibling handler in `CritterBids.Listings` consumed from exactly one source BC (`ListingSnapshotHandler` ← Selling, `AuctionStatusHandler` ← Auctions). M4-S6's `SessionMembershipHandler` as currently scoped consumes the session trio from `listings-auctions-events` **and** `ListingWithdrawn` from `listings-selling-events` — a new multi-source sibling topology. ADR 014 must pick one shape explicitly:
  - **Option A — one sibling per source BC (M3 precedent).** Split into `SessionMembershipHandler` (Auctions-sourced) and `ListingWithdrawnHandler` (Selling-sourced). Stricter isolation, more files, aligns with existing precedent.
  - **Option B — one sibling per logical feature (plan's implicit choice).** Merged `SessionMembershipHandler` consuming from both queues. Fewer files, but sets precedent for multi-source siblings that will propagate to M5 (Settlement) and M5+ (Obligations).
  The ADR's resolution sets precedent for every subsequent sibling; do not defer to a fourth lived application.
- **Alternatives considered for the top-level path:** Path B (one view per source BC, UI-side join) and Path C (native `MultiStreamProjection` with cross-BC composition)
- **Evidence:** M3-S6 auction-status fields (`ListingSnapshotHandler` + `AuctionStatusHandler`, single-source each); M4-S6 session + withdrawn fields (additional sibling — one or two depending on the sub-question's resolution)
- **Consequences:** The pattern will apply again at M5 (Settlement status fields) and M5+ (Obligations status fields); cross-BC read-model extension becomes a named, ADR-backed pattern rather than folklore

### Aspire RabbitMQ management UI exposure

M3-S7's smoke test noted that the Aspire-provisioned RabbitMQ container exposes AMQP (port 5672) only; the management UI (port 15672) is not exposed. S7 used `rabbitmqctl list_queues` via `docker exec` as the workaround. M4-S1 exposes the management UI in `CritterBids.AppHost/Program.cs` alongside the other docs-only work — fifteen minutes of effort, not its own session.

### Retrospective skills work

- `wolverine-sagas.md` — new section or expanded §5 covering composite-key saga correlation (the Proxy Bid Manager is the first in-repo example); folded in at S4 close per the M3-established retrospective-skills discipline
- `marten-projections.md` §7 — reinforced with the second lived Path A application; folded in at S6 close alongside ADR 014

---

## 3. Explicit Non-Goals

Hard line — if you catch yourself building any of these in M4, stop and flag it:

- **Settlement BC work** — M5. `ListingSold` and `BuyItNowPurchased` continue to be published without a consumer through M4; `ReserveCheckCompleted` is M5.
- **Obligations, Relay, Operations BC work** — post-M5.
- **HTTP endpoint surface** — M6 with the frontends. Commands continue to be exercised through `IMessageBus` in dispatch tests. The M2-deferred `POST /api/listings/submit` remains deferred (tech debt list).
- **React frontends (`critterbids-web`, `critterbids-ops`)** — M6.
- **Real authentication scheme** — M6. `[AllowAnonymous]` through M5 remains the intentional project stance.
- **SignalR wiring and Relay BC** — post-M5. The `ProxyBidExhausted` contract is authored in M4 but has no consumer until Relay exists.
- **Session rescheduling, pausing, or cancellation after start** — not in Workshop 002 §5. `StartSession` is terminal; a started session runs its listings through the standard Auction Closing saga. Post-MVP if ever.
- **Session lifecycle beyond the three events** — no `SessionEnded`, no pre-start cancellation, no detach-listing-from-session. The Workshop 002 §5 scope is exactly what M4 implements.
- **`StartSession` filtering of listings withdrawn since attach** — not in M4. If a listing is attached to a session and then withdrawn via Selling's `WithdrawListing` before the session starts, `StartSession` still emits `SessionStarted` with the full `ListingIds[]`, and the fan-out handler produces `BiddingOpened` for that listing. Termination happens reactively: either the DCB on the already-withdrawn listing rejects the `BiddingOpened`, or the Auction Closing saga's consumption of the earlier `ListingWithdrawn` closes the flow. Defensive pre-filtering at `StartSession` time is post-MVP hardening; Workshop 002 §5 does not assert it. The exact terminal path observed at S5 is captured in the retrospective.
- **Proxy cancellation or modification after registration** — not in Workshop 002 §4. A participant cannot adjust their `MaxAmount` or cancel the proxy once registered. The proxy terminates only on `ListingSold` / `ListingPassed` / `ListingWithdrawn` / `ProxyBidExhausted`. Post-MVP if ever.
- **Multiple simultaneous Flash Sessions** — MVP supports one active Flash Session at a time (per Workshop 001 demo flow). M4 scenarios do not assert a cross-session constraint; it lands organically if Ops can only show one at a time on the dashboard. Not a hard-enforced invariant in M4.
- **Proxy bid stored for an unstarted session's listings** — proxies require a live `BiddingOpened`. If a participant attempts to register a proxy on a listing that is attached to an unstarted session, the command is rejected. Exact error shape resolved in S3.
- **Selling BC `ReviseListing`, `EndListingEarly`, `Relist`** — same deferral as M2–M3.
- **Withdrawal by a party other than the seller** — M4's `WithdrawListing` is a seller-initiated command only. Ops-staff-initiated withdrawal (abuse, fraud) is post-M4.

---

## 4. Solution Layout

### No new projects

M4 extends existing projects. The solution layout is unchanged from M3:

```
CritterBids/
├── CritterBids.sln
├── Directory.Packages.props
├── src/
│   ├── CritterBids.AppHost/              # RabbitMQ management UI port exposure (S1)
│   ├── CritterBids.Api/                  # unchanged
│   ├── CritterBids.Contracts/            # +Auctions/Session* +Auctions/ProxyBid* +Selling/ListingWithdrawn
│   ├── CritterBids.Participants/         # unchanged
│   ├── CritterBids.Selling/              # +WithdrawListing command and handler
│   ├── CritterBids.Listings/             # +SessionMembershipHandler (S6)
│   └── CritterBids.Auctions/             # +Session aggregate, +ProxyBidManagerSaga, +SessionStartedHandler
└── tests/
    ├── CritterBids.Api.Tests/
    ├── CritterBids.Contracts.Tests/
    ├── CritterBids.Participants.Tests/
    ├── CritterBids.Selling.Tests/        # +WithdrawListing tests (~4)
    ├── CritterBids.Listings.Tests/       # +Session/withdrawn catalog tests (~4)
    └── CritterBids.Auctions.Tests/       # +Session + Proxy tests (~22)
```

No new project references. The `CritterBids.Api.csproj` reference graph is stable.

### New files added in M4 (representative, not exhaustive)

```
src/CritterBids.Contracts/Auctions/
  RegisterProxyBid.cs               (command)
  ProxyBidRegistered.cs
  ProxyBidExhausted.cs
  SessionCreated.cs
  ListingAttachedToSession.cs
  SessionStarted.cs

src/CritterBids.Contracts/Selling/
  ListingWithdrawn.cs

src/CritterBids.Selling/
  WithdrawListing.cs                (command)
  WithdrawListingHandler.cs         (if separate from SellerListing aggregate decide method)

src/CritterBids.Auctions/
  Session.cs                        (aggregate)
  CreateSession.cs                  (command)
  AttachListingToSession.cs         (command)
  StartSession.cs                   (command)
  SessionStartedHandler.cs          (fan-out: SessionStarted → BiddingOpened per listing)
  ProxyBidManagerSaga.cs
  ProxyBidManagerStatus.cs

src/CritterBids.Listings/
  SessionMembershipHandler.cs       (sibling to ListingSnapshotHandler and AuctionStatusHandler)
```

---

## 5. Infrastructure

### Marten configuration

Auctions gains two new registered types in its `AddAuctionsModule()` call:
- `Session` aggregate (event-sourced, `LiveStreamAggregation<Session>()`)
- `ProxyBidManagerSaga` (Marten document; saga storage handled by Wolverine integration)

Plus the new event types:
- `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`
- `ProxyBidRegistered`, `ProxyBidExhausted`

Event type registration (`AddEventType<T>()`) happens at the same commit as the event type itself — the M2 key learning about silent `AggregateStreamAsync<T>` null returns continues to apply.

Selling gains one new event type:
- `ListingWithdrawn`

Listings gains consumers but no new stored types.

### RabbitMQ routing

No new queues. Three new routing rules added to `Program.cs`:

| Source BC | Event | Queue | Destination BC |
|---|---|---|---|
| Selling | `ListingWithdrawn` | `auctions-selling-events` | Auctions (saga termination) |
| Selling | `ListingWithdrawn` | `listings-selling-events` | Listings (catalog status) |
| Auctions | `SessionCreated` / `ListingAttachedToSession` / `SessionStarted` | `listings-auctions-events` | Listings (catalog session fields) |

Listings-side `ListenToRabbitQueue` binding for `listings-selling-events` already exists; the new routing rule is purely publisher-side.

### Scheduled messages

Proxy Bid Manager does **not** use scheduled messages (per Workshop 002 Phase 1 architecture summary — the proxy is reactive to `BidPlaced`, not timer-driven). This is the second saga in CritterBids but the scheduled-message-cancel pattern is still exercised only by Auction Closing. The skill file update at S4 should explicitly call this out — scheduled messages are saga-specific infrastructure, not a universal requirement.

### No new stores

Shared primary Marten store (ADR 009) is unchanged. All-Marten (ADR 011) is unchanged. No ancillary store, no Polecat.

---

## 6. Conventions Pinned

Conventions inherit from `CLAUDE.md` and all prior milestones unless overridden below.

### `[WriteAggregate]` from first commit

Every aggregate command handler (`CreateSession`, `AttachListingToSession`, `StartSession`, `WithdrawListing`) uses `[WriteAggregate(nameof(Command.AggregateIdProperty))]` from the first commit, per M2.5 / M3 precedent. Every command gets a dispatch test.

### UUID strategy

- **Session stream IDs:** UUID v7 (`Guid.CreateVersion7()`) — per ADR 007 stream-ID section, consistent with all Marten BCs
- **ProxyBidManagerSaga document ID:** UUID v5 on the composite key, namespace + deterministic name = `$"{ListingId}:{BidderId}"` per `002-scenarios.md` §4.1. The namespace constant goes in a new `AuctionsIdentityNamespaces` static class if one doesn't already exist, or is added to an existing identity-namespaces class if there is one.
- **Event row IDs:** per ADR 007 Gate 4 disposition (resolved or re-deferred in S1)

### Proxy saga idempotency

The Proxy Bid Manager reacts to `BidPlaced` events. Replays must be idempotent:
- **Own bids (IsProxy = true or false)** — updated `LastBidAmount` if higher, otherwise no-op. Saga stays Active.
- **Competing bids where `nextBid <= competingBid`** — emit `ProxyBidExhausted` once and `MarkCompleted()`. Saga terminal; status = Exhausted. Subsequent redeliveries land on the `NotFound` branch (same convention as Auction Closing saga per M3 discovery).
- **Terminal events (`ListingSold` / `ListingPassed` / `ListingWithdrawn`)** — idempotent terminal guard (`if Status != Active return`); set status = ListingClosed; `MarkCompleted()`.

### SessionStarted fan-out handler idempotency

The `SessionStarted → BiddingOpened` handler receives one inbound `SessionStarted` event carrying `ListingIds[]` and produces one `BiddingOpened` per listing. Redelivery (Wolverine retry, Rabbit redelivery, replay during projection rebuild) must not double-fire — the invariant is exactly N `BiddingOpened` events per session start, not 2N on a second delivery.

**Primary mechanism (proposed, confirmed in S5):** rely on per-listing `BidConsistencyState` DCB to reject a second `BiddingOpened` append to a stream that is already open. The handler is stateless — it appends unconditionally through `OutgoingMessages` and treats DCB rejection on an already-open listing as an expected no-op, not a handler failure.

**Fallback if S5 discovers DCB does not compose cleanly with `OutgoingMessages` fan-out** (e.g. one per-listing rejection aborts the whole handler invocation): pre-query each listing's bidding-state before emission, skip listings already open. Either shape must pass `SessionStarted_Redelivery_DoesNotDoubleFireBiddingOpened`.

S5 is the first session to exercise fan-out in CritterBids — Auction Closing saga (M3) and Proxy Bid Manager saga (M4-S3/S4) both emit at most one outbound event per inbound. Whichever mechanism S5 confirms must be folded into `docs/skills/wolverine-message-handlers.md` retrospectively.

### Session aggregate command semantics

- **`CreateSession`** — always creates a new aggregate. No "session already exists with this title" check; titles are not unique identifiers.
- **`AttachListingToSession`** — rejects if the listing is not in `Published` status. Check mechanism (M4-D4, resolved in S1): **Auctions-side duplicate `PublishedListings` projection** (option 4). Auctions subscribes to `ListingPublished` and `ListingWithdrawn` on the existing `auctions-selling-events` queue and maintains a small Marten document projection keyed by `ListingId` recording only the published/withdrawn transition — no fields duplicated beyond what the handler needs. Handler loads the projection inline; if no row exists or the row is in a non-published state, the command rejects. Preserves BC isolation (Auctions never reads a Listings-owned view), matches the M3 precedent of single-queue consumers, and is a named pattern across modular monoliths (duplicate projection) — no ADR 015 trigger. Residual in S5: event-subscription wiring, catch-up/rebuild behaviour, and projection tests; S5b is pre-drafted to absorb the surface if it exceeds the S5 sizing line. Also rejects if the session is already started.
- **`StartSession`** — rejects if the session has zero attached listings, or if already started. Terminal — no unstart, no pause.

### `ListingWithdrawn` authority

`ListingWithdrawn` is produced only by the Selling BC's `WithdrawListing` handler. Auctions continues to **consume** `ListingWithdrawn` (saga terminal path) but never produces it. The M3 test-fixture `ListingWithdrawn` synthesis is replaced by a real Selling producer; any remaining hand-crafted usage in saga tests is a unit-test-only shortcut clearly isolated from integration paths.

### Catalog projection extension — the M3-D2 Path A pattern applied a second time

`CatalogListingView` gains:
- `SessionId: Guid?` — null for non-flash listings
- `SessionStartedAt: DateTimeOffset?` — null until the session starts
- `Status: "Withdrawn"` transition — new `ClosedReason` value or separate status field, shape decided in S6

Implementation: a new `SessionMembershipHandler` sibling class in `CritterBids.Listings`, subscribed to `listings-auctions-events` for the session trio and to `listings-selling-events` for `ListingWithdrawn`. Tolerant-upsert primitive (`LoadAsync ?? new`) applied uniformly per M3-S6 precedent. No changes to `ListingSnapshotHandler` or `AuctionStatusHandler`.

### No new auth or storage conventions

M4 introduces no new auth or storage conventions. `[AllowAnonymous]` everywhere through M5 is unchanged.

---

## 7. Acceptance Tests

Tests organized by project. All integration tests use xUnit + Shouldly + Testcontainers + Alba per `critter-stack-testing-patterns.md`.

### `CritterBids.Auctions.Tests`

#### `SessionAggregateTests.cs` (S5)

Mapping from `002-scenarios.md` §5. Integration tests — event-sourced aggregate requires Marten.

| Scenario | Test method |
|---|---|
| 5.1 — Create session | `CreateSession_ProducesSessionCreated` |
| 5.2 — Attach published listing | `AttachListing_Published_ProducesListingAttachedToSession` |
| 5.3 — Reject attach, listing not published | `AttachListing_NotPublished_Rejected` |
| 5.4 — Reject attach, session already started | `AttachListing_SessionStarted_Rejected` |
| 5.5 — Start session happy path | `StartSession_WithAttachedListings_ProducesSessionStarted` |
| 5.6 — Reject start, no listings | `StartSession_NoListings_Rejected` |
| 5.7 — Reject start, already started | `StartSession_AlreadyStarted_Rejected` |

**Plus: `SessionStartedFanOutTests.cs` (S5)** — integration tests for the `SessionStarted → BiddingOpened` fan-out handler:

| Scenario | Test method |
|---|---|
| `SessionStarted` with N listings produces N `BiddingOpened` | `SessionStarted_ProducesBiddingOpenedPerListing` |
| Fan-out is idempotent on redelivery | `SessionStarted_Redelivery_DoesNotDoubleFireBiddingOpened` |

**Plus: `CreateSessionDispatchTests.cs`, `AttachListingToSessionDispatchTests.cs`, `StartSessionDispatchTests.cs`** — one integration test each dispatching via `IMessageBus`.

#### `ProxyBidManagerSagaTests.cs` (S3 + S4)

Mapping from `002-scenarios.md` §4. Saga tests use the saga test harness established in M3-S5.

| Scenario | Test method | Session |
|---|---|---|
| 4.1 — Proxy registration starts saga | `RegisterProxyBid_StartsSaga_ProducesProxyBidRegistered` | S3 |
| 4.2 — Competing bid, proxy auto-bids | `CompetingBid_ProxyAutoBidsOneIncrementAbove` | S3 |
| 4.3 — Competing bid, proxy exhausted | `CompetingBid_NextBidExceedsMax_ProducesProxyBidExhausted` | S4 |
| 4.4 — Own proxy bid, track no-op | `OwnProxyBid_TracksNoReact` | S3 |
| 4.5 — Own manual bid, track no-op | `OwnManualBid_TracksNoReact` | S3 |
| 4.6 — `ListingSold` terminates saga | `ListingSold_CompletesSaga` | S4 |
| 4.7 — `ListingPassed` terminates saga | `ListingPassed_CompletesSaga` | S4 |
| 4.8 — `ListingWithdrawn` terminates saga | `ListingWithdrawn_CompletesSaga` | S4 |
| 4.9 — Credit ceiling cap on proxy | `CompetingBidAtCeiling_ProducesProxyBidExhausted` | S4 |
| 4.10 — Two proxies bidding war | `TwoProxies_WeakerExhausts_StrongerWins` | S4 |
| 4.11 — Proxy registered when already outbid | `RegisterProxy_WhileOutbid_WaitsForNextCompetingBid` | S4 |

**Plus: `RegisterProxyBidDispatchTests.cs` (S3)** — one integration test dispatching via `IMessageBus`.

**Auctions test count at M4 close:** 35 (M3) + 7 (Session) + 2 (fan-out) + 3 (Session dispatch) + 11 (Proxy) + 1 (Proxy dispatch) = **59**.

### `CritterBids.Selling.Tests` (S2)

Additions to the Selling test suite for `WithdrawListing`:

| Scenario | Test method |
|---|---|
| Withdraw published listing | `WithdrawListing_Published_ProducesListingWithdrawn` |
| Reject, listing not yet published | `WithdrawListing_NotPublished_Rejected` |
| Reject, listing already closed | `WithdrawListing_AlreadyClosed_Rejected` |
| Dispatch via `IMessageBus` | `WithdrawListing_DispatchedViaMessageBus_InvokesHandler` |

**Selling test count at M4 close:** 32 (M3) + 4 = **36**.

### `CritterBids.Listings.Tests` (S6)

Additions to `CatalogListingViewTests.cs` — extending the view with session + withdrawn fields.

| Scenario | Test method |
|---|---|
| `ListingAttachedToSession` sets `SessionId` on catalog entry | `ListingAttachedToSession_SetsSessionId` |
| `SessionStarted` sets `SessionStartedAt` on all member listings | `SessionStarted_SetsSessionStartedAtForMemberListings` |
| `ListingWithdrawn` transitions status to Withdrawn | `ListingWithdrawn_SetsCatalogStatusWithdrawn` |
| `SessionMembershipHandler` does not collide with `AuctionStatusHandler` writes | `SiblingHandlers_CoexistOnSameView_NoOverwrites` |

**Listings test count at M4 close:** 11 (M3) + 4 = **15**.

### Test count summary at M4 close

| Project | M3 Close | M4 Delta | M4 Close | Type |
|---|---|---|---|---|
| `CritterBids.Auctions.Tests` | 35 | **+24** | **59** | Integration (saga + aggregate + fan-out + dispatch) |
| `CritterBids.Selling.Tests` | 32 | **+4** | **36** | Mixed (aggregate + dispatch) |
| `CritterBids.Listings.Tests` | 11 | **+4** | **15** | Integration (projection) |
| `CritterBids.Participants.Tests` | 6 | 0 | 6 | Unchanged |
| `CritterBids.Api.Tests` | 1 | 0 | 1 | Unchanged |
| `CritterBids.Contracts.Tests` | 1 | 0 | 1 | Unchanged |
| **Total** | **86** | **+32** | **118** | |

---

## 8. Open Questions / Decisions

| ID | Question | Disposition |
|---|---|---|
| ADR 007 Gate 4 | Event row ID strategy — close with JasperFx input or re-defer with specific new trigger | **Resolved in S1 (2026-04-20) — re-deferred with new trigger + named owner.** JasperFx input still not in hand; M3 shipped on engine default without incident. Re-deferred per ADR 007 amendment section "Event Row ID Decision — Re-Deferred (M4-S1)"; new trigger is M5-S1 (Settlement BC foundation decisions — the last Marten BC foundation session), named owner Erik for the JasperFx follow-up nudge. Bare re-deferral rejected per prompt's open-questions guidance. |
| M4-D1 | Proxy saga composite key format — `$"{ListingId}:{BidderId}"` vs byte-concatenation before UUID v5 hash | **Resolved in S1 — string form `$"{ListingId}:{BidderId}"` per Workshop 002 §4.1.** Pinned in code in the new `src/CritterBids.Auctions/AuctionsIdentityNamespaces.cs` with a dedicated `ProxyBidManagerSaga` namespace Guid. Byte-concatenation was not chosen — the colon-separated string form is human-readable in logs and matches the workshop scenario text verbatim, and the UUID v5 SHA-1 hash is deterministic on either input so the hash-domain cost is identical. |
| M4-D2 | Session aggregate ID — UUID v7 (new entity, no natural key) or UUID v5 (deterministic from Title + CreatedAt) | **Resolved in S1 — UUID v7 (`Guid.CreateVersion7()`).** Rationale: no natural business key exists (Title is not a unique identifier — two Flash sessions can share a title), and every event-sourced aggregate in the codebase uses v7 per ADR 007's Stream ID Decision section. UUID v5 would have required asserting Title as a stable business key, which it is not. S5 implements the aggregate using v7; no code ships from S1 for this decision. |
| M4-D3 | ADR 014 timing — author at S6 (second Path A application landed) or bundled into S7 retro | **Call in S6.** Author alongside the code that justifies it, per the rule of thumb that ADRs record lived decisions. S7 reviews and closes. ADR 014 row reserved in `docs/decisions/README.md` at S1. |
| M4-D4 | `AttachListingToSession` published-status check — query `CatalogListingView` directly from Auctions, require Selling to publish a lightweight "listing-available" signal, accept the handler-time ambiguity, or maintain an Auctions-side duplicate `PublishedListings` projection | **Resolved in S1 — option 4: Auctions-side duplicate `PublishedListings` projection.** Auctions subscribes to `ListingPublished` and `ListingWithdrawn` on the existing `auctions-selling-events` queue and maintains a small Marten document projection keyed by `ListingId` recording only the published/withdrawn state transition. Handler loads the projection inline during `AttachListingToSession`; absence or non-published state rejects the command. Rationale: preserves BC isolation (Auctions never reads a Listings-owned view — option 1 rejected for this reason and for its ADR 015 cost), resolves the workshop §5.3 reject-not-published requirement (option 3 rejected), and avoids adding a lightweight cross-BC signal event that would duplicate information already in `ListingPublished`/`ListingWithdrawn` (option 2 rejected). Duplicate projections across BCs are a named modular-monolith pattern, so no ADR trigger. Residual in S5: event-subscription wiring + catch-up/rebuild behaviour + projection tests — already pre-drafted as an S5b trigger per §9. **No ADR 015 candidate flagged at M4-S1** — option 1 was not chosen. |
| M4-D5 | `ListingWithdrawn` status field on `CatalogListingView` — new `Withdrawn` enum value, or `ClosedReason = "Withdrawn"` | **Resolve in S6.** Likely a new enum value on the existing `Status` field rather than overloading `ClosedReason`, since withdrawn is mechanically distinct from closed (no bidding happened, or bidding was cut short). S6 decides and folds into the `CatalogListingView` shape. |
| M4-D6 | M3 test-fixture `ListingWithdrawn` synthesis — remove entirely once real producer exists, or keep as unit-test shortcut | **Resolve in S2.** Preference: keep as a unit-test shortcut for saga tests that only need the event shape, clearly marked; replace with real producer in integration paths. |
| M2-deferred | `POST /api/listings/submit` HTTP endpoint | **Stays deferred to M6 frontend milestone.** M4 does not open the HTTP surface. |
| M2-deferred | RabbitMQ routing in BC modules vs `Program.cs` | **Stays deferred.** No M4 session has scope to rework this; continues in `Program.cs`. |

---

## 9. Session Breakdown

Seven sessions, matching M2–M3 shape. S1 is docs-only; S7 is retrospective + skills + M4 close. Implementation sessions each correspond to a PR and a retrospective. Half-session buffer is built in — S3+S4 together cover the Proxy Bid Manager, with S4b pre-drafted as a preemptive split slot per the M3 retrospective's recommendation ("acceptance criteria approaching 20, preemptively draft an Xb continuation").

| # | Prompt file | Scope summary |
|---|---|---|
| 1 | `docs/prompts/M4-S1-auctions-completion-foundation-decisions.md` | Docs only. ADR 007 Gate 4 final re-evaluation; resolve M4-D1, M4-D2, and M4-D4 (the last promoted from S5 because it sets a cross-BC read precedent); author seven contract stubs (`RegisterProxyBid`, `ProxyBidRegistered`, `ProxyBidExhausted`, `SessionCreated`, `ListingAttachedToSession`, `SessionStarted`, `ListingWithdrawn`); expose Aspire RabbitMQ management UI port. No handlers, no code paths. If M4-D4 resolves to "cross-BC read from handlers," flag as **ADR 015 candidate** (Cross-BC read access from handlers) for later authorship. |
| 2 | `docs/prompts/M4-S2-selling-withdraw-listing.md` | Selling-side `WithdrawListing` command + `ListingWithdrawn` producer. `SellerListing.Apply(ListingWithdrawn)`. RabbitMQ routing rule added for Auctions and Listings consumers. 4 scenarios. The M3 fixture-synthesized `ListingWithdrawn` is replaced by the real producer in integration tests; unit-test shortcut retained per M4-D6. |
| 3 | `docs/prompts/M4-S3-proxy-bid-manager-saga-skeleton.md` | **Risk session.** Proxy Bid Manager saga with composite UUID v5 correlation; `RegisterProxyBid` handler starts the saga; reactive-path handlers for own bids and competing bids up to exhaustion trigger. Workshop 002 §4.1–4.5 (5 scenarios) + `RegisterProxyBid` dispatch test. First in-repo composite-key saga. Skill `wolverine-sagas.md` updated retrospectively. |
| 4 | `docs/prompts/M4-S4-proxy-bid-manager-terminal-paths.md` | Proxy saga completion: exhaustion event emission, terminal handlers for `ListingSold` / `ListingPassed` / `ListingWithdrawn`, credit-ceiling cap, two-proxy bidding war, register-while-outbid. Workshop 002 §4.6–4.11 (6 scenarios). Close `wolverine-sagas.md` updates from S3. |
| 5 | `docs/prompts/M4-S5-session-aggregate.md` | Session aggregate with `CreateSession` / `AttachListingToSession` / `StartSession` commands + `SessionStarted → BiddingOpened` fan-out handler. Workshop 002 §5 (7 scenarios) + fan-out tests + 3 dispatch tests. `AttachListingToSession` published-status check implemented per the S1 disposition of M4-D4 — no design work here, only implementation. |
| 6 | `docs/prompts/M4-S6-listings-catalog-session-and-withdrawn.md` | Listings BC catalog extension — new `SessionMembershipHandler` sibling class, `CatalogListingView` gains session + withdrawn fields. Second Path A application. ADR 014 authored. 4 projection tests. M4-D5 resolved. |
| 7 | `docs/prompts/M4-S7-retrospective-skills-m4-close.md` | Skills + retro + M4 close. Consolidate S3/S4/S6 skill updates if not fully captured inline. Author M4 retrospective. Confirm ADR 014 accepted status. Operational smoke test against Aspire (now with management UI port exposed). |

### Preemptive split slot

**S4b** is pre-drafted but only executed if S4's scenario count or surface area exceeds the M3 threshold. Candidate split boundary: two-proxy bidding war (§4.10) plus register-while-outbid (§4.11) — these are the least-coupled scenarios to the exhaustion + terminal paths and can defer cleanly. No S3b pre-drafted; S3 is tightly scoped to registration + reactive-path mechanics.

**S5b** is pre-drafted as a second split candidate. S5 carries three commands, a fan-out handler, twelve tests (7 aggregate + 2 fan-out + 3 dispatch), and the implementation of whatever M4-D4 resolution S1 pinned. Trigger conditions:
- S1's M4-D4 resolution is "Auctions-side duplicate `PublishedListings` projection," which adds event-subscription wiring + catch-up + tests beyond S5's current sizing; OR
- S5's acceptance test count exceeds 12 at session open; OR
- The fan-out handler's idempotency mechanism (§6) turns out to need the fallback pre-query shape rather than the DCB-only primary mechanism, significantly expanding the fan-out test surface.

Candidate split boundary at S5b: the `SessionStarted → BiddingOpened` fan-out handler and its two tests. The Session aggregate and its three command handlers (plus dispatch tests) are the least-coupled piece and land as S5; fan-out ships as S5b. Rationale: the aggregate has value independently (a session can be created, attached, started) even before the fan-out is wired — the downstream listings simply do not open for bidding until S5b, which is acceptable because S6 (Listings projection consumer) has not yet landed either.

### Session dependency graph

```
S1 (docs — Gate 4, M4-D1/D2, contract stubs, Aspire UI port)
 └── S2 (Selling WithdrawListing + ListingWithdrawn producer)
      └── S3 (Proxy Bid Manager saga skeleton — reactive path)
           └── S4 (Proxy Bid Manager terminal paths + bidding war)
                └── S5 (Session aggregate + SessionStarted fan-out)
                     └── S6 (Listings catalog extension + ADR 014)
                          └── S7 (skills + retro + M4 close)
```

Sessions are strictly sequential. S3 and S4 are the risk nodes; S5 is moderate; S6 is pattern-stable by M3 precedent.

### Session sizing notes

- **S1 is the largest docs session yet.** Three concurrent tracks (ADR 007 Gate 4 closure, M4-D1/D2 resolution, seven contract stubs) plus the Aspire UI port fix. Risk is scope creep into handler shapes; discipline is "stubs only, no implementation."
- **S3 is the primary risk session.** First in-repo composite-key saga. The correlation-key shape (UUID v5 on `$"{ListingId}:{BidderId}"`) is novel, and the reactive-path `BidPlaced` subscription must be carefully isolated from the Auction Closing saga's `BidPlaced` subscription — two sagas consuming the same event with different correlation keys. Wolverine supports this, but it is the first time the pattern is exercised in CritterBids. Skill-file update at S3 close is mandatory.
- **S4 is the second-largest.** Two-proxy bidding war (§4.10) is the hardest scenario to test — it exercises saga-to-saga message flow through a shared `BidPlaced` stream within a single message processing cycle. The skill file's testing section (updated in S3) is the reference.
- **S5 is moderate with a known risk surface.** Session aggregate is a standard event-sourced aggregate, pattern-stable. The fan-out handler (`SessionStarted → BiddingOpened`) is the first in-repo instance of one inbound producing N outbound via `OutgoingMessages`, and its idempotency mechanism (§6) is proposed-but-unconfirmed. Additionally, whichever M4-D4 shape S1 pinned lands here. S5b is pre-drafted to absorb either surprise (fan-out fallback path or M4-D4 duplicate-projection scaffolding).
- **S6 is the lowest-risk implementation session.** Pattern-stable by M3-S6 precedent. ADR 014 authorship is the new element; use the M3-D2 section of the retro as the starting point.
- **S2 is smaller than it looks.** One new command, one new event, one `Apply` method, one routing rule update, four scenarios. The cross-BC coordination (replacing M3 fixture synthesis with real producer) is what justifies its own session.
- **S7 is docs-only, risk is scope creep.** Smoke test is operational evidence only; no code changes.

### Risk watch-items

Lifted forward from the M3 retro's "three risk nodes" pattern:

1. **Two sagas subscribed to `BidPlaced` with different correlation keys.** Auction Closing saga correlates on `ListingId`; Proxy Bid Manager saga correlates on a composite UUID v5 derived from `ListingId + BidderId`. Wolverine handles the multi-saga dispatch, but the `[SagaIdentityFrom]` attribute shapes differ and the handler-discovery rules from M3-S6 (cross-BC handler shadowing) may surface again in the Auctions-internal two-saga configuration. If S3 hits a `NoHandlerForEndpointException` or a similar dispatch ambiguity, the fix pattern is the same `*DiscoveryExclusion` trick — but the underlying principle (foreign-BC contribution under `Separated` mode) is different from the M3 case.
2. **Two-proxy bidding war timing.** Workshop 002 §4.10 asserts the escalation completes in milliseconds within a single message processing cycle. If Wolverine's handler execution does not in fact chain proxy reactions within one cycle (e.g. if each `BidPlaced` is delivered via a separate Rabbit round-trip), the test shape needs to accommodate eventual-consistency delays. Resolve in S4 with the first two-proxy integration test.
3. **M4-D4 resolution residual.** Resolved in S1 (promoted from S5 because it sets a cross-BC read precedent — see §8). Residual risk at S5 open: if S1's resolution introduces an Auctions-side duplicate `PublishedListings` projection, that projection needs its own event-subscription wiring, catch-up / rebuild behaviour, and test scaffolding in S5 — work that is not currently sized into the S5 scope line. If S1 resolves to "query `CatalogListingView` directly from Auctions," the residual is smaller but an ADR 015 authoring effort attaches to S7 (or earlier). S1's prompt must name whichever residual it creates so S5 can absorb or split.

Any of these blowing up may justify an unplanned docs follow-up session or an S3b/S4b/S5b split. If so, the split takes a number in the M4 session log and the count moves from 7 to 8.

---

## Appendix: Cross-BC Integration Map at M4 Close

Six integration connections at M4 close — three from earlier milestones unchanged, three new or extended in M4:

```
Participants ─── SellerRegistrationCompleted ────────────► Selling
              (queue: selling-participants-events — M2)    (RegisteredSellers projection)

Selling ─────── ListingPublished ────────────────────────► Listings
              (queue: listings-selling-events — M2)        (CatalogListingView — base projection)

Selling ─────── ListingPublished ────────────────────────► Auctions
              (queue: auctions-selling-events — M3)        (BiddingOpened produced)

Selling ─────── ListingWithdrawn ────────────────────────► Auctions        [NEW M4]
              (queue: auctions-selling-events — existing)  (AuctionClosingSaga + ProxyBidManagerSaga termination)

Selling ─────── ListingWithdrawn ────────────────────────► Listings        [NEW M4]
              (queue: listings-selling-events — existing)  (CatalogListingView — Withdrawn status)

Auctions ────── BiddingOpened, BidPlaced,                ─► Listings
                BiddingClosed, ListingSold, ListingPassed,
                BuyItNowPurchased
              (queue: listings-auctions-events — M3)       (CatalogListingView — auction-status fields)

Auctions ────── SessionCreated,                          ─► Listings        [NEW M4]
                ListingAttachedToSession,
                SessionStarted
              (queue: listings-auctions-events — existing) (CatalogListingView — session membership)
```

**Internal Auctions flows new at M4:**

```
Auctions  ────── SessionStarted                          ─► Auctions        [NEW M4]
                                                            (SessionStartedHandler fans out one
                                                             BiddingOpened per attached listing)

Auctions  ────── BidPlaced                               ─► Auctions        [NEW M4]
                                                            (ProxyBidManagerSaga reacts —
                                                             the second BidPlaced subscriber alongside
                                                             AuctionClosingSaga)

Auctions  ────── RegisterProxyBid (command)              ─► Auctions        [NEW M4]
                                                            (ProxyBidManagerSaga started)
```

Settlement remains a future consumer of `ListingPublished` (for reserve value) and `ListingSold` / `BuyItNowPurchased` (for settlement). Neither subscription exists at M4 close — authored in M5. The contract payloads are complete for those future consumers per the `integration-messaging.md` L2 discipline maintained across M2–M4.

At M4 close, the Auctions BC is **feature-complete for MVP** — all Workshop 002 scenarios (48) are implemented and green, all eight BC-internal components (Session, Listing aggregate, BidConsistencyState DCB, PlaceBid handler, BuyNow handler, Auction Closing saga, Proxy Bid Manager saga, SessionStarted fan-out) are in place, and the integration surface to Listings is stable. The demo-path from QR-code scan (M1 participant session) through Flash Session start (M4) through simultaneous listing close (M3 Auction Closing saga) runs end-to-end with proxy bid defense, extended bidding, and reserve evaluation. No frontend, no Settlement, no Relay — but the core "Flash Session with real bidding depth" story works end-to-end and is exercised by integration tests.
