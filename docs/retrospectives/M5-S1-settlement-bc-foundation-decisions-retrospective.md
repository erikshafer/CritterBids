# M5-S1: Settlement BC Foundation Decisions + Contract Stubs + W003 Amendments — Retrospective

**Date:** 2026-05-03
**Milestone:** M5 — Settlement BC
**Slice:** S1 of 6 (Foundation Decisions + Contract Stubs + W003 Amendments)
**Agent:** @PSA (Claude, explanatory output style)
**Prompt:** `docs/prompts/implementations/M5-S1-settlement-bc-foundation-decisions.md`
**Narrative (joint authority):** `docs/narratives/002-winner-clears-settlement.md`

---

## Baseline

- `dotnet build` — 0 errors, 0 warnings (slnx-managed solution; the file is `CritterBids.slnx` not `.sln`)
- Foundation refresh closed at the prior PR (#24, 2026-04-29) with the cutover gate armed: M5-S1 prompt authored, narrative 002 cited as joint scope, Phase 5 retro merged
- ADR Status Ledger pointer at `019-<slug>.md` (next unreserved); 016, 017, 018 accepted at Phases 1/4 close
- `src/CritterBids.Contracts/` carries `Auctions/`, `Selling/`, and a top-level `SellerRegistrationCompleted.cs`; no `Settlement/` directory yet
- Narrative 002 finding ledger: F001 + F003 resolved in PR #20; F002, F004, F005 deferred to W003 follow-up — folded into M5-S1 per Phase 5 retro Item 7
- W003 deep dive carries six Phase 1 Parts plus seven design decisions in the summary; no Bidder Credit projection named, no Field Name Convention documented, `SettlementInitiated` payload mismatched between scenarios §1.1 (7 fields) and §7.1 (8 fields)
- This is the first slice prompt to run under the NDD-informed regime per AUTHORING.md rule 3's joint-authority clause; the cutover-gate's visible signal lives in this prompt's metadata

---

## Items completed

| Item | Description |
|------|-------------|
| S1a | ADR-019 Settlement Workflow Hosting authored (`docs/decisions/019-settlement-workflow-hosting.md`); Status Ledger row added; next-unreserved pointer advanced 019 → 020 |
| S1b | W003 F002 amendment — "Field Name Convention: `Price` at Initiation, `HammerPrice` Post-Initiation" subsection added to Phase 1 Part 2 (`003-settlement-bc-deep-dive.md`) |
| S1c | W003 F004 amendment — `SettlementInitiated` payload normalized across §1.1 / §1.2 to 9 fields per §7.1; SettlementId added to §4.1 / §4.2 `FinalValueFeeCalculated` payload; canonical-payload-shapes preamble added (`003-scenarios.md`) |
| S1d | W003 F005 amendment — Part 7 "The BidderCreditView Projection" added (Phase 1); Ubiquitous Language table extended; Phase 1 Summary table row 8 added; "New design elements identified" extended; narrative 002 Moment 3 deferred-list parenthetical updated per Phase 5 §7 cite-and-edit |
| S1e | Three Settlement integration contract stubs authored in `src/CritterBids.Contracts/Settlement/`: `SettlementCompleted.cs`, `PaymentFailed.cs`, `SellerPayoutIssued.cs` |
| S1f | `docs/skills/marten-projections.md` carries a "Pending: M5-S3 amendment" note flagging the cross-BC-event-seeded projection pattern as in-scope for retrospective amendment after S3 ships |
| S1g | Hosting-decision pointer added to the W003 Phase 1 Part 2 "design around decider semantics" decision blockquote, citing ADR-019 as the M5-S1 closure |
| S1h | This retrospective |

---

## S1a — ADR-019 Settlement Workflow Hosting

### Decision

**Wolverine Saga adopted for M5.** `ProcessManager<TState>` deferred until the Wolverine framework primitive's API stabilizes. The W003 §Part 2 design discipline of "decider semantics regardless of host" is preserved at the workshop and scenarios level — events, transitions, state shapes, and the 41 scenarios in `003-scenarios.md` apply unchanged across either host. The lived precedent is M3-S5's Auction Closing saga; the framework migration path when triggers fire is a single-slice rewrite, bounded by the 28 pure-function scenarios in Sections 1-7 acting as the migration's contract.

### Why deferral over adoption

Three triggers were considered for adopting `ProcessManager<TState>` immediately:

1. **Framework readiness** — the API is in active design, not stable. Adopting an in-design primitive in a milestone slice introduces risk bounded only by the framework's own development cadence. CritterBids' demo-vehicle and reference-architecture goals require shipping M5 on schedule; the framework-readiness gate fired in favor of the established Saga primitive.
2. **CritterBids-as-first-lived-example** — a real evangelism argument exists, but Erik holds the framework-roadmap context that would make this the right call. Without that input, the safer default is the well-trodden pattern with explicit migration triggers documented.
3. **Type-safety gains** — the discriminated-union state type sketched in W003 collapses into a `SettlementStatus` enum plus nullable fields on the Saga document. The type-safety gains the decider pattern provides become disciplined nullable handling carried by code review and handler-entry assertions. This is a real cost; ADR-019 names it honestly rather than papering over it.

### Three explicit revisit triggers

ADR-019 records three independent triggers, any one of which reopens the choice:

1. `ProcessManager<TState>` framework API stabilizes (1.0-grade surface in Wolverine release)
2. Saga shape produces specific friction during M5 implementation that the decider pattern would have prevented (cumulative pattern across S2-S6 retros)
3. JasperFx project direction explicitly requires CritterBids as the first lived example

The migration scope is named: a single-slice prompt rewriting the host wrapper while preserving events, scenarios, and the W003 design verbatim. No contract changes; no scenario rewrites; no W003 restructure.

### Structural metrics

| Metric | Before | After |
|--------|--------|-------|
| ADRs in `docs/decisions/` | 14 (016, 017, 018 latest accepted) | 15 (019 accepted) |
| ADR Status Ledger next-unreserved pointer | 019 | 020 |
| W003 Phase 1 Part 2 hosting decision status | "Deferred to Erik at implementation time" | Closed; cites ADR-019 as the M5-S1 closure |
| Settlement workflow host primitive | Undecided | Wolverine Saga |
| Migration contract from Saga to `ProcessManager<TState>` | Implicit | Named: 28 pure-function scenarios (Sections 1-7) plus the W003 design — no contract changes, no scenario rewrites |

---

## S1b — W003 F002: Field Name Convention

### Resolution

A new "Field Name Convention: `Price` at Initiation, `HammerPrice` Post-Initiation" subsection lives at the end of W003 Phase 1 Part 2, after the @Architect decision blockquote. The subsection names the convention at six touchpoints (`InitiateSettlement` command, `SettlementInitiated` event, `SettlementState.Initiated`, `SettlementState.ReserveChecked` for BIN, `ReserveCheckCompleted`, `WinnerCharged.Amount`, downstream events), explains the source-agnostic-at-initiation rationale, and names the implementation-side analogue (the inbound-event handler maps cross-BC-contract field names to the source-agnostic `Price` at command construction).

### Why Phase 1 Part 2 placement

The Open Questions section preferred Phase 1 Part 2 over §7's evolver sidebar because the rename's rationale is most legible alongside the Saga-vs-ProcessManager hosting comparison. That preference held: the Field Name Convention sits at the bottom of Part 2 where a reader exiting the host comparison naturally encounters it before reading Part 3 onwards. Placing it as a §7 sidebar would have buried the convention under an evolver-mechanics paragraph that most readers skim.

### Touchpoint table

The amendment renders the convention as a six-row table mapping each touchpoint to its field name and the "why" — a structural shape that reads as a contract rather than as prose. The table also records `WinnerCharged.Amount` as an intentional naming difference (payment-domain vocabulary at the moment money moves) — surfacing what would otherwise look like a third inconsistency.

---

## S1c — W003 F004: payload normalization + SettlementId audit

### Resolution

Two coordinated edits:

1. **Canonical-payload-shapes preamble** added to `003-scenarios.md` between the test-structure block and Section 1. Names the eight-field `SettlementInitiated` payload (plus `InitiatedAt` as the timestamp on decider-output renderings) and explains why `ReservePrice` and `FeePercentage` are load-bearing for the evolver; names the SettlementId-on-every-settlement-internal-event convention; explains why `ReserveCheckCompleted` is the lone exception (stream-internal, no downstream consumers).
2. **§1.1 and §1.2 updated** to render the full nine-field payload (eight payload fields + `InitiatedAt`). §1.3 renders an exception (no payload to normalize). §4.1 and §4.2 `FinalValueFeeCalculated` payloads gained `SettlementId` per the prompt's "include on all four §3-§6 sections" lean.

### Why the preamble

The `…` placeholder in §1.2 was load-bearing prior to this slice — readers had to consult §7.1 to understand what fields were elided. The preamble eliminates that inference burden by naming the canonical shape once at the top of the file. Future scenario authors writing new sections inherit the convention by default rather than re-deriving it.

### Structural metrics

| Metric | Before | After |
|--------|--------|-------|
| `SettlementInitiated` payload field count, §1.1 | 7 (no ReservePrice, no FeePercentage) | 9 (8 payload + InitiatedAt) |
| `SettlementInitiated` payload field count, §1.2 | Truncated with `…` | 9 (full render) |
| `FinalValueFeeCalculated` payload includes SettlementId | No (§4.1, §4.2) | Yes |
| Canonical-payload-shape preamble at file top | Absent | Present |

---

## S1d — W003 F005: BidderCreditView projection

### Resolution

Three coordinated edits to W003:

1. **Phase 1 Part 7** "The BidderCreditView Projection" authored, between the existing Part 6 (Settlement ID Strategy) and the Phase 1 Summary. Defines the projection's shape (`BidderId`, `RemainingCredit`, `LastChargedSettlementId`, `UpdatedAt`), lifecycle (initialised on `ParticipantSessionStarted`; updated on `WinnerCharged`; idempotent against `LastChargedSettlementId`), and consumer model (Relay's broadcast handler; future bidder-balance endpoint; explicitly NOT the Auctions DCB per Part 4 Option A).
2. **Ubiquitous Language table** gains a `BidderCreditView` row with cross-reference to Part 7 and an explicit "what it is *not*" clarification distinguishing the running balance from the per-bid ceiling enforced by Auctions' DCB.
3. **Phase 1 Summary table** gains row 8; "New design elements identified" list extended with the Part 7 reference.

