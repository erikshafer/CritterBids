using CritterBids.Contracts.Settlement;
using CritterBids.Settlement;
using CritterBids.Settlement.Tests.Fixtures;
using Marten;

namespace CritterBids.Settlement.Tests;

/// <summary>
/// Integration tests for the <see cref="SellerSettlementSummary"/> handler-driven projection
/// and the <c>GET /api/settlement/summaries?sellerId={sellerId}</c> query endpoint (M9-S3).
/// </summary>
[Collection(SettlementTestCollection.Name)]
public class SellerSettlementSummaryTests : IAsyncLifetime
{
    private readonly SettlementTestFixture _fixture;

    public SellerSettlementSummaryTests(SettlementTestFixture fixture)
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

    // ── handler tests (direct invocation) ────────────────────────────────────

    [Fact]
    public async Task Handle_SettlementCompleted_CreatesSellerSettlementSummary()
    {
        var listingId = Guid.CreateVersion7();
        var settlementId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        var completedAt = DateTimeOffset.UtcNow;

        var message = new SettlementCompleted(
            SettlementId: settlementId,
            ListingId: listingId,
            WinnerId: winnerId,
            SellerId: sellerId,
            HammerPrice: 500m,
            FeeAmount: 50m,
            SellerPayout: 450m,
            CompletedAt: completedAt);

        await using var session = _fixture.GetDocumentSession();
        await SellerSettlementSummaryHandler.Handle(message, session, CancellationToken.None);
        await session.SaveChangesAsync();

        var summary = await session.LoadAsync<SellerSettlementSummary>(listingId);
        summary.ShouldNotBeNull();
        summary!.Id.ShouldBe(listingId);
        summary.SettlementId.ShouldBe(settlementId);
        summary.SellerId.ShouldBe(sellerId);
        summary.WinnerId.ShouldBe(winnerId);
        summary.HammerPrice.ShouldBe(500m);
        summary.FeeAmount.ShouldBe(50m);
        summary.SellerPayout.ShouldBe(450m);
        summary.CompletedAt.ShouldBe(completedAt);
    }

    [Fact]
    public async Task Handle_SettlementCompleted_Redelivery_UpsertsSameValues()
    {
        var listingId = Guid.CreateVersion7();
        var settlementId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();
        var completedAt = DateTimeOffset.UtcNow;

        var message = new SettlementCompleted(
            SettlementId: settlementId,
            ListingId: listingId,
            WinnerId: winnerId,
            SellerId: sellerId,
            HammerPrice: 500m,
            FeeAmount: 50m,
            SellerPayout: 450m,
            CompletedAt: completedAt);

        // First delivery
        await using (var session = _fixture.GetDocumentSession())
        {
            await SellerSettlementSummaryHandler.Handle(message, session, CancellationToken.None);
            await session.SaveChangesAsync();
        }

        // Re-delivery — same values
        await using (var session = _fixture.GetDocumentSession())
        {
            await SellerSettlementSummaryHandler.Handle(message, session, CancellationToken.None);
            await session.SaveChangesAsync();
        }

        await using var verifySession = _fixture.GetDocumentSession();
        var summary = await verifySession.LoadAsync<SellerSettlementSummary>(listingId);
        summary.ShouldNotBeNull();
        summary!.SellerPayout.ShouldBe(450m);
    }

    // ── query endpoint tests ─────────────────────────────────────────────────

    private async Task SeedSellerSettlementSummaryAsync(
        Guid listingId,
        Guid sellerId,
        decimal hammerPrice = 100m,
        decimal feeAmount = 10m,
        decimal sellerPayout = 90m)
    {
        await using var session = _fixture.GetDocumentSession();
        session.Store(new SellerSettlementSummary
        {
            Id = listingId,
            SettlementId = Guid.CreateVersion7(),
            SellerId = sellerId,
            WinnerId = Guid.CreateVersion7(),
            HammerPrice = hammerPrice,
            FeeAmount = feeAmount,
            SellerPayout = sellerPayout,
            CompletedAt = DateTimeOffset.UtcNow,
        });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSellerSettlements_WithSummaries_ReturnsSellersSettlements()
    {
        var sellerId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();
        await SeedSellerSettlementSummaryAsync(listingId, sellerId, 500m, 50m, 450m);

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/settlement/summaries?sellerId={sellerId}");
            s.StatusCodeShouldBe(200);
        });

        var summaries = result.ReadAsJson<SellerSettlementSummary[]>();
        summaries.ShouldNotBeNull();
        summaries!.Length.ShouldBe(1);
        summaries[0].Id.ShouldBe(listingId);
        summaries[0].SellerId.ShouldBe(sellerId);
        summaries[0].HammerPrice.ShouldBe(500m);
        summaries[0].FeeAmount.ShouldBe(50m);
        summaries[0].SellerPayout.ShouldBe(450m);
    }

    [Fact]
    public async Task GetSellerSettlements_UnknownSeller_ReturnsEmptyList()
    {
        var unknownSellerId = Guid.CreateVersion7();

        var result = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/settlement/summaries?sellerId={unknownSellerId}");
            s.StatusCodeShouldBe(200);
        });

        var summaries = result.ReadAsJson<SellerSettlementSummary[]>();
        summaries.ShouldNotBeNull();
        summaries!.Length.ShouldBe(0);
    }

    [Fact]
    public async Task GetSellerSettlements_DifferentSellers_OnlyReturnOwnSettlements()
    {
        var sellerA = Guid.CreateVersion7();
        var sellerB = Guid.CreateVersion7();
        await SeedSellerSettlementSummaryAsync(Guid.CreateVersion7(), sellerA, 300m, 30m, 270m);
        await SeedSellerSettlementSummaryAsync(Guid.CreateVersion7(), sellerB, 600m, 60m, 540m);

        var resultA = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/settlement/summaries?sellerId={sellerA}");
            s.StatusCodeShouldBe(200);
        });

        var summariesA = resultA.ReadAsJson<SellerSettlementSummary[]>();
        summariesA.ShouldNotBeNull();
        summariesA!.Length.ShouldBe(1);
        summariesA[0].HammerPrice.ShouldBe(300m);

        var resultB = await _fixture.Host.Scenario(s =>
        {
            s.Get.Url($"/api/settlement/summaries?sellerId={sellerB}");
            s.StatusCodeShouldBe(200);
        });

        var summariesB = resultB.ReadAsJson<SellerSettlementSummary[]>();
        summariesB.ShouldNotBeNull();
        summariesB!.Length.ShouldBe(1);
        summariesB[0].HammerPrice.ShouldBe(600m);
    }
}
