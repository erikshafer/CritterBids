using CritterBids.Contracts.Auctions;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace CritterBids.Auctions;

/// <summary>
/// Second CritterBids saga. Defends one participant's position on one listing by auto-bidding
/// up to <see cref="MaxAmount"/> whenever a competing bid arrives. One saga instance per
/// <c>(ListingId, BidderId)</c> composite — the document's <see cref="Id"/> is the
/// deterministic UUID v5 derived by
/// <see cref="AuctionsIdentityHelpers.ProxyBidManagerSagaId"/>.
///
/// <para><b>Correlation (M4-S3 OQ1 — Path C).</b> Wolverine's <c>[SagaIdentityFrom]</c>
/// attribute resolves the saga id by reading a named property off the inbound message;
/// it has no expression / delegate / method-based resolution mode (verified by reading
/// <c>Wolverine.Persistence.Sagas.PullSagaIdFromMessageFrame</c>). The two existing
/// sagas (<see cref="AuctionClosingSaga"/>, <c>SettlementSaga</c>) correlate against a
/// Guid property already on the inbound contract; the Proxy Bid Manager's id is a derived
/// composite that no contract carries.</para>
///
/// <para>Path A (resolver-based <c>[SagaIdentityFrom]</c>) is unavailable. Path B (add
/// <c>ProxyBidManagerSagaId</c> to <see cref="BidPlaced"/>) is incompatible with multi-saga
/// dispatch — a single <c>BidPlaced</c> can target N proxy sagas (one per registered bidder
/// on the listing), and one Guid field cannot address many. The saga therefore correlates
/// against an Auctions-internal command <see cref="ProxyBidObserved"/>, dispatched by
/// <see cref="ProxyBidDispatchHandler"/> which receives <see cref="BidPlaced"/>, queries
/// active sagas on the listing, and fans out one <see cref="ProxyBidObserved"/> per match
/// — each carrying its target saga's resolved id. <c>[SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))]</c>
/// then loads the saga via the standard property-pull path.</para>
///
/// <para><b>Idempotency (M4-S3 / M4 milestone §6).</b> Own-bid arrivals (<see cref="BidderId"/>
/// matches the inbound bidder) update <see cref="LastBidAmount"/> only if strictly higher —
/// monotone tracking, safe under at-least-once redelivery. Competing-bid arrivals emit one
/// <c>PlaceBid</c> at <c>competingBid + increment</c> when within <see cref="MaxAmount"/>;
/// the exhaustion branch (next defensive bid would exceed <see cref="MaxAmount"/>) is
/// authored at M4-S4 — Workshop 002 §4.3 and §4.9 are out of S3 scope.</para>
///
/// <para><b>S3 scope (this slice):</b> start handler (<see cref="StartProxyBidManagerSagaHandler"/>),
/// reactive own-bid + competing-bid branches up to (but not including) exhaustion emission.
/// Scenarios 4.1 / 4.2 / 4.4 / 4.5 are green.</para>
///
/// <para><b>Out of S3 scope:</b> exhaustion emission (4.3 / 4.9), terminal handlers
/// (4.6 / 4.7 / 4.8), credit-ceiling cap, two-proxy bidding war (4.10),
/// register-while-outbid (4.11). All M4-S4.</para>
/// </summary>
public sealed class ProxyBidManagerSaga : Wolverine.Saga, JasperFx.IRevisioned
{
    public Guid Id { get; set; }

    /// <summary>
    /// Marten numeric revision (JasperFx.IRevisioned) — required for Wolverine to emit the
    /// revision-checked saga update; the schema's UseNumericRevisions alone does not enforce
    /// optimistic concurrency (M8-S3c finding, see AuctionClosingSaga's concurrency note).
    /// </summary>
    public int Version { get; set; }
    public Guid ListingId { get; set; }
    public Guid BidderId { get; set; }
    public decimal MaxAmount { get; set; }

    /// <summary>
    /// Credit-ceiling cap on the saga's auto-bids (scenario 4.9). Populated at M4-S4 from
    /// an Auctions-side <c>ParticipantCreditCeiling</c> projection (the M4-D4 duplicate-
    /// projection pattern applied a second time — M4-S3 OQ4 Path b). S3 defaults to <c>0m</c>
    /// because none of the S3 scenarios (4.1 / 4.2 / 4.4 / 4.5) consult the field; the
    /// exhaustion calc that does is S4's.
    /// </summary>
    public decimal BidderCreditCeiling { get; set; }

