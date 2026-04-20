# ADR 007 — UUID Strategy for Stream IDs and Event Row IDs

**Status:** Partially Accepted — stream IDs (✅ Accepted); event row IDs (⏸ Deferred — Gate 1 unconfirmed, Gate 4 re-deferred at M4-S1 to M5-S1 Settlement BC trigger with named owner)
**Date:** 2026-04-13 (original) · 2026-04-16 (M3-S1 Gate 4 deferral) · 2026-04-20 (M4-S1 Gate 4 second re-evaluation, re-deferred)
**Milestone:** M1-S5 — Slice 0.2: StartParticipantSession (original) · M3-S1 — Auctions Foundation Decisions (Gate 4 first deferral) · M4-S1 — Auctions Completion Foundation Decisions (Gate 4 second re-evaluation, re-deferred)

---

## Context

CritterBids uses event sourcing across two storage backends (Marten on PostgreSQL, Polecat on SQL
Server). Event streams require stable, collision-free stream IDs. Individual event rows (the `pc_events`
table in Polecat, `mt_events` in Marten) also carry IDs, though they are typically assigned by the
storage engine rather than application code.

Two UUID strategies are relevant:

- **UUID v5** — deterministic (SHA-1 hash of a namespace + name). Produces the same GUID for the
  same input. Suitable for stream IDs when a natural business key exists (e.g., seller ID, listing ID).

- **UUID v7** — time-ordered, random. Encodes Unix millisecond timestamp in the high bits, random
  data in the low bits. Provides insert locality benefits for B-tree indexes; no determinism.

---

## Decision: Stream IDs

**Stream IDs use UUID v5 where a natural business key exists; UUID v7 otherwise.**

The Participants BC (`StartParticipantSession`) has no natural business key — each call starts a new
anonymous participant. UUID v5 determinism is load-bearing only when multiple handlers need to resolve
the same stream ID without a database lookup (idempotent creation). With no key to hash, UUID v7 is
the correct choice:

```csharp
// M1-S5 — StartParticipantSession handler
var participantId = Guid.CreateVersion7();
var stream = PolecatOps.StartStream<Participant>(participantId, evt);
```

The `ParticipantsNamespace` constant established in M1-S4 remains available for future Participants
handlers that DO have a natural key (e.g., if QR-code session tokens are introduced — see
`001-scenarios.md` §0.2 deferred note).

Future BCs with natural keys (e.g., `SellerId` derived from `ParticipantId`, `ListingId` from
`SellerId + title hash`) SHOULD use UUID v5 with a BC-specific namespace constant.

---

## Decision: Event Row IDs — Under Consideration

UUID v7 is **under consideration** for event row IDs and high-write projection IDs, primarily:

- **Auctions BC** — bid events at scale (highest write rate in the system)
- **Listings BC** — projection row IDs for multi-stream projections with range partitioning potential

**Rationale for considering v7 here:**
UUID v7's Unix-ms prefix gives insert locality in B-tree indexes (IDs are monotonically increasing
within each millisecond). On high-insert tables, this reduces page splits and WAL amplification
compared to random UUIDs. PostgreSQL range partitioning by time becomes natural.

**Why not decided yet:**
The acceptance gates below must be satisfied before this moves to Accepted.

---

## Acceptance Gates (for moving Proposed → Accepted)

- [ ] **Gate 1:** Verify that Marten 8 exposes event row ID generation to application code (e.g.,
      `opts.Events.UseIdentityStrategies()` or equivalent). If it does not, v7 cannot be adopted for
      Marten event rows.
- [ ] **Gate 2:** Verify that Polecat 2 exposes the same. Polecat's `pc_events` table structure and
      any identity column constraints must support custom ID assignment.

  > **Gate 2 — Closed (no longer applicable).** ADR 011 eliminates Polecat/SQL Server from CritterBids
  > entirely. All BCs use Marten/PostgreSQL. There is no Polecat 2 event table to verify. Gate 2 is moot.
- [ ] **Gate 3:** Confirm v7 support: `Guid.CreateVersion7()` (.NET 9+ built-in) is available in the
      CritterBids target framework (net10.0). **Confirmed** — .NET 10 includes `Guid.CreateVersion7()`.
