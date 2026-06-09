using CritterBids.Contracts.Auctions;
using JasperFx.Events.Tags;
using Marten;

namespace CritterBids.Auctions;

/// <summary>
/// Shared write path for opening a listing for bidding. Both production open-paths use it:
/// <see cref="SessionStartedHandler"/> (Flash, on session start) and
/// <see cref="ListingPublishedHandler"/> (Timed, on publish).
///
/// <para><b>Why this exists (bug fix).</b> The two handlers previously guarded idempotency with
/// <c>FetchStreamStateAsync(listingId)</c> ("skip if the stream exists") and wrote the
/// <see cref="BiddingOpened"/> via an <b>untagged</b> <c>StartStream&lt;Listing&gt;</c>. Both are
/// wrong in the integrated shared store (ADR 009):</para>
/// <list type="number">
///   <item>The <c>listingId</c> stream <b>always</b> exists by open time — Selling's
///     <c>SellerListing</c> aggregate started it with draft/submit/approve/publish events. So the
///     guard always tripped and <see cref="BiddingOpened"/> was never written.</item>
///   <item>The bid-acceptance DCB (<see cref="PlaceBidHandler"/>) reads <b>by tag</b>
///     (<see cref="ListingStreamId"/> over <see cref="BidConsistencyState"/>), not by stream. An
///     untagged <see cref="BiddingOpened"/> is invisible to the boundary, so every bid would reject
///     <c>ListingNotOpen</c> even if the event were written.</item>
/// </list>
///
/// <para>This helper mirrors <see cref="PlaceBidHandler"/> exactly: it reads the boundary via
/// <c>FetchForWritingByTags</c> (idempotency: skip if the listing is already open — the boundary's
/// <c>AssertDcbConsistency</c> also guards a concurrent open), then appends the
/// <see cref="BiddingOpened"/> <b>tagged</b> with <see cref="ListingStreamId"/> so the bid DCB sees
/// it. The append targets the <c>listingId</c> stream so the closing saga's
/// <c>AggregateStreamAsync&lt;Listing&gt;</c> SellerId lookup keeps working.</para>
/// </summary>
public static class OpenListingForBidding
{
    /// <summary>
    /// Append a tagged <see cref="BiddingOpened"/> for the listing unless it is already open
    /// (idempotent under at-least-once redelivery). The caller commits via the Wolverine
    /// <c>AutoApplyTransactions</c> pipeline.
    /// </summary>
    public static async Task AppendIfNotOpenAsync(
        IDocumentSession session,
        BiddingOpened opened,
        CancellationToken cancellationToken = default)
    {
        // Idempotency is "is bidding already OPEN?", read from the DCB tag boundary — NOT "does the
        // stream exist?" (the old guard's mistake: in the shared store the stream always exists
        // because Selling's SellerListing owns it). BidConsistencyState.ListingId is set by an applied
        // BiddingOpened, so a non-empty ListingId means a prior delivery already opened this listing.
        var boundary = await session.Events.FetchForWritingByTags<BidConsistencyState>(
            PlaceBidHandler.BuildQuery(opened.ListingId));
        if (boundary.Aggregate is { } state && state.ListingId != Guid.Empty)
            return;

        var tag = new ListingStreamId(opened.ListingId);

        // StartStream vs Append depends on whether the listingId stream already exists. In the
        // integrated host it does (Selling's draft/submit/publish events) → Append. In an isolated
        // Auctions store (tests, or a future split store) it does not → StartStream declares the
        // Listing aggregate type (required by UseMandatoryStreamTypeDeclaration). Either way the
        // BiddingOpened is tagged with ListingStreamId so the bid DCB can see it.
        var existing = await session.Events.FetchStreamStateAsync(opened.ListingId);
        if (existing is null)
        {
            var action = session.Events.StartStream<Listing>(opened.ListingId, opened);
            action.Events[0].AddTag(tag);
        }
        else
        {
            var wrapped = session.Events.BuildEvent(opened);
            wrapped.AddTag(tag);
            session.Events.Append(opened.ListingId, wrapped);
        }
    }
}
