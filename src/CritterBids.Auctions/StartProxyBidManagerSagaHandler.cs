using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine;

namespace CritterBids.Auctions;

/// <summary>
/// Starts the <see cref="ProxyBidManagerSaga"/> on inbound <see cref="RegisterProxyBid"/>.
/// Lives in a separate static class per the wolverine-sagas skill (§Starting a Saga) so
/// Wolverine can distinguish "create + persist" from "load existing and handle".
///
/// <para><b>Composite-key id derivation.</b> The saga's <see cref="ProxyBidManagerSaga.Id"/>
/// is <c>UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")</c>
/// via <see cref="AuctionsIdentityHelpers.ProxyBidManagerSagaId"/>. The same command
/// (<see cref="RegisterProxyBid"/>) consumed twice for the same <c>(ListingId, BidderId)</c>
/// derives the same id; the existence check below converts that into idempotent
/// re-registration without an explicit dedup set. Mirrors the
/// <see cref="CritterBids.Settlement.StartSettlementSagaHandler"/> shape (the first lived
/// in-repo UUID v5 + existence-check pattern, M5-S4).</para>
///
/// <para><b>Emission shape (M4-S3 OQ3 Path a).</b> <see cref="ProxyBidRegistered"/> is
/// emitted via <c>OutgoingMessages</c> as a bus message. No consumer is wired in S3 (Relay
/// lands post-M5 per the contract's docstring), so the integration test asserts the event
/// reaches <c>tracked.NoRoutes</c> — the same fixture-stance pattern Settlement's
/// SellerPayoutIssued/PaymentFailed routes follow at M5-S6.</para>
///
/// <para><b>Credit-ceiling lookup (M4-S4 — M4-S3 OQ4 deferred path resolved).</b>
/// <see cref="ProxyBidManagerSaga.BidderCreditCeiling"/> is populated from the
/// Auctions-side <see cref="ParticipantCreditCeiling"/> projection (M4-D4 duplicate-
/// projection pattern, second lived application — first was Settlement's
/// <c>BidderCreditView</c> at M5-S5). If the projection has not caught up from
/// <c>ParticipantSessionStarted</c> on the <c>auctions-participants-events</c> queue, the
/// handler throws <see cref="ParticipantCreditCeilingNotFoundException"/>;
/// <see cref="AuctionsConcurrencyRetryPolicies"/> catches and re-queues with progressive
/// backoff (mirrors the M5-S4 <c>PendingSettlementNotFoundException</c> shape from
/// <c>SettlementsConcurrencyRetryPolicies</c>). The race essentially never fires in
/// practice — participants establish their credit ceiling well before they register a
/// proxy bid.</para>
/// </summary>
public static class StartProxyBidManagerSagaHandler
{
    public static async Task<(ProxyBidManagerSaga?, OutgoingMessages)> Handle(
        RegisterProxyBid message,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(
            message.ListingId, message.BidderId);

        // Idempotent re-registration: the deterministic composite-key id collides on every
        // re-delivery of RegisterProxyBid for the same (listing, bidder), so the existence
        // check absorbs the duplicate without creating a second saga.
        var existing = await session.LoadAsync<ProxyBidManagerSaga>(sagaId, cancellationToken);
        if (existing is not null)
        {
            return (null, new OutgoingMessages());
        }

        // Load the bidder's credit ceiling from the local Auctions projection. Throws on
        // not-found; the retry policy in AuctionsConcurrencyRetryPolicies re-queues the
        // command with backoff until the projection catches up from the
        // auctions-participants-events queue.
        var ceiling = await session.LoadAsync<ParticipantCreditCeiling>(
            message.BidderId, cancellationToken)
            ?? throw new ParticipantCreditCeilingNotFoundException(message.BidderId);

        var saga = new ProxyBidManagerSaga
        {
            Id = sagaId,
            ListingId = message.ListingId,
            BidderId = message.BidderId,
            MaxAmount = message.MaxAmount,
            BidderCreditCeiling = ceiling.CreditCeiling,
            LastBidAmount = 0m,
            Status = ProxyBidManagerStatus.Active,
        };

        var registered = new ProxyBidRegistered(
            ListingId: message.ListingId,
            BidderId: message.BidderId,
            MaxAmount: message.MaxAmount,
            RegisteredAt: time.GetUtcNow());

        return (saga, new OutgoingMessages { registered });
    }
}
