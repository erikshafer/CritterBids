namespace CritterBids.Auctions;

/// <summary>
/// Saga-internal command dispatched by the Auction Closing saga as a scheduled message.
/// Delivered at the listing's scheduled close time, cancelled and rescheduled each time
/// extended bidding fires. Not an integration event — lives only inside CritterBids.Auctions
/// and is never published to CritterBids.Contracts. Type accessibility is public (not C#
/// internal) because Wolverine's handler discovery scans only public Handle methods, and the
/// public saga exposes a Handle(CloseAuction) method that requires at-least-as-accessible
/// parameters. The architectural boundary (no Contracts reference) is what the BC isolation
/// rule actually constrains.
///
/// Correlation: ListingId doubles as the saga identity (Saga.Id == ListingId per M3-S5
/// decision); Wolverine uses the [SagaIdentityFrom(nameof(ListingId))] attribute on the
/// saga handler parameter to locate the saga instance without a redundant SagaId property.
/// </summary>
public sealed record CloseAuction(Guid ListingId, DateTimeOffset ScheduledAt);
