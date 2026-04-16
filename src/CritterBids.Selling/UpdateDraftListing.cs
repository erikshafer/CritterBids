using Wolverine.Marten;

namespace CritterBids.Selling;

/// <summary>Command to update a listing that is still in <see cref="ListingStatus.Draft"/> state.</summary>
/// <remarks>
/// The HTTP endpoint is deferred to a later session — the command and handler are defined now so that
/// state-guard aggregate tests (scenarios 1.3–1.5) can run in S5 without the full submission flow.
/// Only non-null fields are applied to the aggregate.
/// </remarks>
public sealed record UpdateDraftListing(
    Guid ListingId,
    string? Title = null,
    decimal? ReservePrice = null,
    decimal? BuyItNowPrice = null);

/// <summary>Thrown when a state-transition guard is violated on a <see cref="SellerListing"/>.</summary>
public sealed class InvalidListingStateException : Exception
{
    public InvalidListingStateException(string message) : base(message) { }
}

/// <summary>Thrown when a <see cref="UpdateDraftListing"/> change violates a listing invariant.</summary>
public sealed class ListingValidationException : Exception
{
    public ListingValidationException(string message) : base(message) { }
}

/// <summary>
/// Wolverine handler for <see cref="UpdateDraftListing"/>.
/// Guards that the listing is in Draft state, validates the proposed change, then produces
/// a <see cref="DraftListingUpdated"/> event.
/// </summary>
public static class UpdateDraftListingHandler
{
    public static Events Handle(
        UpdateDraftListing cmd,
        [WriteAggregate(nameof(UpdateDraftListing.ListingId))] SellerListing listing)
    {
        if (listing.Status != ListingStatus.Draft)
            throw new InvalidListingStateException("Cannot update draft on non-draft listing");

        // Compute effective post-change values for invariant checks
        var effectiveReserve = cmd.ReservePrice ?? listing.ReservePrice;
        var effectiveBin = cmd.BuyItNowPrice ?? listing.BuyItNowPrice;

        if (effectiveBin.HasValue && effectiveReserve.HasValue
            && effectiveBin.Value < effectiveReserve.Value)
            throw new ListingValidationException("BuyItNowPrice must be >= ReservePrice");

        return [new DraftListingUpdated(listing.Id, cmd.Title, cmd.ReservePrice, cmd.BuyItNowPrice, DateTimeOffset.UtcNow)];
    }
}
