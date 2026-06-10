# M8-S3c — ADR 027: Per-BC Sticky Queue Bindings

**Status:** ✅ Executed (2026-06-09) — retro: `docs/retrospectives/M8-S3c-adr027-sticky-queue-bindings-retrospective.md`
**Milestone:** M8 ([React Frontend SPAs](../../milestones/M8-frontend-spas.md)) — backend-housekeeping slice inserted after S3b; this prompt's first deliverable amends the milestone §7 slice ladder to add the S3c row (precedent: the v0.2 amendment that split S3 into S3a/S3b)
**Slice:** S3c of M8 — implements [ADR 027](../../decisions/027-per-bc-sticky-queue-bindings.md), accepted 2026-06-09 at the Bug #2 follow-ups session
**Narrative:** none (infrastructure truth-restoration slice; no journey change — every observable journey behavior must be byte-identical except duplicate-delivery side effects disappearing)
**Agent:** any (Claude Code precedent: the Bug #2 fix sessions)
**Date authored:** 2026-06-09
**Estimated scope:** one PR; backend-only (`Program.cs` routing block, `[StickyHandler]`/fluent bindings across BC consumer handlers, test-fixture updates) plus the milestone-doc amendment and the session retro. No contract changes, no new domain capability, no frontend changes.

---

## Preconditions

- ADR 027 is **Accepted** (it is — do not relitigate the decision; implement it).
- PR #90 (dispatcher bridge) and #91 (skills) are on `main` (they are).
- Read the Separated-mode dispatch mechanics FIRST — this slice only makes sense with the fan-out
  model loaded: `docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md` §"The mechanism".

## Goal

Every broker-published integration event is delivered to each consuming handler **exactly once,
on the queue its BC owns** — implemented per ADR 027 (sticky bindings + the new
`auctions-auctions-events` self-consumption queue), with the Bug #3-class saga-start dead letters
eliminated and the BIN/withdrawal flows live-verified for the first time.

## Context to load

1. [ADR 027](../../decisions/027-per-bc-sticky-queue-bindings.md) — the decision, scope, and consequences.
2. `docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md` — the dispatch mechanics
   (`HandlerFor(Type, Endpoint)` sticky-match vs fan-out vs default).
3. `docs/skills/message-flow-diagnosis/SKILL.md` — the verification instruments this slice's
   acceptance criteria are stated in (routing probe, debug-log signatures, envelope semantics).
4. `docs/skills/wolverine-sagas/SKILL.md` § Separated-mode rule — the invariant this slice must not break.
5. `src/CritterBids.Api/Program.cs` — the routing block being rewired.
6. `docs/skills/critter-stack-testing-patterns/SKILL.md` §1 (cross-BC fixture isolation) and §6
   (fresh-state browser smoke) — fixtures will feel this change.
7. Upstream ai-skills: `critterstack-arch-modular-monolith` (Separated semantics + the sharp-edge note).

## In scope

1. **Consumer audit (do this first, commit the table in the retro):** for every cross-BC contract
   event, enumerate its in-process consumers and classify each as broker-fed (needs a sticky
   binding) vs forwarding-fed local (e.g. `SessionStarted` → `SessionStartedHandler`) vs
   internal-command (unaffected). Use `GET /api/dev/routing-probe`, `describe-routing --all`, and
   one debug-logged seed run as evidence — do not classify from memory.
2. **Sticky bindings:** bind each BC's broker-fed handlers to that BC's queue. Default to the
   `[StickyHandler("<queue>")]` attribute on the handler class (self-documenting; the BC owns its
   queue name) unless the audit surfaces a reason for the fluent `AddStickyHandler` form — record
   the choice and rationale in the retro.
3. **`auctions-auctions-events` queue:** declare it, route the Auctions-family events the two
   dispatchers consume (`BidPlaced`, `ReserveMet`, `ExtendedBiddingTriggered`, `BuyItNowPurchased`,
   `ListingSold`, `ListingPassed`, plus `Contracts.Selling.ListingWithdrawn`) to it, listen, and
   bind `AuctionClosingDispatchHandler`, `ProxyBidDispatchHandler`, and
   `StartAuctionClosingSagaHandler` (audit confirms the exact set) sticky to it.
4. **Test-suite reconciliation:** fixtures that relied on fan-out delivery or
   `tracked.NoRoutes`/`tracked.Sent` shapes will shift; update per the testing-patterns skill.
   Full `dotnet test CritterBids.slnx` green.
5. **Live verification (fresh containers):** seed → bid → confirm each consumer logs exactly ONE
   `Successfully processed` per event per the debug-log signatures; run the **BIN purchase** and
   **withdrawal** journeys end-to-end (first-ever live observation — `WithdrawListingEndpoint`
   exists; BIN is bus-only, drive it like the seed does); confirm `wolverine_dead_letters` does
   not grow across a full seed→bid→close→settle journey.
6. **Docs:** milestone §7 S3c row + Document History entry; findings-note Bug #3 section flipped
   to resolved; STATUS.md refresh at close; session retro (shared slug with this prompt).

## Explicitly out of scope

- The upstream Wolverine fix (separate work order:
  `docs/research/wolverine-upstream-saga-sticky-separation-handoff.md`).
- Removing the client-side `LiveActivity` dedupe or the saga idempotency guards — they remain as
  at-least-once redelivery hygiene (ADR 027 records this).
- Reverting the dispatcher bridge (it stays; ADR 027's bindings and the bridge are complementary).
- Any ops-SPA work (M8-S5/S6), any contract change, any new endpoint.
- Queue consolidation (ADR 027 Alternative B was rejected — don't reopen it).

## Conventions to pin or follow

- One PR; retro in the same PR (shared slug). Never commit to `main` directly.
- `dotnet build` + full `dotnet test` before every commit that touches `.cs`/`.csproj`.
- Queue-name strings are schema-like: new queue name `auctions-auctions-events` follows the
  established `<consumer-bc>-<producer-bc>-events` convention (self-consumption reads literally).
- Debug-log + probe evidence over inference, per `message-flow-diagnosis`.

## Spec delta

None to workshops/narratives (behavior-preserving infrastructure). The milestone §7 amendment
adding the S3c row IS the spec delta; ADR 027 already records the decision rationale.

## Acceptance criteria

1. A debug-logged live run shows each broker-fed consumer processing each event **exactly once**,
   at its own BC's queue endpoint (`Successfully processed … from rabbitmq://queue/<own-queue>`),
   with **zero** post-receipt `local://` fan-out relays for contract events.
2. `wolverine_dead_letters` count is unchanged after a full seed→bid→BIN→withdraw→close→settle
   journey on fresh containers (saga-start races gone).
3. BIN and withdrawal journeys live-verified: `CatalogListingView` reaches the right terminal
   status, Settlement/Obligations react, saga documents are MarkCompleted-deleted.
4. Full solution test suite green; no `[AllowAnonymous]`/auth posture changes; `describe-routing
   --all` output reflects the new topology and is pasted into the retro.
5. The bidder SPA journey (catalog → bid → live outbid) still passes the fresh-state browser
   smoke (testing-patterns §6) — the feed now shows exactly one entry per bid naturally.

## Open questions

1. Attribute vs fluent sticky form (in-scope item 2 — implementer decides with rationale).
2. Does `SessionStarted`'s dual life (forwarded locally to `SessionStartedHandler` + published to
   queues for Listings/Operations) need a binding for the local consumer, or does the local
   forwarding path coexist untouched with sticky broker consumers? The audit answers this; if the
   answer is surprising, halt-and-consult before rewiring it.
3. Do any test fixtures depend on fan-out multiplicity (asserting N deliveries)? If yes, the
   assertions change to exactly-once — flag each in the retro as a behavior-truth correction.
