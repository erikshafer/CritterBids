# Lessons Learned

The **only evaluative artifact** in `docs/extraction/`. Every other file in this corpus is descriptive — facts cited from code or vision. Here is where judgment lives.

Each lesson: what happened, the evidence (cited retro, ADR, or gap-register entry), and the generalizable insight. Lessons may name what was hard and what would have weighed differently. Lessons **may not** prescribe a rebuild or propose new architecture — "the next system should X" is out of bounds.

Insights are framed at the level a future Critter-Stack practitioner could carry into an unrelated project.

---

## 1. The storage saga — ADR 008 → 009 → 010 → 011

**What happened.** Over a four-day window in April 2026 the project's storage architecture went through three live revisions and a final pivot:

1. **ADR 008** — picked **named Marten stores per BC** (`AddMartenStore<T>()`) for hard schema isolation between modules.
2. **ADR 009** — superseded ADR 008 the same week. The ancillary store API was found to **omit critical Wolverine registrations** that the primary store gets automatically. Switched to a single shared primary `IDocumentStore`, with each BC contributing types via `services.ConfigureMarten()` inside its module extension.
3. **ADR 010** — surfaced the dual-store conflict. Once Settlement and Participants were planned on Polecat/SQL Server alongside Marten/PostgreSQL BCs, both calls (`AddMarten().IntegrateWithWolverine()` and `AddPolecat().IntegrateWithWolverine()`) claimed the "main" message-store role. Production Aspire start crashed with `InvalidWolverineStorageConfigurationException`. Polecat had no ancillary-store API to demote it (verified by reading `Wolverine.Polecat.xml` directly).
4. **ADR 011** — the all-Marten pivot. Migrated Participants, Settlement, Operations from Polecat back to Marten/PostgreSQL. **The dual-store scenario was eliminated rather than resolved.** Supersedes ADR 003 (the original Polecat-BCs decision).

**Evidence.** `docs/decisions/008-marten-bc-isolation.md`, `009-shared-marten-store.md`, `010-wolverine-dual-store-resolution.md`, `011-all-marten-pivot.md`; `docs/retrospectives/M2-S3-wolverine-dual-store-resolution-retrospective.md` lines 42-80 (the research that confirmed Polecat had no ancillary-store API); `docs/decisions/README.md` superseded chain.

**Generalizable insight.** When a multi-storage modular monolith touches multiple integration surfaces of a message-bus framework, the framework's view of the storage layer often has invariants ("exactly one main store") that aren't discoverable from package descriptions. **Read the NuGet XML docs and the source before assuming the integration is symmetrical.** And: when two reasonable options conflict, eliminating the conflict (drop one option) is often cheaper than resolving it (engineer a shim). The all-Marten pivot was a smaller diff than the alternative would have been, and it removed an entire category of future problem.

**Methodology takeaway.** Three architectural changes in three weeks is a sign that the foundation was being designed in code, not in advance. The next ADR ledger entry (ADR 016 spec-anchored development, 2026-04-25) was authored two weeks later in part to give early-foundation work a structured place to absorb this kind of iteration without the source code being the design surface.

---

## 2. UUID v5 vs UUID v7 — a convergence the docs didn't quite catch up with

**What happened.** ADR 007 evolved across four dated amendments (2026-04-13, -16, -20, 2026-05-17). The lived position by M5 close: **UUID v7 is primary** for stream IDs when no natural business key exists; UUID v5 (with a BC-specific namespace constant) is reserved for the two deterministic-ID cases that proved necessary — Settlement's saga + financial event stream id (`SettlementId(listingId)`) and the Proxy Bid Manager saga id (the composite `UuidV5(ns, $"{ListingId}:{BidderId}")`). Most stream creations are `Guid.CreateVersion7()`.

But `.github/copilot-instructions.md:26` still says "UUID v5 stream IDs with BC-specific namespace prefixes" as a universal rule. The instructions file was a snapshot of the original ADR 007 stance; ADR 007's amendments updated `CLAUDE.md` (line 106) but not the Copilot-instructions file. Recorded in `gaps-and-drift.md` D1.03.

**Evidence.** ADR 007 amendment dates; `CLAUDE.md:106-110`; `.github/copilot-instructions.md:26`; `gaps-and-drift.md` D1.03.

