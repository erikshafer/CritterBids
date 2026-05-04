using JasperFx.Core;
using Wolverine;
using Wolverine.ErrorHandling;

namespace CritterBids.Settlement;

/// <summary>
/// Wolverine retry policies for the Settlement BC. Mirrors the
/// <see cref="CritterBids.Auctions.AuctionsConcurrencyRetryPolicies"/> shape.
///
/// <para><b>PendingSettlementNotFoundException retry.</b> Per W003 Phase 1 Part 1 Option A,
/// the saga's start handler throws <see cref="PendingSettlementNotFoundException"/> when
/// the <see cref="PendingSettlement"/> projection has not yet caught up from
/// <c>ListingPublished</c>. Wolverine's retry policy re-queues the inbound message with
/// progressive backoff (100ms → 250ms → 500ms — three retries giving the projection up to
/// ~850ms cumulative wait time). The triggering event stays in the queue until the
/// projection catches up; in practice the race rarely fires because <c>ListingPublished</c>
/// happens hours or days before <c>ListingSold</c>.</para>
/// </summary>
internal sealed class SettlementsConcurrencyRetryPolicies : IWolverineExtension
{
    public void Configure(WolverineOptions options)
    {
        options.OnException<PendingSettlementNotFoundException>()
            .RetryWithCooldown(100.Milliseconds(), 250.Milliseconds(), 500.Milliseconds());
    }
}
