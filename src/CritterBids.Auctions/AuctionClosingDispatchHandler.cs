using CritterBids.Contracts.Auctions;

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
/// <para><b>Delivery count.</b> Contract events fan out once per consuming RabbitMQ queue (the
/// same at-least-once shape <c>BiddingOpened</c> has always had), so the saga may receive each
/// observed command several times. Its existing idempotency guards (BidCount monotonicity,
/// set-to-true, terminal-status early returns) absorb the duplicates.</para>
/// </summary>
public static class AuctionClosingDispatchHandler
{
    public static ClosingBidObserved Handle(BidPlaced message) =>
        new(message.ListingId, message.BidderId, message.Amount, message.BidCount);

    public static ClosingReserveMetObserved Handle(ReserveMet message) =>
        new(message.ListingId);

    public static ClosingExtendedBiddingObserved Handle(ExtendedBiddingTriggered message) =>
        new(message.ListingId, message.NewCloseAt);

    public static ClosingBuyItNowObserved Handle(BuyItNowPurchased message) =>
        new(message.ListingId);

    public static ClosingListingWithdrawnObserved Handle(CritterBids.Contracts.Selling.ListingWithdrawn message) =>
        new(message.ListingId);
}
