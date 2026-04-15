namespace CritterBids.Selling;

/// <summary>Determines timing rules for a listing in the Selling BC.</summary>
/// <remarks>
/// Flash — no Duration; bidding window opens and closes based on session schedule.
/// Timed — Duration required; bidding closes after the specified interval from open.
/// </remarks>
public enum ListingFormat
{
    Flash,
    Timed
}
