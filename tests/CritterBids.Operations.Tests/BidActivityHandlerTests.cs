using CritterBids.Contracts.Auctions;
using CritterBids.Operations.Tests.Fixtures;
using Marten;
using Wolverine;
using Wolverine.Tracking;

namespace CritterBids.Operations.Tests;

/// <summary>
/// End-to-end Testcontainers projection tests for the M7-S3 bid-activity feed (W006 §3). The feed
/// is the one append/feed-shaped Operations view: each <c>BidPlaced</c> appends one immutable
/// <see cref="BidActivityEntry"/> keyed by <c>BidId</c>, in contrast to the upsert views that mutate
/// one row across many events. Events are dispatched through the in-process Wolverine bus so the
/// full path (discovery, code-gen, Marten session, transaction commit) is exercised.
///
/// Coverage: N distinct bids produce N rows, queryable by <c>ListingId</c> and ordered by
/// <c>PlacedAt</c>; a re-delivered <c>BidId</c> is an idempotent no-op (no duplicate row); and the
/// handler publishes nothing (ADR-014 Path A pure consumer).
/// </summary>
[Collection(OperationsTestCollection.Name)]
public class BidActivityHandlerTests : IAsyncLifetime
{
    private readonly OperationsTestFixture _fixture;

    public BidActivityHandlerTests(OperationsTestFixture fixture)
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
            // Host failed to start — let the test fail with a clearer message.
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTimeOffset BaseAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ───────────────────────────────────────────────────────────────────────────
    // N bids → N rows, filterable by ListingId and ordered by PlacedAt
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BidActivity_AppendsOneImmutableRowPerBid_OrderedByPlacedAt()
    {
        var listingA = Guid.CreateVersion7();
        var listingB = Guid.CreateVersion7();

        var bidA1 = Guid.CreateVersion7();
        var bidA2 = Guid.CreateVersion7();
        var bidA3 = Guid.CreateVersion7();
        var bidB1 = Guid.CreateVersion7();

        // Three bids on listing A (placed out of timestamp order to prove PlacedAt sorting) and one
        // bid on listing B (to prove ListingId filtering isolates the feed).
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingA, bidA2, Guid.CreateVersion7(), Amount: 25m, BidCount: 2, IsProxy: false, BaseAt.AddMinutes(2)));
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingA, bidA1, Guid.CreateVersion7(), Amount: 10m, BidCount: 1, IsProxy: false, BaseAt.AddMinutes(1)));
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingA, bidA3, Guid.CreateVersion7(), Amount: 40m, BidCount: 3, IsProxy: true, BaseAt.AddMinutes(3)));
        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingB, bidB1, Guid.CreateVersion7(), Amount: 99m, BidCount: 1, IsProxy: false, BaseAt.AddMinutes(1)));

        await using var session = _fixture.GetDocumentSession();

        // Listing A's feed: three rows, ordered by PlacedAt.
        var feedA = await session.Query<BidActivityEntry>()
            .Where(x => x.ListingId == listingA)
            .OrderBy(x => x.PlacedAt)
            .ToListAsync();

        feedA.Count.ShouldBe(3);
        feedA.Select(x => x.BidId).ShouldBe(new[] { bidA1, bidA2, bidA3 });
        feedA.Select(x => x.Amount).ShouldBe(new[] { 10m, 25m, 40m });
        feedA[2].IsProxy.ShouldBeTrue();   // bidA3 was a proxy bid
        feedA[0].BidCount.ShouldBe(1);

        // Listing B's feed is isolated — only its one row.
        var feedB = await session.Query<BidActivityEntry>()
            .Where(x => x.ListingId == listingB)
            .ToListAsync();
        feedB.Count.ShouldBe(1);
        feedB[0].BidId.ShouldBe(bidB1);
        feedB[0].Amount.ShouldBe(99m);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Re-delivered BidId is a no-op — no duplicate row (idempotent append)
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BidActivity_RedeliveredBidId_IsNoOp_NoDuplicateRow()
    {
        var listingId = Guid.CreateVersion7();
        var bidId     = Guid.CreateVersion7();

        var bid = new BidPlaced(
            listingId, bidId, Guid.CreateVersion7(), Amount: 15m, BidCount: 1, IsProxy: false, BaseAt);

        await _fixture.Host.SendMessageAndWaitAsync(bid);
        // Same BidId re-delivered (at-least-once transport): must not create a second row.
        await _fixture.Host.SendMessageAndWaitAsync(bid);

        await using var session = _fixture.GetDocumentSession();
        var rows = await session.Query<BidActivityEntry>()
            .Where(x => x.ListingId == listingId)
            .ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].BidId.ShouldBe(bidId);
        rows[0].Amount.ShouldBe(15m);

        // The handler is the pure-consumer guarantee's append side: re-delivery produced no second
        // row. (The "emits no downstream message" half of ADR-014 Path A is proven cleanly via the
        // single-handler Invoke path in LotBoardHandlerTests; BidPlaced fans out to two sticky
        // Separated queues here, so it must be published rather than invoked.)
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Field fidelity — every W006 §3 field round-trips from the BidPlaced payload
    // ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BidActivity_RowCarriesEveryW006Field()
    {
        var listingId = Guid.CreateVersion7();
        var bidId     = Guid.CreateVersion7();
        var bidderId  = Guid.CreateVersion7();
        var placedAt  = BaseAt.AddSeconds(42);

        await _fixture.Host.SendMessageAndWaitAsync(new BidPlaced(
            listingId, bidId, bidderId, Amount: 33.50m, BidCount: 7, IsProxy: true, placedAt));

        await using var session = _fixture.GetDocumentSession();
        var entry = await session.LoadAsync<BidActivityEntry>(bidId);

        entry.ShouldNotBeNull();
        entry.BidId.ShouldBe(bidId);
        entry.ListingId.ShouldBe(listingId);
        entry.BidderId.ShouldBe(bidderId);
        entry.Amount.ShouldBe(33.50m);
        entry.BidCount.ShouldBe(7);
        entry.IsProxy.ShouldBeTrue();
        entry.PlacedAt.ShouldBe(placedAt);
    }
}
