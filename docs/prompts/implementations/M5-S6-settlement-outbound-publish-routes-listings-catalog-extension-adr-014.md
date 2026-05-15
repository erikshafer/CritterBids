# M5-S6: Settlement Outbound Publish Routes + Listings Catalog `Settled` Status + ADR-014 Authoring + M5 Milestone Close

**Milestone:** M5 ([Settlement BC](../../milestones/M5-settlement-bc.md))
**Slice:** S6 of 6 (M5 milestone closer — Slice 6.3: Seller Payout Notification + Catalog Settled Status + M5 retrospective)
**Narrative:** [`docs/narratives/002-winner-clears-settlement.md`](../../narratives/002-winner-clears-settlement.md)
**Agent:** @PSA
**Estimated scope:** one PR; ~12 files added (prompt + ADR-014 body + `SettlementStatusHandler` + Listings tests file for settled-status + Relay-side stub or test fixture if Relay BC has not shipped + M5-S6 retro + M5 milestone retro + three doc-update touches + skill amendments site), ~6 files modified (`CatalogListingView.cs` if a `SettledAt` timestamp field lands, `ListingsModule.cs` for the new handler discovery, `Program.cs` for the two new RabbitMQ queue routes, `docs/decisions/README.md` for ADR-014 status flip, `docs/skills/marten-projections.md` §7 second-application callout, `docs/skills/wolverine-message-handlers.md` if a multi-source sibling pattern surfaces)

---

> **Prompt status: v0.1, authored 2026-05-15 at M5-S5 prompt time.** This prompt is intentionally written before M5-S5 ships. It captures the slice's scope **from the M5-S4 lived ground** plus the M5-S5 prompt's declared deliverables, knowing that S5's actual lived ground (BidderCreditView shape, ParticipantSessionStarted contract-promotion outcome, ADR-007 Gate 4 status, PaymentFailed publish-route deferral disposition, etc.) will require amendment to this document before S6 runs. **Update sites** are marked inline with the **🔄 S5 hand-off** annotation; each names what discovery from S5 would change here and how. A v0.2 amendment lands once M5-S5 retro is written, before the M5-S6 session opens.

---

## Goal

Close the Settlement BC milestone. **Workstream A — `SettlementCompleted` cross-BC publish route + Listings catalog `Settled` status:** wire the `listings-settlement-events` RabbitMQ queue route so `SettlementCompleted` reaches the Listings BC, then author a new `SettlementStatusHandler` sibling class in Listings that transitions `CatalogListingView.Status` from `"Sold"` to `"Settled"` on receipt. This is the **second lived application** of the M3-D2 Path A cross-BC read-model extension pattern (M3-S6's `AuctionStatusHandler` being the first; M4-S6's planned `SessionMembershipHandler` slipped when M4 paused after M4-S2). **Workstream B — `SellerPayoutIssued` outbound publish route + Relay-side stub or test fixture:** Relay BC has not shipped at M5 close, so the outbound publish route either (a) wires structurally to a `relay-settlement-events` queue with no consumer, (b) wires to a test-fixture-only subscriber that exercises the route, or (c) defers entirely to post-M5. Decision resolves at S6 open per Open Question Q1. **Workstream C — ADR-014 authoring:** the second lived Path A application from Workstream A is the evidence that triggers ADR-014's body authoring per the ADR README's reservation note ("Reserved in M4-S1. ADR body authored at M4-S6 alongside the second lived application of the M3-D2 Path A pattern"). M4-S6 has not shipped; M5-S6's `SettlementStatusHandler` becomes the natural second application. **Workstream D — M5 milestone retrospective:** S6 retro plus M5 milestone retro, both per the milestone exit criteria (`docs/milestones/M5-settlement-bc.md` §1).

S6 closes the Settlement BC milestone. The narrative 002 arc — the keyboard's QR-scan-to-settlement journey from M1 through M5 — runs end-to-end at M5 close: a participant scans a QR code (M1 Participants), a seller publishes a listing (M2 Selling), the Auctions BC opens bidding via Sessions (M3 — M4's Session aggregate still not shipped, see Concerns), `ListingSold` triggers Settlement (M5), the six-event financial stream produces `SellerPayoutIssued` and `SettlementCompleted`, and the Listings catalog reflects the final `Status: "Settled"` state. Real-time Relay broadcast is post-M5; the bidder-balance display is post-MVP; the operator dashboard surfacing of `PaymentFailed` is post-M5. None of those are M5-closing; the milestone's exit criteria are about backend completeness through the six-event stream's end-to-end consumer surface.

S6 walks in with the M5-S5 surface green (assumed; **🔄 S5 hand-off** — confirm test count, the three workstreams' acceptance criteria status, and any deferred items at session start). The Settlement saga emits `SettlementCompleted` and `SellerPayoutIssued` via `OutgoingMessages` (M5-S4); the local-in-process `PendingSettlementHandler` consumer transitions `PendingSettlement.Status = Consumed` (M5-S3); the `BidderCreditView` projection is debited via `WinnerCharged` (M5-S5). What does **not** yet exist: a cross-BC RabbitMQ publish route for either `SettlementCompleted` or `SellerPayoutIssued`. S6 wires the Listings consumer surface for `SettlementCompleted` and the Relay-or-test-fixture surface for `SellerPayoutIssued`.

This slice also closes **two M5-close-blocking documentation items** forwarded from M5-S5's retro (per the S5 prompt's "Documentation forwarding" section): (1) **ADR 007 Gate 4** — trigger fired at PR #25 (M5-S1); the lived disposition is "Settlement shipped on engine-default row IDs through M5-S5 without surfaced incident." S6 amends ADR-007 with the lived-fact close, or re-defers with a new dated rationale and a new specific trigger naming the next BC. (2) **W003 Phase 1 Part 7 `BidderCreditView` lazy-init posture** — if M5-S5 descoped the Participants contract promotion (Q2 of S5's prompt) and shipped lazy-init-on-`WinnerCharged` only, the M5 milestone retro records the `document-as-intentional` workshop-update lane finding against narrative 002 (the design-vs-implementation gap is acknowledged; no W003 amendment ships in M5). **🔄 S5 hand-off** — both items' specific dispositions confirm at S6 open.