**Generalizable insight.** AI-orienting documents (CLAUDE.md, copilot-instructions.md, AGENTS.md) tend to outlive their accuracy. They get written early, when one stance is held with conviction, and they don't get touched when the stance evolves through ADRs because no one re-reads them on each ADR. **A single-source-of-truth for non-negotiable conventions should be canonical; the AI-orienting docs should reference it, not duplicate it.** CritterBids' instructions duplicate the conventions, and the duplications drift first.

---

## 3. Saga terminal-path discipline — the `NotFound` absorber as a Wolverine convention

**What happened.** Across three Wolverine sagas — `AuctionClosingSaga` (M3-S5b), `ProxyBidManagerSaga` (M4-S4), `SettlementSaga` (M5-S5) — the same pattern emerged independently three times: every saga handler that ends in `MarkCompleted()` needs a paired `public static OutgoingMessages NotFound(X) => new()` absorber, because Wolverine will throw `UnknownSagaException` on the post-`MarkCompleted` redelivery that the at-least-once delivery contract guarantees.

The first encounter (M3-S5b) was a surprise. The third encounter (M5-S5) was mechanical. The `docs/skills/wolverine-sagas.md` skill file was amended after the first encounter; the second and third sagas adopted the pattern without re-discovery.

**Evidence.** `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md`; `docs/retrospectives/M4-S4-proxy-bid-manager-terminal-paths-retrospective.md`; `docs/retrospectives/M5-retrospective.md` Key Learning #4 (status-preservation cascade); `bcs/auctions.md`, `bcs/settlement.md`.

**Generalizable insight.** **Skill files are most valuable when they capture the pattern after the first discovery and before the third.** CritterBids' habit of folding retro learnings into `docs/skills/*` paid off here — by M5, the saga skill file was a checklist that prevented re-encountering the pattern. The cost of not capturing it would have been re-discovering it on every saga: three retros, three skill updates, three "surprise" entries. Pay the documentation cost on the first discovery.

---

## 4. `MultipleHandlerBehavior.Separated` and the testing dispatch idiom

**What happened.** Setting `opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated` (`Program.cs:20`) was the right structural choice for cross-BC fan-out — it gives each handler for the same message type its own sticky queue, enabling independent retry/dedup per consumer. But it carries a test-fixture consequence: dispatch via the bus must use `SendMessageAndWaitAsync`, not `InvokeMessageAndWaitAsync`. The latter resolves a single handler in-process and silently bypasses the queue topology entirely.

This was a recurring discovery across BCs. Each new BC's first dispatch test rediscovered it.

**Evidence.** `bcs/auctions.md` "Notable internal conventions"; `bcs/settlement.md`; cross-referenced in `docs/retrospectives/M3-S4-dcb-place-bid-retrospective.md` and forward.

**Generalizable insight.** **Framework defaults shape testing more than they shape production wiring.** The default Wolverine behavior is `MultipleHandlerBehavior.ClassicCombined`; flipping to `Separated` is a one-line change but it changes the entire testing posture. When making this kind of structural framework choice, write the first test against it — don't let it be a posture choice that the codebase discovers asymmetrically over five milestones.

---

## 5. The duplicate-projection pattern (M4-D4) — premature abstraction avoided

**What happened.** Auctions and Settlement both need a participant's credit ceiling and credit balance respectively. The first design impulse was to share a single read-model (either a cross-BC read or a shared package). M4-D4 chose **duplication** — each BC maintains its own copy projected from `ParticipantSessionStarted` on a BC-specific queue (`auctions-participants-events`, `settlement-participants-events`). Auctions has `ParticipantCreditCeiling`; Settlement has `BidderCreditView`. They evolve independently; their schemas already differ (the Settlement view tracks `LastChargedSettlementId` for idempotency; Auctions doesn't need that).

