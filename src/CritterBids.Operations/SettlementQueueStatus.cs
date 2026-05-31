namespace CritterBids.Operations;

/// <summary>
/// The settlement-queue lifecycle status surfaced on the operations staff board, derived
/// from which Settlement-family integration event last advanced the
/// <see cref="SettlementQueueView"/> row (W006 §1 Status-derivation rule).
///
/// <para><b>Derivation.</b> <c>PaymentFailed</c> → <see cref="Failed"/>;
/// <c>SettlementCompleted</c> → <see cref="Completed"/>; <c>SellerPayoutIssued</c> →
/// <see cref="PaidOut"/>. The only mandated preservation guard is that <see cref="PaidOut"/>
/// must not regress to <see cref="Completed"/> when <c>SettlementCompleted</c> is re-delivered
/// after <c>SellerPayoutIssued</c> (W006 §1).</para>
/// </summary>
public enum SettlementQueueStatus
{
    /// <summary>The settlement failed (reserve not met in MVP). Set by <c>PaymentFailed</c>.</summary>
    Failed,

    /// <summary>The financial workflow reached its terminal happy-path state. Set by <c>SettlementCompleted</c>.</summary>
    Completed,

    /// <summary>The seller payout was issued. Set by <c>SellerPayoutIssued</c>; terminal — does not regress.</summary>
    PaidOut,
}
