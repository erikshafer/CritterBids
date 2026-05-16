using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using CritterBids.Settlement.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Settlement.Tests;

[Collection(SettlementTestCollection.Name)]
public class SettlementSagaBinSourceTests : IAsyncLifetime
{
    private readonly SettlementTestFixture _fixture;

    public SettlementSagaBinSourceTests(SettlementTestFixture fixture)
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
    // §9.2 — Full BIN-source happy path (five-event stream)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Full_BuyItNowSource_HappyPath_ProducesFiveEventStream()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var buyerId   = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);

        // Seed PendingSettlement the same way as the §9.1 bidding test — the BIN handler
        // loads the same projection for FeePercentage and SellerId.
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

        // Dispatch BuyItNowPurchased — the saga starts at Status: ReserveChecked, skips
        // the reserve-check phase, walks four continuation phases (ChargeWinner →
        // CalculateFee → IssueSellerPayout → CompleteSettlement), and reaches MarkCompleted.
        var binPurchased = new BuyItNowPurchased(
            ListingId: listingId,
            BuyerId: buyerId,
            Price: 100m,
            PurchasedAt: DateTimeOffset.UtcNow);

        await _fixture.Host.InvokeMessageAndWaitAsync(binPurchased);

        var settlementId = SettlementsIdentityNamespaces.SettlementId(listingId);

        await using var querySession = _fixture.GetDocumentSession();
        var events = await querySession.Events.FetchStreamAsync(settlementId);

        // Exactly five events per §9.2: no ReserveCheckCompleted. The absence is the
        // canonical audit signal — "show me all BIN settlements" is literally event streams
        // where no ReserveCheckCompleted appears.
        events.Count.ShouldBe(5);

        var initiated = events[0].Data.ShouldBeOfType<SettlementInitiated>();
        initiated.Source.ShouldBe(SettlementSource.BuyItNow);
        initiated.Price.ShouldBe(100m);
        initiated.WinnerId.ShouldBe(buyerId);
        initiated.SellerId.ShouldBe(sellerId);

        // Position 1 must be WinnerCharged (not ReserveCheckCompleted) — this is the
        // canonical absence assertion. If a future regression appended a ReserveCheckCompleted
        // to the BIN-source stream, this assertion would fail before the count assertion fired.
        var charged = events[1].Data.ShouldBeOfType<WinnerCharged>();
        charged.SettlementId.ShouldBe(settlementId);
        charged.WinnerId.ShouldBe(buyerId);
        charged.Amount.ShouldBe(100m);

        var feeCalculated = events[2].Data.ShouldBeOfType<FinalValueFeeCalculated>();
        feeCalculated.HammerPrice.ShouldBe(100m);
        feeCalculated.FeePercentage.ShouldBe(0.10m);
        feeCalculated.FeeAmount.ShouldBe(10m);
        feeCalculated.SellerPayout.ShouldBe(90m);

        var payoutIssued = events[3].Data.ShouldBeOfType<SellerPayoutIssued>();
        payoutIssued.PayoutAmount.ShouldBe(90m);
        payoutIssued.FeeDeducted.ShouldBe(10m);

        var completed = events[4].Data.ShouldBeOfType<SettlementCompleted>();
        completed.HammerPrice.ShouldBe(100m);
        completed.FeeAmount.ShouldBe(10m);
        completed.SellerPayout.ShouldBe(90m);

        // Belt-and-suspenders: no event in the stream is a ReserveCheckCompleted.
        events.ShouldNotContain(e => e.Data is ReserveCheckCompleted);

        // PendingSettlement transitions to Consumed via SettlementCompleted local dispatch.
        var pending = await querySession.LoadAsync<PendingSettlement>(listingId);
        pending.ShouldNotBeNull();
        pending.Status.ShouldBe(PendingSettlementStatus.Consumed);

        var saga = await querySession.LoadAsync<SettlementSaga>(settlementId);
        saga.ShouldBeNull();
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Deterministic SettlementId — same id regardless of source
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BinSource_DeterministicSettlementId_MatchesBiddingSourceDerivation()
    {
        // Structural contract: the deterministic UUID v5 SettlementId for a given ListingId
        // is identical regardless of whether the source is ListingSold (bidding) or
        // BuyItNowPurchased (BIN). Per W003 Phase 1 Part 6 — the namespace constant is
        // SettlementSaga, the name input is "settlement:{listingId}", neither carries source
        // information. The pure-function helper guarantees this structurally; this test
        // documents the contract for future readers and catches any regression where source
        // information accidentally leaks into the id derivation.
        var listingId = Guid.CreateVersion7();

        var fromBidding = SettlementsIdentityNamespaces.SettlementId(listingId);
        var fromBin     = SettlementsIdentityNamespaces.SettlementId(listingId);

        fromBidding.ShouldBe(fromBin);
        fromBidding.ShouldNotBe(Guid.Empty);
    }
}
