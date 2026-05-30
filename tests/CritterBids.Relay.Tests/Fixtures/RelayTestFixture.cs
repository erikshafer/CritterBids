using Alba;
using CritterBids.Relay;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Relay.Tests.Fixtures;

/// <summary>
/// Boots-clean fixture for the Relay BC. Mirrors the sibling BC fixtures: it boots the full
/// <c>Program</c> host through Alba with a Testcontainers PostgreSQL instance, registers the Marten
/// primary store (Program.cs's AddMarten is null-guarded on the absent Aspire connection string),
/// and registers <c>AddRelayModule()</c> so Relay's SignalR services are present.
///
/// Relay owns no Marten document in M6-S5, so this fixture exists chiefly to prove the host wires up
/// cleanly with Relay active: the hubs map, <c>IHubContext&lt;BiddingHub&gt;</c> /
/// <c>IHubContext&lt;OperationsHub&gt;</c> resolve, and the three notification handlers code-gen.
///
/// Every other BC's handlers are excluded from discovery — their modules aren't registered here, so
/// their aggregate / saga / read-model schema mappings and DI dependencies are absent. This is the
/// canonical cross-BC handler isolation pattern (see critter-stack-testing-patterns.md). Relay is
/// NOT excluded — it is the BC under test.
/// </summary>
public class RelayTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"relay-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(postgresConnectionString);
                    opts.DatabaseSchemaName = "public";
                    opts.Events.AppendMode = EventAppendMode.Quick;
                    opts.Events.UseMandatoryStreamTypeDeclaration = true;
                    opts.DisableNpgsqlLogging = true;
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine(configure: integration => integration.UseFastEventForwarding = true);

                // Relay's own module — registers AddSignalR() so IHubContext<BiddingHub> and the
                // unconditional app.MapHub<...>() calls resolve.
                services.AddRelayModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // ─── Cross-BC handler isolation ──────────────────────────────────────────────
                // Exclude every BC whose module is not registered here. Relay stays active.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new ObligationsBcDiscoveryExclusion());
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (Host != null)
        {
            try
            {
                await Host.StopAsync();
                await Host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }
}

/// <summary>Excludes Selling BC handlers — AddSellingModule is not registered in the Relay fixture.</summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x => x.Excludes.WithCondition(
            "Selling BC inactive — no AddSellingModule in Relay fixture",
            t => t.Namespace?.StartsWith("CritterBids.Selling") == true));
}

/// <summary>Excludes Auctions BC handlers — AddAuctionsModule is not registered in the Relay fixture.</summary>
internal sealed class AuctionsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x => x.Excludes.WithCondition(
            "Auctions BC inactive — no AddAuctionsModule in Relay fixture",
            t => t.Namespace?.StartsWith("CritterBids.Auctions") == true));
}

/// <summary>Excludes Listings BC handlers — AddListingsModule is not registered in the Relay fixture.</summary>
internal sealed class ListingsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x => x.Excludes.WithCondition(
            "Listings BC inactive — no AddListingsModule in Relay fixture",
            t => t.Namespace?.StartsWith("CritterBids.Listings") == true));
}

/// <summary>Excludes Settlement BC handlers — AddSettlementModule is not registered in the Relay fixture.</summary>
internal sealed class SettlementBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x => x.Excludes.WithCondition(
            "Settlement BC inactive — no AddSettlementModule in Relay fixture",
            t => t.Namespace?.StartsWith("CritterBids.Settlement") == true));
}

/// <summary>Excludes Obligations BC handlers — AddObligationsModule is not registered in the Relay fixture.</summary>
internal sealed class ObligationsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x => x.Excludes.WithCondition(
            "Obligations BC inactive — no AddObligationsModule in Relay fixture",
            t => t.Namespace?.StartsWith("CritterBids.Obligations") == true));
}

/// <summary>Excludes Participants BC handlers — AddParticipantsModule is not registered in the Relay fixture.</summary>
internal sealed class ParticipantsBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x => x.Excludes.WithCondition(
            "Participants BC inactive — no AddParticipantsModule in Relay fixture",
            t => t.Namespace?.StartsWith("CritterBids.Participants") == true));
}
