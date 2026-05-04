using Marten;
using Microsoft.Extensions.DependencyInjection;

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
            // Phase 1 Part 6 (S4 territory).
            opts.Schema.For<SettlementSaga>()
                .DatabaseSchemaName("settlement")
                .Identity(x => x.Id)
                .UseNumericRevisions(true);

            // Event types register at first use (M2 silent-AggregateStreamAsync<T>-null
            // lesson — registering ahead of Apply / Handle methods can mask bugs). The
            // Settlement-internal events SettlementInitiated / ReserveCheckCompleted /
            // WinnerCharged / FinalValueFeeCalculated / SellerPayoutIssued land in S4 with
            // the saga's Handle methods; the integration-out contracts (already authored
            // at src/CritterBids.Contracts/Settlement/ in M5-S1) wire publish routes in
            // Program.cs at S6.
            //
            // Projections register at first use as well: PendingSettlement at S3,
            // BidderCreditView at S5.
        });

        return services;
    }
}
