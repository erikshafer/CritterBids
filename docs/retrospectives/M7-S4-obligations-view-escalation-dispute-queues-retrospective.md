# M7-S4: `OperationsObligationsView` — Escalation + Dispute Queues - Retrospective

**Date:** 2025-02-14
**Milestone:** M7 - Operations BC
**Slice:** S4 - `OperationsObligationsView` (escalation queue + open-dispute queue)
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M7-S4-obligations-view-escalation-dispute-queues.md`

## Baseline

- Build clean at session start: 0 errors / 0 warnings across `CritterBids.slnx`.
- Operations BC scaffold from M7-S2/S3 in place: `AddOperationsModule()`, the `operations`
  Marten schema, `SettlementQueueView`/`LotBoardView`/`BidActivityEntry`, the
  `OperationsTestFixture` (six foreign-BC exclusions + `DisableAllExternalWolverineTransports()`),
  and the per-project `OperationsBcDiscoveryExclusion` pattern. Built additively — none recreated.
- Foreign fixtures that dispatch obligations events (Auctions, Selling, Settlement, Obligations×2)
  already carried whole-namespace `OperationsBcDiscoveryExclusion` registrations from S2/S3.
- The four Obligations contracts (`DeadlineEscalated`/`DisputeOpened`/`DisputeResolved`/
  `ObligationFulfilled`) routed only to `relay-obligations-events`. W006 §4 froze the view's
  field set before the slice opened.

## Items completed

| Item | Description |
|------|-------------|
| S4a | `OperationsObligationsView` sealed record + `QueueState` enum (W006 §4 field freeze) |
| S4b | One Obligations-source Path A Sub-Option A handler family (four tolerant-upsert overloads) |
| S4c | Non-monotone terminal-absorbing guard (`Extension` backward transition left open) |
| S4d | `AddOperationsModule()` additive `ConfigureMarten` registration + indexes |
| S4e | `Program.cs` routing-only additions (`operations-obligations-events` listen + 4 publish routes + `AutoProvision`) |
| S4f | Cross-BC discovery exclusions — empirical red-run set (result: none newly required) |
| S4g | End-to-end Testcontainers projection tests + schema-mapping assertion |

## S4a: View + `QueueState` enum

W006 §4's field set was implemented exactly — no more, no fewer. The view is a `sealed record`
keyed by `ObligationId` with `Id => ObligationId` alias, carrying `ListingId`, the nullable
dispute/resolution/fulfilment fields, the four `…At` timestamps, and `QueueState`.

**Why no `LastUpdatedAt` column.** S2/S3 both shipped a `LastUpdatedAt` field, but W006 §4
enumerates the obligations-view field set without one. The rubber-duck pass flagged that adding
it would violate "no more, no fewer," so the latest-known timestamp is *derived* at guard time
from the four populated `…At` fields rather than persisted. This is the first Operations view to
take that stance — flagged here as a deliberate divergence from the S2/S3 precedent.

**`QueueState` member set** (the unfrozen W006 implementation choice): `None` (0 sentinel, never
persisted), `Escalated`, `Disputed`, `Active`, `Resolved`, `Fulfilled`.

```
DeadlineEscalated ─▶ Escalated ──DisputeOpened─▶ Disputed
                                                    │
                            ┌── Extension ──────────┤
                            ▼                        ├── Refund/Closed ──▶ Resolved (terminal)
                          Active ───ObligationFulfilled──▶ Fulfilled (terminal)
                            │                        ▲
                            └── ObligationFulfilled ─┘
```

`Active` (post-`Extension`, non-terminal) and `Resolved` (post-`Refund`/`Closed`, terminal) are
distinct members so the one sanctioned backward transition is representable without collapsing it
into a terminal. `Fulfilled` and `Resolved` are kept distinct — they are mutually exclusive
terminal outcomes, not the same state. Kept Operations-internal (no Contracts type), consistent
with `LotBoardStatus`/`SettlementQueueStatus`.

## S4b/S4c: Handler family + the non-monotone guard

One `public static class OperationsObligationsHandler` with four `Handle(Event, IDocumentSession,
CancellationToken)` overloads — the lived shape from `SettlementQueueHandler`/
`LotBoardSellingHandler`. Returns `Task`, no `OutgoingMessages`, no `IMessageBus`.

**The guard is ignore-the-event, not freeze-the-state.** The rubber-duck pass caught that freezing
only `QueueState` on a stale/absorbed event would still let the payload fields be overwritten by a
late re-delivery. The guard therefore early-returns *before any write*:

```csharp
private static bool IsIgnored(OperationsObligationsView view, DateTimeOffset incoming) =>
    QueueStateRules.IsTerminal(view.QueueState) || incoming < LatestKnown(view);
