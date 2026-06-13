using CritterBids.Obligations;
using CritterBids.Obligations.Tests.Fixtures;

namespace CritterBids.Obligations.Tests;

/// <summary>
/// HTTP-level integration tests for <c>GET /api/obligations/status?sellerId={sellerId}</c> (M9-S3).
/// Queries the <see cref="ObligationStatusView"/> inline projection filtered by seller.
/// </summary>
[Collection(ObligationsTestCollection.Name)]
public class GetSellerObligationsApiTests : IAsyncLifetime
{
    private readonly ObligationsTestFixture _fixture;

    public GetSellerObligationsApiTests(ObligationsTestFixture fixture)
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

    private async Task SeedObligationStatusViewAsync(
        Guid obligationId,
        Guid listingId,
        Guid sellerId,
        Guid winnerId,
        decimal hammerPrice = 100m,
        ObligationStatus status = ObligationStatus.AwaitingShipment)
    {
        await using var session = _fixture.GetDocumentSession();
        session.Store(new ObligationStatusView
        {
            Id = obligationId,
            ListingId = listingId,
            WinnerId = winnerId,
            SellerId = sellerId,
            HammerPrice = hammerPrice,
            Status = status,
            ShipByDeadline = DateTimeOffset.UtcNow.AddDays(3),
        });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSellerObligations_WithObligations_ReturnsSellersObligations()
    {
        var sellerId = Guid.CreateVersion7();
        var obligationId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        await SeedObligationStatusViewAsync(obligationId, listingId, sellerId, winnerId, 250m);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/obligations/status?sellerId={sellerId}");
            s.StatusCodeShouldBe(200);
        });

        var obligations = result.ReadAsJson<ObligationStatusView[]>();
        obligations.ShouldNotBeNull();
        obligations!.Length.ShouldBe(1);
        obligations[0].Id.ShouldBe(obligationId);
        obligations[0].SellerId.ShouldBe(sellerId);
        obligations[0].ListingId.ShouldBe(listingId);
        obligations[0].HammerPrice.ShouldBe(250m);
        obligations[0].Status.ShouldBe(ObligationStatus.AwaitingShipment);
    }

    [Fact]
    public async Task GetSellerObligations_UnknownSeller_ReturnsEmptyList()
    {
        var unknownSellerId = Guid.CreateVersion7();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/obligations/status?sellerId={unknownSellerId}");
            s.StatusCodeShouldBe(200);
        });

        var obligations = result.ReadAsJson<ObligationStatusView[]>();
        obligations.ShouldNotBeNull();
        obligations!.Length.ShouldBe(0);
    }

    [Fact]
    public async Task GetSellerObligations_DifferentSellers_OnlyReturnOwnObligations()
    {
        var sellerA = Guid.CreateVersion7();
        var sellerB = Guid.CreateVersion7();
        await SeedObligationStatusViewAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), sellerA, Guid.CreateVersion7(), 100m);
        await SeedObligationStatusViewAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), sellerB, Guid.CreateVersion7(), 200m);

        var resultA = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/obligations/status?sellerId={sellerA}");
            s.StatusCodeShouldBe(200);
        });

        var obligationsA = resultA.ReadAsJson<ObligationStatusView[]>();
        obligationsA.ShouldNotBeNull();
        obligationsA!.Length.ShouldBe(1);
        obligationsA[0].HammerPrice.ShouldBe(100m);

        var resultB = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/obligations/status?sellerId={sellerB}");
            s.StatusCodeShouldBe(200);
        });

        var obligationsB = resultB.ReadAsJson<ObligationStatusView[]>();
        obligationsB.ShouldNotBeNull();
        obligationsB!.Length.ShouldBe(1);
        obligationsB[0].HammerPrice.ShouldBe(200m);
    }
}
