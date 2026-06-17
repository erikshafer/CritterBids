using Marten;

namespace CritterBids.Listings;

/// <summary>
/// Shared write idiom for <see cref="CatalogListingView"/>, owned by the M9-S7 cross-queue-race
/// fix. The catalog view is written by sibling handlers on three RabbitMQ queues; under
/// concurrent delivery two of them can <c>LoadAsync</c> the same listing as null and both take
/// the create path.
/// </summary>
internal static class CatalogListingViewWrites
{
    /// <summary>
    /// Persist <paramref name="next"/> with cross-queue create-race safety.
    ///
    /// Create path (<paramref name="existing"/> is null): <c>Insert</c> so a concurrent creator on
    /// another queue collides with a <c>DocumentAlreadyExistsException</c> instead of silently
    /// overwriting. ListingsConcurrencyRetryPolicies re-runs the losing handler, which then
    /// re-loads the now-committed row and arrives here on the merge path.
    ///
    /// Merge path (<paramref name="existing"/> found): <c>Store</c> — the established
    /// load-and-preserve upsert is unchanged; after the winner committed, only one writer is
    /// updating the row at a time.
    /// </summary>
    public static void InsertOrStore(
        this IDocumentSession session,
        CatalogListingView? existing,
        CatalogListingView next)
    {
        if (existing is null)
            session.Insert(next);
        else
            session.Store(next);
    }
}
