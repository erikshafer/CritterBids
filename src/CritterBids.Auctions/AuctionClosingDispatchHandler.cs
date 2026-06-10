using CritterBids.Contracts.Auctions;
using Wolverine.Attributes;

namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal bridge between the cross-BC contract events and the
/// <see cref="AuctionClosingSaga"/> (M8 Bug #2 fix). Pure translation handlers — no IO — each
/// converting one inbound contract event into the saga-directed internal command the saga
/// correlates on (<c>[SagaIdentityFrom]</c>, Saga.Id == ListingId).
///
/// <para><b>Why the saga cannot subscribe to these events directly.</b> Under
/// <c>MultipleHandlerBehavior.Separated</c>, Wolverine 6.5.1 keeps a SINGLE saga type's
/// continue-handlers as the chain's default handler (<c>SagaChain.maybeAssignStickyHandlers</c>
/// only separates sagas when more than one saga type handles the message), and a chain with a
/// default handler never fans externally-delivered messages out to the sticky local queues the
/// other consumers live on. The saga therefore consumed every RabbitMQ delivery of
/// <c>BidPlaced</c> / <c>ReserveMet</c> / <c>ExtendedBiddingTriggered</c> while Listings,
/// Relay, and Operations silently starved. With this bridge the contract-event chains are
/// saga-free — every handler is sticky, the fan-out fires for all of them — and the saga
/// receives its updates on the internal commands' own single-handler chains. Root cause +
/// upstream fix proposal: <c>docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md</c>
/// and <c>docs/research/wolverine-upstream-saga-sticky-separation-handoff.md</c>.</para>
///
/// <para><b>Pattern precedent.</b> Same shape as <see cref="ProxyBidDispatchHandler"/> (M4-S3
/// OQ1 Path C), which bridged the Proxy Bid Manager saga for composite-key correlation — and,
/// in hindsight, accidentally immunized that saga against this exact dispatch defect. Unlike
/// the proxy dispatcher there is no saga lookup here: the closing saga is 1:1 with the listing,
/// so translation needs no query. Late deliveries to an already-completed saga are absorbed by
/// the saga's static <c>NotFound</c> methods.</para>
///
/// <para><b>Delivery count.</b> Under ADR 027 each contract event arrives exactly once, on the
/// <c>auctions-auctions-events</c> queue this class is sticky to. The saga's idempotency guards
/// (BidCount monotonicity, set-to-true, terminal-status early returns) remain as at-least-once
/// REDELIVERY hygiene — broker redeliveries still duplicate, steady-state traffic no longer does.</para>
///
/// <para><b>One sticky chain per (message type, endpoint) — ADR 027 / M8-S3c.</b> Wolverine 6.5.1
/// resolves the sticky handler for an endpoint with <c>ByEndpoint.FirstOrDefault</c>, so two handler
/// classes sticky to the same queue for the same message type would silently starve one of them.
/// <c>BidPlaced</c> and <c>ListingWithdrawn</c> are consumed by BOTH Auctions dispatchers, so their
/// discovered handlers live on <see cref="ProxyBidDispatchHandler"/> (which needs the saga query
/// anyway) and this class contributes pure <c>Translate</c> functions the proxy dispatcher emits
/// alongside its own commands. The three events only the closing saga observes keep their
/// discovered handlers here.</para>
/// </summary>
[StickyHandler("auctions-auctions-events")]
public static class AuctionClosingDispatchHandler
{
    public static ClosingReserveMetObserved Handle(ReserveMet message) =>
        new(message.ListingId);

    public static ClosingExtendedBiddingObserved Handle(ExtendedBiddingTriggered message) =>
        new(message.ListingId, message.NewCloseAt);

    public static ClosingBuyItNowObserved Handle(BuyItNowPurchased message) =>
        new(message.ListingId);

    /// <summary>
    /// Pure translation for the closing saga's BidPlaced observation. Not named <c>Handle</c> on
    /// purpose — discovery for BidPlaced at the auctions queue belongs to
    /// <see cref="ProxyBidDispatchHandler"/>, which emits this command alongside the proxy fan-out.
    /// </summary>
    public static ClosingBidObserved Translate(BidPlaced message) =>
        new(message.ListingId, message.BidderId, message.Amount, message.BidCount);

    /// <summary>
    /// Pure translation for the closing saga's withdrawal observation. Not named <c>Handle</c> —
    /// see <see cref="Translate(BidPlaced)"/>.
    /// </summary>
    public static ClosingListingWithdrawnObserved Translate(CritterBids.Contracts.Selling.ListingWithdrawn message) =>
        new(message.ListingId);
}
