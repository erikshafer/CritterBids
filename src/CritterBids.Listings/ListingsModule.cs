using JasperFx;
using JasperFx.Core;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;
using Wolverine.ErrorHandling;

namespace CritterBids.Listings;

public static class ListingsModule
{
    public static IServiceCollection AddListingsModule(this IServiceCollection services)
    {
        // Contribute Listings BC's document types to the primary IDocumentStore registered
        // in Program.cs (see ADR 009). The connection string is owned by Program.cs;
        // this module only declares what Listings owns within the shared store.
        services.ConfigureMarten(opts =>
        {
            // CatalogListingView documents live in the "listings" schema — isolated from
            // other BCs while sharing the same PostgreSQL database.
            opts.Schema.For<CatalogListingView>().DatabaseSchemaName("listings");
        });

        // Cross-queue read-model create race (M9-S7). See ListingsConcurrencyRetryPolicies.
        services.AddSingleton<IWolverineExtension, ListingsConcurrencyRetryPolicies>();

        return services;
    }
}

/// <summary>
/// Wolverine retry policy for the Listings BC's cross-queue read-model create race (M9-S7).
///
/// CatalogListingView is written by sibling handlers on three RabbitMQ queues
/// (listings-selling-events, listings-auctions-events, listings-settlement-events). When two of
/// them LoadAsync the same listing as null and both take the create path, both Insert; the
/// loser's commit throws <see cref="DocumentAlreadyExistsException"/>. The guard is the document
/// primary key, not numeric revisions: Marten's UseNumericRevisions only bumps the version on a
/// plain Store, it does not enforce — exactly the lesson the Auctions saga docstrings
/// (AuctionClosingSaga) record from M8-S3c, where enforcement comes from Wolverine's saga
/// persistence frame that a plain read-model handler does not have.
///
/// Retrying re-runs the losing handler, whose LoadAsync now returns the committed row, so it
/// takes the merge path (Store of the load-and-preserve upsert) and both writers' field sets
/// survive. Mirrors the AuctionsConcurrencyRetryPolicies shape: an IWolverineExtension
/// registered as a singleton, not inline in UseWolverine().
/// </summary>
internal sealed class ListingsConcurrencyRetryPolicies : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.OnException<DocumentAlreadyExistsException>()
            .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds());
    }
}
