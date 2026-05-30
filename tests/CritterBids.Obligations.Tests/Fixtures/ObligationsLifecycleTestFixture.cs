using Alba;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Obligations.Tests.Fixtures;

/// <summary>
/// Variant of <see cref="ObligationsTestFixture"/> that flips the Obligations BC into
/// <c>DemoMode</c> with collapsed (minute-scale) timer durations injected through the same
/// <see cref="ObligationsOptions"/> configuration path the conference demo uses. This proves the
/// demo-duration config seam (W001-6) works end-to-end and exercises the saga reading
/// <c>AutoConfirmWindow</c> when scheduling <c>ConfirmDelivery</c>.
///
/// <para>The durations are minutes (reminder 2m / ship-by 5m / auto-confirm 3m) — long enough that
/// no scheduled timer fires on its own during a test run, so the M6-S3 lifecycle test drives every
/// transition deterministically via direct <c>InvokeMessageAndWaitAsync</c> (the
/// AuctionClosingSagaTests precedent) rather than waiting on the clock.</para>
/// </summary>
public class ObligationsLifecycleTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"obligations-lifecycle-postgres-test-{Guid.NewGuid():N}")
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
            // Demo-mode durations injected via configuration — the same binding path the demo host
            // uses (Obligations:DemoMode + Obligations:Demo:*). Minute-scale so nothing fires
            // during the test; transitions are driven manually.
            builder.UseSetting("Obligations:DemoMode", "true");
            builder.UseSetting("Obligations:Demo:ReminderOffset", "00:02:00");
            builder.UseSetting("Obligations:Demo:ShipByDeadline", "00:05:00");
            builder.UseSetting("Obligations:Demo:AutoConfirmWindow", "00:03:00");

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

                services.AddObligationsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // Cross-BC handler isolation — reuses the exclusion extensions defined alongside
                // ObligationsTestFixture (same namespace). See that fixture for the rationale.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new AuctionsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new ListingsBcDiscoveryExclusion());
                services.AddSingleton<IWolverineExtension>(new SettlementBcDiscoveryExclusion());

                // Exclude Relay BC handlers — see RelayBcDiscoveryExclusion (defined in
                // ObligationsTestFixture.cs). Keeps Relay's push handler from co-consuming the
                // SettlementCompleted this lifecycle fixture drives.
                services.AddSingleton<IWolverineExtension>(new RelayBcDiscoveryExclusion());
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

    public Task CleanAllMartenDataAsync() => Host.CleanAllMartenDataAsync();

    public Marten.IDocumentSession GetDocumentSession() =>
        Host.DocumentStore().LightweightSession();
}
