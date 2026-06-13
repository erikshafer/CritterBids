namespace CritterBids.Selling;

/// <summary>
/// Inline snapshot projection of the <see cref="SellerListing"/> event stream.
/// Stored as a Marten document in the "selling" schema, queryable by <see cref="SellerId"/>
/// for the seller's "my listings" dashboard.
/// Registered via <c>Projections.Snapshot&lt;SellerListingSummary&gt;(SnapshotLifecycle.Inline)</c>
/// in <see cref="SellingModule"/>.
/// </summary>
public class SellerListingSummary
{
    public Guid Id { get; set; }
    public Guid SellerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ListingFormat Format { get; set; }
    public ListingStatus Status { get; set; }
    public decimal StartingBid { get; set; }
    public decimal? ReservePrice { get; set; }
    public decimal? BuyItNowPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public void Apply(DraftListingCreated @event)
    {
        Id = @event.ListingId;
        SellerId = @event.SellerId;
        Title = @event.Title;
        Format = @event.Format;
        StartingBid = @event.StartingBid;
        ReservePrice = @event.ReservePrice;
        BuyItNowPrice = @event.BuyItNowPrice;
        Status = ListingStatus.Draft;
        CreatedAt = @event.CreatedAt;
    }

    public void Apply(DraftListingUpdated @event)
    {
        if (@event.Title is not null) Title = @event.Title;
        if (@event.ReservePrice is not null) ReservePrice = @event.ReservePrice;
        if (@event.BuyItNowPrice is not null) BuyItNowPrice = @event.BuyItNowPrice;
    }

    public void Apply(ListingSubmitted _) =>
        Status = ListingStatus.Submitted;

    public void Apply(ListingApproved _) =>
        Status = ListingStatus.Published;

    public void Apply(ListingPublished @event)
    {
        Status = ListingStatus.Published;
        PublishedAt = @event.PublishedAt;
    }

    public void Apply(ListingRejected _) =>
        Status = ListingStatus.Rejected;

    public void Apply(ListingWithdrawn _) =>
        Status = ListingStatus.Withdrawn;
}
