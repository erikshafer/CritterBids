namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal command emitted by <see cref="AuctionClosingDispatchHandler"/> when a
/// <see cref="CritterBids.Contracts.Auctions.BidPlaced"/> arrives, carrying the fields the
/// <see cref="AuctionClosingSaga"/> tracks. The saga correlates via
/// <c>[SagaIdentityFrom(nameof(ListingId))]</c> (Saga.Id == ListingId, M3-S5 OQ1 Path A —
/// unchanged by the bridge).
///
/// <para><b>Why the saga no longer handles the contract event directly.</b> Under
/// <c>MultipleHandlerBehavior.Separated</c>, Wolverine 6.5.1 leaves a SINGLE saga type's
/// continue-handlers in the chain's default <c>Handlers</c> (only the multi-saga case is
/// separated into sticky chains — <c>SagaChain.maybeAssignStickyHandlers</c>,
/// <c>groupedSagas.Length &gt; 1</c>). A chain with default handlers never takes the
/// <c>FanoutMessageHandler</c> path for deliveries arriving at external (RabbitMQ) endpoints,
/// so the saga consumed every <c>BidPlaced</c> delivery and the sticky cross-BC consumers
/// (Listings / Relay / Operations) silently starved — M8 Bug #2. Bridging the saga behind this
/// internal command (the <see cref="ProxyBidObserved"/> Path-C shape) removes the saga from the
/// contract event's chain: all its handlers are sticky, fan-out fires, and the saga still gets
/// exactly the updates it needs on this command's own single-handler chain. Root cause +
/// upstream fix: <c>docs/research/jasperfx-escalation-bidplaced-cross-bc-delivery.md</c>.</para>
///
/// <para>Not on a RabbitMQ queue; never leaves the Auctions BC (modular-monolith project graph —
/// no other BC references <c>CritterBids.Auctions</c>). Public for Wolverine handler-signature
/// accessibility, same rationale as <see cref="ProxyBidObserved"/>.</para>
/// </summary>
public sealed record ClosingBidObserved(
    Guid ListingId,
    Guid BidderId,
    decimal Amount,
    int BidCount);