- [ ] **Gate 4:** JasperFx team input on recommended strategy at the application layer for Auctions-
      scale write workloads. Re-surfaces naturally at M3 (Auctions BC planning).

  > **Gate 4 — Deferred (M3-S1, 2026-04-16).** JasperFx guidance had not been received at the
  > time of the M3-S1 foundation-decisions session. Formally deferred per the Event Row ID
  > Decision section below. Re-evaluation trigger set before the M3-S4 DCB authoring session
  > or on JasperFx response, whichever comes first.
  >
  > **Gate 4 — Re-Deferred (M4-S1, 2026-04-20).** Second scheduled re-evaluation at the
  > M4-S1 foundation-decisions session. JasperFx guidance still not in hand; the M3-S4
  > trigger lapsed without Gate 1 being confirmed, M3 shipped on the Marten engine default,
  > and no operational signal during M3 motivated an unscheduled re-open. Re-deferred with
  > a new trigger and a named owner rather than allowed to drift into indefinite deferral.
  > **New trigger:** re-evaluate at **M5-S1 — Settlement BC Foundation Decisions**. M5
  > lands the last Marten BC in CritterBids; M5-S1 is the final foundation-decisions
  > session at which an application-layer row-ID strategy could be adopted uniformly
  > across every BC without a post-hoc backfill. **Named owner for the JasperFx
  > follow-up nudge:** Erik. If the M5-S1 trigger fires without JasperFx input, the
  > accepted outcome is final engine-default acceptance recorded as one more amendment
  > to this ADR at that time (not a new ADR). See the Event Row ID Decision — Re-Deferred
  > (M4-S1) section below.

---

## Stream ID Decision — Accepted

All Marten BC stream IDs in CritterBids use UUID v7 (`Guid.CreateVersion7()`). This is the
established and implemented convention as of M2 close.

| BC | Aggregate | Strategy | Notes |
|---|---|---|---|
| Participants | `Participant` | UUID v7 | No natural business key — M1-S5 |
| Selling | `SellerListing` | UUID v7 | Generated at draft creation — M2-S5 |
| Auctions (M3) | `Session`, Listing states | UUID v7 (expected) | Confirm at M3 |

UUID v5 with a BC-specific namespace constant remains available for use cases where a natural
business key enables deterministic, idempotent stream creation (e.g., QR-code session tokens
for Participants — see `001-scenarios.md` §0.2 deferred note). No current BC has this condition.

Gate 3 is confirmed: `Guid.CreateVersion7()` is available in .NET 10 (`net10.0` target framework).

The event row ID question (Gates 1 and 4) remained open at the time of the stream-ID decision.
Gate 4 resurfaced at M3 (Auctions BC — the highest-write BC in the system and the primary
motivation for v7 insert locality in event rows) and was formally deferred in M3-S1 per the
Event Row ID Decision section below.

---

## Event Row ID Decision — Deferred (M3-S1, 2026-04-16)

**Status:** ⏸ Formally deferred; re-evaluation trigger set.

**Blocker:** JasperFx team input on recommended event row ID generation strategy for Auctions-
scale write workloads had not been received as of 2026-04-16 (session M3-S1, the scheduled Gate 4
closing session). Gate 1 (Marten 8 exposing an application-level event row ID generation seam)
also remains unconfirmed; without a documented seam, application-layer UUID v7 assignment to
event rows is not possible at all, independent of JasperFx guidance on whether it would be
beneficial.

**Default in effect until re-evaluation:** Marten's engine-assigned event row IDs. No application-
layer assignment is performed for event rows in any BC. Stream IDs remain UUID v7 per the
accepted Stream ID Decision section above — stream-ID and event-row-ID are independent
decisions and the stream-ID answer is already in force.

**Re-evaluation trigger:** Revisit before the M3-S4 prompt is drafted (DCB `PlaceBid` / `BuyNow`
authoring — the first high-write DCB use in the codebase and the first session whose write
profile would benefit from insert locality on event rows), OR on receipt of JasperFx guidance,
whichever comes first. If neither trigger fires before the M3-S4 prompt is drafted, the accepted
outcome is to ship M3 on the Marten engine default for Auctions event row IDs and record the
"trade-off accepted" rationale as an additional amendment to this ADR (not a new ADR).

**Rationale for deferral over guessing:**
- The insert-locality benefit of UUID v7 on event rows is real but not load-bearing for M3
  functional correctness. The DCB `EventTagQuery` correctness test (M3-S4) does not depend on
  event row ID strategy.
- Guessing at a UUID v7 row-ID strategy without confirming Gate 1 risks producing code that
  compiles but silently does nothing (Marten assigning its own IDs regardless of the application-
  layer attempt), producing a false-positive "done" state.
- Deferring with a named trigger (M3-S4 prompt draft) and a documented default (engine-assigned)
  keeps the work unblocked while preserving the option to adopt v7 for row IDs if either gate
  opens within M3.

| Gate | Status as of 2026-04-16 |
|---|---|
| Gate 1 — Marten 8 exposes event row ID generation seam | 🟡 Unconfirmed |
| Gate 2 — Polecat 2 exposes the same | ✅ Closed (N/A per ADR 011) |
| Gate 3 — `Guid.CreateVersion7()` in net10.0 | ✅ Confirmed (M1-S5) |
| Gate 4 — JasperFx guidance for Auctions-scale write workloads | ⏸ Deferred — awaiting input |

---

## Event Row ID Decision — Re-Deferred (M4-S1, 2026-04-20)