The first lived application (Settlement, M5-S5) and the second (Auctions, in the M4 work that preceded but didn't ship before M5) confirmed the pattern. By the time both were in place, the divergence in field shape proved the duplication wasn't a premature copy — they were different read models that happened to share an event source.

**Evidence.** `docs/retrospectives/M5-S5-settlement-failure-paths-bin-source-bidder-credit-view-retrospective.md`; `bcs/settlement.md` "BidderCreditView"; `bcs/auctions.md` "ParticipantCreditCeiling"; M4-D4 decision referenced across retros.

**Generalizable insight.** **In a modular monolith, two BCs needing "the same data" is often two BCs needing different read models built from the same event stream.** Sharing the read model couples lifecycle, schema, and idempotency strategy across modules that should not be coupled. The cost of one extra queue route and one extra projection class is low; the cost of unwinding a premature shared abstraction is high. CritterBids' "three is better than premature abstraction" rule (the bid-increment ladder co-located three times) is the same principle applied at the line level.

---

## 6. Composite-key saga correlation — Wolverine's identity-resolution ceiling

**What happened.** The Proxy Bid Manager saga's identity is `UuidV5(ns, $"{ListingId}:{BidderId}")` — a derived Guid that no contract event carries directly. M4-S3 found three paths and analyzed each: (A) a custom identity resolver; (B) add a `ProxyBidManagerSagaId` field to `BidPlaced`; (C) a dedicated dispatcher.

Path A was unavailable — Wolverine's `[SagaIdentityFrom]` only resolves Guid properties by name; no resolver hook. Path B was ruled out because a single `BidPlaced` can target N proxy sagas (one per registered bidder on the listing), and one Guid field cannot address many. Path C — the dispatcher — was chosen. `ProxyBidDispatchHandler` queries active sagas by `ListingId` and emits one wrapped `ProxyBidObserved` (`ProxyListingSoldObserved`, etc.) per active saga carrying the resolved `SagaId` for downstream routing.

The dispatcher is one extra hop, one extra class, and one extra set of wrapper-event types. But it gets the composite-key semantics that the saga actually requires.

**Evidence.** `docs/retrospectives/M4-S3-proxy-bid-manager-saga-skeleton-retrospective.md`; `bcs/auctions.md` Notable design decisions; `workflows/proxy-bidding.md` "Composite-key correlation".

**Generalizable insight.** **A framework's identity-resolution surface is a hard ceiling on what's expressible.** When the domain demands a derived or composite key, expect to build a bridge. The pattern — query-by-natural-key, emit wrapper events carrying the resolved framework-key — is general; it would apply to any per-(ListingId, BidderId), per-(SessionId, ParticipantId), per-(OrderId, LineItemId) saga shape.

---

## 7. Meaningful event absence as a design tool

**What happened.** The Settlement saga has two source overloads on `StartSettlementSagaHandler`: bidding and BIN. The bidding overload starts the financial event stream at `Initiated` and proceeds through `ReserveCheckCompleted` before `WinnerCharged`. The BIN overload starts the stream at `ReserveChecked` directly and **never appends `ReserveCheckCompleted`**. The absence of that event in a stream is the §9.2 audit signal "this was a BIN settlement."

This is part of a broader pattern called out in `docs/vision/domain-events.md:135`: some patterns rely on what's NOT in a stream. The bidding-vs-BIN audit guarantee is the canonical CritterBids example.

**Evidence.** `bcs/settlement.md` "Source overload contract"; `glossary.md` "Meaningful event absence"; `docs/vision/domain-events.md:135`.

**Generalizable insight.** **Event sourcing's strongest auditing power comes from the events that didn't happen as much as the events that did.** Designing for meaningful absence requires discipline: it means the source overload must never append the absent event for any reason, the audit query must check for absence rather than presence, and the documentation must call out the absence as the signal. CritterBids embedded the rule in three places (source code, glossary, vision doc) — appropriate weighting for an invariant the entire audit story depends on.

---

## 8. Stream-existence pre-checks as the canonical idempotency idiom (vs DCB consistency)

**What happened.** Two distinct idempotency strategies surfaced in the Auctions BC, and they were initially conflated. The M4-S5 milestone doc framed Flash-session fan-out as "DCB-primary," but the M5 lived implementation of `SessionStartedHandler` clarified that the idempotency mechanism is **stream-existence pre-query**, not DCB. The handler queries whether the listing's stream already exists; if it does, the append is skipped. DCB is a separate mechanism that gates per-event consistency within a stream once that stream exists.

The M3 `ListingPublishedHandler` for Timed listings is the original example of the stream-existence idiom; M5 `SessionStartedHandler` for Flash listings is the inheriting application.

**Evidence.** `bcs/auctions.md` Notable internal conventions; `src/CritterBids.Auctions/SessionStartedHandler.cs:46-56` (the comment block that disambiguates).

**Generalizable insight.** **Two correctness mechanisms in the same BC will be confused with each other when the documentation only names them by their effect.** "Idempotency" is the effect; "stream-existence pre-query" and "DCB consistency assertion" are different mechanisms that produce idempotent-looking behavior. Naming the mechanism in the doc, the test, and the source comment is worth the verbosity.

---

## 9. The vision doc's present-tense framing of unbuilt BCs

**What happened.** The vision doc (`docs/vision/bounded-contexts.md`) describes Obligations (lines 139-162), Relay (165-186), and Operations (189-211) in confident present tense — aggregates, projections, events, integration points — as if these BCs exist. They do not exist as `src/` projects. A reader new to the project would form an inaccurate picture of what is built. Recorded in `gaps-and-drift.md` D1.05, D1.06, D1.07.

Conversely, the BCs that do exist (Participants, Selling, Auctions, Listings, Settlement) have lived implementations that the vision doc captures faithfully because they were written before the vision doc was last revised.

**Evidence.** `docs/vision/bounded-contexts.md` lines 139-211; Phase 0 inventory; `bcs/obligations.md`, `bcs/relay.md`, `bcs/operations.md`.

**Generalizable insight.** **Present tense is the wrong tense for unbuilt capability.** A "Status: Planned" or "Status: Not yet implemented" header on each unbuilt BC's vision section would do the work of distinguishing intent from reality without losing the design value of the prose. The cost of getting this wrong is highest at AI tooling: a coding agent reading the vision doc forms a mental model and acts as if those BCs were available — it doesn't know to verify against `src/` unless explicitly told to. The Phase 0 verification step in the business-extraction handoff existed for exactly this reason.

---

## 10. Spec-anchored development as a corrective to drift

**What happened.** ADR 016 (2026-04-25) was written after the storage saga (ADR 008-011) but before the Auctions BC milestone. It formalized **spec-anchored development**: workshop narratives in `docs/narratives/` are the architectural reference; code is authoritative for runtime behavior; drift is caught at retrospective time via four "finding lanes" (workshop-update, narrative-update, ADR-amend, skill-update).

The discipline has visible effects in later retros. M5 retros (S1-S6) routinely cite narrative findings; M5-S1 specifically calls out W003 amendments per "narrative 002 findings F002/F004/F005." The retro-as-drift-audit cadence works because the drift is named and recorded explicitly each session.

**Evidence.** `docs/decisions/016-spec-anchored-development.md`; `docs/retrospectives/M5-retrospective.md` "Findings against narrative" sections; the `docs/narratives/` folder existence and structure.

**Generalizable insight.** **Drift between intent and code is inevitable; the question is whether it's caught early or late.** ADR 016 chose early — at every session retrospective — and the cost is one of four standard "finding lanes" per drift item. The alternative (drift caught at deploy or never) costs more. The discipline only works because retros are mandatory deliverables on every PR, not optional artifacts.

**Caveat.** The discipline applies to narratives and ADRs, not to `.github/copilot-instructions.md` or the vision doc's older prose. The drift items in `gaps-and-drift.md` Class 1 D1.01-D1.07 are the residue of pre-ADR-016 docs that the discipline doesn't reach.

---

## 11. Declining executable specifications (ADR 018) — a "no" decision with a trigger

**What happened.** ADR 018 (2026-04-27) **declined Reqnroll / executable specifications** at MVP. The convention-based linkage between workshop scenarios and tests (scenarios cite by name and number; tests reference them in `[Fact]` titles) was judged sufficient. The ADR includes an explicit revisit trigger: "3+ `workshop-update` findings per narrative attributable to absence of mechanical generation, or cumulative PR rework from convention-linkage breakage."

By M5 close, no revisit was triggered. The convention held.

**Evidence.** `docs/decisions/018-reqnroll-position.md`; M5 retros' lack of workshop-update findings against the convention.

**Generalizable insight.** **"No" decisions are easier to live with when they ship with a revisit trigger.** A bare "we won't do X" tends to either get re-litigated every session or get forgotten. A trigger — "we'll revisit when Y happens" — gives the decision a future inflection point without re-opening it weekly. Useful pattern for any architectural choice that picks a less-instrumented option.

---

## 12. The Settlement saga's seven phases — saga over process-manager-via-handlers

**What happened.** ADR 019 (2026-05-03) chose **Wolverine Saga** over the Process Managers via Handlers alternative for the Settlement workflow. The reasoning: Settlement's seven phases share evolving state (`HammerPrice`, `FeePercentage`, `FeeAmount` materializing mid-workflow), which is exactly what the Saga primitive hosts. Process Managers via Handlers fit event-reactive flows without phased state — Relay's broadcast pipeline (post-M5) is the canonical alternative-shape fit.

The decider pattern from W003 was preserved as a **design lens within the saga**, not as a competing implementation primitive. ADR 019 Option C ("extract pure-function decider helpers") was left conditional on M5 implementation surfacing friction. No friction surfaced. No helpers extracted. The saga shape held without modification.

**Evidence.** `docs/decisions/019-settlement-workflow-hosting.md`; `docs/retrospectives/M5-retrospective.md` Key Learning #3 ("Multi-phase saga shape generalizes cleanly across source overloads"); `bcs/settlement.md`.

**Generalizable insight.** **The right framework primitive is the one whose lifecycle matches the workflow's lifecycle.** Saga is right when phases share state; Process Managers via Handlers is right when events fan out independently. The decision wasn't between "best practice" and "alternative" — it was between two valid Wolverine patterns with different shapes. ADR 019's clean phrasing of the asymmetry ("phased-state-fit") is portable to any future workflow-hosting decision in Wolverine or a similar message framework.

---

## 13. Test-fixture cross-BC handler isolation

**What happened.** Multiple times during M3 and M5, a test fixture that booted the full API host found that handlers from foreign BCs were firing when only one BC's handler was the test target. Examples: M3-S6 surfaced the `ListingsBcDiscoveryExclusion` need; M2-S3 used a similar `SellingBcDiscoveryExclusion`; M5 fixtures inherited the pattern.

The fix is an `IWolverineExtension` that excludes specific handler discovery — a per-BC fixture concern. The pattern is documented in `docs/skills/critter-stack-testing-patterns.md`.

**Evidence.** `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md`; `docs/retrospectives/M5-retrospective.md` Key Learning #1 (`tracked.Sent` vs `tracked.NoRoutes` fixture stance); `bcs/auctions.md` Test-evidenced behaviors; `bcs/settlement.md`.

**Generalizable insight.** **A modular monolith's test fixtures inherit the full registration topology, not the subset under test.** "Boot the host, exercise one handler" is the default mental model but it isn't the default mechanical behavior — every registered handler fires on its trigger event. Per-BC exclusion fixtures are the pragmatic answer; awareness of why they're needed is the prerequisite. New BCs in a modular monolith should plan for the fixture-exclusion concern at scaffold time, not test-authoring time.

---

## 14. What we'd weigh differently

A handful of choices, if revisited with the lived knowledge, would land in different places:

- **The Polecat experiment (ADR 003 → 011)** was substantial work for a learning that could plausibly have been front-loaded by a 30-minute API audit of `WolverineFx.Polecat`. The retro for M2-S3 effectively performed that audit; performing it before ADR 003 would have skipped two storage ADRs.
- **The `.github/copilot-instructions.md` file** is a static snapshot. Keeping it would mean keeping it accurate via per-ADR maintenance. Replacing its conventions section with "see CLAUDE.md and ADR ledger" would avoid the drift items D1.01-D1.04 entirely.
- **Present-tense framing of unbuilt BCs in the vision doc** is the single most damaging drift for AI agents. A "Status: Planned" header on each unbuilt section would eliminate the entire Class 1 D1.05-D1.07 cluster from `gaps-and-drift.md` for the cost of one editing pass.
- **`BiddingClosed` vs `ListingSold`/`ListingPassed`** is a strong distinction whose value is real — but the M5 retro evidence shows that downstream consumers consistently subscribed to outcome events (`ListingSold`/`ListingPassed`/`BuyItNowPurchased`) and not to `BiddingClosed`. The mechanical-close event has zero cross-BC consumers in current code. The distinction is defensible (future consumers may need it; saga-internal logic uses it) but it's worth reassessing whether the integration-event status should be downgraded to internal.

---

## What is not a lesson

- **Item-level dossier detail.** Each BC has its own dossier; lessons stay at the cross-BC pattern level.
- **Style or naming preferences.** The "no Event suffix" rule, the `sealed record` rule, the `IReadOnlyList<T>` rule — these are house rules, captured in `CLAUDE.md` and `glossary.md`. They are not lessons.
- **Prescriptions for a successor system.** The handoff prompt forbids it; this document holds the line.
