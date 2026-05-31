using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Operations;

public static class OperationsModule
{
    /// <summary>
    /// Contributes the Operations BC's document types to the primary IDocumentStore registered in
    /// Program.cs (ADR 009 / ADR 011 All-Marten pivot). The connection string is owned by
    /// Program.cs; this module only declares what Operations owns within the shared store. Per the
    /// M7 milestone §5, Operations is documents-only — it registers <b>no</b> sagas and <b>no</b>
    /// event-sourced aggregates, and makes <b>no</b> <c>AddMarten()</c> call (the host owns the
    /// single one).
    /// </summary>
    public static IServiceCollection AddOperationsModule(this IServiceCollection services)
    {
        services.ConfigureMarten(opts =>
        {
            // SettlementQueueView — Operations BC's settlement-queue read model per W006 §1
            // (M7-S2). Tolerant-upsert document maintained by SettlementQueueHandler from three
            // Settlement integration events (PaymentFailed / SettlementCompleted /
            // SellerPayoutIssued). Marten resolves the document key via the Id => SettlementId
            // expression-bodied property (no Identity override needed — mirrors PendingSettlement
            // / BidderCreditView); no UseNumericRevisions because at-least-once redelivery is
            // handled by the handler's load-mutate-store upsert shape, not optimistic concurrency.
            //
            // Documents only — no event types are registered here: Operations is a pure consumer
            // that folds inbound Wolverine messages into a document upsert (M7 milestone §5). It
            // appends to no local stream, so the Settlement event types it consumes need no
            // Marten event-graph registration (they arrive as Wolverine message envelopes, not via
            // Marten stream replay).
            opts.Schema.For<SettlementQueueView>().DatabaseSchemaName("operations");

            // LotBoardView — Operations BC's lot-board read model per W006 §2 (M7-S3). An
            // upsert document keyed on ListingId (via the Id => ListingId alias), maintained by
            // two ADR-014 Sub-Option A sibling handlers (LotBoardSellingHandler /
            // LotBoardAuctionsHandler) folding the Selling and Auctions integration-event families
            // into one row per listing. Same tolerant-upsert shape as SettlementQueueView — no
            // UseNumericRevisions; at-least-once redelivery is absorbed by the load-mutate-store
            // discipline plus the monotone Status/LastUpdatedAt guards.
            opts.Schema.For<LotBoardView>().DatabaseSchemaName("operations");

            // BidActivityEntry — Operations BC's bid-activity feed per W006 §3 (M7-S3). An
            // append/feed document keyed on BidId (via the Id => BidId alias): one immutable row
            // per accepted bid, maintained by BidActivityHandler. Indexed on ListingId (feed
            // filter/grouping) and PlacedAt (feed sort key) since queries scroll a listing's bids
            // in time order; the key remains BidId so redelivery dedupes naturally.
            opts.Schema.For<BidActivityEntry>()
                .DatabaseSchemaName("operations")
                .Index(x => x.ListingId)
                .Index(x => x.PlacedAt);

            // OperationsObligationsView — Operations BC's obligations read model per W006 §4
            // (M7-S4). An upsert document keyed on ObligationId (via the Id => ObligationId alias),
            // maintained by the single ADR-014 Sub-Option A sibling handler
            // (OperationsObligationsHandler) folding the four Obligations integration events into one
            // row per obligation. Same tolerant-upsert shape as the other Operations views — no
            // UseNumericRevisions; at-least-once redelivery and out-of-order arrival are absorbed by
            // the handler's load-mutate-store discipline plus its terminal-absorbing + strictly-older
            // ordering guard. Indexed on QueueState (the escalation/open-dispute queue filter axis)
            // and ListingId (the cross-view join key to the lot board); the key stays ObligationId so
            // redelivery dedupes naturally.
            opts.Schema.For<OperationsObligationsView>()
                .DatabaseSchemaName("operations")
                .Index(x => x.QueueState)
                .Index(x => x.ListingId);
        });

        return services;
    }
}
