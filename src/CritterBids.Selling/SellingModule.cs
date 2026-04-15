using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Selling;

public static class SellingModule
{
    public static IServiceCollection AddSellingModule(this IServiceCollection services)
    {
        // Contribute Selling BC's document types to the primary IDocumentStore registered
        // in Program.cs (see ADR 009). The connection string is owned by Program.cs;
        // this module only declares what Selling owns within the shared store.
        services.ConfigureMarten(opts =>
        {
            opts.Schema.For<RegisteredSeller>().DatabaseSchemaName("selling");

            // Add event types, projections, and snapshots here as slices introduce them.
        });

        services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

        return services;
    }
}
