namespace CritterBids.Auctions;

/// <summary>
/// Strong-typed tag identifier for the Listing event stream in DCB queries. Wraps a
/// Guid so that .NET 10's introduction of Variant/Version as public instance properties
/// on Guid doesn't trip Marten's ValueTypeInfo validation (which requires exactly one
/// public instance property on the wrapping record).
///
/// Registered in AuctionsModule via
/// <c>opts.Events.RegisterTagType&lt;ListingStreamId&gt;("listing").ForAggregate&lt;BidConsistencyState&gt;()</c>.
/// </summary>
public sealed record ListingStreamId(Guid Value);
