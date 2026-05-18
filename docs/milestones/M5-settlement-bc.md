# M5 — Settlement BC

**Status:** Shipped (M5-S6 closed 2026-05-17)
**Scope:** Settlement BC core — the seven-phase workflow (Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed) that consumes `ListingSold` from Auctions and `BuyItNowPurchased` from Auctions, plus the `PendingSettlement` projection seeded from `ListingPublished` in Selling. Real payment-processor integration is post-MVP; the credit-ledger posture from narrative 002 Setting governs all money movements.
**Companion docs:** [`../workshops/003-settlement-bc-deep-dive.md`](../workshops/003-settlement-bc-deep-dive.md) · [`../workshops/003-scenarios.md`](../workshops/003-scenarios.md) · [`../narratives/002-winner-clears-settlement.md`](../narratives/002-winner-clears-settlement.md) · [`../narratives/002-findings.md`](../narratives/002-findings.md) · [`../workshops/PARKED-QUESTIONS.md`](../workshops/PARKED-QUESTIONS.md) · [`../skills/README.md`](../skills/README.md) · [`../decisions/007-uuid-strategy.md`](../decisions/007-uuid-strategy.md) · [`../decisions/011-all-marten-pivot.md`](../decisions/011-all-marten-pivot.md)

---

## 1. Goal & Exit Criteria

### Goal

Deliver the Settlement BC: the seven-phase financial workflow that runs when a listing closes with a winning outcome, plus the supporting projections (`PendingSettlement` seeded from `ListingPublished`; bidder-credit read model surfaced via `WinnerCharged`). At M5 close, the keyboard's Flash auction journey from M3 / M4 closes by emitting `ListingSold`, which Settlement consumes, runs the workflow, and produces a complete five-event financial stream (`SettlementInitiated` → `ReserveCheckCompleted` → `WinnerCharged` → `FinalValueFeeCalculated` → `SellerPayoutIssued` → `SettlementCompleted`) plus the integration `SettlementCompleted` event for downstream consumers (Relay's broadcast, Operations' dashboard). The full bidder-experience arc from QR-scan (M1) through settlement (M5) runs end-to-end through real Postgres + RabbitMQ via Testcontainers.

This milestone lands two firsts for the CritterBids codebase: the first multi-phase financial workflow with explicit state transitions and the first cross-BC saga-style coordination that consumes events from two upstream BCs (Auctions and Selling). Both patterns trace back to W003 Phase 1's design decisions; the implementation choice between Wolverine Saga and `ProcessManager<TState>` decider is the M5-S1 foundation decision per W003 Phase 1 Part 2.

The Settlement BC is also the cutover-gate target: M5-S1's prompt is the first slice prompt under the new NDD-informed workflow per the foundation refresh's cutover-gate definition (Phase 5 §3.6 / handoff §15.5). Its `Narrative:` line cites narrative 002 (`docs/narratives/002-winner-clears-settlement.md`) as jointly authoritative scope alongside this milestone doc.

### Exit criteria