Plus one cite-and-edit per Phase 5 §7 (single-paragraph fix authorized for narrative drift cited from a slice prompt):

4. **Narrative 002 Moment 3** "Things deliberately not included" parenthetical updated. The prior text claimed "W003 does not define a named bidder-credit projection — see Finding 005 at session close"; the new text cites Phase 1 Part 7's name (`BidderCreditView`) and notes the M5-S1 closure. The narrative's body prose ("the bidder-credit ledger debits…") is preserved as-is — the framing is about MVP credit-ledger posture, not the document's name.

### Why `BidderCreditView` over `BidderCreditLedger`

Open Question #1 preferred `BidderCreditView` for two reasons: (a) it matches CritterBids' `*View` projection convention from `CatalogListingView` and `ListingBidSummary`; (b) the "credit-ledger posture" framing in narrative 002's Setting refers to MVP-vs-real-payment-processor, not to the document's name. The projection is a Marten document derived from events; calling it a "Ledger" would overcommit the name to a financial-domain primitive that the read model is not. The name choice was straightforward at session start and held without revisit.

### Why a Settlement-side projection rather than a Participants-side projection

Part 7 names the rationale explicitly: `WinnerCharged` is a Settlement-internal event; the projection's lifecycle is owned by the BC that owns the events feeding it. A Participants-side projection would have required Settlement to publish a cross-BC `BidderCharged` integration event for Participants to consume — doubling the contract surface for no clear MVP benefit. This decision is consistent with the BC-isolation discipline that CritterBids practices across all eight BCs.

