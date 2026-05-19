namespace CritterBids.Auctions;

/// <summary>
/// Auctions-internal command emitted by <see cref="ProxyBidDispatchHandler"/> for each
/// active <see cref="ProxyBidManagerSaga"/> that should react to an inbound
/// <see cref="CritterBids.Contracts.Auctions.BidPlaced"/>. Carries the original bid's full
/// payload plus the resolved target <see cref="SagaId"/> so the saga's
/// <c>[SagaIdentityFrom(nameof(SagaId))]</c> can load the right document via Wolverine's
/// standard property-pull correlation path.
///
/// <para><b>Why this command exists (M4-S3 OQ1 Path C).</b> Wolverine's <c>[SagaIdentityFrom]</c>
/// only resolves a Guid property by name; the Proxy Bid Manager's id is a derived UUID v5
/// composite (<c>$"{ListingId}:{BidderId}"</c>) that no contract event carries. A single
/// <see cref="CritterBids.Contracts.Auctions.BidPlaced"/> can target N proxy sagas
/// (one per registered bidder on the listing), which rules out Path B (add a field to
/// <c>BidPlaced</c>) — one field cannot address many sagas. The dispatcher pattern is the
/// only viable correlation shape: query active sagas, fan out one wrapped command per
/// target with each target's resolved id.</para>
///
/// <para>Not on a RabbitMQ queue. Not exposed outside the Auctions BC by reference graph
/// — no other BC's <c>.csproj</c> references <c>CritterBids.Auctions</c>. Declared
/// <c>public</c> for C# accessibility because the saga's <c>public Handle</c> takes it as
/// a parameter and Wolverine discovers public Handle methods; the BC-isolation semantics
/// hold via the modular-monolith project graph, not via the C# accessibility modifier.</para>
/// </summary>
public sealed record ProxyBidObserved(
    Guid SagaId,
    Guid ListingId,
    Guid BidId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    bool IsProxy,
    DateTimeOffset PlacedAt);
