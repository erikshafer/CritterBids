using CritterBids.Contracts.Settlement;
using CritterBids.Listings;
using CritterBids.Listings.Tests.Fixtures;
using Marten;

namespace CritterBids.Listings.Tests;

/// <summary>
/// Integration tests for the M5-S6 Listings → SettlementStatusHandler surface.
/// Covers the "Sold" → "Settled" transition, the "Passed" status-preservation guard,
/// and the tolerant-upsert posture for the structurally near-impossible
/// SettlementCompleted-before-ListingPublished race.
///
/// Handler is invoked directly (not via Wolverine bus dispatch) per the
/// project_wolverine_sticky_handler.md memory: opts.ListenToRabbitQueue creates a
/// sticky binding for SettlementCompleted to the RabbitMQ endpoint, and the test
/// fixture calls DisableAllExternalWolverineTransports — bus dispatch via
/// Host.InvokeMessageAndWaitAsync raises NoHandlerForEndpointException. Direct
/// invocation exercises the same handler logic; only the dispatch mechanism differs.
/// </summary>
[Collection(ListingsTestCollection.Name)]
public class SettlementStatusHandlerTests : IAsyncLifetime
{
    private readonly ListingsTestFixture _fixture;

    public SettlementStatusHandlerTests(ListingsTestFixture fixture)
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

    private async Task InvokeHandlerAsync(SettlementCompleted message)
    {
        await using var session = _fixture.GetDocumentSession();
        await SettlementStatusHandler.Handle(message, session, CancellationToken.None);
        await session.SaveChangesAsync();
    }

    private static SettlementCompleted BuildMessage(Guid listingId, DateTimeOffset? completedAt = null) =>
        new(
            SettlementId: Guid.CreateVersion7(),
            ListingId: listingId,
            WinnerId: Guid.CreateVersion7(),
            SellerId: Guid.CreateVersion7(),
            HammerPrice: 85.00m,
            FeeAmount: 8.50m,
            SellerPayout: 76.50m,
            CompletedAt: completedAt ?? DateTimeOffset.UtcNow);

    // ── M5-S6: SettlementCompleted transitions Sold → Settled ────────────────

    [Fact]
    public async Task Handle_TransitionsCatalogListingViewFromSoldToSettled()
    {
        // Arrange — view in "Sold" terminal state (post-ListingSold or post-BuyItNowPurchased).
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var winnerId = Guid.CreateVersion7();

        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await using (var session = _fixture.GetDocumentSession())
        {
            var view = await session.LoadAsync<CatalogListingView>(listingId);
            view.ShouldNotBeNull();
            session.Store(view! with
            {
                Status      = "Sold",
                HammerPrice = 85.00m,
                WinnerId    = winnerId,
                ClosedAt    = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
            await session.SaveChangesAsync();
        }

        var completedAt = DateTimeOffset.UtcNow;
        var completed = BuildMessage(listingId, completedAt);

        // Act
        await InvokeHandlerAsync(completed);

        // Assert — Status transitions to "Settled", SettledAt populated, prior fields preserved.
        var settled = await _fixture.LoadCatalogListingViewAsync(listingId);
        settled.ShouldNotBeNull();
        settled!.Status.ShouldBe("Settled");
        settled.SettledAt.ShouldBe(completedAt);
        settled.HammerPrice.ShouldBe(85.00m);                              // prior auction-status field preserved
        settled.WinnerId.ShouldBe(winnerId);                               // prior auction-status field preserved
        settled.Title.ShouldBe("Mint Condition Foil Black Lotus");         // prior M2 field preserved
        settled.SellerId.ShouldBe(sellerId);                               // prior M2 field preserved
    }

    // ── M5-S6: Defensive — "Passed" listings never settle ────────────────────

    [Fact]
    public async Task Handle_OnPassedListing_PreservesPassedStatus()
    {
        // Arrange — view in "Passed" terminal state. SettlementCompleted should never
        // arrive on a Passed listing (the financial workflow's reserve-not-met branch
        // emits PaymentFailed, not SettlementCompleted), but the guard is structural.
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var passedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await _fixture.SeedCatalogListingViewAsync(listingId, sellerId);
        await using (var session = _fixture.GetDocumentSession())
        {
            var seeded = await session.LoadAsync<CatalogListingView>(listingId);
            seeded.ShouldNotBeNull();
            session.Store(seeded! with
            {
                Status       = "Passed",
                PassedReason = "ReserveNotMet",
                ClosedAt     = passedAt,
            });
            await session.SaveChangesAsync();
        }

        var completed = BuildMessage(listingId);

        // Act
        await InvokeHandlerAsync(completed);

        // Assert — Passed state preserved; SettledAt remains null.
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Status.ShouldBe("Passed");
        view.PassedReason.ShouldBe("ReserveNotMet");
        view.ClosedAt.ShouldBe(passedAt);
        view.SettledAt.ShouldBeNull();
    }

    // ── M5-S6: Tolerant upsert on cross-queue race (no prior row) ────────────

    [Fact]
    public async Task Handle_OnMissingRow_TolerantUpsertCreatesMinimalSettledRow()
    {
        // Arrange — no prior CatalogListingView row exists. The structurally
        // near-impossible cross-queue race where SettlementCompleted arrives before
        // ListingPublished. The M5-S6 amendment to ListingPublishedHandler will
        // preserve Status = "Settled" and SettledAt when ListingPublished later lands.
        var listingId = Guid.CreateVersion7();
        var completedAt = DateTimeOffset.UtcNow;
        var completed = BuildMessage(listingId, completedAt);

        // Act
        await InvokeHandlerAsync(completed);

        // Assert — minimal row created with Id, Status, SettledAt only. M2 fields
        // default-initialized.
        var view = await _fixture.LoadCatalogListingViewAsync(listingId);
        view.ShouldNotBeNull();
        view!.Id.ShouldBe(listingId);
        view.Status.ShouldBe("Settled");
        view.SettledAt.ShouldBe(completedAt);
        view.Title.ShouldBe("");                  // default-initialized M2 field
        view.SellerId.ShouldBe(Guid.Empty);       // default-initialized M2 field
    }
}
