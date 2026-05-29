---
slug: 007-seller-recovers-missed-shipping-deadline
status: draft
journey: seller
perspective: single-seller
scope: missed-deadline-recovery
bounded_contexts: [Obligations]
boundaries_touched: [Operations, Relay, Settlement, Auctions, Selling, Participants, Listings]
slices_implemented: [5.5, 5.6, 5.4]
canonical_id: ObligationId
---

# Seller Recovers a Missed Shipping Deadline (Escalation → Late-Tracking Recovery)

An Obligations-grain narrative, and the first of the two `alternate-path-failure` successors that narrative 006 deferred. Where 006 told the clean run — GreyOwl12 ships inside the deadline and the obligation auto-confirms — this narrative tells what happens when the seller is *slow*: the ship-by deadline passes with no tracking, the saga escalates, and then the seller ships late and the obligation **recovers** to the happy path. It dramatises W005 Decision 2's defining property — *one reminder, then a **non-terminal** escalation* — from the seller's vantage: the escalation is a pressure signal, not a death sentence, and late tracking still closes the loop. Single seller, single sold listing, escalation fires once, recovery succeeds on the first late tracking entry, no dispute. The dispute sub-workflow — including the case where an escalation is *not* recovered and becomes a dispute — belongs to narrative 008, not as a branch inside this story.

This narrative is **forward-spec**: the escalation and recovery transitions (W005 slices 5.5, 5.6) ship in M6-S4 and were not yet code when this was authored. The happy-path tail it rejoins (auto-confirm, slice 5.4) shipped in M6-S3. One architectural decision surfaced and is recorded outside this narrative rather than resolved in it: `DeadlineEscalated`, which W005 §7 labels "internal, routed to Ops," is promoted to an Obligations integration event so Operations can be alerted in real time (an ADR 005 additive contract change, recorded in the M6-S4 prompt). The narrative names the escalation reaching Operations as the system is designed to run; the routing mechanism is skill/ADR territory, not narrative territory.

## Cast

- **GreyOwl12** — the seller, protagonist. Continuity from narratives 004, 005, and 006, where he registered, published, sold, and fulfilled the Vintage Mechanical Keyboard cleanly. Here he is selling a different listing — a **Vintage Film Camera** that closed in a later Flash session — and this time life gets in the way: he does not ship before the deadline. Single protagonist; the narrative is told from his vantage. He is an observer in Moment 1 (the escalation fires while he is inactive) and Moment 3 (the recovered obligation auto-confirms), and acts once in Moment 2 (he finally ships and enters tracking).
- **The Post-Sale Coordination saga** — onstage across all three Moments. The Wolverine `Saga` (ADR-022) driving the obligation. This narrative dramatises its **non-terminal escalation** and **state-tolerant recovery** — the transitions 006 kept off-page — and shows that the saga stays alive through `Escalated` so that a late `TrackingInfoProvided` rejoins the awaiting-delivery flow.
- **The `ObligationStatusView`** — onstage in every Moment. The single-stream Marten projection backing GreyOwl12's seller dashboard: status, `ShipByDeadline`, tracking, escalation state. The surface through which he perceives the deadline turning against him and then recovering.
- **QuietBadger88** — the winner. Offstage but named. The buyer-side party of this obligation (`WinnerId`); she receives the `TrackingInfoProvided` and `ObligationFulfilled` broadcasts via Relay in Moments 2 and 3 but never appears in GreyOwl12's view. She does not open a dispute in this narrative — a winner who *does* is narrative 008's story.
- **Operations** — the bounded context whose staff are alerted when the obligation escalates. Onstage but offstage-of-GreyOwl12 in Moment 1: the narrator names the escalation landing in the Operations escalation queue, but GreyOwl12 never sees the Operations floor. The operator's own vantage on that queue is narrative 008's perspective. The Operations read model that surfaces the queue is M7 work (see Deferred).
- **Relay** — the bounded context whose hubs broadcast `TrackingInfoProvided` and `ObligationFulfilled` to the winner and (via the staff hub) the escalation alert to Operations. Onstage in Moments 1–3. Forward-spec; Relay BC ships M6-S5–S7. The Moments narrate the broadcasts as the system is designed to run.

