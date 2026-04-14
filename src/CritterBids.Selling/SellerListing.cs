namespace CritterBids.Selling;

/// <summary>
/// Event-sourced aggregate representing a seller's listing in the Selling BC.
/// Marten instantiates this via the default constructor and populates Id from the event stream.
/// </summary>
/// <remarks>
/// Stream IDs use UUID v7 (<c>Guid.CreateVersion7()</c>) at entity creation time per ADR 0002.
/// Unlike Polecat BCs, Marten BC stream IDs have no cross-handler coordination requirement,
/// so no namespace constant is needed here.
/// Apply() methods and DraftListingCreated arrive in S4.
/// </remarks>
public class SellerListing
{
    /// <summary>Stream ID, populated by Marten from the event stream.</summary>
    public Guid Id { get; set; }
}
