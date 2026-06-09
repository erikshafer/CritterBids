using JasperFx.Events.Tags;
using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Auctions;

/// <summary>
/// HTTP request for placing a bid (M8-S3a). Carries only what the browser legitimately knows:
/// the listing, the bidder (the <c>ParticipantId</c> Guid minted at session start), and the
/// amount. There is intentionally NO <c>CreditCeiling</c> field — the ceiling is owned by the
/// Participants BC and is sourced server-side from the Auctions-local
/// <see cref="ParticipantCreditCeiling"/> projection, so a client cannot supply an inflated
/// ceiling to bypass the credit check.
/// </summary>
public sealed record PlaceBidRequest(
    Guid ListingId,
    Guid BidderId,
    decimal Amount);

/// <summary>
/// Success body for an accepted bid (M8-S3a). The shape the M8-S3b frontend binds its
/// optimistic-update/rollback reconciliation to: the new high bid, the running bid count, the
/// cumulative reserve status, and the server-assigned <c>BidId</c>.
/// <see cref="ExtendedBidding"/> is null unless this bid pushed the close out.
/// </summary>
public sealed record PlaceBidResponse(
    Guid BidId,
    Guid ListingId,
    Guid BidderId,
    decimal Amount,
    int BidCount,
    decimal CurrentHighBid,
    bool ReserveMet,
    ExtendedBiddingOutcome? ExtendedBidding);

/// <summary>
/// The single sanctioned M8 backend exception (milestone §3): a thin <c>[AllowAnonymous]</c> HTTP
/// entry over the EXISTING internal <see cref="PlaceBid"/> DCB command. It adds no new domain
/// capability — the DCB consistency boundary, the rejection rules, and the bid-increment policy
/// all live in <see cref="PlaceBidHandler"/>. This endpoint:
///
/// <list type="number">
///   <item>Sources the bidder's credit ceiling server-side from the Auctions-local
///         <see cref="ParticipantCreditCeiling"/> projection (the same row
///         <see cref="StartProxyBidManagerSagaHandler"/> reads). The browser never supplies it.</item>
///   <item>Server-generates a UUID v7 <c>BidId</c> (ADR 007). A client idempotency key for
///         safe retry-on-dropped-response is deferred to M8-S3b, which owns the retry story.</item>
///   <item>Runs the decision + DCB write through the canonical Wolverine <c>[BoundaryModel]</c>
///         shape (M8-S3b Bug #2 fix): <see cref="Load"/> declares the consistency boundary as an
///         <c>EventTagQuery</c>; Wolverine fetches + projects <see cref="BidConsistencyState"/>,
///         injects the <see cref="IEventBoundary{T}"/>, and saves the outbox-enrolled session. The
///         decision + audit + acceptance events run in <see cref="PlaceBidHandler.DecideAndWrite"/>
///         (shared with the bus handler), appended via <c>boundary.AppendOne</c> so
///         <c>UseFastEventForwarding</c> routes the accepted events to their RabbitMQ destinations
///         (Listings / Relay / Operations) — the propagation the prior synchronous
///         <c>Execute</c>-over-injected-session path silently dropped.</item>
///   <item>Maps the outcome: acceptance → 200 + <see cref="PlaceBidResponse"/>; rejection →
///         a ProblemDetails 4xx with a machine-readable <c>reason</c> (400 for input-relative
///         rejections, 409 Conflict for listing-state rejections).</item>
/// </list>
///
/// <para><b>Auth posture (ADR-024).</b> <c>[AllowAnonymous]</c>, consistent with the anonymous
/// bidder app and the <c>BiddingHub</c> precedent — <c>BidderId</c> is a display/correlation
/// identifier, not a trust anchor. Per-user auth remains post-MVP.</para>
///
/// <para><b>Unknown bidder.</b> A missing <see cref="ParticipantCreditCeiling"/> row (the bidder
/// never started a session, or the projection has not caught up) is an HTTP precondition failure
/// OUTSIDE the five domain rejection reasons: the endpoint returns 404 <c>UnknownBidder</c>
/// WITHOUT running the DCB decision or writing a <see cref="BidRejected"/> audit entry — there is
/// no domain decision to audit when the bidder cannot be sourced. The boundary is still fetched by
/// the generated <see cref="Load"/> step, but nothing is appended, so the empty save is a no-op.</para>
/// </summary>
public static class PlaceBidEndpoint
{
    /// <summary>
    /// Declares the DCB consistency boundary for this command. Wolverine runs <c>Load</c> first
    /// (its <see cref="PlaceBidRequest"/> parameter is bound by name from the request body), fetches
    /// the events matching the returned <c>EventTagQuery</c>, projects them into
    /// <see cref="BidConsistencyState"/>, and injects the boundary into <see cref="Post"/>. Same
    /// query the bus handler uses (<see cref="PlaceBidHandler.BuildQuery"/>).
    /// </summary>
    public static EventTagQuery Load(PlaceBidRequest request) =>
        PlaceBidHandler.BuildQuery(request.ListingId);

