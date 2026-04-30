# M5-S1: Settlement BC Foundation Decisions + Contract Stubs + W003 Amendments

**Milestone:** M5 ([Settlement BC](../../milestones/M5-settlement-bc.md))
**Slice:** S1 (Foundation Decisions + Contract Stubs + W003 Amendments)
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
**Agent:** @PSA
**Estimated scope:** one PR; ~5 files added (3 contract stubs + ADR-019 + the M5-S1 retro), ~2 files modified (W003 + W003-scenarios)

---

## Goal

Close the workflow-hosting decision that blocks M5 implementation, fold the three deferred W003 amendments from narrative 002's findings file (F002, F004, F005) into the workshop, lock the Settlement integration vocabulary by authoring three `sealed record` contract stubs, and update the affected skill file. M5-S1 is docs-and-stubs only — no Settlement-BC code, no project scaffolding, no event handlers; that work begins in M5-S2.

This slice is structurally equivalent to M3-S1 (Auctions Foundation Decisions) and M4-S1 (Auctions Completion Foundation Decisions). Starting M5-S2 with an undecided workflow hosting (Wolverine Saga vs `ProcessManager<TState>`), unresolved W003 inconsistencies, or un-locked Settlement contract shapes would force those decisions to surface mid-implementation — the failure mode the M2 retro's "three rapid ADR pivots" warning explicitly called out.

This slice is also the **cutover gate** for the foundation refresh per Phase 5 §3.6 / handoff §15.5. The `**Narrative:**` metadata line above is the visible signal: M5-S1 is the first slice prompt under the new NDD-informed workflow that cites a narrative as jointly authoritative scope alongside its milestone doc per AUTHORING.md rule 3 (added in PR #18 as Phase 5 Items 2+3 amendments).

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M5-settlement-bc.md` | Milestone scope — S1 deliverables are §2's W003-amendments table + §6's hosting decision + §7's contract-stub list |
| `docs/narratives/002-winner-clears-settlement.md` | The journey narrative 002 dramatises (jointly authoritative scope per AUTHORING.md rule 3); its Cast and Setting establish the financial ground; Moments 1-5 dramatise the workflow phases this slice's contract stubs will eventually back |
| `docs/narratives/002-findings.md` | F002 / F004 / F005 routing details — the three findings folded into this slice as W003 amendments |
| `docs/workshops/003-settlement-bc-deep-dive.md` | The workshop being amended this slice; Phase 1 Parts 1, 2, 5, 6 are the principal references for the hosting decision and the SettlementId convention |
| `docs/workshops/003-scenarios.md` | Scenarios §1-§7 inform the contract shapes; F004's payload reconciliation lives between §1.1 (decider output) and §7.1 (evolver input) |
| `docs/decisions/README.md` | Status Ledger — confirm next-unreserved ADR number for the hosting-choice ADR (likely 019; verify before authoring) |
| `docs/decisions/007-uuid-strategy.md` | Gate 4's disposition (decided in M3-S1) — Settlement event row IDs follow the same disposition; no new decision in M5-S1 |
| `docs/decisions/011-all-marten-pivot.md` | Storage decision; Settlement is Marten-on-PostgreSQL; relevant for the W003 amendments since narrative 002 PR #20 already corrected the Phase 1 Part 1 framing per F003 (this slice does not re-do that work) |
| `docs/skills/wolverine-sagas.md` | Destination if Saga path is chosen; the saga-pattern skill file gets a Settlement-side amendment after S5 ships (not S1's responsibility but the file is in scope for the decision rationale) |
| `src/CritterBids.Contracts/Auctions/ListingSold.cs` | Reference shape for contract stubs — namespace, sealed record, triple-slash docstring listing publisher / transport / consumers |
| `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs` | Reference shape for an integration event whose consumer side is intentionally narrow (Settlement publishes only the integration-out set per W003) |

---

## In scope

- **ADR-019 (or analogous slug per the next-unreserved pointer): Settlement Workflow Hosting.** Decide between Wolverine Saga and `ProcessManager<TState>` per W003 Phase 1 Part 2. The ADR's status / context / decision / consequences sections mirror ADR 016 / 017 / 018 from foundation-refresh Phases 1 and 4. Whichever path is chosen, the workflow's behavior is identical — only the hosting differs. Record the rationale, the trade-offs, and the conditions under which the decision would be revisited. Update `docs/decisions/README.md`'s Status Ledger; advance the next-unreserved pointer from 019 to 020.

- **W003 F002 amendment (`document-as-intentional`).** Add a "Field Name Convention" note to `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 (or as a sidebar in §7's evolver narrative — author's call) explaining the `Price` ↔ `HammerPrice` rename across initiation. Coverage required: source-agnostic rationale at command and `SettlementInitiated` time (Bidding source = hammer price; BIN source = BIN price); post-initiation specificity (state and downstream events use `HammerPrice` because the value is the hammer price by definition once `Source` is committed); the evolver step at §7.1 where the rename happens. Per narrative 002 Finding 002.

- **W003 F004 amendment (`workshop-update`).** Normalize `docs/workshops/003-scenarios.md` §1.1 / §1.2 / §1.3 `SettlementInitiated` event payloads to show the full eight-field shape per §7.1's evolver-input form (`SettlementId, ListingId, WinnerId, SellerId, Price, Source, ReservePrice, FeePercentage, InitiatedAt` — though `InitiatedAt` is already implicit per §1.1's `<now>` placeholder). Audit §3.1 / §4.1 / §5.1 / §6.1 `SettlementId` rendering for consistency: §3.1 (`WinnerCharged`) and §5.1 (`SellerPayoutIssued`) include `SettlementId`; §4.1 (`FinalValueFeeCalculated`) currently omits it; §6.1 (`SettlementCompleted`) includes it. Pick a consistent rendering and apply across the §3 / §4 / §5 / §6 sections. Per narrative 002 Finding 004.

