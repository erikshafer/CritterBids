# M6-S7: End-to-End Integration + Housekeeping

**Milestone:** M6 ([Obligations BC + Relay BC](../../milestones/M6-obligations-relay-bc.md))
**Slice:** S7 of 7 (cross-BC integration proof + route-topology audit — the M6 close-out slice)
**Narrative:** [`docs/narratives/001-bidder-wins-flash-auction.md`](../../narratives/001-bidder-wins-flash-auction.md) (the winner-facing `BiddingHub` settlement push, Moments 5–6) + [`docs/narratives/006-seller-fulfills-post-sale-obligation.md`](../../narratives/006-seller-fulfills-post-sale-obligation.md) (the Obligations saga start). Narrative 002 is the upstream emitter context (the winner clearing settlement is what *produces* `SettlementCompleted`); S7 implements no new behaviour — it proves the fan-out the narratives separately describe.
**Agent:** @PSA
**Estimated scope:** one PR; one end-to-end integration test in `CritterBids.Api.Tests` (or a composed integration fixture reusing the Relay hub-test pattern), a `Program.cs` route-topology audit (verification; edits only if the audit finds a concrete defect), the M6-close test-count baseline update, two narrative Document History rows, and the slice retro. No new BC code, no new contracts, no new projections.

---

## Goal

Close M6 by proving the post-sale **fan-out** runs end to end in one composed host: a single published `SettlementCompleted` integration event drives **two independent downstream consumers** — (a) the Obligations `SettlementCompletedHandler` starts the `PostSaleCoordinationSaga` (recording `PostSaleCoordinationStarted` and an `ObligationStatusView` of "Awaiting shipment"), and (b) the Relay `SettlementCompletedHandler` pushes a `SettlementCompletedNotification` to the winner's `BiddingHub` group. The Relay push is a **sibling consumer** of `SettlementCompleted`, not a consequence of `PostSaleCoordinationStarted`; the test asserts the fan-out, not a chain.

S7 is an **integration + housekeeping** slice: it adds no new aggregate, saga transition, contract, internal event, or projection. Its deliverables are (1) the end-to-end fan-out test that crosses the Settlement-publish → {Obligations, Relay} seam in-process, (2) a verification audit that every M6 RabbitMQ route in the milestone §5 table is wired in `Program.cs` with the correct direction, and (3) the M6-close test-count baseline recorded in the retro.

The prior six slices each tested their own BC in isolation behind the cross-BC handler-exclusion fixtures (per `critter-stack-testing-patterns`). S7 is the one slice that deliberately composes Settlement-publish, Obligations, and Relay together to assert the wiring they share actually connects. The Relay push half reuses the established real-SignalR-client mechanism from `tests/CritterBids.Relay.Tests/BiddingHubPushTests.cs` (`RelayHubTestFixture` + a `HubConnection` awaiting the push on a `TaskCompletionSource`); the Obligations half asserts the saga started by reading the saga document / `ObligationStatusView`.

If the audit surfaces a real wiring defect (a missing `ListenTo`, a mis-directed publish route, a route the milestone table names but `Program.cs` lacks), fix it in this slice — that is housekeeping in scope, `Program.cs`-only. If it surfaces a genuine **design** question (a BC-boundary ambiguity, or a need for new behaviour to make the test green), stop and flag per rule 7 rather than redesigning in-session.

## Context to load

