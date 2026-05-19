using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Marten;
using Wolverine;

namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal handler that resolves inbound bid + terminal events to the set of
/// active <see cref="ProxyBidManagerSaga"/> documents on the affected listing, then fans
/// out one wrapped command per match. Non-saga handler — plain static class alongside
/// <see cref="AuctionClosingSaga"/>, the BC's other subscriber on these same events.
///
/// <para><b>Composite-key correlation bridge (M4-S3 OQ1 Path C).</b> Wolverine's
/// <c>[SagaIdentityFrom]</c> resolves the saga id by reading a Guid property on the inbound
/// message; the Proxy Bid Manager's id is a UUID v5 derived from <c>(ListingId, BidderId)</c>
/// that no contract carries. This dispatcher is the bridge — it looks up the document id
/// out-of-band and routes a wrapped command that the saga correlates against the standard
/// property-pull path.</para>
///
/// <para><b>One inbound, N outbound.</b> A single <see cref="BidPlaced"/> may target every
/// proxy registered on the listing — the bidder whose proxy emitted the auto-bid (own-bid
/// tracking branch on its saga) and every competing bidder's proxy (competing-bid branch).
/// Terminal events (<see cref="ListingSold"/> / <see cref="ListingPassed"/> /
/// <see cref="ListingWithdrawn"/>) similarly broadcast to every active proxy on the listing
/// — each saga terminates independently.</para>
///
/// <para><b>Coexistence with AuctionClosingSaga.</b> The Auction Closing saga subscribes to
/// <see cref="BidPlaced"/> and <see cref="ListingWithdrawn"/> within the Auctions BC; the
/// dispatcher's new terminal methods add a second handler for each of
/// <see cref="ListingSold"/> / <see cref="ListingPassed"/> / <see cref="ListingWithdrawn"/>.
/// With <c>MultipleHandlerBehavior.Separated</c>, each handler runs on its own sticky local
/// queue; tests dispatching these events must use <c>SendMessageAndWaitAsync</c> rather
/// than <c>InvokeMessageAndWaitAsync</c> per the M4-S3 retro and the wolverine-sagas skill
/// §"Multiple Handlers + Separated — Send, Don't Invoke".</para>
///
/// <para><b>Empty fan-out is the common case.</b> Most listings have zero proxy
/// registrations, so the dispatcher's Marten query returns an empty list and the handler
/// emits no wrapped commands. The dispatcher always runs (Wolverine routes the message to
/// its sticky queue), so the cascade outcome event lands in <c>tracked.Sent</c> in tests
/// even when no proxy sagas are active.</para>
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

    public static async Task<OutgoingMessages> Handle(
        ListingSold message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var sagas = await QueryActiveSagasAsync(session, message.ListingId, cancellationToken);
        var outgoing = new OutgoingMessages();
        foreach (var saga in sagas)
        {
            outgoing.Add(new ProxyListingSoldObserved(saga.Id, message.ListingId));
        }
        return outgoing;
    }

    public static async Task<OutgoingMessages> Handle(
        ListingPassed message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var sagas = await QueryActiveSagasAsync(session, message.ListingId, cancellationToken);
        var outgoing = new OutgoingMessages();
        foreach (var saga in sagas)
        {
            outgoing.Add(new ProxyListingPassedObserved(saga.Id, message.ListingId));
        }
        return outgoing;
    }

    public static async Task<OutgoingMessages> Handle(
        ListingWithdrawn message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var sagas = await QueryActiveSagasAsync(session, message.ListingId, cancellationToken);
        var outgoing = new OutgoingMessages();
        foreach (var saga in sagas)
        {
            outgoing.Add(new ProxyListingWithdrawnObserved(saga.Id, message.ListingId));
        }
        return outgoing;
    }

    private static async Task<IReadOnlyList<ProxyBidManagerSaga>> QueryActiveSagasAsync(
        IDocumentSession session, Guid listingId, CancellationToken cancellationToken) =>
        await session.Query<ProxyBidManagerSaga>()
            .Where(s => s.ListingId == listingId
                        && s.Status == ProxyBidManagerStatus.Active)
            .ToListAsync(cancellationToken);
}
