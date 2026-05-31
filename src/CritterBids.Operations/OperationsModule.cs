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
        });

        return services;
    }
}