- **W003 F005 amendment (`workshop-update`).** Define the bidder-credit projection in `docs/workshops/003-settlement-bc-deep-dive.md` Phase 1 (a new Part — likely Part 7 — or an extension of Part 1's projection coverage). Define: name (`BidderCreditView` is the working slug; confirm at session start), shape (`(BidderId, RemainingCredit, LastChargedSettlementId, UpdatedAt)` is the working shape; confirm), lifecycle (initialized on `ParticipantSessionStarted` consumption with the assigned credit ceiling; updated on `WinnerCharged`), and consumer model (read by Relay's broadcast handler when composing the post-charge `SettlementCompleted` push; read directly by any future bidder-facing balance endpoint). Update narrative 002 Moment 3's reference if the chosen name diverges from the placeholder "bidder-credit projection / ledger" the narrative used. Per narrative 002 Finding 005.

- **Author three Settlement integration `sealed record` contract stubs** in `src/CritterBids.Contracts/Settlement/`. Namespace `CritterBids.Contracts.Settlement`. Each carries a triple-slash summary naming the publisher (Settlement BC), the transport (RabbitMQ via Wolverine outbox), the inbound queue routes (`listings-settlement-events` for `SettlementCompleted`; post-M5 routes for `PaymentFailed` and `SellerPayoutIssued`), and the consumer list. Contract shapes are final at this slice's close — S2 through S6 consume them as-is.

  | File | Event | Payload |
  |---|---|---|
  | `SettlementCompleted.cs` | Terminal happy-path event | `SettlementId, ListingId, WinnerId, SellerId, HammerPrice, FeeAmount, SellerPayout, CompletedAt` per `003-scenarios.md` §6.1 |
  | `PaymentFailed.cs` | Terminal failure event | `SettlementId, ListingId, WinnerId, Reason, FailedAt` per `003-scenarios.md` §3.2 |
  | `SellerPayoutIssued.cs` | Payout event for Relay broadcast | `SettlementId, SellerId, PayoutAmount, FeeDeducted, IssuedAt` per `003-scenarios.md` §5.1 |

