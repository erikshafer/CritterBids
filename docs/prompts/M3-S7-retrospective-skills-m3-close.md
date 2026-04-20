# M3-S7: Skill Bulk Pass + M3 Retrospective — M3 Close

**Milestone:** M3 — Auctions BC
**Slice:** S7 of 9 (final M3 session — M3 close; follows S6 which landed the Listings catalog auction-status extension)
**Agent:** @PSA
**Estimated scope:** one PR; **documentation-only**; ~5–7 doc files touched; zero `.cs` / `.csproj` / `.slnx` / `Program.cs` diff
**Baseline:** 86 tests passing (1 Api + 1 Contracts + 11 Listings + 6 Participants + 35 Auctions + 32 Selling) · `dotnet build CritterBids.slnx` — 0 errors, 0 warnings · M3-S6 closed. End-to-end demo path from `ParticipantSessionStarted` (M1) through `ListingSold` / `ListingPassed` (M3) runs across RabbitMQ via five integration hops. `CatalogListingView` has 18 fields (8 M2-frozen + 10 M3-additive). `src/CritterBids.Auctions/` is byte-frozen vs S5b close; `ListingPublishedHandler.cs` is byte-frozen vs M2-S7 close.

---

## Goal

Close M3 with three carried-forward deliverables from M3-S6's retro §"What M3-S7 should know":

