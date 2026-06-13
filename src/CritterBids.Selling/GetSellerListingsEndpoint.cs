using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace CritterBids.Selling;

/// <summary>
/// Seller-facing query endpoint for the "my listings" dashboard (M9-S2).
/// Queries the <see cref="SellerListingSummary"/> inline projection by seller ID.
/// Wolverine HTTP binds <c>sellerId</c> from the query string automatically for GET endpoints.
/// </summary>
public static class GetSellerListingsEndpoint
{
    [WolverineGet("/api/selling/listings")]
    [AllowAnonymous]
    public static async Task<IReadOnlyList<SellerListingSummary>> Get(
        Guid sellerId,
        IQuerySession session,
        CancellationToken ct)
    {
        var listings = await session.Query<SellerListingSummary>()
            .Where(x => x.SellerId == sellerId)
            .ToListAsync(ct);

        return listings;
    }
}
