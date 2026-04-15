using CritterBids.Contracts;
using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;

namespace CritterBids.Selling.Tests;

/// <summary>
/// HTTP-level gateway tests for <c>POST /api/listings/draft</c>.
/// Verifies the seller-registration gate (scenarios 7.1 and 7.2 from
/// <c>docs/workshops/004-scenarios.md</c> §7).
/// </summary>
[Collection(SellingTestCollection.Name)]
public class CreateDraftListingApiTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public CreateDraftListingApiTests(SellingTestFixture fixture)
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

    private static CreateDraftListing BuildDraftCmd(Guid sellerId) => new(
        SellerId: sellerId,
        Title: "Hand-Forged Damascus Steel Knife",
        Format: ListingFormat.Flash,
        StartingBid: 50m,
        ReservePrice: 100m,
        BuyItNowPrice: 200m,
        Duration: null,
        ExtendedBiddingEnabled: false,
        ExtendedBiddingTriggerWindow: null,
        ExtendedBiddingExtension: null);

    // ── 7.1 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraftListing_WithRegisteredSeller_Returns201()
    {
        // Arrange: ensure the seller is in the RegisteredSellers projection
        var sellerId = Guid.CreateVersion7();
        await _fixture.ExecuteAndWaitAsync(
            new SellerRegistrationCompleted(sellerId, DateTimeOffset.UtcNow));

        var cmd = BuildDraftCmd(sellerId);

        // Act
        var (_, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(cmd).ToUrl("/api/listings/draft");
            s.StatusCodeShouldBe(201);
        });

        // Assert: Location header points to the new listing resource
        var location = result.Context.Response.Headers.Location.ToString();
        location.ShouldStartWith("/api/listings/");
    }

    // ── 7.2 ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDraftListing_WithUnregisteredSeller_Returns403()
    {
        // Arrange: use a seller ID that was never registered
        var unknownSellerId = Guid.CreateVersion7();
        var cmd = BuildDraftCmd(unknownSellerId);

        // Act
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(cmd).ToUrl("/api/listings/draft");
            s.StatusCodeShouldBe(403);
        });

        // Assert: response body contains the rejection reason
        var body = await result.ReadAsTextAsync();
        body.ShouldContain("Seller is not registered");
    }
}
