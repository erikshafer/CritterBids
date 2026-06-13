using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Selling;

/// <summary>
/// Seller-facing HTTP surface for updating a draft listing (M9-S2).
/// Thin gateway — cascades <see cref="UpdateDraftListing"/> to
/// <see cref="UpdateDraftListingHandler"/> and returns 202 Accepted.
/// Matches the <see cref="WithdrawListingEndpoint"/> precedent.
/// </summary>
public static class UpdateDraftListingEndpoint
{
    [WolverinePut("/api/selling/listings/draft")]
    [AllowAnonymous]
    public static (IResult, UpdateDraftListing) Put(UpdateDraftListing command)
        => (Results.Accepted($"/api/selling/listings/{command.ListingId}"), command);
}