    public decimal LastBidAmount { get; set; }
    public ProxyBidManagerStatus Status { get; set; } = ProxyBidManagerStatus.Active;

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(ProxyBidObserved.SagaId))] ProxyBidObserved message)
    {
        // Idempotent terminal guard. A late dispatch arriving after the saga has reached
        // a terminal status (Exhausted, ListingClosed — both M4-S4) is silently absorbed
        // rather than re-emitting. The Active-only path below is the only reactive shape
        // exercised in S3; the guard is sized for S4's branches.
        if (Status != ProxyBidManagerStatus.Active) return new OutgoingMessages();

        if (message.BidderId == BidderId)
        {
            // Own bid — scenarios 4.4 (proxy-emitted) and 4.5 (manual). Monotone tracking:
            // re-deliveries of a lower-or-equal amount are no-ops. The IsProxy flag is
            // observed but does not branch — same tracking shape for proxy and manual bids
            // per Workshop 002 §4.4 / §4.5.
            if (message.Amount > LastBidAmount)
            {
                LastBidAmount = message.Amount;
            }
            return new OutgoingMessages();
        }

        // Competing bid — scenarios 4.2 / 4.3 / 4.9. Workshop 002 increment: $1 under $100,
        // $5 at $100+. Inline math (third co-located copy alongside PlaceBidHandler.cs:174-175
        // and the bidding-war cascade); S4 retro decides extraction per CLAUDE.md's
        // "three similar lines is better than a premature abstraction" rule.
        var increment = message.Amount >= 100m ? 5m : 1m;

        // M4-S4 exhaustion calc (Workshop 002 §4.9, corrected formula — the workshop's
        // first-pass example using competing $195 was inconsistent; the inline correction
        // below it uses competing $200 to show the true exhaustion case). The next defensive
        // bid is the minimum of three caps: the natural next-increment-above-competing,
        // the proxy's MaxAmount, and the bidder's overall credit ceiling. If that minimum
        // doesn't strictly exceed the competing bid, the proxy can't beat the current bid
        // and exhausts.
        var capped = Math.Min(Math.Min(message.Amount + increment, MaxAmount), BidderCreditCeiling);

        if (capped <= message.Amount)
        {
            // Exhaustion is terminal: emit the audit event (M4-S4 OQ2 Path a — bus-only via
            // OutgoingMessages, no AddEventType, lands in tracked.NoRoutes per the M4-S3
            // ProxyBidRegistered precedent), transition state, and MarkCompleted() to delete
            // the saga document. Cross-BC consumer is post-M5 Relay per
            // src/CritterBids.Contracts/Auctions/ProxyBidExhausted.cs.
            Status = ProxyBidManagerStatus.Exhausted;
            MarkCompleted();
            return new OutgoingMessages
            {
                new ProxyBidExhausted(
                    ListingId: ListingId,
                    BidderId: BidderId,
                    MaxAmount: MaxAmount,
                    ExhaustedAt: message.PlacedAt),
            };
        }

        return new OutgoingMessages
        {
            new PlaceBid(
                ListingId: message.ListingId,
                BidId: Guid.CreateVersion7(),
                BidderId: BidderId,
                Amount: capped,
                CreditCeiling: BidderCreditCeiling),
        };
    }

    // ─── M4-S4 terminal handlers (Workshop 002 §4.6 / §4.7 / §4.8) ─────────────
    //
    // All three terminal events flow through ProxyBidDispatchHandler, which queries active
    // sagas on the listing and emits one wrapped command (ProxyListingXxxObserved) per
    // match. The wrapped command's SagaId field drives Wolverine's standard property-pull
    // correlation. Each handler is idempotent under at-least-once redelivery via the
    // Status guard — a late dispatch arriving after the saga has already terminated
    // (Exhausted or ListingClosed) is silently absorbed.
    //
    // Static NotFound absorbers are essential: the saga document is deleted by
    // MarkCompleted() at the previous terminal step, but a redelivered or out-of-order
    // ProxyListingXxxObserved may still target the deleted id. Without the absorber,
    // Wolverine throws UnknownSagaException. Symmetric pattern with
    // AuctionClosingSaga.NotFound(CloseAuction) at line 146.

    public void Handle(
        [SagaIdentityFrom(nameof(ProxyListingSoldObserved.SagaId))] ProxyListingSoldObserved message)
    {
        if (Status != ProxyBidManagerStatus.Active) return;
        Status = ProxyBidManagerStatus.ListingClosed;
        MarkCompleted();
    }

    public static OutgoingMessages NotFound(ProxyListingSoldObserved message) => new();

    public void Handle(
        [SagaIdentityFrom(nameof(ProxyListingPassedObserved.SagaId))] ProxyListingPassedObserved message)
    {
        if (Status != ProxyBidManagerStatus.Active) return;
        Status = ProxyBidManagerStatus.ListingClosed;
        MarkCompleted();
    }

    public static OutgoingMessages NotFound(ProxyListingPassedObserved message) => new();

    public void Handle(
        [SagaIdentityFrom(nameof(ProxyListingWithdrawnObserved.SagaId))] ProxyListingWithdrawnObserved message)
    {
        if (Status != ProxyBidManagerStatus.Active) return;
        Status = ProxyBidManagerStatus.ListingClosed;
        MarkCompleted();
    }

    public static OutgoingMessages NotFound(ProxyListingWithdrawnObserved message) => new();
}
