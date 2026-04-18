using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Auctions.Tests;

[Collection(AuctionsTestCollection.Name)]
public class BiddingOpenedConsumerTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public BiddingOpenedConsumerTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _fixture.CleanAllMartenDataAsync();
        }
        catch (ObjectDisposedException)
        {
            // Host failed to start (e.g. schema migration error during fixture initialization).
            // Tests will fail with a clearer message rather than cascading ObjectDisposedExceptions.
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ListingPublished_FromSelling_ProducesBiddingOpened()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromDays(7);

        var message = new ListingPublished(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Rare first-edition vintage bid",
            Format: "Timed",
            StartingBid: 100m,
            ReservePrice: 250m,
            BuyItNow: 500m,
            Duration: duration,
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromMinutes(2),
            ExtendedBiddingExtension: TimeSpan.FromMinutes(5),
            FeePercentage: 0.10m,
            PublishedAt: publishedAt);

        await using (var session = _fixture.GetDocumentSession())
        {
            await ListingPublishedHandler.Handle(message, session);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var events = await querySession.Events.FetchStreamAsync(listingId);

        events.Count.ShouldBe(1);
        var opened = events[0].Data.ShouldBeOfType<BiddingOpened>();

        opened.ListingId.ShouldBe(listingId);
        opened.SellerId.ShouldBe(sellerId);
        opened.StartingBid.ShouldBe(100m);
        opened.ReserveThreshold.ShouldBe(250m);
        opened.BuyItNowPrice.ShouldBe(500m);
        opened.ScheduledCloseAt.ShouldBe(publishedAt.Add(duration));
        opened.ExtendedBiddingEnabled.ShouldBeTrue();
        opened.ExtendedBiddingTriggerWindow.ShouldBe(TimeSpan.FromMinutes(2));
        opened.ExtendedBiddingExtension.ShouldBe(TimeSpan.FromMinutes(5));
        opened.MaxDuration.ShouldBe(duration);
        opened.OpenedAt.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task ListingPublished_Duplicate_IsIdempotent()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromDays(3);

        var message = new ListingPublished(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Idempotency check listing",
            Format: "Timed",
            StartingBid: 50m,
            ReservePrice: null,
            BuyItNow: null,
            Duration: duration,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            FeePercentage: 0.10m,
            PublishedAt: publishedAt);

        // First delivery — produces the BiddingOpened and starts the Listing stream.
        await using (var firstSession = _fixture.GetDocumentSession())
        {
            await ListingPublishedHandler.Handle(message, firstSession);
            await firstSession.SaveChangesAsync();
        }

        // Second delivery — same ListingPublished, same ListingId. The handler's
        // FetchStreamStateAsync check finds the existing stream and returns without
        // appending a second event and without throwing. A fresh session is used to
        // mirror what a re-delivery from RabbitMQ would look like in production.
        await using (var secondSession = _fixture.GetDocumentSession())
        {
            await ListingPublishedHandler.Handle(message, secondSession);
            await secondSession.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var events = await querySession.Events.FetchStreamAsync(listingId);

        events.Count.ShouldBe(1);
        events[0].Data.ShouldBeOfType<BiddingOpened>();
    }
}
