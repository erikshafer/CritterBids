using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Obligations;

/// <summary>
/// Seller-facing in-process HTTP endpoint for providing shipping tracking on a sold listing's
/// obligation (narrative 006 Moment 3 — "GreyOwl12 enters the tracking number"). Cascades a
/// <see cref="ProvideTracking"/> command to the <see cref="PostSaleCoordinationSaga"/>, which
/// cancels the pending reminder/escalation timers, records
/// <see cref="CritterBids.Contracts.Obligations.TrackingInfoProvided"/>, and schedules the
/// auto-confirm timer.
///
/// <para><c>[AllowAnonymous]</c> holds through M6 — real authentication is deferred per the
/// project stance. The endpoint returns 202 Accepted: tracking is recorded asynchronously by the
/// saga, so there is no resource body to return synchronously.</para>
/// </summary>
public static class ProvideTrackingEndpoint
{
    [WolverinePost("/api/obligations/tracking")]
    [AllowAnonymous]
    public static (IResult, ProvideTracking) Post(ProvideTracking command)
        => (Results.Accepted($"/api/obligations/{command.ObligationId}"), command);
}
