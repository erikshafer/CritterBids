using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritterBids.Selling;

/// <summary>
/// Command to create a new draft listing in the Selling BC.
/// Dispatched by <c>POST /api/listings/draft</c>.
/// </summary>
public sealed record CreateDraftListing(
    Guid SellerId,
    string Title,
    ListingFormat Format,
    decimal StartingBid,
    decimal? ReservePrice,
    decimal? BuyItNowPrice,
    TimeSpan? Duration,
    bool ExtendedBiddingEnabled,
    TimeSpan? ExtendedBiddingTriggerWindow,
    TimeSpan? ExtendedBiddingExtension);

/// <summary>
/// Thrown when a <see cref="CreateDraftListing"/> command arrives for a seller who has not
/// completed registration. Wolverine will retry — once the <c>RegisteredSellers</c> projection
/// catches up, the retry will succeed (scenario 1.2 / M2 §6 race-condition note).
/// </summary>
public sealed class SellerNotRegisteredException : Exception
{
    public Guid SellerId { get; }

    public SellerNotRegisteredException(Guid sellerId)
        : base($"Seller {sellerId} is not registered.")
    {
        SellerId = sellerId;
    }
}

/// <summary>
/// Wolverine compound handler for <see cref="CreateDraftListing"/>.
/// <list type="bullet">
///   <item><term><c>ValidateAsync</c></term><description>
///     Gates on <see cref="ISellerRegistrationService"/>; returns HTTP 403 if not registered.
///   </description></item>
///   <item><term><c>Handle</c></term><description>
///     Happy path — creates the <see cref="SellerListing"/> stream via
///     <see cref="MartenOps.StartStream{T}"/> and returns HTTP 201.
///   </description></item>
/// </list>
/// </summary>
public static class CreateDraftListingHandler
{
    /// <summary>
    /// Pre-handler validation. Returns <c>ProblemDetails { Status = 403 }</c> when the seller is
    /// not in the <c>RegisteredSellers</c> projection; returns <see cref="WolverineContinue.NoProblems"/>
    /// to allow <see cref="Handle"/> to proceed.
    /// </summary>
    public static async Task<ProblemDetails> ValidateAsync(
        CreateDraftListing cmd,
        ISellerRegistrationService registrationService,
        CancellationToken ct)
    {
        if (!await registrationService.IsRegisteredAsync(cmd.SellerId, ct))
            return new ProblemDetails { Detail = "Seller is not registered", Status = 403 };

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Happy-path handler. Stream ID is a UUID v7 generated at creation time (M2 §6 / ADR 007).
    /// Returns HTTP 201 with <c>Location: /api/listings/{listingId}</c>.
    /// </summary>
    [WolverinePost("/api/listings/draft")]
    [AllowAnonymous]
    public static (CreationResponse<Guid>, IStartStream) Handle(CreateDraftListing cmd)
    {
        var listingId = Guid.CreateVersion7();

        var evt = new DraftListingCreated(
            listingId,
            cmd.SellerId,
            cmd.Title,
            cmd.Format,
            cmd.StartingBid,
            cmd.ReservePrice,
            cmd.BuyItNowPrice,
            cmd.Duration,
            cmd.ExtendedBiddingEnabled,
            cmd.ExtendedBiddingTriggerWindow,
            cmd.ExtendedBiddingExtension,
            DateTimeOffset.UtcNow);

        // MartenOps.StartStream is the correct pattern — direct session.Events.StartStream()
        // silently discards events (anti-pattern #9 in wolverine-message-handlers.md).
        var stream = MartenOps.StartStream<SellerListing>(listingId, evt);

        // HTTP response type must be first in the tuple (anti-pattern #3).
        return (new CreationResponse<Guid>($"/api/listings/{listingId}", listingId), stream);
    }
}