`PaymentFailed`'s outbound publish route is **deferred to post-M5** per the M5-S5 prompt's out-of-scope section and per M5-S4 retro item 6 (Operations BC has not shipped; no consumer = no value to wire). The decision was made at S5 prompt time; S6 honors the deferral. If S5 surfaced a reason to flip this — e.g., Operations BC suddenly enters scope, or a downstream consumer needs the publish route for non-Operations reasons — **🔄 S5 hand-off** — S6 wires it; otherwise out of scope.

---

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M5-settlement-bc.md` §1 (Exit criteria) + §7 (Slice Breakdown) + §2 (Cross-BC wiring) | S6's deliverables — the slice row reads "Slice 6.3; `SellerPayoutIssued` integration-event publishing; Relay-side stub or test fixture if Relay BC has not yet shipped"; §1's exit criteria gate M5 close on "M5-S6 retrospective doc written" + "M5 retrospective doc written" + "ADR 007 Gate 4 honored"; §2's wiring table names the three new queue routes (`listings-settlement-events`, post-M5 `relay-settlement-events`, post-M5 `operations-settlement-events`) |
| `docs/retrospectives/M5-S5-settlement-failure-paths-bin-source-bidder-credit-view-retrospective.md` | **🔄 S5 hand-off — confirm exists at session open.** S5's retro is the most-recent lived ground; its "What M5-S6 should know" section is the authoritative S6 entry handoff. This v0.1 prompt is written before that retro exists; v0.2 amendment after retro write absorbs its findings |
| `docs/milestones/M4-auctions-bc-completion.md` §"ADR 014 authoring" subsection | Original ADR-014 plan — second application was M4-S6's `SessionMembershipHandler`. M4 paused after M4-S2; M5-S6's `SettlementStatusHandler` is the second application by chronological lived ground. The sub-question framing (one sibling per source BC vs one sibling per logical feature) still applies; the M5-S6 sibling is single-source (Settlement only), so the sub-question resolves to **Option A — one sibling per source BC** without ambiguity for this application |
| `docs/decisions/README.md` ADR-014 row | The reservation note: "ADR body authored at M4-S6 alongside the second lived application of the M3-D2 Path A pattern (Listings `CatalogListingView` extended with session-membership and withdrawn fields). Do not draft before M4-S6 — the lived second application is the evidence." With M4-S6 slipped, M5-S6's lived application is the evidence; the reservation's "M4-S6" pointer is amended to "M5-S6 (where the second lived application actually landed)" in this slice |
| `docs/decisions/007-uuid-strategy.md` §Status + Gate 4 paragraph | The ADR's current text shows "Re-Deferred (M4-S1)" with the trigger "M5-S1 (Settlement BC foundation decisions), named owner Erik for the JasperFx follow-up nudge." Trigger fired; ADR text unamended. S6 amends per the M5 retro's disposition |
| `docs/decisions/PARKED.md` | P-001 (Operations runbook) and P-002 (Demo-script runbook) — neither triggers at M5 close; recorded for completeness. Document remains unchanged |
| `src/CritterBids.Listings/AuctionStatusHandler.cs` + `src/CritterBids.Listings/ListingPublishedHandler.cs` + `src/CritterBids.Listings/CatalogListingView.cs` | The M3-S6 lived Path A precedent — two sibling handler classes (one per source BC, single-source each), one shared `CatalogListingView` document, `Status` string transitions `"Published" → "Open" → "Closed" → "Sold"/"Passed"`. M5-S6's `SettlementStatusHandler` is the third sibling and adds the `"Sold" → "Settled"` transition |
| `src/CritterBids.Api/Program.cs` (M5-S5-modified) | The RabbitMQ wiring host — S6 adds the `listings-settlement-events` outbound publish from Settlement and inbound consumption at Listings. **🔄 S5 hand-off** — the `settlement-participants-events` queue route from S5 is the precedent shape for the new routes |
| `docs/skills/marten-projections.md` §7 ("Cross-BC View Extension via Sibling Handlers") | The skill amendment site — second lived application reinforces the pattern; the §7 reference is a one-line touch noting `SettlementStatusHandler` as the second example, not a content expansion |
| `docs/narratives/002-winner-clears-settlement.md` Moment 5 (`SettlementCompleted` broadcast) | The narrative cross-reference for the end-to-end arc closure. Narrative 002 dramatizes the bidder-visible outcome; the Listings catalog `Status: "Settled"` transition is the offstage-but-load-bearing read-model state that supports any future "your settled listings" view. The narrative's Moment 5 does not require S6 amendment unless **🔄 S5 hand-off** surfaces a finding |

---

## In scope

### Workstream A — `SettlementCompleted` cross-BC publish route + Listings `SettlementStatusHandler`

**RabbitMQ route wiring (`src/CritterBids.Api/Program.cs`):**

- Add an outbound publish from the Settlement BC for `SettlementCompleted` to a new `listings-settlement-events` queue. The route shape mirrors M5-S3's `settlement-auctions-events` route in reverse direction (Settlement → Listings instead of Auctions → Settlement). Wolverine's `opts.PublishMessage<SettlementCompleted>().ToRabbitQueue("listings-settlement-events")` (or analogous shape per the Wolverine 5 API) is the registration site. Per the `wolverine-outbox-tracking` memory: a `Publish` routing rule is the pre-condition for `tracked.Sent` to surface the message in integration test assertions — the rule lands here, not skipped.
- Add the inbound `ListenToRabbitQueue("listings-settlement-events")` registration on the Listings BC consumer side. Per memory `project_wolverine_sticky_handler.md`: this is a sticky-handler registration; tests that disable external Wolverine transports must direct-invoke `SettlementStatusHandler.Handle(...)` rather than going through `IMessageBus.InvokeAsync(...)` to avoid `NoHandlerForEndpointException`.

**Handler authoring (`src/CritterBids.Listings/SettlementStatusHandler.cs`, new):**

- `public static class SettlementStatusHandler` mirroring `AuctionStatusHandler`'s shape exactly. Single `Handle(SettlementCompleted message, IDocumentSession session, CancellationToken ct)` method:
  - `LoadAsync<CatalogListingView>(message.ListingId, ct)`.
  - If absent: tolerant upsert per the Path A `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert" convention — construct a minimal row with `Id = ListingId`, `Status = "Settled"`, no other fields set. This is unusual in practice (a `SettlementCompleted` arriving before `ListingPublished` requires the Selling-side queue and Settlement-side queue to race; the inbox dedup + queue ordering make it almost-impossible-but-not-zero), but the tolerant-upsert convention applies uniformly.
  - If present and `Status = "Sold"`: transition to `Status = "Settled"`. Set `SettledAt = message.CompletedAt` (new field on `CatalogListingView`; see below).
  - If present and `Status != "Sold"`: status-preservation guard per the M5-S3 `PendingSettlementHandler` precedent. The `Status` field is **not** strictly absorbing the way `PendingSettlement.Status` is; the Listings catalog's status semantics are workflow-position rather than terminal-or-not. The §S6 read: only the `"Sold" → "Settled"` transition is legal; any other arrival state ignores. (`"Passed"` listings never settle; `"Closed"` listings either advance to `"Sold"` and then `"Settled"` or to `"Passed"` and terminate — they never reach `Settled`.)
  - `session.Store(existing with { Status = "Settled", SettledAt = message.CompletedAt })`.
