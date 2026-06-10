using JasperFx;
using Microsoft.AspNetCore.Mvc;

namespace CritterBids.Api;

/// <summary>
/// Maps commit-time optimistic-concurrency failures on HTTP requests to a 409 Conflict
/// ProblemDetails instead of a 500 (the M8-S3a deferred item: "a genuine
/// <c>DcbConcurrencyException</c> on simultaneous bids surfaces as 5xx; graceful 409 left out").
///
/// <para><b>Why middleware and not a Wolverine retry policy.</b> The DCB consistency assertion
/// fires inside the generated <c>SaveChangesAsync</c> AFTER the endpoint method returns, so the
/// endpoint body cannot catch it. And as of Wolverine 6.5.1, HTTP chains do not consume Wolverine
/// failure rules at all — there are zero <c>Failures</c> references in <c>Wolverine.Http</c> or the
/// Marten HTTP frames, so a per-endpoint <c>Configure(HandlerChain)</c> retry (the shape the DCB
/// blog series shows) generates nothing on an HTTP chain. The message-bus path keeps its existing
/// retry policies (<c>AuctionsConcurrencyRetryPolicies</c>); the HTTP path gets this deterministic
/// 409 mapping, and the client treats it like any other "the world moved under you" conflict —
/// refetch and re-decide (the M8-S3b reconcile model already does this for 409s).</para>
///
/// <para><b>One catch covers both exception types.</b> <c>DcbConcurrencyException</c>
/// (JasperFx.Events) derives from <c>JasperFx.ConcurrencyException</c> as of JasperFx.Events 2.8.2
/// (verified by reflection against the pinned package), so catching the base type handles the DCB
/// consistency check, per-stream optimistic appends, and saga document revisions uniformly.</para>
/// </summary>
public sealed class ConcurrencyConflictMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConcurrencyConflictMiddleware> _logger;

    public ConcurrencyConflictMiddleware(RequestDelegate next, ILogger<ConcurrencyConflictMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ConcurrencyException ex)
        {
            // If the endpoint already started writing the response, the conflict surfaced after
            // headers went out — nothing safe to rewrite; let the server fault the connection.
            if (context.Response.HasStarted)
            {
                throw;
            }

            _logger.LogInformation(
                ex,
                "Optimistic concurrency conflict on {Method} {Path} mapped to 409",
                context.Request.Method,
                context.Request.Path);

            await Results.Problem(new ProblemDetails
            {
                Title = "Concurrency conflict",
                Detail = "Another request changed the same data first. Refresh and try again.",
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["reason"] = "ConcurrencyConflict" },
            }).ExecuteAsync(context);
        }
    }
}
