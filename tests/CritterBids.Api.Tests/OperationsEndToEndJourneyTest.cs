using Alba;
using CritterBids.Api.Tests.Fixtures;
using CritterBids.Contracts.Auctions;
using CritterBids.Contracts.Obligations;
using CritterBids.Contracts.Participants;
using CritterBids.Contracts.Selling;
using CritterBids.Contracts.Settlement;
using CritterBids.Operations;
using Wolverine.Tracking;

namespace CritterBids.Api.Tests;

/// <summary>
/// M7-S7 cross-BC end-to-end journey test — the milestone-closing proof that every Operations
/// operator view works from integration event to staff-gated HTTP endpoint on real Postgres.
///
/// <para><b>Shape.</b> One <c>[Fact]</c> that dispatches a representative integration event from every
/// source BC family (Settlement, Selling, Auctions, Obligations, Participants), waits via the
/// tracked-session pattern (<c>InvokeMessageAndWaitAsync</c> — never a sleep), reads back every
/// projected view through the seven StaffOnly-gated query endpoints with a valid staff token, and
/// asserts on the Operations read models — never on a Relay push.</para>
///
/// <para><b>Pure-consumer contract (ADR-014 Path A).</b> The <c>tracked.Sent</c> envelope collection
/// is asserted empty on every dispatch — Operations handlers return <see cref="Task"/>, produce no
/// <c>OutgoingMessages</c>, and inject no <c>IMessageBus</c>. The assertion is sound because
/// <see cref="JourneyTestFixture"/> excludes all six foreign-BC handler families; the only message
/// handlers that run are Operations'.</para>
///
/// <para><b>Fixture.</b> Uses <see cref="JourneyTestFixture"/>, which boots from the real
/// <c>Program.cs</c> via <c>AlbaHost.For&lt;Program&gt;</c> (so full Wolverine routing is in
/// effect), overlays Testcontainers PostgreSQL + staff auth, and limits handler discovery to the
/// Operations BC only — the same cross-BC exclusion pattern as <c>OperationsTestFixture</c>.
/// This lets <c>InvokeMessageAndWaitAsync</c> complete cleanly without foreign handlers
/// co-consuming events and failing on absent aggregates.</para>
/// </summary>
[Collection(JourneyTestCollection.Name)]
public sealed class OperationsEndToEndJourneyTest : IAsyncLifetime
{
    private readonly JourneyTestFixture _fixture;

    public OperationsEndToEndJourneyTest(JourneyTestFixture fixture) => _fixture = fixture;

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

