namespace CritterBids.Auctions;

/// <summary>
/// Outcome of a bid decision over the DCB write path, returned by
/// <see cref="PlaceBidHandler.Execute"/>. It exists so the HTTP endpoint
/// (<see cref="PlaceBidEndpoint"/>) can learn accept-vs-reject + reason while the bus handler
/// (<see cref="PlaceBidHandler.HandleAsync"/>) keeps its <c>Task</c> (void) message-handler
/// signature — the bus path discards the outcome, the HTTP path maps it to a 2xx body or a 4xx
/// ProblemDetails.
///
/// <para><b>M8-S3a — the bid placement endpoint's result path.</b> Before this slice the DCB
/// handler returned <c>void</c> and folded every outcome into either the listing's acceptance
/// events or the <see cref="BidRejected"/> audit stream, leaving the HTTP caller blind. The
/// outcome makes the decision observable to the synchronous HTTP caller WITHOUT changing the
/// write: both the bus and HTTP paths go through the identical
/// <c>FetchForWritingByTags</c> + <c>AssertDcbConsistency</c> write and the same audit append,
/// so the optimistic-concurrency guarantee and the audit stream are preserved regardless of
/// caller. No new domain capability — only a return value over the existing decision.</para>
///
/// <para>The hierarchy is closed (private constructor + nested sealed records) so the endpoint's
/// switch is exhaustive over <see cref="Accepted"/> and <see cref="Rejected"/>.</para>
/// </summary>
public abstract record BidOutcome
{
    private BidOutcome() { }

    /// <summary>
    /// The bid was accepted and its events appended to the listing stream. Fields are shaped for
    /// the M8-S3b frontend's optimistic-update/rollback reconciliation (the contract S3b binds to).
    /// </summary>
    public sealed record Accepted(
        Guid ListingId,
        Guid BidId,
        Guid BidderId,
        decimal Amount,
        int BidCount,
        decimal CurrentHighBid,
        bool ReserveMet,
        ExtendedBiddingOutcome? ExtendedBidding) : BidOutcome;

    /// <summary>
    /// The bid was rejected. <see cref="Reason"/> is the machine-readable code the DCB decision
    /// produced (<c>BelowMinimumBid</c>, <c>ExceedsCreditCeiling</c>, <c>ListingClosed</c>,
    /// <c>ListingNotOpen</c>, <c>SellerCannotBid</c>); <see cref="CurrentHighBid"/> is carried so
    /// the frontend can reconcile its rolled-back optimistic update against the real high bid.
    /// The rejection has already been written to the <see cref="BidRejected"/> audit stream.
    /// </summary>
    public sealed record Rejected(
        string Reason,
        decimal CurrentHighBid) : BidOutcome;
}

/// <summary>
/// Extended-bidding extension outcome carried on an accepted bid when the bid landed in the
/// listing's trigger window and pushed the close out. Both shoulders come straight from the
/// emitted <c>ExtendedBiddingTriggered</c> event. Null on an accepted bid that did not trigger
/// an extension.
/// </summary>
public sealed record ExtendedBiddingOutcome(
    DateTimeOffset PreviousCloseAt,
    DateTimeOffset NewCloseAt);
