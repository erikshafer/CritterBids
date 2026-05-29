# M6-S4: Obligations Saga — Escalation + Dispute Sub-Workflow - Retrospective

**Date:** 2026-05-29
**Milestone:** M6 - Obligations BC + Relay BC
**Slice:** S4 - saga failure paths (missed-deadline escalation, late-tracking recovery, dispute sub-workflow); closes the `add-obligation-lifecycle` change
**Agent:** @PSA
**Prompt:** `docs/prompts/implementations/M6-S4-obligations-escalation-dispute.md`

## Baseline

- Branch stacked on `erikshafer/m6-s4-prompt-authoring` (PR #50, not yet on `main`); the S4 prompt and narratives 007/008 were authored there.
- S3 left the happy path green: `PostSaleCoordinationSaga` (start → reminder → tracking → auto-confirm → fulfilled), `ObligationStatusView`, and the cancellable timer chain. `SendDeadlineEscalation` existed only as a routable no-op stub.
- `ObligationStatus` already carried `Escalated` and `Disputed`; four integration contracts were frozen at M6-S1 (`TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved`) — `DeadlineEscalated` did not exist.
- Full solution green before the slice; only the pre-existing NU1904 Marten 8.35.0 advisory warning.
- `openspec validate add-obligation-lifecycle --strict` passing with tasks 6.1, 6.2, 7.1–7.4, 8.2, 8.3, 9.4, 9.5, 9.6 open.

## Items completed

| Item | Description |
|------|-------------|
| S4 (1) | `Handle(SendDeadlineEscalation)` body — append + emit `DeadlineEscalated`, advance to non-terminal `Escalated` |
| S4 (2) | `DeadlineEscalated` — fifth Obligations integration contract (ADR 005 additive) |
| S4 (3) | State-tolerant `ProvideTracking` — recovers from `Escalated` |
| S4 (4) | `OpenDispute` command + `[WolverinePost]` `[AllowAnonymous]` endpoint |
| S4 (5) | `ResolveDispute` command + endpoint |
| S4 (6) | Dispute terminal-vs-non-terminal branch (`Refund`/`Closed` → `MarkCompleted()`; `Extension` → reschedule + continue) |
| S4 (7) | `ObligationsAwaitingDelivery*` Inline projection (row on `TrackingInfoProvided`, self-remove on `DeliveryConfirmed`) |
| S4 (8) | opsx 8.3 `OperationsObligationsView` deferred-to-M7 via `tasks.md` change-scope edit (not built) |
| S4 (9) | Module + publish-only `relay-obligations-events` route wiring |
| S4 (10) | Escalation/recovery + dispute (Refund/Extension/Closed) tests |
| S4 (11) | `add-obligation-lifecycle` tasks checked off, 8.3 deferred, change archived |

## S4 (2): `DeadlineEscalated` as a fifth integration event

**Why this approach.** The milestone's queue tables omitted `DeadlineEscalated` while its prose said Operations is notified "via `DeadlineEscalated` on `relay-obligations-events`" and `design.md` called it "(internal)." The authoring session resolved the contradiction by promoting it to a published contract: real-time ops alerting needs the fact to cross the broker, not merely sit in a read model. It follows the S3 `TrackingInfoProvided` **append+emit** taxonomy — appended to the obligation stream *and* returned via `OutgoingMessages`. Shape mirrors the existing contracts: `(Guid ObligationId, Guid ListingId, DateTimeOffset EscalatedAt)`.

## S4 (6): Extension reschedule and the projection rebuild-correctness gap

**Why this approach.** The frozen `DisputeResolved` contract carries only `(DisputeId, ResolutionType)` — it cannot carry the recomputed `ShipByDeadline`. A projection rebuilding `ObligationStatusView` from the stream would therefore not know the post-extension deadline. Resolution: append an **internal** `ShipByDeadlineExtended(ObligationId, NewShipByDeadline, ExtendedAt)` stream event alongside `DisputeResolved`. `Apply(ShipByDeadlineExtended)` replays the new deadline and resets `Status = AwaitingShipment`; `Apply(DisputeResolved)` records the resolution fields. The internal event stays out of `CritterBids.Contracts` — no wire-contract change.

**Handler shape after** (`Extension` branch): append `DisputeResolved`, append `ShipByDeadlineExtended`, `bus.ScheduleAsync` fresh reminder + escalation, update saga state, `DisputeId = null`, `Status = AwaitingShipment`, **no** `MarkCompleted()`. `Refund`/`Closed`/unrecognized fall through to `DisputeId = null; MarkCompleted()`.

## S4 (10): Cross-test scheduled-message pollution

**Discovery / resolution.** The escalation/recovery test first failed:

```
Shouldly.ShouldAssertException : await QueryScheduledOfTypeAsync(typeof(ConfirmDelivery))
should have single item but had 4 items
```

Root cause: Wolverine's scheduled-message store lives in its own envelope tables, which `CleanAllMartenDataAsync` does **not** truncate. Scheduled `ConfirmDelivery` messages (never cancelled on the happy path) accumulate across tests sharing the collection's host. The existing `PostSaleCoordinationSagaTests` global `ShouldHaveSingleItem` assertions only pass because that class runs first against an empty store. Resolution: my failure-path tests assert **before/after deltas** (`CountScheduledOfTypeAsync` + `ShouldBe(before ± 1)`) instead of absolute counts, making them isolation-independent regardless of run order. The existing S3 tests were left unchanged (out of scope; they pass).

## Test results

| Phase | Obligations Tests | Result |
|-------|-------------------|--------|
| After source impl (first run) | 12 / 13 | 1 fail — scheduled-store pollution |
| After delta-assertion fix | 13 / 13 | green |
| Full `dotnet test CritterBids.slnx` | all projects | green |

Full solution: Contracts 1, Api 1, Obligations 13, Participants 6, Listings 20, Selling 36, Settlement 25, Auctions 65 — all passing. Obligations test count: +7 (the `ObligationsFailurePathsTests` class).

## Build state at session close

- `dotnet build`: 0 errors.
- Warnings: unchanged from baseline — only the pre-existing NU1904 Marten 8.35.0 advisory across the projects that reference Marten. No new warnings.
- `MarkCompleted()` on every dispute terminal path: `Refund`, `Closed`, and the defensive unrecognized-resolution fall-through all terminate; `Extension` is the sole non-terminating branch.
- `IMessageBus` usage in the saga: confined to `bus.ScheduleAsync()` (the one justified use per CLAUDE.md). No `IMessageBus` send/publish/invoke.
- Cancellation via `IMessageStore.ScheduledMessages.CancelAsync(ScheduledMessageQuery)` — `bus.CancelScheduledAsync` usage: 0.
- `session.Store()` calls in the saga: 0 (handlers return events/`OutgoingMessages`).

## Key learnings

1. **Wolverine's scheduled-message store is not Marten-cleaned.** Tests that assert on scheduled-message counts must use before/after deltas, not absolute counts, because `CleanAllMartenDataAsync` does not touch the envelope tables and counts accumulate across a shared-host collection. Absolute-count assertions are silently order-dependent.
2. **A frozen contract that can't carry replay-critical state needs an internal companion event.** When a projection must rebuild a value the wire contract omits (here, the post-`Extension` deadline), append an internal stream event carrying it rather than widening the public contract.
3. **append+emit is the established Obligations taxonomy for cross-BC facts** — appended to the stream for replay/audit and returned via `OutgoingMessages` for the broker. Reused unchanged from S3 `TrackingInfoProvided` for all three new emitted events.

## Findings against narrative

Both narratives implemented as drafted — `document-as-intentional`.

- **Narrative 007** (seller — escalation → late-tracking recovery): the non-terminal escalation and state-tolerant `ProvideTracking` recovery shipped exactly as dramatised; the `DeadlineEscalated`-as-integration-event note it carried landed as the fifth contract. No drift.
- **Narrative 008** (operator — dispute sub-workflow, `Extension` resolution): the three-way resolution with `Extension` as the one non-terminal path shipped as written. The `OperationsObligationsView` it names as forward-spec remains M7 Operations-owned, consistent with the narrative's own framing — not drift, but the documented M7 boundary. No `code-update` follow-up warranted.

## Spec delta - landed?

**Landed as written.** The `add-obligation-lifecycle` delta spec's missed-deadline, late-tracking-recovery, and dispute requirements are now runnable: `DeadlineEscalated` (published integration event, ADR 005 additive), the state-tolerant `ProvideTracking` recovery, and the `OpenDispute`/`ResolveDispute` intake with three-way `Refund`/`Closed`/`Extension` branching all gained implementations and integration coverage, surfaced through the new `ObligationsAwaitingDelivery*` read model. Task 8.3's `OperationsObligationsView` was reframed as M7 Operations-BC work and deferred via a `tasks.md` change-scope edit; `openspec validate add-obligation-lifecycle --strict` still passes. Tasks 6.1, 6.2, 7.1–7.4, 8.2, 9.4, 9.5, 9.6 are checked, 8.3 deferred, and the change is archived via `openspec-archive-change` — the first M6 OpenSpec change to close. Narratives 007 and 008 gained Document-History rows promoting them `draft` → `accepted` at session close. See `openspec/changes/archive/` for the archived change and `docs/narratives/00{7,8}-*.md` § Document History.

## Carried open questions — disposition

- **`wolverine-sagas.md` cancellation API still wrong (owed skill correction).** The skill documents a non-existent `bus.CancelScheduledAsync(id)`; the working API for Wolverine 5.39.3 is `IMessageStore.ScheduledMessages.CancelAsync(ScheduledMessageQuery)` keyed on the scheduled instant + message type. S4's `OpenDispute` timer-freeze and `Extension` reschedule both exercise it, following the `AuctionClosingSaga` precedent (not the skill). **Skill not edited in-session** per AUTHORING rule 4 — the correction remains owed as a doc-pass item, now carried across S3 → S4.
- **Same-instant cross-cancel precision (carried).** `CancelScheduledAsync` brackets the pending message with a ±100ms window on the scheduled instant + message type, not the saga id. Two obligations with timers at an identical instant could cross-cancel. Demo/test instants are `DateTimeOffset.UtcNow`-derived and unique, so it is not exercised; the production limitation persists and is documented on the helper (same limitation `AuctionClosingSaga` carries).
- **Dispute reason/resolution representation — RESOLVED: shared internal constants, string match.** Rather than a private saga enum or scattered string literals, an `internal static class DisputeResolutions` holds `Refund`/`Extension`/`Closed` consts, referenced by both the saga's branch logic and the projection's `Apply(DisputeResolved)`. One source of truth for the magic strings without touching the string-valued wire contract (the `ListingPassed.Reason` precedent).

## Deferred / not implemented

- **Timer generation-token robustness.** An escalation or reminder redelivered after an `Extension` re-arm could in principle re-fire against the new cycle. Mitigated by `UseDurableLocalQueues()` inbox dedupe and unique demo/test instants; a generation token would touch S3 happy-path scheduling code (out of scope this slice). Flagged for a future hardening pass if the Relay/Operations consumers surface it.

## Verification checklist

- [x] `Handle(SendDeadlineEscalation)` appends `DeadlineEscalated`, advances to non-terminal `Escalated`, no-ops when state advanced, no `MarkCompleted()`.
- [x] `DeadlineEscalated` sealed record in `CritterBids.Contracts.Obligations` (`ObligationId` + `ListingId` + `EscalatedAt`), appended + emitted; ADR 005 additive recorded.
- [x] `ProvideTracking` recovers from `Escalated` → `Shipped` → auto-confirms to `Fulfilled`.
- [x] `OpenDispute` + `ResolveDispute` `[WolverinePost]` `[AllowAnonymous]` endpoints cascade their commands.
- [x] `Handle(OpenDispute)` appends + emits `DisputeOpened` and sets `Disputed` without terminating.
- [x] `Handle(ResolveDispute)` appends + emits `DisputeResolved`; `Refund`/`Closed` `MarkCompleted()`; `Extension` recomputes + reschedules and continues.
- [x] `ObligationsAwaitingDelivery*` Inline projection adds a row on `TrackingInfoProvided`, self-removes on `DeliveryConfirmed`.
- [x] `OperationsObligationsView` not built; `tasks.md` marks 8.3 deferred-to-M7; `openspec validate --strict` passes.
- [x] `AddObligationsModule()` registers `DeadlineEscalated` + projection; `Program.cs` has publish-only routes for `DeadlineEscalated` + `DisputeOpened` + `DisputeResolved`.
- [x] Escalation/recovery + dispute tests green, asserting `DeadlineEscalated` cascades, using the demo-duration fixture with deterministic driving.
- [x] `dotnet build` 0 errors; full `dotnet test CritterBids.slnx` green, no regressions.
- [x] `openspec validate add-obligation-lifecycle --strict` passes; 6.1/6.2/7.1–7.4/8.2/9.4/9.5/9.6 checked, 8.3 deferred; change archived.
- [x] Narratives 007 and 008 promoted `draft` → `accepted` with a Document-History row.
- [x] This retrospective written with `## Spec delta - landed?`.
- [x] No commit to `main`; no `Co-Authored-By` trailer.

## What remains / next session should verify

- **Relay BC consumers (S5–S7):** the `relay-obligations-events` routes are publish-only this slice; the Relay project that `ListenTo`s them is S5–S7.
- **Operations BC read model (M7):** `OperationsObligationsView` (the escalation/dispute operator queues) subscribes to the events S4 publishes — built when Operations adopts those streams.
- **Owed doc-pass:** the `wolverine-sagas.md` cancellation-API correction (carried S3 → S4) is still unfixed; fold into the next skill doc-pass.
