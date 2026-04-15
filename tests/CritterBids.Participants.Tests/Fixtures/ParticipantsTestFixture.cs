using Alba;
using CritterBids.Contracts;
using CritterBids.Participants;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;

namespace CritterBids.Participants.Tests.Fixtures;

public class ParticipantsTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"participants-postgres-test-{Guid.NewGuid():N}")
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

                // Register the Participants BC module so its event types (ParticipantSessionStarted,
                // SellerRegistered) are contributed to the primary Marten store above.
                // Program.cs guards this inside the postgres null check, which ConfigureServices bypasses.
                services.AddParticipantsModule();

                // Selling BC handlers inject ISellerRegistrationService (registered by AddSellingModule()).
                // AddSellingModule() is NOT called here — the Selling BC is not under test in this fixture.
                // Exclude Selling BC handlers from Wolverine discovery to prevent code-gen failures at startup.
                // The stub routing rule ensures tracked.Sent captures SellerRegistrationCompleted.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();
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

    // ─── Document store helpers ───────────────────────────────────────────────

    public Marten.IDocumentSession GetDocumentSession() =>
        Host.DocumentStore().LightweightSession();

    public IDocumentStore GetDocumentStore() =>
        Host.DocumentStore();

    // ─── Cleanup helpers ──────────────────────────────────────────────────────

    public Task CleanAllMartenDataAsync() => Host.CleanAllMartenDataAsync();

    // ─── Wolverine tracking helpers ───────────────────────────────────────────

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.ExecuteAndWaitAsync(
            async ctx => await ctx.InvokeAsync(message),
            timeoutSeconds * 1000);
    }

    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration)
    {
        IScenarioResult result = null!;
        var tracked = await Host.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });
        return (tracked, result);
    }
}

/// <summary>
/// Excludes Selling BC handler types from Wolverine discovery in the Participants fixture.
/// AddSellingModule() is not called here, so ISellerRegistrationService is not registered —
/// Selling BC handlers that inject it would cause code-gen failures at host startup.
/// The stub routing rule ensures tracked.Sent captures SellerRegistrationCompleted when
/// RegisterAsSeller publishes the integration event via OutgoingMessages.
/// </summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — AddSellingModule() not called in Participants fixture",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });

        options.PublishMessage<SellerRegistrationCompleted>()
            .ToLocalQueue("selling-participants-stub");
    }
}
