# Workshop 005 — Obligations BC Deep Dive

**Type:** BC-Focused (vertical depth)
**Date started:** 2026-05-28
**Status:** Complete — Phases 1–5

**Scope:** The Obligations BC internals. The post-sale coordination saga (the canonical CritterBids "cancel and reschedule" Wolverine saga), its cancellable scheduled-message chain, the deadline-escalation path, the auto-confirm delivery mechanic, and the dispute sub-workflow. This workshop is the design source for M6-S1 (Obligations BC opening) and the first OpenSpec-adopting BC per ADR 021.

**Personas active:** `@Facilitator`, `@DomainExpert`, `@Architect`, `@BackendDeveloper`, `@QA`, with `@ProductOwner` called in for scope and demo-mode decisions, and `@FrontendDeveloper` / `@UX` for the storyboard phase.

**Prerequisites:** Workshops 001 (Flash Session demo-day journey) and 003 (Settlement BC) completed. This workshop picks up where Settlement hands off: `SettlementCompleted` is the trigger that starts the post-sale coordination saga.

**Implementation status:** All slices below carry status `design`. The Obligations BC is unshipped; M6-S1 is the opening implementation session. Per-slice status migrates to `planned` when the M6-S1 implementation prompt is authored.

**OpenSpec note (ADR 021):** Obligations is the first OpenSpec-adopting BC. The SHALL-form requirements derived from this workshop's slices land in `openspec/changes/add-obligation-lifecycle/` (capability `obligation-lifecycle`). This workshop is the journey-prose source; the OpenSpec change folder is authoritative for the SHALL-form requirement. Where they conflict at retrospective time, the conflict is a finding per ADR 016's four lanes.

---

## What Prior Workshops Established

From the vision docs and earlier workshops, Obligations has:

**Storage:** PostgreSQL via Marten (ADR 011 All-Marten Pivot). Saga state document plus read-model projections.

**Integration in:** `SettlementCompleted` (from Settlement BC). Per the M6 milestone doc, the payload carries `HammerPrice`, `FeeAmount`, `SellerPayout`, and the routing identities `ListingId`, `WinnerId`, `SellerId`.

**Integration out:** `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` — all consumed by Relay (M6) for participant/staff push, and by Operations (M7) for the dashboards.

**Internal events:** `PostSaleCoordinationStarted`, `ShippingReminderSent`, `DeadlineEscalated`, `DeliveryConfirmed`.

**Key role:** Obligations is the post-sale *coordinator*. It owns no money (Settlement does) and no bidding state (Auctions does). Its authority is over the **commitment chain**: did the seller ship, did delivery land, were the deadlines honored, and if not, how is the failure escalated and resolved.

**Defining pattern:** the cancellable-scheduled-message saga. Where Settlement (W003) is a *linear phased* workflow, Obligations is *state-driven with cancellable timers and recovery* — late tracking after escalation recovers the happy path. This is the project's canonical lived example of `bus.ScheduleAsync()` plus saga-state-driven cancellation.

---

## Narrative Cross-References

This workshop's happy-path slices (5.1–5.4) are implemented by **Narrative 006 — [`006-seller-fulfills-post-sale-obligation.md`](../narratives/006-seller-fulfills-post-sale-obligation.md)** (authored in the M6 design sequence after this workshop, per ADR 017 step 4). The narrative is forward-spec at authoring time (Obligations is unshipped; M6 ship target). Per the narratives README bidirectional-referencing convention, this consolidated block is the inverse index:

| W005 slice | Narrative 006 Moment |
|---|---|
| 5.1 — Start post-sale coordination | Moment 1 (the obligation begins) |
| 5.2 — Shipping reminder fires | Moment 2 (the reminder nudges him) |
| 5.3 — Seller provides tracking (cancels reminders) | Moment 3 (he ships and provides tracking) |
| 5.4 — Delivery auto-confirms → fulfilled | Moment 4 (delivery auto-confirms; obligation fulfilled) |

Slices 5.5–5.8 (escalation, recovery, and the two dispute slices) are deferred to successor narratives and are not yet cross-referenced.

---

## Ubiquitous Language