- Triple-slash docstring naming: the M3-S6 Path A precedent (sibling handler per source BC), the second-lived-application status, the ADR-014 evidence-pointer, the `"Sold" → "Settled"` transition rule, and the M5-S6 provenance.

**`CatalogListingView` field extension (`src/CritterBids.Listings/CatalogListingView.cs`, modified):**

- Add `public DateTimeOffset? SettledAt { get; init; }`. Nullable because pre-M5 listings have no settlement; post-M5 listings populate at `SettlementCompleted` arrival. The field lands in the `// ─── M3-S6 auction-status fields (additive) ───` block (or a new `// ─── M5-S6 settlement-status field (additive) ───` block — convention is per-slice block markers, see M2-S7 + M3-S6 precedent).
- The existing `Status` field's documentation comment is amended: the transitions list grows from `"Published" → "Open" → "Closed" → "Sold" / "Passed"` to `"Published" → "Open" → "Closed" → "Sold" → "Settled"` (and `"Passed"` is the terminal-failure branch which never settles). The BIN path note becomes `"Published" → "Open" → "Sold" → "Settled"` (no `"Closed"` intermediate).

**Listings module discovery (`src/CritterBids.Listings/ListingsModule.cs`, modified):**

- Confirm the handler is discovered. The existing module pattern registers `IncludeAssembly` against `CritterBids.Listings`'s assembly (per ADR 008/009/011 and the `wolverine-message-handlers.md` skill); `SettlementStatusHandler` is in the same assembly so no explicit registration. Verify at session start.

**Cross-BC fixture exclusion matrix (`tests/CritterBids.Settlement.Tests/SettlementTestFixture.cs` + `tests/CritterBids.Listings.Tests/ListingsTestFixture.cs`):**

- Per memory `project_cross_bc_handler_isolation.md`: the `SettlementStatusHandler` adds Listings as a Settlement-event consumer. The Settlement.Tests fixture's existing `ListingsBcDiscoveryExclusion` covers this (the M5-S3 `PendingSettlementHandler` already prompted that exclusion). No new fixture work unless the §9.1 / §9.2 integration tests now over-discover the new Listings handler — confirm at session start.
- The Listings.Tests fixture's exclusions are M3-S6 precedent (excludes Auctions, Selling). M5-S6 adds `SettlementBcDiscoveryExclusion` if Listings.Tests would otherwise discover the Settlement saga + start handler. **🔄 S5 hand-off** — the M5-S4 cumulative exclusion matrix shows Listings.Tests already excludes Settlement (line 401 of the M5-S4 retro); confirm the exclusion still holds post-S5.

**Workstream A test file (`tests/CritterBids.Listings.Tests/SettlementStatusHandlerTests.cs`, new):**

- Three `[Fact]`s minimum:
  1. **`SettlementCompleted_TransitionsCatalogListingViewToSettled`** — seed a `CatalogListingView` at `Status = "Sold"`; dispatch `SettlementCompleted` via direct handler invocation; assert `Status = "Settled"` and `SettledAt = message.CompletedAt`.
  2. **`SettlementCompleted_OnPassedListing_PreservesPassedStatus`** — defensive scenario; seed `CatalogListingView` at `Status = "Passed"`; dispatch `SettlementCompleted`; assert status unchanged. This shouldn't happen in practice (a passed listing never produces `SettlementCompleted`), but the handler's guard is structural correctness.
  3. **`SettlementCompleted_TolerantUpsert_OnMissingRow`** — defensive scenario; dispatch `SettlementCompleted` with no prior `CatalogListingView` row; assert minimal row created with `Id = ListingId, Status = "Settled", SettledAt = message.CompletedAt`. Mirrors the M3-S6 `AuctionStatusHandler` tolerant-upsert test.

