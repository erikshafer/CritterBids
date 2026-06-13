using CritterBids.Contracts.Settlement;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace CritterBids.Settlement;

public static class SettlementModule
{
    public static IServiceCollection AddSettlementModule(this IServiceCollection services)
    {
        // Contribute Settlement BC's document types to the primary IDocumentStore registered
        // in Program.cs (see ADR 009). The connection string is owned by Program.cs;
        // this module only declares what Settlement owns within the shared store.
        services.ConfigureMarten(opts =>
        {
            // Settlement workflow document. Wolverine Saga per ADR-019. Numeric revisions
            // provide optimistic concurrency for saga writes (mirrors AuctionClosingSaga
            // from M3-S5). Identity binds the saga document's primary key to the
            // deterministic UUID v5 SettlementId computed at saga-start time per W003
            // Phase 1 Part 6 (M5-S4).
            opts.Schema.For<SettlementSaga>()
                .DatabaseSchemaName("settlement")
                .Identity(x => x.Id)
                .UseNumericRevisions(true);

            // PendingSettlement — Settlement BC's local cache of listing data needed at
            // saga-start time per W003 Phase 1 Part 1. Tolerant-upsert document maintained
            // by PendingSettlementHandler from five cross-BC integration events. Marten
            // resolves the document key via the Id property (no Identity override needed);
            // no UseNumericRevisions because at-least-once redelivery is handled by the
            // handler's load-mutate-store upsert shape, not optimistic concurrency.
            opts.Schema.For<PendingSettlement>().DatabaseSchemaName("settlement");

            // FinancialEventStream — marker class for the per-settlement audit stream per
            // W003 §"Financial Event Stream". Required by opts.Events.UseMandatoryStreamTypeDeclaration
            // in Program.cs; sole purpose is satisfying the mandatory-stream-type-declaration
            // rule at session.Events.StartStream<FinancialEventStream>(sagaId, ...).
            opts.Schema.For<FinancialEventStream>().DatabaseSchemaName("settlement");

            // SellerSettlementSummary — Settlement BC's per-listing seller settlement
            // outcome (M9-S3). Tolerant-upsert document maintained by
            // SellerSettlementSummaryHandler from SettlementCompleted. Marten
            // resolves the document key via the Id property (ListingId); no
            // UseNumericRevisions because idempotency is handled by the handler's
            // deterministic-key upsert shape.
            opts.Schema.For<SellerSettlementSummary>().DatabaseSchemaName("settlement");

            // BidderCreditView — Settlement BC's per-bidder credit projection per W003
            // Phase 1 Part 7 (M5-S5). Tolerant-upsert document maintained by
            // BidderCreditViewHandler from two events: ParticipantSessionStarted
            // (seed at CreditCeiling) and WinnerCharged (debit by Amount). Marten
            // resolves the document key via the Id => BidderId expression-bodied
            // property; no UseNumericRevisions because idempotency lives in the
            // handler's LastChargedSettlementId equality check, not optimistic
            // concurrency.
            opts.Schema.For<BidderCreditView>().DatabaseSchemaName("settlement");

            // Settlement event types — registered at first use per the M2 silent-
            // AggregateStreamAsync<T>-null lesson. Four stream-internal events authored
            // at M5-S4 plus the three integration contracts authored at M5-S1 (the
            // contracts are appended to the financial event stream AND emitted on the
            // bus per §9.1's six-event stream listing — Marten requires each event type
            // registered for stream replay / forwarding to resolve the typed payload).
            // PaymentFailed is registered now even though it doesn't emit until M5-S5;
            // this saves the M5-S5 module edit.
            opts.Events.AddEventType<SettlementInitiated>();
            opts.Events.AddEventType<ReserveCheckCompleted>();
            opts.Events.AddEventType<WinnerCharged>();
            opts.Events.AddEventType<FinalValueFeeCalculated>();
            opts.Events.AddEventType<SellerPayoutIssued>();
            opts.Events.AddEventType<SettlementCompleted>();
            opts.Events.AddEventType<PaymentFailed>();
        });

        // Wolverine retry policies. PendingSettlementNotFoundException is the M5-S4
        // retry-on-projection-lag posture per W003 Phase 1 Part 1 Option A.
        services.AddSingleton<IWolverineExtension, SettlementsConcurrencyRetryPolicies>();

        return services;
    }
}
