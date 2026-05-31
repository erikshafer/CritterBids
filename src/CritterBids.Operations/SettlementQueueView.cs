namespace CritterBids.Operations;

/// <summary>
/// Operations BC's settlement-queue read model — the first cross-BC consumer surface of the
/// eighth MVP bounded context (M7-S2). A staff-facing board row tracking the financial
/// disposition of one settlement, folded from the Settlement BC's three integration events
/// (<c>PaymentFailed</c>, <c>SettlementCompleted</c>, <c>SellerPayoutIssued</c>) per the W006 §1
/// field freeze. Operations is a <b>pure consumer</b> (ADR-014 Path A): it listens off RabbitMQ
/// and folds the firehose into this upsert view; it appends to no local stream and publishes
/// nothing.
///
/// <para><b>Lifecycle.</b> Maintained by <see cref="SettlementQueueHandler"/> as a tolerant
/// upsert keyed by <see cref="SettlementId"/>. <see cref="Status"/> is derived from which event
/// arrived (W006 §1): <c>PaymentFailed</c> → <see cref="SettlementQueueStatus.Failed"/> (plus
/// <see cref="FailureReason"/>, the staff-attention flag); <c>SettlementCompleted</c> →
/// <see cref="SettlementQueueStatus.Completed"/>; <c>SellerPayoutIssued</c> →
/// <see cref="SettlementQueueStatus.PaidOut"/>. The sole mandated preservation guard:
/// <see cref="SettlementQueueStatus.PaidOut"/> must not regress to
/// <see cref="SettlementQueueStatus.Completed"/> on a re-delivered <c>SettlementCompleted</c>.</para>
///
/// <para><b>Set-once fields.</b> <see cref="ListingId"/> and <see cref="WinnerId"/> are populated
/// by <c>PaymentFailed</c>/<c>SettlementCompleted</c> and follow the W006 set-once guard (a later
/// event never overwrites a value already set). <c>SellerPayoutIssued</c> carries neither, so it
/// leaves both untouched. If <c>SellerPayoutIssued</c> is the first event to arrive for a
/// <see cref="SettlementId"/>, the constructed minimal row leaves the set-once fields at
/// <see cref="System.Guid.Empty"/> until a later <c>PaymentFailed</c>/<c>SettlementCompleted</c>
/// fills them (the first-arrival behavior recorded in the M7-S2 retro). <see cref="SellerId"/> is
/// last-write (W006 §1 lists no set-once guard for it — null until completed).</para>
///
/// <para><b>Marten Id convention.</b> <see cref="SettlementId"/> doubles as the Marten document
/// key, exposed via the <see cref="Id"/> expression-bodied alias — the lived natural-key-as-id
/// idiom from <c>PendingSettlement</c> (M5-S3) and <c>BidderCreditView</c> (M5-S5). No separate
/// id field; the Operations module registers the document with no <c>.Identity()</c> override.</para>
/// </summary>
public sealed record SettlementQueueView
{
    /// <summary>The settlement this row tracks (deterministic UUID v5 stream id). Doubles as the Marten document key.</summary>
    public Guid SettlementId { get; init; }

    /// <summary>
    /// The source listing. Populated from <c>PaymentFailed</c>/<c>SettlementCompleted</c>; set-once
    /// (W006 §1). Not carried on <c>SellerPayoutIssued</c>; remains <see cref="System.Guid.Empty"/>
    /// if a payout-first arrival creates the row before either listing-bearing event.
    /// </summary>
    public Guid ListingId { get; init; }

    /// <summary>
    /// The winning bidder. Populated from <c>PaymentFailed</c>/<c>SettlementCompleted</c>; set-once
    /// (W006 §1). Not carried on <c>SellerPayoutIssued</c>.
    /// </summary>
    public Guid WinnerId { get; init; }

    /// <summary>The seller receiving the payout. Null until <c>SettlementCompleted</c>/<c>SellerPayoutIssued</c> arrives; last-write.</summary>
    public Guid? SellerId { get; init; }

    /// <summary>Final accepted price. Carried by <c>SettlementCompleted</c>.</summary>
    public decimal? HammerPrice { get; init; }

    /// <summary>Platform fee carved from the hammer price. Carried by <c>SettlementCompleted</c>.</summary>
    public decimal? FeeAmount { get; init; }

    /// <summary>Projected seller payout (hammer minus fee). Carried by <c>SettlementCompleted</c>.</summary>
    public decimal? SellerPayout { get; init; }

    /// <summary>Actual issued payout amount. Carried by <c>SellerPayoutIssued</c>.</summary>
    public decimal? PayoutAmount { get; init; }

    /// <summary>Fee deducted at payout time. Carried by <c>SellerPayoutIssued</c>.</summary>
    public decimal? FeeDeducted { get; init; }

    /// <summary>Failure-classification string ("ReserveNotMet" in MVP). Carried by <c>PaymentFailed</c>; the staff-attention flag.</summary>
    public string? FailureReason { get; init; }

    /// <summary>The lifecycle status, derived from the latest event per W006 §1.</summary>
    public SettlementQueueStatus Status { get; init; }

    /// <summary>Latest-wins timestamp of the most recent event applied to this row.</summary>
    public DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>
    /// Marten document key — equals <see cref="SettlementId"/>. Expression-bodied to keep the
    /// record's storage shape identical to the <c>PendingSettlement</c> / <c>BidderCreditView</c>
    /// natural-key-as-id pattern; no <c>.Identity()</c> override is needed in the module.
    /// </summary>
    public Guid Id => SettlementId;
}
