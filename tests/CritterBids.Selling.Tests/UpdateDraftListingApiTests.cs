using CritterBids.Contracts;
using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;

namespace CritterBids.Selling.Tests;

/// <summary>
/// HTTP-level integration tests for <c>PUT /api/selling/listings/draft</c> (M9-S2).
/// The endpoint is a thin gateway that cascades <see cref="UpdateDraftListing"/> to
/// <see cref="UpdateDraftListingHandler"/> and returns 202 Accepted.
/// </summary>
[Collection(SellingTestCollection.Name)]
public class UpdateDraftListingApiTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public UpdateDraftListingApiTests(SellingTestFixture fixture)
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
            Title: "Victorian Era Pocket Watch",
            Format: ListingFormat.Flash,
            StartingBid: 75m,
            ReservePrice: 150m,
            BuyItNowPrice: 300m,
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
    public async Task UpdateDraftListing_DraftListing_Returns202AndUpdatesListing()
    {
        var (_, listingId) = await SeedDraftListingAsync();

        var (tracked, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Put.Json(new UpdateDraftListing(
                    ListingId: listingId,
                    Title: "Restored Victorian Era Pocket Watch",
                    ReservePrice: 200m))
                .ToUrl("/api/selling/listings/draft");
            s.StatusCodeShouldBe(202);
        });

        var location = result.Context.Response.Headers.Location.ToString();
        location.ShouldBe($"/api/selling/listings/{listingId}");

        await using var querySession = _fixture.GetDocumentSession();
        var listing = await querySession.Events.AggregateStreamAsync<SellerListing>(listingId);
        listing.ShouldNotBeNull();
        listing.Title.ShouldBe("Restored Victorian Era Pocket Watch");
        listing.ReservePrice.ShouldBe(200m);
        listing.BuyItNowPrice.ShouldBe(300m);
    }

    // ── guard: non-draft listing ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateDraftListing_PublishedListing_Returns202ButHandlerRejects()
    {
        var (sellerId, listingId) = await SeedDraftListingAsync();

        // Transition to Published
        await _fixture.ExecuteAndWaitAsync(new SubmitListing(listingId, sellerId));

        // Update via HTTP — endpoint returns 202, handler will throw
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Put.Json(new UpdateDraftListing(ListingId: listingId, Title: "Should Not Stick"))
                .ToUrl("/api/selling/listings/draft");
            s.StatusCodeShouldBe(202);
        });
    }
}
