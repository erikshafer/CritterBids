namespace CritterBids.Auctions;

/// <summary>
/// UUID v5 namespace constants for deterministic identifier generation inside the Auctions BC.
///
/// Values are generated once and committed as hard-coded literals — changing any namespace
/// Guid would invalidate every existing deterministic identifier hashed against it.
///
/// This file is the Auctions-internal counterpart to
/// <see cref="CritterBids.Participants.ParticipantsConstants.ParticipantsNamespace"/>.
/// Saga-identifier and deterministic-stream-key helpers consume these constants; the
/// constants themselves carry no helper methods or logic.
/// </summary>
internal static class AuctionsIdentityNamespaces
{
    /// <summary>
    /// Namespace Guid for the Proxy Bid Manager saga's composite correlation key
    /// (<c>UuidV5(AuctionsIdentityNamespaces.ProxyBidManagerSaga, $"{ListingId}:{BidderId}")</c>).
    /// Pinned by decision <c>M4-D1</c> at M4-S1 (<c>docs/milestones/M4-auctions-bc-completion.md</c>
    /// §8), derived from the string form specified in Workshop 002 §4.1
    /// (<c>docs/workshops/002-scenarios.md</c> §4.1). The saga implementation and the
    /// UUID v5 helper that consumes this namespace are authored at M4-S3.
    /// </summary>
    public static readonly Guid ProxyBidManagerSaga = new Guid("abffa589-fb32-4b62-8ff7-ee1ca4f255ff");
}
