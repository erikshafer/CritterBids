# 0001 — UUID Strategy for Stream IDs and Event Row IDs

**Status:** Proposed
**Date:** 2026-04-13
**Milestone:** M1-S5 — Slice 0.2: StartParticipantSession

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
- [ ] **Gate 3:** Confirm v7 support: `Guid.CreateVersion7()` (.NET 9+ built-in) is available in the
      CritterBids target framework (net10.0). **Confirmed** — .NET 10 includes `Guid.CreateVersion7()`.
- [ ] **Gate 4:** JasperFx team input on recommended strategy at the application layer for Auctions-
      scale write workloads. Re-surfaces naturally at M3 (Auctions BC planning).

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
