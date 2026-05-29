using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Obligations;

/// <summary>
/// In-process HTTP endpoint for opening a dispute against a sold listing's obligation (narrative
/// 008 — the winner raises a non-delivery dispute, or Operations escalates a missed deadline).
/// Cascades an <see cref="OpenDispute"/> command to the <see cref="PostSaleCoordinationSaga"/>,
/// which appends + emits <see cref="CritterBids.Contracts.Obligations.DisputeOpened"/> and
/// advances to <see cref="ObligationStatus.Disputed"/> without terminating.
///
/// <para><c>[AllowAnonymous]</c> holds through M6 — real authentication is deferred per the
/// project stance. The endpoint returns 202 Accepted: the dispute is recorded asynchronously by
/// the saga, so there is no resource body to return synchronously.</para>
/// </summary>
public static class OpenDisputeEndpoint
{
    [WolverinePost("/api/obligations/disputes")]
    [AllowAnonymous]
    public static (IResult, OpenDispute) Post(OpenDispute command)
        => (Results.Accepted($"/api/obligations/{command.ObligationId}"), command);
}
