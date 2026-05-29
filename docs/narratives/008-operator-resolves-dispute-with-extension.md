---
slug: 008-operator-resolves-dispute-with-extension
status: accepted
journey: operator
perspective: single-operator
scope: dispute-extension
bounded_contexts: [Obligations]
boundaries_touched: [Operations, Relay, Settlement, Auctions, Selling, Participants, Listings]
slices_implemented: [5.7, 5.8, 5.6, 5.4]
canonical_id: ObligationId
---

# Operator Resolves a Dispute with an Extension (The One Non-Terminal Resolution)

An Obligations-grain narrative, and the second of the two successors narrative 006 deferred — the dispute sub-workflow. Where narrative 007 told the seller recovering a missed deadline on his own, this narrative tells what happens when recovery does *not* happen on its own and a **dispute** is raised: the winner reports a problem, the obligation enters the `Disputed` state, and an Operations associate resolves it. It dramatises W005 Decision 5's defining property — *three dispute resolutions, of which `Extension` is the **one** that does not call `MarkCompleted()`* — by following the `Extension` path specifically: the operator grants the seller more time, the saga **reschedules a fresh ship-by deadline and continues** rather than terminating, and the obligation later fulfils on the recovered happy path. Single operator protagonist, single disputed listing, one dispute, resolved once with `Extension`. The two *terminal* resolutions (`Refund`, `Closed`) are named as the alternates the operator could have chosen but are not dramatised as branches inside this story.

This narrative is **forward-spec**: the dispute sub-workflow (W005 slices 5.7, 5.8) ships in M6-S4 and was not yet code when this was authored; the recovery and auto-confirm tail it rejoins (slices 5.6, 5.4) ship in M6-S4 and M6-S3 respectively. It is also CritterBids' **first operator-perspective narrative** — a deliberate deviation from the single-bidder/single-seller default, sanctioned by the narratives README ("a new narrative is warranted when the protagonist is different … operator"). The protagonist is an Operations associate; the winner and seller are named but offstage of the operator's direct action. The Operations read model the operator works from (`OperationsObligationsView`) is **M7 Operations-BC work**: in M6-S4 the Obligations BC publishes the integration events that will feed it, but the staff dashboard that surfaces the dispute queue ships with the Operations BC. The narrative names the dispute landing in the operator's queue as the system is designed to run; the read model's ownership and build are recorded outside this narrative (M6-S4 prompt; M7 milestone).

## Cast

- **Morgan** — the Operations associate on shift, protagonist. CritterBids' first operator protagonist. Morgan works the post-sale floor from the staff dashboard: the lot board, the settlement queue, the escalation queue, and — this narrative's focus — the open-dispute queue. Single protagonist; the narrative is told from Morgan's vantage. Morgan observes in Moment 1 (a dispute arrives in the queue) and Moment 3 (the extended obligation fulfils), and acts once in Moment 2 (resolving the dispute with an `Extension`).
- **The Post-Sale Coordination saga** — onstage across all three Moments. The Wolverine `Saga` (ADR-022) driving the obligation. This narrative dramatises its **dispute sub-workflow** and, in Moment 2, the one transition that makes the saga's lifecycle interesting: a resolution that does *not* terminate. `Extension` reschedules a fresh `ShipByDeadline` through the same start-time scheduling path narrative 006 Moment 1 used, and returns the saga to awaiting-tracking — alive, with new timers.
- **The `OperationsObligationsView`** — onstage in every Moment. The Operations-staff read model surfacing the escalation queue and the open-dispute queue. The surface through which Morgan perceives the dispute, acts on it, and watches it clear. Forward-spec and **M7 Operations-BC-owned**; named here as the operator's working surface, built from the Obligations integration events.
- **PaleOtter22** — the winner who raises the dispute. Named, onstage at the moment of the raise (observed from Morgan's vantage as the dispute card's origin), offstage thereafter. The buyer-side party (`WinnerId`); she reports that the boxed vintage synthesizer she won has not arrived (`Reason: NonDelivery`). She receives the `DisputeResolved` broadcast via Relay in Moment 2.
- **TimberFox07** — the seller. Named but offstage throughout — the operator's investigation in Moment 2 reaches him out of frame. The seller-side party (`SellerId`) responsible for shipping the synthesizer; the `Extension` grants *him* the fresh deadline, and his late shipment in Moment 3 is what fulfils the obligation.
- **Relay** — the bounded context whose hubs broadcast `DisputeOpened` and `DisputeResolved` to the participants and the staff hub. Onstage in Moments 1–3. Forward-spec; Relay BC ships M6-S5–S7.

