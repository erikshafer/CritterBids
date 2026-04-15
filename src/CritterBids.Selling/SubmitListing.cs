using Wolverine;
using Wolverine.Marten;
using ContractListingPublished = CritterBids.Contracts.Selling.ListingPublished;

namespace CritterBids.Selling;

/// <summary>
/// Command to submit a draft (or previously rejected) listing for publication.
/// Handled by <see cref="SubmitListingHandler"/>.
/// No HTTP endpoint in M2 — tested as an aggregate handler only (scenario 2.1–2.4).
/// </summary>
public sealed record SubmitListing(Guid ListingId, Guid SellerId);

/// <summary>
/// Wolverine handler for <see cref="SubmitListing"/>.
/// Produces a 3-event atomic chain on the happy path:
/// <c>ListingSubmitted + ListingApproved + ListingPublished</c> (domain stream) and
/// publishes <c>CritterBids.Contracts.Selling.ListingPublished</c> to the Wolverine outbox
/// for RabbitMQ delivery.
/// On validation failure: <c>ListingSubmitted + ListingRejected</c> only, nothing outgoing.
/// </summary>
public static class SubmitListingHandler
{
    /// <summary>
    /// Handle a <see cref="SubmitListing"/> command against the loaded <see cref="SellerListing"/>.
    /// Returns <c>(Events, OutgoingMessages)</c> — tuple order is load-bearing for Wolverine dispatch.
    /// </summary>
    /// <exception cref="InvalidListingStateException">
    /// Thrown when the listing is not in <see cref="ListingStatus.Draft"/> or
    /// <see cref="ListingStatus.Rejected"/> state (scenario 2.4).
    /// </exception>
    public static (Events, OutgoingMessages) Handle(
        SubmitListing cmd,
        [WriteAggregate] SellerListing listing)
    {
        if (listing.Status != ListingStatus.Draft && listing.Status != ListingStatus.Rejected)
            throw new InvalidListingStateException(
                $"Cannot submit listing in {listing.Status} state. Only Draft or Rejected listings can be submitted.");

        var events = new Events();
        var outgoing = new OutgoingMessages();

        var now = DateTimeOffset.UtcNow;

        events.Add(new ListingSubmitted(listing.Id, listing.SellerId, now));

        var validation = ListingValidator.Validate(listing);

        if (validation.IsRejection)
        {
            events.Add(new ListingRejected(listing.Id, validation.Reason!, now));
        }
        else
        {
            events.Add(new ListingApproved(listing.Id, now));
            events.Add(new ListingPublished(listing.Id, now));

            outgoing.Add(new ContractListingPublished(
                listing.Id,
                listing.SellerId,
                listing.Title,
                listing.Format.ToString(),
                listing.StartingBid,
                listing.ReservePrice,
                listing.BuyItNowPrice,
                listing.Duration,
                listing.ExtendedBiddingEnabled,
                listing.ExtendedBiddingTriggerWindow,
                listing.ExtendedBiddingExtension,
                FeePercentage: 0.10m,  // M5 placeholder — no fee engine exists yet
                PublishedAt: now));
        }

        return (events, outgoing);
    }
}