    [Fact]
    public async Task CrossBc_Consume_Project_Query_Journey_Through_All_Operator_Views()
    {
        // ── Shared identifiers ──────────────────────────────────────────────
        var listingId     = Guid.CreateVersion7();
        var sellerId      = Guid.CreateVersion7();
        var winnerId      = Guid.CreateVersion7();
        var bidderId      = Guid.CreateVersion7();
        var settlementId  = Guid.CreateVersion7();
        var bidId         = Guid.CreateVersion7();
        var obligationIdA = Guid.CreateVersion7(); // escalation queue
        var obligationIdB = Guid.CreateVersion7(); // dispute queue
        var disputeId     = Guid.CreateVersion7();
        var sessionId     = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();
        var now           = DateTimeOffset.UtcNow;

        // ═══════════════════════════════════════════════════════════════════
        // Phase 1 — Dispatch integration events (consume → project)
        // Each dispatch asserts tracked.Sent is empty, proving Operations'
        // pure-consumer contract (ADR-014 Path A): no OutgoingMessages, no
        // IMessageBus, no cascading messages from any Operations handler.
        // ═══════════════════════════════════════════════════════════════════

        // ── Settlement family → SettlementQueueView ─────────────────────
        var trackedSettlement = await _fixture.Host.InvokeMessageAndWaitAsync(
            new SettlementCompleted(settlementId, listingId, winnerId, sellerId,
                HammerPrice: 100m, FeeAmount: 10m, SellerPayout: 90m, now));
        trackedSettlement.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        // ── Selling family → LotBoardView (seed as Draft) ───────────────
        var trackedPublished = await _fixture.Host.InvokeMessageAndWaitAsync(
            new ListingPublished(listingId, sellerId, "Journey Test Listing", "English",
                StartingBid: 50m, ReservePrice: 75m, BuyItNow: null,
                Duration: TimeSpan.FromMinutes(30), ExtendedBiddingEnabled: false,
                ExtendedBiddingTriggerWindow: null, ExtendedBiddingExtension: null,
                FeePercentage: 10m, now));
        trackedPublished.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        // ── Auctions family → LotBoardView (advance to Open) + BidActivityEntry (append) ──
        var trackedOpened = await _fixture.Host.InvokeMessageAndWaitAsync(
            new BiddingOpened(listingId, sellerId, StartingBid: 50m,
                ReserveThreshold: 75m, BuyItNowPrice: null,
                ScheduledCloseAt: now.AddMinutes(30),
                ExtendedBiddingEnabled: false,
                ExtendedBiddingTriggerWindow: null,
                ExtendedBiddingExtension: null,
                MaxDuration: TimeSpan.FromMinutes(30),
                OpenedAt: now));
        trackedOpened.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        // BidPlaced fans out to two sticky Separated queues (LotBoardAuctions + BidActivity),
        // so it must be published rather than invoked. The pure-consumer assertion for these
        // handlers is proven via the single-handler Invoke path in LotBoardHandlerTests.
        await _fixture.Host.SendMessageAndWaitAsync(
            new BidPlaced(listingId, bidId, bidderId,
                Amount: 80m, BidCount: 1, IsProxy: false, PlacedAt: now));

        // ── Obligations family → OperationsObligationsView (one Escalated, one Disputed) ──
        var trackedEscalation = await _fixture.Host.InvokeMessageAndWaitAsync(
            new DeadlineEscalated(obligationIdA, listingId, EscalatedAt: now));
        trackedEscalation.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        var trackedDispute = await _fixture.Host.InvokeMessageAndWaitAsync(
            new DisputeOpened(obligationIdB, listingId, disputeId,
                RaisedBy: winnerId, Reason: "NonDelivery", OpenedAt: now));
        trackedDispute.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        // ── Auctions session family → SessionActivityView ────────────────
        var trackedSession = await _fixture.Host.InvokeMessageAndWaitAsync(
            new SessionCreated(sessionId, "Flash Journey Session",
                DurationMinutes: 30, CreatedAt: now));
        trackedSession.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        // ── Participants family → ParticipantActivityView ────────────────
        var trackedParticipant = await _fixture.Host.InvokeMessageAndWaitAsync(
            new ParticipantSessionStarted(participantId, "Journey User",
                "Bidder 7777", CreditCeiling: 5000m, StartedAt: now));
        trackedParticipant.Sent.AllMessages().ShouldBeEmpty("Operations handlers must not produce outgoing messages (pure consumer)");

        // ═══════════════════════════════════════════════════════════════════
        // Phase 2 — Query through StaffOnly-gated endpoints (project → query)
        // Uses Alba Scenario with the X-Staff-Token header; the full ASP.NET
        // pipeline (auth → authorization → endpoint) is exercised.
        // ═══════════════════════════════════════════════════════════════════

        // ── /api/operations/settlement-queue ──────────────────────────────
        var settlementRows = await GetStaffAsync<SettlementQueueView>("/api/operations/settlement-queue");
        var settlementRow = settlementRows.ShouldHaveSingleItem();
        settlementRow.SettlementId.ShouldBe(settlementId);
        settlementRow.ListingId.ShouldBe(listingId);
        settlementRow.WinnerId.ShouldBe(winnerId);
        settlementRow.SellerId.ShouldBe(sellerId);
        settlementRow.HammerPrice.ShouldBe(100m);
        settlementRow.Status.ShouldBe(SettlementQueueStatus.Completed);

        // ── /api/operations/lot-board ─────────────────────────────────────
        var lotRows = await GetStaffAsync<LotBoardView>("/api/operations/lot-board");
        var lotRow = lotRows.ShouldHaveSingleItem();
        lotRow.ListingId.ShouldBe(listingId);
        lotRow.SellerId.ShouldBe(sellerId);
        lotRow.Title.ShouldBe("Journey Test Listing");
        lotRow.Status.ShouldBe(LotBoardStatus.Open);
        lotRow.CurrentBid.ShouldBe(80m);
        lotRow.BidCount.ShouldBe(1);

        // ── /api/operations/bid-activity ──────────────────────────────────
        var bidRows = await GetStaffAsync<BidActivityEntry>("/api/operations/bid-activity");
        var bidRow = bidRows.ShouldHaveSingleItem();
        bidRow.BidId.ShouldBe(bidId);
        bidRow.ListingId.ShouldBe(listingId);
        bidRow.BidderId.ShouldBe(bidderId);
        bidRow.Amount.ShouldBe(80m);

        // ── /api/operations/obligations/escalations ───────────────────────
        var escalations = await GetStaffAsync<OperationsObligationsView>("/api/operations/obligations/escalations");
        var escalationRow = escalations.ShouldHaveSingleItem();
        escalationRow.ObligationId.ShouldBe(obligationIdA);
        escalationRow.QueueState.ShouldBe(QueueState.Escalated);

        // ── /api/operations/obligations/disputes ──────────────────────────
        var disputes = await GetStaffAsync<OperationsObligationsView>("/api/operations/obligations/disputes");
        var disputeRow = disputes.ShouldHaveSingleItem();
        disputeRow.ObligationId.ShouldBe(obligationIdB);
        disputeRow.QueueState.ShouldBe(QueueState.Disputed);
        disputeRow.DisputeReason.ShouldBe("NonDelivery");

        // ── /api/operations/sessions ──────────────────────────────────────
        var sessionRows = await GetStaffAsync<SessionActivityView>("/api/operations/sessions");
        var sessionRow = sessionRows.ShouldHaveSingleItem();
        sessionRow.SessionId.ShouldBe(sessionId);
        sessionRow.Title.ShouldBe("Flash Journey Session");
        sessionRow.DurationMinutes.ShouldBe(30);

        // ── /api/operations/participants ──────────────────────────────────
        var participantRows = await GetStaffAsync<ParticipantActivityView>("/api/operations/participants");
        var participantRow = participantRows.ShouldHaveSingleItem();
        participantRow.ParticipantId.ShouldBe(participantId);
        participantRow.DisplayName.ShouldBe("Journey User");
        participantRow.BidderId.ShouldBe("Bidder 7777");
        participantRow.CreditCeiling.ShouldBe(5000m);
    }

    private async Task<IReadOnlyList<T>> GetStaffAsync<T>(string route)
    {
        var result = await _fixture.Host.Scenario(x =>
        {
            x.Get.Url(route);
            x.WithRequestHeader("X-Staff-Token", JourneyTestFixture.ValidStaffToken);
            x.StatusCodeShouldBe(200);
        });
        var rows = await result.ReadAsJsonAsync<List<T>>();
        return rows ?? [];
    }
}