| File | Purpose |
|---|---|
| `docs/milestones/M6-obligations-relay-bc.md` | Milestone scope — §1 exit criteria (the S7 lines: journey test, `relay-settlement-events` `ListenTo`, all `Program.cs` routes verified, test baseline), §5 the authoritative 7-route RabbitMQ table the audit checks against, §7 slice table (S7 row) |
| `docs/narratives/001-bidder-wins-flash-auction.md` | Joint anchor; Moments 5–6 are the winner-facing `BiddingHub` settlement push the fan-out's Relay half completes |
| `docs/narratives/006-seller-fulfills-post-sale-obligation.md` | Joint anchor; the obligation start (`PostSaleCoordinationStarted` / "Awaiting shipment") the fan-out's Obligations half asserts |
| `docs/decisions/023-relay-reactive-broadcast-architecture.md` | **Authoritative** for Relay hub architecture (direct `IHubContext`); overrides any stale `wolverine-signalr` skill guidance |
| `docs/skills/critter-stack-testing-patterns/SKILL.md` | §Cross-BC Handler Isolation — S7 needs the *inverse* (a host that **includes** Settlement-publish + Obligations + Relay); deterministic in-process driving, no real-clock waits. Reuse the `RelayHubTestFixture` / `BiddingHubPushTests` push-observation pattern; assert the saga start by querying the Obligations saga doc / `ObligationStatusView` |
| `docs/skills/wolverine-signalr/SKILL.md` | The real-SignalR-client transport for observing the Relay push inside a composed host (read under ADR 023's authority — note the skill's lived update is still owed and out of scope here) |
| `src/CritterBids.Api/Program.cs` | The single composition root carrying every M6 route, `IncludeAssembly`, `AddObligationsModule()` / `AddRelayModule()`, and the `MapHub` calls — the audit's subject; its inline comments already document the accepted `relay-settlement-events` routing |

## In scope

1. **End-to-end fan-out test** in `CritterBids.Api.Tests` (or a composed integration fixture reusing `RelayHubTestFixture`): publish/dispatch one `SettlementCompleted` and assert, in one run, that (a) the Obligations `PostSaleCoordinationSaga` started — `PostSaleCoordinationStarted` recorded for the deterministic `ObligationId` (UUID v5 from `ListingId`), `ObligationStatusView` reads "Awaiting shipment" — and (b) the Relay `SettlementCompletedNotification` reaches the winner's `BiddingHub` group. The Relay half reuses the `BiddingHubPushTests` mechanism (real `HubConnection`, `TaskCompletionSource` with a failsafe timeout); the Obligations half reads Marten. Deterministic, in-process; **no real-clock waits** (demo-mode `ObligationsOptions` durations or tracked-session completion, never `Task.Delay`).
2. **Composed host, not a weakened fixture**: the test runs against a host that includes Settlement-publish + Obligations + Relay together — the inverse of the sibling-exclusion fixtures. Do **not** weaken or remove the existing per-BC `{TargetBc}BcDiscoveryExclusion` fixtures to achieve this; compose a dedicated integration host (extending the real-Kestrel `RelayHubTestFixture` so the SignalR client can connect).
3. **`Program.cs` route-topology audit** against the milestone §5 table: verify all seven M6 queues are wired with the correct direction —
   - `obligations-settlement-events` (Obligations listens; Settlement publishes `SettlementCompleted`)
   - `relay-obligations-events` (Relay listens; Obligations publishes the four Obligations contracts)
   - `relay-participants-events`, `relay-selling-events`, `relay-auctions-events`, `relay-listings-events` (Relay listens; source BCs publish)
   - `relay-settlement-events` (Relay listens; Settlement publishes) — **confirm the `ListenToRabbitQueue("relay-settlement-events")` call is present and consuming.** It already exists; this item is confirmation. **The existing `SettlementCompleted` publish route to `relay-settlement-events` (alongside `SellerPayoutIssued`) is accepted** per the `Program.cs` M6-S5 comment and the M6 exit criteria — do **not** treat it as drift against the §5 table's `SellerPayoutIssued`-only row. The audit verifies required listen/publish sides exist, not event-exhaustiveness.
   Add or correct a route **only if** the audit finds a concrete defect; otherwise this item is verification, `Program.cs`-only, no BC source touched.
