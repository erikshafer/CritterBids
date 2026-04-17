# Parked Questions Ledger

Cross-workshop tracker for questions raised during Event Modeling sessions. Open questions at the top; resolved questions below with the resolving workshop noted.

**Last updated:** 2026-04-16
**Workshops covered:** W001 (Flash Session Demo-Day Journey), W002 (Auctions BC), W003 (Settlement BC), W004 (Selling BC)

IDs are stable: `W00X-N` where N is the question number within the workshop where it was originally raised.

---

## Open Questions

| ID | Question | Source | Target | Notes |
|---|---|---|---|---|
| W001-6 | Demo-mode timeout config for Obligations sagas? | W001 Ph2 | Obligations BC | PO decision captured: sagas need demo-mode timeout config with a cap. Implementation deferred. |
| W001-7 | UI state between timer-zero and outcome event? | W001 Ph2 | Frontend | Duplicated/refined as W001-12. |
| W001-9 | Where does `ParticipantBidHistoryView` live? | W001 Ph3 | Listings or Auctions BC | Tentatively Listings. Confirm during Listings BC workshop. |
| W001-10 | Ops screens: separate routes or tabbed dashboard? | W001 Ph3 | Frontend | Frontend workshop. |
| W001-11 | Auto-navigate ops to LiveBoard on session start? | W001 Ph3 | Frontend | Frontend workshop. |
| W001-12 | "Closing..." UI state between timer-zero and outcome? | W001 Ph3 | Frontend / Auctions | Frontend workshop. |
| W001-13 | How does seller provide tracking? Dedicated screen or inline? | W001 Ph3 | Frontend / Obligations | Frontend workshop. |
| W001-15 | Frontend milestone: one or split participant/ops? | W001 Ph4 | Milestone scoping | Resolve at M6 planning. |
| W002-8 | Two-proxy bidding war: worth a specific integration test? | W002 Ph3 | Auctions BC (M4) | Proxy Bid Manager saga deferred to M4 per `M3-auctions-bc.md` non-goals. Moves with the saga — confirm at M4 test time. Likely yes; scenario 4.10 covers it. |
| W003-1 | What happens if `SellerPayoutIssued` fails (infrastructure issue)? | W003 Ph1 | Settlement / Ops | Wolverine retries for transient. Permanent failures need operator tooling. Ops workshop. |
| W003-2 | Post-MVP compensation when real payment processor is wired in? | W003 Ph1 | Settlement BC | Refund-winner step before terminating. Parked until payment integration. |
| W003-3 | Second chance offer fallback if winner's payment fails? | W003 Ph1 | Settlement BC | Post-MVP. |
| W003-5 | Does Settlement need a manual retry mechanism for ops staff? | W003 Ph1 | Settlement / Ops | Post-MVP. Requires ops command interface. |
| W003-6 | Where does `FeePercentage` come from? Platform, per-seller, or per-listing? | W003 Ph1 | Settlement + Selling | Partially resolved by W004: `ListingPublished` carries `FeePercentage`, read from platform config. MVP uses `appsettings.json`. Per-seller/per-listing override story still open. |
| W003-7 | Should `PendingSettlement` projection be cleaned up after `SettlementCompleted`, or retained for audit? | W003 Ph1 | Settlement BC | Implementation detail. |
| W004-P1-3 | Platform config location for fees and other defaults? | W004 Ph1 | Infrastructure | MVP: `appsettings.json`. Post-MVP: revisit. |
| W004-P1-4 | Image/media handling scope? | W004 Ph1 | Selling BC or sub-workshop | Post-MVP or dedicated sub-workshop. |
| W004-P2-7 | Seller UX for finding relisted versions? | W004 Ph2 | Listings/Frontend | Frontend workshop. |
| W004-P2-8 | Publish notification via Relay or HTTP 200 sufficient? | W004 Ph2 | Relay BC | Relay workshop. |
| W004-P2-9 | Is `RegisteredSellers` the only Selling projection? | W004 Ph2 | Selling BC | Likely yes; confirm during M2 coding. |