**Status:** ⏸ Re-deferred at the second scheduled re-evaluation; new trigger and named owner
pinned. Not a bare re-deferral — the M4-S1 prompt's open-question guidance explicitly
flagged that outcome as unacceptable.

**Second re-evaluation outcome:** JasperFx input is still not in hand at M4-S1. The M3-S4
trigger set in the first deferral lapsed — M3 shipped on the Marten engine default and no
operational signal during the M3 Auctions BC implementation (DCB `PlaceBid` / `BuyNow` at
M3-S4, Auction Closing saga at M3-S5/S5b, listings catalog at M3-S6) motivated an
unscheduled re-open. The engine default has held correctly through the first high-write
DCB use case.

**Why re-deferral over engine-default acceptance at M4-S1:**

The M4-S1 prompt's acceptance criteria listed two outcomes:
(a) closure with a decision, or (b) re-deferral with a specific downstream trigger and
named owner. Bare re-deferral without a new trigger would escalate to a milestone-level
question about indefinite drift. Immediate engine-default acceptance at M4-S1 would close
the door on the remaining high-value adoption window (M5-S1 as the last foundation
session across every Marten BC) without the JasperFx input the ADR has always treated as
the blocker. Re-deferral to M5-S1 with a named owner preserves the decision window one
more time while bounding the drift.

**New trigger:** Re-evaluate at **M5-S1 — Settlement BC Foundation Decisions**, when M5
(the last Marten BC in the system) opens. M5-S1 is the final foundation-decisions session
at which an application-layer row-ID strategy could be adopted uniformly across every BC
without a post-hoc backfill. If JasperFx guidance arrives before M5-S1, trigger fires
early (same whichever-comes-first shape as the M3-S1 deferral).

**Named owner for the JasperFx follow-up nudge:** Erik. The nudge is the outbound request
for guidance, not the internal decision authority. If no guidance is received by M5-S1
prompt draft, the accepted outcome at that point is permanent engine-default acceptance,
recorded as one further amendment to this ADR rather than a new ADR.

**Default in effect until M5-S1:** Unchanged from the M3-S1 deferral — Marten's engine-
assigned event row IDs. No application-layer assignment is performed for event rows in any
BC. Stream IDs remain UUID v7 per the accepted Stream ID Decision section.

**Rationale for re-deferral over closure:**

- Gate 1 (Marten 8 exposing an application-level event row ID generation seam) remains
  unconfirmed. Closing the ADR by accepting "engine default, permanent" at M4-S1 would
  foreclose adoption before the last foundation session where it could be applied
  uniformly — a one-way door being walked through while a reversible option remains.
- M3 shipped on the engine default without operational issue. This is evidence that the
  engine default is functionally adequate for the current write profile, not evidence
  that v7 row IDs would not add value. Insert-locality benefits surface under sustained
  high-write load (Auctions BC bid events at conference-demo scale or beyond) that the
  M3 integration test suite does not exercise.
- Carrying the deferral forward one more scheduled step with a named owner converts an
  open-ended drift into a bounded one. The M5-S1 forcing function matches the original
  "foundation session = decision window" discipline the ADR has followed since M3-S1.

| Gate | Status as of 2026-04-20 |
|---|---|
| Gate 1 — Marten 8 exposes event row ID generation seam | 🟡 Unconfirmed (unchanged since M3-S1) |
| Gate 2 — Polecat 2 exposes the same | ✅ Closed (N/A per ADR 011) |
| Gate 3 — `Guid.CreateVersion7()` in net10.0 | ✅ Confirmed (M1-S5) |
| Gate 4 — JasperFx guidance for Auctions-scale write workloads | ⏸ Re-deferred — trigger: M5-S1 (Settlement BC foundation decisions); owner: Erik |

---

## What Was Used in M1-S5

| Location | Strategy | Rationale |
|---|---|---|
| `StartParticipantSession` stream ID | UUID v7 (`Guid.CreateVersion7()`) | No natural business key |
| Display name derivation | Derived from UUID v7 bytes 8–11 (random portion) | Uniqueness by construction |
| BidderId derivation | Derived from UUID v7 bytes 12–13 (random portion) | Format: "Bidder N", 1–9999 |
| CreditCeiling derivation | Derived from UUID v7 byte 14 | Range 200–1000, 100-unit steps |

---

## QR-Code Session Token Note

The `001-scenarios.md` §0.2 deferred question ("What happens when a participant scans the QR code
twice?") would revisit this decision. If QR codes encode a stable token (e.g., a phone fingerprint
or email hash), UUID v5 derived from that token would make `StartParticipantSession` idempotent. The
`ParticipantsNamespace` constant is pre-established for exactly this case.

If QR codes are purely session-scoped and don't encode stable identity, UUID v7 remains correct.

This is a BC-workshop-level decision, not an M1 decision.
