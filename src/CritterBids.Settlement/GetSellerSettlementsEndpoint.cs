using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace CritterBids.Settlement;

public static class GetSellerSettlementsEndpoint
{
    [WolverineGet("/api/settlement/summaries")]
    [AllowAnonymous]
    public static async Task<IReadOnlyList<SellerSettlementSummary>> Get(
        Guid sellerId,
        IQuerySession session,
        CancellationToken ct)
    {
        var summaries = await session.Query<SellerSettlementSummary>()
            .Where(x => x.SellerId == sellerId)
            .ToListAsync(ct);

        return summaries;
    }
}
