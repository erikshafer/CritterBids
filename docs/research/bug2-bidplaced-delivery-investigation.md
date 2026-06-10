# Bug #2 — BidPlaced Cross-BC Delivery: the Full Investigation Trail (RESOLVED)

**Status:** Resolved — preserved as the historical investigation record
**Owner:** Erik Shafer
**Last Updated:** 2026-06-09
**Split from:** [`dcb-marten-blog-series-research.md`](./dcb-marten-blog-series-research.md) §5–§7,
per that doc's planned lifecycle (executed at the M8 Bug #2 follow-ups session, after resolution).

> **Read this first:** the bug is FIXED. The authoritative root-cause report is
> [`jasperfx-escalation-bidplaced-cross-bc-delivery.md`](./jasperfx-escalation-bidplaced-cross-bc-delivery.md)
> (consume-side Wolverine dispatch defect; app-side dispatcher-bridge fix merged in PR #90), and the
> upstream-fix work order is
> [`wolverine-upstream-saga-sticky-separation-handoff.md`](./wolverine-upstream-saga-sticky-separation-handoff.md).
> This document preserves the experiment trail — eight falsified hypotheses and the misreadings
> that §5.5 corrects — because the *shape* of the investigation is itself a lesson (codified in
> `docs/skills/message-flow-diagnosis/SKILL.md`). Section numbers (§5–§7) are kept as split from
> the source doc so existing cross-references stay resolvable.

---

## 5. Bug #2 — hypotheses informed by the series

**Symptom recap:** accepted `bid_placed` persists; **zero** `BidPlaced` envelopes forwarded;
Listings read model + `BiddingHub` never update. Routed/cascaded handler appends (e.g.
`SessionStartedHandler`'s `BiddingOpened`) forward fine; the **synchronous HTTP-origin** `PlaceBid`
write does not. Five workarounds (direct `Execute`, `(IResult, OutgoingMessages)`, `IMartenOutbox.PublishAsync`,
`bus.InvokeAsync<BidOutcome>`, explicit `SaveChangesAsync`) all produced 0 envelopes.

`UseFastEventForwarding = true` is configured on `IntegrateWithWolverine` in `Program.cs`. It forwards
Marten-appended events to the Wolverine outbox **on `SaveChangesAsync()` of an outbox-enrolled
session**. The series points at three candidate root causes, in priority order:

> **⚠️ UPDATE 2026-06-09 — H1 FALSIFIED by a live baseline+fix experiment (branch
> `fix/m8-bid-http-event-forwarding-h1`).** See §5.0 for the result and the refined diagnosis. The
> three hypotheses below are kept verbatim as the *pre-experiment* reasoning; H1 was the leading one
> and the live run disproved it. Read §5.0 first.
>
> **✅ RESOLVED 2026-06-09 (follow-up session) — ROOT CAUSE FOUND. Read §5.5.** The bug is NOT in
> DCB, NOT in the publish path, and NOT HTTP-vs-queued: it is a Wolverine consume-side dispatch
> defect under `MultipleHandlerBehavior.Separated` when a message type has exactly one saga type
> plus other handlers. §5.0–§5.4 below are preserved as the experiment trail; their inferences about
> "local forwarding works / external doesn't" were a misreading of the same symptom (§5.5 explains
> what was actually happening).

### 5.0 Experiment result — local forward works, external RabbitMQ routing does not (H1 falsified)

Ran the findings-note repro against the live Aspire host (real Postgres + RabbitMQ) on the **current**
code (baseline) and again after implementing the **canonical `[BoundaryModel]` endpoint** with
`boundary.AppendOne` on Wolverine's generated outbox-enrolled session (H1). One accepted HTTP bid
(`amount=30`), then inspected `mt_events`, `wolverine.*` envelope tables, the `AuctionClosingSaga`
doc, and `CatalogListingView`:

| Signal | Baseline (current) | After H1 (`[BoundaryModel]`) |
|---|---|---|
| `mt_events` `bid_placed` persisted | ✅ v6 | ✅ v6 |
| `AuctionClosingSaga` advanced (**local** in-process forward) | — *(not measured at baseline)* | ✅ `BidCount=1, CurrentHighBid=30` |
| `BidPlaced` outgoing / incoming / dead envelopes (**external** RabbitMQ) | **0 / 0 / 0** | **0 / 0 / 0** |
| `CatalogListingView.CurrentHighBid` (Listings read model) | `<null>` (stale) | `<null>` (stale) |
| Contrast: `BiddingOpened` incoming envelopes (routed-handler append) | 10 | 10 |

**Conclusion.** H1 changed nothing. The endpoint shape, the append API (`session.Events.Append` vs
`boundary.AppendOne`), and the outbox-enrolled generated session are **all irrelevant** to the bug.
The decisive new datapoint: under H1 the **local** `AuctionClosingSaga` handler *did* receive the
forwarded `BidPlaced` (the saga advanced) — yet its **external** `PublishMessage<BidPlaced>().ToRabbitQueue(...)`
routes (Listings / Relay / Operations) produced zero envelopes.

**Refined root cause.** `UseFastEventForwarding` forwards a Marten-appended event to **local**
in-process Wolverine handlers regardless of execution context, but only applies the **external**
broker routing rules when the append happens inside a **routed message handler** — not inside an
**HTTP endpoint request**. The differentiator is the execution context (HTTP request vs message-handler
envelope/outbox), not anything about DCB, the endpoint shape, or the append call. This matches the
findings-note observation that even `bus.InvokeAsync<BidOutcome>` (request/reply, inline execution)
failed to forward externally — request/reply does not flush the durable outbox to external transports
the way queued/routed delivery does.

**Status.** This now points squarely at **H3** (framework-level interaction gap), not H1/H2.

#### Experiment 2 — inject `IMessageBus` to enroll the HTTP outbox middleware (also FALSIFIED)

The Wolverine docs (`guide/http/integration.md`) state: *"When WolverineFx.Http endpoints depend on
Marten's `IDocumentSession` and `IMessageBus`, Wolverine automatically wraps the method execution
within Marten's transactional middleware [outbox]."* Our endpoint (both pre-H1 and H1) injected only
`IDocumentSession`, so the hypothesis was: no `IMessageBus` → bare transactional save, no outbox
`MessageContext` → forwarded events go local-only. Added `IMessageBus bus` to the endpoint signature,
rebuilt, re-ran the live procedure.

**Result: identical.** `bid_placed` persists, saga advances (local forward), `CatalogListingView`
stale, `BidPlaced` envelopes still **0 / 0 / 0**. Outbox enrollment via `IMessageBus` did not make
the forwarded event route externally.

**Sharpened root cause.** Cross-referencing the findings-note result that `bus.InvokeAsync<BidOutcome>`
*also* produced 0 envelopes, the dividing line is **queued/routed delivery vs inline execution** — not
HTTP vs handler. `UseFastEventForwarding` applies external routing only when the appending code runs
inside Wolverine's durable message pipeline (a queued/routed handler invocation). Inline execution —
an HTTP endpoint *or* `InvokeAsync` request/reply — gets the local immediate forward but never the
external routes. This matches the docs' framing: forwarding publishes events appended "within a
Wolverine message handler." The endpoint shape, append API, outbox-enrolled session, and `IMessageBus`
enrollment are all orthogonal to it.

**Key asymmetry for the fix:** the docs' Todo example shows an *explicit* `bus.PublishAsync(...)` from
an HTTP endpoint **does** reach external brokers via the outbox after commit. So the gap is specific to
*automatic forwarding*, not HTTP outbox sending. Three live options remain:

- **(a) Explicit publish (in-sync-contract):** keep the synchronous endpoint, but *explicitly* publish
  the accepted integration events through the (now outbox-enrolled) endpoint instead of relying on
  forwarding. Caveat: must NOT add this to the shared `DecideAndWrite` (the bus/auto-bid path already
  forwards externally under queued delivery — explicit publish there would double-send to
  Listings/Relay/Operations). HTTP-path-only, and the local saga's `BidCount`-monotonicity idempotency
  absorbs any local double-delivery.
- **(b) Async-202 (routed delivery):** endpoint routes a `PlaceBid` command to a local queue and
  returns 202; the queued handler appends → forwards externally (the proven path). Changes the
  M8-S3a/S3b sync contract.
- **(c) Escalate to JasperFx:** confirm whether forwarding is *expected* to skip external routing for
  inline (HTTP / `InvokeAsync`) appends, with this minimal repro.

The H1 branch's `[BoundaryModel]` refactor + `IMessageBus` enrollment are idiomatic and regression-clean
(27/27 Auctions PlaceBid tests green) but do **not** fix Bug #2 on their own.

#### Experiment 3 — explicit `bus.PublishAsync` of the accepted events from the endpoint (also FALSIFIED)

The chosen workaround direction: keep the synchronous 200 contract, but stop relying on automatic
forwarding — instead **explicitly publish** the accepted integration events through the outbox from the
(now `IMessageBus`-enrolled) endpoint, since the Wolverine.HTTP docs show an explicit
`bus.PublishAsync(...)` from an HTTP endpoint *is* sent externally after commit. Implementation:
captured each appended event's `.Data` via the `DecideAndWrite` sink and `await bus.PublishAsync(@event)`
for each on the accepted path only (HTTP-path-only; the bus/auto-bid path already forwards under queued
delivery). Saga double-delivery is idempotent (verified: all three saga handlers guard). Rebuilt,
re-ran live.

**Result: identical — `BidPlaced` incoming envelopes still 0, `CatalogListingView` still stale.** The
saga advanced (local), no dead-letters. Explicit publish from the HTTP endpoint did **not** reach the
external consumers.

**Refinement — the routes EXIST; the gap is the outbox flush/send, not routing resolution.**
Immediately after the bid, `wolverine.wolverine_outgoing_envelopes` is **empty** (total 0). I then
dumped the static routing table with `dotnet run --project src/CritterBids.Api -- describe` (run
standalone against the live Aspire containers, both connection strings supplied). It shows `BidPlaced`
**does** have all three external routes registered:

```
CritterBids.Contracts.Auctions.BidPlaced → rabbitmq://queue/listings-auctions-events
CritterBids.Contracts.Auctions.BidPlaced → rabbitmq://queue/operations-auctions-events
CritterBids.Contracts.Auctions.BidPlaced → rabbitmq://queue/relay-auctions-events
```

(Only the three RabbitMQ destinations — no `local://` route; the saga's local delivery comes via
`UseFastEventForwarding`, not the routing table.) So the earlier "routes never resolved" guess is
**refuted** — the routes are present and correct. The precise diagnosis is therefore:

> **A `bus.PublishAsync(BidPlaced)` from the HTTP-endpoint path resolves three valid external routes
> yet produces zero outgoing envelopes and zero sends — the published messages are never flushed to
> the transactional outbox.** Combined with the bid committing and forwarding locally, the only
> explanation is that the HTTP endpoint's `IMessageBus`/outbox is **not flushed** on request
> completion: the DB transaction (driven by the `[BoundaryModel]` / `AutoApplyTransactions` path)
> commits, but the outbox holding the published messages is never flushed, so they are silently
> dropped (not persisted, not sent). This is consistent with findings-note approaches #2
> (`OutgoingMessages`) and #3 (`IMartenOutbox`) failing on the *pre-`[BoundaryModel]`* manual endpoint
> too — i.e. it is an **HTTP-endpoint outbox-flush gap in this app**, not specific to `[BoundaryModel]`.

**The crux question for JasperFx:** is Wolverine's transactional-outbox middleware actually being
applied to Wolverine.HTTP endpoints in this app (it should be, per the docs, when an endpoint depends
on `IDocumentSession` + `IMessageBus`), and if so why is the outbox not flushed on HTTP request
completion — such that both explicit `PublishAsync` and `UseFastEventForwarding` from an HTTP endpoint
never reach the (correctly-routed) external transports, while queued/routed handler delivery does?
Possible local angles to check before/with escalation: whether `opts.Policies.AutoApplyTransactions()`
(a *handler* policy) covers HTTP endpoints or whether Wolverine.HTTP needs a separate transactional
opt-in (e.g. on `AddWolverineHttp`/`MapWolverineEndpoints`); and whether the `[BoundaryModel]` DCB
middleware's `SaveChanges` is the flush point.

**This is the convergent finding.** Explicit outbox publish from this HTTP endpoint has now failed in
**three** independent forms: `bus.PublishAsync` (this experiment), returning `(IResult, OutgoingMessages)`
(findings-note approach #2), and `IMartenOutbox.PublishAsync` (findings-note approach #3) — plus
automatic forwarding (current code / H1) and `bus.InvokeAsync` (findings-note #4). Every path that is
**not** queued/routed delivery fails to send `BidPlaced` externally, while the Wolverine.HTTP docs'
Todo example (`IDocumentSession` + `IMessageBus` + `bus.PublishAsync`) is documented to work. The
likely root cause is that **this app's HTTP endpoints are not actually getting Wolverine's
transactional-outbox sending applied** — the DB transaction commits (the bid persists) and local
forwarding fires, but the outbox's *external* send never happens from the HTTP-request pipeline.
Candidate contributing factors to investigate / put to JasperFx:
  - whether `opts.Policies.AutoApplyTransactions()` (handler policy) actually applies the
    transactional-outbox middleware to **Wolverine.HTTP endpoints**, or whether HTTP needs separate
    opt-in;
  - whether the `[BoundaryModel]` DCB middleware (Wolverine.Marten) manages its own
    `SaveChanges`/session lifecycle in a way that bypasses the outbox flush — though approaches #2/#3
    failed on the pre-`[BoundaryModel]` manual endpoint too, so this is not the whole story;
  - whether, in this modular-monolith config, HTTP-endpoint outbox sends to RabbitMQ are routed at all
    (note: `bus.PublishAsync(object)` routes by **runtime** type in Wolverine, so the `List<object>`
    capture is not the cause).

**Ruled OUT as the cause:** `bus.PublishAsync(object)` runtime-type routing (Wolverine routes by
`message.GetType()`); message-content (the same events forward fine under queued delivery); DCB
correctness (the bid persists and the DCB assertion holds).

#### Experiment 4 — add the `WolverineFx.Http.Marten` package (necessary, NOT sufficient)

Discovered `CritterBids.Api` referenced `WolverineFx.Http` + `WolverineFx.Marten` but **not**
`WolverineFx.Http.Marten` — the package that provides the Marten↔Wolverine.HTTP integration
(including the transactional-outbox middleware for HTTP endpoints; per the
`wolverine-http-marten-integration` skill it is *the* HTTP+Marten package and transitively pulls the
other two). Added the reference (6.5.1, already pinned in `Directory.Packages.props`), reverted the
endpoint to rely on automatic forwarding (simple `boundary.AppendOne` sink, no explicit publish), kept
`IMessageBus` injected as the documented middleware trigger, rebuilt, re-ran live.

**Result: still 0 `BidPlaced` incoming envelopes, read model still stale.** Verified the package
restored and loaded (`Wolverine.Http.Marten.dll` in the API `bin`, in `project.assets.json`). The
package is necessary/correct but did **not**, on its own, make HTTP-origin forwarding reach the
external routes.

**Why (skill-informed):** the `wolverine-http-marten-integration` skill flags our endpoint shape as an
anti-pattern. The transactional-outbox middleware that enrolls the Marten session in Wolverine's
outbox and flushes it is applied to endpoints using the **idiomatic side-effect return** pattern
(`return` `Events` / `IStartStream` / `(UpdatedAggregate, Events)`; `[Aggregate]`/`[WriteAggregate]`
with the middleware managing `SaveChanges`). Our endpoint injects `IDocumentSession`, **manually**
appends via `boundary.AppendOne`, and returns `IResult` for multi-status mapping (200/400/404/409) +
the `BidRejected` audit — so it commits the DB itself (via the `[BoundaryModel]`/DCB middleware) and
never engages the outbox-flush middleware. That explains why every path — automatic forwarding,
`bus.PublishAsync`, `OutgoingMessages`, `IMartenOutbox`, and now even with the package — fails to send
externally: the outbox is never flushed because the middleware that flushes it isn't on this chain.

**Implication for the fix.** The transactional-outbox middleware keys off the idiomatic side-effect
**return**, not `IDocumentSession` injection. Reaching the external routes from a synchronous endpoint
likely requires reshaping it to the idiomatic return form — which is in tension with our multi-status
+ rejection-audit + unknown-bidder needs (the very reasons we used `IResult` + manual append). Open
sub-question for JasperFx/escalation: is there a supported way to engage the outbox-flush middleware
on an `IResult` DCB endpoint (e.g. returning `(IResult, OutgoingMessages)` *with* the package — note
findings-note #2 tried the tuple WITHOUT the package), or is async-202 the intended shape for a
synchronous DCB-write-that-must-fan-out?

#### Experiment 5 — return `(IResult, OutgoingMessages)` WITH the package (also FALSIFIED)

The idiomatic side-effect-return shot, single-variable from findings-note #2 (which returned the tuple
*without* `WolverineFx.Http.Marten`): reshaped the endpoint to `Task<(IResult, OutgoingMessages)>`,
collected the accepted events into `OutgoingMessages` (alongside `boundary.AppendOne`), and returned
them so the cascading-message middleware would fan them out through the outbox. Package present.
Rebuilt, 27/27 tests green, re-ran live.

**Result: still 0 `BidPlaced` incoming, read model still stale, no dead-letters.** A cascading-message
return from the `[BoundaryModel]` endpoint did not send externally either.

**Tally — five mechanisms on the `[BoundaryModel]` endpoint, all 0 external sends:** automatic
forwarding · `IMessageBus` enrollment · explicit `bus.PublishAsync` · `WolverineFx.Http.Marten`
package · `(IResult, OutgoingMessages)` return. Plus findings-note `OutgoingMessages` (no package),
`IMartenOutbox`, and `InvokeAsync`. Every HTTP-origin mechanism fails; only queued/routed handler
delivery reaches RabbitMQ.

**Prime remaining suspect: the `[BoundaryModel]` DCB middleware.** It runs its own
`FetchForWritingByTags` + `SaveChanges` on its session, plausibly committing the DB **before/instead
of** the transactional-outbox middleware's enroll+flush — so any outgoing messages (returned, published,
or forwarded) on that chain are never flushed to the sender. The **one untested isolating combo** is a
**manual** endpoint (no `[BoundaryModel]`: hand-rolled `FetchForWritingByTags` like
`PlaceBidHandler.Execute`) **+ the package + `OutgoingMessages` return**. If that sends externally,
`[BoundaryModel]` is the culprit and the fix preserves the sync contract (manual DCB endpoint + cascading
return), at the cost of dropping `[BoundaryModel]` for this endpoint (i.e. reverting H1). If it also
fails, the gap is a deeper HTTP-outbox-send issue in this app → escalate. (Note: findings-note #2/#3
exercised the manual endpoint but WITHOUT the package, so this combo is genuinely new.)

#### Experiment 6 — manual DCB endpoint (no `[BoundaryModel]`) + package + `OutgoingMessages` (FALSIFIED → `[BoundaryModel]` disproven)

Reverted the endpoint to a hand-rolled `FetchForWritingByTags` (the `PlaceBidHandler.Execute` shape,
no `[BoundaryModel]` middleware), kept `WolverineFx.Http.Marten`, injected `IMessageBus`, returned the
accepted events as `OutgoingMessages`. Rebuilt, re-ran live.

**Result: still 0 `BidPlaced` external; read model stale; saga advanced (local).** So `[BoundaryModel]`
is **NOT** the blocker — a plain manual DCB endpoint with the package and a cascading-message return
fails identically.

### 5.1 Conclusion — a SYSTEMIC HTTP-endpoint external-send gap (escalate)

Six approaches, one outcome. Final contrast, captured live:

| Event | Origin | External (RabbitMQ incoming) envelopes |
|---|---|---|
| `BiddingOpened`, `SessionStarted`, `ListingPublished`, … | message **handler** (queued/routed) | **>0** (e.g. BiddingOpened = 10) ✅ |
| `BidPlaced` | **HTTP endpoint** (any of 6 mechanisms) | **0** ❌ |

And the local `AuctionClosingSaga` advances every time (in-process forwarding works regardless of
origin). The gap is therefore **not** about DCB, `[BoundaryModel]`, the append API, the endpoint return
shape, explicit-vs-automatic publish, or the `WolverineFx.Http.Marten` package. The single
differentiator is **execution context**: events appended/published from a **Wolverine message-handler
(queued/routed) invocation** reach external transports; events appended/published from an **HTTP-request
pipeline** never do — only their local in-process delivery fires.

**This is bigger than the bid endpoint:** it implies **no HTTP endpoint in this app can send a message
to an external broker via the outbox**. (CritterBids has not needed HTTP-origin external publishing
before — all prior cross-BC publishing originates in handlers — so this is the first time the gap
surfaces.) The Wolverine.HTTP docs say an HTTP endpoint with `IDocumentSession` + `IMessageBus`
*should* get transactional-outbox sending; in this app it does not. Likely a single systemic
configuration cause (e.g. the durable sending agent / `AddWolverineHttp` / transactional-policy wiring
not covering the HTTP pipeline) rather than anything DCB-specific.

**Six experiments, all FALSIFIED:** H1 `[BoundaryModel]` · `IMessageBus` enrollment · explicit
`bus.PublishAsync` · `WolverineFx.Http.Marten` package · `(IResult, OutgoingMessages)` return · manual
endpoint + package + `OutgoingMessages`. Plus findings-note `OutgoingMessages`/`IMartenOutbox`/`InvokeAsync`.

**Recommendation.** The finding is systemic and fundamental enough that **JasperFx escalation should
come before building async-202** — there may be a one-line config that enables HTTP-origin external
sends app-wide, which would make the async-202 contract change unnecessary. If escalation confirms
async-202 (queued/routed delivery) is the intended shape for "synchronous DCB write that must fan out,"
implement that. Kept on the branch as the documented baseline: the H1 `[BoundaryModel]` endpoint (clean,
idiomatic) + the `WolverineFx.Http.Marten` package (correct dependency) + the `DecideAndWrite` refactor
+ the local-forward regression test. The workaround experiments (explicit publish, `OutgoingMessages`,
manual revert) are reverted — none worked.

**Escalation packet (ready):** this §5.0/§5.1 trail + the `describe` routing rows (routes exist) + the
six-experiment table + the handler-vs-HTTP contrast.

### 5.2 ⚠️ MAJOR REFRAMING — the gap is `BidPlaced`-specific external forwarding, NOT HTTP-origin

Experiment 7 (async-202): routed `PlaceBid` to an explicit **durable local queue**
(`opts.PublishMessage<PlaceBid>().ToLocalQueue("auctions-place-bid")`) and published it from a dev
probe endpoint (`/api/dev/async-bid-probe`). Verified live:

- `wolverine_incoming_envelopes` has **`CritterBids.Auctions.PlaceBid` = 1** → the command genuinely
  traversed the durable local queue and `PlaceBidHandler` ran in a **separate queued worker context**
  (NOT inline in the HTTP request).
- `bid_placed` persisted; the `AuctionClosingSaga` advanced (`BidCount=1`) → `BidPlaced` was forwarded
  **locally**.
- **`BidPlaced` external incoming envelopes = 0; `CatalogListingView` still stale.**

So `BidPlaced` fails to forward externally **even from a genuine queued-worker context** — which
**falsifies the "HTTP-origin vs queued delivery" hypothesis** that the previous six experiments
appeared to support. Every prior experiment was HTTP-origin, so `BidPlaced` had never been exercised
from a pure queued path until now. The real contrast is now **between two events**, both appended the
same way (`FetchForWritingByTags<BidConsistencyState>` + `BuildEvent` + `AddTag(ListingStreamId)` +
`Append`) from a queued handler, both with identical 3-queue RabbitMQ routing (per `describe`):

| Event | Append site (queued handler) | Forwarded LOCALLY | Forwarded EXTERNALLY |
|---|---|---|---|
| `BiddingOpened` | `OpenListingForBidding` ← `SessionStartedHandler` | ✅ (saga start) | ✅ (incoming = 10) |
| `BidPlaced` | `PlaceBidHandler` ← async-202 local queue | ✅ (saga advance) | ❌ (incoming = 0) |

**The async-202 fix does NOT work** for the same underlying reason — the problem was never the request
pipeline. **New crux question for JasperFx:** why does `UseFastEventForwarding` forward `BidPlaced`
only to its **local** handler (the saga) and not to its **external** `PublishMessage().ToRabbitQueue()`
routes, while `BiddingOpened` — appended identically (DCB-tagged) from an equivalent queued handler,
with identical routing — forwards to **both**? Candidate angles to investigate next: whether a message
type having a **local handler** suppresses its external forwarding routes under
`MultipleHandlerBehavior.Separated`; whether the forwarding route-resolution differs for `BidPlaced`
(e.g. the saga's `[SagaIdentityFrom]` handler vs `BiddingOpened`'s saga-START handler); or a
per-event-type forwarding subscription difference. **Note:** routing the command via a durable local
queue (the async-202 plumbing) is sound and works; it is retained behind the dev probe. The actual fix
must address `BidPlaced`'s external forwarding specifically (or have the queued `PlaceBidHandler`
explicitly publish the integration events as cascading messages rather than relying on forwarding —
tested in §5.3 below: also FALSIFIED). The only path empirically
proven to send `BidPlaced` externally is **queued/routed delivery** (every handler-pipeline event in
the seed flow — `ParticipantSessionStarted`, `ListingPublished`, `SessionStarted`, `BiddingOpened` —
produces external envelopes). Next: (1) escalate to JasperFx with this repro + the three-experiment
trail, and/or (2) implement the **async-202** pivot (route `PlaceBid` to a local queue → queued
handler appends → forwards externally), accepting the contract change. The H1 + `IMessageBus` +
explicit-publish code is retained on the branch as the documented attempt; the explicit-publish block
is a no-op until the outbox-send question is resolved.

---

#### Pre-experiment hypotheses (kept for the record)

### H1 (leading) — we never use the generated DCB endpoint, so we never use the outbox-aware path

Babu's canonical endpoint (§4.0) runs **entirely inside Wolverine's generated chain**, whose session
comes from `OutboxedSessionFactory` and whose `SaveChangesAsync` is the one `UseFastEventForwarding`
hooks. Our `PlaceBidEndpoint` injects a **plain `IDocumentSession`** and calls
`PlaceBidHandler.Execute` *off to the side*. Even if `AutoApplyTransactions` flushes that session, the
events may never enter the forwarding path because the append didn't go **through `boundary.AppendOne`**
and/or the session isn't the outbox-bound one the forwarding listener is attached to. This also
explains why approaches #2–#5 failed: they bolt forwarding onto a write that already committed outside
the forwarding-aware session.
**Test:** rebuild `PlaceBid` as the canonical `[BoundaryModel] IEventBoundary<BidConsistencyState>`
Wolverine.HTTP endpoint (with `Load` returning our `EventTagQuery`, build+tag + `boundary.AppendOne`,
and a `Configure` retry policy). If envelopes appear, H1 is confirmed and this *is* the fix.

### H2 — `session.Events.Append(streamId, …)` vs `boundary.AppendOne(…)`

We append the tagged event **directly to the `command.ListingId` stream** rather than through the
`IEventBoundary` returned by `FetchForWritingByTags`. It is plausible that the forwarding hook (and/or
the boundary's bookkeeping) is wired to `boundary.AppendOne`/`AppendMany`, and a raw
`session.Events.Append` to a specific stream bypasses it. Note also Part 2's remark that boundary
events "route to a single stream identified by the first tag that has `.ForAggregate<>()`" — combined
with the "runtime pins **string** stream identity for boundary aggregates" note (§4.5), there may be a
**stream-identity mismatch**: we append to a `Guid` stream while the boundary machinery expects a
string-keyed stream. Worth confirming where our tagged events actually land vs where the boundary
reads.
**Test:** switch the append to `boundary.AppendOne(wrapped)` (keeping build+tag) within the current
hand-rolled `Execute`, leave everything else, and re-check forwarding. Isolates the append API from
the endpoint shape.

### H3 — genuine framework gap (escalation candidate)

Approach #4 (`bus.InvokeAsync<BidOutcome>(command)`) is a *real* message-handler context and **still**
didn't forward. If H1/H2 both fail, that's a strong signal that `UseFastEventForwarding` does not
intercept DCB/boundary appends made under request/reply (`InvokeAsync`) delivery the way it does under
routed delivery — i.e. a real Marten/Wolverine interaction gap. Per the memory note, Erik is JasperFx
core team; this would be the framed-and-minimal-repro question to take to the team (cf. the ADR-010
dual-store consult). The coupon-sample repo is the natural reproduction baseline — it forwards-or-not
under the canonical pattern, which itself is a useful data point.

**Recommended order:** H1 first (most likely the fix *and* aligns us with the documented idiom),
then H2 as the isolating experiment, then H3 escalation only if both fail.

---

### 5.3 Experiment 8 — explicit `OutgoingMessages` from the QUEUED handler (FALSIFIED) + decisive control

Changed `PlaceBidHandler.HandleAsync` to return `OutgoingMessages` carrying the accepted events
(explicit cascading publish — the project's "integration events via `OutgoingMessages`" convention),
kept the async-202 plumbing (probe → durable local queue → handler). Re-ran live.

**Result: still 0 `BidPlaced` external; `CatalogListingView` still stale; no dead-letters.** Explicit
cascading publish of `BidPlaced` from a genuine queued-worker handler also fails to reach the external
consumers.

**The decisive control (strongest escalation evidence).** `Listings.AuctionStatusHandler` handles
**both** `BiddingOpened` and `BidPlaced` — *same handler class, same Listings BC, same
`listings-auctions-events` RabbitMQ queue, same routing config* (`describe`-confirmed). Every live run:
`BiddingOpened` is delivered to it (`CatalogListingView.Status` → `"Open"`, which the seed waits on),
`BidPlaced` is **never** delivered to it (`CurrentHighBid` stays `null`). And `BidPlaced` cannot be
delivered there via any mechanism tried (forwarding, `bus.PublishAsync`, `(IResult, OutgoingMessages)`,
`OutgoingMessages` from the queued handler; from HTTP or the durable-queue worker). This **eliminates**
every "different consumer / route / handler / origin" explanation — two event types, one handler, one
queue: one deliverable, the other not.

**Consumers confirmed to exist** (rules out "no handler"): `Listings.AuctionStatusHandler.Handle(BidPlaced)`
sets `CurrentHighBid = message.Amount`; `Relay.BidPlacedHandler.Handle(BidPlaced)` → `BiddingHub`;
`Relay.AuctionsOperationsHandlers.Handle(BidPlaced)` → `OperationsHub`. The read model is stale because
`BidPlaced` never arrives, not because nothing would process it.

### 5.4 FINAL STATUS — escalate (8 experiments exhausted)

`BidPlaced` is not delivered to its cross-BC consumers by **any** of eight attempted mechanisms, while
`BiddingOpened` — identical DCB-tagged append, identical routing, **the same `AuctionStatusHandler`** —
is delivered normally. `BidPlaced` IS forwarded **locally** to the `AuctionClosingSaga` every time, so
it is being produced and forwarded; only its cross-BC / external delivery fails. This affects **all**
bid paths (HTTP and proxy-saga auto-bid), not just M8 — `BidPlaced` cross-BC propagation appears never
to have worked end-to-end (only ever exercised in isolated tests with external transports disabled
before M8-S3b).

**Escalation question (final):** In a Wolverine modular monolith (`MultipleHandlerBehavior.Separated`,
`IntegrateWithWolverine(UseFastEventForwarding = true)`, `PublishMessage<T>().ToRabbitQueue(...)` +
`ListenToRabbitQueue(...)` in-process), why is one Marten event type (`BidPlaced`) delivered **only to
its local in-process handler** and never to its external RabbitMQ routes — via forwarding *or* explicit
`OutgoingMessages` — while a sibling type (`BiddingOpened`) appended and routed identically is delivered
to **both**? Both have a local handler (`AuctionClosingSaga`: `BidPlaced` continue-handler via
`[SagaIdentityFrom]`; `BiddingOpened` start-handler) and the same external routes. Prime suspect: an
interaction between a type having a local (sticky, `Separated`) handler and its external publish routes
during route resolution.

**Eight experiments, all FALSIFIED:** H1 `[BoundaryModel]` · `IMessageBus` enrollment · explicit
`bus.PublishAsync` · `WolverineFx.Http.Marten` package · `(IResult, OutgoingMessages)` return · manual
endpoint + package + `OutgoingMessages` · async-202 (durable local queue) · `OutgoingMessages` from the
queued handler. **Working plumbing proven:** routing `PlaceBid` to a durable local queue (async-202)
works; pairing it with whatever makes `BidPlaced` route externally would complete the fix once JasperFx
identifies the cause.

> **Superseded by §5.5** — the eight experiments all failed for one reason none of them touched:
> the publishes all SUCCEEDED; the deliveries were eaten on the consume side.

### 5.5 ✅ ROOT CAUSE (2026-06-09 follow-up session) — `SagaChain` keeps a single saga as the DEFAULT handler under `Separated`, which suppresses the sticky-handler fan-out

The follow-up session took the two investigative steps the original eight experiments never did:
**(1) read the Wolverine 6.5.1 source** for `UseFastEventForwarding` + route resolution +
handler-graph dispatch (the repo is on disk at `C:\Code\JasperFx\wolverine`; the 6.5.1 tag was
fetched from upstream and read via `git show FETCH_HEAD:...` without touching the working tree),
and **(2) instrument the live runtime** instead of inferring from envelope tables: a dev
`PreviewSubscriptions` probe (`src/CritterBids.Api/Dev/RoutingProbeEndpoint.cs`) plus a full live
run with `Logging__LogLevel__Wolverine=Debug`.

**What the source says (all symmetric — routing was never the problem):**
- Fast event forwarding is `PublishIncomingEventsBeforeCommit`, a session listener that does
  `_bus.PublishAsync(e)` for each pending `IEvent` wrapper before `SaveChanges`
  (`Wolverine.Marten/Publishing/OutboxedSessionFactory.cs` + `PublishIncomingEventsBeforeCommit.cs`).
- The wrapper routes via `MartenEventRouter` (first, non-additive custom route source):
  `RoutingFor(Event<T>)` → `RoutingFor(IEvent<T>)` → `EventUnwrappingMessageRoute` over
  `RoutingFor(T)` = the raw type's `ExplicitRouting` subscriptions (our three
  `PublishMessage<BidPlaced>().ToRabbitQueue(...)` rules).
- The live probe confirmed it: `Event<BidPlaced>` and `Event<BiddingOpened>` both resolve to their
  three RabbitMQ unwrapping routes, message transformed to the raw contract type.

**What the debug log shows (one accepted HTTP bid):** `BidPlaced` envelopes **enqueued for sending
to all three rabbit queues, sent, received back in-process, and "Successfully processed"** — on
every queue. The publish side has been working through *every* prior experiment. The difference
from `BiddingOpened`: after processing, `BiddingOpened` shows fan-out relays
(`Enqueued for sending BiddingOpened to local://critterbids.listings.auctionstatushandler/`, ...);
`BidPlaced` shows **no local relays at all**.

**The defect (Wolverine 6.5.1):**
- `BidPlaced`'s chain is a `SagaChain` (the `AuctionClosingSaga.Handle([SagaIdentityFrom] BidPlaced)`
  continue-handler). `SagaChain.maybeAssignStickyHandlers` makes all NON-saga handlers sticky on
  their own local queues under `Separated`, **but only separates the saga calls when
  `groupedSagas.Length > 1`** — a single saga type stays in the chain's default `Handlers`.
- `HandlerGraph.HandlerFor(messageType, endpoint)` only builds the `FanoutMessageHandler` (the
  relay from an external endpoint to every sticky local queue) **when
  `chain.HasDefaultNonStickyHandlers()` is false**. The lone saga keeps it true → each rabbit
  delivery executes the default chain (the saga) only → `ProxyBidDispatchHandler`, Listings,
  Relay ×2, and Operations ×2 starve silently. "Successfully processed," zero dead letters.
- `BiddingOpened` works because its saga involvement is a saga-START via a separate static class
  (`StartAuctionClosingSagaHandler`) — not a `Saga`-typed method — so its chain is a plain
  `HandlerChain`, all handlers sticky, no defaults, fan-out fires. (The fan-out × 3 queues is also
  the full explanation of Bug #3's saga-start `DocumentAlreadyExistsException` dead letters.)

**Reinterpreting the §5.0–§5.4 evidence:** the "saga advanced = local forward works" inference was
wrong. The saga advance WAS the external delivery — consumed inline at the rabbit listener (no
durable-inbox rows; the `wolverine_incoming_envelopes` rows seen for other types are the durable
**local sticky-queue copies** created by fan-out, which `BidPlaced` never reaches). Single-handler
chains (`BuyItNowOptionRemoved`, `BidRejected`) skip sticky grouping entirely (`grouping.Count() > 1`
gate), stay default, and execute directly at the rabbit endpoint — which is why they were never
symptomatic.

**Predictions (same mechanism), verified live:** `ReserveMet` and `ExtendedBiddingTriggered`
(saga continue-handlers + Relay route) — sent, received, "Successfully processed," **no relay** →
Relay starved, saga state perfect (`BidCount=2, ReserveHasBeenMet=true, Status=Extended`).
**Predicted, unverified:** `BuyItNowPurchased` and `ListingWithdrawn` starve their
Listings/Settlement/Operations/ProxyBidDispatch consumers identically — two latent integration
bugs nothing has surfaced yet.

**Fix directions** (detail in the rewritten escalation doc
[`jasperfx-escalation-bidplaced-cross-bc-delivery.md`](./jasperfx-escalation-bidplaced-cross-bc-delivery.md)):
1. **Upstream (preferred):** in `SagaChain.maybeAssignStickyHandlers`, separate the single-saga
   case under `Separated` exactly like the multi-saga case (or, more generally, make
   `HandlerFor(Type, Endpoint)` fan out to sticky locals even when defaults exist). Erik has the
   JasperFx channel; the escalation doc is now a root-caused bug report with a minimal generic repro.
2. **App-level interim — IMPLEMENTED AND LIVE-VERIFIED (same day, branch
   `fix/m8-auction-closing-dispatch-bridge`):** dispatcher-bridge the saga's five contract-event
   continue-handlers through Auctions-internal commands, mirroring `ProxyBidDispatchHandler`
   (M4-S3 Path C) — `AuctionClosingDispatchHandler` → `Closing*Observed` commands. Makes every
   contract-event chain saga-free → all-sticky → fan-out works. Live result: an HTTP bid now
   updates `CatalogListingView.CurrentHighBid` (the first time ever), Operations'
   `BidActivityHandler` and Relay's handlers receive `BidPlaced`, and the saga advances via the
   bridged commands (idempotency absorbs the once-per-queue copies). Full suite green (293
   tests, one Invoke→Send test edit). **Bug #2 is FIXED at the application level.**
3. **NOT a fix:** anything on the publish side (forwarding vs Marten event subscriptions vs
   explicit publish) — the consume-side dispatch eats the delivery regardless.

**Design verdict (the "did we design the workflow wrong?" question):** No. The DCB write shape,
the tagged-append, `UseFastEventForwarding`, and the per-BC queue topology are all sound and all
worked. The single design wrinkle that *exposed* the framework defect is `AuctionClosingSaga`
subscribing to shared integration events directly via `[SagaIdentityFrom]` continue-handlers while
six other handlers consume the same types — the one shape `Separated` mishandles. Notably, the
M4-S3 `ProxyBidManagerSaga` dispatcher-bridge (adopted for composite-key correlation) accidentally
immunized that saga against this bug; `AuctionClosingSaga`'s direct subscription stepped in it.

**Secondary upstream finding:** `wolverine-diagnostics describe-routing <type> --explain` NREs at
`MessageRoute.Describe()` (MessageRoute.cs:204) in this app (description-mode route with
null-tolerant members; the throwing dereference is the route's `Serializer`, per independent review
mode) — small separate bug; the live `PreviewSubscriptions` probe was the workaround.

---

## 6. Candidate approach changes for CritterBids (for discussion — not yet decided)

1. **Adopt the canonical `[BoundaryModel]` Wolverine.HTTP endpoint for `PlaceBid`** (and, by
   symmetry, BuyNow). Keeps `Guid ListingId` contract events (build+tag explicitly,
   `boundary.AppendOne`), lets Wolverine generate the fetch/save chain on the outbox-enrolled session.
   Primary motivation: fixes Bug #2 if H1 holds; secondary: deletes the hand-rolled `Execute`/`HandleAsync`
   split and aligns us with the idiom the framework authors document. **Tension to resolve:** M8-S3a's
   synchronous accept/reject HTTP contract — the canonical endpoint *is* synchronous and returns
   `IResult`, so this looks compatible; verify the response body still carries our `PlaceBidResponse`
   shape (use `[ProducesResponseType]` for OpenAPI since `IResult` hides the schema).
2. **Add the DCB retry policy** (`opts.Policies.OnException<ConcurrencyException>().RetryWithCooldown(...)`
   globally, or `Configure(HandlerChain)` per endpoint). Closes the M8-S3a 5xx-on-concurrent-bid gap
   and gives the losing bid a clean retry → map the final loss to a graceful 409. Low risk, do
   regardless of the Bug #2 outcome.
3. **Lock the tag name `"listing"` as schema** (§4.6) — document it, treat any change as a migration.
4. **Keep `AddEventType<T>` discipline** — every new boundary-query event type must be registered in
   `AuctionsModule` or the DCB fetch silently under-matches (§4.5).
5. **Confirm the DCB side-table schema is applied** wherever `AutoCreate.None` would be used (§4.4).
   Locally `ApplyAllDatabaseChangesOnStartup()` covers it.
6. **Production note (not now):** consider `DcbStorageMode.HStore` only if bid throughput on a single
   hot listing ever becomes a measured problem; default `TagTables` is correct for the demo.

---

## 7. Open questions to take forward

> **2026-06-09 resolution status:** Q1–Q3 are ANSWERED by §5.5 — forwarding intercepts every
> outboxed session's appends regardless of endpoint shape, append API, or delivery mode, and routes
> them correctly; there is no string-vs-Guid mismatch; nothing is excluded by design. The failure
> was consume-side dispatch (`SagaChain` default-handler vs sticky fan-out). Q4 stands, narrowed:
> the canonical endpoint IS transparent to the sync 200 contract (verified live — accepted bids
> return 200 with the full response body).

- **Q1 (Bug #2 core):** Does `UseFastEventForwarding` forward events appended via
  `boundary.AppendOne` inside a Wolverine-generated DCB endpoint, but *not* events appended via
  `session.Events.Append(streamId, …)` from a plain endpoint calling a helper? (H1/H2.)
- **Q2:** For a `[BoundaryAggregate]` with `Guid` stream-id append targets, is there a string-vs-Guid
  stream-identity mismatch between where tagged events land and where the boundary reads? (Part 3/4
  "runtime pins string for boundary aggregates" note.)
- **Q3 (escalation):** If even `bus.InvokeAsync` doesn't forward, is request/reply delivery excluded
  from `UseFastEventForwarding` interception by design? (H3 — JasperFx question, with coupon-sample as
  baseline repro.)
- **Q4:** Does adopting the canonical endpoint conflict with M8-S3b's optimistic-update-against-200
  reconcile model, or is it transparent (still a synchronous 200/4xx)?

---

> **Post-resolution notes on §6/§7 (added at the split):** §6 candidate #1 ([BoundaryModel] endpoint)
> shipped in PR #89/#90. §6 candidate #2 (the "DCB retry policy") resolved differently than written:
> the message-bus path already had `AuctionsConcurrencyRetryPolicies`, and Wolverine.HTTP chains do
> not consume failure rules at 6.5.1 — the HTTP path instead maps commit-time
> `ConcurrencyException` to a 409 via `ConcurrencyConflictMiddleware` (this session). That same
> finding means the blog series' Part 3/4 per-endpoint `Configure(HandlerChain)` retry guidance
> does not appear to apply to HTTP chains at this version — flagged for follow-up with Babu.
> §6 candidates #3–#5 hold. §7's Q1–Q3 are answered (see §5.5); Q4 verified transparent.
