namespace CritterBids.Settlement;

/// <summary>
/// Emitted by the saga's <c>Handle(ChargeWinner)</c> phase per workshop 003 scenario §3.1.
/// Records the winner's credit-ledger debit at the MVP credit-ledger posture per W003
/// §"Winner Charge" — no real payment processor is invoked at MVP; the charge is a Marten
/// document update against Settlement's bidder-credit projection (lands in M5-S5 as
/// <c>BidderCreditView</c>).
///
/// <para><b>Stream-internal — not in <c>CritterBids.Contracts.Settlement.*</c>.</b> Per
/// W003 §"Integration in/out", the winner-charge event is entirely a Settlement-internal
/// concern; Relay's seller-side push consumes <see cref="CritterBids.Contracts.Settlement.SellerPayoutIssued"/>
/// instead, and the bidder-side push at terminal state composes the remaining-credit
/// number from the <c>BidderCreditView</c> projection (W003 Phase 1 Part 7).</para>
///
/// <para><b>Field name convention.</b> The <see cref="Amount"/> field uses payment-domain
/// vocabulary at the moment money moves — distinct from <c>HammerPrice</c> (the auction-
/// domain value) per the W003 Phase 1 Part 2 Field Name Convention's touchpoint table
/// (M5-S1 F002 amendment).</para>
/// </summary>
public sealed record WinnerCharged(
    Guid SettlementId,
    Guid WinnerId,
    decimal Amount,
    DateTimeOffset ChargedAt);
