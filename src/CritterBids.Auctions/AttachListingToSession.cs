using CritterBids.Contracts.Auctions;
using JasperFx.Events;
using Marten;
using Wolverine.Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Command to attach a published listing to a (not-yet-started) Session aggregate.
/// Handled by <see cref="AttachListingToSessionHandler"/>.
/// </summary>
public sealed record AttachListingToSession(Guid SessionId, Guid ListingId);

/// <summary>
/// Wolverine handler for <see cref="AttachListingToSession"/>. First in-Auctions
/// handler combining <c>[WriteAggregate]</c> with a separate cross-projection lookup
/// (<see cref="PublishedListings"/>).
///
/// <para><b>Rejection invariants.</b></para>
/// <list type="bullet">
///   <item>Workshop 002 §5.3 — listing not in <c>Published</c> status:
///     <see cref="ListingNotPublishedException"/>. The check consults the
///     <see cref="PublishedListings"/> projection (M4-D4 resolution; field shape OQ1 Path
///     A — the projection carries the published/withdrawn transition and the full
///     BiddingOpened-precursor payload).</item>
///   <item>Workshop 002 §5.4 — session already started:
///     <see cref="SessionAlreadyStartedException"/>.</item>
/// </list>
///
/// <para><b>Idempotency / re-attach.</b> The handler does not currently guard against
/// re-attaching the same listing twice — Workshop 002 §5 does not define this case.
/// A second <c>AttachListingToSession</c> with the same ListingId on the same session
/// would emit a second <see cref="Contracts.Auctions.ListingAttachedToSession"/>; the
/// aggregate's <see cref="Session.Apply(Contracts.Auctions.ListingAttachedToSession)"/>
/// would append the listing id a second time. Future hardening if it surfaces.</para>
/// </summary>
public static class AttachListingToSessionHandler
{
    public static async Task<Events> Handle(
        AttachListingToSession cmd,
        [WriteAggregate(nameof(AttachListingToSession.SessionId))] Session session,
        IDocumentSession documentSession,
        CancellationToken cancellationToken)
    {
        if (session.StartedAt is not null)
            throw new SessionAlreadyStartedException(cmd.SessionId);

        var published = await documentSession.LoadAsync<PublishedListings>(
            cmd.ListingId, cancellationToken);

        if (published is null || published.Status == PublishedListingsStatus.Withdrawn)
            throw new ListingNotPublishedException(cmd.ListingId);

        return new Events
        {
            new ListingAttachedToSession(
                SessionId: cmd.SessionId,
                ListingId: cmd.ListingId,
                AttachedAt: DateTimeOffset.UtcNow),
        };
    }
}

/// <summary>Thrown when <see cref="AttachListingToSession"/> arrives for a listing that
/// is not in <see cref="PublishedListingsStatus.Published"/> (Workshop 002 §5.3).</summary>
public sealed class ListingNotPublishedException : Exception
{
    public Guid ListingId { get; }

    public ListingNotPublishedException(Guid listingId)
        : base($"Listing {listingId} is not in Published status; cannot be attached to a session.")
    {
        ListingId = listingId;
    }
}

/// <summary>Thrown when <see cref="AttachListingToSession"/> or <see cref="StartSession"/>
/// arrives for a session whose <see cref="Session.StartedAt"/> is already non-null
/// (Workshop 002 §5.4 / §5.7).</summary>
public sealed class SessionAlreadyStartedException : Exception
{
    public Guid SessionId { get; }

    public SessionAlreadyStartedException(Guid sessionId)
        : base($"Session {sessionId} has already started; cannot be modified.")
    {
        SessionId = sessionId;
    }
}
