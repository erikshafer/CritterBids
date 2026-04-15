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

            // SellerListing aggregate stream — registered so Marten knows the stream type and
            // can enforce UseMandatoryStreamTypeDeclaration (set in Program.cs). DraftListingCreated
            // is the first event in the stream; additional events arrive in S6+.
            opts.Events.AddEventType<DraftListingCreated>();
            opts.Events.AddEventType<DraftListingUpdated>();
        });

        services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

        return services;
    }
}
