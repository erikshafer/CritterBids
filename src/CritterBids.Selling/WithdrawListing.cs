using Wolverine;
using Wolverine.Marten;
using ContractListingWithdrawn = CritterBids.Contracts.Selling.ListingWithdrawn;

namespace CritterBids.Selling;

/// <summary>
/// Command to withdraw a published listing before its scheduled close.
/// Handled by <see cref="WithdrawListingHandler"/>.
/// </summary>
/// <remarks>
/// M4 scope is seller-initiated withdrawal only — <c>WithdrawnBy</c> is the seller's
/// participant identifier. Ops-staff-initiated withdrawal (abuse, fraud) is post-M4 and
/// will add a parallel producer command; the contract event is already shaped for it.
/// No HTTP endpoint in M4 — tested through <c>IMessageBus</c> dispatch only per the
/// M5-through-M6 backend-only posture in CLAUDE.md.
/// </remarks>
public sealed record WithdrawListing(Guid ListingId, Guid WithdrawnBy);

/// <summary>
/// Wolverine handler for <see cref="WithdrawListing"/>.
/// Happy path emits the Selling-internal <see cref="ListingWithdrawn"/> domain event onto
/// the <see cref="SellerListing"/> stream and a <c>CritterBids.Contracts.Selling.ListingWithdrawn</c>
/// onto <see cref="OutgoingMessages"/> for the Wolverine outbox to fan out via RabbitMQ.
/// Replaces the M3-S5b test-fixture synthesis of <see cref="ContractListingWithdrawn"/>
/// as the production producer of the integration event.
/// </summary>
public static class WithdrawListingHandler
{
    /// <summary>
    /// Handle a <see cref="WithdrawListing"/> command against the loaded <see cref="SellerListing"/>.
    /// Returns <c>(Events, OutgoingMessages)</c> — tuple order is load-bearing for Wolverine dispatch.
    /// </summary>
    /// <exception cref="InvalidListingStateException">
    /// Thrown when the listing is not in <see cref="ListingStatus.Published"/> state. The Selling
    /// lifecycle ends at <c>Published</c> (or <c>Withdrawn</c> after this handler); closure /
    /// sale / pass are Auctions-side concepts that Selling does not track, so "reject-already-closed"
    /// in this context means "reject if already withdrawn".
    /// </exception>
    public static (Events, OutgoingMessages) Handle(
        WithdrawListing cmd,
        [WriteAggregate(nameof(WithdrawListing.ListingId))] SellerListing listing)
    {
        if (listing.Status != ListingStatus.Published)
            throw new InvalidListingStateException(
                $"Cannot withdraw listing in {listing.Status} state. Only Published listings can be withdrawn.");

        var now = DateTimeOffset.UtcNow;

        var events = new Events { new ListingWithdrawn(listing.Id, now) };
        var outgoing = new OutgoingMessages
        {
            new ContractListingWithdrawn(
                ListingId: listing.Id,
                WithdrawnBy: cmd.WithdrawnBy,
                Reason: null,
                WithdrawnAt: now),
        };

        return (events, outgoing);
    }
}
