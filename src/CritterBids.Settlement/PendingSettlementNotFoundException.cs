namespace CritterBids.Settlement;

/// <summary>
/// Thrown by <see cref="StartSettlementSagaHandler"/> when a <c>ListingSold</c> arrives but
/// the corresponding <see cref="PendingSettlement"/> projection row has not yet caught up
/// from <c>ListingPublished</c>. Per W003 Phase 1 Part 1 Option A, Wolverine's retry policy
/// re-queues the inbound message with backoff until the projection row appears — the
/// triggering event stays in the queue until the handler succeeds.
///
/// <para><b>Retryable.</b> Registered in <see cref="SettlementsConcurrencyRetryPolicies"/>'s
/// <c>OnException&lt;PendingSettlementNotFoundException&gt;().RetryWithCooldown(...)</c>.</para>
///
/// <para>In practice this path should essentially never fire — <c>ListingPublished</c>
/// happens hours or days before <c>ListingSold</c>, giving the projection plenty of time
/// to catch up. The exception is the correctness guarantee if the projection is a few
/// milliseconds behind.</para>
/// </summary>
public sealed class PendingSettlementNotFoundException : Exception
{
    public PendingSettlementNotFoundException(Guid listingId)
        : base($"PendingSettlement not found for listing '{listingId}' — projection has not caught up from ListingPublished.")
    {
        ListingId = listingId;
    }

    public Guid ListingId { get; }
}
