using CritterBids.Contracts.Auctions;
using Marten;
using Wolverine;
using Wolverine.Attributes;

namespace CritterBids.Settlement;

/// <summary>
/// Starts the <see cref="SettlementSaga"/> when a listing closes with a winning outcome.
/// Runs as a separate static class — per the wolverine-sagas skill the Start pattern lives
/// outside the saga type so Wolverine can distinguish "create + persist" from
/// "load existing and handle".
///
/// <para><b>Idempotency at two layers.</b> First, the deterministic UUID v5 <c>SettlementId</c>
/// per W003 Phase 1 Part 6: the same <c>ListingId</c> always derives the same id, so a
/// duplicate <c>ListingSold</c> consumption resolves to the same saga document key. Second,
/// the existing-saga check below: when a saga is already at this id (re-delivery of
/// <c>ListingSold</c>), the handler returns <c>(null, empty)</c> — Wolverine skips saga
/// creation and dispatches no continuation command. Wolverine inbox dedup should prevent
/// re-delivery in practice; the existence check is the correctness guarantee if dedup fails.</para>
///
/// <para><b>Retry on PendingSettlement-not-found.</b> If the projection has not caught up
/// from <c>ListingPublished</c> (W003 Phase 1 Part 1's race condition), the handler throws
/// <see cref="PendingSettlementNotFoundException"/>. The
/// <see cref="SettlementsConcurrencyRetryPolicies"/> Wolverine extension catches the
/// exception and retries the inbound message with backoff per W003 Phase 1 Part 1
/// Option A. The triggering event stays in the queue until the handler succeeds.</para>
///
/// <para><b>Two source overloads — bidding and BIN.</b> The <see cref="ListingSold"/> overload
/// initiates at <see cref="SettlementStatus.Initiated"/> and self-sends <see cref="CheckReserve"/>
/// as the first continuation (M5-S4). The <see cref="BuyItNowPurchased"/> overload initiates
/// at <see cref="SettlementStatus.ReserveChecked"/> directly with <see cref="SettlementSaga.ReserveWasMet"/>
/// set to <c>true</c>, does NOT append <c>ReserveCheckCompleted</c> (the absence is the
/// audit signal per W003 §canonical-payload "show me all BIN settlements" query), and
/// self-sends <see cref="ChargeWinner"/> as the first continuation per W003 Phase 1 Part 5
/// (M5-S5). The same listing-id can only initiate one settlement — Auctions enforces "BIN
/// removes after first bid" per M3 lived ground, so the deterministic <c>SettlementId</c>
/// derivation collides only on re-delivery, never across sources.</para>
/// </summary>
[StickyHandler("settlement-auctions-events")]
public static class StartSettlementSagaHandler
{
    public static async Task<(SettlementSaga?, OutgoingMessages)> Handle(
        ListingSold message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Load PendingSettlement for reserve / fee / seller fields. The ListingSold payload
        // deliberately does not carry these — they are Selling-source fields cached at
        // ListingPublished time per W003 Phase 1 Part 1 / M5-S3.
        var pending = await session.LoadAsync<PendingSettlement>(
            message.ListingId, cancellationToken);

        if (pending is null)
        {
            // Wolverine retry policy in SettlementsConcurrencyRetryPolicies catches this
            // and re-queues the inbound ListingSold with backoff.
            throw new PendingSettlementNotFoundException(message.ListingId);
        }

        var sagaId = SettlementsIdentityNamespaces.SettlementId(message.ListingId);

        // Idempotent re-delivery guard: if a saga already exists at this id, the inbound
        // ListingSold has been consumed before. Skip creation and dispatch no continuation.
        var existing = await session.LoadAsync<SettlementSaga>(sagaId, cancellationToken);
        if (existing is not null)
        {
            return (null, new OutgoingMessages());
        }

        var saga = new SettlementSaga
        {
            Id = sagaId,
            ListingId = message.ListingId,
            WinnerId = message.WinnerId,
            SellerId = pending.SellerId,
            HammerPrice = message.HammerPrice,
            ReservePrice = pending.ReservePrice,
            FeePercentage = pending.FeePercentage,
            Status = SettlementStatus.Initiated,
        };

        // Append the first event to the financial event stream at sagaId. StartStream<T>
        // is required by opts.Events.UseMandatoryStreamTypeDeclaration = true; the marker
        // type FinancialEventStream exists for this purpose.
        session.Events.StartStream<FinancialEventStream>(
            sagaId,
            new SettlementInitiated(
                sagaId,
                message.ListingId,
                message.WinnerId,
                pending.SellerId,
                message.HammerPrice,
                SettlementSource.Bidding,
                pending.ReservePrice,
                pending.FeePercentage,
                DateTimeOffset.UtcNow));

        return (saga, new OutgoingMessages { new CheckReserve(sagaId) });
    }

    public static async Task<(SettlementSaga?, OutgoingMessages)> Handle(
        BuyItNowPurchased message,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        // Same PendingSettlement load as the bidding overload — the projection's seller /
        // fee / reserve fields are needed regardless of source. ReservePrice is loaded but
        // never compared on the BIN path (BIN skips reserve check by definition).
        var pending = await session.LoadAsync<PendingSettlement>(
            message.ListingId, cancellationToken);

        if (pending is null)
        {
            throw new PendingSettlementNotFoundException(message.ListingId);
        }

        var sagaId = SettlementsIdentityNamespaces.SettlementId(message.ListingId);

        // Same deterministic-id idempotency guard as the bidding overload. The same listing
        // can only settle once across sources — Auctions enforces "BIN removes after first
        // bid" per M3 lived ground — so existing-saga collisions here are always re-deliveries
        // of the same BuyItNowPurchased.
        var existing = await session.LoadAsync<SettlementSaga>(sagaId, cancellationToken);
        if (existing is not null)
        {
            return (null, new OutgoingMessages());
        }

        var saga = new SettlementSaga
        {
            Id = sagaId,
            ListingId = message.ListingId,
            WinnerId = message.BuyerId,
            SellerId = pending.SellerId,
            HammerPrice = message.Price,
            ReservePrice = pending.ReservePrice,
            FeePercentage = pending.FeePercentage,
            // BIN-source initial state per W003 Phase 1 Part 5: skip the reserve-check phase
            // entirely. The saga starts at ReserveChecked with ReserveWasMet = true; no
            // ReserveCheckCompleted event is appended (its absence is the canonical audit
            // signal for "this was a BIN settlement" per workshop 003 scenario §9.2).
            Status = SettlementStatus.ReserveChecked,
            ReserveWasMet = true,
        };

        // Same financial-event-stream initiation as the bidding overload, but with
        // Source: BuyItNow and the BIN-side Price field.
        session.Events.StartStream<FinancialEventStream>(
            sagaId,
            new SettlementInitiated(
                sagaId,
                message.ListingId,
                message.BuyerId,
                pending.SellerId,
                message.Price,
                SettlementSource.BuyItNow,
                pending.ReservePrice,
                pending.FeePercentage,
                DateTimeOffset.UtcNow));

        // First self-send is ChargeWinner (not CheckReserve) — the BIN path bypasses the
        // reserve check entirely. The saga's existing Handle(ChargeWinner) state guard
        // permits dispatch from ReserveChecked, so no saga-side changes are required.
        return (saga, new OutgoingMessages { new ChargeWinner(sagaId) });
    }
}