- [ ] Solution builds clean with `dotnet build` — 0 errors, 0 warnings
- [ ] Settlement BC implemented: `CritterBids.Settlement` and `CritterBids.Settlement.Tests` projects, `AddSettlementModule()`, Marten config per `adding-bc-module.md` and ADR 011's All-Marten Pivot
- [ ] Settlement workflow hosting choice (Wolverine Saga vs `ProcessManager<TState>`) decided in S1 with rationale documented in ADR-019 (or analogous slug per the next-unreserved pointer in `docs/decisions/README.md`)
- [ ] `PendingSettlement` projection implemented as a Marten document projection seeded from `CritterBids.Contracts.Selling.ListingPublished`; lifecycle states (Pending / Consumed / Expired) managed per W003 Phase 1 Part 1
- [ ] Settlement workflow happy-path implemented: all six decider scenarios from `003-scenarios.md` §1-§6 green; all ten evolver scenarios from §7 green; all four projection scenarios from §8 green; at least the bidding-source happy-path workflow integration scenarios from §9 green
- [ ] `CritterBids.Contracts.Settlement.*` integration events authored — `SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued` (the integration-out set; the Settlement-internal events `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated` stay BC-internal per W003 §"Integration in/out")
- [ ] `[WriteAggregate]` pattern applied with explicit `nameof` override on every Settlement aggregate command (per M2.5-S1 / M2.5-S2 / M3 / M4 precedent), if Saga path chosen; equivalent pattern for `ProcessManager<TState>` if that path chosen
- [ ] `BidderCreditView` (or analogous slug; design call in S1 per F005 from narrative 002 findings) projection implemented, updated on `WinnerCharged`, surfaces remaining credit for downstream consumers (Relay's `SettlementCompleted` broadcast carries `remainingCredit`)
- [ ] At least one dispatch test per Settlement command exercising the Wolverine routing path
- [ ] ADR 007 Gate 4 honored — Settlement event row IDs follow whatever decision M3-S1 closed (UUID v7 if accepted; default if deferred)
- [ ] M5-S1 / M5-S2 / M5-S3 / M5-S4 / M5-S5 / M5-S6 retrospective docs written
- [ ] M5 retrospective doc written

---

## 2. In Scope

### Settlement BC — core components

| Component | What it owns | Scenario source |
|---|---|---|
| `Settlement` workflow (Saga or ProcessManager — S1 decision) | Seven-phase progression: Initiated → ReserveChecked → WinnerCharged → FeeCalculated → PayoutIssued → Completed; failure exit at any phase via `PaymentFailed`; idempotency guards per W003 Phase 1 Part 6's deterministic SettlementId | `003-scenarios.md` §1-§7 (28 decider+evolver scenarios) |
| `PendingSettlement` projection | Cached read model seeded from `ListingPublished`; carries reserve, fee percentage, BIN price, seller identity for use at workflow-start time without crossing the Settlement-Selling boundary | `003-scenarios.md` §8 (4 projection scenarios) |
| `BidderCreditView` projection (slug TBD per F005) | Settlement-side bidder-credit ledger; updated on `WinnerCharged`; surfaces remaining credit for Relay's `SettlementCompleted` broadcast and any future bidder-balance endpoint | F005 follow-up (W003 amendment territory; design call in S1) |
| Financial Event Stream | The append-only audit log of every event in a settlement's lifecycle, one stream per `SettlementId`. Marten-backed (PostgreSQL) per ADR 011's All-Marten Pivot. Never deleted. | W003 §"Ubiquitous Language" Financial Event Stream entry |

Total decider + evolver + projection scenarios in scope: **41** (§1-§8 of `003-scenarios.md`); §9 workflow integration scenarios are exercised end-to-end across S4-S5.

### Cross-BC wiring

| From | Event | To | Purpose |
|---|---|---|---|
| Selling (M2) | `ListingPublished` | Settlement (M5) | Seed `PendingSettlement` projection at publish time so reserve, fee, and seller fields are available when the listing eventually sells |
| Auctions (M3 / M4) | `ListingSold` | Settlement (M5) | Trigger settlement workflow on the bidding-source happy path |
| Auctions (M3 / M4) | `BuyItNowPurchased` | Settlement (M5) | Trigger settlement workflow on the BIN path (skips reserve check; per W003 Phase 1 Part 5) |
| Participants (M1) | `ParticipantSessionStarted` | Settlement (M5) | Initialize `BidderCreditView` row with the bidder's assigned `CreditCeiling` per W003 Phase 1 Part 7 (added at M5-S5; contracts-promotion pre-step lifted `ParticipantSessionStarted` from a Participants-internal record to a cross-BC contract) |
| Settlement (M5) | `SettlementCompleted` | Listings (M5) and Relay (post-M5) | Update `CatalogListingView.Status` to "settled"; broadcast confirmation push to bidder UIs |
| Settlement (M5) | `SellerPayoutIssued` | Relay (post-M5) | Broadcast seller-payout notification |
| Settlement (M5) | `PaymentFailed` | Operations (post-M5) | Surface failure to operator dashboards for intervention |

New RabbitMQ queue routes:

- `settlement-selling-events` — Settlement listens; consumes `ListingPublished` from the existing `listings-selling-events` queue's publisher routing
- `settlement-auctions-events` — Settlement listens; consumes `ListingSold` and `BuyItNowPurchased` from existing Auctions publishing
- `settlement-participants-events` — Settlement listens; consumes `ParticipantSessionStarted` from Participants (added at M5-S5)
- `listings-settlement-events` — Settlement publishes `SettlementCompleted`; Listings listens
- (post-M5) `relay-settlement-events`, `operations-settlement-events` — Relay and Operations subscribe when those BCs ship

Existing queues (`listings-selling-events`, `auctions-selling-events`, `listings-auctions-events`) stay unchanged.

### Integration contracts authored in M5

All go in `src/CritterBids.Contracts/Settlement/`:

- `SettlementCompleted` — terminal happy-path event; carries `SettlementId, ListingId, WinnerId, SellerId, HammerPrice, FeeAmount, SellerPayout, CompletedAt` per `003-scenarios.md` §6.1
- `PaymentFailed` — terminal failure event; carries `SettlementId, ListingId, WinnerId, Reason, FailedAt` per `003-scenarios.md` §3.2
- `SellerPayoutIssued` — payout event for Relay broadcast and seller-side notification; carries `SettlementId, SellerId, PayoutAmount, FeeDeducted, IssuedAt` per `003-scenarios.md` §5.1

Settlement-internal events (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`) stay BC-internal in `src/CritterBids.Settlement/` and do not cross BC boundaries. Per W003 §"Integration in / out" the integration set is intentionally narrow.

Contracts carry complete payload for all future consumers at first commit, per `integration-messaging.md` L2 and the precedent established by every prior BC's contract authoring.

### W003 follow-up amendments folded into S1 (per narrative 002 Findings F002, F004, F005)

Narrative 002's findings file (`docs/narratives/002-findings.md`) routed three findings to a W003 follow-up PR. M5-S1 absorbs them as foundation decisions:

| Finding | Routing | Resolution in M5-S1 |
|---|---|---|
| F002: `Price` / `HammerPrice` rename across initiation undocumented | `document-as-intentional` | Add a "Field Name Convention" note to W003 Phase 1 (or §7's evolver narrative) explaining the rename, the source-agnostic-at-initiation rationale, and the post-initiation specificity |
| F004: `SettlementInitiated` payload mismatch between scenarios §1.1 and §7.1 | `workshop-update` | Normalize §1.1, §1.2, §1.3 to show the full event payload per §7.1's evolver-input form; audit §3.1 / §4.1 / §5.1 / §6.1 SettlementId rendering for consistency |
| F005: Missing named bidder-credit projection in W003 | `workshop-update` | Define the projection's name (`BidderCreditView` or analogous), shape (`(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)`), lifecycle (initialized on `ParticipantSessionStarted`; updated on `WinnerCharged`), and consumer model |

These W003 amendments land in M5-S1's PR as a coordinated workshop edit alongside the foundation decisions. The previously-deferred follow-up PR is no longer needed; M5-S1 absorbs the work.

### Settlement workflow hosting decision in S1

S1 closes the Wolverine Saga vs `ProcessManager<TState>` choice per W003 Phase 1 Part 2. Decision options:

- **Wolverine Saga** — established pattern in CritterBids (Auction Closing saga from M3-S5); reuses Wolverine's saga-state persistence and `MarkCompleted()` lifecycle; fits the seven-phase progression naturally as state transitions on a single document.
- **`ProcessManager<TState>` decider** — Erik (JasperFx core team) is actively designing this framework; Settlement was named in W003 Phase 1 as a natural candidate for the decider-style process manager pattern given its linear, phased workflow with explicit state transitions. Adopting `ProcessManager<TState>` in M5 would be CritterBids' first lived example of the pattern and a contribution back to the JasperFx ecosystem.

The decision is recorded as ADR-019 (or analogous slug per the next-unreserved pointer in `docs/decisions/README.md`). The ADR carries the same shape as ADR 016 / 017 / 018 from foundation-refresh Phase 1 / 4: status, context, decision, consequences. Whichever path is chosen, the workflow's behavior is unchanged — only the hosting differs (per W003 Phase 1 Part 2's explicit framing).

### W001 / W002 parked questions resolved or carried

| ID | Question | Disposition in M5 |
|---|---|---|
| W001-5 | Reserve check authority: Auctions vs Settlement | Already resolved in W002 + W003. Settlement is financially authoritative; Auctions' `ReserveMet` is UX-grade. Reaffirmed in M5 implementation. |
| W001-Q5 / PARKED P-001 (Operations runbook) | Ops-staff intervention on settlement | Out of scope per Phase 4 PARKED.md P-001 trigger. Carried forward. |
| (none new from W003 itself) | — | W003 has no parked questions remaining post-narrative-002 audit; F002 / F004 / F005 absorb into M5-S1 per the table above. |

### Retrospective skills work

`docs/skills/wolverine-sagas.md` (or `process-manager.md`, depending on the S1 decision) updated retrospectively with the M5 in-repo example. If `ProcessManager<TState>` is chosen, M5-S1 also pairs with a new skill file authoring session (or extends the existing sagas skill) to document the pattern.

`docs/skills/marten-projections.md` updated retrospectively with the `PendingSettlement` projection's seed-from-cross-BC-event pattern, since it is structurally distinct from the M3 / M4 same-BC projections.

---

## 3. Explicit Non-Goals

- **Real payment-processor integration.** MVP credit-ledger posture only. Bidder credits are debited and seller credits are incremented as Marten document updates; no Stripe / Braintree / banking integration. Real payment is post-MVP per W003 §"Winner Charge".
- **Compensation paths beyond MVP.** W003 Phase 1 Part 3 explicitly defers compensation design beyond MVP. M5 does not implement rollback / refund / charge-reversal flows.
- **Manual payment-failure recovery.** Post-MVP. Operations dashboard surfacing of `PaymentFailed` is post-M5.
- **The Settlement BC's own dashboard or admin UI.** M5 is backend-only; M6 frontend MVP introduces the bidder-side balance display and any seller-side payout-receipt view.
- **Real-time bidder credit-balance updates pushed via Relay during the auction.** Bidder credit is checked at place-bid time (DCB scope from M3) and updated at settlement-charge time. No mid-auction balance-change pushes.
- **Settlement of listings published before M5 ships.** Existing M3 / M4 test fixtures synthesise `ListingSold`; production listings published before M5 has shipped will not retroactively settle. M5 is forward-only.
- **Auctions BC changes.** M3 + M4 Auctions ship `ListingSold` and `BuyItNowPurchased`; Settlement consumes them as-is. M5 does not modify Auctions code.
- **The W003 broader storage-staleness sweep beyond F003's minimum-scope correction.** Narrative 002 PR #20 corrected the Phase 1 Part 1 + Ubiquitous Language Polecat references; the remaining Polecat references at L29, L649, L663 (per narrative 002 Finding 003) are deferred to a future workshop-cleanup session, not M5.

---

## 4. Solution Layout

### New projects added in M5

- `src/CritterBids.Settlement/` — Settlement BC implementation (workflow, projections, handlers, module wiring)
- `tests/CritterBids.Settlement.Tests/` — Settlement BC test project; xUnit + Shouldly + Testcontainers + Alba per the standard CritterBids test stack

### New files added in M5 (representative, not exhaustive)

Settlement BC core:

- `Settlement.cs` (or `SettlementWorkflow.cs` if Saga path; `SettlementProcessManager.cs` if ProcessManager path) — the workflow document
- `SettlementState.cs` — state record(s); the seven phases per W003 Phase 1 Part 2
- `SettlementInitiated.cs`, `ReserveCheckCompleted.cs`, `WinnerCharged.cs`, `FinalValueFeeCalculated.cs`, `SellerPayoutIssued.cs` — Settlement-internal domain events (workflow-internal; not in Contracts namespace)
- `InitiateSettlement.cs`, `CheckReserve.cs`, `ChargeWinner.cs`, `CalculateFee.cs`, `IssueSellerPayout.cs`, `CompleteSettlement.cs` — workflow command records
- `PendingSettlement.cs` — projection document
- `BidderCreditView.cs` — read model document (slug TBD per F005)
- `SettlementId.cs` — strongly-typed identifier (UUID v5 derived per W003 Phase 1 Part 6)
- `SettlementModule.cs` — DI wiring; `AddSettlementModule()` extension
- `SettlementsIdentityNamespaces.cs` — UUID v5 namespace constant analogous to `AuctionsIdentityNamespaces`
- Cross-BC consumers: `ListingPublishedHandler.cs`, `ListingSoldHandler.cs`, `BuyItNowPurchasedHandler.cs` (one or more handlers per consumer; structure decided in S2)

Contracts:

- `src/CritterBids.Contracts/Settlement/SettlementCompleted.cs`
- `src/CritterBids.Contracts/Settlement/PaymentFailed.cs`
- `src/CritterBids.Contracts/Settlement/SellerPayoutIssued.cs`

API host wiring:

- `src/CritterBids.Api/Program.cs` — `builder.Services.AddSettlementModule()`; RabbitMQ routing for the new queues; Marten event-type registration for Settlement-internal events

### Full solution layout at M5 close

```
src/
├── CritterBids.Api/
├── CritterBids.AppHost/
├── CritterBids.Contracts/
│   ├── Auctions/  (M3 / M4)
│   ├── Selling/   (M2 / M4-S2)
│   └── Settlement/  ← NEW IN M5
├── CritterBids.Auctions/  (M3 / M4)
├── CritterBids.Listings/  (M2 / M3-S6)
├── CritterBids.Participants/  (M1)
├── CritterBids.Selling/  (M2 / M4-S2)
└── CritterBids.Settlement/  ← NEW IN M5

tests/
├── CritterBids.Api.Tests/
├── CritterBids.Auctions.Tests/
├── CritterBids.Listings.Tests/
├── CritterBids.Participants.Tests/
├── CritterBids.Selling.Tests/
└── CritterBids.Settlement.Tests/  ← NEW IN M5
```

---

## 5. Infrastructure

### Marten configuration

Settlement uses Marten on PostgreSQL per ADR 011's All-Marten Pivot. The BC's `SettlementModule.ConfigureMarten` registers:

- The workflow document (`Settlement` or `SettlementProcessManager`) as a saga-store-managed type if Saga path; or as a standard Marten document with state-machine-as-records if ProcessManager path
- The `PendingSettlement` projection as a Marten document projection
- The `BidderCreditView` projection as a Marten document projection (slug TBD per F005)
- Settlement-internal event types: `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`, `SellerPayoutIssued`

Per `adding-bc-module.md` and the M3 / M4 precedents, Settlement does not call `AddMarten()` directly — `Program.cs` carries the single `AddMarten()` registration, and BCs contribute via `services.ConfigureMarten()` per their `AddXyzModule()` extension method.

### RabbitMQ routing

Three new outbound + inbound routes per the cross-BC wiring table in §2:

- Outbound: `Settlement → Listings` and (post-M5) `Settlement → Relay`, `Settlement → Operations`
- Inbound: Settlement consumes `ListingPublished` (Selling), `ListingSold` (Auctions), `BuyItNowPurchased` (Auctions)

The Wolverine outbox handles transactional delivery for all outbound events. Settlement's inbound handlers register via the standard `[WolverineHandler]` discovery pattern; the BC discovery isolation pattern (per memory `project_cross_bc_handler_isolation.md`) applies — Settlement's handler tests need a `*BcDiscoveryExclusion` for any shared event type with handlers in two BCs.

### Scheduled messages

Settlement does not schedule messages of its own in M5. The workflow runs synchronously through its phases on the inbound trigger (`ListingSold` or `BuyItNowPurchased`); no `bus.ScheduleAsync()` calls.

If retry-on-projection-lag is implemented for the `PendingSettlement` race condition (per W003 Phase 1 Part 1's Option A: Wolverine retry with backoff), it uses the standard Wolverine retry policies, not custom scheduling.

### No new stores

Settlement uses the same Marten-on-PostgreSQL store as the rest of the BCs. No new database, no new schema, no new container.

---

## 6. Conventions Pinned

### Settlement workflow hosting (S1 decision)

Decision recorded as ADR-019 (or analogous slug) per the §2 "Settlement workflow hosting decision in S1" framing. Either Wolverine Saga or `ProcessManager<TState>`; the workflow's behavior is identical in either case.

### UUID v5 deterministic SettlementId

Per W003 Phase 1 Part 6: `SettlementId = UuidV5(SettlementsIdentityNamespaces.SettlementSaga, $"settlement:{ListingId}")`. Idempotent by construction; a duplicate `ListingSold` consumption derives the same `SettlementId` and the workflow's state guard rejects re-initiation. The namespace constant lives in `SettlementsIdentityNamespaces.cs`. (The original W003 reference to "AuctionsNamespace" was corrected at M5-S4 as a workshop-update finding — the namespace is Settlement-owned per the BC-isolation discipline.)

This is CritterBids' first lived example of UUID v5 namespace-derived stream IDs (M3 Auctions uses UUID v7; M2 Selling uses UUID v7; M1 Participants uses UUID v7). Per `CLAUDE.md`'s ADR 007 reference: "UUID v5 with a BC-specific namespace constant remains available when a natural business key enables deterministic stream creation."

### `[WriteAggregate]` from first commit

If Saga path: every Settlement command handler that loads workflow state uses `[WriteAggregate]` with explicit `nameof(<Command>.<IdField>)` override. Per the M2.5-S1 / M2.5-S2 / M3 / M4 precedent.

If ProcessManager path: equivalent state-loading pattern per the framework's API; documented in the M5-S1 retro and the (extended or new) skill file.

### `OutgoingMessages` for integration events

Settlement's three integration events (`SettlementCompleted`, `PaymentFailed`, `SellerPayoutIssued`) publish via `OutgoingMessages` from the relevant handler — never `IMessageBus.PublishAsync()` directly per anti-pattern #11 in `wolverine-message-handlers.md`. The workflow returns the integration events alongside the domain events as part of its handler tuple.

### `FeePercentage` configuration

For M5, `FeePercentage` reads from the `PendingSettlement` projection (which was seeded from `ListingPublished`, which carries the constant `0.10m` placeholder per narrative 004 Finding 001 / `SubmitListing.cs:70`). Post-M5, when a fee-engine moves the constant to a configurable boundary, the placeholder rewires; the M5 implementation does not pre-empt that boundary work.

### `[AllowAnonymous]` posture

Per `CLAUDE.md`'s M1-through-M6 posture: all Settlement-side endpoints (none planned in M5; Settlement is backend-only) carry `[AllowAnonymous]` if any are added. Real authentication lifts at M6.

---

## 7. Slice Breakdown

M5 ships in six slices, mirroring the M3 (S1-S6) structure:

| Slice | Title | Scope |
|---|---|---|
| M5-S1 | Settlement Foundation Decisions + Contract Stubs + W003 Amendments | Workflow hosting decision (ADR-019); contract stub authoring in `src/CritterBids.Contracts/Settlement/`; W003 amendments per F002 / F004 / F005 |
| M5-S2 | Settlement BC Scaffold | `CritterBids.Settlement` project; `AddSettlementModule()`; Marten config; module wiring in `Program.cs`; RabbitMQ routing setup |
| M5-S3 | `PendingSettlement` Projection + `ListingPublished` Consumer | Slice 6.1's seed mechanism; the projection + consumer + lifecycle states (Pending / Consumed / Expired) |
| M5-S4 | Settlement Workflow Happy Path (Bidding Source) | Slice 6.1; the seven-phase workflow consuming `ListingSold`; all decider scenarios from `003-scenarios.md` §1-§6 happy-path branches green; integration test exercising the full workflow end-to-end |
| M5-S5 | Settlement Workflow Failure Paths + BIN Source | Slice 6.2 (BIN source); failure-branch scenarios from §3.2 (PaymentFailed); state-guard scenarios from §1.3 / §3.3 / §3.4 / §4.3 / §5.2 / §6.2 |
| M5-S6 | Seller Payout Notification (Relay Stub) | Slice 6.3; `SellerPayoutIssued` integration-event publishing; Relay-side stub or test fixture if Relay BC has not yet shipped |

The cutover gate's visible signal is M5-S1's prompt carrying the `**Narrative:**` line citing narrative 002. Subsequent slice prompts inherit the same discipline.

---

## 8. Open Questions Surfacing in M5

| ID | Question | Surfacing slice | Disposition |
|---|---|---|---|
| M5-1 | Wolverine Saga vs `ProcessManager<TState>` workflow hosting | S1 | Decided in S1 via ADR-019 |
| M5-2 | `BidderCreditView` projection name and shape | S1 (per F005) | Decided in S1 as part of W003 amendment |
| M5-3 | Retry-on-projection-lag mechanism for `PendingSettlement` race condition | S3 | Defaults to Wolverine retry policies per W003 Phase 1 Part 1 Option A; S3 confirms |
| M5-4 | BIN-source short-circuit through reserve-check phase | S5 | Per W003 Phase 1 Part 5: the evolver branches BIN-source state directly to `ReserveChecked(WasMet: true)`; S5 implements |

---

## 9. Cutover Gate

This milestone doc is jointly authoritative with narrative 002 (`docs/narratives/002-winner-clears-settlement.md`) for any slice prompt that implements a Settlement-BC slice, per AUTHORING.md rule 3's joint-authority clause (added in PR #18 as Phase 5 Items 2+3 amendments).

The cutover-gate signal lands at M5-S1's prompt: its metadata block carries the `**Narrative:**` line citing narrative 002. Subsequent slice prompts (M5-S2 through M5-S6) inherit the discipline; their `**Narrative:**` lines either continue citing narrative 002 (for slices that implement journey steps narrative 002 dramatises) or cite a different narrative (for any future seller-side or operator-side narratives that come online before M5 closes).

---

## Document History

- **v0.1** (2026-04-29): Authored as foundation-refresh Phase 5 Item 4 deliverable (the cutover gate). Adapts the M3-auctions-bc.md / M4-auctions-bc-completion.md milestone-doc structure for Settlement BC. Six-slice breakdown mirrors M3's S1-S6 shape. Foundation decisions in S1: workflow hosting (Saga vs ProcessManager → ADR-019); W003 amendments per narrative 002 findings F002 / F004 / F005 (folded into S1 instead of a separate workshop-cleanup PR); contract-stub authoring. Cross-BC wiring established for three new RabbitMQ routes (Settlement-side inbound from Selling / Auctions; outbound to Listings; Relay / Operations post-M5). MVP credit-ledger posture explicit; real payment-processor integration is post-MVP. Cutover-gate framing in §9 captures the joint-authority discipline for all M5 slice prompts.
- **v0.2** (2026-05-17): M5 milestone closed at M5-S6. Status flipped Planning → Shipped. Six slices delivered (S1 foundation, S2 scaffold, S3 PendingSettlement projection, S4 saga happy path, S5 failure paths + BIN source + BidderCreditView, S6 outbound publish routes + Listings catalog `Settled` status + ADR-014). 115 tests passing at M5 close (1 Api + 36 Auctions + 1 Contracts + 14 Listings + 6 Participants + 32 Selling + 25 Settlement). ADR-014 (cross-BC read-model extension shape) authored at M5-S6 — Path A with the M3-S6 + M5-S6 single-source sibling precedent; multi-source sub-question deferred. ADR-007 Gate 4 closed by lived-fact at M5 close — Settlement shipped three event streams on Marten engine-default row IDs without surfaced incident; engine default is the permanent posture. PaymentFailed publish route wired at M5-S6 per the M5-S5 retro's queue-topology-completeness recommendation (flips the v0.1 prompt's deferred stance). See `docs/retrospectives/M5-retrospective.md` for the milestone-level retro.
