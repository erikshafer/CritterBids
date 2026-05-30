using CritterBids.Contracts.Auctions;
using CritterBids.Relay.History;
using CritterBids.Relay.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace CritterBids.Relay.Tests;

[Collection(RelayTestCollection.Name)]
public class NotificationHistoryViewTests
{
    private readonly RelayTestFixture _fixture;

    public NotificationHistoryViewTests(RelayTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BidPlaced_And_ListingSold_AccumulateNotificationHistoryEntries()
    {
        using var scope = _fixture.Host.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();

        var bidderId = Guid.CreateVersion7();
        var listingId = Guid.CreateVersion7();

        await bus.PublishAsync(new BidPlaced(
            listingId,
            BidId: Guid.CreateVersion7(),
            BidderId: bidderId,
            Amount: 150m,
            BidCount: 3,
            IsProxy: false,
            PlacedAt: DateTimeOffset.UtcNow));

        await bus.PublishAsync(new ListingSold(
            listingId,
            SellerId: Guid.CreateVersion7(),
            WinnerId: bidderId,
            HammerPrice: 220m,
            BidCount: 4,
            SoldAt: DateTimeOffset.UtcNow));

        NotificationHistoryView? view = null;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await using var query = store.QuerySession();
            view = await query.LoadAsync<NotificationHistoryView>(bidderId);

            if (view is not null && view.Entries.Count >= 2)
            {
                break;
            }

            await Task.Delay(100);
        }

        view.ShouldNotBeNull();
        view.BidderId.ShouldBe(bidderId);
        view.Entries.Count.ShouldBeGreaterThanOrEqualTo(2);
        view.Entries.Any(x => x.EventType == nameof(BidPlaced)).ShouldBeTrue();
        view.Entries.Any(x => x.EventType == nameof(ListingSold)).ShouldBeTrue();
    }
}
