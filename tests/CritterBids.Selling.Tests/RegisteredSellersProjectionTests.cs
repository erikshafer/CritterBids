using CritterBids.Contracts;
using CritterBids.Selling;
using CritterBids.Selling.Tests.Fixtures;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Selling.Tests;

[Collection(SellingTestCollection.Name)]
public class RegisteredSellersProjectionTests : IAsyncLifetime
{
    private readonly SellingTestFixture _fixture;

    public RegisteredSellersProjectionTests(SellingTestFixture fixture)
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
            // Host failed to start (e.g. schema migration error during fixture initialization).
            // Tests will fail with a clearer message rather than cascading ObjectDisposedExceptions.
        }
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SellerRegistrationCompleted_CreatesRegisteredSellerRow()
    {
        var sellerId = Guid.CreateVersion7();
        var message = new SellerRegistrationCompleted(sellerId, DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var seller = await session.LoadAsync<RegisteredSeller>(sellerId);

        seller.ShouldNotBeNull();
        seller.Id.ShouldBe(sellerId);
    }

    [Fact]
    public async Task SellerRegistrationCompleted_Duplicate_IsIdempotent()
    {
        var sellerId = Guid.CreateVersion7();
        var message = new SellerRegistrationCompleted(sellerId, DateTimeOffset.UtcNow);

        // First delivery
        await _fixture.ExecuteAndWaitAsync(message);

        // Second delivery — same message, same ID
        await _fixture.ExecuteAndWaitAsync(message);

        await using var session = _fixture.GetDocumentSession();
        var sellers = await session.Query<RegisteredSeller>()
            .Where(s => s.Id == sellerId)
            .ToListAsync();

        sellers.Count.ShouldBe(1);
        sellers[0].Id.ShouldBe(sellerId);
    }

    [Fact]
    public async Task IsRegistered_WithKnownSeller_ReturnsTrue()
    {
        var sellerId = Guid.CreateVersion7();
        var message = new SellerRegistrationCompleted(sellerId, DateTimeOffset.UtcNow);

        await _fixture.ExecuteAndWaitAsync(message);

        var service = _fixture.Host.Services.GetRequiredService<ISellerRegistrationService>();
        var isRegistered = await service.IsRegisteredAsync(sellerId);

        isRegistered.ShouldBeTrue();
    }

    [Fact]
    public async Task IsRegistered_WithUnknownSeller_ReturnsFalse()
    {
        var unknownSellerId = Guid.CreateVersion7();

        var service = _fixture.Host.Services.GetRequiredService<ISellerRegistrationService>();
        var isRegistered = await service.IsRegisteredAsync(unknownSellerId);

        isRegistered.ShouldBeFalse();
    }
}