    [WolverinePost("/api/auctions/bids")]
    [AllowAnonymous]
    public static async Task<IResult> Post(
        PlaceBidRequest request,
        [BoundaryModel] IEventBoundary<BidConsistencyState> boundary,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        // Server-side credit-ceiling sourcing: read the Auctions-local projection of the
        // Participants-owned ceiling. No cross-BC read, no client-supplied value.
        var ceiling = await session.LoadAsync<ParticipantCreditCeiling>(request.BidderId, cancellationToken);
        if (ceiling is null)
        {
            return Results.Problem(new ProblemDetails
            {
                Title = "Unknown bidder",
                Detail = "No credit ceiling is on file for this bidder; start a participant session first.",
                Status = StatusCodes.Status404NotFound,
                Extensions = { ["reason"] = "UnknownBidder" },
            });
        }

        var command = new PlaceBid(
            ListingId: request.ListingId,
            BidId: Guid.CreateVersion7(),
            BidderId: request.BidderId,
            Amount: request.Amount,
            CreditCeiling: ceiling.CreditCeiling);

        // Decide + write over the ALREADY-FETCHED boundary, appending accepted events through
        // boundary.AppendOne. The boundary's queued DCB consistency assertion fires on the generated
        // SaveChanges; the decision + audit + outcome shaping live in the shared DecideAndWrite.
        //
        // NOTE (M8-S3b Bug #2): accepted events reach the LOCAL AuctionClosingSaga (UseFastEvent
        // forwarding, in-process) but NOT the external RabbitMQ consumers (Listings / Relay /
        // Operations) — HTTP-endpoint-origin events do not get sent through Wolverine's outbox to
        // external transports in this app (six fixes attempted, all failed; see
        // docs/research/dcb-marten-blog-series-research.md §5.0/§5.1). Pending JasperFx escalation /
        // async-202; the read model + BiddingHub live-update remain affected.
        var state = boundary.Aggregate ?? new BidConsistencyState();
        var outcome = await PlaceBidHandler.DecideAndWrite(
            command, state, session,
            appendAccepted: wrapped => boundary.AppendOne(wrapped),
            now: time.GetUtcNow());

        return outcome switch
        {
            BidOutcome.Accepted a => Results.Ok(new PlaceBidResponse(
                BidId: a.BidId,
                ListingId: a.ListingId,
                BidderId: a.BidderId,
                Amount: a.Amount,
                BidCount: a.BidCount,
                CurrentHighBid: a.CurrentHighBid,
                ReserveMet: a.ReserveMet,
                ExtendedBidding: a.ExtendedBidding)),

            BidOutcome.Rejected r => Results.Problem(new ProblemDetails
            {
                Title = "Bid rejected",
                Detail = $"The bid was rejected: {r.Reason}.",
                Status = StatusForReason(r.Reason),
                Extensions =
                {
                    ["reason"] = r.Reason,
                    ["currentHighBid"] = r.CurrentHighBid,
                },
            }),

            _ => throw new InvalidOperationException($"Unhandled bid outcome: {outcome.GetType().Name}"),
        };
    }

    /// <summary>
    /// 409 Conflict for listing-state rejections ("the world moved under you" — the frontend
    /// refetches state and reconciles); 400 Bad Request for input-relative rejections (the bid
    /// itself was invalid). The reason strings are the codes <see cref="PlaceBidHandler"/> emits.
    /// </summary>
    private static int StatusForReason(string reason) => reason switch
    {
        "ListingClosed" or "ListingNotOpen" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest, // BelowMinimumBid, ExceedsCreditCeiling, SellerCannotBid
    };
}
