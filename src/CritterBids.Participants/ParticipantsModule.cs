using CritterBids.Contracts.Participants;
using CritterBids.Participants.Features.RegisterAsSeller;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Participants;

public static class ParticipantsModule
{
    public static IServiceCollection AddParticipantsModule(this IServiceCollection services)
    {
        // Contribute Participants event types to the primary Marten store registered in Program.cs.
        // UseMandatoryStreamTypeDeclaration = true (set globally) requires all event types to be
        // explicitly registered — unregistered types cause a runtime error on stream append.
        services.ConfigureMarten(opts =>
        {
            opts.Events.AddEventType<ParticipantSessionStarted>();
            opts.Events.AddEventType<SellerRegistered>();
        });

        return services;
    }
}
