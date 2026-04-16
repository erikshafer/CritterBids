using Marten;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}
