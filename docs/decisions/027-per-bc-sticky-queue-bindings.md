# ADR 027 — Per-BC Sticky Queue Bindings under Separated Fan-Out

**Status:** ✅ Accepted (implementation scheduled — see the follow-up implementation session prompt)
**Date:** 2026-06-09 (M8 Bug #2 follow-ups session)
**Deciders:** Erik Shafer
**Informed by:** the M8 Bug #2 root-cause investigation
([`../research/jasperfx-escalation-bidplaced-cross-bc-delivery.md`](../research/jasperfx-escalation-bidplaced-cross-bc-delivery.md))
and its independent verification pass

---

## Context

CritterBids publishes cross-BC integration events to **one RabbitMQ queue per consuming BC**
(`listings-auctions-events`, `operations-auctions-events`, `relay-auctions-events`, …) and listens
to all of them in the same process. This "consumer-isolation discipline" is a deliberate teaching
surface of the reference architecture: each BC owns its queue, and a future extraction of a BC into
its own process keeps its queue unchanged.

What the M8 Bug #2 investigation made visible is how those queues actually interact with
`MultipleHandlerBehavior.Separated` at runtime. Under Separated, every handler is sticky on its own
**local** queue, and an externally-delivered message reaches them via Wolverine's
`FanoutMessageHandler` — which relays the delivery from the receiving broker endpoint to **every**
sticky local queue. With N per-BC queues all carrying the same event, the fan-out runs N times:

> **Every consumer executes once per consuming queue.** `BidPlaced` (3 queues) runs each of its
> seven handlers 3×. The per-BC queues do not partition consumption — the fan-out re-broadcasts
> each queue's copy to all consumers regardless of which BC "owns" the queue.

Observed costs of this N-copies behavior (all live-verified during the Bug #2 work):

- 3× processing of every multi-queue event by every consumer.
- The "Bug #3" class of dead-letter noise: saga-START handlers race their own duplicates
  (`BiddingOpened` → `AuctionClosingSaga`, `ListingSold` → Settlement, `SettlementCompleted` →
  PostSaleCoordination), producing `DocumentAlreadyExistsException` dead letters on every flow.
- Every non-idempotent consumer surface must implement its own duplicate absorption — the
  SignalR live feed shipped without one and rendered each bid 3× (fixed client-side in PR #90,
  but the obligation recurs for every future push/email/webhook surface).

The N-copies behavior is not what the queue topology *looks like* it does, and "the topology
implies semantics it doesn't have" is a poor property for a reference architecture.

## Decision

**Bind each BC's broker-fed handlers sticky to that BC's own queue** (Wolverine's
`[StickyHandler("<queue-name>")]` attribute, or the fluent `AddStickyHandler` equivalent on the
listener endpoint), so that a delivery on `listings-auctions-events` executes the Listings
consumers and nothing else. Each consumer then receives **exactly one copy per event**, delivered
on the queue its BC owns.

Because a sticky match at the receiving endpoint suppresses the fan-out entirely, the Auctions BC's
own consumers of Auctions events (`AuctionClosingDispatchHandler`, `ProxyBidDispatchHandler` — the
ADR-precedented dispatcher bridges) lose their delivery path. They get their own queue:
**`auctions-auctions-events`**, with the Auctions-family events published to it and the two
dispatchers bound sticky to it. Self-consumption through the broker is consistent with the
extraction story (an extracted Auctions service would consume its own events the same way).

Scope: all broker-fed contract-event consumers across Listings, Relay, Operations, Settlement,
Obligations, Selling, and Auctions. Local-only internal commands (`Closing*Observed`,
`ProxyBidObserved`, `CloseAuction`, …) are unaffected — they stay on their Separated default
local queues.

## Consequences

**Positive**

- Exactly-once-per-consumer delivery restores the semantics the per-BC topology advertises; the
  queue map becomes truthful documentation again.
- The Bug #3-class saga-start dead-letter noise disappears (one `BiddingOpened` copy → one start).
- The per-consumer duplicate-absorption obligation drops from "every surface" to the at-least-once
  baseline (redeliveries only) — client dedupe and idempotency guards remain as redelivery
  hygiene, not as steady-state traffic shaping.
- The extraction-readiness story strengthens: sticky bindings make "which handlers move with this
  queue" explicit in code.
- Demonstrates Wolverine's sticky-handler feature in the reference architecture.

**Negative / accepted costs**

- One new queue (`auctions-auctions-events`) plus publish routes for the Auctions family to it.
- `[StickyHandler]` annotations (or fluent bindings) across the consumer handlers of six BCs —
  a wide but mechanical diff, plus test-fixture updates where fixtures relied on fan-out delivery.
- Sticky handlers are not reachable via `InvokeAsync` at the default endpoint (already the
  documented Separated trade; tests use send/publish per the wolverine-sagas skill).
- The fan-out mechanism stops being exercised in CritterBids — the upstream Wolverine fix for the
  single-saga fan-out defect (see the escalation doc) remains worth landing for the ecosystem,
  but CritterBids no longer depends on fan-out behavior either way.

## Alternatives considered

- **B — Consolidate to one queue per producer family** (single `auctions-events`; fan-out
  distributes exactly once to every sticky local handler). Least configuration — it would delete
  roughly two-thirds of the Program.cs routing block — and the durable local queues already provide
  per-consumer isolation. Rejected because it erases the per-BC queue ownership topology that this
  project exists to demonstrate, and parks delivery semantics on the exact mechanism
  (`FanoutMessageHandler` gating) whose sharp edge caused Bug #2.
- **C — Status quo, documented** (N copies absorbed by idempotency). Zero implementation risk, but
  3× processing, permanent dead-letter noise, a recurring dedupe obligation on every new surface,
  and a topology that misleads readers about its own semantics.
- **Defer until the upstream Wolverine fix lands.** Rejected: the fix changes *who wins the
  dispatch* for saga chains; it does not change N-copies fan-out, so the decision is orthogonal.

## Implementation

Scheduled as its own implementation session (prompt:
`docs/prompts/implementations/M8-S3c-adr027-sticky-queue-bindings.md`), including: the sticky
bindings + new queue, test-fixture updates, removal of any now-redundant duplicate-absorption
special-casing that existed purely for steady-state copies (client dedupe stays as redelivery
hygiene), live verification of `BidPlaced`, `BuyItNowPurchased`, and `ListingWithdrawn` flows, and
a check that saga-start dead letters no longer accumulate.

**Revisit triggers:** the upstream Wolverine fix landing (re-evaluate whether any binding can be
simplified); a BC actually extracting to its own process (validates or falsifies the
extraction-readiness claim); sticky-binding friction in tests exceeding the fan-out-era friction it
replaced.