### Workstream B — `SellerPayoutIssued` outbound publish route + Relay-side stub or test fixture

**Decision required at session start (Q1):** Relay BC has not shipped. Three paths:

- **B.1 — Wire `relay-settlement-events` publish route structurally, no consumer.** Add `opts.PublishMessage<SellerPayoutIssued>().ToRabbitQueue("relay-settlement-events")` to `Program.cs`. The route exists; no Listener registration; messages enqueue and sit. When Relay ships post-M5, the consumer wires up and drains the queue. Risk: queue depth grows unbounded if M5 settlements run without Relay ever shipping (mitigated by Wolverine's TTL on the queue if configured; flag for the M5 retro).
- **B.2 — Wire the route AND author a test-fixture-only stub subscriber.** A Listings-side or Settlement-side test fixture subscribes to the queue and asserts `SellerPayoutIssued` arrives with the W003-canonical payload. This proves the publish route works end-to-end and gives the M5 retro a green test rather than a "wired but not tested" state.
- **B.3 — Defer entirely to post-M5.** Same disposition as `PaymentFailed`. The Settlement-side emission via `OutgoingMessages` still happens (it's a no-op for cross-BC delivery without a route, but it's not removed from the saga). When Relay ships, the route wires alongside the consumer.

Recommended default: **B.2.** The route + a test fixture proves the publish path works without requiring Relay BC to ship; the M5 milestone exit criterion ("`SellerPayoutIssued` integration-event publishing per the milestone doc's §7 S6 row") is honored with green test coverage rather than wired-but-untested infrastructure. **🔄 S5 hand-off** — if S5 surfaced friction with the M5-S4 §9.1 integration test's `OutgoingMessages` assertion path (an indication that `tracked.Sent`-style assertions need the publish route to land first), B.2 is firmly preferred; if S5 was clean, B.1 or B.3 are also viable. Defer to session-start judgment.

**If B.2 chosen — test fixture authoring:**

- `tests/CritterBids.Settlement.Tests/SellerPayoutIssuedPublishRouteTests.cs` (new) — single `[Fact]`. Uses Wolverine's outbox tracking shape per the M5-S4 retro's Wolverine tracking idiom: dispatch `ListingSold` end-to-end (recreating the §9.1 setup), use `tracked.Sent.MessagesOf<SellerPayoutIssued>()` to assert exactly one `SellerPayoutIssued` was published with the W003-canonical payload (`PayoutAmount: 76.50, FeeDeducted: 8.50`). Per memory `feedback_wolverine_outbox_tracking.md`: tracked.Sent only works with a `Publish` routing rule, which is what Workstream B.2 wires.

### Workstream C — ADR-014 authoring

**`docs/decisions/014-cross-bc-read-model-extension-shape.md` (new):**

- Status: Accepted on session close.
- Title: **Cross-BC Read-Model Extension Shape**.
- Date: 2026-05-XX (session date).
- Context: The pattern surfaced at M3-S6 when `AuctionStatusHandler` extended `CatalogListingView` with auction-status fields without crossing the modular-monolith BC-isolation rule. The M3-S6 retro and M3-S7 ADR-candidate review flagged the pattern as ADR-worthy but reserved the body authoring for the second lived application as evidence. M4-S6's `SessionMembershipHandler` was the original second-application surface but M4 paused after M4-S2. M5-S6's `SettlementStatusHandler` is now the chronological second application. The two lived applications: M3-S6 (one Auctions-sourced sibling extending with auction-status fields) and M5-S6 (one Settlement-sourced sibling extending with settled-status fields). Both single-source siblings.
- Decision: **Path A — one `CatalogListingView` per logical entity, sibling handler classes, fields added additively.** The decision matches the M4 milestone doc's draft decision shape (§"ADR 014 authoring" subsection of `M4-auctions-bc-completion.md`). The sub-question framing (one sibling per source BC vs one sibling per logical feature) lands on **Option A — one sibling per source BC** by lived precedent; both M3-S6 and M5-S6 siblings are single-source. The multi-source-sibling sub-question's resolution is deferred to a third lived application that actually needs multi-source coordination; M5-S6's `SettlementStatusHandler` is not that application.
- Alternatives considered: **Path B — one view per source BC, UI-side join.** Two `CatalogListingView`-shape documents (Listings-Auctions-side, Listings-Settlement-side) with the frontend joining on `ListingId`. Rejected: complicates the read API (every catalog query crosses two document types); pushes join cost into the API layer; loses the single-document atomicity for status transitions. **Path C — native Marten `MultiStreamProjection` with cross-BC composition.** Single projection driven by event streams from multiple BCs. Rejected at M3-S6: the cross-BC event streams are not Marten-managed (RabbitMQ + Wolverine handler chain, not direct stream subscriptions); using `MultiStreamProjection` would require subscribing to remote BCs' streams which violates the modular-monolith BC-isolation rule. Path A wins by elimination AND by simplicity.
- Evidence: M3-S6 `AuctionStatusHandler` + M5-S6 `SettlementStatusHandler`. Two single-source siblings, one shared `CatalogListingView` document, fields added additively. The cumulative `CatalogListingView` shape at M5-S6 close has M2 base fields + M3-S6 auction-status fields + M5-S6 settled-status fields, all on one document, no breakage of prior fields.
- Consequences:
  - **The pattern will apply again.** Future BCs whose state intersects the catalog will follow the same Path A shape. The Listings catalog is the projected fact; remote BCs' read-model extensions are sibling handlers, not new documents.
  - **Multi-source siblings remain an open sub-question.** When a sibling handler legitimately consumes from two BCs' queues (the M4-S6 `SessionMembershipHandler` original framing — `SessionCreated` / `ListingAttachedToSession` / `SessionStarted` from Auctions plus `ListingWithdrawn` from Selling), the sub-question resolves at that slice. M5-S6 does not pre-empt.
  - **Skill file amendment.** `docs/skills/marten-projections.md` §7 ("Cross-BC View Extension via Sibling Handlers") gets a one-line callout listing `SettlementStatusHandler` as the second lived example. The skill content already documents the pattern from M3-S6; M5-S6 reinforces by adding a second example to the lived-ground reference list.
  - **The M4-S6 (or whenever M4 closes) third application is no-longer the ADR-014 authoring trigger.** ADR-014 ships at M5-S6; M4-S6 (if it ships) is bound by the already-accepted ADR.
- References: ADR-001 (Modular Monolith — the BC-isolation rule this ADR's Path A honors), ADR-009 (Shared Primary Marten Store — the storage substrate enabling sibling handlers to share a `CatalogListingView` document), M3-S6 retro (first lived application), M5-S6 retro (second lived application), `docs/skills/marten-projections.md` §7.

**`docs/decisions/README.md` (modified):**

- ADR-014 row: status flips from `🔒 Reserved for M4-S6 authorship` to `✅ Accepted`, date filled in, summary line written.
- "Next unreserved ADR number" pointer updated. Currently the README says "**`020-<slug>.md>`** ... ADR 014 is reserved for M4-S6 authorship. ADR 015 is reserved for conditional authorship at M4-S7 or earlier if M4-S1 resolved M4-D4 to the cross-BC read option (Cross-BC read access from handlers)." After M5-S6 ships ADR-014, ADR-015's reservation status confirms (still reserved per M4-S1's conditional; no action), and the next-unreserved-pointer stays at 020.

### Workstream D — M5 milestone retrospective + M5-S6 retrospective

**`docs/retrospectives/M5-S6-settlement-outbound-publish-routes-listings-catalog-extension-adr-014-retrospective.md` (new):**

- Mirrors the M5-S4 retro shape. Covers Workstream A (route wiring + `SettlementStatusHandler` + `CatalogListingView.SettledAt` field), Workstream B (Q1's resolution — B.1, B.2, or B.3 + the test fixture if B.2 + any tracked-message-assertion patterns surfaced), Workstream C (ADR-014's body authoring + the multi-source-sibling sub-question deferral), and Workstream D (the M5 milestone retro authoring).
- "Findings against narrative 002" section confirms the cumulative ledger at M5 close — F001 ✓, F002 ✓, F003 ✓ (minimum-scope), F004 ✓, F005 ✓ — and surfaces any S6-specific findings (likely none; S6 is plumbing + ADR authoring, not new behavior).
- "Skill gaps surfaced" section flags any new skill-file work needed post-M5. Likely candidates: a callout in `docs/skills/wolverine-message-handlers.md` for the multi-source-sibling sub-question if S6 surfaces friction around the `SettlementStatusHandler`'s single-source posture; an amendment to `marten-projections.md` §7 with the second-application example. Defer to a future skill-maintenance pass per the M5-S4 retro precedent.

**`docs/retrospectives/M5-retrospective.md` (new):**

- The milestone-level retrospective per `docs/retrospectives/README.md` template and the M3 milestone retro precedent (`docs/retrospectives/M3-auctions-bc-retrospective.md`).
- Covers:
  - **Scope shipped.** Settlement BC end-to-end: seven-phase saga, `PendingSettlement` projection, `BidderCreditView` projection, cross-BC consumers (Selling, Auctions, Participants if S5 promoted the contract), three integration events published (`SettlementCompleted` + `SellerPayoutIssued` confirmed in M5; `PaymentFailed` deferred to post-M5). Six slices (S1–S6) shipped per the M5 milestone §7 plan.
  - **Cumulative scenario coverage.** 41 of the 41 scenarios from `003-scenarios.md` Sections 1–9 are green via the §9 end-to-end integration tests (which exercise Sections 1–8 transitively per the M5-S4 retro's argument). **🔄 S5 hand-off** — if S5 extracted pure-function decider helpers per ADR-019 Option C, the §1–§7 coverage shape may shift from transitive to explicit.
  - **Findings ledger.** Cumulative narrative 002 findings + any M5-specific cross-BC drift; close ADR 007 Gate 4 per Workstream C's lived disposition (engine-default row IDs throughout M5; close with the M5 lived ground as evidence, or re-defer if a specific concrete reason surfaced in S5/S6).
  - **What's deferred from M5 to post-M5.** `PaymentFailed` outbound publish route; Relay BC full implementation (any tests + stubs ship in S6 but Relay BC's own implementation is post-M5); Operations BC; real payment-processor integration; compensation paths; manual payment-failure recovery; `SessionExpired` / `BidderCreditView` cleanup; bidder-balance endpoint.
  - **What's next.** Two routes: (a) **return to M4-S3 through M4-S6** to complete the Auctions BC (Proxy Bid Manager saga + Session aggregate + ADR-014's "session-membership" surface, now bound by the already-accepted ADR-014 since M5-S6 shipped it); (b) **open M6 (frontend MVP)** assuming the backend is sufficiently complete. The recommended order per the M5-S4 retro's state-of-repo framing is **finish M4 before M6**; M5 retro carries that recommendation forward.
  - **Key learnings.** Multi-phase saga patterns; cross-BC-event-seeded projections; UUID v5 deterministic stream IDs; the cutover-gate narrative-as-joint-authority discipline holding across six consecutive slices; the M5-S5 lived ground (S5-specific learnings come from the S5 retro; M5 retro abstracts).