**Open count: 20** — 6 frontend, 3 implementation-detail, 4 Settlement/Ops post-MVP, 3 infrastructure/config, 2 Relay, 2 milestone/test-time.

---

## Resolved Questions

| ID | Question | Source | Resolved In | Resolution |
|---|---|---|---|---|
| W001-1 | Listing UI before session starts? | W001 Ph1 | W004 Ph1 | Hidden from participant catalog until `ListingAttachedToSession`. Visible in ops dashboard immediately. |
| W001-2 | `SessionStarted` → N × `BiddingOpened` fan-out | W001 Ph1 | W002 Ph1 | Option B: Session aggregate produces `SessionStarted`; separate Wolverine handler produces `BiddingOpened` per listing. |
| W001-3 | Promote `ProxyBidExhausted` to integration? | W001 Ph1 | W002 Ph1 | Yes. Relay pushes distinct "proxy exceeded" notification. |
| W001-4 | Multiple sequential extended bidding triggers | W001 Ph1 | W002 Ph1 | No count limit. Extensions chain. `MaxDuration` config caps total duration. |
| W001-5 | Reserve check authority: Auctions vs Settlement | W001 Ph1 | W002 Ph1 + W003 Ph1 | Auctions fires `ReserveMet` as real-time UX signal. Settlement performs authoritative `ReserveCheckCompleted` using cached reserve from `PendingSettlement` projection. |
| W001-8 | Can a proxy bid trigger extended bidding? | W001 Ph2 | W002 Ph1 | Yes. DCB handler is bid-source-agnostic. |
| W001-14 | Automated approval: single handler chain or separate steps? | W001 Ph4 | W004 Ph1 | Single handler chain for MVP. Migrates to separate handlers post-MVP without event vocabulary changes. |
| W002-P1-1 | Bid increment strategy | W002 Ph1 | W002 Ph2 | Two-tier: $1 under $100, $5 at $100+. Platform default. |
| W002-P1-2 | `BidRejected` stream placement | W002 Ph1 | W002 Ph2 | Separate stream, tagged with `ListingId`. Excluded from DCB tag query. |
| W002-P1-3 | Saga tracks vs DCB read at close | W002 Ph1 | W002 Ph2 | Incremental tracking via handlers. Trust Wolverine delivery guarantees. |
| W002-P1-4 | `ListingWithdrawn` saga interaction | W002 Ph1 | W002 Ph2 | Terminates both Auction Closing and all Proxy Bid Manager sagas. No reserve check, no sold/passed. |
| W002-P1-5 | Proxy bid rejection handling | W002 Ph1 | W002 Ph2 | Proxy stores `BidderCreditCeiling` at registration. Caps auto-bid at `min(next, max, ceiling)`. |
| W002-P1-6 | `MaxDuration` ownership | W002 Ph1 | W002 Ph2 | Platform default for MVP (2x original). Not seller-configurable until post-MVP. |
| W002-7 | `BidRejected` stream: dedicated Marten type or general audit pattern? | W002 Ph3 | M3-S1 | Dedicated Marten stream type per listing, tagged with `ListingId`. Excluded from DCB `EventTagQuery` by type-filter (narrowing `AndEventsOfType<...>`), not a separate stream-filter predicate. Rationale and rule live in `docs/skills/dynamic-consistency-boundary.md` under the "CritterBids Usage" section — S4 loads that skill before authoring the PlaceBid handler. |
| W002-9 | `BiddingOpened` payload: carry full config or saga loads from stream? | W002 Ph3 | M3-S1 | Carry full extended-bidding configuration on the contract (enabled flag, trigger window, extension duration, MaxDuration). Auction Closing saga is self-contained from `BiddingOpened` alone, no event-store lookup needed on saga reactions. Decision recorded inline in `src/CritterBids.Contracts/Auctions/BiddingOpened.cs` XML docstring. |
| W003-P1-1 | How does Settlement get the reserve value? | W003 Ph1 | W003 Ph1 | `PendingSettlement` projection built from `ListingPublished`, stored in Polecat. |
| W003-P1-2 | Projection race condition (`ListingSold` before projection catches up)? | W003 Ph1 | W003 Ph1 | Wolverine retry policy. Workflow retries if `PendingSettlement` not found. |
| W003-P1-3 | Wolverine Saga vs Process Manager? | W003 Ph1 | W003 Ph1 | Design around decider semantics. Hosting choice deferred to M5. **`ProcessManager<TState>` is Erik's JasperFx proposal, not shipping. Saga is the only currently-viable host.** |
| W003-P1-4 | Compensation logic for MVP? | W003 Ph1 | W003 Ph1 | None. Only failure path is reserve-not-met → `PaymentFailed` → terminate. |
| W003-P1-5 | Credit ledger — does the DCB see charges? | W003 Ph1 | W003 Ph1 | No. Ceiling is per-bid max, not running balance. Settlement records charges in its own stream. |
| W003-P1-6 | Buy It Now settlement path | W003 Ph1 | W003 Ph1 | Starts in `ReserveChecked(WasMet: true)`. Reserve check skipped for BIN. |
| W003-P1-7 | SettlementId strategy | W003 Ph1 | W003 Ph1 | Deterministic UUID v5 from ListingId. Idempotent by construction. |
| W003-crossBC-4 | `BuyItNowPrice >= ReservePrice` invariant — where does it live? | W003 Ph1 | W004 Ph3 | Selling BC. Validation rule 5.7. Settlement's BIN-skip-reserve-check backed by upstream guarantee. |
| W003-backlog-3 | `PaymentFailed` carries `ListingId` as first-class field | W003 vocab | 2026-04-10 | Canonical shape in `003-settlement-bc-deep-dive.md` (both Saga and decider sketches) already carries `ListingId`. `003-scenarios.md` aligned. No C# exists yet. No edits required. |
| W004-P1-1 | Explicit save vs auto-save on draft? | W004 Ph1 | W004 Ph2 | Explicit save only. |
| W004-P1-2 | Seller registration race condition | W004 Ph1 | W004 Ph2 | `RegisteredSellers` projection with Wolverine retry. Defense in depth: projection check, integration test, and API gateway pre-check. |
| W004-P1-5 | Relist fee — carry over or fresh? | W004 Ph1 | W004 Ph2 | Fresh config. New agreement at current rates. |
| W004-P1-6 | Mid-session listing revision | W004 Ph1 | W004 Ph2 | Selling accepts. Listings BC catalog projection filters during active sessions. |

**Resolved count: 28**

---

## Load-Bearing Assumptions

Some resolutions depend on external conditions that could change. Flagged here so they're not silently forgotten:

- **W003-P1-3 (Saga vs Process Manager)** depends on `ProcessManager<TState>` framework readiness at M5. If the proposal stalls or is rejected by JasperFx, Section 9 of W003 and its framework-agnostic phrasing are the first things to revisit. **Saga is the only currently-viable host today.**
- **W003-6 (FeePercentage source)** is resolved for MVP only. The per-seller/per-listing override story will resurface when a real fee model is designed.
- **W001-14 (automated approval chain)** resolved as "single handler chain now, separate handlers later without event vocabulary changes." The vocabulary-stability claim is the load-bearing part.

---

## How to Use This Ledger

- **Before a new workshop:** scan Open for the target BC. Matches go on the agenda.
- **During a workshop:** assign stable IDs (`W00X-N` or `W00X-PhY-N`) when questions are raised.
- **After a workshop:** update this ledger in the same PR as the workshop doc. Move resolved questions with the resolving workshop noted.
- **Before milestone planning:** scan Open for anything targeting that milestone's scope.
