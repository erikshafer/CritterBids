namespace CritterBids.Settlement;

/// <summary>
/// Self-send continuation command emitted by the saga's <c>Handle(CalculateFee)</c>. The
/// saga's <c>Handle(IssueSellerPayout)</c> credits the seller's credit ledger and emits
/// <see cref="CritterBids.Contracts.Settlement.SellerPayoutIssued"/> per workshop 003 §5.1,
/// advancing state to <see cref="SettlementStatus.PayoutIssued"/>.
/// </summary>
public sealed record IssueSellerPayout(Guid SettlementId);
