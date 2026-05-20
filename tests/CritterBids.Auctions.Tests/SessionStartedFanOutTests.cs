using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine.Tracking;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Integration tests for the M4-S5 Flash-session fan-out (<see cref="SessionStartedHandler"/>).
/// Method names per <c>docs/milestones/M4-auctions-bc-completion.md</c> §7 §5.
///
/// <para><b>Dispatch shape.</b> <c>SendMessageAndWaitAsync</c> per the M4-S5 prompt's
/// "Both tests dispatch via SendMessageAndWaitAsync" directive. The actual in-Auctions
/// handler count for <see cref="SessionStarted"/> is one (the fan-out;
/// <see cref="Session"/>'s Marten <c>Apply</c> is a projection, not a Wolverine handler),
/// so <c>InvokeMessageAndWaitAsync</c> would also work, but Send gives the right
/// semantics for an integration event with a RabbitMQ publish route and cascades the
/// downstream <c>AuctionClosingSaga.Handle(BiddingOpened)</c> start handler via
/// UseFastEventForwarding under the same tracked session.</para>
///
/// <para><b>What lands on the listing streams.</b> The fan-out appends one
/// <see cref="BiddingOpened"/> per attached listing via
/// <see cref="IEventStore.StartStream"/>. UseFastEventForwarding then forwards each
/// appended event as a Wolverine message — locally to
/// <c>AuctionClosingSaga.Handle(BiddingOpened)</c> (start handler), and externally to
/// the <c>listings-auctions-events</c> RabbitMQ queue (Listings BC's
/// <c>AuctionStatusHandler</c> consumer at M4-S6).</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class SessionStartedFanOutTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public SessionStartedFanOutTests(AuctionsTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        try { await _fixture.CleanAllMartenDataAsync(); }
        catch (ObjectDisposedException) { }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SessionStarted_ProducesBiddingOpenedPerListing()
    {
        var listingA = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();
        var sellerA = Guid.CreateVersion7();
        var sellerB = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(durationMinutes: 5);

        // Workshop 002 §0 defaults for listing-A ($25 starting, no reserve, no BIN, no
        // extended bidding); listing-B carries reserve + extended-bidding fields to verify
        // the per-listing payload differentiation through the fan-out.
        await _fixture.SeedPublishedListingAsync(listingA, sellerA, startingBid: 25m);
        await _fixture.SeedPublishedListingAsync(
            listingB,
            sellerB,
            startingBid: 50m,
            reservePrice: 100m,
            buyItNowPrice: 200m,
            extendedBiddingEnabled: true,
            extendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
            extendedBiddingExtension: TimeSpan.FromSeconds(15));

        var startedAt = DateTimeOffset.UtcNow;

        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(new SessionStarted(
                SessionId: sessionId,
                ListingIds: new[] { listingA, listingB },
                StartedAt: startedAt));

        await using var session = _fixture.GetDocumentSession();

        var streamA = await session.Events.FetchStreamAsync(listingA);
        streamA.Count.ShouldBe(1);
        var openedA = streamA[0].Data.ShouldBeOfType<BiddingOpened>();
        openedA.ListingId.ShouldBe(listingA);
        openedA.SellerId.ShouldBe(sellerA);
        openedA.StartingBid.ShouldBe(25m);
        openedA.ReserveThreshold.ShouldBeNull();
        openedA.BuyItNowPrice.ShouldBeNull();
        openedA.ScheduledCloseAt.ShouldBe(startedAt.AddMinutes(5));
        openedA.ExtendedBiddingEnabled.ShouldBeFalse();
        openedA.MaxDuration.ShouldBe(TimeSpan.FromMinutes(10)); // 2x platform default

        var streamB = await session.Events.FetchStreamAsync(listingB);
        streamB.Count.ShouldBe(1);
        var openedB = streamB[0].Data.ShouldBeOfType<BiddingOpened>();
        openedB.ListingId.ShouldBe(listingB);
        openedB.SellerId.ShouldBe(sellerB);
        openedB.StartingBid.ShouldBe(50m);
        openedB.ReserveThreshold.ShouldBe(100m);
        openedB.BuyItNowPrice.ShouldBe(200m);
        openedB.ScheduledCloseAt.ShouldBe(startedAt.AddMinutes(5));
        openedB.ExtendedBiddingEnabled.ShouldBeTrue();
        openedB.ExtendedBiddingTriggerWindow.ShouldBe(TimeSpan.FromSeconds(30));
        openedB.ExtendedBiddingExtension.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task SessionStarted_Redelivery_DoesNotDoubleFireBiddingOpened()
    {
        var listingA = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();
        var sessionId = await _fixture.SeedSessionAsync(durationMinutes: 5);
        await _fixture.SeedPublishedListingAsync(listingA, Guid.CreateVersion7());
        await _fixture.SeedPublishedListingAsync(listingB, Guid.CreateVersion7());

        var sessionStarted = new SessionStarted(
            SessionId: sessionId,
            ListingIds: new[] { listingA, listingB },
            StartedAt: DateTimeOffset.UtcNow);

        // First delivery — fan-out appends BiddingOpened to both listing streams.
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(sessionStarted);

        // Second delivery (redelivery) — fan-out's pre-query stream-state check skips
        // both listings because their streams already exist. No additional BiddingOpened
        // appended; no exception propagates.
        await _fixture.Host.TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .SendMessageAndWaitAsync(sessionStarted);

        await using var session = _fixture.GetDocumentSession();
        var streamA = await session.Events.FetchStreamAsync(listingA);
        streamA.Count.ShouldBe(1, "redelivery must not double-fire BiddingOpened on listing A");

        var streamB = await session.Events.FetchStreamAsync(listingB);
        streamB.Count.ShouldBe(1, "redelivery must not double-fire BiddingOpened on listing B");
    }
}
