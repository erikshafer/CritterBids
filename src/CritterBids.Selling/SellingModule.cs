using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Marten;

namespace CritterBids.Selling;

public static class SellingModule
{
    public static IServiceCollection AddSellingModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration["ConnectionStrings:critterbids-postgres"];

        if (string.IsNullOrEmpty(connectionString))
        {
            // No PostgreSQL connection string present — skip Selling BC registration.
            // This occurs in test fixtures (e.g., ParticipantsTestFixture) that start
            // AlbaHost.For<Program> without provisioning PostgreSQL. The Selling BC
            // test fixture provides the connection via ConfigureAppConfiguration before
            // Program.cs reads it, ensuring full registration in Selling-specific tests.
            // In production, Aspire always injects 'ConnectionStrings:critterbids-postgres'
            // so this branch is never taken outside of test environments.
            return services;
        }

        // Explicit StoreOptions type annotation required to resolve the correct
        // Action<StoreOptions> overload — WolverineFx.Marten also exposes a
        // Func<IServiceProvider, StoreOptions> overload that the compiler prefers
        // without the annotation.
        services.AddMartenStore<ISellingDocumentStore>((StoreOptions opts) =>
        {
            opts.Connection(connectionString);

            // Schema isolation — Selling BC owns exactly this schema; no other BC touches it.
            opts.DatabaseSchemaName = "selling";

            // NOTE: AutoApplyTransactions is a WolverineOptions policy (WolverineOptions.Policies),
            // not a Marten StoreOptions policy. The global opts.Policies.AutoApplyTransactions()
            // in Program.cs UseWolverine() applies to all BC handlers including Marten-backed ones.
            // IntegrateWithWolverine() below hooks this store's sessions into that pipeline.

            // RegisteredSeller is a projection document (not event-sourced) populated by
            // SellerRegistrationCompletedHandler. Registering it here ensures Marten creates
            // the underlying table during ApplyAllDatabaseChangesOnStartup() at startup.
            opts.Schema.For<RegisteredSeller>();

            // ⚠️ All Wolverine handlers in this BC must carry:
            //     [MartenStore(typeof(ISellingDocumentStore))]
            // Without this attribute, Wolverine does not route injected sessions to this
            // named store. Add it to every handler class in CritterBids.Selling.
            // S3 introduces the first handler — verify the attribute is present there.
        })
        // No identity map overhead for sessions — always chain this on named stores.
        .UseLightweightSessions()
        // Apply schema objects at startup so test fixtures have a fully-initialised schema.
        .ApplyAllDatabaseChangesOnStartup()
        // Transactional outbox + Wolverine transaction middleware for this named store.
        .IntegrateWithWolverine();

        // ISellerRegistrationService exposes seller registration state to the API layer.
        // Transient lifetime — the service holds no shared state; ISellingDocumentStore
        // is singleton-safe and a new IQuerySession is opened per call.
        services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

        // RabbitMQ routing rules live in Program.cs UseWolverine() — AddSellingModule()
        // receives only IServiceCollection and has no access to WolverineOptions.
        // Publish: selling-participants-events (added in S3 Program.cs)
        // Subscribe: selling-participants-events (added in S3 Program.cs)
        // Publish: listings-selling-events for ListingPublished (S5)

        return services;
    }
}
