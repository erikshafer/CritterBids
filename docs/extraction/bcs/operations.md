# Operations BC

**Maturity:** Planned-only.

**Evidence for the call:** No `src/CritterBids.Operations` project exists. No staff command handler, cross-BC projection, dashboard hub, or demo-reset type corresponding to this BC is present anywhere in `src/`. The BC is declared in `docs/vision/bounded-contexts.md` lines 189–211, and is referenced as the consuming endpoint for many integration events in the topology (lines 226, 229, 231, 234, 240, 244, 247). It is also named in `CLAUDE.md` lines 139.

This dossier records only what the vision doc declares.

---

## Business purpose (per vision doc)

> "Internal staff view and the projector-facing dashboard for live demonstrations. Aggregates read models across all BCs. Provides real-time visibility into lot activity, saga states, settlement queue, obligation pipeline, and flagged sessions. Also owns staff-initiated commands and the demo reset capability."
> — `docs/vision/bounded-contexts.md` line 191.

## What the vision doc attributes to it

From `docs/vision/bounded-contexts.md` lines 193–204:

- Cross-BC aggregate projections — live lot board, bid activity feed, settlement queue, obligation status, dispute queue, participant activity.
- Staff command handlers — force-close a listing, flag a participant session, resolve a dispute, start a Flash Session, reset demo state.
- Live ops feed — SignalR hub for real-time dashboard updates.
- Staff authentication seam — config-driven passphrase in MVP, extensible to full staff identity.
- Two SPAs share the same API host: the participant app and the ops dashboard (line 204).
- `DemoResetInitiated` command/event is post-MVP (lines 202–203, also `docs/vision/domain-events.md` line 120).

## Events attributed to it

From `docs/vision/domain-events.md` lines 116–120:

| Event | Type (per vision) |
|---|---|
| `DemoResetInitiated` | 🟠 Internal — post-MVP |

No Operations type exists in `src/CritterBids.Contracts` (the Contracts namespace tree has no `Operations` folder).

## Integration topology (per vision)

- **In:** events from all BCs (`docs/vision/bounded-contexts.md` line 208). The topology routes virtually everything significant to Operations: all Selling significant events (line 226), `SessionCreated` (229), `ListingAttachedToSession` (231), `SessionStarted` (234), all significant Auctions events (240), all Settlement events (244), all Obligations events (247).
- **Out:** none — staff commands are intra-BC; Operations consumes but does not publish integration events (line 210).

A single Settlement → Operations publish route exists in `src/CritterBids.Api/Program.cs` lines 158–163: `PublishMessage<PaymentFailed>().ToRabbitQueue("operations-settlement-events")`. No `ListenToRabbitQueue` for that queue exists in `Program.cs`. The comment on lines 159–161 explicitly states the route is wired for queue-topology completeness with no consumer because Operations has not shipped.

## Storage (per vision)

PostgreSQL via Marten (per ADR 011 all-Marten pivot). `docs/vision/bounded-contexts.md` line 206 notes the pivot from the original Polecat/SQL Server design.

## Tests

None — there is no Operations project and no Operations test project.

## Open questions

None at the BC level beyond the BC's complete absence from code.
