using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Selling;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Integration tests for the Proxy Bid Manager saga's S3 scope (Workshop 002 §4.1 / §4.2 /
/// §4.4 / §4.5). Method names per <c>docs/milestones/M4-auctions-bc-completion.md</c> §7
/// §4.
///
/// <para><b>Correlation (M4-S3 OQ1 Path C).</b> The saga's reactive <c>Handle</c> takes
/// <see cref="ProxyBidObserved"/>, not <see cref="BidPlaced"/> directly — the composite-key
/// id (<c>UuidV5(ns, $"{ListingId}:{BidderId}")</c>) is not on the contract, so
/// <see cref="ProxyBidDispatchHandler"/> bridges by querying active sagas and emitting one
/// wrapped command per match. Tests dispatch <see cref="BidPlaced"/> and assert on the
/// final saga state and emitted <see cref="PlaceBid"/>; the dispatcher hop is invisible to
/// the assertion shape but exercised end-to-end.</para>
///
/// <para><b>Seeding an AuctionClosingSaga alongside the proxy saga.</b>
/// <see cref="AuctionClosingSaga.Handle(BidPlaced)"/> has no <c>NotFound(BidPlaced)</c>
/// static absorber (it is M4-S3 acceptance criterion that <c>AuctionClosingSaga.cs</c>
/// stay byte-unchanged), so dispatching a <see cref="BidPlaced"/> without an existing
/// auction-closing saga would surface <c>UnknownSagaException</c>. The competing-bid /
/// own-bid scenarios therefore seed a paired auction-closing saga via the fixture helper —
/// the same shape <see cref="PlaceBidDispatchTests"/> uses for the same reason.</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class ProxyBidManagerSagaTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public ProxyBidManagerSagaTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Scenario 4.1 ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterProxyBid_StartsSaga_ProducesProxyBidRegistered()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var expectedSagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);

        // M4-S4: seed the ParticipantCreditCeiling projection row that
        // StartProxyBidManagerSagaHandler reads at saga-start. Without this seed the
        // handler throws ParticipantCreditCeilingNotFoundException after retries exhaust.
        await _fixture.SeedParticipantCreditCeilingAsync(bidderId, creditCeiling: 500m);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new RegisterProxyBid(
                ListingId: listingId,
                BidderId: bidderId,
                MaxAmount: 75m));

        var saga = await _fixture.LoadSaga<ProxyBidManagerSaga>(expectedSagaId);
        saga.ShouldNotBeNull();
        saga!.Id.ShouldBe(expectedSagaId);
        saga.ListingId.ShouldBe(listingId);
        saga.BidderId.ShouldBe(bidderId);
        saga.MaxAmount.ShouldBe(75m);
        saga.Status.ShouldBe(ProxyBidManagerStatus.Active);
        saga.LastBidAmount.ShouldBe(0m);
        // M4-S4: credit ceiling now populated from the projection seeded above (S3 OQ4
        // Path c resolved). The S3-era zero default was the placeholder before the
        // projection landed.
        saga.BidderCreditCeiling.ShouldBe(500m);

        // ProxyBidRegistered is emitted via OutgoingMessages with no cross-BC consumer wired
        // — Relay (post-M5) is the only known consumer per the contract's docstring. Lands in
        // tracked.NoRoutes per the M5-S6 fixture-stance pattern.
        var registered = tracked.NoRoutes.MessagesOf<ProxyBidRegistered>().ShouldHaveSingleItem();
        registered.ListingId.ShouldBe(listingId);
        registered.BidderId.ShouldBe(bidderId);
        registered.MaxAmount.ShouldBe(75m);
    }

    // ─── Scenario 4.2 ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompetingBid_ProxyAutoBidsOneIncrementAbove()
    {
        var listingId = Guid.CreateVersion7();
        var proxyBidderId = Guid.CreateVersion7();
        var competingBidderId = Guid.CreateVersion7();
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);
        await SeedProxySagaAsync(listingId, proxyBidderId, maxAmount: 75m);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new BidPlaced(
                ListingId: listingId,
                BidId: Guid.CreateVersion7(),
                BidderId: competingBidderId,
                Amount: 45m,
                BidCount: 1,
                IsProxy: false,
                PlacedAt: DateTimeOffset.UtcNow));

        // Workshop 002 §4.2 — competing $45 + $1 increment = $46 (≤ max $75) → one PlaceBid
        // emission at $46 from the saga's BidderId. PlaceBidHandler is a local handler in
        // the same BC, so the emission lands in tracked.Sent. Filter by BidderId because
        // PlaceBidHandler's own appended BidPlaced (if it ran successfully) would also be
        // observed; in this fixture no listing stream is seeded, so PlaceBidHandler will
        // reject with BidRejected — the assertion is robust either way.
        var auto = tracked.Sent.MessagesOf<PlaceBid>().ShouldHaveSingleItem();
        auto.ListingId.ShouldBe(listingId);
        auto.BidderId.ShouldBe(proxyBidderId);
        auto.Amount.ShouldBe(46m);
    }

    // ─── Scenario 4.4 ────────────────────────────────────────────────────────

    [Fact]
    public async Task OwnProxyBid_TracksNoReact()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);
        await SeedProxySagaAsync(listingId, bidderId, maxAmount: 75m);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new BidPlaced(
                ListingId: listingId,
                BidId: Guid.CreateVersion7(),
                BidderId: bidderId,
                Amount: 46m,
                BidCount: 1,
                IsProxy: true,
                PlacedAt: DateTimeOffset.UtcNow));

        // No auto-bid — the inbound bid is the proxy's own emission per workshop §4.4. Saga
        // stays Active; LastBidAmount = inbound amount.
        tracked.Sent.MessagesOf<PlaceBid>().ShouldBeEmpty();

        var saga = await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(ProxyBidManagerStatus.Active);
        saga.LastBidAmount.ShouldBe(46m);
    }

    // ─── Scenario 4.5 ────────────────────────────────────────────────────────

    [Fact]
    public async Task OwnManualBid_TracksNoReact()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);
        await SeedProxySagaAsync(listingId, bidderId, maxAmount: 75m);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new BidPlaced(
                ListingId: listingId,
                BidId: Guid.CreateVersion7(),
                BidderId: bidderId,
                Amount: 50m,
                BidCount: 1,
                IsProxy: false,
                PlacedAt: DateTimeOffset.UtcNow));

        // Same shape as §4.4 — proxy stays Active and tracks regardless of IsProxy flag.
        // The two tests differ only in IsProxy and amount; identical outcomes prove the
        // saga does not branch on IsProxy (the same-bidder check is the load-bearing one).
        tracked.Sent.MessagesOf<PlaceBid>().ShouldBeEmpty();

        var saga = await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(ProxyBidManagerStatus.Active);
        saga.LastBidAmount.ShouldBe(50m);
    }

    // ─── Scenario 4.6 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingSold_CompletesSaga()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);

        await SeedProxySagaAsync(listingId, bidderId, maxAmount: 75m);

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new ListingSold(
                ListingId: listingId,
                SellerId: Guid.CreateVersion7(),
                WinnerId: Guid.CreateVersion7(),
                HammerPrice: 75m,
                BidCount: 5,
                SoldAt: DateTimeOffset.UtcNow));

        // Saga document deleted by MarkCompleted (Wolverine.Saga.cs MarkCompleted contract).
        // The terminal handler sets Status = ListingClosed; MarkCompleted() then deletes the
        // document, so LoadAsync returns null. Mirrors AuctionClosingSaga's terminal shape.
        (await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId)).ShouldBeNull();
    }

    // ─── Scenario 4.7 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingPassed_CompletesSaga()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);

        await SeedProxySagaAsync(listingId, bidderId, maxAmount: 75m);

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new ListingPassed(
                ListingId: listingId,
                Reason: "ReserveNotMet",
                HighestBid: 40m,
                BidCount: 3,
                PassedAt: DateTimeOffset.UtcNow));

        (await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId)).ShouldBeNull();
    }

    // ─── Scenario 4.8 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListingWithdrawn_CompletesSaga()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        // The Auctions BC has two ListingWithdrawn handlers (AuctionClosingSaga +
        // ProxyBidDispatchHandler). The AuctionClosingSaga path requires an existing saga
        // doc — seed one alongside the proxy so the within-BC fan-out doesn't surface
        // UnknownSagaException from the closing saga's missing-saga branch.
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);
        await SeedProxySagaAsync(listingId, bidderId, maxAmount: 75m);

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new ListingWithdrawn(
                ListingId: listingId,
                WithdrawnBy: Guid.NewGuid(),
                Reason: null,
                WithdrawnAt: DateTimeOffset.UtcNow));

        (await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId)).ShouldBeNull();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task SeedProxySagaAsync(
        Guid listingId,
        Guid bidderId,
        decimal maxAmount,
        decimal bidderCreditCeiling = 500m)
    {
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, bidderId);
        await using var session = _fixture.GetDocumentSession();
        session.Store(new ProxyBidManagerSaga
        {
            Id = sagaId,
            ListingId = listingId,
            BidderId = bidderId,
            MaxAmount = maxAmount,
            // M4-S4: scenarios that don't exercise the credit-ceiling cap inherit the
            // workshop default ($500 — participant-002's ceiling per Workshop 002 setup).
            // Scenario 4.9 overrides to $200 to drive the cap-triggered exhaustion path.
            BidderCreditCeiling = bidderCreditCeiling,
            LastBidAmount = 0m,
            Status = ProxyBidManagerStatus.Active,
        });
        await session.SaveChangesAsync();
    }
}
