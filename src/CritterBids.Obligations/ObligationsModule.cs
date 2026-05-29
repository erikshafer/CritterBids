using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CritterBids.Obligations;

public static class ObligationsModule
{
    public static IServiceCollection AddObligationsModule(this IServiceCollection services)
    {
        // Bind ObligationsOptions from the "Obligations" configuration section (production +
        // demo durations, DemoMode flag). Resolves W001-6; consumed by SettlementCompletedHandler
        // to compute the ship-by deadline and by the M6-S3 reminder/auto-confirm timers.
        services.AddOptions<ObligationsOptions>()
            .BindConfiguration(ObligationsOptions.SectionName);

        // Contribute the Obligations BC's document and event types to the primary IDocumentStore
        // registered in Program.cs (ADR 009 / ADR 011). The connection string is owned by
        // Program.cs; this module only declares what Obligations owns within the shared store.
        services.ConfigureMarten(opts =>
        {
            // Post-sale coordination saga document. Wolverine Saga per ADR-022. Numeric revisions
            // provide optimistic concurrency for saga writes (mirrors SettlementSaga). Identity
            // binds the saga document's primary key to the deterministic UUID v5 ObligationId
            // computed at saga-start time.
            opts.Schema.For<PostSaleCoordinationSaga>()
                .DatabaseSchemaName("obligations")
                .Identity(x => x.Id)
                .UseNumericRevisions(true);

            // ObligationEventStream — marker class for the per-obligation event stream. Required
            // by opts.Events.UseMandatoryStreamTypeDeclaration in Program.cs; sole purpose is
            // satisfying the mandatory-stream-type-declaration rule at
            // session.Events.StartStream<ObligationEventStream>(obligationId, ...).
            opts.Schema.For<ObligationEventStream>().DatabaseSchemaName("obligations");

            // ObligationStatusView — the M6-S3 single-stream read model. Lives in the obligations
            // schema alongside the saga; keyed on the ObligationId.
            opts.Schema.For<ObligationStatusView>()
                .DatabaseSchemaName("obligations")
                .Identity(x => x.Id);

            // Obligation event types — registered at first use per the M2 silent-
            // AggregateStreamAsync<T>-null lesson. PostSaleCoordinationStarted is appended at
            // saga start (M6-S2); the M6-S3 reminder/tracking/fulfillment events join here.
            // TrackingInfoProvided is a Contracts integration event that is also appended to the
            // obligation stream (it drives the projection); ObligationFulfilled is emitted on the
            // bus only and is not registered as a stream event type.
            opts.Events.AddEventType<PostSaleCoordinationStarted>();
            opts.Events.AddEventType<ShippingReminderSent>();
            opts.Events.AddEventType<CritterBids.Contracts.Obligations.TrackingInfoProvided>();
            opts.Events.AddEventType<DeliveryConfirmed>();

            // ObligationStatusView Inline projection (opsx 8.1) — strongly consistent so the view
            // is queryable immediately after the appending transaction commits.
            opts.Projections.Add<ObligationStatusViewProjection>(
                JasperFx.Events.Projections.ProjectionLifecycle.Inline);
        });

        return services;
    }
}
