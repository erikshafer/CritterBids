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

    // ─── Scenario 4.3 ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompetingBid_NextBidExceedsMax_ProducesProxyBidExhausted()
    {
        var listingId = Guid.CreateVersion7();
        var proxyBidderId = Guid.CreateVersion7();
        var competingBidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, proxyBidderId);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);

        // Workshop 002 §4.3: MaxAmount $75. Competing bid arrives at $75 → next defensive
        // bid would be min($76, $75, $500) = $75, which is NOT strictly > $75 → exhaustion.
        await SeedProxySagaAsync(listingId, proxyBidderId, maxAmount: 75m);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new BidPlaced(
                ListingId: listingId,
                BidId: Guid.CreateVersion7(),
                BidderId: competingBidderId,
                Amount: 75m,
                BidCount: 1,
                IsProxy: false,
                PlacedAt: DateTimeOffset.UtcNow));

        // No PlaceBid emission — saga exhausted instead.
        tracked.Sent.MessagesOf<PlaceBid>().ShouldBeEmpty();

        // ProxyBidExhausted is bus-only per M4-S4 OQ2 (no AddEventType registration); the
        // contract's cross-BC consumer is post-M5 Relay, so the event lands in NoRoutes
        // until Relay subscribes. Same shape as ProxyBidRegistered at M4-S3.
        var exhausted = tracked.NoRoutes.MessagesOf<ProxyBidExhausted>().ShouldHaveSingleItem();
        exhausted.ListingId.ShouldBe(listingId);
        exhausted.BidderId.ShouldBe(proxyBidderId);
        exhausted.MaxAmount.ShouldBe(75m);

        // Saga document deleted by MarkCompleted.
        (await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId)).ShouldBeNull();
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

    // ─── Scenario 4.9 ────────────────────────────────────────────────────────

    [Fact]
    public async Task CompetingBidAtCeiling_ProducesProxyBidExhausted()
    {
        var listingId = Guid.CreateVersion7();
        var proxyBidderId = Guid.CreateVersion7();
        var competingBidderId = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, proxyBidderId);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);

        // Workshop 002 §4.9 corrected example: MaxAmount $300, BidderCreditCeiling $200,
        // competing bid $200. Next defensive bid = min($201, $300, $200) = $200, which is
        // NOT strictly > $200 → exhaustion. The credit ceiling caps the proxy even though
        // the per-listing MaxAmount has plenty of headroom — proves the three-way min.
        await SeedProxySagaAsync(
            listingId,
            proxyBidderId,
            maxAmount: 300m,
            bidderCreditCeiling: 200m);

        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new BidPlaced(
                ListingId: listingId,
                BidId: Guid.CreateVersion7(),
                BidderId: competingBidderId,
                Amount: 200m,
                BidCount: 4,
                IsProxy: false,
                PlacedAt: DateTimeOffset.UtcNow));

        tracked.Sent.MessagesOf<PlaceBid>().ShouldBeEmpty();

        var exhausted = tracked.NoRoutes.MessagesOf<ProxyBidExhausted>().ShouldHaveSingleItem();
        exhausted.ListingId.ShouldBe(listingId);
        exhausted.BidderId.ShouldBe(proxyBidderId);
        // MaxAmount on the event is the proxy's configured MaxAmount, NOT the credit ceiling
        // — per ProxyBidExhausted.cs payload, the event reports the user's intended ceiling.
        // Relay's "your proxy has been exceeded" notification renders the original cap.
        exhausted.MaxAmount.ShouldBe(300m);

        (await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId)).ShouldBeNull();
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

    // ─── Scenario 4.10 ───────────────────────────────────────────────────────

    [Fact]
    public async Task TwoProxies_WeakerExhausts_StrongerWins()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var participant002 = Guid.CreateVersion7();
        var participant003 = Guid.CreateVersion7();
        var saga002Id = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, participant002);
        var saga003Id = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, participant003);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        // Listing stream + closing saga: PlaceBidHandler validates each cascaded PlaceBid
        // against the DCB state on the listing stream. Without the BiddingOpened seed each
        // PlaceBid is rejected and the cascade stops at step one. AuctionClosingSaga.Handle
        // runs for every BidPlaced — needs an existing saga doc, otherwise UnknownSagaException
        // halts the within-BC fan-out.
        await _fixture.SeedListingStreamAsync(listingId, sellerId, closeAt, startingBid: 25m);
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt);

        // Workshop 002 §4.10 seed: proxy-002 MaxAmount $50 + ceiling $500; proxy-003
        // MaxAmount $45 + ceiling $200. Escalation runs until proxy-003 attempts
        // min($47, $45, $200) = $45 against competing $46 → not > $46 → exhausts.
        // Final: proxy-002 wins at $46, proxy-003 exhausted.
        await SeedProxySagaAsync(listingId, participant002,
            maxAmount: 50m, bidderCreditCeiling: 500m);
        await SeedProxySagaAsync(listingId, participant003,
            maxAmount: 45m, bidderCreditCeiling: 200m);

        // proxy-003 fires first against participant-002's notional high — the trigger event.
        // The cascade then alternates: saga-002 reacts to $31 → PlaceBid $32 →
        // PlaceBidHandler appends BidPlaced $32 → forwarded → saga-003 reacts → ...
        // M4-S4 OQ7 first-run signal: SendMessageAndWaitAsync's TrackedSession waits for
        // every queued envelope (including the cascade's recursive PlaceBid/BidPlaced
        // fan-outs), so the whole bidding war completes before this await returns.
        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(30))
            .SendMessageAndWaitAsync(new BidPlaced(
                ListingId: listingId,
                BidId: Guid.CreateVersion7(),
                BidderId: participant003,
                Amount: 31m,
                BidCount: 1,
                IsProxy: true,
                PlacedAt: DateTimeOffset.UtcNow));

        // Final state:
        //  - proxy-003 exhausted → saga doc deleted by MarkCompleted
        //  - proxy-002 still Active (the winner; its $46 was its own-bid tracking emission)
        (await _fixture.LoadSaga<ProxyBidManagerSaga>(saga003Id)).ShouldBeNull();

        var saga002 = await _fixture.LoadSaga<ProxyBidManagerSaga>(saga002Id);
        saga002.ShouldNotBeNull();
        saga002!.Status.ShouldBe(ProxyBidManagerStatus.Active);

        var exhausted = tracked.NoRoutes.MessagesOf<ProxyBidExhausted>().ShouldHaveSingleItem();
        exhausted.BidderId.ShouldBe(participant003);
        exhausted.MaxAmount.ShouldBe(45m);
    }

    // ─── Scenario 4.11 ───────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterProxy_WhileOutbid_WaitsForNextCompetingBid()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var participant002 = Guid.CreateVersion7();
        var participant003 = Guid.CreateVersion7();
        var sagaId = AuctionsIdentityHelpers.ProxyBidManagerSagaId(listingId, participant002);
        var closeAt = DateTimeOffset.UtcNow.AddHours(1);

        // Workshop 002 §4.11: participant-003 holds the high at $40 before participant-002
        // registers a proxy. The proxy is reactive only — registration alone does NOT cause
        // an immediate counter-bid. The proxy waits for the NEXT BidPlaced from someone
        // other than participant-002 to trigger.
        await _fixture.SeedListingStreamAsync(listingId, sellerId, closeAt, startingBid: 25m);
        await _fixture.SeedAuctionClosingSagaAsync(
            listingId,
            status: AuctionClosingStatus.Active,
            scheduledCloseAt: closeAt,
            originalCloseAt: closeAt,
            bidCount: 1,
            currentHighBid: 40m,
            currentHighBidderId: participant003);

        // Seed the credit ceiling so the start handler doesn't throw the not-found exception.
        await _fixture.SeedParticipantCreditCeilingAsync(participant002, creditCeiling: 500m);

        // RegisterProxyBid is single-handler — Invoke is correct here (not Send).
        var tracked = await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(new RegisterProxyBid(
                ListingId: listingId,
                BidderId: participant002,
                MaxAmount: 60m));

        // Workshop assertion: ProxyBidRegistered emitted, saga created at Active, but NO
        // PlaceBid emission — the proxy is reactive and does not retroactively counter the
        // existing high bid from before registration.
        var registered = tracked.NoRoutes.MessagesOf<ProxyBidRegistered>().ShouldHaveSingleItem();
        registered.BidderId.ShouldBe(participant002);

        tracked.Sent.MessagesOf<PlaceBid>().ShouldBeEmpty();
        tracked.NoRoutes.MessagesOf<PlaceBid>().ShouldBeEmpty();

        var saga = await _fixture.LoadSaga<ProxyBidManagerSaga>(sagaId);
        saga.ShouldNotBeNull();
        saga!.Status.ShouldBe(ProxyBidManagerStatus.Active);
        saga.MaxAmount.ShouldBe(60m);
        saga.BidderCreditCeiling.ShouldBe(500m);
        saga.LastBidAmount.ShouldBe(0m);
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
