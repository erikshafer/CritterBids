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

            // SellerListing aggregate stream — all event types registered so Marten can enforce
            // UseMandatoryStreamTypeDeclaration (set in Program.cs) and correctly reconstruct
            // aggregate state (missing Apply() for a registered event type causes silent null return).
            opts.Events.AddEventType<DraftListingCreated>();
            opts.Events.AddEventType<DraftListingUpdated>();
            opts.Events.AddEventType<ListingSubmitted>();
            opts.Events.AddEventType<ListingApproved>();
            opts.Events.AddEventType<ListingRejected>();
            opts.Events.AddEventType<ListingPublished>();
        });

        services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

        return services;
    }
}
