namespace CritterBids.Selling;

/// <summary>
/// Event-sourced aggregate representing a seller's listing in the Selling BC.
/// Marten instantiates this via the default constructor and replays Apply() methods
/// to rebuild state from the event stream.
/// </summary>
/// <remarks>
/// Stream IDs use UUID v7 (<c>Guid.CreateVersion7()</c>) at entity creation time (ADR 007, M2 §6).
/// Public property setters are required by Marten's default aggregate hydration strategy.
/// </remarks>
public class SellerListing
{
    /// <summary>Stream ID — set by <see cref="Apply(DraftListingCreated)"/>.</summary>
    public Guid Id { get; set; }

    public Guid SellerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public ListingFormat Format { get; set; }
    public decimal StartingBid { get; set; }
    public decimal? ReservePrice { get; set; }
    public decimal? BuyItNowPrice { get; set; }
    public TimeSpan? Duration { get; set; }
    public bool ExtendedBiddingEnabled { get; set; }
    public TimeSpan? ExtendedBiddingTriggerWindow { get; set; }
    public TimeSpan? ExtendedBiddingExtension { get; set; }
    public ListingStatus Status { get; set; }
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
        Duration = @event.Duration;
        ExtendedBiddingEnabled = @event.ExtendedBiddingEnabled;
        ExtendedBiddingTriggerWindow = @event.ExtendedBiddingTriggerWindow;
        ExtendedBiddingExtension = @event.ExtendedBiddingExtension;
        Status = ListingStatus.Draft;
    }

    public void Apply(DraftListingUpdated @event)
    {
        if (@event.Title is not null) Title = @event.Title;
        if (@event.ReservePrice is not null) ReservePrice = @event.ReservePrice;
        if (@event.BuyItNowPrice is not null) BuyItNowPrice = @event.BuyItNowPrice;
    }

    public void Apply(ListingSubmitted @event) =>
        Status = ListingStatus.Submitted;

    public void Apply(ListingApproved @event) =>
        Status = ListingStatus.Published;

    public void Apply(ListingRejected @event) =>
        Status = ListingStatus.Rejected;

    public void Apply(ListingPublished @event)
    {
        Status = ListingStatus.Published;
        PublishedAt = @event.PublishedAt;
    }
}
