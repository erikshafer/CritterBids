using Alba;
using CritterBids.Contracts;
using CritterBids.Participants;
using JasperFx.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Polecat;
using Testcontainers.MsSql;
using Wolverine;
using Wolverine.Polecat;
using Wolverine.Tracking;

namespace CritterBids.Participants.Tests.Fixtures;

public class ParticipantsTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04")
        .WithPassword("CritterBids#Test2025!")
        .WithName($"participants-sqlserver-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlServer.StartAsync();
        var connectionString = _sqlServer.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Register the Participants BC module directly with the Testcontainers connection.
                // Program.cs's AddParticipantsModule() is null-guarded on the sqlserver connection
                // string, which is absent in tests (no ConfigureAppConfiguration for sqlserver).
                // ConfigureServices runs after Program.cs, so this is the only Polecat registration —
                // no competing "main" Wolverine message store conflict.
                services.AddParticipantsModule(connectionString);

                // No PostgreSQL is provisioned here — Program.cs's AddMarten() and AddSellingModule()
                // guards both fail (no postgres connection). Selling BC handlers inject IDocumentSession
                // and cannot be code-generated without SessionVariableSource. Exclude them.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

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

        await _sqlServer.DisposeAsync();
    }

    // ─── Document store helpers ───────────────────────────────────────────────

    public IDocumentSession GetDocumentSession() =>
        Host.Services.DocumentStore().LightweightSession();

    public IDocumentStore GetDocumentStore() =>
        Host.Services.DocumentStore();

    // ─── Cleanup helpers ──────────────────────────────────────────────────────

    public Task CleanAllPolecatDataAsync() =>
        Host.Services.CleanAllPolecatDataAsync();

    // ─── Wolverine tracking helpers ───────────────────────────────────────────

    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.Services
            .TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host.Services)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx => await ctx.InvokeAsync(message)));
    }

    public async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(
        Action<Scenario> configuration)
    {
        IScenarioResult result = null!;
        var tracked = await Host.Services.ExecuteAndWaitAsync(async () =>
        {
            result = await Host.Scenario(configuration);
        });
        return (tracked, result);
    }
}

/// <summary>
/// Excludes Selling BC handler types from Wolverine discovery when Marten is not provisioned.
/// Program.cs's AddMarten() and AddSellingModule() are null-guarded on the postgres connection
/// string, which is absent in the Participants fixture. Without IDocumentStore, SessionVariableSource
/// is absent — handlers injecting IDocumentSession cause code-gen failures.
/// The stub routing rule ensures tracked.Sent captures SellerRegistrationCompleted.
/// </summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — Marten not configured (no postgres in Participants fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });

        options.PublishMessage<SellerRegistrationCompleted>()
            .ToLocalQueue("selling-participants-stub");
    }
}
