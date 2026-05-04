using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using CritterBids.Settlement.Tests.Fixtures;
using Marten;

namespace CritterBids.Settlement.Tests;

[Collection(SettlementTestCollection.Name)]
public class PendingSettlementHandlerTests : IAsyncLifetime
{
    private readonly SettlementTestFixture _fixture;

    public PendingSettlementHandlerTests(SettlementTestFixture fixture)
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

    // ───────────────────────────────────────────────────────────────────────────
    // §8.1 — Create on ListingPublished
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingPublished_CreatesPendingRow()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow;

        var message = new ListingPublished(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Vintage mechanical keyboard",
            Format: "Timed",
            StartingBid: 25m,
            ReservePrice: 50m,
            BuyItNow: 100m,
            Duration: TimeSpan.FromHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            FeePercentage: 0.10m,
            PublishedAt: publishedAt);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<PendingSettlement>(listingId);

        row.ShouldNotBeNull();
        row.Id.ShouldBe(listingId);
        row.SellerId.ShouldBe(sellerId);
        row.ReservePrice.ShouldBe(50m);
        row.BuyItNowPrice.ShouldBe(100m);
        row.FeePercentage.ShouldBe(0.10m);
        row.PublishedAt.ShouldBe(publishedAt);
        row.Status.ShouldBe(PendingSettlementStatus.Pending);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §8.8 — Idempotent replay; ListingPublished arriving twice
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingPublished_Duplicate_IsIdempotent()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow;

        var message = new ListingPublished(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Idempotency check listing",
            Format: "Timed",
            StartingBid: 25m,
            ReservePrice: 50m,
            BuyItNow: null,
            Duration: TimeSpan.FromHours(12),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            FeePercentage: 0.10m,
            PublishedAt: publishedAt);

        // First delivery — creates the row in Pending state.
        await using (var firstSession = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, firstSession, default);
            await firstSession.SaveChangesAsync();
        }

        // Second delivery with a fresh session — mirrors what a re-delivery from
        // RabbitMQ would look like in production.
        await using (var secondSession = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, secondSession, default);
            await secondSession.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<PendingSettlement>(listingId);

        row.ShouldNotBeNull();
        row.Status.ShouldBe(PendingSettlementStatus.Pending);
        row.SellerId.ShouldBe(sellerId);
        row.ReservePrice.ShouldBe(50m);
        row.FeePercentage.ShouldBe(0.10m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §8.4 — Mark Expired on ListingPassed
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingPassed_TransitionsPendingToExpired()
    {
        var listingId = Guid.CreateVersion7();
        await SeedPendingRowAsync(listingId);

        var message = new ListingPassed(
            ListingId: listingId,
            Reason: "ReserveNotMet",
            HighestBid: 30m,
            BidCount: 2,
            PassedAt: DateTimeOffset.UtcNow);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<PendingSettlement>(listingId);

        row.ShouldNotBeNull();
        row.Status.ShouldBe(PendingSettlementStatus.Expired);
        // Other fields preserved.
        row.ReservePrice.ShouldBe(50m);
        row.FeePercentage.ShouldBe(0.10m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §8.5 — Mark Expired on ListingWithdrawn
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingWithdrawn_TransitionsPendingToExpired()
    {
        var listingId = Guid.CreateVersion7();
        await SeedPendingRowAsync(listingId);

        var message = new ListingWithdrawn(
            ListingId: listingId,
            WithdrawnBy: Guid.CreateVersion7(),
            Reason: "Seller request",
            WithdrawnAt: DateTimeOffset.UtcNow);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<PendingSettlement>(listingId);

        row.ShouldNotBeNull();
        row.Status.ShouldBe(PendingSettlementStatus.Expired);
        row.ReservePrice.ShouldBe(50m);
        row.FeePercentage.ShouldBe(0.10m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §8.6 — Mark Consumed on SettlementCompleted
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SettlementCompleted_TransitionsPendingToConsumed()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = await SeedPendingRowAsync(listingId);

        var message = new SettlementCompleted(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: Guid.CreateVersion7(),
            SellerId: sellerId,
            HammerPrice: 55m,
            FeeAmount: 5.50m,
            SellerPayout: 49.50m,
            CompletedAt: DateTimeOffset.UtcNow);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<PendingSettlement>(listingId);

        row.ShouldNotBeNull();
        row.Status.ShouldBe(PendingSettlementStatus.Consumed);
        row.ReservePrice.ShouldBe(50m);
        row.FeePercentage.ShouldBe(0.10m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §8.7 — Mark Failed on PaymentFailed
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentFailed_TransitionsPendingToFailed()
    {
        var listingId = Guid.CreateVersion7();
        await SeedPendingRowAsync(listingId);

        var message = new PaymentFailed(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: Guid.CreateVersion7(),
            Reason: "InsufficientCredit",
            FailedAt: DateTimeOffset.UtcNow);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PendingSettlementHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var querySession = _fixture.GetDocumentSession();
        var row = await querySession.LoadAsync<PendingSettlement>(listingId);

        row.ShouldNotBeNull();
        row.Status.ShouldBe(PendingSettlementStatus.Failed);
        row.ReservePrice.ShouldBe(50m);
        row.FeePercentage.ShouldBe(0.10m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seed a PendingSettlement row in <see cref="PendingSettlementStatus.Pending"/> via
    /// the canonical <see cref="ListingPublished"/> path. Returns the SellerId so terminal-
    /// status tests can assert it on the row after transition.
    /// </summary>
    private async Task<Guid> SeedPendingRowAsync(Guid listingId)
    {
        var sellerId = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);

        var seed = new ListingPublished(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Seed listing",
            Format: "Timed",
            StartingBid: 25m,
            ReservePrice: 50m,
            BuyItNow: 100m,
            Duration: TimeSpan.FromHours(24),
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            FeePercentage: 0.10m,
            PublishedAt: publishedAt);

        await using var session = _fixture.GetDocumentSession();
        await PendingSettlementHandler.Handle(seed, session, default);
        await session.SaveChangesAsync();

        return sellerId;
    }
}
