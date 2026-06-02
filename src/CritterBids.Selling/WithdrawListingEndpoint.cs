using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Selling;

/// <summary>
/// Staff-facing HTTP surface for withdrawing a published listing (M7-S6, ADR-024). M4 wired
/// <see cref="WithdrawListing"/> for <c>IMessageBus</c> dispatch only; this slice gives it a gated
/// HTTP entry point without touching <see cref="WithdrawListingHandler"/> — the handler stays a
/// message handler (attaching an HTTP attribute to it would deregister it and break the M4 dispatch
/// tests). This thin endpoint cascades the command and returns 202; the
/// <see cref="WithdrawListingHandler"/> applies it asynchronously.
///
/// <para>Withdrawal is a staff action, so the endpoint is gated by the <c>StaffOnly</c> policy. The
/// literal policy string is used because the Selling BC cannot reference the host where the
/// policy-name constant lives (ADR-024).</para>
/// </summary>
public static class WithdrawListingEndpoint
{
    [WolverinePost("/api/selling/listings/withdraw")]
    [Authorize(Policy = "StaffOnly")]
    public static (IResult, WithdrawListing) Post(WithdrawListing command)
        => (Results.Accepted($"/api/selling/listings/{command.ListingId}"), command);
}