### Documentation forwarding (not implementation; one-line touches)

- **`docs/skills/marten-projections.md` §7** — one-line callout adding `SettlementStatusHandler` as the second lived example of the Cross-BC View Extension via Sibling Handlers pattern. Cross-reference ADR-014.
- **`docs/decisions/007-uuid-strategy.md`** — Gate 4 disposition close per Workstream C's M5-lived-ground evidence. **🔄 S5 hand-off** — if S5 surfaced a specific reason to keep Gate 4 open (e.g., a row-ID-related friction in event-stream queries), the close becomes a re-deferral with a new trigger and a new dated rationale.
- **`docs/milestones/M5-settlement-bc.md`** — status update from `Planning` to `Shipped`; document history v0.1 entry gains a v0.2 line noting the M5 close date.

---

## Explicitly out of scope

- **`PaymentFailed` outbound RabbitMQ publish route.** Deferred to post-M5 per the M5-S5 prompt's out-of-scope section. Operations BC has not shipped; no consumer. Settlement-side emission via `OutgoingMessages` (M5-S5) continues to work for the local-in-process `PendingSettlementHandler` consumer.
- **Relay BC full implementation.** Workstream B's stub or test fixture is the M5-close-bound exercise; Relay BC's actual broadcast pipeline (consuming `SellerPayoutIssued` and `SettlementCompleted`, composing SignalR pushes, surfacing `remainingCredit` from `BidderCreditView` per W003 Phase 1 Part 7) is post-M5.
- **Operations BC.** Post-M5 per `MVP.md`. `PaymentFailed` surfacing on the operator dashboard is post-M5; Operations BC's own implementation is post-M5.
- **M4-S3 through M4-S6 (Auctions BC completion).** Out of scope for M5; the M5 retro carries the "return to M4" recommendation forward but does not author the slice prompts. Specifically: the Proxy Bid Manager saga, the Session aggregate, the `SessionStarted → BiddingOpened` fan-out handler, and the `SessionMembershipHandler` sibling that would have been the original ADR-014 second application — all out of scope. ADR-014 ships at M5-S6 binding M4-S6 when it ships, not the other way around.
- **M6 (frontend MVP).** Out of scope for M5. The `M6-*.md` milestone doc has not been authored; the M5 retro can recommend M6 scoping but does not author it.
- **Real payment-processor integration.** Post-MVP per W003 §"Winner Charge."
- **Compensation paths beyond MVP.** Post-MVP per W003 Phase 1 Part 3.
- **`SessionExpired` / `BidderCreditView` cleanup lifecycle.** Post-MVP per W003 Phase 1 Part 7.
- **Bidder-balance endpoint.** Post-MVP per W003 Phase 1 Part 7.
- **The multi-source-sibling sub-question's resolution.** Deferred per ADR-014's authored body to a third lived application that legitimately needs multi-source coordination. M5-S6's single-source `SettlementStatusHandler` does not need it; M4-S6's `SessionMembershipHandler` (when it ships) is the natural third application.
- **The proposed `ProcessManager<TState>` framework primitive.** Per ADR-019; out of scope for M5 close.

