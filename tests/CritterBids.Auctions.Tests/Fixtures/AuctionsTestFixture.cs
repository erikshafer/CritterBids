using Alba;
using CritterBids.Auctions;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Auctions.Tests.Fixtures;

public class AuctionsTestFixture : IAsyncLifetime
{
    // Only PostgreSQL is needed for the Auctions BC — Marten is the only store registered
    // per ADR 011 (All-Marten Pivot). Program.cs's AddParticipantsModule() is registered in
    // the postgres-guarded block alongside the other modules, so no separate SQL Server
    // container is needed.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"auctions-postgres-test-{Guid.NewGuid():N}")
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
                // Register the primary Marten store with the Testcontainers connection string.
                // Program.cs's AddMarten() is null-guarded on the Aspire postgres connection
                // string, which is absent in tests. ConfigureServices runs after Program.cs, so
                // this registration is always present and wins for IDocumentStore resolution.
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
                .IntegrateWithWolverine();

                // Register the Auctions BC module so its ConfigureMarten contributions
                // (auctions schema for Listing, LiveStreamAggregation<Listing>) are present.
                // Program.cs guards its AddAuctionsModule() call inside the postgres null
                // check, which the ConfigureServices path bypasses.
                services.AddAuctionsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // Exclude Selling BC handlers — ISellerRegistrationService is not registered
                // in this fixture (AddSellingModule() is not called). Without exclusion,
                // Wolverine's handler discovery for CreateDraftListingHandler.ValidateAsync
                // would fail code-gen due to the unresolvable ISellerRegistrationService
                // dependency. See critter-stack-testing-patterns.md §Cross-BC Handler Isolation.
                // Listings and Participants handlers have no unresolvable dependencies in this
                // fixture and do not need exclusion.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
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

    // ─── Cleanup helpers ──────────────────────────────────────────────────────

    public Task CleanAllMartenDataAsync() => Host.CleanAllMartenDataAsync();
    public Task ResetAllMartenDataAsync() => Host.ResetAllMartenDataAsync();

    // ─── Query helpers ────────────────────────────────────────────────────────

    public Marten.IDocumentSession GetDocumentSession() =>
        Host.DocumentStore().LightweightSession();
}

/// <summary>
/// Excludes Selling BC handlers from Wolverine's handler discovery in the Auctions test fixture.
/// The Selling BC module is not registered here (no ISellerRegistrationService), so handlers
/// like CreateDraftListingHandler that depend on it cannot be code-generated.
/// </summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — ISellerRegistrationService not registered (no AddSellingModule in Auctions fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });
    }
}
