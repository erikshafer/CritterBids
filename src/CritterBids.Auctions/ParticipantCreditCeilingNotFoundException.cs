namespace CritterBids.Auctions;

/// <summary>
/// Thrown by <see cref="StartProxyBidManagerSagaHandler"/> when a <c>RegisterProxyBid</c>
/// arrives but the corresponding <see cref="ParticipantCreditCeiling"/> projection row
/// has not yet caught up from <c>ParticipantSessionStarted</c>. Wolverine's retry policy
/// re-queues the inbound command with progressive backoff per the M5-S4
/// <see cref="CritterBids.Settlement.PendingSettlementNotFoundException"/> precedent —
/// the triggering message stays in the queue until the projection row appears.
///
/// <para><b>Retryable.</b> Registered in
/// <see cref="AuctionsConcurrencyRetryPolicies"/>'s
/// <c>OnException&lt;ParticipantCreditCeilingNotFoundException&gt;().RetryWithCooldown(...)</c>.</para>
///
/// <para>In practice this race rarely fires — participants register and receive their
/// credit ceiling well before they register a proxy bid on any listing. The exception is
/// the correctness guarantee if the auctions-participants-events queue is a few
/// milliseconds behind the saga-start dispatch.</para>
/// </summary>
public sealed class ParticipantCreditCeilingNotFoundException : Exception
{
    public ParticipantCreditCeilingNotFoundException(Guid bidderId)
        : base($"ParticipantCreditCeiling not found for bidder '{bidderId}' — Auctions projection has not caught up from ParticipantSessionStarted.")
    {
        BidderId = bidderId;
    }

    public Guid BidderId { get; }
}