## Setting

Some days after the keyboard sale of narrative 006. Another Flash session at a later demo has resolved the Vintage Film Camera to `ListingSold`; QuietBadger88 has been charged and GreyOwl12 has been paid out, exactly as narrative 002 traced for the keyboard. The `SettlementCompleted` for the camera has committed, the post-sale coordination saga has started against it, and the obligation is awaiting GreyOwl12's shipment. His seller dashboard shows the camera with `Status: "Awaiting shipment"`, a `ShipByDeadline`, and — by the time this narrative opens — a reminder already fired and acknowledged.

The system's MVP infrastructure is healthy: Wolverine is processing messages, Marten's event store on PostgreSQL (ADR 011) is up, the cross-BC RabbitMQ queues are draining, and Wolverine's scheduled-message store holds the saga's pending `SendDeadlineEscalation` timer. `ObligationsOptions` is bound with `DemoMode = true` for the conference run: the demo offsets collapse the chain so it executes on the projector in seconds — the reminder fires shortly after start, the ship-by deadline lands a short interval later, and the auto-confirm fires a short interval after tracking arrives. The production-duration counterparts (reminder days before the deadline, a multi-day ship-by window, an auto-confirm window of several days) are the config alternates on the same keys; the saga's transitions are identical under either set. The exact demo second-counts are an `implementation-detail` of `ObligationsOptions`, named here only as relative offsets.

What distinguishes this run from narrative 006 is one fact established before Moment 1: GreyOwl12 does **not** act on the reminder. The single courtesy nudge of W005 Decision 2 has fired and gone un-actioned, and the `SendDeadlineEscalation` timer is still pending, counting down to the `ShipByDeadline` with no tracking in sight.

## Moment 1: The deadline passes — the obligation escalates

**Implements:** W005 slice 5.5.

**Context.** The saga's state is awaiting-shipment. The reminder fired earlier and GreyOwl12 saw it, but he has not boxed the camera. The `SendDeadlineEscalation` message scheduled at saga start is still pending in the scheduled-message store, and the `ShipByDeadline` carried on saga state is about to arrive. GreyOwl12's dashboard still reads "Action needed: ship your item by <deadline>," the deadline now imminent.

