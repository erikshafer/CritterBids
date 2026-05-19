using CritterBids.Auctions.Tests.Fixtures;
using CritterBids.Contracts.Selling;
using Marten;

namespace CritterBids.Auctions.Tests;

/// <summary>
/// Idempotency tests for <see cref="PublishedListingsHandler"/>. M4-S5's third lived
/// application of the M4-D4 duplicate-projection pattern (after Settlement's
/// <c>BidderCreditView</c> at M5-S5 and Auctions's own <see cref="ParticipantCreditCeiling"/>
/// at M4-S4). The handler consumes two cross-BC integration events from the
/// <c>auctions-selling-events</c> queue:
/// <list type="bullet">
///   <item><c>ListingPublished</c> — creates / refreshes the row at
///     <see cref="PublishedListingsStatus.Published"/> with terminal-state preservation
///     for re-delivery against an already-Withdrawn row.</item>
///   <item><c>ListingWithdrawn</c> — transitions to
///     <see cref="PublishedListingsStatus.Withdrawn"/> and stamps
///     <c>WithdrawnAt</c>; idempotent on re-delivery.</item>
/// </list>
///
/// <para>Tests call <see cref="PublishedListingsHandler.Handle(ListingPublished, IDocumentSession, CancellationToken)"/>
/// and <see cref="PublishedListingsHandler.Handle(ListingWithdrawn, IDocumentSession, CancellationToken)"/>
/// directly against an isolated <see cref="IDocumentSession"/> — bypasses Wolverine to
/// keep the assertion focused on the projection's upsert behaviour. Same shape as
/// <c>ParticipantCreditCeilingProjectionTests</c> from M4-S4.</para>
/// </summary>
[Collection(AuctionsTestCollection.Name)]
public class PublishedListingsProjectionTests : IAsyncLifetime
{
    private readonly AuctionsTestFixture _fixture;

    public PublishedListingsProjectionTests(AuctionsTestFixture fixture)
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
    public async Task ListingPublished_InitializesRowAtPublished()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow;

        var message = new ListingPublished(
            ListingId: listingId,
            SellerId: sellerId,
            Title: "Vintage Mechanical Keyboard",
            Format: "Flash",
            StartingBid: 25m,
            ReservePrice: 50m,
            BuyItNow: 100m,
            Duration: null,
            ExtendedBiddingEnabled: true,
            ExtendedBiddingTriggerWindow: TimeSpan.FromSeconds(30),
            ExtendedBiddingExtension: TimeSpan.FromSeconds(15),
            FeePercentage: 0.10m,
            PublishedAt: publishedAt);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PublishedListingsHandler.Handle(message, session, default);
            await session.SaveChangesAsync();
        }

        await using var query = _fixture.GetDocumentSession();
        var row = await query.LoadAsync<PublishedListings>(listingId);

        row.ShouldNotBeNull();
        row.Id.ShouldBe(listingId);
        row.SellerId.ShouldBe(sellerId);
        row.StartingBid.ShouldBe(25m);
        row.ReservePrice.ShouldBe(50m);
        row.BuyItNowPrice.ShouldBe(100m);
        row.Duration.ShouldBeNull();
        row.ExtendedBiddingEnabled.ShouldBeTrue();
        row.ExtendedBiddingTriggerWindow.ShouldBe(TimeSpan.FromSeconds(30));
        row.ExtendedBiddingExtension.ShouldBe(TimeSpan.FromSeconds(15));
        row.PublishedAt.ShouldBe(publishedAt);
        row.WithdrawnAt.ShouldBeNull();
        row.Status.ShouldBe(PublishedListingsStatus.Published);
    }

    [Fact]
    public async Task ListingWithdrawn_TransitionsToWithdrawn()
    {
        var listingId = Guid.CreateVersion7();
        var sellerId = Guid.CreateVersion7();
        var publishedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var firstWithdrawalAt = DateTimeOffset.UtcNow;

        // Seed an existing Published row directly via the fixture helper (workshop defaults
        // plus the listingId/sellerId).
        await _fixture.SeedPublishedListingAsync(
            listingId: listingId,
            sellerId: sellerId,
            publishedAt: publishedAt);

        // First ListingWithdrawn delivery — transitions to Withdrawn and stamps WithdrawnAt.
        var firstWithdrawal = new ListingWithdrawn(
            ListingId: listingId,
            WithdrawnBy: Guid.NewGuid(),
            Reason: null,
            WithdrawnAt: firstWithdrawalAt);

        await using (var session = _fixture.GetDocumentSession())
        {
            await PublishedListingsHandler.Handle(firstWithdrawal, session, default);
            await session.SaveChangesAsync();
        }

        await using (var query = _fixture.GetDocumentSession())
        {
            var row = await query.LoadAsync<PublishedListings>(listingId);
            row.ShouldNotBeNull();
            row.Status.ShouldBe(PublishedListingsStatus.Withdrawn);
            row.WithdrawnAt.ShouldBe(firstWithdrawalAt);
            row.SellerId.ShouldBe(sellerId, "the original Published payload survives the transition");
        }

        // Re-deliver ListingWithdrawn with a later WithdrawnAt — the projection's idempotency
        // guard preserves the original WithdrawnAt and Status. Terminal-status preservation
        // per the M5-S3 PendingSettlement pattern.
        var redelivery = new ListingWithdrawn(
            ListingId: listingId,
            WithdrawnBy: Guid.NewGuid(),
            Reason: "Late re-delivery",
            WithdrawnAt: firstWithdrawalAt.AddMinutes(10));

        await using (var session = _fixture.GetDocumentSession())
        {
            await PublishedListingsHandler.Handle(redelivery, session, default);
            await session.SaveChangesAsync();
        }

        await using var finalQuery = _fixture.GetDocumentSession();
        var finalRow = await finalQuery.LoadAsync<PublishedListings>(listingId);
        finalRow.ShouldNotBeNull();
        finalRow.Status.ShouldBe(PublishedListingsStatus.Withdrawn);
        finalRow.WithdrawnAt.ShouldBe(firstWithdrawalAt, "re-delivery must not re-stamp WithdrawnAt");
    }
}