## Setting

A live conference demo at the Operations dashboard. The post-sale floor is busy: several obligations are mid-flight, a couple sit in the escalation queue, and the settlement queue is draining cleanly. Morgan is the associate on shift, working the staff dashboard the operator persona uses to keep the post-sale loop healthy. The system's MVP infrastructure is healthy: Wolverine is processing messages, Marten's event store on PostgreSQL (ADR 011) is up, the cross-BC RabbitMQ queues are draining, and the scheduled-message store holds the live obligations' timers.

`ObligationsOptions` is bound with `DemoMode = true`: the demo offsets collapse the chain so the whole sub-workflow — dispute, extension, fresh deadline, recovered shipment, auto-confirm — runs on the projector in seconds. The production-duration counterparts are the config alternates on the same keys; the saga's transitions are identical under either set. The exact demo second-counts are an `implementation-detail` of `ObligationsOptions`.

The obligation this narrative follows belongs to a boxed vintage synthesizer that TimberFox07 sold to PaleOtter22 in an earlier Flash session. Settlement cleared, the saga started, and — established before Moment 1 — the obligation has already gone quiet past its first deadline: TimberFox07 did not provide tracking, the obligation escalated (narrative 007's Moment 1 shape), and it now sits in the escalation queue. PaleOtter22, having waited and seen nothing arrive, is about to escalate from her side by opening a dispute. This is where narrative 007's road *not* taken — an escalation that is not recovered by the seller alone — leads.

## Moment 1: A dispute lands in the operator's queue

**Implements:** W005 slice 5.7.

**Context.** The synthesizer's obligation is in the `Escalated` state — the seller missed the deadline and has not recovered it. The escalation alert is already in Morgan's escalation queue. Morgan is working other items; this obligation has been waiting on either a late shipment (which would clear it, narrative 007's recovery) or a participant action. PaleOtter22, on the buyer side, has decided the silence has gone on long enough.

**Interaction.** PaleOtter22 opens a dispute from her order view — "I never received this item." The frontend is forward-spec (M8); the M6 seam is an in-process HTTP command endpoint that issues `OpenDispute { ObligationId, DisputeId, RaisedBy: PaleOtter22, Reason: "NonDelivery" }` to the saga. The saga receives it against `state = Escalated`. From Morgan's vantage, the interaction is not something Morgan does — it is something that *arrives*: a new card in the open-dispute queue.

**Response.** The saga emits `DisputeOpened { ObligationId, ListingId: synthesizer, DisputeId, RaisedBy: PaleOtter22, Reason: "NonDelivery", OpenedAt: <now> }`, appended to the `ObligationId` stream; the saga's state advances from `Escalated` to `Disputed`. As W005 Decision 5 requires, opening a dispute does **not** terminate the saga — `Disputed` is a holding state awaiting an Operations resolution, and the saga stays alive. `DisputeOpened` is an integration event: Relay's staff hub surfaces the dispute, and the `OperationsObligationsView` adds a card to Morgan's open-dispute queue showing the listing, the winner, the reason, and the obligation's history (escalated, never shipped). Morgan's dashboard ticks the open-dispute count up by one; the new card reads "NonDelivery — boxed vintage synthesizer — raised by buyer; obligation escalated, no tracking on file."

**Why this matters to the operator.** The dispute is the system handing Morgan a decision it cannot make automatically. The platform took the obligation as far as automation allows — it reminded, it escalated, it stayed alive waiting for recovery — and now a human judgment is required: is this a seller who needs more time, or a sale that should be unwound? The `Disputed` state is the saga deliberately *pausing* its automation and deferring to Operations, while keeping itself alive so that whatever Morgan decides can be enacted as a transition rather than a fresh workflow. Morgan's queue is, in effect, the saga's set of open questions.

### Things deliberately not included

- A winner-raised `ItemCondition` dispute, or an Operations-raised `MissedDeadline` dispute, rather than this `NonDelivery` raise. Same slice 5.7, different reason; the reason is a string-valued field carried through, not a branch in the journey. *(`alternate-path-failure`.)*
- More than one open dispute per obligation at a time. MVP allows one; multi-round negotiation is `post-MVP` (W005-3). *(`post-MVP`.)*
- The open-dispute queue's screen design and the buyer's "Report a problem" form. *(`UX-or-UI-detail`.)*

## Moment 2: The operator resolves with an Extension — the saga continues

**Implements:** W005 slice 5.8.

**Context.** The saga's state is `Disputed`. Morgan has the dispute card open and the obligation's history in view: escalated, no tracking, a buyer reporting non-delivery. Morgan investigates — reaching TimberFox07 out of frame — and learns the synthesizer is real, is being shipped, and was merely delayed, not abandoned. The judgment call is W005 Decision 5's three-way choice: `Refund` (unwind the sale, terminal), `Closed` (dismiss the dispute, terminal), or `Extension` (give the seller a fresh deadline, non-terminal). Morgan chooses `Extension`.

**Interaction.** Morgan resolves the dispute from the staff dashboard — "Verified with seller; granting a shipping extension." The M6 seam is an in-process HTTP command endpoint that issues `ResolveDispute { ObligationId, DisputeId, ResolutionType: "Extension" }` to the saga. This is the one Moment in the narrative where the protagonist acts, and the action is the architecturally significant one: of the three resolutions, `Extension` is the single path that does *not* end the saga.

**Response.** The saga emits `DisputeResolved { ObligationId, ListingId: synthesizer, DisputeId, ResolutionType: "Extension", ResolvedAt: <now> }`, appended to the `ObligationId` stream. Then — and this is the beat the whole narrative exists to show — instead of calling `MarkCompleted()`, the saga recomputes a fresh `ShipByDeadline` from `ObligationsOptions` and **reschedules** the reminder and deadline-escalation timers through `bus.ScheduleAsync()`, reusing the exact start-time scheduling path narrative 006 Moment 1 used at the obligation's birth. The saga's state returns from `Disputed` to awaiting-shipment, alive and timing a new window. Had Morgan chosen `Refund` or `Closed`, the saga would have appended `DisputeResolved` with that `ResolutionType` and called `MarkCompleted()` — terminal — and this Moment would have been the obligation's last. Because it is `Extension`, it is not. `DisputeResolved` is an integration event: Relay notifies PaleOtter22 ("Your dispute was resolved — the seller has been granted a shipping extension; you'll be notified when it ships") and updates the staff dispute board. The `OperationsObligationsView` moves the card out of the open-dispute queue; the obligation re-enters the active-with-deadline set. Morgan's open-dispute count ticks back down.

**Why this matters to the operator.** `Extension` is the resolution that treats a dispute as recoverable rather than fatal, and it is the operator's lever for exactly the situation Morgan found: a genuine sale delayed, not a fraud or a no-show. From Morgan's vantage, resolving with an extension is not closing a case — it is *reopening the clock*, handing the obligation back to the same automated chain that runs every healthy sale, with a fresh deadline and fresh timers. The saga's willingness to continue is what makes the operator's judgment cheap: Morgan does not have to manually shepherd the extended obligation, because the `Extension` resolution drops it back into the ordinary reminder/escalation machinery. This is W005 Decision 5's design intent realised — one non-terminal resolution, so that "give them more time" is a first-class outcome and not a workaround.

### Things deliberately not included

- The `Refund` resolution (unwind the sale; settlement reversal is `post-MVP`) and the `Closed` resolution (dismiss the dispute) — both terminal, both calling `MarkCompleted()`. Named here as the alternates Morgan did not choose. *(`alternate-path-failure` for `Closed`; `post-MVP` for `Refund`'s settlement reversal.)*
- Multi-round dispute negotiation or appeals after a resolution (W005-3). MVP resolves once. *(`post-MVP`.)*
- A second dispute raised after the extension is granted. *(`alternate-path-failure`.)*

## Moment 3: The extended obligation fulfils on the recovered path

**Implements:** W005 slices 5.6, 5.4.

**Context.** The saga's state is awaiting-shipment again, now governed by the extended `ShipByDeadline` and fresh timers from Moment 2. Morgan has moved on to other cards but can still see the obligation in the active set, no longer flagged as disputed. TimberFox07, having been granted the extension, ships the synthesizer within the new window.

**Interaction.** TimberFox07 provides tracking — `ProvideTracking { ObligationId, Carrier, TrackingNumber }` against the (now post-extension) awaiting-shipment state — and the obligation recovers exactly as narrative 007 Moment 2 described: `TrackingInfoProvided`, the pending timers cancelled, a `ConfirmDelivery` scheduled. Time then passes and the auto-confirm fires. From Morgan's vantage, both beats are observed, not acted: the dispute Morgan extended quietly works itself out.

**Response.** The saga emits `TrackingInfoProvided`, advancing to awaiting-delivery and scheduling the auto-confirm; then, at the offset, `DeliveryConfirmed` and the terminal `ObligationFulfilled { ObligationId, ListingId: synthesizer, WinnerId: PaleOtter22, SellerId: TimberFox07, FulfilledAt: <now> }`, with the saga calling `MarkCompleted()` — the obligation's true terminal, reached two transitions after the `Extension` that kept it alive. The `OperationsObligationsView` drops the synthesizer from every active queue. Relay broadcasts the shipment and the fulfilment to PaleOtter22 ("Your item shipped"; then "Delivery confirmed — order complete"). Morgan sees the obligation clear the active set entirely.

**Why this matters to the operator.** The extension paid off: the obligation Morgan chose not to refund or dismiss fulfilled itself on the recovered happy path, vindicating the non-terminal resolution. From the operator's vantage, the whole arc — dispute in, extension out, fulfilment without further intervention — is the system working as designed: human judgment applied once, at the one point automation could not decide, and the automated chain resuming on either side of it. The narrative closes on the same `MarkCompleted()` terminal as narratives 006 and 007, reached here through a dispute and an extension rather than a clean run, demonstrating that all three roads lead to the same retired obligation when the resolution is `Extension`.

### Things deliberately not included

- The validation-failure and carrier-rejection paths on the post-extension `ProvideTracking`. *(`alternate-path-failure`.)*
- A *second* missed deadline after the extension (the extended window also lapsing). MVP grants one extension; a re-escalation after an extension is `post-MVP` policy. *(`post-MVP`.)*

## Deferred from this narrative

Cumulative aggregation of the per-Moment deferred items, bucketed by disposition.

### `post-MVP`

- Multi-round dispute negotiation or appeals after a resolution (W005-3). Moment 2.
- The `Refund` resolution's settlement-reversal mechanics; `DisputeResolved` carries `ResolutionType: "Refund"` but the reversal is post-MVP. Moment 2.
- A re-escalation or second dispute after an extension lapses; MVP grants one extension and does not model the extended window also failing. Moments 2, 3.
- More than one open dispute per obligation at a time. Moment 1.

### `alternate-path-failure`

- A winner-raised `ItemCondition` dispute or an Operations-raised `MissedDeadline` dispute, rather than the dramatised `NonDelivery` raise (same slice 5.7, different `Reason` string). Moment 1.
- The `Closed` terminal resolution (dispute dismissed, `MarkCompleted()`), named as the alternate Morgan did not choose. Moment 2.
- Validation-failure / carrier-rejection on the post-extension `ProvideTracking`. Moment 3.

### `separate-narrative`

- The seller's own vantage on the escalation that precedes this dispute, and the recovery that does *not* require a dispute — narrative 007. Referenced as the road this obligation did not take alone.

### `implementation-detail`

- The `OperationsObligationsView` read model the operator works from: in M6-S4 the Obligations BC publishes `DisputeOpened` / `DisputeResolved` (and `DeadlineEscalated`, the ADR 005 additive integration event) that feed it, but the staff dashboard read model itself is **M7 Operations-BC work**. Recorded in the M6-S4 prompt and the M7 milestone, not resolved here. Cast / all Moments.
- The `Extension` reschedule reusing the start-time `bus.ScheduleAsync()` scheduling path, and the scheduled-message cancellation mechanic; skill territory (`wolverine-sagas.md`, ADR-022). Moment 2.
- The exact demo second-counts on the extended `ShipByDeadline` and the auto-confirm offset; `ObligationsOptions` territory. Setting, Moments 2 and 3.

### `UX-or-UI-detail`

- The staff dashboard's open-dispute queue, the dispute-resolution controls, and the buyer's "Report a problem" form (M8 frontend; M7 Operations dashboard). Moments 1–3.

## Retrospective

### What this narrative establishes

Narrative 008 is the second of narrative 006's deferred successors and CritterBids' **first operator-perspective narrative**. It makes W005 Decision 5's defining property legible from the operator's vantage: of the three dispute resolutions, `Extension` is the single one that does not call `MarkCompleted()`, and it works by rescheduling a fresh ship-by deadline and continuing the saga. By following the `Extension` path end to end — dispute raised, extension granted, obligation fulfilled on the recovered path — it shows the one non-terminal lifecycle branch the prior two narratives deliberately kept off-page, and closes on the same `MarkCompleted()` terminal as 006 and 007 to prove all three roads converge.

### Audit posture

Forward-spec for the dispute sub-workflow (W005 slices 5.7, 5.8), which ships in M6-S4; the recovery and auto-confirm tail it rejoins (slices 5.6, 5.4) ship in M6-S4 and M6-S3. The audit surface is W005 (the saga's shape, Decision 5, the dispute slices), the frozen `DisputeOpened` / `DisputeResolved` contracts (M6-S1), and the M6-S3 auto-confirm tail. Cross-narrative consistency with narrative 007 (the escalation that precedes the dispute, the recovery mechanic) and narrative 006 (the saga's shape, the demo-mode framing, the `MarkCompleted()` terminal) is the principal consistency surface; the operator, winner, and seller are new to avoid entangling prior obligations. Any drift discovered at M6-S4 implementation time routes per ADR 016's lanes and amends this narrative via a Document History row (ADR 020).

### Voice and grain

Mixed observer/active protagonist, operator perspective — a deliberate deviation from the single-bidder/single-seller default, documented in the intro and sanctioned by the narratives README. Morgan observes in Moments 1 and 3 (a dispute arrives; the extended obligation fulfils) and acts once in Moment 2 (resolving with `Extension`). The narrator's responsibility-split is between Morgan's staff-dashboard window and the saga-internal dramatisation underneath — the same technique 006 and 007 use, retargeted to the operator. Moment 1's "the dispute arrives rather than is sought" framing is the operator-grain equivalent of the seller-grain "system reacts to silence" beat in narrative 007.

### Decisions exercised (not made here)

W005 Decision 5 (three dispute resolutions, `Extension` non-terminal) is the spine of the whole narrative — Moment 2 dramatises the one non-`MarkCompleted()` resolution and names the two terminal alternates. Decision 2 (non-terminal escalation) is the Setting's precondition: the obligation reaches the dispute via an un-recovered escalation. Decision 1 (auto-confirm) drives Moment 3's terminal. Decision 4 (`ObligationsOptions`) drives the Setting. Two architectural facts *surfaced* and are recorded outside the narrative: the `OperationsObligationsView` read model being M7 Operations-BC-owned (built from the integration events M6-S4 publishes), and `DeadlineEscalated`'s promotion to a published integration event (ADR 005 additive) — both flagged in Cast / Deferred, neither resolved here.

### Follow-ups generated

- The M6-S4 implementation prompt cites this narrative plus narrative 007 and W005 slices 5.7/5.8; this narrative is jointly authoritative with W005 for the dispute journey.
- W005's "Narrative Cross-References" can now back-reference narrative 008 on slices 5.7 and 5.8.
- The M7 Operations milestone owns the `OperationsObligationsView` dashboard this operator works from; this narrative is the operator-vantage spec that milestone should cite.
- As CritterBids' first operator narrative, the operator-perspective deviation may become a recurring pattern for Operations/Relay journeys; if so, the narratives README's voice section is updated to record it.

### Narrative status

**Draft (v0.1, 2026-05-29).** Three Moments covering W005 slices 5.7, 5.8, and the rejoined 5.6 recovery / 5.4 auto-confirm tail; cumulative deferred section; retrospective. First CritterBids operator-perspective narrative; first dramatisation of the dispute sub-workflow and the one non-terminal `Extension` resolution. Mixed observer/active protagonist Voice, operator perspective. Status promotes to `accepted` at session-close commit once the M6-S4 prompt that cites it is reviewed.

## Document History

- **v1.0** (2026-05-29): Promoted `draft` → `accepted` at M6-S4 session close. The dispute sub-workflow (slices 5.7, 5.8) and the one non-terminal `Extension` resolution this narrative dramatises shipped and are green under `ObligationsFailurePathsTests` (Refund/Closed terminate, Extension reschedules and continues). `OperationsObligationsView` remains M7 Operations-BC work as recorded; M6-S4 publishes the `DisputeOpened` / `DisputeResolved` / `DeadlineEscalated` integration events that will feed it. Narrative is now jointly authoritative with W005 for the dispute journey.
- **v0.1** (2026-05-29): Authored as M6-S4 design preparation, alongside narrative 007 and the M6-S4 implementation prompt. Covers the dispute sub-workflow (slices 5.7, 5.8) narrative 006 deferred as `separate-narrative`, following the `Extension` non-terminal resolution to fulfilment. First operator-perspective narrative (deviation documented). Records the `OperationsObligationsView`-is-M7 and `DeadlineEscalated`-as-integration-event decisions as surfaced-not-resolved architectural notes. Status `draft` pending M6-S4 prompt review.