- **Update `docs/skills/marten-projections.md`** with a brief note about the cross-BC-event-seeded projection pattern that `PendingSettlement` will exercise in M5-S3 (this slice flags the skill file as in-scope for a retrospective amendment after S3 ships; M5-S1 itself does not need to author the full pattern documentation, just queue the skill-file work as an explicit deliverable for S3's retro).

- **Session retrospective** at `docs/retrospectives/M5-S1-settlement-bc-foundation-decisions-retrospective.md`.

---

## Explicitly out of scope

- **Any Settlement-BC implementation code.** No `CritterBids.Settlement` project, no `Settlement` workflow document, no handlers, no projection implementations. M5-S2 lands the BC scaffold.
- **The Settlement-internal event types** (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`). These stay in `src/CritterBids.Settlement/` once that project exists; M5-S1 only authors the integration-out set in `Contracts/Settlement/`.
- **Cross-BC handler wiring.** `ListingPublishedHandler`, `ListingSoldHandler`, `BuyItNowPurchasedHandler` are M5-S3 / M5-S4 territory.
- **`AddSettlementModule()` extension method.** M5-S2 territory.
- **`Program.cs` Marten / RabbitMQ wiring for Settlement.** M5-S2 territory.
- **Real payment-processor integration design.** Out per M5 §3 non-goals.
- **Compensation-path design.** Out per M5 §3; W003 Phase 1 Part 3 explicitly defers.
- **Re-doing F003's W003 Polecat / SQL Server staleness correction.** Already landed in narrative 002 PR #20. The remaining Polecat references at L29 / L649 / L663 (per narrative 002 Finding 003's broader-sweep deferral) stay deferred to a future workshop-cleanup session, not M5-S1.
- **The skill file's full M5 amendments.** `wolverine-sagas.md` (or `process-manager.md` if ProcessManager path) gets its retrospective amendment after S5 ships, not in S1. `marten-projections.md` flags a pending S3 retro amendment but does not write the full pattern in S1.
- **M6 frontend MVP design.** Out per CLAUDE.md M1-through-M6 posture.

---

## Conventions to pin or follow

- **AUTHORING.md rule 3 (joint-authority).** This prompt's `**Narrative:**` line in the metadata block is the cutover-gate signal. The slice's authoritative scope is the M5 milestone doc *and* narrative 002 jointly. Any disagreement between the two surfaces as either an M5 milestone-doc edit (if the milestone is wrong) or a narrative 002 Phase 5 §7 cite-and-edit single-paragraph fix (if the narrative is wrong); structural rewrite of either is out.
- **ADR shape** per ADR 016 / 017 / 018 from foundation-refresh: status, context, decision, consequences. Status starts at `accepted` (or `proposed` if the decision needs follow-up validation; lean `accepted` for M5-S1's hosting choice).
- **Contract stub shape** per `src/CritterBids.Contracts/Auctions/ListingSold.cs` and `src/CritterBids.Contracts/Selling/ListingWithdrawn.cs`. Namespace `CritterBids.Contracts.Settlement`; `sealed record`; triple-slash docstring with publisher / transport / consumers. Full payload at first commit per `integration-messaging.md` L2.
- **W003 amendment scope.** Each finding's amendment lands in one cohesive edit per the F002 / F004 / F005 table above. Em-dash hygiene drop applies (per the memory clarification at narrative 002 close); no audit step on internal docs.

---

## Acceptance criteria

- [ ] ADR-019 (or analogous slug) authored at `docs/decisions/<NNN>-settlement-workflow-hosting.md` with status, context, decision, consequences sections per ADR 016 / 017 / 018 shape.
- [ ] `docs/decisions/README.md` Status Ledger lists ADR-019 (or analogous); next-unreserved pointer advanced.
- [ ] W003 carries the F002 "Field Name Convention" note explaining the `Price` ↔ `HammerPrice` rename across initiation.
- [ ] W003's `003-scenarios.md` §1.1 / §1.2 / §1.3 show the full `SettlementInitiated` payload per §7.1 (eight fields including `ReservePrice` and `FeePercentage`).
- [ ] W003's `003-scenarios.md` §3.1 / §4.1 / §5.1 / §6.1 SettlementId rendering is consistent (one chosen rendering applied across all four sections).
- [ ] W003 carries the F005 bidder-credit projection definition (name, shape, lifecycle, consumer model).
- [ ] If the F005 projection name diverges from narrative 002 Moment 3's "bidder-credit projection / ledger" placeholder, narrative 002 Moment 3 carries a single-paragraph cite-and-edit per Phase 5 §7.
- [ ] Three contract stubs at `src/CritterBids.Contracts/Settlement/` (`SettlementCompleted.cs`, `PaymentFailed.cs`, `SellerPayoutIssued.cs`); namespace correct; `sealed record`; triple-slash docstring per the reference shapes.
- [ ] `docs/skills/marten-projections.md` carries a "Pending: M5-S3" note flagging the cross-BC-event-seeded projection pattern as an in-scope retrospective amendment after S3 ships.
- [ ] `dotnet build` clean (0 warnings, 0 errors) on the new contract stubs.
- [ ] `docs/retrospectives/M5-S1-settlement-bc-foundation-decisions-retrospective.md` exists; mirrors the M3-S1 / M4-S1 retro shape.
- [ ] No `src/` files outside `src/CritterBids.Contracts/Settlement/` edited in this slice.
- [ ] No `tests/` files edited (M5-S1 is docs-and-stubs only; tests come with implementation in M5-S2+).

---

## Open questions

- **F005 projection name.** Working slug is `BidderCreditView`. Alternative: `BidderCreditLedger` (more financial-domain accurate; matches the "credit-ledger posture" framing from narrative 002 Setting). Decide at session start; the chosen name anchors as canonical for M5-S3's projection implementation and any future references.
- **Saga vs `ProcessManager<TState>` decision.** The W003 Phase 1 Part 2 framing presents both paths neutrally. Erik (JasperFx core team) is actively designing the `ProcessManager<TState>` framework; choosing it in M5 makes CritterBids the first lived example. Choosing Saga keeps CritterBids on its established Wolverine-saga pattern (Auction Closing saga from M3-S5 is the precedent). Trade-off: contributing-back-to-JasperFx vs staying on the well-trodden in-repo pattern. Decide at session start with rationale recorded in ADR-019.
- **F002 placement in W003.** Phase 1 prose vs §7 evolver sidebar. Lean: Phase 1 Part 2 (workflow framing) since the rename's rationale is most legible alongside the Saga-vs-ProcessManager hosting comparison. Confirm at session start.
- **F004 SettlementId rendering choice.** §3.1 / §5.1 / §6.1 include SettlementId; §4.1 omits. Lean: include SettlementId on all four for consistency. Confirm at session start. Apply uniformly.
