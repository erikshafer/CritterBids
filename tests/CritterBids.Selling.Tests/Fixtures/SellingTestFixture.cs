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
                services.AddMartenStore<ISellingDocumentStore>((Marten.StoreOptions opts) =>
                {
                    opts.Connection(postgresConnectionString);
                    opts.DatabaseSchemaName = "selling";
                })
                .UseLightweightSessions()
                .ApplyAllDatabaseChangesOnStartup()
                .IntegrateWithWolverine();

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
    /// Cleans all Marten documents and event data in the Selling BC store atomically.
    /// Call in InitializeAsync() of each test class to ensure test isolation.
    /// </summary>
    public Task CleanAllMartenDataAsync() =>
        Host.CleanAllMartenDataAsync();

    /// <summary>
    /// Disables async projections, clears all Marten data, and restarts projection daemons.
    /// Use for async projection tests. Not required in S2 (no projections yet).
    /// </summary>
    public Task ResetAllMartenDataAsync() =>
        Host.ResetAllMartenDataAsync();
}
