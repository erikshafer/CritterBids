# M4-S7: M4 Retrospective + Skill Consolidation Review + M4 Close

**Milestone:** M4 — Auctions BC Completion
**Slice:** S7 of 7 (final M4 session — M4 close; follows S6 which landed the Listings catalog session-membership + withdrawn extension and resolved ADR-014's sub-question)
**Narrative:** `docs/narratives/001-bidder-wins-flash-auction.md` — Moments 3 and 7 reflect M4-shipped surfaces (Session aggregate fan-out + Listings-side catalog reflection). S7's narrative work is a `defer → green` finding for Moment 3's lived-code audit, not a re-narration.
**Agent:** @PSA
**Estimated scope:** one PR; **documentation-only**; ~2-4 doc files touched; zero `.cs` / `.csproj` / `Program.cs` / `.slnx` diff
**Baseline:** 154 tests passing (1 Api + 1 Contracts + 6 Participants + 20 Listings + 36 Selling + 25 Settlement + 65 Auctions) · `dotnet build` 0 errors, 24 pre-existing NU1904 Marten NuGet warnings (unchanged across M3 / M4 / M5) · M4-S6 closed at the squash-merge of PR #39 (`32d6e70`). `CatalogListingView` carries 21 fields at M4 close (8 M2 + 10 M3-S6 + 1 M5-S6 + 2 M4-S6); the Status vocabulary is 7 strings. ADR-014 status is Accepted; sub-question resolved to Sub-Option A at M4-S6.

---

## Goal

Close M4 with three carried-forward deliverables from M4-S6's retro §"What M4-S7 should know":

1. The **M4 milestone retrospective** — capstone document for sessions S1 → S6 inclusive, authored against the exit criteria in `docs/milestones/M4-auctions-bc-completion.md` §1, using the M3 milestone retrospective `docs/retrospectives/M3-auctions-bc-retrospective.md` as the structural precedent.
2. A **skill consolidation review** — explicit confirmation that all three M4-relevant skill files (`wolverine-sagas.md` updated at S3/S4; `marten-projections.md` updated at S6) absorbed their findings inline, no deferred bulk pass is owed, and any first-use findings the M4 arc surfaced but didn't fold into a skill are flagged with a disposition (defer-to-future / fold-into-retro-only / fold-now). This is a **null-call audit**, not an M3-S7-style bulk pass — M4's session-by-session skill discipline means S7 ships with no skill-file commit unless the review surfaces a gap.
3. The **Aspire operational smoke test** — confirm the RabbitMQ management UI port (exposed in M4-S1 per milestone doc §1) is reachable, all M4 publish bindings and listen queues are visible, and the four new M4 queues / routes from §Appendix are wired correctly. Record the result in the retro per the M3-S7 precedent.

The session also performs two narrative-and-pattern dispositions surfaced by the M4-S6 retro:

- **Narrative 001 Moment 3 `defer → green` finding.** M4-S5 + M4-S6 shipped the Flash session cascade that Moment 3 narrates. The lived class names (`AuctionsSessionHandler` + `SellingListingWithdrawnHandler`, split per Sub-Option A) differ from the narrated `SessionMembershipHandler` (a single multi-source class). S7 decides: amend the narrative inline at this commit, or stub a Phase 2.5 follow-up prompt. Recommend the latter (separation of audit-and-fix from author-and-ship per ADR 016 Phase 2 discipline), but the call lives in the prompt's Open Questions.
- **OQ3 Path α Auctions-side terminal observation.** M4-S6 pinned the Listings-side terminal (Path 3 — catalog handler is the source of truth) via the `BiddingOpened_AfterListingWithdrawn_PreservesWithdrawnStatus` test. The Auctions-side terminal (whether the saga store exhibits Path 1 or Path 2 from the M4-S5 retro's candidate enumeration) remains unobserved. S7 decides: defer-with-named-trigger to M5/M6, or pin by adding an Auctions-side composition test. Recommend defer (S7 is docs-only); the call lives in the prompt's Open Questions.

No production code changes. No new tests. No `Program.cs` diff. No skill-file commits unless the consolidation review surfaces a gap.

---

## This session is documentation-only

Do not modify any `.cs` file, `.csproj` file, `.slnx` file, `Program.cs`, or any test file. Do not run `dotnet test` — the 154-test baseline is already green. Do not run `dotnet build` unless a documentation edit touches an embedded code block whose compile-ability warrants a spot sanity check. If the operational smoke test surfaces a wiring defect (e.g. a queue is not visible in the Aspire dashboard), **flag and stop** — do not hot-patch `Program.cs` in this slice; that work would belong to an M4-S8 or post-M4 follow-up.

If you find yourself about to write or edit production code, stop and re-read this prompt. S6 landed the last M4 production change; the build is clean at 154 green. M4 is closing out, not opening new technical territory.

---

## Context to load

- `docs/milestones/M4-auctions-bc-completion.md` — §1 exit criteria (walk each for the milestone retro's exit-criteria table), §7 acceptance-tests test counts for M4-close totals, §8 open-questions disposition for the milestone-level decisions resolved across S1-S6, §9 session breakdown (S1 → S7 timeline for the session-by-session summary), Appendix cross-BC integration map
- `docs/retrospectives/M4-S6-listings-catalog-session-and-withdrawn-retrospective.md` — authoritative source for S7's carry-forwards. Specifically §"What M4-S7 should know" (closing test count, ADR-014 amendment shape, OQ3 Path α observed state, final field inventory, narrative finding candidate, Operations BC `SessionCatalog` candidate)
- `docs/retrospectives/M4-S1-auctions-completion-foundation-decisions-retrospective.md` through `docs/retrospectives/M4-S5-session-aggregate-retrospective.md` — six prior M4 retros, one per implementation slice. Read for session-by-session table content; do not duplicate session-local detail in the milestone retro
- `docs/retrospectives/M3-auctions-bc-retrospective.md` — structural precedent for the M4 milestone retro. Match the section order (Exit Criteria Status → Session Timeline → Key Decisions → Cross-BC Integration Map → Test Counts → Key Learnings → Technical Debt / Deferred → What M5 Should Know — though M5 already shipped, so the section is forward-looking to M6 + post-M5 work)
- `docs/decisions/014-cross-bc-read-model-extension-shape.md` — for the M4 retro's "Key decisions / ADRs" section. The ADR is Accepted with a dual-date (2026-05-17 initial / 2026-05-20 M4-S6 amendment); S7 confirms status, does not amend
- `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 + `docs/narratives/001-findings.md` — for the `defer → green` finding disposition. Re-read Moment 3 to confirm the lived class topology divergence from the narrated `SessionMembershipHandler`

(Six items — under the seven-item AUTHORING.md soft cap.)

## In scope (numbered)

1. **`docs/retrospectives/M4-auctions-bc-completion-retrospective.md`** — the M4 milestone retrospective, written as the slice's primary deliverable. Structure inherits from `docs/retrospectives/M3-auctions-bc-retrospective.md`. Required sections, in order:

   - **Header block:** date (2026-05-21 or the commit-landing date), milestone, sessions span (S1 → S7, no splits), author
   - **Baseline vs. exit state:** test count progression (86 at M3 close → 134 at M4-S4 close → 148 at M4-S5 close → 154 at M4-S6 close — walk the arc; M4-S7 adds zero tests). Build state at M4 close (0 errors, 24 NU1904 NuGet warnings unchanged from baseline). What the end-to-end demo does at M4 close that it didn't at M3 close: Flash session create / attach / start; Proxy Bid Manager registration; competing-bid auto-response; proxy-exhausted exit; two-proxy bidding war; the M3-fixture `ListingWithdrawn` replaced by a real Selling-side producer
   - **Exit criteria status:** walk each criterion from `docs/milestones/M4-auctions-bc-completion.md` §1 with ✅ / ⚠️ / ❌ and a one-line rationale. One known reconciliation needed: §1 says "0 errors, 0 warnings"; the lived shape is "0 errors, 24 NU1904 NuGet vulnerability warnings unchanged across M3/M4/M5" — record the reconciliation explicitly (compiler warnings vs. NuGet advisory warnings; pre-existing across the milestone; no M4 work introduced any new warnings)
   - **Session-by-session summary table:** S1 (foundation decisions + contract stubs + Aspire UI port), S2 (Selling `WithdrawListing` producer + `ListingWithdrawn` real-publisher), S3 (Proxy Bid Manager saga skeleton — 5 of 11 scenarios + dispatcher pattern surfaced), S4 (Proxy Bid Manager terminal paths — 6 remaining §4 scenarios + saga-to-saga cascade), S5 (Session aggregate + `SessionStarted → BiddingOpened` fan-out + `PublishedListings` duplicate-projection), S6 (Listings catalog session + withdrawn + ADR-014 sub-question resolution + OQ3 Path α terminal pin), S7 (this session). Include a "Notable deviations" column per the M3 precedent — one line per cell
   - **Cross-BC integration map at M4 close:** reproduce or summarize §Appendix from the milestone doc with a ✅ confirmation per hop. Six integration hops live at M4 close (three from earlier milestones unchanged, three new in M4 per the appendix table)
   - **Test count at M4 close:** table matching the milestone doc §7 Test count summary projection (~118) against the actual close (154). The +36 delta is partially M5's interleaved work (Settlement BC tests landed during the M4 pause that the milestone doc didn't anticipate) and partially M4's own work expanding past the original sizing; reconcile explicitly so M5's planning hindsight is documented for the next milestone's sizing exercise
   - **Key decisions / ADRs:** M4-D1 (Proxy saga composite key as `$"{ListingId}:{BidderId}"`), M4-D2 (Session aggregate UUID v7), M4-D3 (ADR 014 timing — authored at M5-S6 outside M4, amended at M4-S6), M4-D4 (Auctions-side duplicate `PublishedListings` projection), M4-D5 (`ListingWithdrawn` as a new `Status` value, resolved at M4-S6), M4-D6 (M3 test-fixture `ListingWithdrawn` synthesis kept as unit-test shortcut, resolved at M4-S2). ADR 007 Gate 4 re-evaluated at M4-S1 — re-deferred with new trigger (M5-S1) and named owner (Erik). ADR-014 amendment at M4-S6 — third lived application + Sub-Option A resolution
   - **Key learnings — cross-session patterns:** top 5-7 learnings that apply across more than one M4 session or that M5+M6 should carry forward. Candidates from the M4 arc: (a) composite-key saga correlation via dispatcher pattern (S3), (b) saga-to-saga cascades under `SendMessageAndWaitAsync` are eager / single-cycle (S4), (c) `[WriteAggregate]` from first commit pays off at dispatch-test time, (d) duplicate-projection pattern as a named modular-monolith primitive (S4 `ParticipantCreditCeiling`, S5 `PublishedListings`, M5's `BidderCreditView`), (e) `UseFastEventForwarding` + `IStartStream` cascade doesn't land in `tracked.Sent` synchronously (S5 blocker), (f) ADR-014 single-source-per-sibling discipline strengthened to unconditional at S6, (g) named-field-allow-list discipline in seed handlers (S6 verification finding)
   - **Technical debt and deferred items:** carry-forward table with Item / Deferred in / Target columns per the M3 precedent. Candidates: ADR 007 Gate 4 (re-deferred at S1 to M5-S1; M5-S1 has already shipped — record the disposition update if M5-S1's retro resolved or re-deferred); the M3 fixture `ListingWithdrawn` shortcut (kept per M4-D6); defensive pre-filtering at `StartSession` time (M4 milestone doc §3 post-MVP hardening); `StartSession` filtering of listings withdrawn since attach (same §3); Operations BC `SessionCatalog` view (named in `SessionCreated` contract docstring as candidate post-M5); Auctions-side OQ3 Path α terminal observation (Listings-side pinned at S6, Auctions-side unobserved); narrative 001 Moment 3 `defer → green` finding (this slice — see item 4 below); bid-increment helper still at two co-located copies (threshold of three uncrossed across all of M3/M4)
   - **What M5/M6 should know:** 3-5 sentences. State of the codebase at M4 close (154 tests, 7 BCs with PostgreSQL stores per ADR 011, six cross-BC integration hops). What M5 inherits — already known since M5 shipped during the M4 pause; this section addresses M6 specifically. What M6 adds: real authentication (lifting `[AllowAnonymous]`), HTTP endpoint surface for Auctions commands (Proxy bid registration, Session create/attach/start, Selling withdrawal), frontend rendering of the 21-field `CatalogListingView`, Operations BC dashboards. What is stable and ready to build on vs. what is flagged fragile

2. **Skill consolidation review — null-call audit.** Walk the M4-S1 through M4-S6 retros and confirm each session's skill-file updates landed inline:
   - **M4-S3:** `wolverine-sagas.md` gained §"Composite-Key Correlation — the Dispatcher Pattern" and §"Multiple Handlers + MultipleHandlerBehavior.Separated — Send, Don't Invoke" (commit `67b2252`) ✅
   - **M4-S4:** `wolverine-sagas.md` gained §"Saga-to-Saga Cascades — Eager / Single-Cycle Under SendMessageAndWaitAsync" (commit `01d5c12`) ✅
   - **M4-S6:** `marten-projections.md` §"View Extension Across Milestones" extended with the third application + new §"Status-Preservation Guards" subsection authored fresh (commit `7aebe37`) ✅
   - **M4-S1, M4-S2, M4-S5:** retros explicitly recorded no skill append needed (S1 was foundation-decisions only; S2 inherited M3 patterns; S5 retro §"Skill append discipline" called out that nothing new surfaced beyond what M5-S3 PendingSettlement covered)

   The audit deliverable is a **paragraph in the M4 retro's "Skill files" subsection** (under "Key learnings" or as its own section per the M3 precedent), naming the three inline-folded updates and the three no-append calls. If the audit surfaces a gap (a finding from a retro that should have been folded but wasn't), flag it and **stop the retro** — do not silently bolt a skill commit into this slice. The disposition is either "fold-now in S7 as commit 1" or "fold in a follow-up M4-bonus or M5-S7 prompt" depending on the gap's shape.

3. **Operational smoke test — Aspire dashboard verification at M4 close.** Per M4-S6 retro §"What M4-S7 should know" §"Aspire / Rabbit operational posture" and the M3-S7 precedent. Steps:
   - Start `dotnet run --project src/CritterBids.AppHost --launch-profile http`
   - Open the Aspire dashboard at `http://localhost:15237` and confirm the `critterbids` Docker Compose project label grouping in Docker Desktop
   - Confirm the RabbitMQ management UI is reachable (port exposed in M4-S1 per milestone doc §1)
   - Confirm all six cross-BC integration queues from the milestone doc §Appendix are visible with their bindings: `selling-participants-events`, `listings-selling-events`, `auctions-selling-events`, `listings-auctions-events`, `settlement-selling-events`, `settlement-auctions-events`, `settlement-participants-events`, `auctions-participants-events`, `listings-settlement-events`. Note: the M5-S5 + M5-S6 work added queues beyond the M4 §Appendix scope; record all visible queues but flag any M4-promised queue that is missing
   - Confirm all M4-specific publish bindings are visible: `ListingWithdrawn` → 3 queues (M4-S2); the Session trio → `listings-auctions-events` (M4-S5); `ParticipantSessionStarted` → `auctions-participants-events` (M4-S4)
   - Record the outcome in the M4 retro under a "M4 operational posture at close" section — pass / partial / fail with timestamp and any anomalies. **Anomalies flagged in the retro, not fixed in this slice.**

4. **Narrative 001 Moment 3 `defer → green` disposition.** The lived M4-S5 + M4-S6 surfaces shipped what Moment 3 narrated as forward-spec. The lived class topology (`AuctionsSessionHandler` + `SellingListingWithdrawnHandler`, split per Sub-Option A) differs from the narrated single `SessionMembershipHandler`. Per Open Question 1, S7 decides:
   - **Path A:** amend `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 inline at this commit with a one-line note that the implementation lives in two source-split classes per ADR-014 Sub-Option A
   - **Path B:** stub a Phase 2.5 follow-up prompt at `docs/prompts/implementations/phase2-5-narrative-001-moment-3-flash-cascade-audit.md` (per the narrative session's `code-update` finding precedent) and record the deferral in the M4 retro

   Recommend Path B. Either way, file `docs/narratives/001-findings.md` gets a new finding entry routed `narrative-update` (or `code-update` if the divergence is judged a lived-code-vs-design mismatch rather than a documentation drift).

5. **No updates to `CLAUDE.md`.** Per the M3-S7 precedent — the modular monolith rules, convention list, and skill invocation table are unchanged by this session. If the consolidation review reveals a `CLAUDE.md`-level convention worth pinning (e.g. "every new `CatalogListingView` field adds a paired preservation line in `ListingPublishedHandler`" — surfaced at M4-S6), flag for a follow-up docs slice; do not edit `CLAUDE.md` in this slice.

6. **No `CURRENT-CYCLE.md` or status-tracker file.** Per the project memory entry `feedback_no_cycle_vocabulary.md`, CritterBids has no `CURRENT-CYCLE.md`. The M4 retrospective itself is the status tracker. This item exists to prevent the M3-S7 template (which already corrected the M2-S8 reference) from being inverted by accident.

## Explicitly out of scope

- **Any `.cs` file modification.** Not production code, not test code, not even a typo fix in a comment. Code is frozen at M4-S6 close (commit `32d6e70`). If a finding during the consolidation review reveals a code-level issue, **flag in the retro** and park it for a follow-up slice.
- **Any `.csproj`, `.slnx`, `Directory.Packages.props`, `global.json`, or `nuget.config` edit.** Build configuration is frozen.
- **Any `Program.cs` edit.** Queue wiring, handler registration, and transport configuration are frozen at S6 close per PR #39 (`32d6e70`). The operational smoke test is **verification, not modification** — if the smoke test fails, flag and stop.
- **Any new test.** The 154-test baseline is the closing count for M4. No `+1 for the Aspire verification` — operational smoke tests live in the retro as prose, not in the test suite.
- **Authoring or amending any ADR.** ADR-014's amendment landed at M4-S6; M4-S7 confirms status (Accepted) but does not edit the ADR. If the M4 milestone retrospective surfaces an ADR candidate (e.g. a duplicate-projection-pattern ADR — surfaced repeatedly across S4 `ParticipantCreditCeiling`, S5 `PublishedListings`, M5 `BidderCreditView` — but never authored), flag with a proposed number (next available) and a one-paragraph framing in the retro. Do not author the ADR.
- **Rewriting existing skill-file sections.** The consolidation review's null-call audit either confirms inline-folded updates (no edit) or surfaces a gap (handled per item 2's disposition rules). It does not authorize silent rewrites.
- **Authoring narrative 002 / 003 / 004 / 005.** The narrative-002 candidate (Auctions-BC seller- or operator-perspective) is the recommended next narrative per narrative 001's session-close retro, but its authoring is a separate session — not bolted into M4 close.
- **Any update to `CLAUDE.md`.** Per item 5.
- **Milestone doc retroactive edits.** `docs/milestones/M4-auctions-bc-completion.md` is the plan; the retro is the actual. Do not edit the milestone doc to match the retro. Plan vs. actual divergence is part of the retro's content.
- **M5 retro updates or M6 planning.** M5 already shipped during the M4 pause — its retro is closed. M6 planning is its own milestone-doc session.
- **Settlement / Obligations / Relay / Operations BC work.** All remain deferred or shipped-in-M5; the retro's "Technical debt and deferred items" section **lists** the post-M5 candidates; it does not **schedule** them.
- **Running `dotnet test` or `dotnet build`.** The baseline is clean; running either burns cycles without changing state. One exception: a targeted `dotnet build` after a documentation edit is permissible if and only if an embedded code block in a retro or skill section raises doubt about its compilability. Record the exception in the retro.
- **Local-hygiene work the user flagged in prior retros** (pruning stale M4-S5 and M4-S6 squash-merged branches). Operator-confirmation-required; not S7's scope.

## Conventions to pin or follow

No new behavioural conventions are introduced. Three M4-established disciplines are reinforced:

- **Inline skill-fold discipline is the M4 default; bulk passes are the exception.** M3-S7 ran an all-with-citations bulk pass because S1-S6 had accumulated findings without inline folds. M4-S3, M4-S4, and M4-S6 all folded inline. The S7 retro reinforces this: a docs-only close-out session should be **a null-call audit**, not a bulk pass, when the implementation slices have done their skill-fold work inline. If a future milestone's S7-equivalent finds itself needing a bulk pass, that's a signal earlier slices skipped their skill-fold discipline.

- **Frozen-file discipline across milestone close.** M3-S7 established the rule (M2-S7's `ListingPublishedHandler.cs` byte-frozen across M3-S6). M4-S7 extends: `src/` is byte-frozen for the duration of M4-S7. Production code is closed at the prior implementation slice's PR; close-out sessions do not modify it.

- **Status-tracker is the retrospective, not a separate file.** Per the M3-S7 correction of M2-S8's obsolete `CURRENT-CYCLE.md` reference (now codified in memory entry `feedback_no_cycle_vocabulary.md`). The M4 retrospective IS the M4-close state; nothing else updates as a consequence of S7 landing.

One new discipline surfaces in this session and is worth flagging in the retro:

- **Named-field-allow-list discipline in seed handlers becomes a `CLAUDE.md`-level convention candidate.** M4-S6's verification finding that `ListingPublishedHandler` uses an explicit per-field preservation block (not implicit record-`with` semantics) is now a load-bearing rule for every future M*-S* milestone that extends `CatalogListingView`. The rule lives in `ListingPublishedHandler.cs`'s XML comment block and in ADR-014 §"Decision" §5; whether it warrants promotion to `CLAUDE.md` is a question for the retro to decide (probably defer — `CLAUDE.md` is project-wide; the rule is Listings-BC specific until a second BC adopts a sibling-handler topology).

## Commit sequence (proposed)

Documentation-only session — the M3-S7 precedent's one-commit-per-deliverable rhythm. If the consolidation review (item 2) surfaces no gap (expected), and the narrative finding (item 4) is dispositioned Path B (recommend), the slice lands in **one commit**:

1. `docs: write M4 milestone retrospective` — item 1, including the consolidation-review audit paragraph (item 2 result), the operational smoke-test result paragraph (item 3 outcome), and the narrative finding's disposition paragraph (item 4 Path A or Path B). Retro lands at one commit because S7's other items produce retro content rather than separate commits.

If item 2's audit surfaces a skill-fold gap and the disposition is "fold-now," the slice lands in two commits:

1. `docs(skills): {target}.md — {gap finding shape}` — the consolidation fix
2. `docs: write M4 milestone retrospective` — item 1, including the audit's gap-resolution paragraph

If item 4's narrative finding is dispositioned Path A, the slice lands in two commits:

1. `docs(narratives): note Sub-Option A Flash cascade topology in Moment 3` — narrative 001 amendment + finding entry in `001-findings.md`
2. `docs: write M4 milestone retrospective` — item 1

Both-paths-surface case (skill gap + narrative Path A): three commits. The retro lands last in all paths so it accurately reflects what was folded in this slice.

## Acceptance criteria

- [ ] `docs/retrospectives/M4-auctions-bc-completion-retrospective.md` exists and contains all required sections from item 1
- [ ] The M4 retro's exit-criteria table walks each criterion from `docs/milestones/M4-auctions-bc-completion.md` §1 with ✅ / ⚠️ / ❌ and a one-line rationale; the "0 warnings" reconciliation is recorded explicitly
- [ ] The M4 retro's session-by-session table covers S1 → S7 (no splits in M4 — confirm at retro authoring time against the six implementation retros); each row's "Notable deviations" column is one line per the M3 precedent
- [ ] The M4 retro includes the operational smoke-test result (item 3) under a named section; outcome stated as pass / partial / fail with timestamp and any anomalies
- [ ] The M4 retro's "What M5/M6 should know" section is 3-5 sentences focused on M6 (M5 already shipped); covers test count, BC count, integration hop count, what M6 adds (auth, HTTP, frontend), what is stable vs. flagged fragile
- [ ] The M4 retro's "Key decisions / ADRs" section names M4-D1 / M4-D2 / M4-D3 / M4-D4 / M4-D5 / M4-D6 + ADR 007 Gate 4 + ADR-014 amendment with one-line rationales; any ADR candidate surfaced (e.g. duplicate-projection pattern) flagged with proposed number
- [ ] The M4 retro includes a skill-consolidation paragraph (item 2) naming the three inline-folded updates (S3 `67b2252`, S4 `01d5c12`, S6 `7aebe37`) and the three no-append calls (S1, S2, S5)
- [ ] The M4 retro records the narrative 001 Moment 3 disposition (item 4 — Path A inline amendment or Path B follow-up stub)
- [ ] If item 4 lands as Path A: `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 gains a one-line note about the lived class topology + `docs/narratives/001-findings.md` gains a new finding entry routed `narrative-update`
- [ ] If item 4 lands as Path B: `docs/prompts/implementations/phase2-5-narrative-001-moment-3-flash-cascade-audit.md` exists as a stub follow-up prompt + the M4 retro records the deferral
- [ ] `src/` byte-level diff vs. M4-S6 close (`32d6e70`): **none**
- [ ] `tests/` byte-level diff vs. M4-S6 close: **none**
- [ ] `Program.cs` byte-level diff vs. M4-S6 close: **none**
- [ ] `CLAUDE.md` byte-level diff vs. M4-S6 close: **none**
- [ ] `docs/milestones/M4-auctions-bc-completion.md` byte-level diff vs. M4-S6 close: **none**
- [ ] `docs/decisions/014-cross-bc-read-model-extension-shape.md` byte-level diff vs. M4-S6 close: **none** (status remains Accepted; the M4-S7 work confirms, does not amend)
- [ ] No `CURRENT-CYCLE.md` file created (per item 6 and memory entry `feedback_no_cycle_vocabulary.md`)
- [ ] Commit count: 1, 2, or 3 depending on the paths chosen in items 2 and 4; no squashing, no amending
- [ ] `dotnet build` and `dotnet test` were **not** run as a baseline check (documentation-only session); any targeted exception recorded in the retro with rationale

## Retrospective gate (REQUIRED)

The M4 milestone retrospective in item 1 is the capstone of M4 and the final commit of the PR (regardless of whether one, two, or three commits land per the commit-sequence variants). Gate condition: the retro commits **only after** any conditional commits (item 2 skill fix + item 4 narrative amendment) have landed with their full content. The retro reflects what was folded in this slice; if the conditional commits don't ride this PR, the retro records their deferral.

Retrospective content requirements (in addition to the structural items in the Acceptance Criteria above):

- **Plan-vs-actual reconciliation table for test counts.** M4 milestone doc §7 projected ~118 tests at M4 close; actual is 154. The +36 delta is mostly Settlement BC (M5 shipped during the M4 pause, landing 25 Settlement tests + auxiliary Selling/Auctions test growth) plus M4's own Listings catalog extension (+6 at S6) and saga test growth across S3/S4. Walk the arithmetic explicitly so M5's planning hindsight is on the durable record.
- **Sub-Option A future-application clause re-confirmation.** ADR-014's strengthened §"Decision" §1 (single-source-per-sibling, unconditional) is the M4-close rule. The retro restates the rule and names the candidate future surfaces where it might bind: Obligations / Relay / Operations status-field extensions in post-M5 work.
- **Skill-fold discipline at M4 close vs. M3 close.** The M3 retro section observed that the M3 close required a six-finding bulk pass (M3-S7). The M4 retro records the opposite outcome: zero accumulated findings at M4 close. Hypothesis to record: per-session skill-fold discipline was tightened across M3-S7 → M4-S1 such that every implementation session either folds inline or makes an explicit null call. Validate against the six M4 implementation retros.
- **Named-field-allow-list discipline as future-`CLAUDE.md` candidate.** Record the disposition (defer / promote / no-op). Recommend defer — too BC-specific to merit `CLAUDE.md` promotion until a second BC adopts a sibling-handler topology.
- **Operational smoke-test outcome.** Pass / partial / fail. If failed: which queue or binding is missing, the proposed follow-up slice number, and whether the failure blocks M4 close PR merge or rides it as a retro-flagged anomaly. Inherit the M3-S7 §"Open question 5" decision rule (a queue missing entirely is a regression worth blocking; partial / config-drift anomalies ride the retro).
- **Auctions-side OQ3 Path α disposition.** Per Open Question 2 — defer or pin. Recommend defer; record the named trigger (M6 frontend ship surfaces the bidder-facing terminal, which makes the Auctions-side saga store behaviour observable) and the named owner.

## Open questions (pre-mortems — flag, do not guess)

1. **Narrative 001 Moment 3 finding — Path A (inline amendment) vs Path B (Phase 2.5 stub).** The M4-S6 retro named this as a candidate finding. Per ADR 016 Phase 2 discipline, the session that **surfaces** a `code-update` finding does not **implement** the fix; for `narrative-update` findings the discipline is the same (separation of audit-and-fix from author-and-ship). Two paths:
   - **Path A:** amend `docs/narratives/001-bidder-wins-flash-auction.md` Moment 3 inline in this slice with a one-line note about the lived two-class topology
   - **Path B:** stub `docs/prompts/implementations/phase2-5-narrative-001-moment-3-flash-cascade-audit.md` and defer

   **Recommend Path B** — consistent with how narrative 001's session-close handled its own `code-update` findings (Phase 2.5 stub at `phase2-5-extension-calculation-fix.md`). Path A is only correct if S7's scope explicitly admits narrative authoring, which it doesn't (item 1's primary deliverable is the M4 retro). Record the finding routing in `001-findings.md` regardless of which path lands.

2. **Auctions-side OQ3 Path α terminal observation — defer or pin?** M4-S6 pinned the Listings-side terminal (Path 3); the Auctions-side terminal (Path 1 or Path 2 from the M4-S5 retro) is unobserved. Two paths:
   - **Path A:** defer to a named trigger. Recommend M6 frontend ship — when the bidder-facing UI renders the catalog post-fan-out, the lived UX makes the Auctions-side terminal observable in user-facing prose ("the withdrawn listing never appears as Open"). If the Listings-side guard is the only thing protecting that, the Auctions-side observation might never need to land
   - **Path B:** pin now by stubbing an Auctions-side composition test prompt for post-M4

   **Recommend Path A.** S7 is docs-only; the Auctions-side observation requires either authoring a new test (out of scope) or running the lived dispatch path with telemetry (operational, not documentation). Defer-with-named-trigger is the right shape for a question that may resolve itself when M6 lands.

3. **ADR candidate review — duplicate-projection pattern as ADR 015?** Three lived applications at M4 close: `Settlement.BidderCreditView` (M5-S5), `Auctions.ParticipantCreditCeiling` (M4-S4), `Auctions.PublishedListings` (M4-S5). The M4 milestone doc §8 M4-D4 row explicitly said "Duplicate projections across BCs are a named modular-monolith pattern, so no ADR trigger." Three lived applications is the same threshold that earned ADR-014 its body; should it earn the duplicate-projection pattern its own ADR? Two paths:
   - **Path A:** flag as ADR candidate (proposed number ADR 015), one-paragraph framing in the M4 retro, deferred to a follow-up session
   - **Path B:** record the three lived applications in the M4 retro's "Key learnings" section as documented pattern (per the M4-S5 retro's note that the pattern doesn't need its own ADR), close the question

   **Recommend Path A.** The threshold-of-three is symmetric with ADR-014's authoring rationale. M4-S5 retro deferred the call to M4 retro, which is this slice; the deferral has reached its trigger. Flag, do not author — keep the discipline consistent with M3-S7's ADR-candidate-review rule.

4. **Plan-vs-actual session count — M4 ran exactly 7 sessions with no splits, but the milestone doc §9 pre-drafted S4b, S5b, and S6b slots. Should the retro reflect on why none of the splits were used?** Three pre-drafted slots, zero used; this is the inverse of M3's two-split outcome (S4b + S5b actually fired). Two paths:
   - **Path A:** the retro notes the zero-split outcome briefly as a session-sizing observation under "Key learnings — cross-session patterns"
   - **Path B:** a dedicated retro section reflects on why pre-drafted splits went unused (better milestone-doc sizing? saga / aggregate scope was more contained than expected? prompt discipline tightened?) — useful input for M6+ sizing exercises

   **Recommend Path B if the answer is non-obvious and would inform M6 planning; Path A if the answer is essentially "the milestone doc oversized M4 by one session per risk node, and that's a feature not a bug."** The retro authoring should make the call after walking the six implementation retros — if the unused-split pattern surfaces a hypothesis worth recording, dedicated section; if not, one paragraph.

5. **Skill-consolidation audit — does the M5-S6 retro's incorrect claim about a §"Status-Preservation Guards" subsection in `marten-projections.md` (called out in M4-S6 retro) warrant a correction to the M5-S6 retro file?** Two paths:
   - **Path A:** correct the M5-S6 retro inline — change "the M5-S6 amendment landed all three subsections" to "the M5-S6 amendment landed two subsections; the §'Status-Preservation Guards' subsection was authored fresh at M4-S6"
   - **Path B:** leave the M5-S6 retro byte-frozen — retros are snapshots-in-time, not living docs; the correction lives in the M4-S6 retro (already in place) and is durable enough

   **Recommend Path B.** Retros are historical records; correcting them in place breaks their snapshot semantics. The M4-S6 retro's "Decisions inheriting forward" §"M5-S6 status-preservation-guards subsection of `marten-projections.md` did not actually exist" already absorbs the correction durably. Do not edit M5-S6.

## Session sizing notes

- **S7 is the smallest M4 session by design** — docs-only, one retro, optional one or two conditional commits.
- **Scope ceiling: 1-3 commits + 1 new retro file + (conditional) 1 narrative amendment or 1 stub prompt.** Well under M3-S7's ~5-7 doc files (which included a 4-file skill bulk pass).
- **No split slot pre-drafted.** S7 has no S7b. If S7 overflows its budget, the residual lands flagged for a post-M4 docs follow-up. The most likely overflow surface is item 1's retro authoring (six implementation retros to walk + a non-trivial plan-vs-actual reconciliation) — if it goes long, the audit + smoke test + narrative disposition items can ride a shorter commit cycle while the retro itself takes the bulk of the session time.
- **Operator-confirmation-required local hygiene** (pruning stale `m4-s5-session-aggregate`, `m4-s6-listings-catalog-session-and-withdrawn`, and `m4-s7-retrospective-skills-m4-close` branches after their squash-merges) is **not** S7 scope.

## Document history

- **v0.1** (2026-05-21): Authored at the close of M4-S6 per the retro's "What M4-S7 should know" handoff payload and the M3-S7 close-out-session precedent. The five Open Questions are framed by S6's lived discoveries (narrative finding, Auctions-side OQ3 Path α observation gap, named-field-allow-list discipline, the M5-S6 retro correction, the duplicate-projection pattern threshold). M4 ran exactly 7 sessions with no splits — the smallest milestone arc since M1 — which is itself a candidate retro observation per Open Question 4.
