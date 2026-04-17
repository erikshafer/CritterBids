# M3-S1: Auctions Foundation Decisions + Contract Stubs

**Milestone:** M3 — Auctions BC
**Session:** S1 of 7
**Prompt file:** `docs/prompts/M3-S1-auctions-foundation-decisions.md`
**Baseline:** 44 tests passing · `dotnet build` 0 errors, 0 warnings · M2.5 complete

---

## Goal

Close the three open decisions that block M3 implementation and lock the Auctions integration
vocabulary, before any Auctions code is written. S1 is docs-only except for nine empty `sealed
record` contract stubs in `src/CritterBids.Contracts/Auctions/` — the contract shapes themselves
are the lockable outputs of the vocabulary decisions, so they ship with S1 rather than being
handed to S2 to invent.

M3 lands the first DCB and the first saga in CritterBids. Both are pattern-stable via skills
extracted from CritterSupply, but their CritterBids-specific configuration has never been
exercised. Starting S2 with ambiguous event row ID strategy, undecided `BidRejected` stream
placement, or an un-locked `BiddingOpened` payload would force those decisions to surface mid-
implementation — the failure mode the M2 retrospective's "three rapid ADR pivots" warning
explicitly called out.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M3-auctions-bc.md` | Milestone scope — S1 deliverables are §2, §6, §8 items |
| `docs/decisions/007-uuid-strategy.md` | Gate 4 lives here — decide the event row ID strategy or defer with dated rationale |
| `docs/workshops/002-auctions-bc-deep-dive.md` | Workshop resolutions including W002-7 and W002-9 |
| `docs/workshops/002-scenarios.md` | Contract shape source — events referenced in §1, §2, §3 scenarios are the nine stubs |
| `docs/workshops/PARKED-QUESTIONS.md` | Open-questions ledger — W002-7 and W002-9 get moved out in this session |
| `docs/skills/dynamic-consistency-boundary.md` | Destination for the `BidRejected` stream-placement decision |
| `docs/skills/adding-bc-module.md` | Stale Marten/Polecat flavor table — correct post-ADR-011 |
| `src/CritterBids.Contracts/Selling/ListingPublished.cs` | Reference shape for contract stubs — naming, namespace, triple-slash docstring consumer list |

---

## In scope

- **ADR 007 Gate 4 — event row ID strategy.** Decide: UUID v7 for Auctions event rows (if Marten 8
  exposes the generation seam per Gate 1 and the Auctions write profile justifies it), or formally
  defer with a dated trigger and named blocker. Record the outcome as an amendment to ADR 007 (a new
  "Event Row ID Decision" section mirroring the existing "Stream ID Decision — Accepted" section
  shape) — not a new ADR. Update the ADR 007 status header and `docs/decisions/README.md` row to
  reflect the new state.

- **W002-7 — `BidRejected` stream placement.** Decide between dedicated Marten stream type per listing
  (tagged with `ListingId`) versus general audit stream across all rejections. Record the decision
  with rationale in `docs/skills/dynamic-consistency-boundary.md` under a new or existing subsection
  that S4 will load before authoring the PlaceBid handler. Move the W002-7 entry from "Open" to
  "Resolved" in `docs/workshops/PARKED-QUESTIONS.md` with "Resolved In" set to this session.

- **W002-9 — `BiddingOpened` payload.** Decide: carry full extended-bidding config on the contract
  (current workshop design), or trim and have the saga load from stream. Record the decision and
  rationale in-line with the `BiddingOpened` contract stub's triple-slash docstring (so the payload
  choice is discoverable at the type it governs), and move the W002-9 entry from "Open" to
  "Resolved" in PARKED-QUESTIONS.md.

- **Author nine `sealed record` contract stubs** in `src/CritterBids.Contracts/Auctions/`, one file
  per event. Each file: namespace `CritterBids.Contracts.Auctions`, `sealed record` with fields
  required for every future consumer (per `integration-messaging.md` L2 — Listings in M3, Settlement
  in M5, Relay and Operations later), triple-slash summary naming the publisher, transport queue,
  and consumer list. Contract shapes are final at this session's close — S3 through S6 consume them
  as-is.

  | File | Event |
  |---|---|
  | `BiddingOpened.cs` | Listing opens for bids; carries payload per W002-9 resolution |
  | `BidPlaced.cs` | Bid accepted; carries listing, bidder, amount, count, `IsProxy: bool` (always `false` in M3) |
  | `BuyItNowOptionRemoved.cs` | BIN no longer available (first bid placed) |
  | `ReserveMet.cs` | Real-time UX signal only; Settlement authoritative check comes separately (W001-5) |
  | `ExtendedBiddingTriggered.cs` | Previous and new close time; consumed by Closing saga and Relay |
  | `BuyItNowPurchased.cs` | BIN terminal event |
  | `BiddingClosed.cs` | Mechanical close signal, separate from sold/passed outcomes |
  | `ListingSold.cs` | Outcome with winner and hammer price |
  | `ListingPassed.cs` | Outcome with reason (`NoBids` | `ReserveNotMet`) |

- **Fix `docs/skills/adding-bc-module.md` two-flavor table.** The Marten/Polecat flavor split is
  stale post-ADR-011. Every BC is Marten now. Update the overview table and any downstream prose
  that references Polecat as an active flavor. Leave archived-pattern references (to
  `polecat-event-sourcing.md`) intact — they are historical.

- **Session retrospective** at `docs/retrospectives/M3-S1-auctions-foundation-decisions-retrospective.md`.

---

## Explicitly out of scope

- **Any Auctions BC implementation.** No `CritterBids.Auctions` project, no `Listing` aggregate, no
  handlers, no module extension method. S2 owns all of that.
- **Wolverine routing rules.** `Program.cs` is not touched. The `auctions-selling-events` and
  `listings-auctions-events` queues are named in the milestone doc but wired in S3 and S6.
- **New ADR for Gate 4.** Amend ADR 007; do not create ADR 012 for this. The event row ID question
  has always lived inside ADR 007 alongside the stream ID question.
- **`BidRejected` event stub.** Not in the nine contracts. `BidRejected` is an internal Auctions
  stream event, not a cross-BC integration event — it never goes on RabbitMQ. Confirmed by W002-7
  resolution's placement location (skill file, not contracts namespace).
- **W002-8** (two-proxy bidding war test). Moves with the proxy saga to M4; stays "Open" in
  PARKED-QUESTIONS.md with target updated to M4 if the existing entry points at M3.
- **Contract field finalization for M4-or-later events.** `SessionCreated`,
  `ListingAttachedToSession`, `SessionStarted`, `RegisterProxyBid`, `ProxyBidRegistered`,
  `ProxyBidExhausted` — all M4 scope. Do not create stubs for them.
- **Skill file rewrites beyond the two targeted edits** (`dynamic-consistency-boundary.md` for
  W002-7, `adding-bc-module.md` for the ADR-011 fix). Retrospective skill updates from S4 and S5
  land later — not here.
- **`docs/vision/bounded-contexts.md`, `docs/milestones/MVP.md`, `CLAUDE.md` edits.** Those were
  swept in M2.5-S1 and are current.

---

## Conventions to pin or follow

- **Contract namespace and folder:** `CritterBids.Contracts.Auctions` in
  `src/CritterBids.Contracts/Auctions/`, one `sealed record` per file. Pattern mirrors
  `CritterBids.Contracts.Selling.ListingPublished` exactly — see `integration-messaging.md` L2
  for the "full payload for all future consumers at first commit" discipline.
- **Contract field completeness:** each contract carries every field any future consumer needs, not
  just M3 consumers. Listings is the only M3 consumer; Settlement (M5), Relay, and Operations all
  subscribe later and must be represented in the payload now. The `ListingPublished` contract's
  docstring consumer-list format is the reference.
- **Gate 4 amendment shape:** the new "Event Row ID Decision" section in ADR 007 follows the same
  structure as the existing "Stream ID Decision — Accepted" section — status line, rationale
  paragraph, and a small table if multiple BCs have different answers. If the outcome is deferral,
  the section states the named blocker and the dated re-evaluation trigger.
- **Decisions land where readers look.** Gate 4 → ADR 007 (architectural). W002-7 →
  `dynamic-consistency-boundary.md` (implementation pattern). W002-9 → `BiddingOpened.cs` docstring
  (the type it governs). Do not duplicate the decisions across locations; cross-reference if needed.
- **PARKED-QUESTIONS.md convention:** move resolved rows from "Open" to "Resolved" with "Resolved
  In" column set to this session's identifier. Do not delete the rows.

---

## Acceptance criteria

- [ ] `docs/decisions/007-uuid-strategy.md` — new "Event Row ID Decision" section present;
  status header updated to reflect Gate 4 state (closed with decision, or formally deferred);
  Gate 4 line in the acceptance gates list annotated with its current state.
- [ ] `docs/decisions/README.md` — ADR 007 row Summary column updated to reflect the new Gate 4
  state; Status column updated if the overall ADR status changed.
- [ ] `docs/skills/dynamic-consistency-boundary.md` — `BidRejected` stream placement decision
  recorded with rationale; cross-referenced from W002 workshop if needed.
- [ ] `docs/workshops/PARKED-QUESTIONS.md` — W002-7 and W002-9 moved from "Open" to "Resolved" with
  "Resolved In: M3-S1"; W002-8 entry target updated to M4 if currently M3.
- [ ] `src/CritterBids.Contracts/Auctions/` — directory exists and contains exactly nine `.cs`
  files, one per event listed in §In scope.
- [ ] Each contract file: namespace `CritterBids.Contracts.Auctions`, `sealed record` with final
  field list, triple-slash summary with publisher, transport queue (`listings-auctions-events` for
  the five Listings-consumed events, named-queue TBD for the others since they're consumed in
  later milestones), and full consumer list.
- [ ] `BiddingOpened.cs` docstring explicitly records the W002-9 payload decision.
- [ ] `BidPlaced.cs` carries `IsProxy: bool` field with a docstring note that M3 always sets it
  to `false`; M4 wires the proxy path with zero contract change.
- [ ] `docs/skills/adding-bc-module.md` — two-flavor Marten/Polecat overview table replaced with
  the current all-Marten state; historical references to Polecat remain untouched.
- [ ] `dotnet build` — 0 errors, 0 warnings. (Adding `sealed record` contract stubs should not
  change test count.)
- [ ] `dotnet test` — 44 passing (baseline unchanged).
- [ ] `docs/retrospectives/M3-S1-auctions-foundation-decisions-retrospective.md` — written;
  records each decision's final state (Gate 4, W002-7, W002-9), the nine contract file paths,
  any scope deviation, and a one-paragraph "what M3-S2 should know" note.

---

## Open questions

- **Gate 4 requires JasperFx team input per ADR 007.** If that input is not in hand at session
  time, the correct output is a formal deferral with a named blocker (specifically: "JasperFx
  guidance on Auctions-scale event row ID strategy not yet received as of [date]; re-evaluate
  at [trigger]") — not an unilateral decision. Deferral is acceptable; guessing is not.
- **`BiddingOpened` payload completeness.** If the workshop design (carry full extended-bidding
  config) turns out to conflict with something discovered during this session's reading — for
  example, if the saga genuinely needs to load config from stream for replay semantics — stop and
  flag. This is a milestone-level design question, not a session decision.

---

## Commit sequence

Four commits, in this order:

1. `docs(adr-007): amend with event row ID decision — [accepted v7 | deferred: <blocker>]`
2. `docs(skills): record BidRejected stream placement (W002-7); fix stale Marten/Polecat table in adding-bc-module (ADR-011)`
3. `feat(contracts): add Auctions integration event stubs — nine sealed records for M3+`
4. `docs: move W002-7 and W002-9 to resolved; write M3-S1 retrospective`

The contracts commit uses `feat` rather than `docs` because it adds real `.cs` files to the
solution, even though the bodies are stubs. The milestone scope frames them as vocabulary-lock
artifacts, not implementation — but they compile, they ship in the `CritterBids.Contracts`
assembly, and they're referenced by every subsequent M3 session. Treating the commit as a feature
addition is the honest framing.
