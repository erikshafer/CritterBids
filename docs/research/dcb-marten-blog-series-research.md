# DCB in Marten (MysticMind 4-part series) — Research Notes

**Status:** Durable DCB reference — distillation live-verified against the series 2026-06-09; Bug #2 trail split out
**Owner:** Erik Shafer
**Last Updated:** 2026-06-09
**Suggested repo location:** `docs/research/dcb-marten-blog-series-research.md`

> **Document lifecycle: split executed (2026-06-09).** §5–§7 (the Bug #2 trail) moved to
> [`bug2-bidplaced-delivery-investigation.md`](./bug2-bidplaced-delivery-investigation.md); this doc
> is now the durable DCB reference (§1–§4, §8). The research-folder index lives at
> [`README.md`](./README.md).

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

> **Distillation verified 2026-06-09 (follow-up session):** all four posts were re-fetched and
> re-read end-to-end. §1–§4 and §8 of this doc are faithful to the source — no misquotes, no missed
> traps found. One scope confirmation that matters for Bug #2: **the series ends at the DCB write;
> it never covers what happens to appended events downstream in Wolverine's messaging layer**
> (`MultipleHandlerBehavior`, sagas, sticky handlers, broker fan-out). Bug #2's root cause (§5.5)
> lives entirely in that uncovered layer — the series was the wrong lens for it, which is itself a
> useful negative result.

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

## 5–7. Bug #2 investigation trail — MOVED

The live Bug #2 debugging trail that occupied §5 (hypotheses + eight falsified experiments + the
§5.5 root-cause resolution), §6 (candidate approach changes), and §7 (open questions) moved to
[`bug2-bidplaced-delivery-investigation.md`](./bug2-bidplaced-delivery-investigation.md) once the
bug was resolved, per this doc's original lifecycle plan. The section numbers are preserved there,
so any `§5.x`/`§6`/`§7` reference from the sections above resolves in that document. The durable
outcomes: root cause + fix in
[`jasperfx-escalation-bidplaced-cross-bc-delivery.md`](./jasperfx-escalation-bidplaced-cross-bc-delivery.md);
methodology codified in `docs/skills/message-flow-diagnosis/SKILL.md`.

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
