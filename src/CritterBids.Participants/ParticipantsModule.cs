using JasperFx;
using JasperFx.Events;
using Microsoft.Extensions.DependencyInjection;
using Polecat;

namespace CritterBids.Participants;

public static class ParticipantsModule
{
    // connectionString is accepted for API consistency with other AddXyzModule() extensions
    // and future named-store refactoring. With the ConfigurePolecat() pattern, the connection
    // is already established by the host-level AddPolecat() call in Program.cs.
    public static IServiceCollection AddParticipantsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.ConfigurePolecat(opts =>
        {
            opts.DatabaseSchemaName = "participants";
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        });

        return services;
    }
}
