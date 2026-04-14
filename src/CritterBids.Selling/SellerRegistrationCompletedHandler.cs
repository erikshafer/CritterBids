using CritterBids.Contracts;
using Wolverine.Marten;

namespace CritterBids.Selling;

/// <summary>
/// Wolverine handler for <see cref="SellerRegistrationCompleted"/>.
/// Upserts a <see cref="RegisteredSeller"/> document into the Selling BC's Marten store
/// when a seller completes registration in the Participants BC.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[MartenStore]</c> attribute routes the handler's durable inbox to the Selling BC's
/// named PostgreSQL schema ("selling"), ensuring transactional atomicity of inbox reads and
/// document writes when using Wolverine's durable messaging.
/// </para>
/// <para>
/// CritterBids uses named Marten stores only (ADR 0002) — no default <c>IDocumentStore</c> is
/// registered. Wolverine's <c>IDocumentSession</c> code-gen variable source (<c>SessionVariableSource</c>)
/// is only activated by <c>AddMarten().IntegrateWithWolverine()</c>, which is never called here.
/// Therefore <c>IDocumentSession</c> cannot be injected as a handler parameter. Instead,
/// <c>ISellingDocumentStore</c> is injected directly from the DI container and a lightweight
/// session is opened and committed explicitly within the handler.
/// </para>
/// <para>
/// Idempotency: <c>session.Store()</c> is a Marten upsert. Processing the same
/// <see cref="SellerRegistrationCompleted"/> message twice produces the same document state — no
/// error, no duplicate row. This satisfies Wolverine's at-least-once delivery guarantee.
/// </para>
/// </remarks>
[MartenStore(typeof(ISellingDocumentStore))]
public static class SellerRegistrationCompletedHandler
{
    public static async Task Handle(
        SellerRegistrationCompleted message,
        ISellingDocumentStore store)
    {
        await using var session = store.LightweightSession();
        session.Store(new RegisteredSeller { Id = message.ParticipantId });
        await session.SaveChangesAsync();
    }
}
