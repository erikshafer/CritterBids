namespace CritterBids.Settlement;

/// <summary>
/// Self-send terminal command emitted by the saga's <c>Handle(IssueSellerPayout)</c>. The
/// saga's <c>Handle(CompleteSettlement)</c> emits
/// <see cref="CritterBids.Contracts.Settlement.SettlementCompleted"/> per workshop 003 §6.1,
/// advances state to <see cref="SettlementStatus.Completed"/>, and calls
/// <c>MarkCompleted()</c> — the saga document is removed from Marten and the financial
/// event stream is closed at terminal state per W003 §"Financial Event Stream".
/// </summary>
public sealed record CompleteSettlement(Guid SettlementId);
