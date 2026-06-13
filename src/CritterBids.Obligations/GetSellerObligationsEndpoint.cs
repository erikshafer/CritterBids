using Marten;
using Microsoft.AspNetCore.Authorization;
using Wolverine.Http;

namespace CritterBids.Obligations;

public static class GetSellerObligationsEndpoint
{
    [WolverineGet("/api/obligations/status")]
    [AllowAnonymous]
    public static async Task<IReadOnlyList<ObligationStatusView>> Get(
        Guid sellerId,
        IQuerySession session,
        CancellationToken ct)
    {
        var obligations = await session.Query<ObligationStatusView>()
            .Where(x => x.SellerId == sellerId)
            .ToListAsync(ct);

        return obligations;
    }
}
