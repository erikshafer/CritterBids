using Alba;
using CritterBids.Contracts;
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
    // SQL Server 2025 image required — Polecat 2.x uses the native `json` data type by default,
    // which requires SQL Server 2025+. See polecat-event-sourcing.md §SQL Server-Specific Gotchas.
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

        // Required for Wolverine to auto-start the host during test execution.
        JasperFxEnvironment.AutoStartHost = true;

        Host = await AlbaHost.For<Program>(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Override the Polecat connection string with the test container's.
                // ConfigurePolecat adds to the IOptions<PolecatOptions> chain, correctly
                // overriding the connection string that AddParticipantsModule registered
                // while preserving DatabaseSchemaName, AutoCreateSchemaObjects, etc.
                services.ConfigurePolecat(opts =>
                {
                    opts.ConnectionString = connectionString;
                });

                // When the Selling BC is not provisioned (no postgres in this fixture),
                // AddSellingModule() returns early and ISellingDocumentStore is absent from DI.
                // SellerRegistrationCompletedHandler is still discovered (its assembly is included
                // via Program.cs opts.Discovery.IncludeAssembly) and fails Wolverine code-gen when
                // SellerRegistrationCompleted is dispatched (OutboxedSessionFactory<ISellingDocumentStore>
                // not in DI). Excluding Selling BC handlers here restores pre-S3 behavior:
                // messages with no handler are silently discarded.
                services.AddSingleton<IWolverineExtension>(new SellingBcDiscoveryExclusion());

                // Disable RabbitMQ and any other external Wolverine transports.
                // Wolverine inbox/outbox (backed by SQL Server) remains active.
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

    /// <summary>
    /// Opens a lightweight Polecat document session for direct event stream access in tests.
    /// Always dispose the returned session after use (use `await using`).
    /// </summary>
    public IDocumentSession GetDocumentSession() =>
        Host.Services.DocumentStore().LightweightSession();

    /// <summary>
    /// Returns the Polecat IDocumentStore for advanced operations (e.g., manual cleanup).
    /// Prefer the host-level CleanAllPolecatDataAsync() for test isolation cleanup.
    /// </summary>
    public IDocumentStore GetDocumentStore() =>
        Host.Services.DocumentStore();

    // ─── Cleanup helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Cleans all Polecat documents and event data atomically.
    /// Call in InitializeAsync() of each test class to ensure test isolation.
    /// </summary>
    public Task CleanAllPolecatDataAsync() =>
        Host.Services.CleanAllPolecatDataAsync();

    // ─── Wolverine tracking helpers ───────────────────────────────────────────

    /// <summary>
    /// Invokes a message through the Wolverine pipeline and waits for all side effects
    /// (event persistence, outbox messages) to complete before returning.
    /// Use this instead of HTTP POST + direct query to avoid race conditions with
    /// Wolverine's async transaction commit.
    /// </summary>
    public async Task<ITrackedSession> ExecuteAndWaitAsync<T>(T message, int timeoutSeconds = 15)
        where T : class
    {
        return await Host.Services
            .TrackActivity(TimeSpan.FromSeconds(timeoutSeconds))
            .DoNotAssertOnExceptionsDetected()
            .AlsoTrack(Host.Services)
            .ExecuteAndWaitAsync((Func<IMessageContext, Task>)(async ctx => await ctx.InvokeAsync(message)));
    }

    /// <summary>
    /// Executes an Alba HTTP scenario and waits for all Wolverine side effects to complete.
    /// Use for tests that assert both HTTP response shape and downstream message/event outcomes.
    /// </summary>
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
/// Excludes Selling BC handler types from Wolverine discovery when the Selling BC is
/// not provisioned in this fixture (no PostgreSQL container). Without this exclusion,
/// <c>SellerRegistrationCompletedHandler</c> is discovered (the Selling assembly is
/// always included via <c>Program.cs opts.Discovery.IncludeAssembly</c>) and fails
/// Wolverine code-gen when <c>SellerRegistrationCompleted</c> is dispatched —
/// <c>OutboxedSessionFactory&lt;ISellingDocumentStore&gt;</c> is absent from DI because
/// <c>AddSellingModule()</c> returned early (no postgres connection string).
/// </summary>
internal sealed class SellingBcDiscoveryExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Selling BC inactive — exclude handlers (no postgres in Participants fixture)",
                t => t.Namespace?.StartsWith("CritterBids.Selling") == true);
        });

        // Route SellerRegistrationCompleted to a stub local queue so Wolverine tracking
        // captures it in tracked.Sent. Without a routing rule, Wolverine discards the
        // message before the tracker records it, causing tracked.Sent to return 0 items.
        // The handler is excluded above (no ISellingDocumentStore in DI), so the message
        // is delivered to the queue and silently dropped — matching pre-S3 placeholder behavior.
        options.PublishMessage<SellerRegistrationCompleted>()
            .ToLocalQueue("selling-participants-stub");
    }
}
