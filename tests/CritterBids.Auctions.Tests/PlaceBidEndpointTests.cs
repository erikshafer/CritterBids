using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using JasperFx.Events;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// HTTP-level tests for the M8-S3a bid placement endpoint (<c>POST /api/auctions/bids</c>) —
/// the single sanctioned M8 backend exception. Covers the acceptance path, each rejection reason
/// (with its accept-vs-reject HTTP status + machine-readable ProblemDetails reason), the
/// server-side credit-ceiling sourcing (a client cannot supply an inflated ceiling), the
/// unknown-bidder precondition (404, no audit), and the preservation of the DCB
/// optimistic-concurrency guarantee through the shared <see cref="PlaceBidHandler.Execute"/> core.
///
/// The endpoint adds no new domain capability — the decision, the rejection rules, and the
/// increment policy live in <see cref="PlaceBidHandler"/> and are exercised at the bus level by
/// <see cref="PlaceBidHandlerTests"/>; these tests prove the HTTP contract over them.
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class PlaceBidEndpointTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public PlaceBidEndpointTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── Acceptance ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceBid_Accepted_Returns200_WithReconciliationBody_AndAppendsBidPlaced()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListingWithSaga(listingId, sellerId, startingBid: 25m, close: now.AddMinutes(5));
        await _fixture.SeedParticipantCreditCeilingAsync(bidderId, creditCeiling: 500m);

        var (_, result) = await _fixture.TrackedHttpCall(s =>
        {
            s.Post.Json(new PlaceBidRequest(listingId, bidderId, 30m)).ToUrl("/api/auctions/bids");
            s.StatusCodeShouldBe(200);
        });

        var body = await result.ReadAsJsonAsync<PlaceBidResponse>();
        body.ShouldNotBeNull();
        body!.BidId.ShouldNotBe(Guid.Empty);
        body.ListingId.ShouldBe(listingId);
        body.BidderId.ShouldBe(bidderId);
        body.Amount.ShouldBe(30m);
        body.BidCount.ShouldBe(1);
        body.CurrentHighBid.ShouldBe(30m);
        body.ReserveMet.ShouldBeTrue();          // no reserve to clear
        body.ExtendedBidding.ShouldBeNull();

        var placed = (await LoadListingEvents(listingId)).OfType<BidPlaced>().ShouldHaveSingleItem();
        placed.Amount.ShouldBe(30m);
        placed.BidCount.ShouldBe(1);
    }

    // ─── Rejections: status mapping + machine-readable ProblemDetails reason ───

    [Fact]
    public async Task PlaceBid_BelowMinimum_Returns400_BelowMinimumBid()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, close: now.AddMinutes(5));
        await _fixture.SeedParticipantCreditCeilingAsync(bidderId, creditCeiling: 500m);

        var body = await PostExpectingProblem(new PlaceBidRequest(listingId, bidderId, 20m), expectedStatus: 400);
        body.ShouldContain("BelowMinimumBid");

        var rejection = await LoadSingleRejection(listingId);
        rejection.Reason.ShouldBe("BelowMinimumBid");
    }

    [Fact]
    public async Task PlaceBid_ExceedsServerCreditCeiling_Returns400_AndClientCannotInflateIt()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, close: now.AddMinutes(5));
        // Server-side ceiling is 200; the bid is 250.
        await _fixture.SeedParticipantCreditCeilingAsync(bidderId, creditCeiling: 200m);

        // The request body carries an extra, inflated "creditCeiling" — the endpoint must IGNORE it
        // and source the ceiling server-side, so the bid still exceeds the real 200 ceiling.
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(new { listingId, bidderId, amount = 250m, creditCeiling = 100_000m })
                .ToUrl("/api/auctions/bids");
            s.StatusCodeShouldBe(400);
        });

        var body = await result.ReadAsTextAsync();
        body.ShouldContain("ExceedsCreditCeiling");

        var rejection = await LoadSingleRejection(listingId);
        rejection.Reason.ShouldBe("ExceedsCreditCeiling");
        rejection.AttemptedAmount.ShouldBe(250m);
    }

    [Fact]
    public async Task PlaceBid_SellerBidsOnOwnListing_Returns400_SellerCannotBid()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, close: now.AddMinutes(5));
        await _fixture.SeedParticipantCreditCeilingAsync(sellerId, creditCeiling: 500m);

        var body = await PostExpectingProblem(new PlaceBidRequest(listingId, sellerId, 30m), expectedStatus: 400);
        body.ShouldContain("SellerCannotBid");
    }

    [Fact]
    public async Task PlaceBid_OnClosedListing_Returns409_ListingClosed()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var pastClose = DateTimeOffset.UtcNow.AddMinutes(-1);

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, close: pastClose);
        await _fixture.SeedParticipantCreditCeilingAsync(bidderId, creditCeiling: 500m);

        var body = await PostExpectingProblem(new PlaceBidRequest(listingId, bidderId, 100m), expectedStatus: 409);
        body.ShouldContain("ListingClosed");
    }

    [Fact]
    public async Task PlaceBid_OnUnopenedListing_Returns409_ListingNotOpen()
    {
        var listingId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();

        // No listing seeded — the boundary has no BiddingOpened, state.ListingId stays default.
        await _fixture.SeedParticipantCreditCeilingAsync(bidderId, creditCeiling: 500m);

        var body = await PostExpectingProblem(new PlaceBidRequest(listingId, bidderId, 30m), expectedStatus: 409);
        body.ShouldContain("ListingNotOpen");
    }

    // ─── Unknown bidder: HTTP precondition failure, OUTSIDE the domain decision ─

    [Fact]
    public async Task PlaceBid_WithNoCreditCeilingOnFile_Returns404_UnknownBidder_AndWritesNoAudit()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var unknownBidderId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, close: now.AddMinutes(5));
        // Intentionally NO SeedParticipantCreditCeilingAsync for unknownBidderId.

        var body = await PostExpectingProblem(new PlaceBidRequest(listingId, unknownBidderId, 30m), expectedStatus: 404);
        body.ShouldContain("UnknownBidder");

        // No domain decision ran, so no BidRejected audit was written.
        (await LoadRejections(listingId)).ShouldBeEmpty();
        // And no acceptance event leaked onto the listing stream.
        (await LoadListingEvents(listingId)).OfType<BidPlaced>().ShouldBeEmpty();
    }

    // ─── DCB optimistic-concurrency guarantee preserved through Execute ────────

    [Fact]
    public async Task TwoConcurrentBids_OverTheSharedExecuteCore_SecondCommitThrowsDcbConcurrency()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderA = Guid.CreateVersion7();
        var bidderB = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, close: now.AddMinutes(5));

        // Two sessions both fetch the SAME boundary version, then both append a bid. The shared
        // Execute core queues AssertDcbConsistency on each session (via FetchForWritingByTags) —
        // exactly as the HTTP endpoint does. The first commit wins; the second sees the boundary
        // moved and faults, proving the optimistic-concurrency guarantee survives the new path.
        await using var sessionA = _fixture.GetDocumentSession();
        await PlaceBidHandler.Execute(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderA, 30m, CreditCeiling: 500m),
            sessionA, TimeProvider.System);

        await using var sessionB = _fixture.GetDocumentSession();
        await PlaceBidHandler.Execute(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderB, 31m, CreditCeiling: 500m),
            sessionB, TimeProvider.System);

        await sessionA.SaveChangesAsync();
        await Should.ThrowAsync<DcbConcurrencyException>(async () => await sessionB.SaveChangesAsync());
    }

    // ─── Accepted-outcome detail (reserve + extended bidding) via Execute ──────
    // Asserted at the Execute layer because the HTTP pipeline resolves TimeProvider.System and
    // cannot be pinned per-test to a moment inside the extended-bidding trigger window.

    [Fact]
    public async Task Execute_BidCrossingReserveInsideTriggerWindow_ReportsReserveMetAndExtension()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var bidderId = Guid.CreateVersion7();
        var anchor = DateTimeOffset.UtcNow;
        var close = anchor.AddMinutes(5);
        var bidMoment = anchor.AddMinutes(4).AddSeconds(40); // 20s before close, inside a 30s window

        await SeedOpenListing(listingId, sellerId, startingBid: 25m, reserve: 50m, close: close,
            extendedEnabled: true,
            triggerWindow: TimeSpan.FromSeconds(30),
            extension: TimeSpan.FromSeconds(15),
            maxDuration: TimeSpan.FromMinutes(5));
        await SeedBidPlaced(listingId, amount: 40m, bidCount: 1); // reserve (50) not yet met

        await using var session = _fixture.GetDocumentSession();
        var outcome = await PlaceBidHandler.Execute(
            new PlaceBid(listingId, Guid.CreateVersion7(), bidderId, 55m, CreditCeiling: 500m),
            session, new FixedTimeProvider(bidMoment));
        await session.SaveChangesAsync();

        var accepted = outcome.ShouldBeOfType<BidOutcome.Accepted>();
        accepted.Amount.ShouldBe(55m);
        accepted.BidCount.ShouldBe(2);
        accepted.CurrentHighBid.ShouldBe(55m);
        accepted.ReserveMet.ShouldBeTrue();
        accepted.ExtendedBidding.ShouldNotBeNull();
        accepted.ExtendedBidding!.PreviousCloseAt.ShouldBe(close);
        accepted.ExtendedBidding.NewCloseAt.ShouldBe(close.AddSeconds(15)); // PreviousCloseAt + extension
    }

    // ─── Seeding + query helpers ──────────────────────────────────────────────

    private Task SeedOpenListing(
        Guid listingId, Guid sellerId, decimal startingBid, DateTimeOffset close,
        decimal? reserve = null,
        bool extendedEnabled = false,
        TimeSpan? triggerWindow = null,
        TimeSpan? extension = null,
        TimeSpan? maxDuration = null)
        => SeedBiddingOpened(listingId, sellerId, startingBid, close, reserve,
            extendedEnabled, triggerWindow, extension, maxDuration);

    /// <summary>
    /// Seed an open listing AND start its Auction Closing saga through the bus — required for the
    /// acceptance path, where the appended <c>BidPlaced</c> is forwarded in-process to the saga
    /// (UseFastEventForwarding). Seeding the saga via a raw store would leave its numeric revision
    /// inconsistent and the forwarded BidPlaced would throw a ConcurrencyException (the lesson
    /// baked into <see cref="PlaceBidDispatchTests"/>).
    /// </summary>
    private async Task SeedOpenListingWithSaga(Guid listingId, Guid sellerId, decimal startingBid, DateTimeOffset close)
    {
        // One BiddingOpened instance both seeds the stream and starts the saga, mirroring
        // PlaceBidDispatchTests.SeedOpenListing — invoking through the bus routes to
        // StartAuctionClosingSagaHandler (which creates the saga doc with a consistent revision)
        // rather than appending another stream event.
        var opened = BuildBiddingOpened(listingId, sellerId, startingBid, close);
        await SeedBiddingOpened(listingId, opened);
        await _fixture.Host.InvokeMessageAndWaitAsync(opened);
    }

    private async Task SeedBiddingOpened(
        Guid listingId, Guid sellerId, decimal startingBid, DateTimeOffset close,
        decimal? reserve = null,
        bool extendedEnabled = false,
        TimeSpan? triggerWindow = null,
        TimeSpan? extension = null,
        TimeSpan? maxDuration = null)
        => await SeedBiddingOpened(listingId, BuildBiddingOpened(
            listingId, sellerId, startingBid, close, reserve,
            extendedEnabled, triggerWindow, extension, maxDuration));

    private async Task SeedBiddingOpened(Guid listingId, BiddingOpened opened)
    {
        await using var session = _fixture.GetDocumentSession();
        session.Events.StartStream<Listing>(listingId, opened);
        session.PendingChanges.Streams().Single().Events.Single().AddTag(new ListingStreamId(listingId));
        await session.SaveChangesAsync();
    }

    private static BiddingOpened BuildBiddingOpened(
        Guid listingId, Guid sellerId, decimal startingBid, DateTimeOffset close,
        decimal? reserve = null,
        bool extendedEnabled = false,
        TimeSpan? triggerWindow = null,
        TimeSpan? extension = null,
        TimeSpan? maxDuration = null)
        => new(
            ListingId: listingId,
            SellerId: sellerId,
            StartingBid: startingBid,
            ReserveThreshold: reserve,
            BuyItNowPrice: null,
            ScheduledCloseAt: close,
            ExtendedBiddingEnabled: extendedEnabled,
            ExtendedBiddingTriggerWindow: triggerWindow,
            ExtendedBiddingExtension: extension,
            MaxDuration: maxDuration ?? TimeSpan.FromMinutes(5),
            OpenedAt: DateTimeOffset.UtcNow);

    private async Task SeedBidPlaced(Guid listingId, decimal amount, int bidCount)
    {
        await using var session = _fixture.GetDocumentSession();
        var placed = new BidPlaced(
            ListingId: listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: Guid.CreateVersion7(),
            Amount: amount,
            BidCount: bidCount,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow);
        var wrapped = session.Events.BuildEvent(placed);
        wrapped.AddTag(new ListingStreamId(listingId));
        session.Events.Append(listingId, wrapped);
        await session.SaveChangesAsync();
    }

    private async Task<string> PostExpectingProblem(PlaceBidRequest request, int expectedStatus)
    {
        var result = await _fixture.Host.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/api/auctions/bids");
            s.StatusCodeShouldBe(expectedStatus);
        });
        return await result.ReadAsTextAsync();
    }

    private async Task<IReadOnlyList<object>> LoadListingEvents(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var events = await session.Events.FetchStreamAsync(listingId);
        return events.Select(e => e.Data).ToList();
    }

    private async Task<IReadOnlyList<BidRejected>> LoadRejections(Guid listingId)
    {
        await using var session = _fixture.GetDocumentSession();
        var streamKey = BidRejectionAudit.StreamKey(listingId);
        var events = await session.Events.FetchStreamAsync(streamKey);
        return events.Select(e => e.Data).OfType<BidRejected>().ToList();
    }

    private async Task<BidRejected> LoadSingleRejection(Guid listingId)
        => (await LoadRejections(listingId)).ShouldHaveSingleItem();
}