### Structural metrics

| Metric | Before | After |
|--------|--------|-------|
| W003 Phase 1 Parts | 6 (Parts 1-6) | 7 (Parts 1-7) |
| W003 Phase 1 Summary table rows | 7 | 8 |
| W003 Ubiquitous Language table rows | 14 | 15 |
| Narrative 002 Moment 3 deferred-list parenthetical | Cites W003 gap | Cites W003 Phase 1 Part 7 |
| Named bidder-credit projection in W003 | None | `BidderCreditView` |

---

## S1e — Three Settlement integration contract stubs

### Resolution

Three `sealed record` files in `src/CritterBids.Contracts/Settlement/`:

| File | Payload | Source scenario |
|---|---|---|
| `SettlementCompleted.cs` | `SettlementId, ListingId, WinnerId, SellerId, HammerPrice, FeeAmount, SellerPayout, CompletedAt` | `003-scenarios.md` §6.1 |
| `PaymentFailed.cs` | `SettlementId, ListingId, WinnerId, Reason, FailedAt` | `003-scenarios.md` §3.2 |
| `SellerPayoutIssued.cs` | `SettlementId, SellerId, PayoutAmount, FeeDeducted, IssuedAt` | `003-scenarios.md` §5.1 |

Triple-slash docstrings on each: publisher, transport, inbound queue routes (named where M5 ships them; flagged as post-M5 where consumers haven't shipped yet), consumer list, field rationale per `integration-messaging.md` L2's full-payload-at-first-commit convention.

### Why these three and not the other four

W003 §"Integration in/out" plus the M5 milestone doc §2 "Integration contracts authored in M5" name the integration-out set as exactly these three. The Settlement-internal events (`SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated`) stay BC-internal in `src/CritterBids.Settlement/` once that project exists (M5-S2). They do not cross BC boundaries, so they do not belong in `Contracts/Settlement/`.

### Cross-reference shape

Each docstring follows the precedent set by `Auctions/ListingSold.cs` and `Selling/ListingWithdrawn.cs`: publisher block, transport block, consumers block (with M5 vs post-M5 markers), field rationale block. The precedent shape is the shape M5-S2 onwards expects when authoring handlers and tests against these contracts.

### Structural metrics

| Metric | Before | After |
|--------|--------|-------|
| Files in `src/CritterBids.Contracts/Settlement/` | 0 (directory absent) | 3 |
| Total `sealed record` integration contracts | 19 | 22 |
| `dotnet build` warnings on Contracts | 0 | 0 |
| `dotnet build` errors on Contracts | 0 | 0 |
| Contracts test count | 1 | 1 (no new tests; M5-S1 is docs-and-stubs only) |

---

## S1f — `marten-projections.md` skill-file flag

### Resolution

A "Pending: M5-S3 amendment (cross-BC-event-seeded projection pattern)" callout added near the file's top alongside the existing `CritterBids status` callout. Names the pattern's grain (one cross-BC integration event seeding one Settlement-internal cache, structurally distinct from M3-S6's sibling-handler pattern), names the lived ground that will materialize in M5-S3 (the `PendingSettlement` projection consuming `ListingPublished`), and pre-authorizes M5-S3's retrospective to author the full pattern documentation rather than M5-S1.

### Why a flag rather than full pattern documentation

M5-S1 is docs-and-stubs only per the prompt's scope. The full pattern documentation requires lived ground — the M5-S3 implementation will surface the retry-on-projection-lag posture in code, the load-by-listing-id correlation idiom, and the Pending → Consumed / Expired / Failed status transition handlers. Authoring the pattern from W003 alone before M5-S3 ships would risk anchoring on workshop framings that the implementation refines.

---

## S1g — W003 hosting-decision pointer

### Resolution

A single blockquote inserted under the W003 Phase 1 Part 2 "design around decider semantics" decision, citing ADR-019 as the M5-S1 hosting closure. This is the visible signal that connects the workshop's deliberately-noncommittal design framing to its M5-grade resolution: a reader exiting the W003 decision encounters the host choice immediately rather than discovering it only by reading ADR-019 separately.

The blockquote does not edit the prior decision — the "design around decider semantics" framing remains the workshop's authoritative position. It only adds the M5-S1 host-choice closure as a downstream-decision pointer.

---

## Test results

| Phase | Contracts Tests | Result |
|-------|-----------------|--------|
| Baseline | 1 | Passed |
| After S1e (contract stubs added) | 1 | Passed |

Test count unchanged. M5-S1 is docs-and-stubs only; M5-S2 onwards lands handlers, projections, and the test surface that exercises the integration contracts.

Full solution build: 0 errors, 0 warnings (verified via `dotnet build CritterBids.slnx`).

---

## Build state at session close

- `dotnet build CritterBids.slnx` — 0 errors, 0 warnings
- `dotnet test --filter Contracts` — 1 passed, 0 failed
- New `sealed record` integration contracts in `src/CritterBids.Contracts/Settlement/`: 3
- New ADRs in `docs/decisions/`: 1 (ADR-019)
- W003 deep dive Phase 1 Parts: 7 (was 6)
- W003 Phase 1 Summary table rows: 8 (was 7)
- W003 Ubiquitous Language table rows: 15 (was 14)
- Files modified outside `docs/`: 0 (the three contract stubs are new files, not modifications)
- `src/CritterBids.Settlement/` project: still absent (correct — M5-S2 territory)
- `tests/CritterBids.Settlement.Tests/` project: still absent (correct — M5-S2 territory)
- Settlement-internal event types in any project: 0 (correct — they live in `src/CritterBids.Settlement/` once that project exists)

---

## Key learnings

1. **The cutover gate's joint-authority discipline holds without ceremony.** AUTHORING.md rule 3 makes the milestone doc and the narrative jointly authoritative; the prompt's `**Narrative:**` line was the only structural difference between this prompt and M3-S1 / M4-S1, and the slice ran without any new ceremony around the citation. The discipline was load-bearing during F005's name choice (`BidderCreditView` over `BidderCreditLedger`) — narrative 002's Setting framed the trade-off, and the resolution included a cite-and-edit back to narrative 002 Moment 3 per Phase 5 §7. Future M5 slices inherit this pattern with no further methodology overhead.

2. **Folding deferred findings into the foundation slice was structurally correct, not just a PR-savings move.** Phase 5 retro Item 7 framed the W003 amendments fold as "saved a PR." The lived experience refines that: the F002 Field Name Convention belongs adjacent to the Saga-vs-ProcessManager hosting comparison because both speak to the same per-phase state-shape decisions; the F004 payload normalization belongs adjacent to the canonical event vocabulary the contract stubs encode; the F005 BidderCreditView definition closes a workshop gap that narrative 001 Moment 8 was already implicitly betting on. Any of the three would have been awkward in a separate workshop-cleanup PR — they are foundation decisions, not workshop hygiene.

3. **The "what it is *not*" column in the Ubiquitous Language table earns its keep at moments like F005.** Adding `BidderCreditView` to the table and writing its "Distinct from the per-bid ceiling enforced by Auctions' DCB" clarifier costs one sentence and prevents a category of future confusion: a reader scanning the table sees the running-balance / per-bid-ceiling distinction without having to read Part 4 plus Part 7 to assemble it. The W003 table convention has been worth its inch since W001; M5-S3 onwards should continue exercising the column when introducing new terms.

4. **The framework-readiness deferral idiom is reusable beyond this ADR.** ADR-019's three explicit revisit triggers (framework stabilization, lived-friction signal, project-direction input) are a structural shape that recurs whenever CritterBids touches Critter Stack primitives that are themselves in design. M3-S1's ADR 007 Gate 4 deferral was the precedent (waiting on JasperFx input). The same three-trigger shape applies; future ADRs that defer on framework readiness can lift the structure verbatim.

5. **Per-Moment surrounding-directory reads pay off for cite-and-edit work.** Phase 5 retro Item 5's "pre-Moment surrounding-directory reads" lesson generalized cleanly to the F005 cite-and-edit: reading narrative 002 Moment 3's full structure (body prose plus deferred-list parenthetical) before authoring the W003 Phase 1 Part 7 amendment made the cite-and-edit scope obvious — only the parenthetical needed the cite, not the body prose. A weaker prep would have either over-touched the narrative (rewriting body prose unnecessarily) or under-touched it (leaving the parenthetical's stale claim that "W003 does not define a named bidder-credit projection").

6. **Ordering matters when adding a new Part to an in-progress workshop.** A first attempt at adding "Part 7" prepended it before the existing "Part 6", creating a Part 5 → Part 7 → Part 6 reading order. Caught and fixed within the session. The lesson for future workshop amendments: when adding a section by number to a numbered series, anchor the edit on the *next* surrounding heading, not the *previous* one. The Edit tool's `old_string` for "## Phase 1 Summary" is a stronger anchor than "### Part 6: Settlement ID Strategy" if the new section is going to land between them.

7. **The single-PR fold for foundation slices is structurally healthy under joint-authority discipline.** The slice produced eight items (S1a-S1h). Splitting them across multiple PRs would have created a coordination tax between the ADR (which informs the W003 amendments), the W003 amendments (which inform the contract stubs and the narrative cite-and-edit), and the contract stubs (which inform M5-S2's wiring). A single PR keeps the dependency chain visible at one review surface. Future foundation slices should default to this shape; the fold is a feature, not a workaround.

---

## Findings against narrative

The slice was **the resolution mechanism for three pre-existing narrative 002 findings** (F002, F004, F005) plus the closure of one foundation decision (ADR-019, not narrative-driven). No *new* findings against narrative 002 surfaced during this slice — the slice's whole purpose was folding the deferred F002/F004/F005 work, plus the narrative 002 Moment 3 cite-and-edit per Phase 5 §7 that F005's resolution flow required.

| Lane | Action |
|---|---|
| `narrative-update` | Narrative 002 Moment 3's deferred-list parenthetical updated as the F005 resolution path. Body prose preserved. Single-paragraph fix per Phase 5 §7. Resolved in this PR. |
| `workshop-update` | Three: F002 (Field Name Convention added to W003 Phase 1 Part 2), F004 (`SettlementInitiated` payload normalized; SettlementId added to §4.1 / §4.2), F005 (W003 Phase 1 Part 7 authored; Ubiquitous Language and Phase 1 Summary updated). All three resolved in this PR. |
| `code-update` | Not applicable. M5-S1 is docs-and-stubs only; the integration contract stubs are new code, not corrections to existing code. |
| `document-as-intentional` | F002's Field Name Convention falls under this lane in narrative 002's findings (the convention is correct as designed; only the documentation was incomplete). Resolved in this PR via the W003 Phase 1 Part 2 subsection. |

The cumulative narrative 002 findings ledger after this PR: F001 ✓ resolved (PR #20), F002 ✓ resolved (this PR), F003 ✓ resolved minimum-scope (PR #20; broader sweep deferred), F004 ✓ resolved (this PR), F005 ✓ resolved (this PR). All five findings closed.

The W003 broader-sweep deferral (narrative 002 F003's untouched references at L29 / L649 / L663) remains queued for a future workshop-cleanup session per the prompt's explicit out-of-scope clause. M5-S1 did not touch it.

---

## Verification checklist

- [x] ADR-019 (or analogous slug) authored at `docs/decisions/<NNN>-settlement-workflow-hosting.md` with status, context, decision, consequences sections per ADR 016 / 017 / 018 shape.
- [x] `docs/decisions/README.md` Status Ledger lists ADR-019; next-unreserved pointer advanced 019 → 020.
- [x] W003 carries the F002 "Field Name Convention" note explaining the `Price` ↔ `HammerPrice` rename across initiation.
- [x] W003's `003-scenarios.md` §1.1 / §1.2 / §1.3 show the full `SettlementInitiated` payload per §7.1 (eight fields including `ReservePrice` and `FeePercentage`; nine fields counting `InitiatedAt`). §1.3 throws an exception so has no payload to normalize.
- [x] W003's `003-scenarios.md` §3.1 / §4.1 / §5.1 / §6.1 SettlementId rendering is consistent (include-on-all chosen rendering applied across all four sections; §4.1 / §4.2 gained SettlementId).
- [x] W003 carries the F005 bidder-credit projection definition (name `BidderCreditView`, shape, lifecycle, consumer model) in Phase 1 Part 7.
- [x] F005 projection name diverged from narrative 002 Moment 3's "bidder-credit projection / ledger" placeholder; narrative 002 Moment 3 deferred-list parenthetical updated per Phase 5 §7 cite-and-edit.
- [x] Three contract stubs at `src/CritterBids.Contracts/Settlement/` (`SettlementCompleted.cs`, `PaymentFailed.cs`, `SellerPayoutIssued.cs`); namespace `CritterBids.Contracts.Settlement`; `sealed record`; triple-slash docstring per the reference shapes.
- [x] `docs/skills/marten-projections.md` carries a "Pending: M5-S3" note flagging the cross-BC-event-seeded projection pattern as an in-scope retrospective amendment after S3 ships.
- [x] `dotnet build` clean (0 warnings, 0 errors) on the new contract stubs.
- [x] `docs/retrospectives/M5-S1-settlement-bc-foundation-decisions-retrospective.md` exists; mirrors the M3-S1 / M4-S1 retro shape.
- [x] No `src/` files outside `src/CritterBids.Contracts/Settlement/` edited in this slice.
- [x] No `tests/` files edited (M5-S1 is docs-and-stubs only; tests come with implementation in M5-S2+).

---

## What remains / next session should verify

### In scope for M5, deferred to S2

- **`CritterBids.Settlement` project scaffold** — `AddSettlementModule()`, Marten config per `adding-bc-module.md`, module wiring in `Program.cs`, RabbitMQ routing setup.
- **`CritterBids.Settlement.Tests` project scaffold** — xUnit + Shouldly + Testcontainers + Alba per the standard CritterBids test stack.
- **Settlement-internal event types** — `SettlementInitiated`, `ReserveCheckCompleted`, `WinnerCharged`, `FinalValueFeeCalculated` land in `src/CritterBids.Settlement/`, not `Contracts/`. They are workflow-internal per W003 §"Integration in / out".

### In scope for M5, deferred to S3

- **`PendingSettlement` projection** seeded from `CritterBids.Contracts.Selling.ListingPublished`. First CritterBids cross-BC-event-seeded projection.
- **`marten-projections.md` full pattern documentation** — the "Pending: M5-S3" flag added in this slice cashes in at S3's retro.

### In scope for M5, deferred to S4

- **Settlement Saga implementation** per ADR-019: seven-phase progression, self-sending continuation commands, `MarkCompleted()` at terminal state. Decider semantics preserved at the handler level.
- **`wolverine-sagas.md` skill-file amendment** with the Settlement-side example.
- **All decider scenarios from `003-scenarios.md` §1-§6 happy-path branches** green; integration test exercising the full workflow end-to-end.

### In scope for M5, deferred to S5

- **Failure paths** (`PaymentFailed` from `ReserveChecked(WasMet: false)` per §3.2) and **BIN source path** (`BuyItNowPurchased` consumption; evolver branches to `ReserveChecked(WasMet: true)` per §1.2 and §7.2).
- **`BidderCreditView` projection implementation** per W003 Phase 1 Part 7.

### In scope for M5, deferred to S6

- **`SellerPayoutIssued` integration-event publishing** via Wolverine outbox; Relay-side stub or test fixture if Relay BC has not yet shipped.

### Out of scope, tracked elsewhere

- **Real payment-processor integration** — post-MVP per W003 §"Winner Charge".
- **Compensation paths beyond MVP** — post-MVP per W003 Phase 1 Part 3.
- **W003 broader storage-staleness sweep** (narrative 002 F003's references at L29 / L649 / L663) — future workshop-cleanup session, not M5.
- **`ProcessManager<TState>` migration** — deferred per ADR-019; revisit on any of the three named triggers.

### Foundation refresh closure

This slice is the cutover gate's first execution. The discipline operated cleanly without methodology adjustment. Phase 5's seven folded-in items (em-dash hygiene scope, sibling-listing pattern, code-comment-as-routing-evidence, path-citation pre-check, pre-Moment surrounding-directory reads, observer-protagonist Voice, W003 amendments folded into M5-S1) all continued to apply in M5-S1's docs-grade work. Future M5 slices inherit the patterns inherited from Phase 5; no methodology amendments needed.