```

| Concern | Before (naive) | After |
|---|---|---|
| Guard granularity | freeze `QueueState` only | early-return, no field touched |
| Terminal set | monotone absorbing rank (S3) | `IsTerminal => Fulfilled or Resolved` |
| `Disputed → Active` (`Extension`) | blocked by rank | left open (non-terminal target) |
| Staleness comparison | n/a | strictly-older (`<`), derived `LatestKnown` |

**Why not the S3 monotone rank.** The lot board's absorbing-rank guard cannot model this view:
`Extension` is a sanctioned `Disputed → Active` *backward* move. Only the terminal states are
absorbing. `LatestKnown` is the `Max` of the populated `…At` fields (`DefaultIfEmpty(MinValue)`),
so a brand-new row has no floor and its first event is never stale. Strictly-older (`<`, not `<=`)
keeps a legitimate same-instant forward transition from being dropped; an exact non-terminal
re-delivery harmlessly rewrites identical data.

**Set-once read field-by-field (the S2 lesson).** `ListingId` is the only set-once field (via the
`Guid.Empty` sentinel) — it is the cross-view join key present on all four events. Every other
field is last-write within its own event family; no unmandated uniform guard was added.

## S4f: Empirical discovery-exclusion — the surprise

The work-order (and S3's lesson) warned that a newly globally-discovered Operations consumer flips
the four obligations events to sticky `Separated` routing and turns foreign fixtures' inline
`InvokeMessageAndWaitAsync` red. **The red full-suite run found zero newly-broken fixtures** — the
full suite was green on the first post-implementation run.

Root cause of the clean result: every foreign fixture that dispatches an obligations event
(Auctions, Selling, Settlement, Obligations×2) already excludes the *entire* `CritterBids.Operations`
namespace from S2/S3, and Relay — which co-consumes `DisputeOpened`/`DisputeResolved` — drives its
tests through paths that did not regress. Per the work-order's explicit instruction ("find the exact
set by a red run — do NOT predict"), **no new `OperationsBcDiscoveryExclusion` was added and no
foreign-fixture `Invoke→Send` swap was needed.** This vindicates the empirical rule: S3 predicted a
set and over-shot; S4 measured and the set was empty.

## Test results

| Phase | Operations Tests | Full suite | Result |
|-------|-----------------|-----------|--------|
| After handler + view + 8 projection tests | 27 | — | green |
| Full `dotnet test CritterBids.slnx` | 27 | 230 | all green, 0 failures |

Per-project final counts (full suite): Contracts 1, Api 1, Operations 27, Participants 6,
Listings 20, Selling 36, Settlement 25, Obligations 13, Relay 36, Auctions 65. No regressions.

One compile fix during the session:

```
error CS1061: 'IAlbaHost' does not contain a definition for 'InvokeMessageAndWaitAsync'
```

Root cause: missing `using Wolverine.Tracking;` (where the tracked-session extension lives).
Resolution: added the using; all 27 Operations tests then passed.

## Build state at session close

- Errors: 0. Warnings: 0 (unchanged from baseline).
- `OutgoingMessages` in `OperationsObligationsHandler`: 0. `IMessageBus` references: 0.
- `tracked.Sent` empty assertion present (pure-consumer contract test-guarded).
- New `AddMarten()` calls in the module: 0 (additive `ConfigureMarten` only).
- New `CritterBids.Contracts.*` types: 0. "Event"-suffixed type names: 0. "paddle" references: 0.
- `[Authorize]`/`StaffOnly`/auth-scheme registrations added: 0. New HTTP endpoints: 0.
- `Title` denormalised into the view: no (only `ListingId` stored).
- New `OperationsBcDiscoveryExclusion` registrations: 0 (empirical red-run set was empty).
- Marten indexes added: `QueueState` and `ListingId` on `OperationsObligationsView` — the two
  query axes the derived queues and the cross-view join key warrant.

## Key learnings

1. **Empirical exclusion can be empty.** A new global consumer does not necessarily break any
   foreign fixture if the at-risk fixtures already exclude the whole consumer namespace. Measure;
   do not pre-emptively add exclusions. The red-run is the only source of truth (S3 over-shot by
   predicting; S4's measured set was empty).
2. **Guard at event-ignore granularity, not state-freeze granularity.** For an upsert view, a stale
   or absorbed event must skip *all* writes — freezing only the status leaves payload fields exposed
   to late re-delivery corruption.
3. **A non-monotone state machine needs an explicit terminal-absorbing guard, not a rank.** When a
   sanctioned backward transition exists (`Extension`), the S3 monotone-rank idiom does not apply;
   guard only the terminal states and leave the backward edge open.
4. **Derive, don't persist, a non-spec timestamp.** When the frozen field set omits a
   `LastUpdatedAt`, derive latest-known from the existing `…At` fields rather than adding a column —
   keeps "no more, no fewer" literal.

## Findings against narrative

Narrative 008 (`docs/narratives/008-operator-resolves-dispute-with-extension.md`) is forward-spec
for the Obligations dispute *saga* (shipped M6-S4), and its Moments are saga-behaviour beats. The
read model's field set is frozen by W006 §4, and narrative 008's Cast/Deferred already flags the
view as "M7 Operations-BC work." S4 implemented the queue transitions narrative 008 describes
(Moment 1 dispute lands; Moment 2 `Extension` returns the card to the active set; Moment 3 the
extended obligation fulfils) faithfully as read-model projections. **No narrative or workshop
Document-History row is owed** — confirmed per the work-order's explicit statement; this is the
same posture S2/S3 took against their W006 sections. Routes to `document-as-intentional`: the
narrative Moments and the W006 §4 freeze are two valid expressions of the same intent (saga
behaviour vs read-model field set).

## Spec delta - landed?

The spec delta landed as written. W006 §4 (`OperationsObligationsView` field freeze) gained its
first runnable, test-backed implementation: the `ObligationId`-keyed upsert view, its single
Obligations-source Path A handler family, the `QueueState` derivation
`Escalated → Disputed → (Resolved-or-active) → Fulfilled`, the escalation/open-dispute queue
membership, the `Extension`-returns-to-active transition, and the `Fulfilled`/`Resolved`-terminal
guard are all proven end-to-end against real Postgres via Testcontainers. `DisputeResolved` is
exercised across all three `ResolutionType` branches (`Extension` non-terminal; `Refund`/`Closed`
terminal). **No narrative or workshop Document-History row is owed** (narrative 008 is saga
forward-spec, the view is frozen by W006 §4, a freeze rather than a behaviour narrative) — confirmed
per the prompt's `## Spec delta` section. No Contracts type was added; the session/participant board
(S5), all auth behaviour (S6), and every query endpoint remain unimplemented.

