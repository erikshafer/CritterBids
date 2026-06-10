using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine;
using Wolverine.Attributes;

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
/// <para><b>Coexistence with AuctionClosingSaga (ADR 027 shape).</b> Since the M8 Bug #2 fix the
/// Auction Closing saga receives its contract-event observations through a dispatcher bridge, and
/// since M8-S3c both Auctions dispatchers are sticky to the BC's own
/// <c>auctions-auctions-events</c> queue. Wolverine 6.5.1 executes at most ONE sticky handler
/// class per (message type, endpoint) — <c>ByEndpoint.FirstOrDefault</c> — so the two events both
/// dispatchers observe (<see cref="BidPlaced"/>, <c>ListingWithdrawn</c>) are discovered HERE only,
/// and this handler emits the closing saga's command (via
/// <see cref="AuctionClosingDispatchHandler"/>'s pure <c>Translate</c> functions) alongside the
/// proxy fan-out. Tests dispatching multi-handler events must use <c>SendMessageAndWaitAsync</c>
/// rather than <c>InvokeMessageAndWaitAsync</c> per the M4-S3 retro and the wolverine-sagas skill.</para>
///
/// <para><b>Empty proxy fan-out is the common case.</b> Most listings have zero proxy
/// registrations, so the saga query returns an empty list and the handler emits only the
/// closing-saga command (for <c>BidPlaced</c>/<c>ListingWithdrawn</c>) or nothing
/// (<c>ListingSold</c>/<c>ListingPassed</c>). The dispatcher always runs, so cascade outcomes
/// land in <c>tracked.Sent</c> in tests even when no proxy sagas are active.</para>
/// </summary>
[StickyHandler("auctions-auctions-events")]
public static class ProxyBidDispatchHandler
{
    public static async Task<OutgoingMessages> Handle(
        BidPlaced message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // The closing saga's observation rides every accepted bid regardless of proxy activity.
        var outgoing = new OutgoingMessages
        {
            AuctionClosingDispatchHandler.Translate(message),
        };

        // Query active proxy sagas on this listing. Empty result is the common case (most
        // listings have zero proxy registrations); the saga document doesn't exist before
        // RegisterProxyBid, so this is a cheap key-range scan in production.
        var sagas = await session.Query<ProxyBidManagerSaga>()
            .Where(s => s.ListingId == message.ListingId
                        && s.Status == ProxyBidManagerStatus.Active)
            .ToListAsync(cancellationToken);

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
        CritterBids.Contracts.Selling.ListingWithdrawn message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // The closing saga's withdrawal observation rides every withdrawal regardless of
        // proxy activity (same consolidation as Handle(BidPlaced) above).
        var outgoing = new OutgoingMessages
        {
            AuctionClosingDispatchHandler.Translate(message),
        };

        var sagas = await QueryActiveSagasAsync(session, message.ListingId, cancellationToken);
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
