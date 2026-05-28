# Obligations BC

**Maturity:** Planned-only.

**Evidence for the call:** No `src/CritterBids.Obligations` project exists. No type, handler, saga, event, or projection corresponding to this BC is present anywhere in `src/`. The BC is declared in `docs/vision/bounded-contexts.md` lines 139–162 and referenced in the integration topology on lines 242–247 of the same file. It is also named in `CLAUDE.md` lines 137, in the canonical BC table.

This dossier therefore records only what the vision doc declares. No behavior is inferred. The `src/` absence is the finding.

---

## Business purpose (per vision doc)

> "Coordinates the post-sale handoff between winner and seller. Watches both parties honor their commitments. Drives a scheduled reminder and escalation chain, cancels messages when obligations are met early, and manages disputes if someone does not follow through."
> — `docs/vision/bounded-contexts.md` lines 141.

## What the vision doc attributes to it

From `docs/vision/bounded-contexts.md` lines 143–161:

- Post-sale coordination saga — triggered by `SettlementCompleted`.
- Shipping reminder chain — scheduled messages with cancellation on early fulfillment.
- Escalation path — missed deadlines escalate to Operations staff review.
- Dispute sub-workflow — open, resolve, close.
- Carrier tracking seam — stubbed in MVP, real webhook receiver in production.

## Events attributed to it

From `docs/vision/domain-events.md` lines 97–106:

| Event | Type (per vision) |
|---|---|
| `PostSaleCoordinationStarted` | 🟠 Internal |
| `ShippingReminderSent` | 🟠 Internal |
| `DeadlineEscalated` | 🟠 Internal |
| `TrackingInfoProvided` | 🔵 Integration |
| `DeliveryConfirmed` | 🟠 Internal |
| `ObligationFulfilled` | 🔵 Integration |
| `DisputeOpened` | 🔵 Integration |
| `DisputeResolved` | 🔵 Integration |

None of these types exist in `src/CritterBids.Contracts`. Verified by directory listing: `src/CritterBids.Contracts` contains subfolders for `Auctions`, `Participants`, `Selling`, `Settlement`, plus a flat `SellerRegistrationCompleted.cs`. There is no `Obligations` folder or any of the eight types named above.

## Integration topology (per vision)

- **In:** `SettlementCompleted` from Settlement (`docs/vision/bounded-contexts.md` line 159, 242).
- **Out:** `TrackingInfoProvided`, `ObligationFulfilled`, `DisputeOpened`, `DisputeResolved` (line 161); the topology also routes `ObligationFulfilled` and `DisputeOpened` to Relay (line 246).

## Storage (per vision)

PostgreSQL via Marten (`docs/vision/bounded-contexts.md` line 157).

## Tests

None — there is no Obligations project and no Obligations test project.

## Open questions

None at the BC level beyond the BC's complete absence from code. The vision doc's description is internally consistent; absence is the only finding.
