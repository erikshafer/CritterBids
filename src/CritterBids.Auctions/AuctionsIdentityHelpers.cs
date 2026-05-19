namespace CritterBids.Auctions;

/// <summary>
/// Deterministic identifier helpers for the Auctions BC. Sibling to
/// <see cref="AuctionsIdentityNamespaces"/>, which holds the namespace constants;
/// this file holds the functions that consume them.
///
/// <para>M4-S1 pinned the "pure constants" shape of <see cref="AuctionsIdentityNamespaces"/>
/// (no methods, no logic). M4-S3 introduces the first consumer — the composite-key helper
/// for the Proxy Bid Manager saga — and parks it here rather than expanding the constants
/// class's contract.</para>
/// </summary>
internal static class AuctionsIdentityHelpers
{
    /// <summary>
    /// Derives the deterministic Marten document id for a Proxy Bid Manager saga keyed by
    /// the <c>(ListingId, BidderId)</c> composite per M4-D1. The colon-delimited string form
    /// (<c>$"{ListingId}:{BidderId}"</c>) is verbatim from Workshop 002 §4.1; do not
    /// reformat or reorder — the UUID v5 SHA-1 hash is sensitive to the exact byte layout.
    /// </summary>
    public static Guid ProxyBidManagerSagaId(Guid listingId, Guid bidderId) =>
        UuidV5.Create(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{listingId}:{bidderId}");
}
