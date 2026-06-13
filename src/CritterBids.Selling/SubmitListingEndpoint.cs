using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Selling;

/// <summary>
/// Seller-facing HTTP surface for submitting a draft listing for publication (M9-S2).
/// Thin gateway — cascades <see cref="SubmitListing"/> to <see cref="SubmitListingHandler"/>
/// and returns 202 Accepted. Matches the <see cref="WithdrawListingEndpoint"/> precedent.
/// </summary>
public static class SubmitListingEndpoint
{
    [WolverinePost("/api/selling/listings/submit")]
    [AllowAnonymous]
    public static (IResult, SubmitListing) Post(SubmitListing command)
        => (Results.Accepted($"/api/selling/listings/{command.ListingId}"), command);
}
