using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine;

namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal handler that resolves <see cref="BidPlaced"/> deliveries to the set of
/// active <see cref="ProxyBidManagerSaga"/> documents on the listing, then fans out one
/// <see cref="ProxyBidObserved"/> per match. Non-saga handler — it is a plain static class
/// alongside <see cref="AuctionClosingSaga.Handle(BidPlaced)"/>, the BC's other
/// <c>BidPlaced</c> subscriber.
///
/// <para><b>Composite-key correlation bridge (M4-S3 OQ1 Path C).</b> Wolverine's
/// <c>[SagaIdentityFrom]</c> resolves the saga id by reading a Guid property on the inbound
/// message; the Proxy Bid Manager's id is a UUID v5 derived from <c>(ListingId, BidderId)</c>
/// that no contract carries. This dispatcher is the bridge — it looks up the document id
/// out-of-band and routes a wrapped command (<see cref="ProxyBidObserved"/>) that the saga
/// correlates against the standard property-pull path.</para>
///
/// <para><b>One inbound, N outbound.</b> A single <see cref="BidPlaced"/> may target every
/// proxy registered on the listing — the bidder whose proxy emitted the auto-bid (own-bid
/// tracking branch on its saga) and every competing bidder's proxy (competing-bid branch).
/// The dispatcher emits one <see cref="ProxyBidObserved"/> per active saga; each saga's
/// reactive <c>Handle</c> applies its own own-bid vs competing-bid logic.</para>
///
/// <para><b>Coexistence with AuctionClosingSaga.</b> The Auction Closing saga is the other
/// <see cref="BidPlaced"/> subscriber inside the Auctions BC. With
/// <c>MultipleHandlerBehavior.Separated</c> (set in <c>Program.cs</c>), each handler is its
/// own endpoint; both fire independently on every <see cref="BidPlaced"/>. The dispatcher's
/// emissions are processed in-process via <c>OutgoingMessages</c>; no RabbitMQ routing
/// involved.</para>
/// </summary>
public static class ProxyBidDispatchHandler
{
    public static async Task<OutgoingMessages> Handle(
        BidPlaced message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Query active proxy sagas on this listing. Empty result is the common case (most
        // listings have zero proxy registrations); the saga document doesn't exist before
        // RegisterProxyBid, so this is a cheap key-range scan in production.
        var sagas = await session.Query<ProxyBidManagerSaga>()
            .Where(s => s.ListingId == message.ListingId
                        && s.Status == ProxyBidManagerStatus.Active)
            .ToListAsync(cancellationToken);

        if (sagas.Count == 0)
        {
            return new OutgoingMessages();
        }

        var outgoing = new OutgoingMessages();
        foreach (var saga in sagas)
        {
            outgoing.Add(new ProxyBidObserved(
                SagaId: saga.Id,
                ListingId: message.ListingId,
                BidId: message.BidId,
                BidderId: message.BidderId,
                Amount: message.Amount,
                BidCount: message.BidCount,
                IsProxy: message.IsProxy,
                PlacedAt: message.PlacedAt));
        }

        return outgoing;
    }
}
