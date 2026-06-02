using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Obligations;

/// <summary>
/// In-process HTTP endpoint for resolving an open dispute (narrative 008 — the operator grants an
/// extension, or refunds/closes). Cascades a <see cref="ResolveDispute"/> command to the
/// <see cref="PostSaleCoordinationSaga"/>, which appends + emits
/// <see cref="CritterBids.Contracts.Obligations.DisputeResolved"/>. <c>Refund</c> and <c>Closed</c>
/// terminate the saga; <c>Extension</c> reschedules a fresh ship-by deadline and continues.
///
/// <para>Dispute resolution is a staff action, so the endpoint is gated by the <c>StaffOnly</c>
/// policy (M7-S6, ADR-024 — superseding the former <c>[AllowAnonymous]</c>-through-M6 stance). The
/// literal policy string is used because the Obligations BC cannot reference the host where the
/// policy-name constant lives. The endpoint returns 202 Accepted: the resolution is applied
/// asynchronously by the saga, so there is no resource body to return synchronously.</para>
/// </summary>
public static class ResolveDisputeEndpoint
{
    [WolverinePost("/api/obligations/disputes/resolve")]
    [Authorize(Policy = "StaffOnly")]
    public static (IResult, ResolveDispute) Post(ResolveDispute command)
        => (Results.Accepted($"/api/obligations/{command.ObligationId}"), command);
}
