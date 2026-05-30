using CritterBids.Relay.Hubs;

namespace CritterBids.Relay.Notifications;

/// <summary>
/// <see cref="BiddingHub"/> push delivered to the winning bidder when their settlement reaches
/// its terminal happy-path state. Composed by <c>SettlementCompletedHandler</c> from
/// <c>CritterBids.Contracts.Settlement.SettlementCompleted</c> and sent to the
/// <c>bidder:{WinnerId}</c> group per the contract docstring (winner-targeted confirmation).
///
/// The <c>remainingCredit</c> field named in narrative 001 Moment 8 is intentionally omitted in
/// M6-S5: composing it requires reading Settlement's <c>BidderCreditView</c> projection, and Relay
/// owns no Marten read model until M6-S6. <see cref="SettlementId"/> is carried for client-side
/// idempotency on retry.
/// </summary>
public sealed record SettlementCompletedNotification(
    Guid SettlementId,
    Guid ListingId,
    Guid WinnerId,
    decimal HammerPrice,
    DateTimeOffset CompletedAt) : IBiddingHubMessage
{
    Guid? IBiddingHubMessage.ListingId => ListingId;

    // Winner-targeted confirmation — routed to the single winning bidder's group.
    Guid? IBiddingHubMessage.BidderId => WinnerId;
}
