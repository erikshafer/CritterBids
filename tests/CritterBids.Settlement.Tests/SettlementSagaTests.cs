using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using CritterBids.Settlement.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Settlement.Tests;

[Collection(SettlementTestCollection.Name)]
public class SettlementSagaTests : IAsyncLifetime
{
    private readonly SettlementTestFixture _fixture;

    public SettlementSagaTests(SettlementTestFixture fixture)
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
            // Host failed to start — let the test fail with a clearer message rather than
            // cascading ObjectDisposedExceptions.
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ───────────────────────────────────────────────────────────────────────────
    // §9.1 — Full bidding-source happy path
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Full_BiddingSource_HappyPath_ProducesSixEventStream()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var soldAt = DateTimeOffset.UtcNow;

        // Seed the PendingSettlement projection via the canonical ListingPublished path —
        // the saga's start handler loads it for ReservePrice / FeePercentage / SellerId.
        var listingPublished = new ListingPublished(
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
            await PendingSettlementHandler.Handle(listingPublished, session, default);
            await session.SaveChangesAsync();
        }

        // Dispatch ListingSold — the saga starts, walks five continuation phases via
        // Wolverine's local queue, and reaches MarkCompleted. InvokeMessageAndWaitAsync
        // drains the local queue before returning.
        var listingSold = new ListingSold(
            ListingId: listingId,
            SellerId: sellerId,
            WinnerId: winnerId,
            HammerPrice: 85m,
            BidCount: 12,
            SoldAt: soldAt);

        await _fixture.Host.InvokeMessageAndWaitAsync(listingSold);

        // The saga's Id is deterministic — derive it the same way the saga did.
        var settlementId = SettlementsIdentityNamespaces.SettlementId(listingId);

        // Assert the financial event stream contains six events in the §9.1 order.
        await using var querySession = _fixture.GetDocumentSession();
        var events = await querySession.Events.FetchStreamAsync(settlementId);

        events.Count.ShouldBe(6);

        var initiated = events[0].Data.ShouldBeOfType<SettlementInitiated>();
        initiated.SettlementId.ShouldBe(settlementId);
        initiated.ListingId.ShouldBe(listingId);
        initiated.WinnerId.ShouldBe(winnerId);
        initiated.SellerId.ShouldBe(sellerId);
        initiated.Price.ShouldBe(85m);
        initiated.Source.ShouldBe(SettlementSource.Bidding);
        initiated.ReservePrice.ShouldBe(50m);
        initiated.FeePercentage.ShouldBe(0.10m);

        var reserveCheck = events[1].Data.ShouldBeOfType<ReserveCheckCompleted>();
        reserveCheck.WasMet.ShouldBeTrue();
        reserveCheck.Price.ShouldBe(85m);
        reserveCheck.ReservePrice.ShouldBe(50m);

        var charged = events[2].Data.ShouldBeOfType<WinnerCharged>();
        charged.SettlementId.ShouldBe(settlementId);
        charged.WinnerId.ShouldBe(winnerId);
        charged.Amount.ShouldBe(85m);

        var feeCalculated = events[3].Data.ShouldBeOfType<FinalValueFeeCalculated>();
        feeCalculated.HammerPrice.ShouldBe(85m);
        feeCalculated.FeePercentage.ShouldBe(0.10m);
        feeCalculated.FeeAmount.ShouldBe(8.50m);
        feeCalculated.SellerPayout.ShouldBe(76.50m);

        var payoutIssued = events[4].Data.ShouldBeOfType<SellerPayoutIssued>();
        payoutIssued.SettlementId.ShouldBe(settlementId);
        payoutIssued.SellerId.ShouldBe(sellerId);
        payoutIssued.PayoutAmount.ShouldBe(76.50m);
        payoutIssued.FeeDeducted.ShouldBe(8.50m);

        var completed = events[5].Data.ShouldBeOfType<SettlementCompleted>();
        completed.SettlementId.ShouldBe(settlementId);
        completed.ListingId.ShouldBe(listingId);
        completed.WinnerId.ShouldBe(winnerId);
        completed.SellerId.ShouldBe(sellerId);
        completed.HammerPrice.ShouldBe(85m);
        completed.FeeAmount.ShouldBe(8.50m);
        completed.SellerPayout.ShouldBe(76.50m);

        // PendingSettlement transitions to Consumed via the M5-S3 PendingSettlementHandler
        // firing on SettlementCompleted from local in-process dispatch.
        var pending = await querySession.LoadAsync<PendingSettlement>(listingId);
        pending.ShouldNotBeNull();
        pending.Status.ShouldBe(PendingSettlementStatus.Consumed);

        // Wolverine removes the saga document at MarkCompleted() — same shape as
        // AuctionClosingSaga's terminal handler (M3-S5).
        var saga = await querySession.LoadAsync<SettlementSaga>(settlementId);
        saga.ShouldBeNull();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // §9.4 — PendingSettlement not found triggers retryable exception
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PendingSettlement_NotFound_ThrowsRetryableException()
    {
        var listingId = Guid.CreateVersion7();
        var listingSold = new ListingSold(
            ListingId: listingId,
            SellerId: Guid.CreateVersion7(),
            WinnerId: Guid.CreateVersion7(),
            HammerPrice: 85m,
            BidCount: 4,
            SoldAt: DateTimeOffset.UtcNow);

        // Direct-invoke the start handler with no PendingSettlement seed — assert that
        // the retryable exception is thrown. The Wolverine retry policy
        // (SettlementsConcurrencyRetryPolicies) catches this in production; the policy
        // is a Wolverine convention and is not re-asserted here. Trusting the framework
        // per the project's "no tests for framework idioms" stance.
        await using var session = _fixture.GetDocumentSession();

        var ex = await Should.ThrowAsync<PendingSettlementNotFoundException>(
            async () => await StartSettlementSagaHandler.Handle(listingSold, session, default));

        ex.ListingId.ShouldBe(listingId);
    }
}
