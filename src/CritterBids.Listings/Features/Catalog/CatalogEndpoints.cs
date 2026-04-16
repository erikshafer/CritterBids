using Marten;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Wolverine.Http;

namespace CritterBids.Listings.Features.Catalog;

/// <summary>
/// Wolverine.HTTP read endpoints for the Listings catalog.
/// Both endpoints carry [AllowAnonymous] — the project-wide M2–M5 stance.
/// Endpoint assembly discovery is handled by opts.Discovery.IncludeAssembly(...) in Program.cs.
/// </summary>
public static class CatalogEndpoints
{
    /// <summary>
    /// Returns all published listings ordered by PublishedAt descending.
    /// Returns an empty array when no listings exist — never 404 on empty.
    /// </summary>
    [AllowAnonymous]
    [WolverineGet("/api/listings")]
    public static async Task<IReadOnlyList<CatalogListingView>> GetCatalog(IQuerySession session)
    {
        return await session.Query<CatalogListingView>()
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Returns a single CatalogListingView by ListingId.
    /// Returns 404 when not found.
    /// </summary>
    [AllowAnonymous]
    [WolverineGet("/api/listings/{id}")]
    public static async Task<IResult> GetListingDetail(Guid id, IQuerySession session)
    {
        var view = await session.LoadAsync<CatalogListingView>(id);
        return view is null
            ? Results.NotFound()
            : Results.Ok(view);
    }
}