1. The deferred **skill bulk pass** — six accumulated first-use findings folded into three skill files in one atomic, all-with-citations pass (partial passes are not acceptable per S5b's discipline): four saga findings from S4b / S5 / S5b into `wolverine-sagas.md`, one tolerant-upsert primitive from S6 into `marten-projections.md`, one cross-BC handler-isolation pattern from S6 into `critter-stack-testing-patterns.md`.
2. The deferred **M3-D2 projection-extension pattern doc** — one view per logical entity, handlers per event-source BC, additive field growth across milestones, grounded in the M2-S7 base + M3-S6 extension precedent — appended to `marten-projections.md` (Path A per M3-S6 retro §"What M3-S7 should know" item 3).
3. The **M3 milestone retrospective** — capstone document for sessions S1 → S6 inclusive, authored against the exit criteria in `docs/milestones/M3-auctions-bc.md` §1, using the M1 and M2 milestone retrospective files as the structural precedent.

The session also performs one operational verification — the `listings-auctions-events` queue against the Aspire dashboard at `http://localhost:15237` under the `critterbids` Docker Compose project label — and records the result in the retro per M3-S6 retro §"What M3-S7 should know" item 6. No production code changes. No new tests. No `Program.cs` diff.

---

## This session is documentation-only

Do not modify any `.cs` file, `.csproj` file, `.slnx` file, `Program.cs`, or any test file. Do not run `dotnet test` — the 86-test baseline is already green. Do not run `dotnet build` unless a skill-file edit touches an embedded code block whose compile-ability warrants a spot sanity check. If the operational smoke test in item 4 surfaces a wiring defect (e.g. the queue is not visible in the Aspire dashboard), **flag and stop** — do not hot-patch `Program.cs` in this slice.

If you find yourself about to write or edit production code, stop and re-read this prompt. S6 landed the last M3 production change; the build is clean at 86 green. M3 is closing out, not opening new technical territory.

---

## Context to load

- `docs/milestones/M3-auctions-bc.md` — §1 exit criteria (walk each for the milestone retro's exit-criteria table), §7 test counts for M3-close totals, §9 session breakdown (S1 → S7 timeline for the session-by-session summary), Appendix cross-BC integration map
- `docs/retrospectives/M3-S6-listings-catalog-auction-status-retrospective.md` — authoritative source for S7's carry-forwards. Specifically §"What M3-S7 should know" items 1, 2, 3, 6 (skill-pass scope, catalog field inventory, M3-D2 call, operational queue posture); §"Key learnings" items 1 and 2 (cross-BC handler isolation, `LoadAsync ?? new` tolerant upsert); §"S6-4" (root cause narrative for `ListingsBcDiscoveryExclusion`)
- `docs/retrospectives/M3-S4b-buy-now-retrospective.md` + `docs/retrospectives/M3-S5-auction-closing-saga-skeleton-retrospective.md` + `docs/retrospectives/M3-S5b-auction-closing-saga-terminal-paths-retrospective.md` — source material for the four saga findings. The S5b retro explicitly names its three "surprises" plus a fourth deferred finding (scoped `IMessageBus` resolution); citations in the source text (pristine-repo file paths on `SagaChain.cs`, etc.) are the grounding the bulk-pass append must reproduce
- `docs/retrospectives/M1-retrospective.md` + `docs/retrospectives/M2-listings-pipeline-retrospective.md` — structural precedent for the M3 milestone retro. Match the section order (Exit Criteria Status → Session Timeline → Key Decisions → Cross-BC Integration Map → Key Learnings → Technical Debt / Deferred → What M4 Should Know)
- `docs/skills/wolverine-sagas.md`, `docs/skills/marten-projections.md`, `docs/skills/critter-stack-testing-patterns.md` — files that receive the bulk-pass append + the M3-D2 section. Read each before editing so the append respects the existing section order and density
- `docs/retrospectives/M2-S8-retrospective-skills-m2-close.md` *(prompt)* at `docs/prompts/M2-S8-retrospective-skills-m2-close.md` — the structural precedent for a docs-only close-out session (one commit per deliverable, session-close checklist, no build/test run)
- `docs/prompts/README.md` — specifically rule #4 (skill files own conventions — the M3-D2 append must define the projection-extension pattern in full, not restate it from the prompt)

## In scope (numbered)

1. **`docs/skills/wolverine-sagas.md` — bulk append of four saga findings.** All four land in one commit, not four. Order by where they most naturally fit inside the existing document (not by session-number order). The four findings:
   - **`NotFound` named-method convention on sagas.** Source: S5b retro §"Surprise 1" (lines 280, 204, 207 of the retro) — Wolverine's `SagaChain` codegen routes the "saga not found" branch to a static `NotFound` method matching the message signature if one exists, bypassing `AssertSagaStateExistsFrame`. Citation: `C:\Code\JasperFx\wolverine\src\Wolverine\Persistence\Sagas\SagaChain.cs:24,354-366` (per S5b). Target audience: saga authors who need to silently no-op on a stale scheduled-message delivery rather than throw `UnknownSagaException`.
   - **Saga state minimality — re-read pattern for emission.** Source: S5b retro §"Surprise 2" (line 281) and the referenced finding in S5 — if outcome events need fields the saga does not store in state, re-read the aggregate stream (or equivalent source of truth) inside the handler rather than expand saga state to carry them. Citation grounding expected from the S5 retro's emission-path discussion plus pristine-repo saga examples at `C:\Code\JasperFx\wolverine\` if applicable.
   - **`tracked.NoRoutes` vs `tracked.Sent` in test harness.** Source: S5b retro §"Surprise 3" (line 282) and the pre-existing memory entry `feedback_wolverine_outbox_tracking.md` — `tracked.Sent.MessagesOf<T>()` returns 0 in integration tests unless an `opts.Publish` routing rule exists; without a route, cascades fall into `tracked.NoRoutes` instead. **Placement call:** the M3-S5b retro explicitly names `critter-stack-testing-patterns.md` as the natural home *"if anywhere"* (line 282) — route it there if content already exists on tracked-session assertions, otherwise `wolverine-sagas.md` is acceptable since outbox tracking is most often hit in saga tests. Record the placement rationale in the S7 retro.
   - **Scoped `IMessageBus` resolution in test harnesses.** Source: S4b retro (the fourth saga finding named in S5b retro §"What M3-S6 should know" §8 and reinforced in M3-S6 retro §"What M3-S7 should know" item 1). Locate the specific S4b narrative before authoring the append — the finding is about how `IMessageBus` must be resolved from an `IServiceScope` rather than the root provider in certain test paths, with a pristine-repo citation from the Wolverine source if applicable.

   The append respects the file's existing density principle (per `docs/skills/domain-event-conventions.md` format guidance): lead with code examples, 2–3 sentence explanations, no code-free prose paragraphs. Each finding carries its source citation inline — **no training-memory claims**.

2. **`docs/skills/marten-projections.md` — bulk append covering (a) the tolerant-upsert primitive and (b) the M3-D2 projection-extension pattern.** Two related but distinct sections, both landing in the same commit. Content:
   - **`LoadAsync ?? new` tolerant-upsert primitive.** Source: M3-S6 retro §"Key learnings" item 2 and §"S6-2". Marten's `IDocumentSession.LoadAsync<T>(id)` returns null on absence (signature `Task<T?>` — citation: `C:\Code\JasperFx\marten\src\Marten\IDocumentSession.cs` per S6 retro OQ4 table); coupling it with a record-init expression handles cross-queue arrival races in two lines, with no `Patch` and no separate insert/update branch. Include the minimal-fields constructor pattern (`new T { Id = message.SomeId }`) so a later arrival can fill in the rest via the same upsert shape. Citation grounding: S6 retro §"S6-2" has the full narrative.
   - **Projection-extension pattern (M3-D2).** Source: M3-S6 retro §"Conventions to pin or follow" section of the S6 prompt (lines 107–108) and §"What M3-S7 should know" item 3 (Path A resolution). Pattern framing to document: **one view per logical entity** (a listing has one catalog row across its entire lifecycle, not one per source-BC), **handlers per event-source BC** (`ListingPublishedHandler` for Selling-sourced, `AuctionStatusHandler` for Auctions-sourced, additional handlers per future BC), **additive field growth across milestones** (M2 publishes the base 8 fields; M3 adds 10 auction-status fields; future milestones add settlement / watchlist / operations dashboard fields without rewriting the view). Ground the pattern in the in-repo precedent: M2-S7 (`CatalogListingView.cs` base + `ListingPublishedHandler.cs`) → M3-S6 (10 additive fields + `AuctionStatusHandler.cs` sibling). Cite the specific files and their commit lineage. Note the sibling-class-for-extension discipline (OQ1 Path B in the S6 retro) as a corollary of the pattern — not a separate pattern.

   **Both sections are additive.** Do not rewrite existing sections. Do not change the file's table of contents or section ordering beyond the new-section insertions. If the existing file has a "Related skills" / cross-link section, fold the new sections' cross-references in without restructuring.

3. **`docs/skills/critter-stack-testing-patterns.md` — bulk append of the cross-BC handler-isolation pattern.** Source: M3-S6 retro §"S6-4" and §"Key learnings" item 1. The finding: `opts.Discovery.IncludeAssembly(...)` scans for handlers regardless of whether the assembly's `AddXyzModule()` was called; once a shared event type gains a second handler in an included assembly, `MultipleHandlerBehavior.Separated` surfaces the ambiguity as a sticky-handler `NoHandlerForEndpointException` on `Host.InvokeMessageAndWaitAsync`. **Remediation:** per-fixture `*BcDiscoveryExclusion` classes mirror the shape of `SellingBcDiscoveryExclusion` (already in repo) and drop handlers from foreign-BC assemblies that were only reachable because the owning module is unregistered in that fixture. Cite the two in-repo precedents: `SellingBcDiscoveryExclusion` (pre-S6) and `ListingsBcDiscoveryExclusion` (added in S6 commit 1514600). The verbatim error message from S6's blocker narrative is worth including as the symptom signature so future debuggers recognise it on sight.

   Also fold in the pre-existing memory entry `project_cross_bc_handler_isolation.md` (created around S6) as the upstream reference — the skill file is the durable home, the memory entry is the prompt-time hint. Do not duplicate content; the skill section is authoritative.

4. **Operational smoke test — `listings-auctions-events` queue against the Aspire dashboard.** Per M3-S6 retro §"What M3-S7 should know" item 6 this is the one outstanding operational verification before M3 close. Steps:
   - Start `dotnet run --project src/CritterBids.AppHost --launch-profile http` (per `CLAUDE.md` Quick Start)
   - Open the Aspire dashboard at `http://localhost:15237` and confirm the `critterbids` Docker Compose project label grouping in Docker Desktop
   - Confirm the `listings-auctions-events` queue is visible in the RabbitMQ management UI (accessed via the Aspire dashboard's RabbitMQ resource link) with both publish bindings (6 rules from S6 commit 0202006) and the listen binding active
   - Record the outcome in the M3 retro under a "M3 operational posture at close" section — pass/fail/partial, timestamp, any anomalies. **Anomalies are flagged in the retro, not fixed in this slice.** If the dashboard shows the queue missing entirely, that is an M3-close regression worth an explicit retro section and a follow-up slice number.

5. **`docs/retrospectives/M3-auctions-bc-retrospective.md`** — the M3 milestone retrospective, written last. Structure inherits from `docs/retrospectives/M2-listings-pipeline-retrospective.md` and `docs/retrospectives/M1-retrospective.md` (the two precedents). Required sections:
   - **Header block:** date (2026-04-20 or the commit-landing date), milestone, sessions span (S1 → S7), author
   - **Baseline vs. exit state:** test count progression (44 → 86 — walk the arc), build state, what the end-to-end demo does at M3 close that it didn't at M2 close
   - **Exit criteria status:** walk each criterion from `docs/milestones/M3-auctions-bc.md` §1 with ✅ / ⚠️ / ❌ and a one-line rationale. All criteria expected ✅ at M3 close; anything else is flagged
   - **Session-by-session summary table:** S1 (contracts + ADR 007 Gate 4), S2 (Auctions scaffold), S3 (BiddingOpened consumer), S4 (DCB PlaceBid — 15 scenarios), S4b (BuyNow — 4 scenarios; split from S4 per S4 retro), S5 (Auction Closing saga skeleton — 7 of 11 saga scenarios), S5b (saga terminal paths — 4 remaining scenarios; split from S5), S6 (Listings catalog extension), S7 (this session). Include Notable Deviations column — each cell one line per the M2 precedent
   - **Cross-BC integration map at M3 close:** reproduce or summarize §Appendix from the milestone doc with a ✅ confirmation per hop. Four integration hops live at M3 close (two from M2, two new M3 — per the milestone doc's diagram)
   - **Test count at M3 close:** table matching the milestone doc §7 Test count summary, with any deltas from the planned targets explained. Milestone doc projected ~81; actual 86 — the delta is worth explaining
   - **Key decisions / ADRs:** ADR 007 Gate 4 resolution, W002-7 (`BidRejected` stream placement) resolution, W002-9 (`BiddingOpened` payload completeness) resolution, M3-D1 (DCB concurrency soak) deferral with dated rationale, M3-D2 (projection-extension pattern) resolution (Path A — documented in `marten-projections.md` this session), any S4 / S5 / S6 ADR candidates that surfaced. Note: ADR 013 was authored outside M3 (frontend-core-stack); ADR numbering for any M3-surfaced ADR candidate starts from the next available number at commit time
   - **Key learnings — cross-session patterns:** top 5–7 learnings that apply across more than one session or that M4 should carry forward. Do not repeat session-local learnings already captured in individual retros. Focus on patterns, anti-patterns, and process observations that generalise. Candidates from the M3 arc: (a) DCB + saga interaction in practice, (b) scheduled-message cancel-and-reschedule under anti-snipe, (c) bus-only emission discipline for outcome events (ADR-level decision made inline in S5b OQ5), (d) session-split discipline (S4 → S4b, S5 → S5b — both clean splits, neither a failure), (e) cross-BC handler isolation via `*BcDiscoveryExclusion`, (f) projection-extension pattern as a reusable shape, (g) reference-doc citation discipline (established S5b, reinforced S6)
   - **Technical debt and deferred items:** M3-D1 (DCB concurrency soak), `ListingWithdrawn` Selling-side publisher (still deferred, fixture-synthetic only at M3 close), `ParticipantBidHistoryView` (W001-9 — not in M3 scope), Watchlist / `LotWatchAdded` / `LotWatchRemoved` (post-M3), any S-session-specific deferrals worth re-listing at milestone level. Use the M2 retro's table shape (Item / Deferred in / Target)
   - **What M4 should know:** 3–5 sentences. What state the codebase is in at M3 close (test count, BC count, integration hop count, known fragility areas). What M4 inherits: the DCB pattern is in production, the saga pattern is in production, the projection-extension pattern is documented. What M4 adds: Proxy Bid Manager, Session aggregate for flash auction format, the 11 §4 proxy scenarios and 7 §5 session scenarios from `002-scenarios.md`. What is stable and ready to build on vs. what is flagged fragile

6. **No updates to `CURRENT-CYCLE.md`.** Per the project memory entry `feedback_no_cycle_vocabulary.md`, CritterBids has no `CURRENT-CYCLE.md` — the most recent retrospective is the single source of session-close state. Do not create the file; do not invent the vocabulary. The M2-S8 prompt referenced `CURRENT-CYCLE.md` as its final act; that reference is obsolete per the memory entry. **This item exists to prevent the M2-S8 template from being copied uncritically.**

7. *(Conditional — S6 retro §"What M3-S7 should know" item 4 is skeptical this surfaces, but the slot exists)* **ADR candidate review.** If the skill bulk pass or the M3-D2 append surfaces a structural pattern that crosses ≥2 BCs and is not adequately captured by ADR 011 (All-Marten Pivot) / ADR 009 (Shared Marten Store), flag an ADR candidate in the M3 retro with a proposed number (next available after 013) and a one-paragraph framing. **Do not author the ADR in this slice** — milestone-close retro flags it for a follow-up session, consistent with the pattern set by ADR 010 (flagged in M2-S2 retro, authored in M2-S3). S6 retro assessed this as unlikely given the bulk-pass findings are implementation-level, not architectural.

## Explicitly out of scope

- **Any `.cs` file modification.** Not production code, not test code, not even a typo fix in a comment. Code is frozen at M3-S6 close. If a finding during the skill pass reveals a code-level issue (e.g. a handler that would benefit from the `LoadAsync ?? new` pattern but uses a different shape), **flag in the retro** and park it for a follow-up slice.
- **Any `.csproj`, `.slnx`, `Directory.Packages.props`, `global.json`, or `nuget.config` edit.** Build configuration is frozen.
- **Any `Program.cs` edit.** Queue wiring, handler registration, and transport configuration are frozen at S6 close per commit 0202006 (publish rules) and the existing M2-S7 listen-side wiring. The operational smoke test in item 4 is **verification, not modification** — if the smoke test fails, flag and stop.
- **Any new test.** The 86-test baseline is the closing count for M3. No `+1 for the Aspire verification` — smoke tests live in the retro as prose, not in the test suite.
- **Rewriting existing skill-file sections.** All three skill-file changes in items 1 / 2 / 3 are **additive appends**. If an existing section has a factual error that the bulk pass exposes, flag the correction in the retro rather than rewrite silently — the same discipline applied to contract payloads in M3-S6.
- **Authoring new ADRs.** Item 7 flags candidates; it does not author them. ADR 013 (frontend-core-stack) was authored outside M3 and is unrelated to M3 close; do not cross-reference it in the M3 retro beyond noting the numbering baseline for any M3-surfaced follow-up ADR candidate.
- **Any update to `CLAUDE.md`.** The modular monolith rules, convention list, and skill invocation table are unchanged by this session. If the skill bulk pass reveals a `CLAUDE.md`-level convention worth pinning (e.g. "per-fixture `*BcDiscoveryExclusion` is mandatory for any assembly whose handlers cross BCs"), flag for M4 or a follow-up docs slice — do not edit `CLAUDE.md` here.
- **Milestone doc retroactive edits.** `docs/milestones/M3-auctions-bc.md` is the plan; the retro is the actual. Do not edit the milestone doc to match the retro. Plan vs. actual divergence is part of the retro's content.
- **M4 planning.** The "What M4 should know" section in the retro is a short note, not a M4 milestone doc. `docs/milestones/M4-*.md` does not exist yet; its authoring is a separate session.
- **Watchlist, `ParticipantBidHistoryView`, `ListingWithdrawn` publisher, Settlement BC work.** All remain deferred per the milestone doc §3 non-goals. The retro's "Technical debt and deferred items" section **lists** these; it does not **schedule** them.
- **Running `dotnet test` or `dotnet build`.** The baseline is clean; running either burns cycles without changing state. One exception: a targeted `dotnet build` after a skill-file edit is permissible if and only if an embedded code block raises doubt about its compilability (e.g. references to types that have been renamed). Record the exception in the retro.
- **Any `git push` to `origin/main` beyond the standard PR flow.** Commit on a branch, open a PR, merge per the project's established process. M3 close does not bypass the PR discipline.

## Conventions to pin or follow

No new behavioural conventions are introduced. Three M3-established disciplines are reinforced:

- **Reference-doc citation discipline.** S5b established the rule, S6 reinforced it: every first-use claim about Wolverine / Marten / Alba / Polecat behaviour cites its source — AI Skills repo path, pristine local repo file, `CritterStackSamples` example, or Context7 library reference. Training-memory citations are insufficient. The bulk skill pass in items 1–3 must reproduce the citations from the source retros, not re-derive them from memory. If a cited path has changed (e.g. a Wolverine source file moved between versions), re-verify and update.
- **All-with-citations or none for skill bulk passes.** S5b retro §"Skill file — append not written (item 9)" established this: partial skill passes that leave accumulated findings undocumented create debt that grows faster than it is paid down. S7 lands all six findings (four saga + one projection + one testing) atomically in one PR, or lands none of them. If item 1 hits a blocker on one of the four saga findings (e.g. the S4b narrative for scoped `IMessageBus` resolution turns out to be thinner than expected), flag the blocker and **lift the whole pass out** to a follow-up — do not ship three findings and defer one.
- **Frozen-file discipline across milestone close.** M2-S7's `ListingPublishedHandler.cs` stayed byte-identical across M3-S6 via OQ1 Path B. S5b's `AuctionsModule.AddEventType<T>()` count (8) stayed fixed through S6. S7 extends the discipline: `src/` is byte-frozen for the duration of M3-S7. The rule is that once a bounded context is feature-complete for a milestone, close-out sessions do not modify its production code.

One new discipline surfaces in this session and is worth flagging in the retro:

- **Docs-only close-out sessions do not update a status tracker file.** Per memory entry `feedback_no_cycle_vocabulary.md`, CritterBids has no `CURRENT-CYCLE.md` equivalent. The retrospective itself is the status tracker. This is a direct correction of the M2-S8 template (which referenced `CURRENT-CYCLE.md` as its final act); that step is removed from the M3 template. Record the template correction in the S7 retro so future close-out prompts (M4-S-final, M5-S-final, etc.) do not re-introduce the obsolete reference.

## Commit sequence (proposed)

Documentation-only session — one commit per deliverable, per the M2-S8 precedent. The retro lands last, after the skill work has settled.

1. `docs(skills): bulk-fold M3 saga findings into wolverine-sagas.md` — item 1. Four saga findings with source citations; respects existing file density; placement of the `tracked.NoRoutes` finding (item 1 bullet 3) noted in the commit message if it landed in `critter-stack-testing-patterns.md` instead.
2. `docs(skills): document tolerant upsert and projection-extension pattern in marten-projections.md` — item 2. Two related sections in one commit: `LoadAsync ?? new` primitive + M3-D2 projection-extension pattern grounded in the M2-S7 → M3-S6 precedent.
3. `docs(skills): document cross-BC handler-isolation pattern in critter-stack-testing-patterns.md` — item 3. `*BcDiscoveryExclusion` pattern with both in-repo precedents cited and the verbatim `NoHandlerForEndpointException` signature for symptom recognition.
4. `docs: write M3 milestone retrospective` — item 5. Retro lands **after** all three skill-file commits so it can accurately reflect the shape of what was folded in.

If item 7 (ADR candidate review) surfaces a candidate, the retro names it and the commit sequence stays at four — the ADR itself is authored in a follow-up slice, not bolted into this PR.

The operational smoke test (item 4) produces no commit — its output is a retro paragraph in commit 4. If the smoke test fails with anything beyond a clean "✅ queue visible, 6 publish bindings, 1 listen binding," flag in the commit message and consider whether the retro should land behind a blocker flag.

## Acceptance criteria

- [ ] `docs/skills/wolverine-sagas.md` — extended with the four saga findings named in item 1, each with a source citation (pristine-repo file path, AI Skills repo path, or `CritterStackSamples` example); existing sections unchanged beyond insertion points
- [ ] `docs/skills/marten-projections.md` — extended with (a) the `LoadAsync ?? new` tolerant-upsert primitive and (b) the M3-D2 projection-extension pattern; both grounded in source citations (Marten pristine-repo path for `LoadAsync` semantics; in-repo file lineage for the pattern precedent)
- [ ] `docs/skills/critter-stack-testing-patterns.md` — extended with the `*BcDiscoveryExclusion` cross-BC handler-isolation pattern; both in-repo precedents cited; the verbatim `NoHandlerForEndpointException` signature included for symptom recognition
- [ ] All skill-file appends land in the three commits 1 / 2 / 3 of the commit sequence — no partial pass (per S5b's all-with-citations-or-none discipline)
- [ ] `docs/retrospectives/M3-auctions-bc-retrospective.md` exists and contains all required sections from item 5
- [ ] The M3 retro's exit-criteria table walks each criterion from `docs/milestones/M3-auctions-bc.md` §1 with ✅ / ⚠️ / ❌ and a one-line rationale
- [ ] The M3 retro's session-by-session table covers S1 → S7 (inclusive of both sub-splits S4b and S5b); each row's "Notable deviations" column is one line
- [ ] The M3 retro includes the operational smoke-test result (item 4) under a named section; outcome stated as pass / partial / fail with timestamp and any anomalies
- [ ] The M3 retro's "What M4 should know" section is 3–5 sentences covering (a) test count and BC count at close, (b) which patterns are production-stable (DCB, saga, projection-extension), (c) what M4 inherits and adds
- [ ] The M3 retro's "Key decisions / ADRs" section names ADR 007 Gate 4, W002-7, W002-9, M3-D1, M3-D2 resolutions with one-line rationales; ADR candidates from item 7 (if any) flagged with proposed number and one-paragraph framing
- [ ] `src/` byte-level diff vs. S6 close: **none**
- [ ] `tests/` byte-level diff vs. S6 close: **none**
- [ ] `Program.cs` byte-level diff vs. S6 close: **none**
- [ ] `CLAUDE.md` byte-level diff vs. S6 close: **none**
- [ ] `docs/milestones/M3-auctions-bc.md` byte-level diff vs. S6 close: **none**
- [ ] No `CURRENT-CYCLE.md` file created (per item 6 and memory entry `feedback_no_cycle_vocabulary.md`)
- [ ] Commit count: 4 (one per deliverable in the commit sequence); no squashing, no amending
- [ ] `dotnet build` and `dotnet test` were **not** run (documentation-only session); any targeted exception recorded in the retro with rationale

## Retrospective gate (REQUIRED)

The milestone retrospective in item 5 is the capstone of M3 and the final commit of the PR. Gate condition: the retro commits **only after** commits 1 / 2 / 3 have landed with their full content. If the skill bulk pass hits a blocker on any of the six findings, the retro does not land — per the all-with-citations-or-none discipline, a partial skill pass is not a basis for milestone close.

Retrospective content requirements (in addition to the structural items in the Acceptance Criteria above):

- **Per-finding citation index.** For each of the six skill-pass findings, the retro names the specific source retro section, the cited pristine-repo / AI-Skills / sample file path, and the target skill file section header where it landed. This is the durable audit trail for "where did this rule come from" questions in M4+.
- **M3-D2 Path resolution rationale.** Why Path A (document in `marten-projections.md`) and not Path B (document in `domain-event-conventions.md`). The S6 retro §"What M3-S7 should know" item 3 already flagged Path A as the structural fit; the retro restates the rationale for the durable record.
- **Operational smoke-test outcome.** Pass / partial / fail, with the Aspire dashboard screenshot-equivalent described in prose (queue name visible, publish bindings visible, listen binding visible) and any anomalies called out. If failed: which component was missing / misconfigured, and the proposed follow-up slice number.
- **What the "86-test baseline" means at M3 close.** One short section reconciling the milestone doc's §7 projection (~81) with the actual close (86). The +5 delta is explicable (Listings catalog extension landed 5 new scenario tests + 2 InlineData rows on the Passed theory + 1 BIN Fact = +7 test cases per S6 retro; baseline pre-S6 was 79, so 79 + 7 = 86). Document the arithmetic for the next milestone's sizing exercise.
- **Session-split retrospective.** S4 split into S4+S4b, S5 split into S5+S5b. Both splits were clean (pre-declared in the milestone doc §9 Session sizing notes as known split plans, not mid-flight panics). The retro reflects on whether the split points were correctly pre-identified, whether a third split could have been avoided with tighter prompting, and what M4 can learn about sizing the Proxy Bid Manager saga session (the most likely M4 split candidate).

## Open questions (pre-mortems — flag, do not guess)

1. **Placement of the `tracked.NoRoutes` finding — `wolverine-sagas.md` vs. `critter-stack-testing-patterns.md`?** S5b retro §"Surprise 3" (line 282) explicitly flags `critter-stack-testing-patterns.md` as the natural home *"if anywhere"* — language that reads as a soft recommendation rather than a firm placement. Two paths:
   - **Path A:** `wolverine-sagas.md` — groups with the three other saga findings; saga tests are where the symptom most often appears.
   - **Path B:** `critter-stack-testing-patterns.md` — matches S5b's explicit recommendation; couples the finding with other integration-test-fixture conventions.

   **Recommend Path B** — defers to S5b's explicit guidance, and the finding is fundamentally a test-harness discipline (routing rules required for `tracked.Sent` to populate), not a saga-pattern discipline. Saga tests are one context where it surfaces; the rule applies equally to any integration test asserting outbox sends. If Path B makes the `wolverine-sagas.md` bulk append look thinner (three findings vs. four), note that and proceed — the count is not load-bearing. Cite the placement call in the retro's per-finding citation index.

2. **Scope of the "scoped `IMessageBus` resolution" finding — is this documented enough in the S4b retro to ground a skill-file append?** S6 retro §"What M3-S7 should know" item 1 names it as one of the four saga findings, but does not include the S4b-specific narrative. Before authoring the append, read the S4b retro fully to confirm:
   - **Path A:** the finding is documented with a specific pristine-repo citation and a clear code-shape rule — append it as-is.
   - **Path B:** the S4b retro captures the symptom but not the pristine-repo citation — re-derive the citation from `C:\Code\JasperFx\wolverine\` source before appending (S5b discipline).
   - **Path C:** the S4b retro is thin on this finding and a skill-file append would require pattern re-derivation that exceeds the slice's docs-only scope — **flag and lift the whole skill pass out** per the all-with-citations-or-none rule, defer to a dedicated M4 skill-pass session.

   **Recommend Path B if the finding is real but under-cited; Path C if the finding is actually not captured in any M3 retro.** The decision belongs in the retro with a one-sentence rationale. Do not paper over a thin finding with generic pattern prose — skill files earn their density.

3. **M3-D2 append placement inside `marten-projections.md` — co-located with the tolerant-upsert primitive, or a separate top-level section?** Both are M3-S6-sourced, both are additive to the file, but they solve different problems (upsert shape vs. view-evolution pattern). Two paths:
   - **Path A:** co-locate under a new "M3 projection-extension patterns" parent section with two subsections.
   - **Path B:** two independent top-level sections — the tolerant-upsert primitive near the existing upsert guidance, the projection-extension pattern near the existing projection-design guidance.

   **Recommend Path B** — the two findings serve different audiences (handler authors vs. projection designers) and co-locating them forces a reader looking for one to read past the other. Flag if the existing file structure doesn't support two natural insertion points — in which case Path A is the fallback. Record the placement call in the per-finding citation index.

4. **Does the M3 retro need a standalone "ADR candidate review" section even if no candidates surface?** Item 7 frames the review as conditional. Two paths:
   - **Path A:** include the section with explicit "no candidates surfaced in this session" text and a one-sentence rationale.
   - **Path B:** omit the section entirely if no candidates surfaced — the "Key decisions / ADRs" section already covers the resolved candidates.

   **Recommend Path A** — the audit trail value of an explicit null call exceeds the ~20 words of overhead. Precedent: M2 retrospective includes "Technical debt and deferred items" explicitly even for items that could have been omitted. Consistency of retro shape across milestones aids skim-reading for M4+ planners.

5. **Aspire smoke-test anomaly threshold — what counts as a blocker?** Item 4 says "anomalies are flagged in the retro, not fixed in this slice" but is ambiguous about what threshold promotes an anomaly to a PR blocker. Two paths:
   - **Path A:** any anomaly is a retro paragraph; the retro still lands.
   - **Path B:** a "queue missing entirely" is a M3-close regression that blocks the PR; partial / config-drift anomalies land with the retro but trigger a follow-up slice.

   **Recommend Path B** — a missing queue would mean S6's commit 0202006 did not land correctly despite green tests (possible if the rabbitmq null-guard hid a config error in the test environment). Flagging as a PR blocker forces diagnosis before M3 close, rather than discovering the regression in M4-S1. If Path B triggers, the resolution is **not** a hot-patch in this slice — it is an explicit S7-followup or S8 slice with its own prompt. Record the decision criteria in the retro.

6. **S7 as "S7 of 9" vs. "S7 of 7" — session-count reconciliation.** The milestone doc §9 projects 7 sessions total (S1 → S7); actuals include two splits (S4b, S5b) making the realised count 9 implementation sessions. The retro's "Sessions" header can count either way.
   - **Path A:** "S1 → S7 inclusive of two mid-stream splits (9 sessions realised)" — counts by plan with a parenthetical for actuals.
   - **Path B:** "S1 → S7 with S4→S4b and S5→S5b splits" — counts by plan with inline split notation.
   - **Path C:** "9 sessions realised (S1, S2, S3, S4, S4b, S5, S5b, S6, S7)" — counts by actual with plan as subtext.

   **Recommend Path B** — reads cleanly, matches the M1 and M2 retro precedent (which list actual sessions including post-S2 and other unplanned inserts). Flag if Path C makes the session-by-session table cleaner to read. The count in the retro's header should be consistent with the count in the table; pick one and apply it uniformly.
