using Alba;
using CritterBids.Api.Auth;
using CritterBids.Operations;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;

namespace CritterBids.Api.Tests.Fixtures;

/// <summary>
/// The M7-S7 cross-BC journey-test fixture. Boots from the real <c>Program.cs</c> via
/// <c>AlbaHost.For&lt;Program&gt;</c> (so the full Wolverine routing, Separated-handler wiring, and
/// <c>MapWolverineEndpoints()</c> are exercised) and overlays:
/// <list type="bullet">
///   <item>Testcontainers PostgreSQL + the full Marten + Operations-module schema registration.</item>
///   <item>Staff auth configuration — the <c>OperationsAuth:StaffToken</c> key is set to
///         <see cref="ValidStaffToken"/> so the StaffOnly-gated endpoints accept it.</item>
///   <item>Six cross-BC handler discovery exclusions (identical to the
///         <c>OperationsTestFixture</c>) so <c>InvokeMessageAndWaitAsync</c> runs only Operations
///         handlers, making tracked-session dispatch clean and the <c>tracked.Sent</c>
///         pure-consumer assertion unambiguous.</item>
/// </list>
///
/// <para>Alba's in-memory <c>TestServer</c> provides the HTTP surface — operations query endpoints
/// are reachable via <see cref="IAlbaHost.Scenario"/> with the <c>X-Staff-Token</c> header, proving
/// the StaffOnly gate without a real Kestrel socket (SignalR is out of scope for this test).</para>
/// </summary>
public class JourneyTestFixture : IAsyncLifetime
{
    /// <summary>The configured staff token for the journey test.</summary>
    public const string ValidStaffToken = "m7-s7-journey-test-token";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"journey-postgres-test-{Guid.NewGuid():N}")
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
            builder.UseSetting(StaffAuthConstants.StaffTokenConfigKey, ValidStaffToken);

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

                services.AddOperationsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // ─── Cross-BC handler isolation (per project_cross_bc_handler_isolation.md) ──
                // Only Operations handlers run. The six foreign BCs are excluded so
                // InvokeMessageAndWaitAsync completes cleanly and tracked.Sent reflects
                // Operations' pure-consumer contract only.
                services.AddSingleton<IWolverineExtension>(new JourneySellingExclusion());
                services.AddSingleton<IWolverineExtension>(new JourneyAuctionsExclusion());
                services.AddSingleton<IWolverineExtension>(new JourneyListingsExclusion());
                services.AddSingleton<IWolverineExtension>(new JourneySettlementExclusion());
                services.AddSingleton<IWolverineExtension>(new JourneyObligationsExclusion());
                services.AddSingleton<IWolverineExtension>(new JourneyRelayExclusion());
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
}

// ─── Foreign-BC discovery exclusions ──────────────────────────────────────────
// Mirror the OperationsTestFixture exclusions, scoped to the journey fixture. One class per BC so
// the reason string is specific and grep-friendly.

internal sealed class JourneySellingExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Journey: Selling BC inactive", t => t.Namespace?.StartsWith("CritterBids.Selling") == true));
}

internal sealed class JourneyAuctionsExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Journey: Auctions BC inactive", t => t.Namespace?.StartsWith("CritterBids.Auctions") == true));
}

internal sealed class JourneyListingsExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Journey: Listings BC inactive", t => t.Namespace?.StartsWith("CritterBids.Listings") == true));
}

internal sealed class JourneySettlementExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Journey: Settlement BC inactive", t => t.Namespace?.StartsWith("CritterBids.Settlement") == true));
}

internal sealed class JourneyObligationsExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Journey: Obligations BC inactive", t => t.Namespace?.StartsWith("CritterBids.Obligations") == true));
}

internal sealed class JourneyRelayExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options) =>
        options.Discovery.CustomizeHandlerDiscovery(x =>
            x.Excludes.WithCondition("Journey: Relay BC inactive", t => t.Namespace?.StartsWith("CritterBids.Relay") == true));
}

/// <summary>The xUnit collection that serializes the journey test onto one shared fixture/container.</summary>
[CollectionDefinition(Name)]
public sealed class JourneyTestCollection : ICollectionFixture<JourneyTestFixture>
{
    public const string Name = "Operations end-to-end journey";
}