---

## Conventions to pin or follow

- **Sibling handler shape per M3-S6 + ADR-014 (Workstream C's authored body).** One sibling class per source BC, single-source per sibling, tolerant upsert on `LoadAsync` per `marten-projections.md` §"Handler-Driven Projections — Tolerant Upsert," `session.Store` with record-`with` mutation.
- **Status-transition guards per the M5-S3 `PendingSettlementHandler` precedent.** The handler reads the current state and applies a permission-check guard (only `"Sold" → "Settled"` is legal; other arrival states no-op). Mirrors the M5-S3 `if (existing.Status != PendingSettlementStatus.Pending) return` shape.
- **`CatalogListingView.SettledAt` is nullable.** Pre-M5 lived listings (none in practice — the M3-M5 test fixtures don't persist across M5 — but the field shape carries forward for forward-compat).
- **RabbitMQ route shape per M5-S3 precedent.** Publisher: `opts.PublishMessage<T>().ToRabbitQueue("queue-name")` on the producer BC's `Program.cs` registration. Consumer: `opts.ListenToRabbitQueue("queue-name")` on the consumer BC's registration. Per memory `project_wolverine_sticky_handler.md`: this is sticky-handler; tests must direct-invoke handlers rather than use `IMessageBus.InvokeAsync(...)`.
- **Wolverine outbox tracking per memory `feedback_wolverine_outbox_tracking.md`.** `tracked.Sent` assertions in Workstream B.2's test require the `Publish` routing rule that Workstream B wires. The §9.1 / §9.2 / §9.3 tests from M5-S4 / S5 may need amendment if their `OutgoingMessages`-only assertions are now tracked.Sent-eligible — **🔄 S5 hand-off** confirm at session start.
- **ADR-014 body shape per the M4 milestone doc's draft decision shape.** `docs/milestones/M4-auctions-bc-completion.md` §"ADR 014 authoring" subsection contains the draft decision shape (Title, Status, Decision, Sub-question, Alternatives, Evidence, Consequences). M5-S6 authors the body verbatim-ish; the sub-question's "Option A — one sibling per source BC" resolution is determined by the M3-S6 + M5-S6 lived precedent.
- **M5 milestone retro shape per the M3 milestone retro precedent.** `docs/retrospectives/M3-auctions-bc-retrospective.md` is the most-recent milestone-level retro at M5-S6 prompt-time; mirror its section order and depth.
- **Em-dash hygiene** is external-prose-only per memory `feedback_em_dash_scope.md`. Code, retro, ADR-014 body, prompt — all may use em dashes freely.

---

## Acceptance criteria

- [ ] `src/CritterBids.Api/Program.cs` carries a new `opts.PublishMessage<SettlementCompleted>().ToRabbitQueue("listings-settlement-events")` Settlement-side outbound route and a matching `opts.ListenToRabbitQueue("listings-settlement-events")` Listings-side inbound registration.
- [ ] `src/CritterBids.Listings/SettlementStatusHandler.cs` defines `public static class SettlementStatusHandler` with a single `Handle(SettlementCompleted, IDocumentSession, CancellationToken)` method per the tolerant-upsert + status-transition-guard shape.
- [ ] `src/CritterBids.Listings/CatalogListingView.cs` carries a new `SettledAt` nullable `DateTimeOffset?` field; the `Status` documentation comment is amended with the `"Sold" → "Settled"` transition and BIN path note.
- [ ] **Workstream B disposition decided** at session start per Q1; one of B.1 / B.2 / B.3 is chosen; if B.2 chosen, `tests/CritterBids.Settlement.Tests/SellerPayoutIssuedPublishRouteTests.cs` exists with one `[Fact]` exercising the publish route end-to-end via `tracked.Sent`.
- [ ] `tests/CritterBids.Listings.Tests/SettlementStatusHandlerTests.cs` exists with three `[Fact]`s covering the `"Sold" → "Settled"` happy path, the `"Passed" → unchanged` guard, and the tolerant-upsert-on-missing-row path.
- [ ] `docs/decisions/014-cross-bc-read-model-extension-shape.md` exists; body authored per Workstream C's outline; status `Accepted`; references list complete; evidence section names M3-S6 and M5-S6 as the two lived applications.
- [ ] `docs/decisions/README.md` ADR-014 row updated: status `✅ Accepted`, date filled, summary line written.
- [ ] `docs/decisions/007-uuid-strategy.md` Gate 4 disposition resolved per the M5 lived ground (close or re-defer with new dated rationale).
- [ ] `docs/skills/marten-projections.md` §7 carries a one-line callout adding `SettlementStatusHandler` as the second lived example.
- [ ] `docs/milestones/M5-settlement-bc.md` status changes from `Planning` to `Shipped`; document history v0.2 entry added.
- [ ] `docs/retrospectives/M5-S6-settlement-outbound-publish-routes-listings-catalog-extension-adr-014-retrospective.md` exists per the M5-S4 retro shape.
- [ ] `docs/retrospectives/M5-retrospective.md` exists per the M3 milestone retro precedent; covers M5 scope, scenario coverage, findings ledger, what's deferred, what's next, key learnings.
- [ ] `dotnet build CritterBids.slnx` — 0 errors, 0 warnings.
- [ ] `dotnet test CritterBids.slnx` — all green; **🔄 S5 hand-off** — baseline test count is the M5-S5 close count (S5 prompt estimated 105+); S6 adds 3 Listings tests + 0 or 1 Settlement publish-route test depending on Q1 disposition.
- [ ] All six M5 milestone exit criteria from `docs/milestones/M5-settlement-bc.md` §1 honored or explicitly deferred to post-M5 with named successor disposition.

---

## Open questions

- **Q1 — `SellerPayoutIssued` publish route disposition (B.1 / B.2 / B.3).** Resolved at session start per Workstream B's framing. Default to B.2 unless **🔄 S5 hand-off** surfaces a reason to flip.
- **Q2 — ADR 007 Gate 4 disposition.** Close with engine-default lived-fact evidence, or re-defer with a new trigger. **🔄 S5 hand-off** — confirm S5 retro's recorded surface for this item; if S5 explicitly closed it, S6 skips the amendment.
- **Q3 — `SettlementStatusHandler`'s tolerant-upsert posture on the unusual race condition.** A `SettlementCompleted` arriving before `ListingPublished` is structurally near-impossible (the listing must be published before it can sell, and the publish-event reaches Listings before the sell-event due to queue chronology). The tolerant-upsert handler creates a minimal row with `Id, Status, SettledAt` only; the row is missing all M2 fields (`SellerId, Title, Format`, etc.). When `ListingPublished` arrives later, the `ListingPublishedHandler` would either: (a) preserve the M5-S6-created row's `Status = "Settled"` and add the missing fields, or (b) overwrite the row with M2 defaults losing the `"Settled"` state. The M5-S3 `PendingSettlementHandler` precedent uses option (a) (status preservation). Confirm the Listings `ListingPublishedHandler` does the same; if not, amend it or relax the tolerant-upsert posture to throw on missing-row.
- **Q4 — Multi-source-sibling sub-question framing in ADR-014.** The M4 milestone doc's draft framing names two options (Option A — one sibling per source BC, Option B — one sibling per logical feature). M5-S6's single-source `SettlementStatusHandler` doesn't force the question. ADR-014's body resolves the sub-question's authorship by **deferring to a third lived application** that actually has multi-source needs — currently M4-S6's planned `SessionMembershipHandler`. The ADR-014 body records the framework but does not pick A or B; M4-S6 (when it ships) picks. Confirm this stance with the user before authoring the ADR body — the alternative is to pick Option A pre-emptively based on M3-S6 + M5-S6 lived precedent, which is defensible but arguably premature.
- **Q5 — M5 milestone retro depth.** The M3 milestone retro is the precedent (~30 lines). M5 has shipped six slices vs M3's six (S1-S6 + S5b + S4b); narrative-anchored M5 close is more bidder-centric than M3's was. The retro can run longer if useful; calibrate at retro-authoring time.
- **Q6 — `CatalogListingView.SettledAt` vs alternative naming.** The new field's name follows the `ClosedAt` precedent on the M3-S6-extended view (a timestamp-of-terminal-state-arrival shape). Alternative: fold into `ClosedAt` semantically (settlement is the workflow's true close) — rejected because `ClosedAt` is populated by Auctions-side terminal events (`BiddingClosed`, `ListingSold`, `ListingPassed`, `BuyItNowPurchased`); settlement is a separate workflow with its own timestamp. Keep `SettledAt` as the new field.
- **Q7 — Test fixture cross-BC exclusion matrix update post-S6.** The M5-S4 retro recorded the matrix at S4 close; S5 may have shifted it (BidderCreditViewHandler consumes `ParticipantSessionStarted` — would Participants.Tests fixture need a Settlement exclusion?); S6 may shift it again (Listings.Tests now discovers `SettlementStatusHandler`). **🔄 S5 hand-off** — confirm at session start by reading the S5 retro's cumulative matrix; S6 retro records the post-S6 matrix.
- **Q8 — Q4-deferred multi-source-sibling sub-question and ADR-014's evidence completeness.** ADR-014's "Evidence" section names M3-S6 + M5-S6 as the two lived applications. Both single-source. The ADR is internally consistent (Path A's "one sibling per source BC" sub-question resolution is determined by these two examples) but does **not** have evidence for the multi-source-sibling sub-question's framework. Is this evidentially sufficient? The M3-S7 ADR-candidate review framing said "second lived application earns the ADR"; the framing did not require evidence on every sub-question. Confirm with the user at ADR authoring time whether single-source-only evidence is sufficient for ADR-014 acceptance.

---

## Commit sequence

Three commits, in this order:

1. `feat(settlement,listings): wire listings-settlement-events RabbitMQ route; author SettlementStatusHandler; extend CatalogListingView with SettledAt; tests for the Sold→Settled transition` — Workstream A. Lands the bulk of the cross-BC plumbing as a single reviewable diff.
2. `feat(settlement): wire SellerPayoutIssued publish route per Q1 disposition; tests if B.2 chosen` **OR** if B.3 chosen this commit is dropped — Workstream B. The smallest commit; isolatable for review.
3. `docs(decisions,settlement): author ADR-014 cross-BC read-model extension shape; close ADR 007 Gate 4 disposition; M5 milestone status update; M5-S6 + M5 milestone retrospectives; doc forwarding touches` — Workstreams C + D plus the M5 close. Three retros + one ADR body + several one-line forwarding touches bundle naturally.

The commit sequence honors the M5-S5 prompt's three-commit shape per slice. Workstream B's commit may collapse into commit 1 if Q1 resolves to B.3 (no test fixture, no publish route wired, deferred entirely to post-M5).

---

## Update sites for v0.2 amendment (after M5-S5 retro lands)

This v0.1 prompt was authored at M5-S5 prompt-time (2026-05-15), before any S5 lived ground exists. The following sites are the explicit amendment targets when M5-S5 retro is written:

1. **Goal section's "S6 walks in with..." paragraph** — confirm S5's actual close state and ratify or amend the assumed test count, the BidderCreditView lazy-init posture disposition, the ParticipantSessionStarted contract promotion outcome, the ADR-007 Gate 4 status, and the PaymentFailed publish-route disposition.
2. **Workstream B's recommended default disposition** — Q1's framing may flip if S5 surfaced specific friction with publish-route assertions in the §9.1 / §9.2 / §9.3 tests.
3. **Cross-BC fixture exclusion matrix** — confirm the post-S5 matrix; S6 amendments depend on it.
4. **ADR 007 Gate 4 disposition** — confirm the S5 retro's recorded surface; S6 either closes or re-defers.
5. **Q3's tolerant-upsert posture** — confirm Listings `ListingPublishedHandler` does status-preserving upsert on missing-row; if not, S6 either amends it or relaxes Workstream A's posture.
6. **Q4's multi-source-sibling sub-question framing** — if S5 surfaced a specific multi-source pattern (the BidderCreditViewHandler is single-source from Participants, but its interaction with Settlement-internal WinnerCharged might shift the framing), reconsider ADR-014's stance on this sub-question.
7. **Workstream D's M5 milestone retro framing** — finalize the cumulative scenario-coverage count, the cumulative findings ledger close, and the cumulative cross-BC exclusion matrix.
8. **Estimated scope ranges** — file counts and test counts re-estimated based on S5's actual surface.

A v0.2 entry in the Document history section lands when these amendments are made.

---

## Document history

- **v0.1** (2026-05-15): Authored at M5-S5 prompt-time as a forward-looking S6 baseline. Three workstreams (publish routes + Listings catalog `Settled` status + ADR-014 authoring + M5 milestone close) plus the documentation-forwarding cleanup. Explicit "S5 hand-off" annotations mark amendment sites for v0.2 update after M5-S5 retro is written. Cutover-gate joint-authority discipline carried from M5-S1 through M5-S5; this prompt continues to cite narrative 002 in its metadata block per AUTHORING.md rule 3. ADR-014's body authoring at M5-S6 rather than M4-S6 is the planned amendment to the original M4-S1 reservation; the cross-BC read-model extension pattern's second lived application is now `SettlementStatusHandler`, not the slipped `SessionMembershipHandler`.
- **v0.2** (date TBD; after M5-S5 retro): Amendments applied per the §"Update sites" inventory. Lock the recommended Q1 disposition; ratify the test count baseline; finalize cross-BC fixture exclusion matrix references; resolve any S5-surfaced multi-source-sibling reframing for ADR-014.
