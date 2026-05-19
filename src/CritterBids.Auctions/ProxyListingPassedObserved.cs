namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal terminal-event command emitted by
/// <see cref="ProxyBidDispatchHandler"/> for each active <see cref="ProxyBidManagerSaga"/>
/// that should react to an inbound <see cref="CritterBids.Contracts.Auctions.ListingPassed"/>.
/// Carries the resolved target <see cref="SagaId"/> for the saga's
/// <c>[SagaIdentityFrom(nameof(SagaId))]</c> correlation. See
/// <see cref="ProxyListingSoldObserved"/> for the M4-S4 OQ1 Path A rationale common to all
/// three terminal wrappers.
/// </summary>
public sealed record ProxyListingPassedObserved(Guid SagaId, Guid ListingId);
