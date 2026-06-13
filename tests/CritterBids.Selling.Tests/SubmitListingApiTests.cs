using CritterBids.Contracts;
using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;

namespace CritterBids.Selling.Tests;

/// <summary>
/// HTTP-level integration tests for <c>POST /api/selling/listings/submit</c> (M9-S2).
/// The endpoint is a thin gateway that cascades <see cref="SubmitListing"/> to
/// <see cref="SubmitListingHandler"/> and returns 202 Accepted.
/// </summary>
[Collection(SellingTestCollection.Name)]
public class SubmitListingApiTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public SubmitListingApiTests(SellingTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _fixture.CleanAllMartenDataAsync();
        }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid sellerId, Guid listingId)> SeedDraftListingAsync()
    {
        var sellerId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();

        await using var session = _fixture.GetDocumentSession();
        session.Store(new RegisteredSeller { Id = sellerId });
        session.Events.StartStream<SellerListing>(listingId, new DraftListingCreated(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Antique Bronze Compass",
            Format: ListingFormat.Flash,
            StartingBid: 25m,
            ReservePrice: 50m,
            BuyItNowPrice: 100m,
            Duration: null,
            ExtendedBiddingEnabled: false,
            ExtendedBiddingTriggerWindow: null,
            ExtendedBiddingExtension: null,
            CreatedAt: DateTimeOffset.UtcNow));
        await session.SaveChangesAsync();

        return (sellerId, listingId);
    }

    // ── happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitListing_DraftListing_Returns202AndPublishesListing()
    {
        var (sellerId, listingId) = await SeedDraftListingAsync();

        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new SubmitListing(listingId, sellerId))
                .ToUrl("/api/selling/listings/submit");
            s.StatusCodeShouldBe(202);
        });

        var location = result.Context.Response.Headers.Location.ToString();
        location.ShouldBe($"/api/selling/listings/{listingId}");

        await using var querySession = _fixture.GetDocumentSession();
        var listing = await querySession.Events.AggregateStreamAsync<SellerListing>(listingId);
        listing.ShouldNotBeNull();
        listing.Status.ShouldBe(ListingStatus.Published);
    }

    // ── guard: non-draft listing ──────────────────────────────────────────────

    [Fact]
    public async Task SubmitListing_AlreadyPublishedListing_Returns202ButHandlerRejects()
    {
        var (sellerId, listingId) = await SeedDraftListingAsync();

        // First submit — transitions to Published
        await _fixture.ExecuteAndWaitAsync(new SubmitListing(listingId, sellerId));

        // Verify Published state
        await using var verifySession = _fixture.GetDocumentSession();
        var published = await verifySession.Events.AggregateStreamAsync<SellerListing>(listingId);
        published.ShouldNotBeNull();
        published.Status.ShouldBe(ListingStatus.Published);

        // Second submit via HTTP — endpoint returns 202 (thin gateway), but the
        // handler will throw InvalidListingStateException during async processing
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new SubmitListing(listingId, sellerId))
                .ToUrl("/api/selling/listings/submit");
            s.StatusCodeShouldBe(202);
        });
    }
}
