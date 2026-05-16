using CritterBids.Contracts.Settlement;
using Marten;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace CritterBids.Settlement;

/// <summary>
/// Settlement BC's seven-phase financial workflow. Wolverine Saga per ADR-019. State is
/// a single mutable document persisted via Marten under a deterministic UUID v5
/// <c>SettlementId</c> (per W003 Phase 1 Part 6 / <see cref="SettlementsIdentityNamespaces.SettlementId"/>).
///
/// <para><b>Lifecycle.</b> The saga starts via <see cref="StartSettlementSagaHandler"/> on
/// inbound <c>ListingSold</c> (M5-S4 happy path) or <c>BuyItNowPurchased</c> (M5-S5 BIN path).
/// Each phase appends one domain event to the financial event stream at <see cref="Id"/>
/// and emits a self-send continuation command via <c>OutgoingMessages</c>. Terminal phases
/// also emit integration events (<see cref="SellerPayoutIssued"/>,
/// <see cref="SettlementCompleted"/>) via <c>OutgoingMessages</c> for cross-BC consumers
/// and the local <c>PendingSettlementHandler</c>. <c>MarkCompleted()</c> at the
/// <see cref="SettlementStatus.Completed"/> phase removes the saga document; the financial
/// event stream is closed at terminal state and persists as audit per W003 §"Financial
/// Event Stream".</para>
///
/// <para><b>State guards throw <see cref="InvalidSettlementTransitionException"/>.</b> Each
/// phase's <c>Handle</c> verifies the saga's current <see cref="Status"/> matches the
/// expected pre-phase state. Re-delivery of a continuation command after the saga has
/// already advanced past the corresponding phase throws (Wolverine inbox dedup should
/// prevent this in practice; the exception is the correctness guarantee if dedup fails).
/// Re-delivery of <c>ListingSold</c> is handled at <see cref="StartSettlementSagaHandler"/>
/// via the saga-existence check — no exception, just a no-op return.</para>
///
/// <para><b>Failure path lands at M5-S5.</b> The reserve-not-met branch in
/// <c>Handle(CheckReserve)</c> self-sends <see cref="FailSettlement"/>; the
/// <c>Handle(FailSettlement)</c> phase appends <see cref="CritterBids.Contracts.Settlement.PaymentFailed"/>
/// to the financial event stream, emits the integration event, and reaches the
/// <see cref="SettlementStatus.Failed"/> terminal state via <c>MarkCompleted()</c>. The
/// failure-path event stream contains exactly three events per workshop 003 scenario §9.3:
/// <c>SettlementInitiated</c>, <c>ReserveCheckCompleted(WasMet: false)</c>, <c>PaymentFailed</c>.</para>
///
/// <para><b>BIN-source path lands at M5-S5.</b> <see cref="StartSettlementSagaHandler"/> accepts
/// both <c>ListingSold</c> (bidding source) and <c>BuyItNowPurchased</c> (BIN source); the BIN
/// overload constructs the saga at <see cref="SettlementStatus.ReserveChecked"/> directly with
/// <see cref="ReserveWasMet"/> = <c>true</c>, skips appending <c>ReserveCheckCompleted</c>
/// (the absence is the §9.2 audit signal), and self-sends <see cref="ChargeWinner"/> as the
/// first continuation per W003 Phase 1 Part 5.</para>
///
/// <para><b>Field name convention.</b> <see cref="HammerPrice"/> is the saga's runtime field
/// (post-initiation per W003 Phase 1 Part 2 Field Name Convention from M5-S1's F002
/// amendment). The pre-initiation field on <see cref="SettlementInitiated"/> uses
/// <c>Price</c> (source-agnostic at command time).</para>
/// </summary>
public sealed class SettlementSaga : Wolverine.Saga
{
    public Guid Id { get; set; }
    public Guid ListingId { get; set; }
    public Guid WinnerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal HammerPrice { get; set; }
    public decimal? ReservePrice { get; set; }
    public decimal FeePercentage { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal? SellerPayout { get; set; }
    public bool ReserveWasMet { get; set; }
    public SettlementStatus Status { get; set; }
    public string? FailureReason { get; set; }

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(CheckReserve.SettlementId))] CheckReserve message,
        IDocumentSession session)
    {
        if (Status != SettlementStatus.Initiated)
        {
            throw new InvalidSettlementTransitionException(
                Id, Status, nameof(CheckReserve));
        }

        // No reserve set → always met (W003 §2.3). Otherwise compare.
        var met = ReservePrice is null || HammerPrice >= ReservePrice.Value;
        ReserveWasMet = met;
        Status = SettlementStatus.ReserveChecked;

        session.Events.Append(
            Id,
            new ReserveCheckCompleted(HammerPrice, ReservePrice, met, DateTimeOffset.UtcNow));

        if (!met)
        {
            // Reserve-not-met defense-in-depth path per W003 §3.2 / §9.3. Self-send
            // FailSettlement; the FailSettlement handler appends PaymentFailed to the
            // financial event stream, emits the integration event, and MarkCompleted()s
            // in Failed terminal state. No ChargeWinner — the failure short-circuits the
            // remaining four happy-path phases.
            return new OutgoingMessages { new FailSettlement(Id, "ReserveNotMet") };
        }

        return new OutgoingMessages { new ChargeWinner(Id) };
    }

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(ChargeWinner.SettlementId))] ChargeWinner message,
        IDocumentSession session)
    {
        if (Status != SettlementStatus.ReserveChecked)
        {
            throw new InvalidSettlementTransitionException(
                Id, Status, nameof(ChargeWinner));
        }

        // MVP credit-ledger posture per W003 §"Winner Charge" — no real payment processor.
        // The bidder-credit projection (BidderCreditView, M5-S5) consumes WinnerCharged to
        // debit the bidder's running balance.
        Status = SettlementStatus.WinnerCharged;

        session.Events.Append(
            Id,
            new WinnerCharged(Id, WinnerId, HammerPrice, DateTimeOffset.UtcNow));

        return new OutgoingMessages { new CalculateFee(Id) };
    }

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(CalculateFee.SettlementId))] CalculateFee message,
        IDocumentSession session)
    {
        if (Status != SettlementStatus.WinnerCharged)
        {
            throw new InvalidSettlementTransitionException(
                Id, Status, nameof(CalculateFee));
        }

        // Banker's rounding per W003 §4.2 MVP convention. FeePercentage is carried as the
        // multiplicative ratio (0.10m for 10%) per the ListingPublished contract's existing
        // constant placeholder — multiply directly without dividing by 100.
        var feeAmount = Math.Round(HammerPrice * FeePercentage, 2, MidpointRounding.ToEven);
        var sellerPayout = HammerPrice - feeAmount;

        FeeAmount = feeAmount;
        SellerPayout = sellerPayout;
        Status = SettlementStatus.FeeCalculated;

        session.Events.Append(
            Id,
            new FinalValueFeeCalculated(
                Id, HammerPrice, FeePercentage, feeAmount, sellerPayout, DateTimeOffset.UtcNow));

        return new OutgoingMessages { new IssueSellerPayout(Id) };
    }

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(IssueSellerPayout.SettlementId))] IssueSellerPayout message,
        IDocumentSession session)
    {
        if (Status != SettlementStatus.FeeCalculated)
        {
            throw new InvalidSettlementTransitionException(
                Id, Status, nameof(IssueSellerPayout));
        }

        Status = SettlementStatus.PayoutIssued;

        // Integration contract — appended to the financial event stream for audit AND
        // emitted on the bus for cross-BC consumers (cross-BC publish route at S6; local
        // in-process consumers — none in M5 since BidderCreditView lands at S5 — fire from
        // OutgoingMessages dispatch under MultipleHandlerBehavior.Separated).
        var payoutIssued = new SellerPayoutIssued(
            Id, SellerId, SellerPayout!.Value, FeeAmount!.Value, DateTimeOffset.UtcNow);
        session.Events.Append(Id, payoutIssued);

        return new OutgoingMessages
        {
            payoutIssued,
            new CompleteSettlement(Id),
        };
    }

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(FailSettlement.SettlementId))] FailSettlement command,
        IDocumentSession session)
    {
        // Terminal-state guard. The reserve-not-met self-send reaches this handler from
        // ReserveChecked(WasMet: false); post-MVP failure modes may dispatch from later
        // phases, so the guard rejects only the two terminal states. Wolverine inbox dedup
        // plus the MarkCompleted() saga-document removal should prevent duplicate
        // FailSettlement delivery in practice; the guard is the correctness contract.
        if (Status is SettlementStatus.Completed or SettlementStatus.Failed)
        {
            throw new InvalidSettlementTransitionException(
                Id, Status, nameof(FailSettlement));
        }

        Status = SettlementStatus.Failed;
        FailureReason = command.Reason;

        // Integration event — appended to the financial event stream for audit AND emitted
        // on the bus. The M5-S3 PendingSettlementHandler.Handle(PaymentFailed) fires from
        // local in-process dispatch and transitions the projection's status to Failed
        // per workshop 003 scenario §8.7. Cross-BC publish route (Operations consumer)
        // defers to post-M5 per M5-S5 prompt Item "Explicitly out of scope".
        var paymentFailed = new CritterBids.Contracts.Settlement.PaymentFailed(
            Id, ListingId, WinnerId, command.Reason, DateTimeOffset.UtcNow);
        session.Events.Append(Id, paymentFailed);

        MarkCompleted();

        return new OutgoingMessages { paymentFailed };
    }

    public OutgoingMessages Handle(
        [SagaIdentityFrom(nameof(CompleteSettlement.SettlementId))] CompleteSettlement message,
        IDocumentSession session)
    {
        if (Status != SettlementStatus.PayoutIssued)
        {
            throw new InvalidSettlementTransitionException(
                Id, Status, nameof(CompleteSettlement));
        }

        Status = SettlementStatus.Completed;

        // Terminal integration event — appended to the financial event stream for audit AND
        // emitted on the bus. The M5-S3 PendingSettlementHandler.Handle(SettlementCompleted)
        // fires from local in-process dispatch and transitions the projection's status to
        // Consumed. Cross-BC publish route (Listings.CatalogListingView "Settled" status)
        // wires at M5-S6.
        var completed = new SettlementCompleted(
            Id, ListingId, WinnerId, SellerId, HammerPrice,
            FeeAmount!.Value, SellerPayout!.Value, DateTimeOffset.UtcNow);
        session.Events.Append(Id, completed);

        MarkCompleted();

        return new OutgoingMessages { completed };
    }
}

/// <summary>
/// Lifecycle states for <see cref="SettlementSaga"/> per W003 Phase 1 Part 2's seven-phase
/// progression. <see cref="Failed"/> is reached via the M5-S5 reserve-not-met branch (and,
/// post-MVP, real-payment-processor failure paths). <see cref="Completed"/> is the
/// happy-path terminal state.
/// </summary>
public enum SettlementStatus
{
    Initiated,
    ReserveChecked,
    WinnerCharged,
    FeeCalculated,
    PayoutIssued,
    Completed,
    Failed,
}
