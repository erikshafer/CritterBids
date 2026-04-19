namespace CritterBids.Auctions;

/// <summary>
/// Strong-typed wrapper around the listing Guid used as the DCB tag type for
/// <see cref="BidConsistencyState"/>. Plain <c>Guid</c> can't be used directly because
/// .NET 10 added <c>Version</c> and <c>Variant</c> public properties to <see cref="Guid"/>,
/// and <c>JasperFx.Core.Reflection.ValueTypeInfo.ForType</c> requires a type to have
/// exactly one gettable public property — so registering <c>Guid</c> throws
/// <c>InvalidValueTypeException</c>.
///
/// Wrapping keeps the tag registration valid at startup. Events are tagged explicitly at
/// append time via <c>IEvent.AddTag(new ListingStreamId(listingId))</c>; Marten's
/// inference-by-property-type does NOT apply here because our contract events carry
/// <c>Guid ListingId</c>, not <c>ListingStreamId ListingTag</c>. Changing the contracts
/// for tag inference would leak a Marten implementation detail into every consumer of
/// <c>CritterBids.Contracts.Auctions.*</c>, so the handler does the tagging.
/// </summary>
public sealed record ListingStreamId(Guid Value);
