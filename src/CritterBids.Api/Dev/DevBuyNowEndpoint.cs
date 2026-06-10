using CritterBids.Auctions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Http;

namespace CritterBids.Api.Dev;

/// <summary>
/// DEV-ONLY manual-testing trigger for the bus-only <see cref="BuyNow"/> command (added at the
/// M8-S3c live verification — the first live observation of the BIN flow). BuyNow has no
/// application HTTP surface by design (M3-S4b; the buyer-side BIN UI is a future milestone), so
/// live smoke-tests drive it through the in-process bus the same way <see cref="DemoSeedEndpoint"/>
/// drives the bus-only seller/operator commands. <c>IsDevelopment()</c>-gated like the seed; the
/// outcome (BuyItNowPurchased vs BidRejected audit) is asynchronous — poll the read models.
/// </summary>
public static class DevBuyNowEndpoint
{
    public sealed record DevBuyNowRequest(Guid ListingId, Guid BuyerId, decimal? CreditCeiling);

    [WolverinePost("/api/dev/buy-now")]
    [AllowAnonymous]
    public static async Task<IResult> Post(
        DevBuyNowRequest request,
        IMessageBus bus,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (!env.IsDevelopment())
            return Results.NotFound();

        // 1000m mirrors the demo credit ceiling the seed-era sessions assign; the production
        // path sources the ceiling server-side (M8-S3a), which a dev trigger has no session for.
        await bus.InvokeAsync(new BuyNow(request.ListingId, request.BuyerId, request.CreditCeiling ?? 1000m), ct);
        return Results.Accepted();
    }
}