4. **Test-count baseline update**: record full-solution and BC-scoped test counts at M6 close in the retro's Baseline / Test-results sections per the retrospectives README, noting the delta from the S6 baseline and the test(s) added here.
5. **Slice retrospective** `docs/retrospectives/M6-S7-end-to-end-integration-housekeeping-retrospective.md`, including the mandatory `## Spec delta — landed?` paragraph and a `## Findings against narrative` section for narratives 001 / 006.
6. **Narrative Document History rows** on **both** `docs/narratives/001-bidder-wins-flash-auction.md` (the winner settlement push is now covered as part of the end-to-end fan-out, completing the partial S5 landing) and `docs/narratives/006-seller-fulfills-post-sale-obligation.md` (the `SettlementCompleted → saga-start` join is now end-to-end test-covered). If the test surfaces drift against either narrative, route it per ADR 016's four lanes and resolve in this PR.

## Explicitly out of scope

- **The M6 milestone retrospective + skills extraction.** Firm non-goal for this slice — do **not** create or update any milestone-level M6 retro here. It lands in a dedicated M6-close session (matching the M2/M3/M4 `retrospective-skills-mX-close` precedent). Only the **S7 slice retro** is written this session.
- **Backfilling the missing M6-S6 prompt.** The S6 retro references a prompt authored in a companion worktree that never landed in `main`. Real gap, tracked separately — do not author it here.
- **The owed `docs/skills/wolverine-signalr/SKILL.md` lived-Relay update** (owed since S5, reaffirmed in the S6 retro). Tracked separately; record it as still-owed in the retro but do not make the skill edit in this slice.
- **`OperationsObligationsView` / the Operations BC operator read models.** Deferred to M7 per the archived `add-obligation-lifecycle` task 8.3. S7 publishes nothing new toward it.
- **Any new behaviour**: no new saga transition, command, contract, internal event, projection, or hub handler. If the fan-out test cannot be made green without new behaviour, that is a `code-update` finding to surface — **stop and flag**, do not implement under an integration-slice banner.
- **New OpenSpec change.** The `add-obligation-lifecycle` change is complete and archived; S7 adds no requirement. Do not author a new `openspec/changes/` folder.
- **Email / SMS / push delivery seams, Relay HTTP endpoints, React SPA / `@microsoft/signalr` client** — post-MVP / M8 per the milestone non-goals.
- **Editing OpenSpec-managed files** under `.github/prompts/` or `.github/skills/`.

## Conventions to pin or follow

- **Relay never publishes integration events** (milestone §6): the push half of the test asserts a SignalR push, never an `OutgoingMessages` return from a Relay handler.
- **ADR 023 is authoritative for Relay hub architecture** (direct `IHubContext`). Where the `wolverine-signalr` skill (lived update still owed) and ADR 023 disagree, ADR 023 wins.
- **Real SignalR-capable test host**: the push assertion requires a host that exercises real hub transport (the existing `RelayHubTestFixture` real-Kestrel pattern). Do not use an in-memory `TestServer`/Alba-only host that cannot drive a `HubConnection`.
- **Deterministic test driving** per `critter-stack-testing-patterns/SKILL.md`: real `HubConnection` + `TaskCompletionSource` for the push; Marten read for the saga start; demo-mode `ObligationsOptions` durations injected; no `Task.Delay`, no real-clock polling.
- **Deterministic identity** per the archived spec: the test asserts the same deterministic `ObligationId` (UUID v5 from `ListingId`) the saga-start requirement defines.
- `sealed record` for any new test-support types; no "Event" suffix; no "paddle"; `[AllowAnonymous]` posture holds through M6.

## Spec delta