The Obligations BC owns the post-sale commitment lifecycle: from `SettlementCompleted` through `ObligationFulfilled` (happy path) or a resolved dispute (`DisputeResolved`). The financial workflow that precedes it is owned by Settlement ([W003 §3](./003-settlement-bc-deep-dive.md#ubiquitous-language)); the bidding lifecycle is owned by Auctions ([W002 §3](./002-auctions-bc-deep-dive.md#ubiquitous-language)).

Domain events are catalogued in [`docs/vision/domain-events.md`](../vision/domain-events.md); they are not duplicated here.

| Term | Definition | Notes |
|---|---|---|
| **Obligation** | The post-sale commitment between a winning bidder and a seller for a single sold listing. Identified by `ObligationId`. | One obligation per sold listing. Distinct from the Settlement that precedes it — Settlement moves the money; the Obligation coordinates the handoff of the item. |
| **ObligationId** | A deterministic UUID v5 derived from `ListingId` (`UuidV5(ObligationsIdentityNamespaces.PostSaleCoordination, $"obligation:{ListingId}")`). Idempotent by construction. | Mirrors W003's `SettlementId` strategy. Idempotency guards against duplicate `SettlementCompleted` delivery (Wolverine at-least-once). The namespace is Obligations-owned per BC-isolation discipline. |
| **Post-Sale Coordination Saga** | The Wolverine `Saga` that drives an obligation from `PostSaleCoordinationStarted` to a terminal `ObligationFulfilled` or resolved dispute. Hosts the cancellable reminder/escalation chain and the auto-confirm timer. | Hosting decision: **Wolverine Saga** (Decision 3; ADR-022 candidate, citing ADR-019 precedent). Not `ProcessManager<TState>` (JasperFx framework work, out of scope) and not handler-based process managers (right host for event-reactive *stateless* coordination — Relay's candidate, per W003 Approach B). |
| **Ship-By Deadline** | The timestamp by which the seller must provide tracking before the saga escalates. Carried on the `PostSaleCoordinationStarted` payload as saga state; not its own event. | Computed at saga start from `ObligationsOptions` (real or demo durations). Reminders and escalation are scheduled relative to it. A `DisputeResolved(Extension)` reschedules it. |
| **Shipping Reminder** | A scheduled nudge to the seller to provide tracking. One reminder fires before the deadline in MVP (Decision 2). | Produces `ShippingReminderSent` (internal). Cancelled when `TrackingInfoProvided` arrives. |
| **Deadline Escalation** | The non-terminal escalation that fires when the Ship-By Deadline passes with no tracking. Alerts Operations staff; the saga stays alive. | Produces `DeadlineEscalated` (internal, routed to Ops). **Non-terminal** (Decision 2): a late `TrackingInfoProvided` still recovers the happy path. |
| **Tracking Info** | The carrier tracking number the seller provides to discharge their shipping obligation. | Produces `TrackingInfoProvided` (integration). Cancels the pending reminder + escalation and schedules the auto-confirm `ConfirmDelivery` timer. MVP: in-process command seam; real carrier webhook is post-MVP. |
| **Delivery Confirmation** | The clock-triggered confirmation that delivery completed. Auto-fires `N` days after `TrackingInfoProvided` (Decision 1). | Produces `DeliveryConfirmed` (internal) → `ObligationFulfilled`. A *temporal-automation slice* (Bruun pattern): the trigger is the passage of time, not an incoming domain event. Buyer-initiated confirmation is post-MVP. |
| **Obligation Fulfilled** | The happy-path terminal state: both parties' post-sale obligations are complete (seller shipped, delivery confirmed). | Produces `ObligationFulfilled` (integration) → `MarkCompleted()`. The buyer's obligation (payment) was discharged at Settlement; their only remaining obligation (receipt confirmation) is auto-confirmed in MVP. |
| **Dispute** | A raised failure of the commitment chain — non-delivery, item condition, or a missed deadline. Identified by `DisputeId`. | MVP scope: open → ops-resolve → close. No appeals, no multi-round negotiation. `DisputeOpened` (integration); `DisputeResolved` (integration). |
| **Dispute Resolution** | The Operations-staff outcome of a dispute. One of `Refund | Extension | Closed`. | `Refund` and `Closed` are **terminal** → `MarkCompleted()`. `Extension` is **non-terminal** (Decision 5): reschedules a fresh Ship-By Deadline and continues the saga. The one deliberate path that does *not* call `MarkCompleted()`. |
| **Winner / Buyer** | The participant who won the listing. The obligation's buyer-side party. Identified by `WinnerId` (= the Auctions/Settlement `WinnerId`). | Same identity as W002/W003. Can raise a dispute (`Reason: NonDelivery | ItemCondition`). |
| **Seller** | The participant who listed the item. The obligation's seller-side party, responsible for shipping. Identified by `SellerId`. | Same identity as W003/W004. The reminder/escalation chain targets the seller. |
| **ObligationsOptions** | The config section carrying both production and demo-mode durations for the reminder, deadline, and auto-confirm timers (Decision 4). | `DemoMode` bool plus `DemoReminderSeconds` / `DemoDeadlineSeconds` / `DemoDeliverySeconds`, with real-duration counterparts. Resolves W001-6. Testable (inject short durations in integration tests) without recompile. |

---

## Phase 1 — Brain Dump: Verification Pass

The Obligations event vocabulary pre-exists in `domain-events.md`, so Phase 1 is a verification pass: walk the post-sale journey and confirm the vocabulary accounts for everything.

`@Facilitator` walked the spine from `SettlementCompleted` forward. `@DomainExpert` confirmed the spine matches eBay's post-sale flow (sale closes → seller ships → tracking entered → delivery → done). `@QA` surfaced three gaps; each resolved by persona consensus rather than a product call:

| Gap raised (`@QA`) | Resolution | Resolved by |
|---|---|---|
| No event for the saga setting its shipping **deadline** | Carry `ShipByDeadline` on the `PostSaleCoordinationStarted` payload (saga state), not as its own event. Reminders/escalation compute off it. | `@Architect` |
| No `RemindersCancelled` audit event | **No separate event.** `TrackingInfoProvided` *is* the audit record of why reminders stopped; cancellation is scheduled-message handling keyed on saga id. Adding it duplicates the record. | `@Architect` + `@BackendDeveloper` |
| No `DisputeClosed` distinct from `DisputeResolved` | **Resolve == close.** "Closed" is a `ResolutionType` on `DisputeResolved`, not a separate event. | `@DomainExpert` |

**Verified vocabulary (no new event types):**

| Event | Type | Role |
|---|---|---|
| `PostSaleCoordinationStarted` | 🟠 Internal | Saga starts on `SettlementCompleted`; payload carries `ShipByDeadline` |
| `ShippingReminderSent` | 🟠 Internal | A scheduled reminder fired to the seller |
| `DeadlineEscalated` | 🟠 Internal | Deadline passed without tracking; escalated to Ops (non-terminal) |
| `TrackingInfoProvided` | 🔵 Integration | Seller provided tracking; cancels reminder + escalation |
| `DeliveryConfirmed` | 🟠 Internal | Delivery auto-confirmed (clock-triggered, temporal-automation) |
| `ObligationFulfilled` | 🔵 Integration | Both parties complete; saga terminal → `MarkCompleted()` |
| `DisputeOpened` | 🔵 Integration | Dispute raised (winner or ops-from-escalation) |
| `DisputeResolved` | 🔵 Integration | Ops resolved; `ResolutionType: Refund | Extension | Closed` |

**Outcome:** the existing vocabulary is sufficient. No additions to `domain-events.md` required from this workshop.

---

## Phase 2 — Storytelling: The Post-Sale Timeline

The sequenced spine, with the auto-confirm decision (Decision 1) and the one-reminder non-terminal escalation (Decision 2) baked in:

```
SettlementCompleted  (from Settlement BC)
   │   payload: ListingId, WinnerId, SellerId, HammerPrice, FeeAmount, SellerPayout
   ▼
PostSaleCoordinationStarted   payload: ObligationId, ListingId, WinnerId, SellerId, ShipByDeadline
   │   schedules: SendShippingReminder(@ reminder offset)
   │   schedules: SendDeadlineEscalation(@ ShipByDeadline)
   │
   ├──── HAPPY PATH ───────────────────────────────────────────────────────────
   │   ShippingReminderSent      (reminder fires before the seller ships)
   │        │
   │        ▼
   │   TrackingInfoProvided      ──► cancels pending reminder + escalation
   │        │   schedules: ConfirmDelivery(@ now + delivery offset)   ← temporal-automation
   │        ▼
   │   DeliveryConfirmed         (auto, clock-triggered)
   │        ▼
   │   ObligationFulfilled       ──► MarkCompleted()
   │
   ├──── ESCALATION + RECOVERY ─────────────────────────────────────────────────
   │   (ShipByDeadline passes, no TrackingInfoProvided)
   │        ▼
   │   DeadlineEscalated         ──► Operations staff review (NON-terminal; saga stays alive)
   │        │
   │        ▼  (seller redeems late)
   │   TrackingInfoProvided      ──► recovers to happy path (schedules ConfirmDelivery)
   │        ▼
   │   DeliveryConfirmed ─► ObligationFulfilled ─► MarkCompleted()
   │
   └──── DISPUTE ───────────────────────────────────────────────────────────────
       DisputeOpened   (winner: NonDelivery | ItemCondition  •  ops-from-escalation: MissedDeadline)
            │
            ▼  (Operations staff works it)
       DisputeResolved
            ├── Refund     ──► terminal ──► MarkCompleted()
            ├── Closed     ──► terminal ──► MarkCompleted()
            └── Extension  ──► reschedules ShipByDeadline; saga CONTINUES (back to awaiting-tracking)
```

The saga is genuinely state-driven, not linear: escalation is a non-terminal branch, and both late tracking and a dispute `Extension` loop back into the awaiting-tracking state.

---

## Phase 3 — Storyboarding: UI → Command → Events → Views

`@FrontendDeveloper` and `@UX` joined. The frontend screens are deferred to M8 (per W001-13); the **backend command seams** land in M6-S1.

**Commands (blue):**

| Command | Trigger | Produces | MVP seam |
|---|---|---|---|
| *(integration handler on `SettlementCompleted`)* | Settlement BC integration event | `PostSaleCoordinationStarted` | Wolverine message handler |
| `SendShippingReminder` | scheduled (saga) | `ShippingReminderSent` | `bus.ScheduleAsync()` |
| `SendDeadlineEscalation` | scheduled (saga, @ deadline) | `DeadlineEscalated` | `bus.ScheduleAsync()` |
| `ProvideTracking` | seller (UI / stubbed carrier HTTP) | `TrackingInfoProvided` | in-process HTTP endpoint; real webhook post-MVP |
| `ConfirmDelivery` | scheduled (auto, @ tracking + N) | `DeliveryConfirmed`, `ObligationFulfilled` | `bus.ScheduleAsync()` |
| `OpenDispute` | winner (UI) | `DisputeOpened` | HTTP endpoint |
| `ResolveDispute` | Ops staff (UI) | `DisputeResolved` | HTTP endpoint |

**Views / read models (green):**

| Read model | Audience | Notes |
|---|---|---|
| `ObligationStatusView` | seller + winner | Per obligation: status, `ShipByDeadline`, tracking #, dispute state. Single-stream Marten projection off the obligation stream. |
| `ObligationsAwaitingDelivery*` | saga internals | Todo-list projection; rows added on `TrackingInfoProvided`, self-remove on `DeliveryConfirmed`. The asterisk marks the Bruun temporal-automation list (per narratives README notation). |
| `OperationsObligationsView` | Ops dashboard | Escalation queue + open-dispute queue. Consumed by the Operations BC dashboards in M7. |

**Screens (white, deferred to M8 frontend):** seller "Provide Tracking," winner "Order Status / Open Dispute," Ops "Disputes & Escalations."

---

## Phase 4 — Slices

`@Facilitator`, `@ProductOwner`, and `@BackendDeveloper` drew the vertical cuts. Each slice is independently deliverable and testable.

| # | Slice | Command | Events | View | BC | Priority |
|---|---|---|---|---|---|---|
| 5.1 | Start post-sale coordination | *(handler on `SettlementCompleted`)* | `PostSaleCoordinationStarted` | `ObligationStatusView` (awaiting shipment) | Obligations | **P0** |
| 5.2 | Shipping reminder fires | `SendShippingReminder` *(scheduled)* | `ShippingReminderSent` | `ObligationStatusView` (last reminded) | Obligations | **P0** |
| 5.3 | Seller provides tracking (cancels reminders) | `ProvideTracking` | `TrackingInfoProvided` | `ObligationStatusView` (shipped, tracking #) | Obligations | **P0** |
| 5.4 | Delivery auto-confirms → fulfilled | `ConfirmDelivery` *(scheduled, auto)* | `DeliveryConfirmed`, `ObligationFulfilled` | `ObligationStatusView` (fulfilled) | Obligations | **P0** |
| 5.5 | Deadline escalation (non-terminal) | `SendDeadlineEscalation` *(scheduled)* | `DeadlineEscalated` | `OperationsObligationsView` (escalations) | Obligations | **P0** |
| 5.6 | Late tracking recovery | `ProvideTracking` *(post-escalation)* | `TrackingInfoProvided` | `ObligationStatusView` | Obligations | **P1** |
| 5.7 | Winner opens dispute | `OpenDispute` | `DisputeOpened` | `OperationsObligationsView` (disputes) | Obligations | **P1** |
| 5.8 | Ops resolves dispute | `ResolveDispute` | `DisputeResolved` | `OperationsObligationsView` | Obligations | **P1** |

**Slice notes:**
- 5.6 is largely the 5.3 handler being *state-tolerant* (it must accept `ProvideTracking` from the escalated state, not only the awaiting-shipment state). It is cut as its own slice because it exercises the recovery transition, which is the saga's defining behavior.
- 5.8's `Extension` resolution is the one path that does **not** call `MarkCompleted()` — it reschedules a fresh `ShipByDeadline` and the saga returns to the awaiting-tracking state, reusing 5.1's scheduling path.

---

## Phase 5 — Scenarios

Given/When/Then scenarios for all eight slices live in the companion file [`005-scenarios.md`](./005-scenarios.md). Highlights of the edge cases `@QA` stress-tested:

- **Idempotent start:** a duplicate `SettlementCompleted` (Wolverine at-least-once delivery) must not start a second saga — `ObligationId` is deterministic from `ListingId`, so the second start is a no-op.
- **Stale reminder after tracking:** if `SendShippingReminder` fires after `TrackingInfoProvided` already arrived (a race), the handler no-ops on saga state — defense in depth alongside scheduled-message cancellation.
- **Late tracking after escalation:** `ProvideTracking` from the escalated state recovers the happy path (slice 5.6).
- **Extension loop:** `DisputeResolved(Extension)` reschedules the deadline and the saga survives; a subsequent missed deadline can escalate again.
- **Demo-mode timing:** with `ObligationsOptions.DemoMode = true`, the entire chain (reminder → deadline → auto-confirm) collapses to seconds so it runs live on a projector.

---

## Decisions Log

| # | Decision | Outcome | Disposition |
|---|---|---|---|
| 1 | What completes the buyer's obligation? | **Auto-confirm**: `DeliveryConfirmed` auto-fires `N` days after `TrackingInfoProvided`; no buyer command in MVP. Buyer-initiated confirmation is post-MVP. | Recorded here; informs slice 5.4 (temporal-automation). |
| 2 | Reminder / escalation chain shape | **One reminder, then escalate.** `DeadlineEscalated` is **non-terminal** (alerts Ops, saga stays alive); late tracking recovers the happy path. | Recorded here; informs slices 5.2, 5.5, 5.6. |
| 3 | Saga hosting | **Wolverine Saga.** Not `ProcessManager<TState>` (JasperFx framework work, not shipping) and not handler-based process managers (right host for *stateless* event-reactive coordination — Relay candidate). | **ADR-022 candidate**, citing ADR-019 (Settlement) as precedent. Authored in the M6-S1 design opening. |
| 4 | Demo-mode timeout config (resolves W001-6) | **`ObligationsOptions`** config section with real + demo durations (`DemoMode` bool + `Demo*Seconds` keys). Testable, no recompile. | Recorded here; W001-6 moved to resolved. |
| 5 | Dispute resolution semantics | **Three resolutions.** `Refund` + `Closed` terminate (`MarkCompleted()`); `Extension` reschedules the deadline and the saga continues (the one deliberate non-`MarkCompleted()` path). | Recorded here; informs slice 5.8. |

---

## Parked Questions Raised

| ID | Question | Target | Notes |
|---|---|---|---|
| W005-1 | Real carrier-tracking webhook receiver (replacing the stubbed in-process `ProvideTracking` seam) | Obligations BC (post-MVP) | MVP uses an in-process command seam. Real webhook integration is post-MVP. |
| W005-2 | Buyer-initiated delivery confirmation (`ConfirmDelivery` as a winner command, not only auto) | Obligations BC (post-MVP) | MVP auto-confirms after `N` days. A buyer "Confirm receipt" action is a post-MVP enhancement. |
| W005-3 | Seller-opened disputes / multi-round negotiation | Obligations BC (post-MVP) | MVP: winner opens (NonDelivery / ItemCondition) + ops-from-escalation (MissedDeadline). No appeals. |
| W005-4 | `N`-days auto-confirm window — platform default, per-category, or configurable? | Obligations BC / config | MVP: a single `ObligationsOptions` value. Per-category windows are a post-MVP fee/policy concern. |

W001-6 (demo-mode timeout config) is **resolved** by Decision 4 — see `PARKED-QUESTIONS.md`.

---

## Document History

| Date | Prompt / Session | Amendment |
|---|---|---|
| 2026-05-28 | M6 Obligations BC opening — Event Modeling workshop session (W005) | Initial authoring. Five structural decisions logged (auto-confirm delivery, one-reminder non-terminal escalation, Wolverine Saga hosting, `ObligationsOptions` demo config, three-way dispute resolution with non-terminal `Extension`). Eight slices defined. W001-6 resolved; four W005 parked questions raised. ADR-022 (saga hosting) flagged as a candidate for the M6-S1 design opening. |
