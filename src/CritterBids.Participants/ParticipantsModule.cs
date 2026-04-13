using JasperFx;
using JasperFx.Events;
using Microsoft.Extensions.DependencyInjection;
using Polecat;
using Wolverine.Polecat;

namespace CritterBids.Participants;

public static class ParticipantsModule
{
    public static IServiceCollection AddParticipantsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddPolecat(opts =>
        {
            opts.ConnectionString = connectionString;
            opts.DatabaseSchemaName = "participants";
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        })
        // Eagerly apply all schema changes at host startup so test fixtures
        // (and Testcontainers) have a fully-initialised schema before any
        // CleanAllPolecatDataAsync / FetchStreamAsync calls are made.
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

        return services;
    }
}
