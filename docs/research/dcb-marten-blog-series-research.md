# DCB in Marten (MysticMind 4-part series) — Research Notes

**Status:** Draft — Research Phase
**Owner:** Erik Shafer
**Last Updated:** 2026-06-09
**Suggested repo location:** `docs/research/dcb-marten-blog-series-research.md`

> **Document lifecycle (planned split).** For now this doc holds **both** the distilled series
> learnings (§1–§4, §8) **and** the live Bug #2 debugging trail (§5–§7) — the hypotheses only make
> sense in light of the distillation. **Once Bug #2 is resolved**, separate the two: §5–§7 move out
> (either to a sibling research note or folded into `docs/notes/integrated-host-flash-bidding-findings.md`,
> which already owns Bug #2), leaving this doc as the durable DCB reference. At that same point,
> create a `docs/research/README.md` index (the folder has none today) covering all research docs.

---

## 0. Why this doc exists

Babu Annamalai (MysticMind — JasperFx core maintainer, after Jeremy) published a four-part
series walking DCB in Marten from first principles through production. We read it to **fine-tune
our understanding of DCB** and to test whether the patterns it documents explain (and potentially
fix) the open defect from the M8-S3b integrated-host manual run:

> **Bug #2** — HTTP-placed bids don't forward to consumers. An accepted `bid_placed` persists, but
> zero `BidPlaced` envelopes reach the Wolverine outbox, so the Listings read model and the Relay
> `BiddingHub` never update. See `docs/notes/integrated-host-flash-bidding-findings.md`.

The series is the single best external source we have on how Marten *intends* DCB to be used, and
it lands directly on the seam where our bug lives (the Wolverine.HTTP + DCB write path).

**Source:**
- Part 1 — The aggregate trap: <https://mysticmind.dev/dcb-in-marten-part-1-the-aggregate-trap/>
- Part 2 — Implementation with plain Marten: <https://mysticmind.dev/dcb-in-marten-part-2-marten-implementation/>
- Part 3 — Less ceremony with Wolverine: <https://mysticmind.dev/dcb-in-marten-part-3-wolverine-handlers/>
- Part 4 — Production considerations: <https://mysticmind.dev/dcb-in-marten-part-4-production-considerations/>

Companion code: `dcb-coupon-sample` repo (coupon-redemption sample, plain-Marten and Wolverine variants).

**Our pinned versions (relevant throughout):** Marten **9.6.0** (transitive via `WolverineFx.Marten`),
Wolverine **6.5.1**, `WolverineFx.RuntimeCompilation` **6.5.1** referenced. We are *past* the 9.4.0
hard-cap line (see §4.4).

---

## 1. The headline finding (read this first)

Our current `PlaceBidHandler` **deliberately avoids** the canonical Wolverine.HTTP DCB shape. The
docstring's stated reason:

> "We do NOT use the canonical `[BoundaryModel]` auto-append shape — that shape requires Marten to
> infer tags from an event property whose type exactly matches the registered tag type. Our contract
> events carry `Guid ListingId`, not `ListingStreamId ListingTag`, and we refuse to leak the tag
> wrapper into `CritterBids.Contracts.Auctions.*`."

**Part 3 addresses this exact objection and resolves it.** Babu's coupon event also carries plain
primitives (`string Code, Guid CustomerId`), *not* the `CouponCode`/`CustomerId` tag wrappers — so
his `boundary.AppendOne(rawEvent)` tag inference fails too. His sanctioned workaround:

```csharp
var redeemed = session.Events.BuildEvent(new CouponRedeemed(code, body.CustomerId, body.OrderTotal));
redeemed.WithTag(new CouponCode(code), new CustomerId(body.CustomerId));
boundary.AppendOne(redeemed);   // <-- explicit build+tag, THEN append THROUGH the boundary
```

> "You could alternatively define the event with wrapper-typed properties; the explicit form is the
> choice that keeps the existing event schema unchanged."

**So our stated reason for hand-rolling the write does not actually force us off the canonical
pattern.** We can keep `Guid ListingId` on the contract events *and* use the generated
`[BoundaryModel]` endpoint — we just build+tag explicitly and call `boundary.AppendOne(builtEvent)`.

The one real divergence that remains is **how we append**:

| | Babu (canonical) | CritterBids (current) |
|---|---|---|
| Append API | `boundary.AppendOne(builtEvent)` (through the `IEventBoundary`) | `session.Events.Append(command.ListingId, wrapped)` (directly to a stream) |
| Who drives `FetchForWritingByTags` + `SaveChanges` | Wolverine-generated endpoint wrapper | hand-written `Execute`, called from a plain endpoint |
| Session | Wolverine `OutboxedSessionFactory` session, inside the generated outbox-aware chain | plain injected `IDocumentSession`, written off to the side |

This divergence is the **leading hypothesis for Bug #2** (§5).

---

## 2. Part 1 — The aggregate trap (the "why")

**Core thesis:** a single event stream's optimistic-concurrency version can defend exactly one
consistency boundary. When a business rule spans *more than one* entity, no single stream version
can protect it.

**The canonical example — coupon `SUMMER25`:**
- Rule A: at most 1000 total redemptions (a property of the *coupon*, spread across thousands of customer streams).
- Rule B: at most 2 redemptions per customer (a property of the *customer* stream).

Pick `Customer` as the aggregate → Rule B is easy, Rule A is undefendable (no single version).
Pick `Coupon` as the aggregate → Rule A is easy, Rule B becomes racy. *One rule spans both entities,
and a single stream can only protect one of them.*

**Usual workarounds and their costs:**
- **Saga + compensation** → eventual consistency *in the bad sense* (redeem, get a confirmation
  email, then a "we're reversing it" email seconds later).
- **One mega-aggregate** (`Promotion` holding every customer's count) → both invariants on one
  version, but every redemption now serializes on that one stream → contention.
- **Two streams in one DB transaction** → gives up per-stream isolation, reintroduces row locking
  through the back door.
- **Distributed lock (Redis)** → new failure modes (lock expiry, clock skew), another system to babysit.

**The reframing (Sara Pellegrini, "Killing the Aggregate"):**
> The consistency boundary belongs to the **decision**, not to the entity. With aggregates the
> boundary is fixed at design time by which stream you write to. With **DCB the boundary is declared
> per command by the events you query.**

**Mechanics in one paragraph:** the command declares a *query of events to defend* (typically by tag);
the store reads matching events, projects them into a decision model, and records the current global
sequence number as the read point; the command decides in memory; on commit, the store checks — *in
the same transaction* — whether any new event matching the query arrived since the read point. If yes,
the append fails.

**When DCB fits:** a real business invariant, spanning ≥2 entities, where eventual consistency would
be a *bug*. (Coupon caps, hotel/room caps, seat allocation, course-enrolment capacity.) If "which
aggregate owns this rule?" answers "both, sort of" → DCB.

> **CritterBids relevance:** our `PlaceBid` DCB is a *legitimate* DCB case. The bid-acceptance
> decision reads across the listing's lifecycle events (`BiddingOpened`, prior `BidPlaced`,
> `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`) that, post-ADR-009, share the
> listing stream with Selling's events. The tag (`ListingStreamId(listingId)`) is **per-instance**,
> which §4.2 confirms is the healthy shape. We are not misusing DCB — the question is purely *how we
> wire the write*.

---

## 3. Part 2 — Plain-Marten implementation (the mechanics, written by hand)

### 3.1 API surface

| API | Purpose |
|---|---|
| `opts.Events.RegisterTagType<T>(name)` | Declare a strong-typed tag wrapper; `name` is persisted with every tagged event (schema) |
| `.ForAggregate<TBoundary>()` | Link the tag to the boundary aggregate that projects events carrying it |
| `[BoundaryAggregate]` | Mark a class as an identity-less aggregate built from a tag query |
| `EventTagQuery` | Compose `tag X or tag Y or ...` |
| `session.Events.FetchForWritingByTags<T>(query)` | Load matching events, project into `T`, remember the read point, **and queue the consistency assertion** |
| `DcbConcurrencyException` | Raised at commit if the consistency check fails |

### 3.2 Registration

```csharp
opts.Events.RegisterTagType<CouponCode>("coupon").ForAggregate<CouponRedemptionGuard>();
opts.Events.RegisterTagType<CustomerId>("customer").ForAggregate<CouponRedemptionGuard>();

opts.Events.DcbStorageMode = DcbStorageMode.HStore;   // default is TagTables (see §4.1)
```

### 3.3 The boundary aggregate

`[BoundaryAggregate]`, **no `Id`, not tied to a single stream**, `Apply(...)` methods project the
tag-query events into in-memory state, plus a decision predicate (`CanRedeem`).

### 3.4 The hand-rolled command (the cycle made visible)

```csharp
for (var attempt = 0; attempt < MaxRetries; attempt++)
{
    await using var session = _store.LightweightSession();

    var query = new EventTagQuery().Or<CouponCode>(couponTag).Or<CustomerId>(customerTag);
    var boundary = await session.Events.FetchForWritingByTags<CouponRedemptionGuard>(query, ct);

    var guard = boundary.Aggregate;
    // ...decide in memory...

    var redeemed = session.Events.BuildEvent(new CouponRedeemed(code, customerId, orderTotal));
    redeemed.WithTag(couponTag, customerTag);
    boundary.AppendOne(redeemed);

    try { await session.SaveChangesAsync(ct); return Accepted; }
    catch (ConcurrencyException) { await Task.Delay(50, ct); /* re-read, re-decide, re-write */ }
}
```

Notes from the post worth keeping:
- The `catch` catches **both** `DcbConcurrencyException` (the DCB query check) **and**
  `EventStreamUnexpectedMaxEventIdException` (Marten's normal per-stream optimistic check). Both
  extend `JasperFx.ConcurrencyException`. *"boundary aggregate events route to a single stream
  identified by the first tag that has `.ForAggregate<>()`, so concurrent appends for the same
  aggregate hit version conflicts before the DCB check even runs."*
- `SaveChangesAsync` runs the insert **and** the consistency check in one Postgres transaction.
- `QueryByTagsAsync` is the read-only sibling with **no** consistency check — swapping it in lets the
  cap leak. The contrast is "what DCB is actually buying you."

> **CritterBids relevance:** our `PlaceBidHandler.Execute` *is* a faithful hand-rolled DCB cycle — it
> calls `FetchForWritingByTags<BidConsistencyState>`, decides, builds+tags events, and relies on the
> queued `AssertDcbConsistency` at `SaveChanges`. The mechanics are right. **But** (a) we append via
> `session.Events.Append(streamId, ...)` rather than `boundary.AppendOne(...)`, and (b) we have **no
> retry loop** — there is no `for` loop and no Wolverine retry policy (§4.3, §5).

---

## 4. Part 3 + Part 4 — the production-shaped learnings

### 4.0 The canonical Wolverine.HTTP DCB endpoint (Part 3)

This is the shape we currently avoid. The endpoint **is** the handler; Wolverine generates the
fetch + save wrapper:

```csharp
public static class RedeemCouponEndpoint
{
    // Wolverine runs Load FIRST; params match the endpoint's by name. Builds the boundary query.
    public static EventTagQuery Load(string code, RedeemCouponBody body)
        => new EventTagQuery()
            .Or<CouponCode>(new CouponCode(code))
            .Or<CustomerId>(new CustomerId(body.CustomerId));

    [WolverinePost("/coupons/{code}/redeem")]
    public static IResult Post(
        string code,
        RedeemCouponBody body,
        [BoundaryModel] IEventBoundary<CouponRedemptionGuard> boundary,   // <-- projected guard + writer
        IDocumentSession session)
    {
        var guard = boundary.Aggregate;
        if (guard is null || guard.MaxTotalUses == 0) return Results.NotFound(/*...*/);
        var decision = guard.CanRedeem(body.CustomerId);
        if (!decision.Allowed) return Results.Conflict(/*...*/);

        var redeemed = session.Events.BuildEvent(new CouponRedeemed(code, body.CustomerId, body.OrderTotal));
        redeemed.WithTag(new CouponCode(code), new CustomerId(body.CustomerId));
        boundary.AppendOne(redeemed);                                     // <-- append THROUGH the boundary
        return Results.Ok(new RedeemResponse("accepted"));
    }

    public static void Configure(HandlerChain chain)
        => chain.OnException<ConcurrencyException>()
                .RetryWithCooldown(50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds());
}
```

At startup Wolverine generates: call `Load` → `FetchForWritingByTags(query)` → project →
`Post` → `SaveChangesAsync`; on `ConcurrencyException` apply `Configure`'s policy and re-run the
*whole* sequence. **The session in that generated chain is the outbox-enrolled
`OutboxedSessionFactory` session** — this is the crux for Bug #2 (§5).

Babu explicitly argues **against** factoring the existence/cap checks into a `Validate(...)` middleware
method: those reads come from the same boundary the endpoint writes to — *they are the business
decision, not input-shape validation*. Keep them inline in the endpoint. (Input-shape checks —
"is the body well-formed", "is the total positive" — do belong in `Validate`/`Before`.)

### 4.1 Tag storage: `TagTables` (default) vs `HStore`

- **TagTables** — one normalised table per tag type, FK to the event store. No extensions. Ad-hoc
  SQL-friendly. Default. Right for "getting started / moderate throughput / DBA forbids extensions /
  you want reporting joins."
- **HStore** — tags in an `HSTORE` column on the event row, GIN-indexed. Fewer rows/joins, faster on
  tag-heavy workloads. Needs the `hstore` extension (RDS/Cloud SQL/Neon/Supabase all support it).
- Switching later is possible but not free (both layouts coexist during migration). **Pick once,
  deliberately, before scale.**

> **CritterBids:** we are on the default (TagTables) — fine for the demo. Note for any future "scale"
> talk track.

### 4.2 The global-lock failure mode (the most important production warning)

> "The more events a tag matches, the closer that tag is to a global lock."

A `Promotion("BLACK_FRIDAY")` tag spanning 50,000 coupons turns every redemption into contention on
one tag → "a global lock with a more elegant API." Rules:
- **Per-instance identifiers are fine; cohort identifiers are dangerous.** `CouponCode("SUMMER25")`
  = one coupon (good). `Promotion("BLACK_FRIDAY")` = a cohort (bad).
- If a tag matches an unbounded set, the rule probably belongs elsewhere (rate limiter / monitor /
  out-of-band ceiling).
- Litmus: a healthy DCB query touches dozens-to-hundreds of events; a sick one touches millions.

> **CritterBids:** `ListingStreamId(listingId)` is **per-instance** (one listing) → healthy. A single
> hot listing serializes its own bids, which is exactly the behavior an auction wants. No action.

### 4.3 Forgetting the Wolverine retry policy (silent failure mode)

> "Wolverine does **not** auto-retry on transient exceptions. That includes `DcbConcurrencyException`.
> Without an explicit retry policy, a single concurrent redemption that triggers the consistency check
> turns into a 500 for the losing request."

Fix is either per-endpoint `Configure(HandlerChain)` or global:

```csharp
opts.Policies.OnException<ConcurrencyException>()
    .RetryWithCooldown(50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds());
```

The hand-rolled plain-Marten path *can't* have this bug (the retry is a `for` loop you wrote);
Wolverine "generates the loop for you, but you have to tell it to."

> **CritterBids:** this is **exactly** the M8-S3a deferred item — "a genuine `DcbConcurrencyException`
> on simultaneous bids surfaces as 5xx; graceful 409 left out." We currently have *neither* a
> hand-written retry loop *nor* a Wolverine retry policy. This is a concrete, low-risk fix
> independent of Bug #2.

### 4.4 Hard cap by default since Marten 9.4.0 (issue #4591) — *we are on 9.6.0*

Pre-9.4.0 the DCB check was a non-locking `SELECT EXISTS(...)` separate from the insert → a **soft
cap** (could briefly sit at `N+1`). 9.4.0 added a **side table parallel to `mt_events`** (one row per
tag boundary, carrying a version); `SaveChangesAsync` emits
`INSERT … ON CONFLICT DO UPDATE … WHERE version = $captured RETURNING 1` against it. Two same-tag
writers contend on that **row-level write lock at READ COMMITTED**: first commit bumps the version,
the second blocks, then finds its captured version stale and throws `DcbConcurrencyException`. No
`SERIALIZABLE`, no advisory lock — one extra row touched per save. The check shape is now independent
of `DcbStorageMode`.

Consequences:
- The cap is **exact by default**; the `Task.Delay`/cooldown in retry loops is now **just backoff**,
  no longer load-bearing for correctness.
- Cost: every writer for the *same tag* serializes through one constraint row. Fine for a per-instance
  tag (naturally scoped); a genuine bottleneck for a broad cohort tag — §4.2 "now with teeth."
- It shipped as a **mandatory schema change** → deployments with `AutoCreate.None` must run
  `db-patch`/`db-apply` before rolling 9.4+.

> **CritterBids:** We are on **Marten 9.6.0**, so we already get the hard-cap serializing constraint.
> Our M8-S3a DCB-concurrency test (two sessions fetch same boundary version, both append, first
> commit wins, second throws `DcbConcurrencyException`) is asserting exactly this guarantee — good.
> **Action item:** confirm our Aspire/migration story applies the DCB side-table schema (we run
> `ApplyAllDatabaseChangesOnStartup()`, so locally we're covered; flag for any `AutoCreate.None`
> production posture).

### 4.5 Other Part-3/4 traps, checked against our config

| Trap | What it causes | CritterBids status |
|---|---|---|
| Missing `AddWolverineHttp()` | Startup throw | ✅ present |
| Missing `WolverineFx.RuntimeCompilation` (W6 split Roslyn out of core) | Startup throw `TypeLoadMode.Dynamic … no IAssemblyGenerator` | ✅ referenced (6.5.1) |
| Missing `AddEventType<T>()` under `IntegrateWithWolverine` | EventGraph finalised at startup → DCB query filters on **empty** event-type set → fetch returns 0 → endpoint 404 / null aggregate | ✅ `AuctionsModule` registers all 5 boundary event types (`BiddingOpened`, `BidPlaced`, `BuyItNowOptionRemoved`, `ReserveMet`, `ExtendedBiddingTriggered`) + others. **Keep this invariant whenever a new boundary event type is added.** |
| `.UseLightweightSessions()` (historical 404 trap) | Old heavy-session identity map made `FetchForWritingByTags` return null | ✅ we call it; now default/redundant but harmless |
| `StreamIdentity.AsString` for boundary aggregates (Marten ≤9.3 dispatcher bug) | DCB dispatcher resolution | ✅ no longer required — *"the runtime now pins `string` for boundary aggregates regardless of `StreamIdentity`."* We're on 9.6.0. (See §5 caveat: our **append target stream** is a `Guid`.) |
| State-dependent checks in `Validate` | Source-gen `CS0128` collision on duplicate `[BoundaryModel]` fetch (patch in flight upstream) + wrong shape | N/A today; **avoid if we adopt the canonical endpoint** — keep the decision inline in the endpoint |

### 4.6 Tag governance + schema evolution

- The tag *name* string (`"listing"`) is persisted on every event = **schema**. Renaming it strands
  old events (they keep the old name, won't match new queries). Treat tag-name changes as DB
  migrations; dual-write across a release if a rename is unavoidable.
- A boundary aggregate must tolerate **every historical shape** of every event its tag query pulls
  (one fetch mixes event types/versions). Prefer additive event changes; use `Apply` as an upcaster;
  version events (`V2`) when forced; remember old events never retroactively gain new tags.

> **CritterBids:** our tag name is `"listing"` (`RegisterTagType<ListingStreamId>("listing")`). Lock
> it down — it's now part of the on-disk schema.

### 4.7 When DCB is the wrong tool (sanity filter)

Avoid DCB when: the invariant lives inside one aggregate; the rule is advisory (quota/soft
rate-limit/fraud threshold → monitor + circuit breaker); the constraint is uniqueness (Postgres
unique index); the decision needs data outside the event store; or "which entity owns this?" has no
answer because the model is wrong (fix the model first). Blunt test: *if you replaced DCB with a saga,
how often would the saga compensate? "Never" → DCB is overkill. "Twice a day, each a support ticket"
→ DCB earns its keep.* For CritterBids `PlaceBid`, eventual consistency on bid acceptance **would** be
a bug (accept-then-retract bids), so DCB is justified.

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

## 8. Reusable takeaways (independent of the bug)

- DCB's boundary is **per-command, declared as a tag query** — not a stream layout chosen up front.
- **Per-instance tags = healthy; cohort tags = a global lock with nicer syntax.**
- Under `IntegrateWithWolverine`, **register every event type explicitly** — lazy registration does
  not reach the finalised EventGraph, and the DCB query silently under-matches.
- Tag *names* are **on-disk schema**; treat changes as migrations.
- Marten **9.4.0+ makes the DCB cap exact by default** via a serializing side-table constraint; the
  retry cooldown is now backoff, not correctness. (We're on 9.6.0.)
- With Wolverine you **must** opt into retry for `DcbConcurrencyException`; it does not auto-retry.
- Keep state-dependent decisions **inline in the endpoint**, not in `Validate`/`Before` middleware.
