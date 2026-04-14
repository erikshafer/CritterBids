using Alba;
using CritterBids.Selling;
using JasperFx.CommandLine;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polecat;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;

namespace CritterBids.Selling.Tests.Fixtures;

public class SellingTestFixture : IAsyncLifetime
{
    // postgres:17-alpine — current stable image. PostgreSqlBuilder follows the same
    // constructor-with-image-tag pattern as MsSqlBuilder in Testcontainers 4.x
    // (confirmed by CS0618 deprecation warning on the parameterless overload).
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"selling-postgres-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    // SQL Server 2025 required — Polecat 2.x uses the native `json` data type by default,
    // which requires SQL Server 2025+. Even though the Selling BC does not use Polecat
    // directly, Program.cs registers the Participants BC (Polecat) and Wolverine's durable
    // inbox/outbox (backed by SQL Server). The host cannot start without a valid SQL Server
    // connection, so the Selling fixture must provision one alongside PostgreSQL.
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-CU1-ubuntu-24.04")
        .WithPassword("CritterBids#Test2025!")
        .WithName($"selling-sqlserver-test-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    public IAlbaHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Start both infrastructure containers in parallel for faster fixture startup.
        await Task.WhenAll(_postgres.StartAsync(), _sqlServer.StartAsync());

        var postgresConnectionString = _postgres.GetConnectionString();
        var sqlServerConnectionString = _sqlServer.GetConnectionString();

        // Required for Wolverine to auto-start the host during test execution.
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            // ConfigureAppConfiguration runs before Program.cs reads IConfiguration.
            // AddSellingModule skips registration when the postgres connection string is
            // absent (to avoid breaking other test fixtures that don't provision PostgreSQL).
            // Providing the real Testcontainers connection string here ensures Program.cs
            // registers the Selling store fully before ConfigureServices runs.
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:critterbids-postgres"] = postgresConnectionString
                });
            });

            builder.ConfigureServices(services =>
            {
                // Override the Polecat (SQL Server) connection string with the Testcontainers one.
                // Program.cs uses ?? string.Empty for SQL Server, so ConfigurePolecat here
                // replaces the empty string with the real Testcontainers connection.
                services.ConfigurePolecat(opts =>
                {
                    opts.ConnectionString = sqlServerConnectionString;
                });

                // Re-register the named Marten store with the Testcontainers connection string.
                // Per ADR 0002, AddMartenStore<ISellingDocumentStore>() replaces the production
                // registration without affecting other BC named store registrations.
                // Explicit Marten.StoreOptions type annotation — same overload resolution fix as SellingModule.cs.
                // Fully qualified to disambiguate from Polecat.StoreOptions (both are in scope here).
                //
                // IMPORTANT: This override replaces the production store registration entirely,
                // including any opts.Schema.For<T>() document registrations. Any document types
                // needed by tests must be registered here too — without them, ApplyAllDatabaseChangesOnStartup()
                // will not create their tables and CleanAllMartenDataAsync<T>() will not clean them.
                services.AddMartenStore<ISellingDocumentStore>((Marten.StoreOptions opts) =>
                {
                    opts.Connection(postgresConnectionString);
                    opts.DatabaseSchemaName = "selling";

                    // RegisteredSeller must be registered here so its table is created at startup
                    // and cleaned between tests. The production AddSellingModule() registration is
                    // replaced by this ConfigureServices override, so document types must be repeated.
                    opts.Schema.For<CritterBids.Selling.RegisteredSeller>();
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine();

                // ISellerRegistrationService is normally registered in AddSellingModule(), but
                // AddSellingModule() returns early when 'ConnectionStrings:critterbids-postgres' is
                // absent at Program.cs execution time (ConfigureAppConfiguration runs later).
                // The Marten store override above replaces the store registration; this line
                // ensures the service seam is also available to tests.
                services.AddTransient<ISellerRegistrationService, SellerRegistrationService>();

                // Prevents Wolverine advisory lock contention when the test host restarts.
                // Required for named Marten stores which use distributed advisory locks.
                services.RunWolverineInSoloMode();

                // Suppress RabbitMQ and all other external Wolverine transports.
                // Wolverine's durable inbox/outbox (backed by PostgreSQL) remains active.
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

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _sqlServer.DisposeAsync().AsTask());
    }

    // ─── Cleanup helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Cleans all Marten documents and event data in the Selling BC's named store.
    /// Call in InitializeAsync() of each test class to ensure test isolation.
    /// </summary>
    /// <remarks>
    /// Must use the typed overload CleanAllMartenDataAsync&lt;T&gt;() because CritterBids
    /// uses named stores only — there is no default IDocumentStore in this process (ADR 0002).
    /// The non-generic Host.CleanAllMartenDataAsync() resolves IDocumentStore and throws.
    /// </remarks>
    public Task CleanAllMartenDataAsync() =>
        Host.CleanAllMartenDataAsync<ISellingDocumentStore>();

    /// <summary>
    /// Disables async projections, clears all Marten data, and restarts projection daemons
    /// for the Selling BC's named store. Use when async projections are registered.
    /// </summary>
    public Task ResetAllMartenDataAsync() =>
        Host.ResetAllMartenDataAsync<ISellingDocumentStore>();

    // ─── Query helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight Marten session for the Selling BC's named store.
    /// Use for seeding documents and asserting state in tests.
    /// Never resolve IDocumentSession directly — IDocumentStore is not registered
    /// (CritterBids uses named stores; see ADR 0002).
    /// </summary>
    // Fully qualified to disambiguate from Polecat.IDocumentSession — both Marten and Polecat
    // namespaces are in scope in this fixture (same issue as StoreOptions annotation in S2).
    public Marten.IDocumentSession GetDocumentSession() =>
        Host.Services.GetRequiredService<ISellingDocumentStore>().LightweightSession();

    // ─── Wolverine invocation helpers ────────────────────────────────────────

    /// <summary>
    /// Invokes <paramref name="message"/> through the Wolverine pipeline and waits for all
    /// side effects (handler execution, session commit, cascaded messages) to complete.
    /// Use instead of HTTP POST + GET to avoid event-sourcing race conditions.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.ExecuteAndWaitAsync(
            async ctx => await ctx.InvokeAsync(message),
            timeoutSeconds * 1000);
    }
}
