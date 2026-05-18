using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using CritterBids.Settlement.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Settlement.Tests;

/// <summary>
/// Integration test for the M5-S6 SellerPayoutIssued cross-BC publish route. Verifies
/// that the saga's terminal-phase emission is dispatched out of the bounded context
/// (i.e., not consumed by any local Settlement-side handler) and carries the
/// W003-canonical payload.
///
/// <para><b>Why <c>tracked.NoRoutes</c> rather than <c>tracked.Sent</c>.</b> The
/// Settlement test fixture calls <c>DisableAllExternalWolverineTransports()</c>, which
/// strips the RabbitMQ publish route wired in <c>Program.cs</c> entirely (not stubs it).
/// Messages routed to external transports therefore land in <c>tracked.NoRoutes</c> in
/// tests even when the production route IS wired. This matches the M3-S5b
/// AuctionClosingSagaTests pattern (e.g., line 136: <c>tracked.NoRoutes.MessagesOf&lt;
/// BiddingClosed&gt;().ShouldHaveSingleItem()</c>). The Participants fixture takes a
/// different approach — it adds a stub local-queue route via WolverineExtension so
/// <c>tracked.Sent</c> surfaces the message — but that approach masks any production
/// route mis-wiring. The NoRoutes pattern asserts the saga's emission contract; the
/// production route wiring at <c>Program.cs</c>'s
/// <c>opts.PublishMessage&lt;SellerPayoutIssued&gt;().ToRabbitQueue("relay-settlement-events")</c>
/// is asserted by code review.</para>
///
/// <para><b>The relay-settlement-events queue has no consumer at M5 close.</b> Relay BC
/// is post-M5. The route is wired structurally per the M5-S5 retro §"What M5-S6 should
/// know" item #1 (queue-topology completeness). When Relay ships, its consumer drains
/// the queue without requiring any Settlement-side change.</para>
/// </summary>
[Collection(SettlementTestCollection.Name)]
public class SellerPayoutIssuedPublishRouteTests : IAsyncLifetime
{
    private readonly SettlementTestFixture _fixture;

    public SellerPayoutIssuedPublishRouteTests(SettlementTestFixture fixture)
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

    [Fact]
    public async Task SellerPayoutIssued_IsDispatchedAsCrossBcMessage_WithCanonicalPayload()
    {
        // Arrange — reproduce the §9.1 bidding-source happy-path seed so the saga runs
        // all five phases and emits SellerPayoutIssued from the IssueSellerPayout handler.
        var listingId = Guid.CreateVersion7();
        var sellerId  = Guid.CreateVersion7();
        var winnerId  = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow.AddDays(-1);
        var soldAt = DateTimeOffset.UtcNow;

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

        var listingSold = new ListingSold(
            ListingId: listingId,
            SellerId: sellerId,
            WinnerId: winnerId,
            HammerPrice: 85m,
            BidCount: 12,
            SoldAt: soldAt);

        // Act — the saga walks five phases; SellerPayoutIssued is emitted from the
        // IssueSellerPayout handler with the W003-canonical 0.10 fee on an 85m hammer.
        var tracked = await _fixture.Host.InvokeMessageAndWaitAsync(listingSold);

        // Assert — SellerPayoutIssued landed in NoRoutes (see class docstring for the
        // DisableAllExternalWolverineTransports rationale). Exactly one was emitted —
        // no Settlement-side handler consumed it, no duplicate dispatch.
        var settlementId = SettlementsIdentityNamespaces.SettlementId(listingId);

        var payoutEvents = tracked.NoRoutes.MessagesOf<SellerPayoutIssued>().ToList();
        payoutEvents.ShouldHaveSingleItem();
        payoutEvents[0].SettlementId.ShouldBe(settlementId);
        payoutEvents[0].SellerId.ShouldBe(sellerId);
        payoutEvents[0].PayoutAmount.ShouldBe(76.50m);
        payoutEvents[0].FeeDeducted.ShouldBe(8.50m);
    }
}
