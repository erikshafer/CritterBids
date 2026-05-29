using Alba;
using CritterBids.Auctions;
using CritterBids.Selling;
using JasperFx.CommandLine;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Tracking;
using ContractListingWithdrawn = CritterBids.Contracts.Selling.ListingWithdrawn;
using SellingListingPublished = CritterBids.Selling.ListingPublished;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// M4-S2 cross-BC integration test — proves that the Selling-side <c>WithdrawListing</c>
/// command authored at M4-S2 drives the Auction Closing saga's terminal-by-withdrawal path
/// end-to-end, replacing the M3-era fixture-synthesis of <see cref="ContractListingWithdrawn"/>
/// as the production-path producer.
///
/// Fixture posture: this test class hosts its own Alba runtime registering BOTH
/// <see cref="SellingModule.AddSellingModule"/> AND <see cref="AuctionsModule.AddAuctionsModule"/>
/// so that Wolverine's local routing delivers the contract event from Selling's handler to
/// Auctions's saga handler in-process. External transports stay disabled — no real RabbitMQ.
/// The existing <c>AuctionClosingSagaTests.ListingWithdrawn_TerminatesWithoutEvaluation</c>
/// (scenario 3.10) continues to run against fixture-synthesized events for coverage economy.
/// </summary>
public class RealSellingProducerSagaTerminationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithName($"m4-s2-integration-{Guid.NewGuid():N}")
        .WithCleanUp(true)
        .Build();

    private IAlbaHost _host = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var postgresConnectionString = _postgres.GetConnectionString();

        JasperFxEnvironment.AutoStartHost = true;

        _host = await AlbaHost.For<Program>(builder =>
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

                services.AddSellingModule();
                services.AddAuctionsModule();

                services.RunWolverineInSoloMode();
                services.DisableAllExternalWolverineTransports();

                // Excludes are narrow — Listings and Settlement only. Selling AND Auctions
                // are intentionally both active so the Selling handler can emit the contract
                // event and the Auctions saga handler can consume it via local routing.
                services.AddSingleton<IWolverineExtension>(new ListingsAndSettlementExclusion());
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync();
                await _host.DisposeAsync();
            }
            catch (ObjectDisposedException) { }
            catch (TaskCanceledException) { }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e =>
                e is OperationCanceledException or ObjectDisposedException)) { }
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task WithdrawListing_DispatchedToSelling_TerminatesAuctionsClosingSaga()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var withdrawnBy = sellerId;
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        var store = _host.DocumentStore();

        await using (var session = store.LightweightSession())
        {
            session.Store(new RegisteredSeller { Id = sellerId });

            session.Events.StartStream<SellerListing>(
                listingId,
                new DraftListingCreated(
                    ListingId: listingId,
                    SellerId: sellerId,
                    Title: "Hand-Forged Damascus Steel Knife",
                    Format: ListingFormat.Flash,
                    StartingBid: 50m,
                    ReservePrice: 100m,
                    BuyItNowPrice: 200m,
                    Duration: null,
                    ExtendedBiddingEnabled: false,
                    ExtendedBiddingTriggerWindow: null,
                    ExtendedBiddingExtension: null,
                    CreatedAt: DateTimeOffset.UtcNow),
                new ListingSubmitted(listingId, sellerId, DateTimeOffset.UtcNow),
                new ListingApproved(listingId, DateTimeOffset.UtcNow),
                new SellingListingPublished(listingId, DateTimeOffset.UtcNow));

            session.Store(new AuctionClosingSaga
            {
                Id = listingId,
                ListingId = listingId,
                Status = AuctionClosingStatus.Active,
                ScheduledCloseAt = closeAt,
                OriginalCloseAt = closeAt,
                BidCount = 3,
                CurrentHighBid = 75m,
                CurrentHighBidderId = bidderId,
                ReserveHasBeenMet = true,
                ExtendedBiddingEnabled = false,
            });

            await session.SaveChangesAsync();
        }

        var tracked = await _host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(ctx => ctx.InvokeAsync(new WithdrawListing(listingId, withdrawnBy)));

        await using (var verify = store.LightweightSession())
        {
            var sellerListing = await verify.Events.AggregateStreamAsync<SellerListing>(listingId);
            sellerListing.ShouldNotBeNull();
            sellerListing.Status.ShouldBe(ListingStatus.Withdrawn);

            var saga = await verify.LoadAsync<AuctionClosingSaga>(listingId);
            saga.ShouldBeNull("Auction Closing saga should be MarkCompleted-deleted by the ListingWithdrawn terminal path");
        }

        // No closing outcome events emitted on the withdrawal terminal path.
        tracked.Sent.MessagesOf<CritterBids.Contracts.Auctions.BiddingClosed>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<CritterBids.Contracts.Auctions.ListingSold>().ShouldBeEmpty();
        tracked.Sent.MessagesOf<CritterBids.Contracts.Auctions.ListingPassed>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<CritterBids.Contracts.Auctions.BiddingClosed>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<CritterBids.Contracts.Auctions.ListingSold>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<CritterBids.Contracts.Auctions.ListingPassed>().ShouldBeEmpty();
    }
}

/// <summary>
/// Narrow-scope exclusion for the cross-BC integration fixture: keeps Selling and Auctions
/// handlers in discovery; excludes Listings (CatalogListingView not registered) and Settlement
/// (PendingSettlement schema not registered).
/// </summary>
internal sealed class ListingsAndSettlementExclusion : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.Discovery.CustomizeHandlerDiscovery(x =>
        {
            x.Excludes.WithCondition(
                "Listings BC inactive — AddListingsModule not called in M4-S2 integration fixture",
                t => t.Namespace?.StartsWith("CritterBids.Listings") == true);
            x.Excludes.WithCondition(
                "Settlement BC inactive — AddSettlementModule not called in M4-S2 integration fixture",
                t => t.Namespace?.StartsWith("CritterBids.Settlement") == true);
            x.Excludes.WithCondition(
                "Obligations BC inactive — AddObligationsModule not called in M4-S2 integration fixture",
                t => t.Namespace?.StartsWith("CritterBids.Obligations") == true);
        });
    }
}
