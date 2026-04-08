# ADR 005 — Integration Event Contract Versioning Policy

**Status:** Accepted  
**Date:** 2026-04

---

## Context

`CritterBids.Contracts` defines the integration event types shared between BC modules. As the system evolves, event schemas will need to change. Without a versioning policy, schema changes risk breaking consumers or corrupting stored event streams.

---

## Decision

CritterBids follows a three-tier versioning policy in order of preference:

### Tier 1 — Additive-Only Changes (Default)

New fields on existing event types must be **nullable or have sensible defaults**. Old events stored in Marten or Polecat deserialize cleanly without migration. Consumers that don't need the new field ignore it.

This handles the majority of real-world schema evolution. It is the default approach and requires no special machinery.

```csharp
// Safe — nullable addition, old events deserialize correctly
public sealed record BidPlaced(
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    bool IsProxyBid,
    // New field — nullable, safe addition
    string? BidSource = null
);
```

### Tier 2 — Upcasting (Breaking Changes)

When a breaking change is unavoidable — renaming a field, changing a type, splitting one event into two — use Marten's or Polecat's **upcasting** support.

An upcaster is a function registered at startup that transforms the raw stored JSON of an old event version into the shape of the new version at read time. Old events remain in the store unchanged.

```csharp
// Register in BC's AddXyzModule() Marten configuration
opts.Events.Upcast<BidPlacedV1, BidPlaced>(v1 => new BidPlaced(...));
```

### Tier 3 — Versioned Type Names (Last Resort)

For changes too complex for upcasting, introduce a new versioned type: `BidPlaced_V2`. The original type is kept for backward compatibility. Consumers migrate to the new type. The old type is retired after all consumers have migrated.

This is expensive and should be rare.

---

## Enforcement

- All new fields on existing `Contracts` types must be nullable or have defaults — PR review enforces this
- Upcaster registration is documented in the BC's module skill file when introduced
- No breaking changes to `Contracts` types without a corresponding upcaster or versioned type

---

## References

- `src/CritterBids.Contracts/` — shared event type definitions
- Marten upcasting documentation: https://martendb.io/events/versioning.html
