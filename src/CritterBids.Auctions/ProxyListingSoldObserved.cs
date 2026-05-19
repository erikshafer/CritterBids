namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal terminal-event command emitted by
/// <see cref="ProxyBidDispatchHandler"/> for each active <see cref="ProxyBidManagerSaga"/>
/// that should react to an inbound <see cref="CritterBids.Contracts.Auctions.ListingSold"/>.
/// Carries the resolved target <see cref="SagaId"/> so the saga's
/// <c>[SagaIdentityFrom(nameof(SagaId))]</c> can load the right document via Wolverine's
/// standard property-pull correlation path.
///
/// <para><b>Why this command exists (M4-S4 OQ1 Path A).</b> Same composite-key correlation
/// reason as the M4-S3 <see cref="ProxyBidObserved"/>: <c>[SagaIdentityFrom]</c> resolves
/// only a Guid property by name, and the Proxy Bid Manager's id is the UUID v5 composite
/// <c>$"{ListingId}:{BidderId}"</c> that no contract event carries. M4-S4 OQ1 chose Path A
/// (three separate wrapped commands, one per terminal event) over Path B (single
/// polymorphic command with a discriminator) for symmetry with the existing
/// <see cref="ProxyBidObserved"/> shape — each handler stays straightforward and the
/// dispatcher's three terminal Handle methods each emit their own wrapped type.</para>
///
/// <para>Not on a RabbitMQ queue. Not exposed outside the Auctions BC by reference graph.
/// Declared <c>public</c> for C# accessibility because the saga's <c>public Handle</c>
/// takes it as a parameter and Wolverine discovers public Handle methods.</para>
/// </summary>
public sealed record ProxyListingSoldObserved(Guid SagaId, Guid ListingId);