Per ADR 020. **No new requirement, no new OpenSpec change.** The `add-obligation-lifecycle` OpenSpec change is complete and archived (2026-05-29); S7 implements nothing new. The only spec-shaped delta is **Document History coverage in two narratives** (the ADR 020 step-4 record): narrative 001 records that the winner-facing `BiddingHub` settlement push is now covered end-to-end as part of the fan-out (completing the partial S5 landing), and narrative 006 records that the `SettlementCompleted → PostSaleCoordinationStarted` join is now end-to-end test-covered. The fan-out itself — one `SettlementCompleted` driving the Obligations saga start *and* the Relay winner push as independent sibling consumers — is the behaviour the test proves; it is already specified across narratives 001, 002, and 006 and adds no new requirement. If the test surfaces drift against narrative 001 or 006, that is a finding routed per ADR 016's four lanes and resolved in this PR. The retro's `## Spec delta — landed?` paragraph confirms the fan-out test is green, both Document History rows landed, the route audit passed (naming any correction made), and that the S6-prompt backfill, the owed `wolverine-signalr` skill update, the Relay OpenSpec ledger row, and the M6 milestone retro remain tracked but out of this slice.

## Acceptance criteria

- [ ] An end-to-end fan-out test exists in `CritterBids.Api.Tests` (or a named composed integration fixture) that, from one `SettlementCompleted`, asserts **both** the Obligations saga start (`PostSaleCoordinationStarted` for the deterministic `ObligationId`; `ObligationStatusView` = "Awaiting shipment") **and** the Relay `SettlementCompletedNotification` push to the winner's `BiddingHub` group — green, deterministic, no real-clock waits.
- [ ] The test composes Settlement-publish + Obligations + Relay in one host without removing or weakening any existing `{TargetBc}BcDiscoveryExclusion` fixture; it uses a real SignalR-capable host.
- [ ] `Program.cs` contains `ListenToRabbitQueue(...)` for each Relay inbound queue (`relay-obligations-events`, `relay-participants-events`, `relay-selling-events`, `relay-auctions-events`, `relay-settlement-events`, `relay-listings-events`) and `obligations-settlement-events`; each has its matching publish route. The existing dual `SettlementCompleted` + `SellerPayoutIssued` publish to `relay-settlement-events` is recorded as accepted, not flagged as drift.
- [ ] No production BC source file is changed except `src/CritterBids.Api/Program.cs`, and only if the audit found a concrete route defect (named in the retro). No new saga transition, command, contract, internal event, projection, or hub handler added (negative-space assertion recorded in the retro).
- [ ] `dotnet build` passes (0 errors, 0 warnings). Full `dotnet test CritterBids.slnx` is green with no regressions; **if Docker/Testcontainers is unavailable** (as in the S6 session), the retro records the exact blocked command and reason and names the subset that did run, rather than claiming a green that did not happen.
- [ ] `docs/narratives/001-bidder-wins-flash-auction.md` and `docs/narratives/006-seller-fulfills-post-sale-obligation.md` each gain a Document History row recording the end-to-end fan-out coverage.
- [ ] `docs/retrospectives/M6-S7-end-to-end-integration-housekeeping-retrospective.md` written, including `## Spec delta — landed?`, `## Findings against narrative` (001/006), the M6-close test-count baseline, and an explicit note that the S6-prompt backfill + owed `wolverine-signalr` skill update + Relay OpenSpec ledger row + M6 milestone retro remain tracked separately.
- [ ] No commit to `main`; no `Co-Authored-By` trailer.

## Open questions

- **Composed-host fixture shape.** `CritterBids.Api.Tests` is the named home, but confirm whether a full `Program.cs` composition or a narrower Settlement-publish + Obligations + Relay composition (extending `RelayHubTestFixture`) is the right host — they are materially different test shapes. Name the chosen composition in the retro; do not mutate the per-BC exclusion fixtures to get there.
- **Push observation under full composition.** Confirm the real-`HubConnection` mechanism from `BiddingHubPushTests` observes the Relay push as deterministically inside a multi-BC host as it did in the Relay-only S5/S6 tests. If it cannot, flag the limitation and the chosen assertion shape in the retro rather than introducing real-clock waits.
- **Relay OpenSpec adoption ledger.** `openspec/README.md` still lists `bid-relay` as "proposed (working) / evaluate at opening"; S5/S6 proceeded narrative-anchored only. Recording the formal decline/defer in the ledger is a housekeeping touch better placed in the M6 close than in S7 — surface it; do not edit the ledger unilaterally here.