**Interaction.** The scheduled `SendDeadlineEscalation` reaches the `ShipByDeadline` and Wolverine delivers it to the saga while the state is still awaiting-shipment — tracking never arrived to cancel it. In the demo run this is seconds after the reminder; in production it is the ship-by-deadline alternate. No human acts: this is the clock-triggered beat where the *absence* of an expected action (the seller's shipment) is itself the trigger.

**Response.** The saga emits `DeadlineEscalated { ObligationId, ListingId: camera, EscalatedAt: <now> }`, appended to the `ObligationId` stream; the saga's state advances from awaiting-shipment to `Escalated`. Critically — this is W005 Decision 2 — the saga does **not** call `MarkCompleted()`: `Escalated` is a *non-terminal* state, and the saga stays alive, still able to receive a late `ProvideTracking`. The escalation is also published as an integration event to Operations: Relay's staff hub surfaces a "missed deadline" alert and the obligation lands in the Operations escalation queue, where an associate could choose to open a dispute (narrative 008's entry point) — but in this narrative no one does. The `ObligationStatusView` updates to `Status: "Overdue — under review"`. GreyOwl12's dashboard ticks the camera's row from "Action needed" to "Overdue — your deadline passed; this sale is under review."

**Why this matters to the seller.** The deadline turning into an escalation is the platform's escalation of *attention*, not a penalty that ends the sale. GreyOwl12 now sees, in plain language, that he has missed a commitment the system was watching — but the same screen that tells him so is still capable of accepting his tracking number, because the saga deliberately stayed alive. The non-terminal escalation is the design choice that protects a seller who is merely slow: the system raises the stakes (Operations is now watching) without foreclosing the ordinary recovery. The pressure is real; the door is still open.

### Things deliberately not included

- An Operations associate opening a dispute against the escalated obligation (`Reason: MissedDeadline`), W005 slice 5.7. That is narrative 008's entry, not this journey. *(`separate-narrative`.)*
- A multi-escalation cadence (a second escalation if the first goes un-actioned). MVP escalates once and waits. *(`post-MVP`.)*
- The Operations-staff vantage on the escalation queue and the read model that surfaces it. *(`separate-narrative`; the read model is M7 Operations work — see Deferred.)*
- The exact demo second-count on the `ShipByDeadline`. *(`implementation-detail`.)*

## Moment 2: He ships late and the obligation recovers

**Implements:** W005 slice 5.6.

**Context.** The saga's state is `Escalated`. The escalation has fired and is visible to Operations; GreyOwl12's dashboard reads "Overdue — under review." No dispute has been opened. GreyOwl12 has now boxed the camera, taken it to the carrier, and has a tracking number in hand. This is the one Moment in the narrative where the protagonist acts.

**Interaction.** GreyOwl12 enters the carrier and tracking number in the seller "Provide Tracking" form — the same in-process HTTP command seam narrative 006 used (the real carrier webhook is `post-MVP`, W005-1). The endpoint issues `ProvideTracking { ObligationId, Carrier, TrackingNumber }` to the saga, but this time the saga receives it against `state = Escalated`, not awaiting-shipment. The recovery turns on the saga's command being **state-tolerant**: it accepts tracking from the escalated state and treats it as the discharge it would have been had it arrived on time.

**Response.** The saga emits `TrackingInfoProvided { ObligationId, ListingId: camera, Carrier, TrackingNumber, ProvidedAt: <now> }`, appended to the `ObligationId` stream; the saga's state advances from `Escalated` to awaiting-delivery — it has *recovered* the happy path. Any residual pending escalation slot is cancelled defensively by scheduled-message cancellation keyed on the obligation, so no further escalation can fire. In the same transition the saga schedules a single new cancellable `ConfirmDelivery` at `now + delivery offset` through `bus.ScheduleAsync()` — the same auto-confirm timer narrative 006 Moment 3 scheduled, now reached by way of recovery rather than an on-time shipment. The `ObligationStatusView` updates to `Status: "Shipped"`, `TrackingNumber: <value>`, clearing the "under review" flag. Because `TrackingInfoProvided` is an integration event, Relay broadcasts it: QuietBadger88's order view ticks to "Your item shipped — tracking #<value>," and the Operations escalation queue drops the camera's obligation, the missed-deadline alert resolved by the seller's own action. GreyOwl12's dashboard ticks the camera's row from "Overdue — under review" to "Shipped — tracking #<value>; delivery confirmation pending."

**Why this matters to the seller.** This is the payoff of the non-terminal escalation: GreyOwl12's late shipment is not a dead end but a recovery, and the act of providing tracking is the act that both discharges his obligation *and* clears the escalation watching it. From his vantage the system forgave a missed deadline the moment he made good on it — no appeal, no penalty box, no separate "request reinstatement" flow. The same cancellation mechanic that protected the on-time seller in narrative 006 (tracking cancels the pending escalation) here does double duty: it both schedules the close and retires the escalation that had already fired. The obligation is now the buyer's and the clock's to close, exactly as it would have been on the happy path.

### Things deliberately not included

- A carrier-rejected or malformed tracking number on the late `ProvideTracking` (a validation-failure path). *(`alternate-path-failure`.)*
- The case where the seller never ships and the escalation becomes a dispute the operator must resolve. *(`separate-narrative`; narrative 008.)*
- The "Provide Tracking" form's screen design. *(`UX-or-UI-detail`.)*

## Moment 3: Delivery auto-confirms; the recovered obligation is fulfilled

**Implements:** W005 slice 5.4.

**Context.** The saga's state is awaiting-delivery, reached by recovery rather than an on-time shipment. The `ConfirmDelivery` message scheduled in Moment 2 is pending in the scheduled-message store. GreyOwl12's dashboard reads "Shipped — delivery confirmation pending"; QuietBadger88's order view reads "Your item shipped." Neither party has further action in MVP — the buyer's receipt confirmation is auto-discharged (W005 Decision 1).

**Interaction.** The `ConfirmDelivery` message reaches its offset and Wolverine delivers it to the saga. As in narrative 006 Moment 4, no human acts: this is the clock-triggered temporal-automation beat, identical whether the obligation reached awaiting-delivery on time or by recovery.

**Response.** The saga emits `DeliveryConfirmed { ObligationId, ConfirmedAt: <now> }` and, as the terminal transition, `ObligationFulfilled { ObligationId, ListingId: camera, WinnerId: QuietBadger88, SellerId: GreyOwl12, FulfilledAt: <now> }`, both appended to the `ObligationId` stream; the saga calls `MarkCompleted()`, retiring the saga state. The `ObligationStatusView` updates to `Status: "Fulfilled"`. Relay broadcasts `ObligationFulfilled` to QuietBadger88 ("Delivery confirmed — order complete"), and the obligation drops from every active Operations queue. GreyOwl12's dashboard ticks the camera's row to "Completed."

**Why this matters to the seller.** The recovered path terminates exactly as the clean path did: `MarkCompleted()`, the ordinary saga terminal. From GreyOwl12's vantage, missing the deadline cost him an "under review" flag for a short while and nothing more — the sale closed, the buyer was served, and the obligation retired. The narrative's whole point lands here: a late seller and an on-time seller reach the *same* terminal state, because the escalation in between was non-terminal by design. The only path that does not end in `MarkCompleted()` — the dispute-`Extension` resolution — is one this obligation never entered; it belongs to narrative 008.

### Things deliberately not included

- Buyer-initiated delivery confirmation rather than the auto-timer (W005-2). *(`post-MVP`.)*
- The dispute sub-workflow's terminal and non-terminal resolutions (W005 slices 5.7, 5.8). *(`separate-narrative`; narrative 008.)*

## Deferred from this narrative

Cumulative aggregation of the per-Moment deferred items, bucketed by disposition.

### `post-MVP`

- A multi-escalation cadence — a second escalation if the first goes un-actioned — rather than the single MVP escalation. Moment 1.
- Real carrier-tracking webhook receiver replacing the in-process `ProvideTracking` command seam (W005-1). Moment 2.
- Buyer-initiated delivery confirmation rather than the auto-timer (W005-2). Moment 3.

### `alternate-path-failure`

- A carrier-rejected or malformed tracking number on the late `ProvideTracking`. Moment 2.

### `separate-narrative`

- An Operations associate opening a dispute against an escalated obligation that is *not* recovered (`Reason: MissedDeadline`), and the three dispute resolutions — `Refund` and `Closed` (terminal), `Extension` (non-terminal) — W005 slices 5.7, 5.8. Moments 1, 2, 3. This is narrative 008.
- The Operations-staff vantage on the escalation and dispute queues. Moment 1. Narrative 008 tells the operator's perspective.

### `implementation-detail`

- The `DeadlineEscalated` routing mechanism that carries the escalation to Operations: its promotion from a W005-labelled "internal" event to a published Obligations integration event is an ADR 005 additive contract change, recorded in the M6-S4 implementation prompt, not resolved here. Moment 1.
- The exact demo second-counts on the `ShipByDeadline`, reminder, and auto-confirm offsets; `ObligationsOptions` territory. Setting, Moments 1 and 3.
- The Wolverine `Saga` host primitive beyond naming it (ADR-022). Cast / all Moments.

### `UX-or-UI-detail`

- The seller dashboard's "Overdue — under review" and "Shipped" states and the "Provide Tracking" form screen designs (M8 frontend MVP). Moments 1–3.

## Retrospective

### What this narrative establishes

Narrative 007 is the first of narrative 006's two deferred successors and the first CritterBids narrative to dramatise a *failure-and-recovery* journey rather than a clean path. It makes W005 Decision 2's most important property legible from the seller's vantage: the deadline escalation is **non-terminal**, so a late shipment recovers the happy path instead of forfeiting the sale. It reuses 006's cancellable-scheduled-message mechanic in its second role — `TrackingInfoProvided` retiring an escalation that has *already fired*, not merely cancelling a pending one — and rejoins 006's auto-confirm tail (slice 5.4) to show that the recovered path and the clean path reach the identical `MarkCompleted()` terminal.

### Audit posture

Forward-spec for the escalation and recovery transitions (W005 slices 5.5, 5.6), which ship in M6-S4; the auto-confirm tail it rejoins (slice 5.4) shipped in M6-S3 and is auditable against `src/CritterBids.Obligations/`. The audit surface is W005 (the saga's shape, Decision 2, the escalation/recovery slices) and the M6-S3 source for the `ProvideTracking` → `ConfirmDelivery` → `ObligationFulfilled` tail. Cross-narrative consistency with narrative 006 (the saga's shape, GreyOwl12 as seller, the demo-mode framing) is the principal consistency surface; the protagonist and mechanics carry through unchanged, the listing and winner are new to avoid contradicting 006's fulfilled keyboard. Any drift discovered at M6-S4 implementation time routes per ADR 016's lanes and amends this narrative via a Document History row (ADR 020).

### Voice and grain

Mixed observer/active protagonist, the same grain as narrative 006. GreyOwl12 observes in Moments 1 and 3 (the escalation fires, the auto-confirm closes) and acts once in Moment 2 (he ships late). The narrator's responsibility-split — GreyOwl12's dashboard window versus the saga-internal and Operations-facing dramatisation underneath — carries from 006. Moment 1 is the narrative's distinctive beat: an event triggered by the *absence* of an action, dramatised as a system response to silence.

### Decisions exercised (not made here)

W005 Decision 2 (one reminder, then non-terminal escalation) is the spine of the whole narrative — Moment 1 fires the escalation, Moment 2 proves it non-terminal. Decision 1 (auto-confirm, no buyer command) drives Moment 3. Decision 4 (`ObligationsOptions` real + demo durations) drives the Setting. Decision 5 (three dispute resolutions, `Extension` non-terminal) is named in Moment 3's "why" as the one terminal this obligation never reaches and deferred whole to narrative 008. One decision *surfaced* during authoring and is recorded outside the narrative: promoting `DeadlineEscalated` to a published integration event so the escalation can reach Operations in real time (ADR 005 additive change, M6-S4 prompt) — flagged in Moment 1 and the Deferred section, not resolved here.

### Follow-ups generated

- The M6-S4 implementation prompt cites this narrative plus narrative 008 and W005 slices 5.5/5.6; this narrative is jointly authoritative with W005 for the escalation and recovery journey.
- W005's "Narrative Cross-References" can now back-reference narrative 007 on slices 5.5 and 5.6.
- Narrative 008 (the dispute sub-workflow, operator perspective) is the remaining successor; it picks up the escalation path this narrative chose *not* to follow into a dispute.

### Narrative status

**Draft (v0.1, 2026-05-29).** Three Moments covering W005 slices 5.5, 5.6, and the rejoined 5.4 auto-confirm tail; cumulative deferred section; retrospective. First CritterBids failure-and-recovery narrative; first dramatisation of the non-terminal escalation and state-tolerant recovery from the seller's vantage. Mixed observer/active protagonist Voice. Status promotes to `accepted` at session-close commit once the M6-S4 prompt that cites it is reviewed.

## Document History

- **v0.1** (2026-05-29): Authored as M6-S4 design preparation, alongside narrative 008 and the M6-S4 implementation prompt. Covers the escalation (slice 5.5) and late-tracking recovery (slice 5.6) journeys narrative 006 deferred as `alternate-path-failure`. Records the `DeadlineEscalated`-as-integration-event decision (ADR 005 additive) as a surfaced-not-resolved architectural note. Status `draft` pending M6-S4 prompt review.