### Judgment calls flagged (autopilot defaults taken)

- **Single view with `QueueState` discriminator** — not two physical views or a row split (W006 §4
  default).
- **Status-flip, not document delete** — "drop from queue" is membership-by-`QueueState`-filter;
  the row persists with its terminal/active state, preserving operator history.
- **Unseen-obligation = tolerant-upsert seed** — a happy-path `ObligationFulfilled` (or a stray
  terminal `DisputeResolved`) for an `ObligationId` never escalated/disputed materialises a terminal
  row with the fulfilment/resolution fields set and the rest null. Chosen over no-op because Path A's
  tolerant-upsert default leans that way and `LoadOrCreate` always constructs. **This is the genuine
  design decision the work-order asked to flag** — a future S6 query reader must filter to the queue
  states it cares about (`Escalated`/`Disputed`) rather than assuming every row entered via a queue.
- **No persisted `LastUpdatedAt`; derived latest-known timestamp** — diverges from S2/S3, see S4a.
- **`QueueState` member set** including the `None` sentinel and the `Active`/`Resolved` split — see
  S4a.
- **Indexes on `QueueState` + `ListingId`** — the two derived-queue filters and the cross-view join.
- **No-cross-queue-asymmetry confirmed at session start** — all four events are Obligations-published
  on the same `relay-obligations-events` family; S4 added the parallel `operations-obligations-events`
  publish routes with no foreign-family asymmetry (unlike S3's `ListingWithdrawn`).

## Verification checklist

- [x] `OperationsObligationsView` is a `sealed record` keyed by `ObligationId` carrying exactly the
  W006 §4 field set; a `QueueState` enum realises the derivation.
- [x] One ADR 014 Sub-Option A Obligations-source handler family consumes all four events as
  tolerant upserts; each `…At` populates its field; `ListingId` is set-once; set-once-vs-last-write
  read field-by-field.
- [x] Escalation (`Escalated`) and open-dispute (`Disputed`) membership derived correctly;
  `DisputeOpened` advances `Escalated → Disputed`, asserted.
- [x] `DisputeResolved { Extension }` returns to the active set (not terminal); `{ Refund | Closed }`
  resolves terminally — both branches asserted.
- [x] `ObligationFulfilled` advances to terminal `Fulfilled`, sets `WinnerId`/`SellerId`/
  `FulfilledAt`, removes the row from active queues; terminal guard prevents regression, asserted;
  the `Extension` transition is not blocked by that guard.
- [x] Idempotent re-delivery of the terminal event is a no-op, asserted.
- [x] `AddOperationsModule()` additively registers the view via `ConfigureMarten` in `operations`;
  no `AddMarten()` in the module; no saga/aggregate/event-stream registration; indexes recorded.
- [x] `Program.cs` has the `operations-obligations-events` listen, the four publish routes, and
  `AutoProvision()`; no upstream Obligations change; no-cross-queue-asymmetry confirmed.
- [x] No `[Authorize]`/`StaffOnly`/auth-scheme; `Program.cs` auth state otherwise unchanged; no HTTP
  endpoint added; `Title` not denormalised.
- [x] `OperationsBcDiscoveryExclusion` extended to exactly the red-run set (empty); no leakage; no
  new shared exclusion class; `SendMessageAndWaitAsync` rule held (no foreign fanned-out dispatch
  needed).
- [x] Handlers return no `OutgoingMessages`, make no `IMessageBus` call; pure-consumer test-guarded
  (`tracked.Sent` empty); `operations`-schema mapping asserted via `information_schema`.
- [x] End-to-end projection test green (escalation seed, dispute open→resolve across three branches,
  fulfilled clearing, terminal-no-regress, idempotent re-delivery) on real Postgres.
- [x] `dotnet build` 0 errors / 0 warnings; full `dotnet test` green, no regressions.
- [x] No new `CritterBids.Contracts.*` type; no "Event" suffix; no "paddle".
- [x] Retro written with `**Prompt:**` header and `## Spec delta — landed?` paragraph.
- [x] No commit to `main`; one PR off `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

- **S5** — session & participant activity board (`operations-participants-events`, views 5a/5b). In
  scope for M7, deferred.
- **S6** — staff auth gating (`StaffOnly`, ADR 024 passphrase scheme), `StaffOnly` on the existing
  `ResolveDisputeEndpoint`, and the read-only query/HTTP endpoints over this view. In scope for M7,
  deferred. **S6's reader must account for the tolerant-seed decision** (filter to queue states).
- **M8** — the render-time `Title` join to the lot board (this view stores only `ListingId`).
- **S7** — end-to-end cross-BC journey test, `Program.cs` route audit, `bounded-contexts.md` status
  flip.
- **Out of scope, tracked elsewhere** — `Refund` settlement-reversal mechanics (post-MVP), re-homing
  `ResolveDispute` (stays in Obligations), SignalR `OperationsHub` (stays in Relay, ADR 023).
- **Skill gap noted, not edited** (AUTHORING rule 4): `docs/skills/marten-projections/SKILL.md`
  could gain a section on the non-monotone-state-machine guard (terminal-absorbing + open backward
  edge) to complement the existing monotone-rank guidance. Recorded here for a future skill-update
  session.
